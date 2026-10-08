using CommunityToolkit.Mvvm.ComponentModel;
using TranscriberClient.Models;

namespace TranscriberClient.ViewModels;

public partial class TranscriptionViewModel : ObservableObject
{
    public Record Record { get; }
    public UserAccount CurrentUser { get; }

    [ObservableProperty]
    private string _status = "Pending";

    [ObservableProperty]
    private string _timeLabel = "00:00 / 00:00";

    [ObservableProperty]
    private double _volume = 0.8;

    [ObservableProperty]
    private double _seekPosition;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isWordReady;

    [ObservableProperty]
    private bool _isPedalConnected;

    public TranscriptionViewModel(Record record, UserAccount currentUser)
    {
        Record = record;
        CurrentUser = currentUser;
        Status = string.IsNullOrWhiteSpace(record.Status) ? "Pending" : record.Status;
    }

    public string WindowTitle => $"Transcription — {Record.MachineNum}";
}
