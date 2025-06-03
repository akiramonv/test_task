using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using test_task.Data;
using test_task.Models;

namespace test_task.Controllers
{
    public class ExcelUploadController : Controller
    {
        private readonly ApplicationDbContext _context;

        // Конструктор принимает через DI контекст данных для работы с БД
        public ExcelUploadController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /ExcelUpload/Upload
        // Отображает форму загрузки Excel-файла
        [HttpGet]
        public IActionResult Upload()
        {
            return View();
        }

        // POST: /ExcelUpload/Upload
        // Обрабатывает загрузку Excel-файла, парсит его и сохраняет содержимое в БД
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(IFormFile excelFile)
        {
            // 1) Если файл не выбран или пустой — вернём ошибку на форму
            if (excelFile == null || excelFile.Length == 0)
            {
                ModelState.AddModelError("", "Пожалуйста, выберите файл Excel (.xlsx).");
                return View();
            }

            // 2) Проверяем расширение файла — должен быть именно .xlsx
            var ext = Path.GetExtension(excelFile.FileName);
            if (!ext.Equals(".xlsx", System.StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError("", "Нужно загрузить файл в формате .xlsx.");
                return View();
            }

            // 3) Перед новым импортом очищаем все текущие записи из таблиц column_data и column_name
            // Сначала удаляем «данные ячеек» (column_data)
            var allData = _context.ColumnDatas.ToList();
            if (allData.Any())
            {
                _context.ColumnDatas.RemoveRange(allData);
                await _context.SaveChangesAsync();
            }

            // Затем удаляем «имена столбцов» (column_name)
            var allNames = _context.ColumnNames.ToList();
            if (allNames.Any())
            {
                _context.ColumnNames.RemoveRange(allNames);
                await _context.SaveChangesAsync();
            }

            // 4) Устанавливаем лицензию для EPPlus (библиотека для работы с Excel)
            ExcelPackage.License.SetNonCommercialPersonal("Амир");

            // 5) Читаем содержимое Excel-файла в память
            using (var stream = new MemoryStream())
            {
                await excelFile.CopyToAsync(stream);
                stream.Position = 0;

                using (var package = new ExcelPackage(stream))
                {
                    // Берём первый лист из книги
                    var worksheet = package.Workbook.Worksheets.FirstOrDefault();
                    if (worksheet == null)
                    {
                        // Если листов нет — возвращаем ошибку
                        ModelState.AddModelError("", "В файле нет рабочих листов.");
                        return View();
                    }

                    // Определяем число колонок и строк на листе
                    int totalCols = worksheet.Dimension.End.Column;
                    int totalRows = worksheet.Dimension.End.Row; // для поиска заголовочной строки по всему листу

                    // 6) Ищем первую непустую строку на всём листе — она будет считаться строкой заголовков
                    int headerRow = 0;
                    for (int row = 1; row <= totalRows; row++)
                    {
                        bool anyNonEmpty = false;

                        // Проверяем каждую ячейку в текущей строке на непустоту
                        for (int col = 1; col <= totalCols; col++)
                        {
                            if (!string.IsNullOrWhiteSpace(worksheet.Cells[row, col].Text))
                            {
                                anyNonEmpty = true;
                                break;
                            }
                        }

                        // Если нашли непустую строку — сохраняем её номер и выходим из цикла
                        if (anyNonEmpty)
                        {
                            headerRow = row;
                            break;
                        }
                    }

                    // Если ни одной непустой строки не обнаружено — возвращаем ошибку
                    if (headerRow == 0)
                    {
                        ModelState.AddModelError("", "Не удалось найти ни одной непустой строки в листе.");
                        return View();
                    }

                    // 7) Считываем заголовки из найденной headerRow: каждую непустую ячейку превращаем в ColumnName
                    var columnNames = new List<ColumnName>();
                    for (int col = 1; col <= totalCols; col++)
                    {
                        string headerText = worksheet.Cells[headerRow, col].Text?.Trim();
                        if (string.IsNullOrWhiteSpace(headerText))
                            continue;

                        columnNames.Add(new ColumnName { Name = headerText });
                    }

                    // Сохраняем все найденные названия столбцов в БД, чтобы у каждого появился свой Id
                    await _context.ColumnNames.AddRangeAsync(columnNames);
                    await _context.SaveChangesAsync();

                    // 8) Строим словарь: ключ — номер колонки (1-based), значение — объект ColumnName с заполненным Id
                    var colIndexToNameEntity = new Dictionary<int, ColumnName>();
                    int idx = 0;
                    for (int col = 1; col <= totalCols; col++)
                    {
                        string headerText = worksheet.Cells[headerRow, col].Text?.Trim();
                        if (string.IsNullOrWhiteSpace(headerText))
                            continue;

                        // Позиция idx соответствует порядку добавления в columnNames
                        colIndexToNameEntity[col] = columnNames[idx++];
                    }

                    // 9) Считываем все ячейки ниже headerRow, относящиеся к «учтённым» столбцам, и формируем список ColumnData
                    var columnDataList = new List<ColumnData>();
                    for (int row = headerRow + 1; row <= totalRows; row++)
                    {
                        for (int col = 1; col <= totalCols; col++)
                        {
                            // Пропускаем колонки, которых нет в словаре (то есть с пустым заголовком)
                            if (!colIndexToNameEntity.ContainsKey(col))
                                continue;

                            var cellText = worksheet.Cells[row, col].Text;
                            if (string.IsNullOrWhiteSpace(cellText))
                                continue;

                            // Добавляем новую запись ColumnData с текстом ячейки и внешним ключом на ColumnName
                            columnDataList.Add(new ColumnData
                            {
                                DataText = cellText,
                                ColumnNameId = colIndexToNameEntity[col].Id
                            });
                        }
                    }

                    // Если есть данные для сохранения, добавляем их в БД
                    if (columnDataList.Any())
                    {
                        await _context.ColumnDatas.AddRangeAsync(columnDataList);
                        await _context.SaveChangesAsync();
                    }
                }
            }

            // 10) После успешного импорта перенаправляем пользователя на страницу DisplayTable
            return RedirectToAction(nameof(DisplayTable));
        }

        // GET: /ExcelUpload/DisplayTable
        // Отображает сохранённые в БД данные в виде HTML-таблицы
        public IActionResult DisplayTable()
        {
            // 1) Получаем из БД все названия столбцов (column_name), отсортированные по Id
            var allColumns = _context.ColumnNames
                                     .OrderBy(cn => cn.Id)
                                     .ToList();

            // 2) Для каждого ColumnName собираем список значений DataText из table column_data
            var dataByColumn = new List<List<string>>();
            foreach (var col in allColumns)
            {
                var values = _context.ColumnDatas
                                     .Where(cd => cd.ColumnNameId == col.Id)
                                     .OrderBy(cd => cd.Id)
                                     .Select(cd => cd.DataText)
                                     .ToList();
                dataByColumn.Add(values);
            }

            // 3) Определяем максимальное число строк среди всех списков значений,
            // чтобы выровнять таблицу по строкам
            int maxRows = dataByColumn.Any() ? dataByColumn.Max(lst => lst.Count) : 0;

            // 4) Строим двумерный список tableRows, где каждая строка состоит из
            // одного элемента из каждого столбца (или пустой строки, если данных нет)
            var tableRows = new List<List<string>>();
            for (int i = 0; i < maxRows; i++)
            {
                var rowValues = new List<string>();
                for (int colIdx = 0; colIdx < allColumns.Count; colIdx++)
                {
                    var columnList = dataByColumn[colIdx];
                    // Если в этом столбце меньше элементов, чем i, добавляем пустую ячейку
                    rowValues.Add(i < columnList.Count ? columnList[i] : string.Empty);
                }
                tableRows.Add(rowValues);
            }

            // Формируем ViewModel для передачи в представление
            var vm = new DisplayTableViewModel
            {
                ColumnNames = allColumns.Select(cn => cn.Name).ToList(),
                TableRows = tableRows
            };

            return View(vm);
        }
    }

    // Модель представления для страницы DisplayTable
    public class DisplayTableViewModel
    {
        // Список заголовков столбцов
        public List<string> ColumnNames { get; set; }

        // Двумерный список строк: каждая внутренняя List<string> соответствует одной строке HTML-таблицы
        public List<List<string>> TableRows { get; set; }
    }
}
