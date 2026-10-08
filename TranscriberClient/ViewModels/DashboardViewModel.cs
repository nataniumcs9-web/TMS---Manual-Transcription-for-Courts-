using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using TranscriberClient.Models;
using TranscriberClient.Services;

namespace TranscriberClient.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly DatabaseService _databaseService = new();
    private readonly LocalAudioAssignmentService _localAudioService = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private List<Record> _lastServerRecords = new();
    private string? _lastSnapshot;
    private string _searchText = string.Empty;

    public UserAccount CurrentUser { get; }

    public ObservableCollection<Record> AssignedRecords { get; } = new();
    public ObservableCollection<Record> PendingRecords { get; } = new();
    public ObservableCollection<Record> SuspendedRecords { get; } = new();
    public ObservableCollection<Record> FinishedRecords { get; } = new();
    public ObservableCollection<string> Recommendations { get; } = new();
    public ICollectionView AssignedView { get; }
    public ICollectionView PendingView { get; }
    public ICollectionView SuspendedView { get; }
    public ICollectionView FinishedView { get; }

    public int InProgressCount => PendingRecords.Count;
    public int AppointmentPassedCount => AssignedRecords.Concat(PendingRecords).Concat(SuspendedRecords)
        .Count(record => record.AppointedOn?.Date < DateTime.Today);
    public int AppointmentTodayCount => AssignedRecords.Concat(PendingRecords).Concat(SuspendedRecords)
        .Count(record => record.AppointedOn?.Date == DateTime.Today);
    public int SearchResultCount => AssignedView.Cast<Record>().Count()
        + PendingView.Cast<Record>().Count()
        + SuspendedView.Cast<Record>().Count()
        + FinishedView.Cast<Record>().Count();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                RefreshSearch();
            }
        }
    }

    [ObservableProperty]
    private string _lastUpdatedText = "Connecting…";

    public DashboardViewModel(UserAccount currentUser)
    {
        CurrentUser = currentUser;
        if (currentUser.IsOfflineSignIn)
        {
            LastUpdatedText = "Offline sign-in · loading local assignments";
        }
        AssignedView = CreateSearchView(AssignedRecords);
        PendingView = CreateSearchView(PendingRecords);
        SuspendedView = CreateSearchView(SuspendedRecords);
        FinishedView = CreateSearchView(FinishedRecords);
    }

    public async Task LoadAsync()
    {
        if (!await _loadLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            var localRecords = await _localAudioService.LoadForUserAsync(CurrentUser.Username);
            if (CurrentUser.IsOfflineSignIn || _lastServerRecords.Count == 0)
            {
                ApplyRecords(_lastServerRecords.Concat(localRecords).ToList());
            }
            var isOnline = true;
            List<Record> serverRecords;
            try
            {
                serverRecords = await _databaseService.GetRecordsForUserAsync(CurrentUser.Username);
            }
            catch (Exception ex)
            {
                isOnline = false;
                serverRecords = new List<Record>();
                Serilog.Log.Warning(ex, "Server assignments unavailable for {Username}; showing local audio assignments", CurrentUser.Username);
            }

            var records = serverRecords.Concat(localRecords).ToList();
            _lastServerRecords = serverRecords;
            ApplyRecords(records);

            LastUpdatedText = isOnline
                ? $"Connected · checked {DateTime.Now:t}"
                : $"No connection · local audio available · checked {DateTime.Now:t}";
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public IReadOnlyList<Record> GetAllRecords()
    {
        return AssignedRecords.Concat(PendingRecords).Concat(SuspendedRecords).Concat(FinishedRecords).ToList();
    }

    public void RefreshCalendarDisplay()
    {
        RefreshRecommendations();
        RefreshSearch();
    }

    private void ApplyRecords(IReadOnlyList<Record> records)
    {
        var snapshot = JsonSerializer.Serialize(records);
        if (string.Equals(snapshot, _lastSnapshot, StringComparison.Ordinal))
        {
            return;
        }

        ReplaceRecords(AssignedRecords, records.Where(record => record.Status == "Assigned"));
        ReplaceRecords(PendingRecords, records.Where(record => record.Status == "Pending"));
        ReplaceRecords(SuspendedRecords, records.Where(record => record.Status == "Suspended"));
        ReplaceRecords(FinishedRecords, records.Where(record => record.Status == "Finished"));
        _lastSnapshot = snapshot;
        OnPropertyChanged(nameof(InProgressCount));
        OnPropertyChanged(nameof(AppointmentPassedCount));
        OnPropertyChanged(nameof(AppointmentTodayCount));
        RefreshRecommendations();
        RefreshSearch();
    }

    private ICollectionView CreateSearchView(ObservableCollection<Record> records)
    {
        var view = CollectionViewSource.GetDefaultView(records);
        view.Filter = item => item is Record record && MatchesSearch(record);
        return view;
    }

    private void RefreshSearch()
    {
        AssignedView.Refresh();
        PendingView.Refresh();
        SuspendedView.Refresh();
        FinishedView.Refresh();
        OnPropertyChanged(nameof(SearchResultCount));
    }

    private bool MatchesSearch(Record record)
    {
        var terms = SearchText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
        {
            return true;
        }

        var searchable = string.Join(" ", new[]
        {
            record.Id.ToString(),
            record.FileNum.ToString(),
            record.MachineNum.ToString(),
            record.Applicant,
            record.Defendant,
            record.WitnessType,
            record.Witnesses,
            record.Trial,
            record.Judge,
            record.Audio,
            record.AudioStatus,
            record.Remark,
            record.Recorder,
            record.Transcriber,
            record.Status,
            record.RecDate?.ToString("d"),
            CalendarDateFormatter.FormatDate(record.RecDate),
            record.AppointedOn?.ToString("d"),
            CalendarDateFormatter.FormatDate(record.AppointedOn),
            record.InsertedOn?.ToString("d"),
            CalendarDateFormatter.FormatDate(record.InsertedOn),
            record.DistributedOn?.ToString("d"),
            CalendarDateFormatter.FormatDate(record.DistributedOn),
            record.FinishedDate?.ToString("d"),
            CalendarDateFormatter.FormatDate(record.FinishedDate)
        });

        return terms.All(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static void ReplaceRecords(ObservableCollection<Record> target, IEnumerable<Record> records)
    {
        target.Clear();
        foreach (var record in records)
        {
            target.Add(record);
        }
    }

    private void RefreshRecommendations()
    {
        Recommendations.Clear();
        var activeRecords = AssignedRecords.Concat(PendingRecords).Concat(SuspendedRecords).ToList();
        var overdue = activeRecords
            .Where(record => record.AppointedOn?.Date < DateTime.Today)
            .OrderBy(record => record.AppointedOn)
            .Take(4)
            .ToList();

        foreach (var record in overdue)
        {
            var daysPast = (DateTime.Today - record.AppointedOn!.Value.Date).Days;
            Recommendations.Add(
                $"Appointment passed {daysPast} day{(daysPast == 1 ? string.Empty : "s")} ago — File {record.FileNum}, Machine {record.MachineNum}. Review the assignment and prioritize the transcript.");
        }

        foreach (var record in activeRecords.Where(record => record.AppointedOn?.Date == DateTime.Today).Take(3))
        {
            Recommendations.Add(
                $"Appointment is today — File {record.FileNum}, Machine {record.MachineNum}. Review the case details and complete the transcript if possible.");
        }

        foreach (var record in activeRecords.Where(record => record.AppointedOn?.Date > DateTime.Today).OrderBy(record => record.AppointedOn).Take(3))
        {
            var daysUntil = (record.AppointedOn!.Value.Date - DateTime.Today).Days;
            Recommendations.Add(
                $"Appointment in {daysUntil} day{(daysUntil == 1 ? string.Empty : "s")} — File {record.FileNum}, Machine {record.MachineNum} ({CalendarDateFormatter.FormatDate(record.AppointedOn)}). Review the assignment and plan completion.");
        }

        if (Recommendations.Count == 0)
        {
            Recommendations.Add(activeRecords.Count == 0
                ? "No active assignments. New work appears automatically while this dashboard is open."
                : "No appointment dates need attention. Review assigned case details and keep your transcripts up to date.");
        }
    }
}
