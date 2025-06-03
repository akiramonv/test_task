using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using System;
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
        public async Task<IActionResult> Upload()
        {
            // Загружаем список компаний для выпадающего списка
            var companies = await _context.Companies.ToListAsync();
            ViewBag.Companies = companies;
            return View();
        }

        // POST: /ExcelUpload/Upload
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(
            IFormFile excelFile,
            long companyId,
            long? fileTypeId,
            string newFileTypeName)
        {
            // Валидация файла
            if (excelFile == null || excelFile.Length == 0)
            {
                ModelState.AddModelError("excelFile", "Пожалуйста, выберите файл Excel (.xlsx).");
                return await ReloadUploadView(companyId);
            }

            // Проверка расширения файла
            var ext = Path.GetExtension(excelFile.FileName);
            if (!ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError("excelFile", "Нужно загрузить файл в формате .xlsx.");
                return await ReloadUploadView(companyId);
            }

            // Проверка: выбран ли тип файла или введен новый
            if (!fileTypeId.HasValue && string.IsNullOrWhiteSpace(newFileTypeName))
            {
                ModelState.AddModelError("", "Выберите тип файла или введите новый.");
                return await ReloadUploadView(companyId);
            }

            FileType fileType;

            // Если выбран существующий тип
            if (fileTypeId.HasValue)
            {
                fileType = await _context.FileTypes
                    .FirstOrDefaultAsync(ft => ft.Id == fileTypeId && ft.CompanyId == companyId);

                // Проверяем, что тип принадлежит компании
                if (fileType == null)
                {
                    ModelState.AddModelError("fileTypeId", "Выбранный тип файла не принадлежит указанной компании.");
                    return await ReloadUploadView(companyId);
                }
            }
            else // Создаем новый тип
            {
                fileType = new FileType
                {
                    Name = newFileTypeName,
                    CompanyId = companyId
                };
                _context.FileTypes.Add(fileType);
                await _context.SaveChangesAsync(); // Сохраняем, чтобы получить Id
            }

            // Удаление старых данных, связанных с этим типом файла
            var existingFileDatas = await _context.FileDatas
                .Where(fd => fd.FileTypeId == fileType.Id)
                .Include(fd => fd.ColumnData) // Включаем связанные данные
                .ToListAsync();

            if (existingFileDatas.Any())
            {
                // Удаляем связанные ColumnData
                var columnDataToDelete = existingFileDatas
                    .Where(fd => fd.ColumnData != null)
                    .Select(fd => fd.ColumnData)
                    .ToList();

                if (columnDataToDelete.Any())
                {
                    _context.ColumnDatas.RemoveRange(columnDataToDelete);
                }

                // Удаляем FileData
                _context.FileDatas.RemoveRange(existingFileDatas);
                await _context.SaveChangesAsync();
            }

            // Обработка Excel файла
            ExcelPackage.License.SetNonCommercialPersonal("Амир");

            List<ColumnName> columnNames = new List<ColumnName>();
            List<ColumnData> columnDataList = new List<ColumnData>();
            Dictionary<int, ColumnName> colIndexToNameEntity = new Dictionary<int, ColumnName>();

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
                        return await ReloadUploadView(companyId);
                    }

                    if (worksheet.Dimension == null)
                    {
                        ModelState.AddModelError("", "Лист Excel пуст — нет ни одной заполненной ячейки.");
                        return await ReloadUploadView(companyId);
                    }

                    int totalCols = worksheet.Dimension.End.Column;
                    int totalRows = worksheet.Dimension.End.Row;

                    // Поиск строки заголовков
                    int headerRow = 0;
                    for (int row = 1; row <= totalRows; row++)
                    {
                        bool anyNonEmpty = false;
                        for (int col = 1; col <= totalCols; col++)
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
                        }
                    }

                    if (headerRow == 0)
                    {
                        ModelState.AddModelError("", "Не удалось найти ни одной непустой строки в листе.");
                        return await ReloadUploadView(companyId);
                    }

                    // Сбор заголовков
                    for (int col = 1; col <= totalCols; col++)
                    {
                        string headerText = worksheet.Cells[headerRow, col].Text?.Trim();
                        if (string.IsNullOrWhiteSpace(headerText))
                            continue;

                        columnNames.Add(new ColumnName { Name = headerText });
                    }

                    // Сохраняем заголовки
                    await _context.ColumnNames.AddRangeAsync(columnNames);
                    await _context.SaveChangesAsync();

                    // Сопоставление индекса столбца с сущностью ColumnName
                    int idx = 0;
                    for (int col = 1; col <= totalCols; col++)
                    {
                        string headerText = worksheet.Cells[headerRow, col].Text?.Trim();
                        if (string.IsNullOrWhiteSpace(headerText))
                            continue;

                        colIndexToNameEntity[col] = columnNames[idx];
                        idx++;
                    }

                    // Сбор данных
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

                    // Сохраняем данные
                    if (columnDataList.Any())
                    {
                        await _context.ColumnDatas.AddRangeAsync(columnDataList);
                        await _context.SaveChangesAsync();
                    }
                }
            }

            // Создаем связи в file_data
            foreach (var data in columnDataList)
            {
                _context.FileDatas.Add(new FileData
                {
                    FileTypeId = fileType.Id,
                    DataId = data.Id
                });
            }
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(DisplayTable));
        }

        // Вспомогательный метод для перезагрузки представления с данными
        private async Task<IActionResult> ReloadUploadView(long? companyId = null)
        {
            ViewBag.Companies = await _context.Companies.ToListAsync();

            if (companyId.HasValue)
            {
                ViewBag.FileTypes = await _context.FileTypes
                    .Where(ft => ft.CompanyId == companyId.Value)
                    .ToListAsync();
            }

            return View("Upload");
        }

        // GET: /ExcelUpload/GetFileTypes
        [HttpGet]
        public async Task<IActionResult> GetFileTypes(long companyId)
        {
            var fileTypes = await _context.FileTypes
                .Where(ft => ft.CompanyId == companyId)
                .Select(ft => new { id = ft.Id, name = ft.Name })
                .ToListAsync();

            return Json(fileTypes);
        }

        // GET: /ExcelUpload/DisplayTable
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

            // 3) Определяем максимальное число строк среди всех списков значений
            int maxRows = dataByColumn.Any() ? dataByColumn.Max(lst => lst.Count) : 0;

            // 4) Строим двумерный список tableRows
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

            // Формируем ViewModel
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