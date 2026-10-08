using System.Windows;
using System.Windows.Controls;
using TranscriberClient.Models;
using TranscriberClient.Services;
using TranscriberClient.ViewModels;

namespace TranscriberClient.Views;

public partial class DashboardWindow : Window
{
    private readonly DashboardViewModel _viewModel;
    private readonly DatabaseService _databaseService = new();
    private bool _isInitializingPreferences = true;

    public DashboardWindow(UserAccount currentUser)
    {
        InitializeComponent();
        _viewModel = new DashboardViewModel(currentUser);
        DataContext = _viewModel;
        Title = $"Transcriber Dashboard — Welcome, {currentUser.FullName}";
        AlwaysOnTopCheckBox.IsChecked = AppSettings.UiPreferences.DashboardAlwaysOnTop;
        Topmost = AlwaysOnTopCheckBox.IsChecked == true;
        _isInitializingPreferences = false;
        Loaded += DashboardWindow_Loaded;
    }

    private async void DashboardWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadRecordsAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadRecordsAsync();
    }

    private async Task LoadRecordsAsync()
    {
        try
        {
            await _viewModel.LoadAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not load dashboard records for {Username}", _viewModel.CurrentUser.Username);
            MessageBox.Show(
                "Could not load your assigned records. Check the database connection and try Refresh. Details were written to the application log.",
                "Dashboard Load Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void AlwaysOnTopChecked(object sender, RoutedEventArgs e)
    {
        Topmost = AlwaysOnTopCheckBox.IsChecked == true;
        if (_isInitializingPreferences)
        {
            return;
        }

        try
        {
            AppSettings.SaveUiPreferences(AppSettings.UiPreferences with
            {
                DashboardAlwaysOnTop = Topmost
            });
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not save dashboard always-on-top preference");
        }
    }

    private async void StartWorkButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var record = button.DataContext as Record;
        if (record == null)
        {
            return;
        }

        try
        {
            await _databaseService.UpdateRecordStatusAsync(record.Id, "Pending", record.Remark);
            record.Status = "Pending";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not move record {RecordId} to Pending when work started", record.Id);
            MessageBox.Show(
                "The record could not be moved to Pending. Check the database connection and try again.",
                "Start Work Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        await LoadRecordsAsync();
        var transcriptionWindow = new TranscriptionWindow(record, _viewModel.CurrentUser);
        transcriptionWindow.Show();
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        Session.CurrentUser = null;
        var loginWindow = new LoginWindow();
        loginWindow.Show();
        Close();
    }
}
