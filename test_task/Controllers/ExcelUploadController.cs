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

        public ExcelUploadController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /ExcelUpload/Upload
        [HttpGet]
        public IActionResult Upload()
        {
            return View();
        }

        // POST: /ExcelUpload/Upload
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(IFormFile excelFile)
        {
            // 1) Если файл не выбран — вернём ошибку
            if (excelFile == null || excelFile.Length == 0)
            {
                ModelState.AddModelError("", "Пожалуйста, выберите файл Excel (.xlsx).");
                return View();
            }

            // 2) Проверим расширение
            var ext = Path.GetExtension(excelFile.FileName);
            if (!ext.Equals(".xlsx", System.StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError("", "Нужно загрузить файл в формате .xlsx.");
                return View();
            }

            // 3) Перед новым импортом — очищаем обе таблицы в БД (column_data → column_name)
            // Сначала удаляем данные, потом сами колонки. SaveChanges после обеих операций.
            var allData = _context.ColumnDatas.ToList();
            if (allData.Any())
            {
                _context.ColumnDatas.RemoveRange(allData);
                await _context.SaveChangesAsync();
            }

            var allNames = _context.ColumnNames.ToList();
            if (allNames.Any())
            {
                _context.ColumnNames.RemoveRange(allNames);
                await _context.SaveChangesAsync();
            }

            ExcelPackage.License.SetNonCommercialPersonal("Амир");

            // 5) Читаем Excel
            using (var stream = new MemoryStream())
            {
                await excelFile.CopyToAsync(stream);
                stream.Position = 0;

                using (var package = new ExcelPackage(stream))
                {
                    var worksheet = package.Workbook.Worksheets.FirstOrDefault();
                    if (worksheet == null)
                    {
                        ModelState.AddModelError("", "В файле нет рабочих листов.");
                        return View();
                    }

                    // 6) Определяем, какая из строк 1–3 содержит хоть одну непустую ячейку (это headerRow)
                    int headerRow = 0;
                    for (int row = 1; row <= 3; row++)
                    {
                        bool anyNonEmpty = false;
                        for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
                        {
                            if (!string.IsNullOrWhiteSpace(worksheet.Cells[row, col].Text))
                            {
                                anyNonEmpty = true;
                                break;
                            }
                        }
                        if (anyNonEmpty)
                        {
                            headerRow = row;
                            break;
                        }
                    }

                    if (headerRow == 0)
                    {
                        ModelState.AddModelError("", "Не удалось найти строку с заголовками в первых трёх строках.");
                        return View();
                    }

                    int totalCols = worksheet.Dimension.End.Column;
                    int totalRows = worksheet.Dimension.End.Row;

                    // 7) Читаем заголовки (каждый непустой столбец в строке headerRow)
                    var columnNames = new List<ColumnName>();
                    for (int col = 1; col <= totalCols; col++)
                    {
                        string headerText = worksheet.Cells[headerRow, col].Text?.Trim();
                        if (string.IsNullOrWhiteSpace(headerText))
                            continue;

                        columnNames.Add(new ColumnName { Name = headerText });
                    }

                    // Сохраняем заголовки в БД (table: column_name)
                    await _context.ColumnNames.AddRangeAsync(columnNames);
                    await _context.SaveChangesAsync();

                    // 8) Делаем словарь: номер столбца в Excel → объект ColumnName (с уже проставленным Id)
                    var colIndexToNameEntity = new Dictionary<int, ColumnName>();
                    int idx = 0;
                    for (int col = 1; col <= totalCols; col++)
                    {
                        string headerText = worksheet.Cells[headerRow, col].Text?.Trim();
                        if (string.IsNullOrWhiteSpace(headerText))
                            continue;

                        colIndexToNameEntity[col] = columnNames[idx++];
                    }

                    // 9) Читаем все ячейки ниже headerRow, относящиеся к «учтённым» столбцам, и сохраняем в column_data
                    var columnDataList = new List<ColumnData>();
                    for (int row = headerRow + 1; row <= totalRows; row++)
                    {
                        for (int col = 1; col <= totalCols; col++)
                        {
                            if (!colIndexToNameEntity.ContainsKey(col))
                                continue;

                            var cellText = worksheet.Cells[row, col].Text;
                            if (string.IsNullOrWhiteSpace(cellText))
                                continue;

                            columnDataList.Add(new ColumnData
                            {
                                DataText = cellText,
                                ColumnNameId = colIndexToNameEntity[col].Id
                            });
                        }
                    }

                    if (columnDataList.Any())
                    {
                        await _context.ColumnDatas.AddRangeAsync(columnDataList);
                        await _context.SaveChangesAsync();
                    }
                }
            }

            // 10) После успешного импорта — перенаправляем на страничку DisplayTable
            return RedirectToAction(nameof(DisplayTable));
        }

        // GET: /ExcelUpload/DisplayTable
        public IActionResult DisplayTable()
        {
            // 1) Забираем все колонки (column_name) в порядке Id
            var allColumns = _context.ColumnNames
                                     .OrderBy(cn => cn.Id)
                                     .ToList();

            // 2) Для каждой колонки формируем список её значений (column_data), упорядоченных по Id
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

            // 3) Определяем максимальное число строк среди всех столбцов
            int maxRows = dataByColumn.Any() ? dataByColumn.Max(lst => lst.Count) : 0;

            // 4) Строим двумерный список tableRows: в каждой строке по одной ячейке из каждого столбца
            var tableRows = new List<List<string>>();
            for (int i = 0; i < maxRows; i++)
            {
                var rowValues = new List<string>();
                for (int colIdx = 0; colIdx < allColumns.Count; colIdx++)
                {
                    var columnList = dataByColumn[colIdx];
                    rowValues.Add(i < columnList.Count ? columnList[i] : string.Empty);
                }
                tableRows.Add(rowValues);
            }

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
        public List<string> ColumnNames { get; set; }
        public List<List<string>> TableRows { get; set; }
    }
}
