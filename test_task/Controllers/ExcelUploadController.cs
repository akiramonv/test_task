using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
        public IActionResult Upload()
        {
            return View();
        }

        // POST: /ExcelUpload/Upload
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(IFormFile excelFile)
        {
            if (excelFile == null || excelFile.Length == 0)
            {
                ModelState.AddModelError("", "Пожалуйста, выберите файл Excel (.xlsx).");
                return View();
            }

            var ext = Path.GetExtension(excelFile.FileName);
            if (!ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError("", "Нужно загрузить файл в формате .xlsx.");
                return View();
            }

            ExcelPackage.License.SetNonCommercialPersonal("Амир Акинов");

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

                    // Определяем строку с заголовками (искать в первых трёх строках)
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

                    // Считываем названия колонок и сохраняем в таблицу column_name
                    var columnNames = new List<ColumnName>();
                    for (int col = 1; col <= totalCols; col++)
                    {
                        string headerText = worksheet.Cells[headerRow, col].Text?.Trim();
                        if (string.IsNullOrWhiteSpace(headerText))
                            continue;

                        columnNames.Add(new ColumnName { Name = headerText });
                    }

                    await _context.ColumnNames.AddRangeAsync(columnNames);
                    await _context.SaveChangesAsync();

                    // Словарь: номер столбца в Excel → объект ColumnName (с заполненным Id)
                    var colIndexToNameEntity = new Dictionary<int, ColumnName>();
                    int idx = 0;
                    for (int col = 1; col <= totalCols; col++)
                    {
                        string headerText = worksheet.Cells[headerRow, col].Text?.Trim();
                        if (string.IsNullOrWhiteSpace(headerText))
                            continue;

                        colIndexToNameEntity[col] = columnNames[idx++];
                    }

                    // Считываем все данные (после headerRow) и сохраняем в column_data
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

            return RedirectToAction(nameof(DisplayTable));
        }

        // GET: /ExcelUpload/DisplayTable
        public IActionResult DisplayTable()
        {
            // Получаем все колонки (column_name) в порядке их Id
            var allColumns = _context.ColumnNames
                                     .OrderBy(cn => cn.Id)
                                     .ToList();

            // Для каждой колонки собираем список её значений (column_data), упорядоченных по Id
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

            // Определяем максимальное количество строк среди всех столбцов
            int maxRows = dataByColumn.Any() ? dataByColumn.Max(lst => lst.Count) : 0;

            // Строим матрицу tableRows: каждая строка содержит по одному элементу из каждой колонки или пустую строку
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
