using System.Text.Json.Serialization;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TranscriberClient.Models;

public class Record : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _isOfflineInfoDownloaded;
    private bool _hasOfflineAudio;
    private bool _isOfflineDownloadInProgress;
    private double _offlineDownloadProgress;
    private string _offlineDownloadStatus = string.Empty;
    private int _localRollNumber;

    public int Id { get; set; }
    public DateTime? RecDate { get; set; }
    public int FileNum { get; set; }
    public int MachineNum { get; set; }
    public string Applicant { get; set; } = string.Empty;
    public string Defendant { get; set; } = string.Empty;
    public string WitnessType { get; set; } = string.Empty;
    public string Witnesses { get; set; } = string.Empty;
    public string Trial { get; set; } = string.Empty;
    public string Judge { get; set; } = string.Empty;
    public string Audio { get; set; } = string.Empty;
    public string LocalAudioFileName { get; set; } = string.Empty;
    public string AudioSourceName { get; set; } = string.Empty;
    public string AudioStatus { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public DateTime? AppointedOn { get; set; }
    public DateTime? InsertedOn { get; set; }
    public string Recorder { get; set; } = string.Empty;
    public string Transcriber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? DistributedOn { get; set; }
    public DateTime? FinishedDate { get; set; }
    public string Doc { get; set; } = string.Empty;
    public double AudioPositionSeconds { get; set; }
    [JsonIgnore]
    public int LocalRollNumber
    {
        get => _localRollNumber;
        set => SetProperty(ref _localRollNumber, value);
    }
    public bool IsOfflineInfoDownloaded
    {
        get => _isOfflineInfoDownloaded;
        set
        {
            if (SetProperty(ref _isOfflineInfoDownloaded, value))
            {
                OnPropertyChanged(nameof(OfflineCopyButtonLabel));
            }
        }
    }
    public bool HasOfflineAudio
    {
        get => _hasOfflineAudio;
        set
        {
            if (SetProperty(ref _hasOfflineAudio, value))
            {
                OnPropertyChanged(nameof(OfflineCopyButtonLabel));
            }
        }
    }
    public bool HasPendingSync { get; set; }
    [JsonIgnore]
    public bool IsOfflineDownloadInProgress
    {
        get => _isOfflineDownloadInProgress;
        set
        {
            if (SetProperty(ref _isOfflineDownloadInProgress, value))
            {
                OnPropertyChanged(nameof(OfflineCopyButtonLabel));
            }
        }
    }
    [JsonIgnore]
    public double OfflineDownloadProgress
    {
        get => _offlineDownloadProgress;
        set => SetProperty(ref _offlineDownloadProgress, value);
    }
    [JsonIgnore]
    public string OfflineDownloadStatus
    {
        get => _offlineDownloadStatus;
        set => SetProperty(ref _offlineDownloadStatus, value);
    }
    [JsonIgnore]
    public string OfflineCopyButtonLabel => IsOfflineInfoDownloaded && HasOfflineAudio
        ? "\uE73E"
        : "\uE896";
    [JsonIgnore]
    public string OfflineCopyToolTip => IsOfflineDownloadInProgress
        ? OfflineDownloadStatus
        : IsOfflineInfoDownloaded && HasOfflineAudio
            ? "Available offline"
            : "Download record information and audio";
    [JsonIgnore]
    public bool IsLocalOnly { get; set; }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
