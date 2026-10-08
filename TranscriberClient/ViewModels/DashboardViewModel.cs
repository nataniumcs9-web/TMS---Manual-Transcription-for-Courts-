using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TranscriberClient.Models;
using TranscriberClient.Services;

namespace TranscriberClient.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly DatabaseService _databaseService = new();

    public UserAccount CurrentUser { get; }

    public ObservableCollection<Record> AssignedRecords { get; } = new();
    public ObservableCollection<Record> PendingRecords { get; } = new();
    public ObservableCollection<Record> SuspendedRecords { get; } = new();
    public ObservableCollection<Record> FinishedRecords { get; } = new();

    public DashboardViewModel(UserAccount currentUser)
    {
        CurrentUser = currentUser;
    }

    public async Task LoadAsync()
    {
        var assigned = await _databaseService.GetRecordsForUserAsync(CurrentUser.Username, "Assigned");
        var pending = await _databaseService.GetRecordsForUserAsync(CurrentUser.Username, "Pending");
        var suspended = await _databaseService.GetRecordsForUserAsync(CurrentUser.Username, "Suspended");
        var finished = await _databaseService.GetRecordsForUserAsync(CurrentUser.Username, "Finished");

        AssignedRecords.Clear();
        PendingRecords.Clear();
        SuspendedRecords.Clear();
        FinishedRecords.Clear();

        foreach (var record in assigned)
        {
            AssignedRecords.Add(record);
        }

        foreach (var record in pending)
        {
            PendingRecords.Add(record);
        }

        foreach (var record in suspended)
        {
            SuspendedRecords.Add(record);
        }

        foreach (var record in finished)
        {
            FinishedRecords.Add(record);
        }
    }
}
