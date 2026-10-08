using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using TranscriberClient.Models;
using WpfRun = System.Windows.Documents.Run;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfTable = System.Windows.Documents.Table;
using WpfTableColumn = System.Windows.Documents.TableColumn;

namespace TranscriberClient.Services;

public static class ReportExportService
{
    public static void ExportExcel(
        string path,
        IReadOnlyList<Record> records,
        UserAccount user,
        DateTime startDate,
        DateTime endDate,
        string status)
    {
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        AddStyles(workbookPart);

        var sheets = workbookPart.Workbook.AppendChild(new Sheets());
        AddSheet(workbookPart, sheets, "Summary", BuildSummaryRows(records, user, startDate, endDate, status), 1);
        AddSheet(workbookPart, sheets, "Assignments", BuildRecordRows(records), 2, addFilter: true);
        workbookPart.Workbook.Save();
    }

    public static void PrintPdf(
        IReadOnlyList<Record> records,
        UserAccount user,
        DateTime startDate,
        DateTime endDate,
        string status)
    {
        var printDialog = new System.Windows.Controls.PrintDialog();
        if (printDialog.ShowDialog() != true)
        {
            return;
        }

        if (printDialog.PrintTicket != null)
        {
            printDialog.PrintTicket.PageOrientation = System.Printing.PageOrientation.Landscape;
        }

        var report = CreatePdfDocument(records, user, startDate, endDate, status);
        printDialog.PrintDocument(
            ((IDocumentPaginatorSource)report).DocumentPaginator,
            $"Court Transcription Report - {user.Username}");
    }

    private static List<string[]> BuildSummaryRows(
        IReadOnlyList<Record> records,
        UserAccount user,
        DateTime startDate,
        DateTime endDate,
        string status)
    {
        var active = records.Where(record => record.Status != "Finished").ToList();
        var rows = new List<string[]>
        {
            new[] { "COURT TRANSCRIPTION WORK REPORT" },
            new[] { "Transcriber", user.FullName },
            new[] { "Username", user.Username },
            new[] { "Report period", $"{CalendarDateFormatter.FormatDate(startDate)} - {CalendarDateFormatter.FormatDate(endDate)}" },
            new[] { "Status filter", status },
            new[] { "Generated", $"{CalendarDateFormatter.FormatDate(DateTime.Now)} {DateTime.Now:HH:mm}" },
            new[] { "" },
            new[] { "WORKLOAD SUMMARY", "Count" },
            new[] { "Records in report", records.Count.ToString(CultureInfo.InvariantCulture) },
            new[] { "Assigned", records.Count(record => record.Status == "Assigned").ToString(CultureInfo.InvariantCulture) },
            new[] { "In progress", records.Count(record => record.Status == "Pending").ToString(CultureInfo.InvariantCulture) },
            new[] { "Suspended", records.Count(record => record.Status == "Suspended").ToString(CultureInfo.InvariantCulture) },
            new[] { "Finished", records.Count(record => record.Status == "Finished").ToString(CultureInfo.InvariantCulture) },
            new[] { "Active appointments passed", active.Count(record => record.AppointedOn?.Date < DateTime.Today).ToString(CultureInfo.InvariantCulture) },
            new[] { "Active appointments today", active.Count(record => record.AppointedOn?.Date == DateTime.Today).ToString(CultureInfo.InvariantCulture) },
            new[] { "" },
            new[] { "WORKLOAD CHART", "Count", "Share", "Distribution" }
        };

        AddChartRows(rows, records.Count,
            ("Assigned", records.Count(record => record.Status == "Assigned")),
            ("In progress", records.Count(record => record.Status == "Pending")),
            ("Suspended", records.Count(record => record.Status == "Suspended")),
            ("Finished", records.Count(record => record.Status == "Finished")));

        rows.Add(new[] { "" });
        rows.Add(new[] { "APPOINTMENT CHART", "Count", "Share", "Distribution" });
        AddChartRows(rows, active.Count,
            ("Past", active.Count(record => record.AppointedOn?.Date < DateTime.Today)),
            ("Today", active.Count(record => record.AppointedOn?.Date == DateTime.Today)),
            ("Upcoming", active.Count(record => record.AppointedOn?.Date > DateTime.Today)),
            ("Not set", active.Count(record => record.AppointedOn == null)));
        return rows;
    }

    private static void AddChartRows(List<string[]> rows, int total, params (string Label, int Count)[] values)
    {
        foreach (var (label, count) in values)
        {
            var share = total == 0 ? 0 : (int)Math.Round(count * 100d / total);
            var barLength = total == 0 ? 0 : (int)Math.Round(count * 24d / values.Max(item => item.Count));
            rows.Add(new[]
            {
                label,
                count.ToString(CultureInfo.InvariantCulture),
                $"{share}%",
                new string('■', barLength)
            });
        }
    }

    private static List<string[]> BuildRecordRows(IReadOnlyList<Record> records)
    {
        var rows = new List<string[]>
        {
            new[] { "Record ID", "Status", "File #", "Machine #", "Applicant", "Defendant", "Witness type", "Witnesses",
                "Trial", "Judge", "Recorded", "Appointment", "Finished", "Recorder", "Audio", "Audio status", "Notes" }
        };

        rows.AddRange(records.Select(record => new[]
        {
            record.Id.ToString(CultureInfo.InvariantCulture),
            record.Status,
            record.FileNum.ToString(CultureInfo.InvariantCulture),
            record.MachineNum.ToString(CultureInfo.InvariantCulture),
            record.Applicant,
            record.Defendant,
            record.WitnessType,
            record.Witnesses,
            record.Trial,
            record.Judge,
            CalendarDateFormatter.FormatDate(record.RecDate),
            CalendarDateFormatter.FormatDate(record.AppointedOn),
            CalendarDateFormatter.FormatDate(record.FinishedDate),
            record.Recorder,
            record.Audio,
            record.AudioStatus,
            record.Remark
        }));
        return rows;
    }

    private static void AddStyles(WorkbookPart workbookPart)
    {
        var styles = workbookPart.AddNewPart<WorkbookStylesPart>();
        styles.Stylesheet = new Stylesheet(
            new DocumentFormat.OpenXml.Spreadsheet.Fonts(
                new Font(),
                new Font(new DocumentFormat.OpenXml.Spreadsheet.Bold(), new DocumentFormat.OpenXml.Spreadsheet.Color { Rgb = "FFFFFFFF" })),
            new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                new Fill(new PatternFill(
                    new ForegroundColor { Rgb = "FF173B63" },
                    new BackgroundColor { Indexed = 64 })
                { PatternType = PatternValues.Solid })),
            new Borders(new Border()),
            new CellStyleFormats(new CellFormat()),
            new CellFormats(
                new CellFormat(),
                new CellFormat { FontId = 1, FillId = 2, ApplyFont = true, ApplyFill = true }));
        styles.Stylesheet.Save();
    }

    private static void AddSheet(
        WorkbookPart workbookPart,
        Sheets sheets,
        string sheetName,
        IReadOnlyList<string[]> rows,
        uint sheetId,
        bool addFilter = false)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        worksheetPart.Worksheet = new Worksheet(
            new Columns(new Column { Min = 1, Max = 17, Width = 20, CustomWidth = true }),
            sheetData);

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = new Row { RowIndex = (uint)(rowIndex + 1) };
            for (var columnIndex = 0; columnIndex < rows[rowIndex].Length; columnIndex++)
            {
                var value = rows[rowIndex][columnIndex] ?? string.Empty;
                var isNumeric = sheetName == "Assignments"
                    && rowIndex > 0
                    && columnIndex is 0 or 2 or 3
                    || sheetName == "Summary"
                        && rowIndex >= 8
                        && columnIndex == 1
                        && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);
                var cell = isNumeric
                    ? new Cell
                    {
                        CellReference = $"{GetColumnName(columnIndex + 1)}{rowIndex + 1}",
                        DataType = CellValues.Number,
                        CellValue = new CellValue(value)
                    }
                    : new Cell
                    {
                        CellReference = $"{GetColumnName(columnIndex + 1)}{rowIndex + 1}",
                        DataType = CellValues.InlineString,
                        InlineString = new InlineString(new Text(value)
                        {
                            Space = SpaceProcessingModeValues.Preserve
                        })
                    };

                if (rowIndex == 0
                    || rows[rowIndex][0].Contains("SUMMARY", StringComparison.Ordinal)
                    || rows[rowIndex][0].Contains("CHART", StringComparison.Ordinal))
                {
                    cell.StyleIndex = 1;
                }

                row.Append(cell);
            }

            sheetData.Append(row);
        }

        if (addFilter && rows.Count > 1)
        {
            var endColumn = GetColumnName(rows[0].Length);
            worksheetPart.Worksheet.Append(new AutoFilter { Reference = $"A1:{endColumn}{rows.Count}" });
        }

        worksheetPart.Worksheet.Save();
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = sheetId,
            Name = sheetName
        });
    }

    private static string GetColumnName(int column)
    {
        var result = string.Empty;
        while (column > 0)
        {
            column--;
            result = (char)('A' + column % 26) + result;
            column /= 26;
        }

        return result;
    }

    private static FlowDocument CreatePdfDocument(
        IReadOnlyList<Record> records,
        UserAccount user,
        DateTime startDate,
        DateTime endDate,
        string status)
    {
        var document = new FlowDocument
        {
            FontFamily = new WpfFontFamily("Arial"),
            FontSize = 9,
            PageWidth = 11 * 96,
            PageHeight = 8.5 * 96,
            PagePadding = new Thickness(32),
            ColumnGap = 0,
            ColumnWidth = 11 * 96 - 64
        };

        var heading = new Paragraph(new WpfRun("GEDEO ZONE HIGH COURT · TRANSCRIPTION REPORT"))
        {
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(23, 59, 99))
        };
        document.Blocks.Add(heading);
        document.Blocks.Add(new Paragraph(new WpfRun(
            $"{user.FullName} ({user.Username}) · {CalendarDateFormatter.FormatDate(startDate)} – {CalendarDateFormatter.FormatDate(endDate)} · Status: {status} · Generated {CalendarDateFormatter.FormatDate(DateTime.Now)} {DateTime.Now:HH:mm}")));
        var summary = new Paragraph(new WpfRun(
            $"Records: {records.Count}    Assigned: {records.Count(r => r.Status == "Assigned")}    In progress: {records.Count(r => r.Status == "Pending")}    Suspended: {records.Count(r => r.Status == "Suspended")}    Finished: {records.Count(r => r.Status == "Finished")}    Past appointments: {records.Count(r => r.Status != "Finished" && r.AppointedOn?.Date < DateTime.Today)}"));
        summary.FontWeight = FontWeights.SemiBold;
        document.Blocks.Add(summary);
        document.Blocks.Add(CreatePdfChart("Workload by status", records.Count,
            ("Assigned", records.Count(record => record.Status == "Assigned")),
            ("In progress", records.Count(record => record.Status == "Pending")),
            ("Suspended", records.Count(record => record.Status == "Suspended")),
            ("Finished", records.Count(record => record.Status == "Finished"))));

        var activeRecords = records.Where(record => record.Status != "Finished").ToList();
        document.Blocks.Add(CreatePdfChart("Appointment health", activeRecords.Count,
            ("Past", activeRecords.Count(record => record.AppointedOn?.Date < DateTime.Today)),
            ("Today", activeRecords.Count(record => record.AppointedOn?.Date == DateTime.Today)),
            ("Upcoming", activeRecords.Count(record => record.AppointedOn?.Date > DateTime.Today)),
            ("Not set", activeRecords.Count(record => record.AppointedOn == null))));

        var table = new WpfTable { CellSpacing = 0 };
        foreach (var _ in Enumerable.Range(0, 9))
        {
            table.Columns.Add(new WpfTableColumn());
        }

        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        var header = new TableRow();
        group.Rows.Add(header);
        foreach (var title in new[] { "Status", "File", "Machine", "Applicant", "Defendant", "Judge", "Recorded", "Appointment", "Notes" })
        {
            header.Cells.Add(CreatePdfCell(title, isHeader: true));
        }

        foreach (var record in records)
        {
            var row = new TableRow();
            group.Rows.Add(row);
            foreach (var value in new[]
            {
                record.Status,
                record.FileNum.ToString(CultureInfo.InvariantCulture),
                record.MachineNum.ToString(CultureInfo.InvariantCulture),
                record.Applicant,
                record.Defendant,
                record.Judge,
                CalendarDateFormatter.FormatDate(record.RecDate),
                CalendarDateFormatter.FormatDate(record.AppointedOn),
                record.Remark
            })
            {
                row.Cells.Add(CreatePdfCell(value, isHeader: false));
            }
        }

        document.Blocks.Add(table);
        document.Blocks.Add(new Paragraph(new WpfRun("Appointment dates are workflow reminders and do not replace court-set transcript deadlines."))
        {
            Margin = new Thickness(0, 10, 0, 0),
            Foreground = Brushes.DimGray,
            FontSize = 8
        });
        return document;
    }

    private static Paragraph CreatePdfChart(string title, int total, params (string Label, int Count)[] values)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 2, 0, 6) };
        paragraph.Inlines.Add(new WpfRun(title + "  ")
        {
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(23, 59, 99))
        });

        var max = values.Length == 0 ? 0 : values.Max(item => item.Count);
        foreach (var (label, count) in values)
        {
            var barLength = max == 0 ? 0 : (int)Math.Round(count * 12d / max);
            paragraph.Inlines.Add(new WpfRun($"{label}: {count} {new string('■', barLength)}  "));
        }

        paragraph.Inlines.Add(new WpfRun($"(Total {total})"));
        return paragraph;
    }

    private static TableCell CreatePdfCell(string value, bool isHeader)
    {
        var paragraph = new Paragraph(new WpfRun(value ?? string.Empty))
        {
            Margin = new Thickness(3, 2, 3, 2),
            FontWeight = isHeader ? FontWeights.Bold : FontWeights.Normal,
            FontSize = 8
        };
        return new TableCell(paragraph)
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(0.5),
            Background = isHeader ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(231, 238, 246)) : Brushes.White
        };
    }
}
