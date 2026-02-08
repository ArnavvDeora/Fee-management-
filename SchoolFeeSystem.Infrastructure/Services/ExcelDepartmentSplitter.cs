using ClosedXML.Excel;
using System.Data;
using System.IO;

namespace SchoolFeeSystem.Presentation.Services
{
    public class ExcelDepartmentSplitter
    {
        public void SplitByDepartment(string filePath, string outputFolder)
        {
            using var workbook = new XLWorkbook(filePath);
            var ws = workbook.Worksheet(1);

            var table = new DataTable();

            int headerRow = 6;

            foreach (var cell in ws.Row(headerRow).CellsUsed())
                table.Columns.Add(cell.GetString());

            foreach (var row in ws.RowsUsed().Skip(headerRow))
            {
                var values = new object[table.Columns.Count];
                for (int i = 0; i < table.Columns.Count; i++)
                    values[i] = row.Cell(i + 1).GetString();

                table.Rows.Add(values);
            }

            var deptName = ws.Cell(3, 1).GetString();

            var newBook = new XLWorkbook();
            var newSheet = newBook.AddWorksheet("Data");

            for (int c = 0; c < table.Columns.Count; c++)
                newSheet.Cell(1, c + 1).Value = table.Columns[c].ColumnName;

            int r = 2;
            foreach (DataRow dr in table.Rows)
            {
                for (int c = 0; c < table.Columns.Count; c++)
                    newSheet.Cell(r, c + 1).SetValue(dr[c]?.ToString());

                r++;
            }

            var safeName = deptName.Replace(" ", "_").Replace("-", "_");
            newBook.SaveAs(Path.Combine(outputFolder, $"{safeName}.xlsx"));
        }
    }
}
