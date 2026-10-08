using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TranscriberClient.Models;
using TranscriberClient.Services;
using TranscriberClient.ViewModels;
using Task = System.Threading.Tasks.Task;

namespace TranscriberClient.Views;

public partial class TranscriptionWindow : Window
{
    private readonly Record _record;
    private readonly DatabaseService _databaseService = new();
    private readonly AudioPlayerService _audioPlayerService = new();
    private readonly WordDocumentService _wordDocumentService = new();
    private readonly PedalService _pedalService = new();
    private readonly DispatcherTimer _playbackTimer = new();
    private readonly DispatcherTimer _audioPositionTimer = new();
    private object? _wordDocument;
    private bool _closeSaveInProgress;
    private bool _allowClose;
    private bool _isWindowInitialized;
    private bool _isUpdatingStatus;
    private bool _isCompact;
    private bool _audioLoaded;
    private bool _isSavingAudioPosition;
    private bool _audioSaveWarningShown;
    private bool _isInitializingPreferences = true;
    private string? _openedDocumentPath;

    public TranscriptionWindow(Record record, UserAccount currentUser)
    {
        InitializeComponent();
        _record = record;
        AlwaysOnTopCheckBox.IsChecked = AppSettings.UiPreferences.MiniWindowAlwaysOnTop;
        Topmost = AlwaysOnTopCheckBox.IsChecked == true;
        _isInitializingPreferences = false;

        DataContext = new TranscriptionViewModel(record, currentUser);
        Title = $"Transcription — {record.MachineNum}";
        _isUpdatingStatus = true;
        StatusComboBox.SelectedIndex = record.Status == "Suspended" ? 1 : 0;
        _isUpdatingStatus = false;

        FileNumberRun.Text = record.FileNum.ToString();
        MachineNumberRun.Text = record.MachineNum.ToString();
        RecordedDateRun.Text = record.RecDate?.ToString("d") ?? "—";
        AppointedDateRun.Text = record.AppointedOn?.ToString("d") ?? "—";
        ApplicantText.Text = record.Applicant;
        DefendantText.Text = record.Defendant;
        WitnessTypeText.Text = record.WitnessType;
        TrialJudgeText.Text = string.Join(" / ", new[] { record.Trial, record.Judge }.Where(value => !string.IsNullOrWhiteSpace(value)));
        RecorderWitnessesText.Text = string.Join(" / ", new[] { record.Recorder, record.Witnesses }.Where(value => !string.IsNullOrWhiteSpace(value)));
        RemarkText.Text = record.Remark;
        OpenDocumentButton.IsEnabled = _wordDocumentService.IsAvailable;
        OpenDocumentsFolderButton.IsEnabled = true;
        InsertTimestampButton.IsEnabled = _wordDocumentService.IsAvailable;
        BringWordToFrontButton.IsEnabled = _wordDocumentService.IsAvailable;

        _playbackTimer.Interval = TimeSpan.FromMilliseconds(250);
        _playbackTimer.Tick += PlaybackTimer_Tick;
        _audioPositionTimer.Interval = TimeSpan.FromSeconds(5);
        _audioPositionTimer.Tick += AudioPositionTimer_Tick;
        _isWindowInitialized = true;
        Loaded += TranscriptionWindow_Loaded;
        Closing += TranscriptionWindow_Closing;
    }

    private async void TranscriptionWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            OpenDocumentForRecord();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not prepare transcription document for record {RecordId}", _record.Id);
            MessageBox.Show(
                $"The transcription document could not be created or opened.\n\n{ex.Message}\n\nThe document folder is: {AppSettings.DocsFolder}",
                "Document Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        try
        {
            var audioUrl = AppSettings.AudioBaseUrl + _record.Audio;
            await _audioPlayerService.LoadFromUrlAsync(audioUrl);

            var savedPosition = await _databaseService.LoadAudioProgressAsync(_record.MachineNum.ToString());
            _audioPlayerService.Seek(savedPosition);
            _audioLoaded = true;

            VolumeSlider.Value = 0.8;
            SeekSlider.Maximum = _audioPlayerService.TotalSeconds > 0 ? _audioPlayerService.TotalSeconds : 100;
            UpdateTimeLabel();

            _pedalService.ConnectionChanged += connected =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    PedalStatusText.Text = connected ? "🎙 Pedal: Connected" : "🎙 Pedal: Not Found";
                }));
            };

            _pedalService.RewindPressed += () => Dispatcher.BeginInvoke(new Action(() => RewindButton_Click(this, new RoutedEventArgs())));
            _pedalService.PlayPausePressed += () => Dispatcher.BeginInvoke(new Action(() => PlayPauseButton_Click(this, new RoutedEventArgs())));
            _pedalService.ForwardPressed += () => Dispatcher.BeginInvoke(new Action(() => ForwardButton_Click(this, new RoutedEventArgs())));
            _pedalService.Start();

        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unable to load the audio file. {ex.Message}", "Audio Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            Serilog.Log.Error(ex, "Audio load failed");
        }
    }

    private void PlaybackTimer_Tick(object? sender, EventArgs e)
    {
        if (_audioPlayerService == null)
        {
            return;
        }

        UpdateTimeLabel();
        SeekSlider.Value = _audioPlayerService.CurrentPositionSeconds;
        if (_audioPlayerService.TotalSeconds > 0)
        {
            SeekSlider.Maximum = _audioPlayerService.TotalSeconds;
        }
    }

    private async void AudioPositionTimer_Tick(object? sender, EventArgs e)
    {
        if (!_audioPlayerService.IsPlaying)
        {
            _audioPositionTimer.Stop();
            return;
        }

        await PersistAudioPositionAsync();
    }

    private async Task<bool> PersistAudioPositionAsync()
    {
        if (!_audioLoaded || _isSavingAudioPosition)
        {
            return false;
        }

        _isSavingAudioPosition = true;
        try
        {
            await _databaseService.SaveAudioProgressAsync(
                _record.MachineNum.ToString(),
                _audioPlayerService.CurrentPositionSeconds);
            return true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not save audio position for machine {MachineNumber}", _record.MachineNum);
            if (!_audioSaveWarningShown)
            {
                _audioSaveWarningShown = true;
                MessageBox.Show(
                    "Audio position could not be saved. Check the database connection. The app will keep retrying while audio plays.",
                    "Audio Progress",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return false;
        }
        finally
        {
            _isSavingAudioPosition = false;
        }
    }

    private void UpdateTimeLabel()
    {
        var current = TimeSpan.FromSeconds(_audioPlayerService.CurrentPositionSeconds);
        var total = TimeSpan.FromSeconds(_audioPlayerService.TotalSeconds);
        TimeText.Text = $"{FormatTime(current)} / {FormatTime(total)}";
    }

    private static string FormatTime(TimeSpan value)
    {
        return $"{value.TotalMinutes:00}:{value.Seconds:00}";
    }

    private void CompactModeButton_Click(object sender, RoutedEventArgs e)
    {
        _isCompact = !_isCompact;
        CaseDetailsPanel.Visibility = _isCompact ? Visibility.Collapsed : Visibility.Visible;
        DocumentPanel.Visibility = _isCompact ? Visibility.Collapsed : Visibility.Visible;
        WorkPanel.Visibility = _isCompact ? Visibility.Collapsed : Visibility.Visible;
        FooterPanel.Visibility = _isCompact ? Visibility.Collapsed : Visibility.Visible;
        CompactModeButton.Content = _isCompact ? "□ Expand" : "− Minimize";

        if (_isCompact)
        {
            LayoutGrid.RowDefinitions[4].Height = GridLength.Auto;
            MinHeight = 210;
            MinWidth = 550;
            Height = 225;
            Width = 580;
        }
        else
        {
            LayoutGrid.RowDefinitions[4].Height = new GridLength(1, GridUnitType.Star);
            MinHeight = 500;
            MinWidth = 660;
            Height = 550;
            Width = 740;
        }
    }

    private void AlwaysOnTopCheckBox_Changed(object sender, RoutedEventArgs e)
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
                MiniWindowAlwaysOnTop = Topmost
            });
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not save transcription window always-on-top preference");
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _audioPlayerService.SetVolume(VolumeSlider.Value);
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Math.Abs(SeekSlider.Value - _audioPlayerService.CurrentPositionSeconds) > 0.05)
        {
            _audioPlayerService.Seek(SeekSlider.Value);
            UpdateTimeLabel();
        }
    }

    private void RewindButton_Click(object sender, RoutedEventArgs e)
    {
        _audioPlayerService.Rewind();
        UpdateTimeLabel();
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        _audioPlayerService.Forward();
        UpdateTimeLabel();
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_audioPlayerService.IsPlaying)
        {
            _audioPlayerService.Pause();
            _playbackTimer.Stop();
            _audioPositionTimer.Stop();
            _ = PersistAudioPositionAsync();
            PlayPauseButton.Content = "▶ Play";
            return;
        }

        try
        {
            _audioPlayerService.Play();
            _playbackTimer.Start();
            _audioPositionTimer.Start();
            PlayPauseButton.Content = "⏸ Pause";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unable to start playback. {ex.Message}", "Playback error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void SaveProgressButton_Click(object sender, RoutedEventArgs e)
    {
        if (await SaveProgressAsync())
        {
            MessageBox.Show("Audio position and document progress were saved.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void FinishButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("Mark this transcription as finished?", "Confirm Finish", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _record.Remark = RemarkText.Text;
            var saved = await SaveProgressAsync();
            if (!saved)
            {
                return;
            }

            await _databaseService.UpdateRecordStatusAsync(_record.Id, "Finished", _record.Remark);
            _record.Status = "Finished";
            await CloseAfterSavingAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unable to finalize the task. {ex.Message}", "Finish error", MessageBoxButton.OK, MessageBoxImage.Warning);
            Serilog.Log.Error(ex, "Finish failed");
        }
    }

    private async Task<bool> SaveProgressAsync()
    {
        try
        {
            _record.Remark = RemarkText.Text;
            await _databaseService.SaveAudioProgressAsync(_record.MachineNum.ToString(), _audioPlayerService.CurrentPositionSeconds);
            if (_wordDocument != null)
            {
                ((dynamic)_wordDocument).Save();
            }
            return true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Save progress failed");
            MessageBox.Show($"Progress could not be saved. {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private async void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        await CloseAfterSavingAsync();
    }

    private void OpenDocumentButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            OpenDocumentForRecord();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not open transcription document for record {RecordId}", _record.Id);
            MessageBox.Show(
                $"The transcription document could not be created or opened.\n\n{ex.Message}",
                "Document Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenDocumentForRecord()
    {
        _openedDocumentPath = _wordDocumentService.GetDocumentPath(
            _record.FileNum.ToString(),
            _record.MachineNum.ToString());
        DocumentPathText.Text = _openedDocumentPath;
        _wordDocument = _wordDocumentService.OpenOrCreateDocument(
            _record.FileNum.ToString(),
            _record.MachineNum.ToString(),
            _record);
    }

    private void OpenDocumentsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _wordDocumentService.OpenDocumentsFolder(_openedDocumentPath);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not open transcription documents folder in Explorer");
            MessageBox.Show(
                $"The documents folder could not be opened.\n\n{ex.Message}",
                "Folder Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void InsertTimestampButton_Click(object sender, RoutedEventArgs e)
    {
        if (_wordDocument == null)
        {
            MessageBox.Show("Open the transcription document first before inserting a timestamp.", "Document Required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var timestamp = TimeSpan.FromSeconds(_audioPlayerService.CurrentPositionSeconds);
        _wordDocumentService.InsertTimestamp(_wordDocument, timestamp);
    }

    private void BringWordToFrontButton_Click(object sender, RoutedEventArgs e)
    {
        if (_wordDocument == null)
        {
            MessageBox.Show(
                "Open the transcription document first.",
                "Document Not Open",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            if (Topmost)
            {
                AlwaysOnTopCheckBox.IsChecked = false;
            }

            _wordDocumentService.BringWordToFront(_wordDocument);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not bring Word to the foreground");
            MessageBox.Show(
                ex.Message,
                "Microsoft Word",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void StatusComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isWindowInitialized || _isUpdatingStatus || _record == null || RemarkText == null)
        {
            return;
        }

        if (StatusComboBox.SelectedItem is ComboBoxItem item)
        {
            var selectedStatus = item.Content?.ToString();
            if (string.IsNullOrWhiteSpace(selectedStatus))
            {
                return;
            }

            _record.Remark = RemarkText.Text;
            _record.Status = selectedStatus;
            try
            {
                await _databaseService.UpdateRecordStatusAsync(_record.Id, selectedStatus, _record.Remark);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Could not update record {RecordId} to {Status}", _record.Id, selectedStatus);
                MessageBox.Show(
                    "The task status could not be updated. Check the database connection and try again.",
                    "Status Update Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    private async Task CloseAfterSavingAsync()
    {
        if (_closeSaveInProgress || _allowClose)
        {
            return;
        }

        _closeSaveInProgress = true;
        _audioPlayerService.Pause();

        if (!await SaveProgressAsync())
        {
            var closeAnyway = MessageBox.Show(
                "Progress could not be saved. Close this task anyway?",
                "Unsaved Progress",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (closeAnyway != MessageBoxResult.Yes)
            {
                _closeSaveInProgress = false;
                return;
            }
        }

        _allowClose = true;
        Close();
    }

    private void TranscriptionWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            _ = CloseAfterSavingAsync();
            return;
        }

        _playbackTimer.Stop();
        _audioPositionTimer.Stop();
        try
        {
            _wordDocumentService.SaveDocument(_wordDocument);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not save the transcription document while closing the task window");
        }

        _pedalService.Dispose();
        _audioPlayerService.Dispose();
    }
}
