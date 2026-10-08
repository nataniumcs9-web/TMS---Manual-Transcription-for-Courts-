using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TranscriberClient.Models;
using TranscriberClient.Services;
using TranscriberClient.ViewModels;

namespace TranscriberClient.Views;

public partial class DashboardWindow : Window
{
    private readonly DashboardViewModel _viewModel;
    private readonly DatabaseService _databaseService = new();
    private readonly LocalAudioAssignmentService _localAudioService = new();
    private readonly DispatcherTimer _refreshTimer = new();
    private readonly DispatcherTimer _searchTimer = new();
    private bool _isLoading;
    private bool _hasShownInitialLoadError;

    public DashboardWindow(UserAccount currentUser)
    {
        InitializeComponent();
        _viewModel = new DashboardViewModel(currentUser);
        DataContext = _viewModel;
        Title = $"Transcriber Dashboard — Welcome, {currentUser.FullName}";
        Topmost = AppSettings.UiPreferences.DashboardAlwaysOnTop;
        SetCompactLayout(AppSettings.UiPreferences.CompactDashboard);

        _refreshTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(AppSettings.UiPreferences.AutoRefreshSeconds, 15, 300));
        _refreshTimer.Tick += RefreshTimer_Tick;
        _searchTimer.Interval = TimeSpan.FromMilliseconds(250);
        _searchTimer.Tick += SearchTimer_Tick;
        Loaded += DashboardWindow_Loaded;
        Closed += DashboardWindow_Closed;
    }

    private async void DashboardWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadRecordsAsync(isInitialLoad: true);
        _refreshTimer.Start();
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        await LoadRecordsAsync(isInitialLoad: false);
    }

    private void DashboardWindow_Closed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _searchTimer.Stop();
    }

    private async Task LoadRecordsAsync(bool isInitialLoad)
    {
        if (_isLoading)
        {
            return;
        }

        _isLoading = true;
        try
        {
            await _viewModel.LoadAsync();
        }
        catch (Exception ex)
        {
            _viewModel.LastUpdatedText = "Offline · retrying automatically";
            Serilog.Log.Error(ex, "Could not refresh dashboard records for {Username}", _viewModel.CurrentUser.Username);
            if (isInitialLoad && !_hasShownInitialLoadError)
            {
                _hasShownInitialLoadError = true;
                MessageBox.Show(
                    "Could not load your assignments. The dashboard will retry automatically. Check your database connection and account permissions.",
                    "Dashboard Connection",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void SearchTimer_Tick(object? sender, EventArgs e)
    {
        _searchTimer.Stop();
        _viewModel.SearchText = SearchTextBox.Text;
    }

    private async void AddLocalAudioButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LocalAudioAssignmentWindow(_viewModel.CurrentUser)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            await LoadRecordsAsync(isInitialLoad: false);
            _viewModel.LastUpdatedText = "Local assignment saved on this computer";
        }
    }

    private async void UserSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new UserSettingsWindow(_viewModel.CurrentUser)
        {
            Owner = this
        };

        if (settingsWindow.ShowDialog() == true)
        {
            var preferences = AppSettings.UiPreferences;
            Topmost = preferences.DashboardAlwaysOnTop;
            _refreshTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(preferences.AutoRefreshSeconds, 15, 300));
            SetCompactLayout(preferences.CompactDashboard);
            _viewModel.RefreshCalendarDisplay();
            AssignedGrid.Items.Refresh();
            PendingGrid.Items.Refresh();
            SuspendedGrid.Items.Refresh();
            FinishedGrid.Items.Refresh();
            await LoadRecordsAsync(isInitialLoad: false);
        }
    }

    private void SetCompactLayout(bool isCompact)
    {
        var rowHeight = isCompact ? 30d : 40d;
        AssignedGrid.RowHeight = rowHeight;
        PendingGrid.RowHeight = rowHeight;
        SuspendedGrid.RowHeight = rowHeight;
        FinishedGrid.RowHeight = rowHeight;
    }

    private void ReportsButton_Click(object sender, RoutedEventArgs e)
    {
        var reportsWindow = new ReportsWindow(_viewModel.CurrentUser, _viewModel.GetAllRecords())
        {
            Owner = this
        };
        reportsWindow.ShowDialog();
    }

    private async void StartWorkButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: Record record })
        {
            return;
        }

        try
        {
            if (record.IsLocalOnly)
            {
                await _localAudioService.SaveStatusAsync(record, "Pending", record.Remark);
            }
            else
            {
                await _databaseService.UpdateRecordStatusAsync(record.Id, "Pending", record.Remark);
            }

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

        await LoadRecordsAsync(isInitialLoad: false);
        new TranscriptionWindow(record, _viewModel.CurrentUser).Show();
    }

    private async void ReturnToPendingButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: Record record })
        {
            return;
        }

        var result = MessageBox.Show(
            $"Return File {record.FileNum}, Machine {record.MachineNum} to In progress? Its finished date will be kept.",
            "Return Assignment",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (record.IsLocalOnly)
            {
                await _localAudioService.SaveStatusAsync(record, "Pending", record.Remark);
            }
            else
            {
                await _databaseService.UpdateRecordStatusAsync(record.Id, "Pending", record.Remark);
            }

            record.Status = "Pending";
            await LoadRecordsAsync(isInitialLoad: false);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not return record {RecordId} to Pending", record.Id);
            MessageBox.Show(
                $"The assignment could not be returned to In progress.\n\n{ex.Message}",
                "Return Assignment Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        Session.CurrentUser = null;
        new LoginWindow().Show();
        Close();
    }
}
