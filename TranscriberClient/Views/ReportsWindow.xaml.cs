using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using TranscriberClient.Models;
using TranscriberClient.Services;

namespace TranscriberClient.Views;

public partial class ReportsWindow : Window
{
    private readonly UserAccount _currentUser;
    private readonly IReadOnlyList<Record> _allRecords;
    private readonly ObservableCollection<Record> _filteredRecords = new();
    public ObservableCollection<ReportChartItem> StatusChart { get; } = new();
    public ObservableCollection<ReportChartItem> AppointmentChart { get; } = new();
    private IReadOnlyList<Record> _reportRecords = Array.Empty<Record>();
    private DateTime _reportStart;
    private DateTime _reportEnd;
    private string _reportStatus = "All";

    public sealed record ReportChartItem(string Label, int Count, double Percent, Brush Color);

    public ReportsWindow(UserAccount currentUser, IReadOnlyList<Record> records)
    {
        InitializeComponent();
        _currentUser = currentUser;
        _allRecords = records;
        DataContext = this;
        ReportGrid.ItemsSource = _filteredRecords;
        StartDateTextBox.Text = CalendarDateFormatter.FormatDate(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));
        EndDateTextBox.Text = CalendarDateFormatter.FormatDate(DateTime.Today);
        StatusComboBox.SelectedIndex = 0;
        ApplyFilters();
    }

    private void Filter_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            ApplyFilters();
        }
    }

    private void ApplyFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        if (!CalendarDateFormatter.TryParseDate(StartDateTextBox.Text, out var start))
        {
            SummaryText.Text = "Enter a valid start date using the selected calendar format.";
            StartDateTextBox.Focus();
            return;
        }

        if (!CalendarDateFormatter.TryParseDate(EndDateTextBox.Text, out var end))
        {
            SummaryText.Text = "Enter a valid end date using the selected calendar format.";
            EndDateTextBox.Focus();
            return;
        }

        start = start.Date;
        end = end.Date;
        if (end < start)
        {
            SummaryText.Text = "The end date must be on or after the start date.";
            return;
        }

        var status = (StatusComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "All";
        var records = _allRecords
            .Where(record =>
            {
                var relevantDate = record.FinishedDate?.Date
                    ?? record.RecDate?.Date
                    ?? record.InsertedOn?.Date
                    ?? record.AppointedOn?.Date;
                return relevantDate.HasValue && relevantDate.Value >= start && relevantDate.Value <= end
                    && (status == "All" || string.Equals(record.Status, status, StringComparison.OrdinalIgnoreCase));
            })
            .OrderBy(record => record.AppointedOn ?? record.RecDate)
            .ThenBy(record => record.FileNum)
            .ToList();

        _reportStart = start;
        _reportEnd = end;
        _reportStatus = status;
        _reportRecords = records;
        _filteredRecords.Clear();
        foreach (var record in records)
        {
            _filteredRecords.Add(record);
        }

        SummaryText.Text =
            $"{records.Count} record(s) · {records.Count(record => record.Status == "Assigned")} assigned · " +
            $"{records.Count(record => record.Status == "Pending")} in progress · " +
            $"{records.Count(record => record.Status == "Finished")} finished";

        UpdateCharts(records);
    }

    private void UpdateCharts(IReadOnlyList<Record> records)
    {
        TotalCountText.Text = records.Count.ToString();
        var finishedCount = records.Count(record => record.Status == "Finished");
        var activeRecords = records.Where(record => record.Status != "Finished").ToList();
        var overdueCount = activeRecords.Count(record => record.AppointedOn?.Date < DateTime.Today);
        ActiveCountText.Text = activeRecords.Count.ToString();
        OverdueCountText.Text = overdueCount.ToString();
        CompletionRateText.Text = records.Count == 0
            ? "0%"
            : $"{Math.Round(finishedCount * 100d / records.Count):0}%";

        StatusChart.Clear();
        AddChartItem(StatusChart, records, "Assigned", "Assigned", "#167D8D");
        AddChartItem(StatusChart, records, "Pending", "In progress", "#2F6FBB");
        AddChartItem(StatusChart, records, "Suspended", "Suspended", "#A76B13");
        AddChartItem(StatusChart, records, "Finished", "Finished", "#32805B");

        AppointmentChart.Clear();
        AddAppointmentItem(AppointmentChart, activeRecords, "Past", record => record.AppointedOn?.Date < DateTime.Today, "#B54738");
        AddAppointmentItem(AppointmentChart, activeRecords, "Today", record => record.AppointedOn?.Date == DateTime.Today, "#A76B13");
        AddAppointmentItem(AppointmentChart, activeRecords, "Upcoming", record => record.AppointedOn?.Date > DateTime.Today, "#167D8D");
        AddAppointmentItem(AppointmentChart, activeRecords, "Not set", record => record.AppointedOn == null, "#8291A3");
    }

    private static void AddChartItem(
        ObservableCollection<ReportChartItem> target,
        IReadOnlyList<Record> records,
        string status,
        string label,
        string color)
    {
        var count = records.Count(record => record.Status == status);
        target.Add(new ReportChartItem(label, count, records.Count == 0 ? 0 : count * 100d / records.Count, new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))));
    }

    private static void AddAppointmentItem(
        ObservableCollection<ReportChartItem> target,
        IReadOnlyList<Record> records,
        string label,
        Func<Record, bool> predicate,
        string color)
    {
        var count = records.Count(predicate);
        target.Add(new ReportChartItem(label, count, records.Count == 0 ? 0 : count * 100d / records.Count, new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))));
    }

    private void ExportExcelButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export transcription report to Excel",
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            AddExtension = true,
            FileName = $"Transcription-report-{DateTime.Today:yyyy-MM-dd}.xlsx"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ReportExportService.ExportExcel(dialog.FileName, _reportRecords, _currentUser, _reportStart, _reportEnd, _reportStatus);
            MessageBox.Show($"Excel report saved to:\n{dialog.FileName}", "Report Exported", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not export Excel report for {Username}", _currentUser.Username);
            MessageBox.Show($"Excel report could not be saved.\n\n{ex.Message}", "Excel Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportPdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (_reportRecords.Count == 0)
        {
            MessageBox.Show("There are no records in this report. Adjust the date or status filters first.", "No Report Data", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            ReportExportService.PrintPdf(_reportRecords, _currentUser, _reportStart, _reportEnd, _reportStatus);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not print or export PDF report for {Username}", _currentUser.Username);
            MessageBox.Show($"The report could not be printed or exported to PDF.\n\n{ex.Message}", "PDF Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
