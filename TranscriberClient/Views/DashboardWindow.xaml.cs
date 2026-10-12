using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using TranscriberClient.Models;
using TranscriberClient.Services;
using TranscriberClient.ViewModels;

namespace TranscriberClient.Views;

public partial class DashboardWindow : Window
{
    private readonly DashboardViewModel _viewModel;
    private readonly LocalAudioAssignmentService _localAudioService = new();
    private readonly OfflineSyncService _syncService = new();
    private readonly WordDocumentService _wordDocumentService = new();
    private readonly DispatcherTimer _refreshTimer = new();
    private readonly DispatcherTimer _searchTimer = new();
    private readonly DispatcherTimer _searchIdleTimer = new();
    private bool _isLoading;
    private bool _hasShownInitialLoadError;
    private bool _isSearchMode;

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
        _searchIdleTimer.Interval = TimeSpan.FromSeconds(30);
        _searchIdleTimer.Tick += SearchIdleTimer_Tick;
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
        _searchIdleTimer.Stop();
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
        if (_isSearchMode)
        {
            ResetSearchIdleTimer();
        }
    }

    private void SearchTimer_Tick(object? sender, EventArgs e)
    {
        _searchTimer.Stop();
        _viewModel.SearchText = SearchTextBox.Text;
    }

    private void SearchToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isSearchMode)
        {
            ShowRecommendations();
            return;
        }

        _isSearchMode = true;
        RecommendationsPanel.Visibility = Visibility.Collapsed;
        SearchPanel.Visibility = Visibility.Visible;
        SearchToggleButton.Content = "\uE711";
        SearchToggleButton.ToolTip = "Close search and show work recommendations";
        SearchTextBox.Focus();
        ResetSearchIdleTimer();
    }

    private void SearchPanel_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        ResetSearchIdleTimer();
    }

    private void SearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        ResetSearchIdleTimer();
    }

    private void SearchIdleTimer_Tick(object? sender, EventArgs e)
    {
        ShowRecommendations();
    }

    private void ResetSearchIdleTimer()
    {
        _searchIdleTimer.Stop();
        if (_isSearchMode)
        {
            _searchIdleTimer.Start();
        }
    }

    private void ShowRecommendations()
    {
        _searchIdleTimer.Stop();
        _isSearchMode = false;
        SearchPanel.Visibility = Visibility.Collapsed;
        RecommendationsPanel.Visibility = Visibility.Visible;
        SearchToggleButton.Content = "\uE721";
        SearchToggleButton.ToolTip = "Search assignments";
        if (SearchTextBox.Text.Length > 0)
        {
            SearchTextBox.Clear();
            _viewModel.SearchText = string.Empty;
        }
    }

    private async void AddLocalAudioButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LocalAudioAssignmentWindow(_viewModel.CurrentUser, _viewModel.GetAllRecords())
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
        var rowHeight = isCompact ? 38d : 42d;
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
            if (!await PrepareOfflineCopyAsync(record))
            {
                return;
            }

            await _localAudioService.SaveStatusAsync(record, "Pending", record.Remark);
            await TrySyncRecordAsync(record);
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
        ShowTranscriptionWindow(record);
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
            if (!await PrepareOfflineCopyAsync(record))
            {
                return;
            }

            await _localAudioService.SaveStatusAsync(record, "Pending", record.Remark);
            await TrySyncRecordAsync(record);
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

    private async void DownloadOfflineCopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: Record record })
        {
            return;
        }

        if (await PrepareOfflineCopyAsync(record))
        {
            _viewModel.LastUpdatedText = $"Record and audio available offline · File {record.FileNum}";
        }
    }

    private async Task<bool> PrepareOfflineCopyAsync(Record record)
    {
        if (record.IsOfflineDownloadInProgress)
        {
            return false;
        }

        record.IsOfflineDownloadInProgress = true;
        record.OfflineDownloadProgress = 0;
        record.OfflineDownloadStatus = "Preparing offline copy";
        try
        {
            var progress = new Progress<OfflineCopyProgress>(update =>
            {
                record.OfflineDownloadProgress = update.Percentage;
                record.OfflineDownloadStatus = update.Status;
            });
            await _localAudioService.EnsureOfflineCopyAsync(record, progress);
            record.OfflineDownloadProgress = 100;
            record.OfflineDownloadStatus = "Available offline";
            return true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not prepare offline copy for record {RecordId}", record.Id);
            MessageBox.Show(
                $"Offline copy could not be completed. Anything already downloaded has been kept; retry to download the missing part.\n\n{ex.Message}",
                "Offline Download Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            record.IsOfflineDownloadInProgress = false;
        }
    }

    private async Task TrySyncRecordAsync(Record record)
    {
        try
        {
            await _syncService.SyncRecordAsync(record, _viewModel.CurrentUser.Username);
        }
        catch (Exception ex)
        {
            Serilog.Log.Information(ex, "Record {RecordId} remains queued for synchronization", record.Id);
        }
    }

    private async void FinishedGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) != null)
        {
            return;
        }

        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is Record record)
        {
            await OpenFinishedRecordAsync(record);
        }
    }

    private async void OpenFinishedDocumentButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: Record record })
        {
            await OpenFinishedRecordAsync(record);
        }
    }

    private async Task OpenFinishedRecordAsync(Record record)
    {
        try
        {
            if (!await PrepareOfflineCopyAsync(record))
            {
                return;
            }

            ShowTranscriptionWindow(record, openExistingDocumentOnly: true);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not open finished transcript and audio for record {RecordId}", record.Id);
            MessageBox.Show(
                $"The finished record could not be opened.\n\n{ex.Message}",
                "Open Finished Record Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ShowTranscriptionWindow(Record record, bool openExistingDocumentOnly = false)
    {
        var workArea = SystemParameters.WorkArea;
        var transcriptionWindow = new TranscriptionWindow(record, _viewModel.CurrentUser, openExistingDocumentOnly)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = workArea.Width * 0.8,
            Height = workArea.Height * 0.8
        };
        transcriptionWindow.Show();
    }

    private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element != null)
        {
            if (element is T ancestor)
            {
                return ancestor;
            }

            element = element switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(element),
                ContentElement contentElement => ContentOperations.GetParent(contentElement)
                    ?? (contentElement as FrameworkContentElement)?.Parent,
                _ => null
            };
        }

        return null;
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        Session.CurrentUser = null;
        new LoginWindow().Show();
        Close();
    }
}
