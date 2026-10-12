using TranscriberClient.Models;

namespace TranscriberClient.Services;

public sealed class OfflineSyncService
{
    private readonly DatabaseService _databaseService;
    private readonly LocalAudioAssignmentService _localAudioService;

    public OfflineSyncService(DatabaseService? databaseService = null, LocalAudioAssignmentService? localAudioService = null)
    {
        _databaseService = databaseService ?? new DatabaseService();
        _localAudioService = localAudioService ?? new LocalAudioAssignmentService();
    }

    public async Task<bool> SyncRecordAsync(Record record, string username)
    {
        if (record.IsLocalOnly || !record.HasPendingSync)
        {
            return false;
        }

        var updated = await _databaseService.UpdateRecordStatusAsync(
            record.Id,
            username,
            record.Status,
            record.Remark);
        if (!updated)
        {
            throw new InvalidOperationException(
                $"Record {record.Id} is no longer assigned to {username}; its local changes were kept.");
        }

        await _databaseService.SaveAudioProgressAsync(
            record.MachineNum.ToString(),
            record.AudioPositionSeconds);
        await _localAudioService.MarkSyncedAsync(record);
        return true;
    }
}
