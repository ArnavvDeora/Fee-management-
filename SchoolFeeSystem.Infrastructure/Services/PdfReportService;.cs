using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Data;
using System.Linq;
using System.Collections.Generic;

namespace SchoolFeeSystem.Presentation.Services
{
    public class PdfReportService
    {
        private readonly CsvDataService _csvService;

        public PdfReportService(CsvDataService csvService)
        {
            _csvService = csvService;
        }

        /// <summary>
        /// Generates a detailed student fee report with payment history
        /// </summary>
        public void GenerateStudentReport(DataRow row, string filePath, CsvDataService.SheetMetadata metadata = null)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            // Extract student ID for payment history lookup
            string studentId = GetValueFromRow(row, "Student ID", "Roll No", "ID");

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(30);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                    // ============ HEADER ============
                    page.Header().Column(column =>
                    {
                        column.Spacing(5);

                        if (metadata != null && !string.IsNullOrWhiteSpace(metadata.InstituteName))
                        {
                            column.Item().AlignCenter().Text(metadata.InstituteName)
                                .FontSize(16)
                                .Bold()
                                .FontColor(Colors.Blue.Darken2);
                        }
                        else
                        {
                            column.Item().AlignCenter().Text("Student Fee Report")
                                .FontSize(16)
                                .Bold()
                                .FontColor(Colors.Blue.Darken2);
                        }

                        if (metadata != null && !string.IsNullOrWhiteSpace(metadata.Period))
                        {
                            column.Item().AlignCenter().Text(metadata.Period)
                                .FontSize(11)
                                .Italic();
                        }

                        if (metadata != null && !string.IsNullOrWhiteSpace(metadata.CourseInfo))
                        {
                            column.Item().AlignCenter().Text(metadata.CourseInfo)
                                .FontSize(10)
                                .FontColor(Colors.Grey.Darken1);
                        }

                        column.Item().PaddingTop(10).LineHorizontal(2).LineColor(Colors.Blue.Darken2);
                    });

                    // ============ CONTENT ============
                    page.Content().PaddingTop(20).Column(column =>
                    {
                        column.Spacing(15);

                        // Student Name (highlighted)
                        var nameCol = row.Table.Columns.Cast<DataColumn>()
                            .FirstOrDefault(c => c.ColumnName.ToLower().Contains("name"));

                        if (nameCol != null)
                        {
                            column.Item().Background(Colors.Blue.Lighten4).Padding(10).Text(text =>
                            {
                                text.Span("Student: ").Bold().FontSize(12);
                                text.Span(row[nameCol]?.ToString() ?? "N/A").FontSize(12).FontColor(Colors.Blue.Darken2);
                            });
                        }

                        // Main Details Table
                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(3);
                            });

                            table.Cell().Element(HeaderCellStyle).Text("Field").Bold();
                            table.Cell().Element(HeaderCellStyle).Text("Value").Bold();

                            bool alternateRow = false;
                            foreach (DataColumn col in row.Table.Columns)
                            {
                                // Skip Payment History column (will show separately)
                                if (col.ColumnName.ToLower().Contains("payment history"))
                                    continue;

                                var cellValue = row[col]?.ToString() ?? "";

                                if (string.IsNullOrWhiteSpace(cellValue) && string.IsNullOrWhiteSpace(col.ColumnName))
                                    continue;

                                if (alternateRow)
                                {
                                    table.Cell().Element(DataCellStyleAlt).Text(col.ColumnName).Bold();
                                    table.Cell().Element(DataCellStyleAlt).Text(FormatValue(cellValue, col.ColumnName));
                                }
                                else
                                {
                                    table.Cell().Element(DataCellStyle).Text(col.ColumnName).Bold();
                                    table.Cell().Element(DataCellStyle).Text(FormatValue(cellValue, col.ColumnName));
                                }

                                alternateRow = !alternateRow;
                            }
                        });

                        // ✅ NEW: Payment History Section
                        var paymentHistory = _csvService.GetPaymentHistory(studentId);
                        if (paymentHistory != null && paymentHistory.Rows.Count > 0)
                        {
                            column.Item().PaddingTop(15).Column(historyColumn =>
                            {
                                historyColumn.Item().Background(Colors.Green.Lighten4)
                                    .Padding(10)
                                    .Text("💳 Payment History")
                                    .FontSize(13)
                                    .Bold()
                                    .FontColor(Colors.Green.Darken3);

                                historyColumn.Item().PaddingTop(10).Table(historyTable =>
                                {
                                    historyTable.ColumnsDefinition(columns =>
                                    {
                                        columns.RelativeColumn(2);  // Date
                                        columns.RelativeColumn(2);  // Amount
                                        columns.RelativeColumn(2);  // Type
                                    });

                                    // Headers
                                    historyTable.Cell().Element(HeaderCellStyle).Text("Date").Bold();
                                    historyTable.Cell().Element(HeaderCellStyle).Text("Amount Paid").Bold();
                                    historyTable.Cell().Element(HeaderCellStyle).Text("Payment Type").Bold();

                                    // Payment entries
                                    bool altRow = false;
                                    foreach (DataRow paymentRow in paymentHistory.Rows)
                                    {
                                        DateTime paymentDate = Convert.ToDateTime(paymentRow["Payment Date"]);
                                        decimal amount = Convert.ToDecimal(paymentRow["Amount"]);
                                        string paymentType = paymentRow["Payment Type"]?.ToString() ?? "";

                                        if (altRow)
                                        {
                                            historyTable.Cell().Element(DataCellStyleAlt).Text(paymentDate.ToString("dd-MM-yyyy"));
                                            historyTable.Cell().Element(DataCellStyleAlt).Text($"₹{amount:N2}").FontColor(Colors.Green.Darken2).Bold();
                                            historyTable.Cell().Element(DataCellStyleAlt).Text(paymentType);
                                        }
                                        else
                                        {
                                            historyTable.Cell().Element(DataCellStyle).Text(paymentDate.ToString("dd-MM-yyyy"));
                                            historyTable.Cell().Element(DataCellStyle).Text($"₹{amount:N2}").FontColor(Colors.Green.Darken2).Bold();
                                            historyTable.Cell().Element(DataCellStyle).Text(paymentType);
                                        }
                                        altRow = !altRow;
                                    }
                                });

                                // Total paid summary
                                var totalPaid = paymentHistory.AsEnumerable()
                                    .Sum(r => Convert.ToDecimal(r["Amount"]));

                                historyColumn.Item().AlignRight().PaddingTop(10).Width(250)
                                    .Background(Colors.Green.Lighten3)
                                    .Border(1).BorderColor(Colors.Green.Darken1)
                                    .Padding(8).Text(text =>
                                    {
                                        text.Span("Total Paid to Date: ").Bold().FontSize(11);
                                        text.Span($"₹{totalPaid:N2}").FontSize(13).Bold().FontColor(Colors.Green.Darken3);
                                    });
                            });
                        }

                        // Summary Box (Total & Balance)
                        column.Item().PaddingTop(15).Row(rowLayout =>
                        {
                            var totalCol = row.Table.Columns.Cast<DataColumn>()
                                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("total"));

                            var balanceCol = row.Table.Columns.Cast<DataColumn>()
                                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("balance"));

                            if (totalCol != null)
                            {
                                rowLayout.RelativeItem().Background(Colors.Blue.Lighten4)
                                    .Border(1).BorderColor(Colors.Blue.Darken1)
                                    .Padding(10).Text(text =>
                                    {
                                        text.Span("Total Fees: ").Bold();
                                        text.Span(row[totalCol]?.ToString() ?? "0")
                                            .FontSize(14).Bold().FontColor(Colors.Blue.Darken3);
                                    });
                            }

                            if (balanceCol != null)
                            {
                                var balanceValue = decimal.TryParse(row[balanceCol]?.ToString(), out decimal bal) ? bal : 0;
                                var balanceColor = balanceValue > 0 ? Colors.Red.Darken1 : Colors.Green.Darken1;
                                var balanceBg = balanceValue > 0 ? Colors.Red.Lighten4 : Colors.Green.Lighten4;

                                rowLayout.RelativeItem().Background(balanceBg)
                                    .Border(1).BorderColor(balanceColor)
                                    .Padding(10).Text(text =>
                                    {
                                        text.Span("Balance Remaining: ").Bold();
                                        text.Span(row[balanceCol]?.ToString() ?? "0")
                                            .FontSize(14).Bold().FontColor(balanceColor);
                                    });
                            }
                        });
                    });

                    // ============ FOOTER ============
                    page.Footer().Column(column =>
                    {
                        column.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

                        column.Item().AlignCenter().PaddingTop(10).Text(text =>
                        {
                            text.Span("Generated on: ").FontSize(8).FontColor(Colors.Grey.Darken1);
                            text.Span(DateTime.Now.ToString("dd-MM-yyyy HH:mm")).FontSize(8).Bold();
                        });

                        column.Item().AlignCenter().Text("Fee Management System")
                            .FontSize(8).Italic().FontColor(Colors.Grey.Medium);
                    });
                });
            })
            .GeneratePdf(filePath);
        }

        /// <summary>
        /// Generates a summary report for multiple students
        /// </summary>
        public void GenerateSummaryReport(DataTable data, string filePath, string reportTitle, CsvDataService.SheetMetadata metadata = null)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(20);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                    // ============ HEADER ============
                    page.Header().Column(column =>
                    {
                        column.Spacing(5);

                        if (metadata != null && !string.IsNullOrWhiteSpace(metadata.InstituteName))
                        {
                            column.Item().AlignCenter().Text(metadata.InstituteName)
                                .FontSize(14)
                                .Bold()
                                .FontColor(Colors.Blue.Darken2);
                        }

                        column.Item().AlignCenter().Text(reportTitle)
                            .FontSize(13)
                            .Bold();

                        if (metadata != null && !string.IsNullOrWhiteSpace(metadata.Period))
                        {
                            column.Item().AlignCenter().Text(metadata.Period)
                                .FontSize(10)
                                .Italic();
                        }

                        column.Item().PaddingTop(8).LineHorizontal(2).LineColor(Colors.Blue.Darken2);
                    });

                    // ============ CONTENT ============
                    page.Content().PaddingTop(15).Column(column =>
                    {
                        // Summary Stats
                        column.Item().Row(row =>
                        {
                            row.RelativeItem().Background(Colors.Blue.Lighten4).Padding(8).Text(text =>
                            {
                                text.Span("Total Records: ").Bold();
                                text.Span(data.Rows.Count.ToString());
                            });

                            row.RelativeItem().Background(Colors.Green.Lighten4).Padding(8).Text(text =>
                            {
                                text.Span("Generated: ").Bold();
                                text.Span(DateTime.Now.ToString("dd-MM-yyyy"));
                            });
                        });

                        // Main Data Table
                        column.Item().PaddingTop(15).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                foreach (DataColumn col in data.Columns)
                                {
                                    // Make Payment History column wider
                                    if (col.ColumnName.ToLower().Contains("payment history"))
                                        columns.RelativeColumn(3);
                                    else
                                        columns.RelativeColumn();
                                }
                            });

                            // Header Row
                            foreach (DataColumn col in data.Columns)
                            {
                                table.Cell().Element(HeaderCellStyle).Text(col.ColumnName).Bold();
                            }

                            // Data Rows
                            bool alternateRow = false;
                            foreach (DataRow dataRow in data.Rows)
                            {
                                foreach (DataColumn col in data.Columns)
                                {
                                    var value = dataRow[col]?.ToString() ?? "";

                                    if (alternateRow)
                                        table.Cell().Element(DataCellStyleAlt).Text(FormatValue(value, col.ColumnName));
                                    else
                                        table.Cell().Element(DataCellStyle).Text(FormatValue(value, col.ColumnName));
                                }
                                alternateRow = !alternateRow;
                            }
                        });
                    });

                    // ============ FOOTER ============
                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.CurrentPageNumber();
                        x.Span(" / ");
                        x.TotalPages();
                    });
                });
            })
            .GeneratePdf(filePath);
        }

        // ============ STYLING HELPERS ============

        private static IContainer HeaderCellStyle(IContainer container)
        {
            return container
                .Border(1)
                .BorderColor(Colors.Blue.Darken2)
                .Background(Colors.Blue.Lighten3)
                .Padding(8)
                .AlignCenter()
                .AlignMiddle();
        }

        private static IContainer DataCellStyle(IContainer container)
        {
            return container
                .Border(1)
                .BorderColor(Colors.Grey.Lighten2)
                .Background(Colors.White)
                .Padding(6)
                .AlignLeft()
                .AlignMiddle();
        }

        private static IContainer DataCellStyleAlt(IContainer container)
        {
            return container
                .Border(1)
                .BorderColor(Colors.Grey.Lighten2)
                .Background(Colors.Grey.Lighten4)
                .Padding(6)
                .AlignLeft()
                .AlignMiddle();
        }

        /// <summary>
        /// Format values for better display
        /// </summary>
        private static string FormatValue(string value, string columnName = "")
        {
            if (string.IsNullOrWhiteSpace(value))
                return "-";

            // Special formatting for payment history
            if (columnName.ToLower().Contains("payment history"))
            {
                // Replace semicolons with line breaks for better readability
                return value.Replace("; ", "\n");
            }

            // Format as currency if it's a number
            if (decimal.TryParse(value, out decimal number))
            {
                return $"₹{number:N2}";
            }

            return value;
        }

        /// <summary>
        /// Helper method to get value from DataRow with fallback column names
        /// </summary>
        private string GetValueFromRow(DataRow row, params string[] columnNames)
        {
            foreach (var colName in columnNames)
            {
                if (row.Table.Columns.Contains(colName) && row[colName] != DBNull.Value)
                {
                    return row[colName].ToString();
                }
            }
            return string.Empty;
        }
    }
}