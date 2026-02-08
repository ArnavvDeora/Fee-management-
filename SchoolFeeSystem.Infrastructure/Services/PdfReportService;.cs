using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Data;
using System.Linq;

namespace SchoolFeeSystem.Presentation.Services
{
    public class PdfReportService
    {
        /// <summary>
        /// Generates a detailed student fee report with all columns from the Excel
        /// </summary>
        public void GenerateStudentReport(DataRow row, string filePath, CsvDataService.SheetMetadata metadata = null)
        {
            QuestPDF.Settings.License = LicenseType.Community;

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

                        // Institute Name (if available from metadata)
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

                        // Period Info
                        if (metadata != null && !string.IsNullOrWhiteSpace(metadata.Period))
                        {
                            column.Item().AlignCenter().Text(metadata.Period)
                                .FontSize(11)
                                .Italic();
                        }

                        // Course Info
                        if (metadata != null && !string.IsNullOrWhiteSpace(metadata.CourseInfo))
                        {
                            column.Item().AlignCenter().Text(metadata.CourseInfo)
                                .FontSize(10)
                                .FontColor(Colors.Grey.Darken1);
                        }

                        // Separator Line
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
                            // Define columns: Label (40%) | Value (60%)
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(3);
                            });

                            // Header Row
                            table.Cell().Element(HeaderCellStyle).Text("Field").Bold();
                            table.Cell().Element(HeaderCellStyle).Text("Value").Bold();

                            // Data Rows - Display ALL columns from Excel
                            bool alternateRow = false;
                            foreach (DataColumn col in row.Table.Columns)
                            {
                                var cellValue = row[col]?.ToString() ?? "";

                                // Skip empty columns
                                if (string.IsNullOrWhiteSpace(cellValue) && string.IsNullOrWhiteSpace(col.ColumnName))
                                    continue;

                                // Alternate row coloring for readability
                                if (alternateRow)
                                {
                                    table.Cell().Element(DataCellStyleAlt).Text(col.ColumnName).Bold();
                                    table.Cell().Element(DataCellStyleAlt).Text(FormatValue(cellValue));
                                }
                                else
                                {
                                    table.Cell().Element(DataCellStyle).Text(col.ColumnName).Bold();
                                    table.Cell().Element(DataCellStyle).Text(FormatValue(cellValue));
                                }

                                alternateRow = !alternateRow;
                            }
                        });

                        // Summary Box (if Total exists)
                        var totalCol = row.Table.Columns.Cast<DataColumn>()
                            .FirstOrDefault(c => c.ColumnName.ToLower().Contains("total"));

                        if (totalCol != null)
                        {
                            column.Item().AlignRight().Width(200).Background(Colors.Green.Lighten4)
                                .Border(1).BorderColor(Colors.Green.Darken1)
                                .Padding(10).Text(text =>
                                {
                                    text.Span("Total Amount: ").Bold();
                                    text.Span(row[totalCol]?.ToString() ?? "0").FontSize(14).Bold().FontColor(Colors.Green.Darken3);
                                });
                        }
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
        /// Generates a summary report for multiple students (e.g., pending fees list)
        /// </summary>
        public void GenerateSummaryReport(DataTable data, string filePath, string reportTitle, CsvDataService.SheetMetadata metadata = null)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape()); // Landscape for wider tables
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
                            // Dynamic column definition based on data
                            table.ColumnsDefinition(columns =>
                            {
                                foreach (DataColumn col in data.Columns)
                                {
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
                                        table.Cell().Element(DataCellStyleAlt).Text(FormatValue(value));
                                    else
                                        table.Cell().Element(DataCellStyle).Text(FormatValue(value));
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
        /// Format values for better display (currency, dates, etc.)
        /// </summary>
        private static string FormatValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "-";

            // Try to format as currency if it's a number
            if (decimal.TryParse(value, out decimal number))
            {
                return $"₹{number:N2}";
            }

            return value;
        }
    }
}