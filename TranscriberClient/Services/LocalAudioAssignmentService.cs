using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Net.Http;
using TranscriberClient.Models;

namespace TranscriberClient.Services;

public sealed record OfflineCopyProgress(double Percentage, string Status);

public sealed class LocalAudioAssignmentService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly HttpClient HttpClient = new();

    public async Task<Record> CreateAsync(
        string sourcePath,
        int fileNumber,
        int machineNumber,
        string applicant,
        string defendant,
        string witnessType,
        string witnesses,
        string trial,
        string judge,
        DateTime? recordedOn,
        DateTime? appointedOn,
        string remark,
        string username)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The selected audio file no longer exists.", sourcePath);
        }

        if (machineNumber is < 1 or > 999999)
        {
            throw new InvalidOperationException("Machine number must contain five or six digits.");
        }

        var existingLocalRecords = await LoadAllLocalRecordsAsync();
        if (existingLocalRecords.Any(record => record.MachineNum == machineNumber))
        {
            throw new InvalidOperationException($"Machine number {machineNumber:D6} already exists in the local collection.");
        }

        var extension = Path.GetExtension(sourcePath);
        var audioName = $"{machineNumber:D6}-local-{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var audioPath = Path.Combine(AppSettings.LocalAudioFolder, audioName);
        var metadataPath = audioPath + ".tms.json";
        var localReference = RandomNumberGenerator.GetInt32(1, int.MaxValue);
        Directory.CreateDirectory(AppSettings.LocalAudioFolder);

        try
        {
            await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            await using (var destination = new FileStream(audioPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await source.CopyToAsync(destination);
            }

            var record = new Record
            {
                Id = -localReference,
                FileNum = fileNumber,
                MachineNum = machineNumber,
                Applicant = applicant,
                Defendant = defendant,
                WitnessType = witnessType,
                Witnesses = witnesses,
                Trial = trial,
                Judge = judge,
                Audio = audioName,
                LocalAudioFileName = audioName,
                AudioStatus = "Local audio",
                Remark = remark,
                RecDate = recordedOn?.Date,
                AppointedOn = appointedOn?.Date,
                InsertedOn = DateTime.Today,
                Recorder = username,
                Transcriber = username,
                Status = "Assigned",
                IsOfflineInfoDownloaded = true,
                HasOfflineAudio = true,
                IsLocalOnly = true
            };

            await SaveMetadataAsync(record);
            return record;
        }
        catch
        {
            DeleteIfExists(audioPath);
            DeleteIfExists(metadataPath);
            throw;
        }
    }

    public async Task<List<Record>> LoadForUserAsync(string username)
    {
        var records = new List<Record>();
        if (!Directory.Exists(AppSettings.LocalAudioFolder))
        {
            return records;
        }

        var allLocalRecords = await LoadAllLocalRecordsAsync();
        foreach (var record in allLocalRecords)
        {
            if (!string.Equals(record.Transcriber, username, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            records.Add(record);
        }

        return records.OrderByDescending(record => record.InsertedOn).ToList();
    }

    private async Task<List<Record>> LoadAllLocalRecordsAsync()
    {
        var records = new List<Record>();
        if (!Directory.Exists(AppSettings.LocalAudioFolder))
        {
            return records;
        }

        foreach (var metadataPath in Directory.EnumerateFiles(AppSettings.LocalAudioFolder, "*.tms.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                await using var stream = File.OpenRead(metadataPath);
                var record = await JsonSerializer.DeserializeAsync<Record>(stream, JsonOptions);
                if (record == null || record.Id == 0)
                {
                    continue;
                }

                record.IsLocalOnly = record.Id < 0;
                if (record.IsLocalOnly)
                {
                    record.IsOfflineInfoDownloaded = true;
                }

                var localAudioName = string.IsNullOrWhiteSpace(record.LocalAudioFileName)
                    ? (record.IsLocalOnly ? record.Audio : string.Empty)
                    : record.LocalAudioFileName;
                if (!string.IsNullOrWhiteSpace(localAudioName)
                    && !string.Equals(Path.GetFileName(localAudioName), localAudioName, StringComparison.Ordinal))
                {
                    Serilog.Log.Warning("Local record metadata {MetadataPath} contains an invalid audio filename", metadataPath);
                    continue;
                }

                if (record.IsLocalOnly && string.IsNullOrWhiteSpace(localAudioName))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(localAudioName)
                    && !File.Exists(Path.Combine(AppSettings.LocalAudioFolder, localAudioName)))
                {
                    Serilog.Log.Warning("Local audio metadata {MetadataPath} refers to a missing audio file", metadataPath);
                    record.HasOfflineAudio = false;
                    record.LocalAudioFileName = string.Empty;
                }
                else if (!string.IsNullOrWhiteSpace(localAudioName))
                {
                    record.LocalAudioFileName = localAudioName;
                    record.HasOfflineAudio = true;
                }

                records.Add(record);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                Serilog.Log.Warning(ex, "Could not read local audio assignment metadata {MetadataPath}", metadataPath);
            }
        }

        return records;
    }

    public async Task SaveOfflineInfoAsync(Record record)
    {
        if (record.Id <= 0)
        {
            throw new InvalidOperationException("Only a server assignment can be saved as an offline record.");
        }

        record.IsOfflineInfoDownloaded = true;
        await SaveMetadataAsync(record);
    }

    public async Task EnsureOfflineCopyAsync(Record record, IProgress<OfflineCopyProgress>? progress = null)
    {
        if (record.Id <= 0)
        {
            if (!record.IsLocalOnly
                || !record.IsOfflineInfoDownloaded
                || !record.HasOfflineAudio
                || !File.Exists(GetLocalAudioPath(record)))
            {
                throw new InvalidOperationException("The local assignment's record information or audio is unavailable.");
            }

            progress?.Report(new OfflineCopyProgress(100, "Available offline"));
            return;
        }

        await RefreshOfflineAvailabilityAsync(record);

        if (!record.IsOfflineInfoDownloaded)
        {
            progress?.Report(new OfflineCopyProgress(5, "Saving record information"));
            await SaveOfflineInfoAsync(record);
        }

        if (!record.HasOfflineAudio
            || string.IsNullOrWhiteSpace(record.LocalAudioFileName)
            || !File.Exists(Path.Combine(AppSettings.LocalAudioFolder, record.LocalAudioFileName)))
        {
            await DownloadAudioAsync(record, progress);
        }

        progress?.Report(new OfflineCopyProgress(100, "Available offline"));
    }

    public async Task RefreshOfflineAvailabilityAsync(Record record)
    {
        if (record.Id <= 0)
        {
            return;
        }

        var metadataPath = GetMetadataPath(record);
        if (!File.Exists(metadataPath))
        {
            record.IsOfflineInfoDownloaded = false;
            record.HasOfflineAudio = false;
            record.LocalAudioFileName = string.Empty;
            record.AudioSourceName = string.Empty;
            return;
        }

        Record? cachedRecord;
        try
        {
            await using var stream = File.OpenRead(metadataPath);
            cachedRecord = await JsonSerializer.DeserializeAsync<Record>(stream, JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(ex, "Could not validate cached record information at {MetadataPath}", metadataPath);
            MarkOfflineCopyUnavailable(record);
            return;
        }

        if (cachedRecord == null)
        {
            Serilog.Log.Warning("Cached record information is empty at {MetadataPath}", metadataPath);
            MarkOfflineCopyUnavailable(record);
            return;
        }

        if (cachedRecord.Id != record.Id)
        {
            Serilog.Log.Warning(
                "Cached record information at {MetadataPath} belongs to record {CachedRecordId}, not {RecordId}",
                metadataPath,
                cachedRecord.Id,
                record.Id);
            MarkOfflineCopyUnavailable(record);
            return;
        }

        record.IsOfflineInfoDownloaded = true;
        var currentAudioReference = record.Audio.Trim().Replace('\\', '/');
        var cachedAudioReference = cachedRecord.Audio.Trim().Replace('\\', '/');
        var expectedAudioName = Path.GetFileName(currentAudioReference);
        var cachedSourceName = string.IsNullOrWhiteSpace(cachedRecord.AudioSourceName)
            ? Path.GetFileName(cachedAudioReference)
            : cachedRecord.AudioSourceName;
        var cachedAudioName = cachedRecord.LocalAudioFileName;
        var audioIsValid = cachedRecord.HasOfflineAudio
            && string.Equals(cachedAudioReference, currentAudioReference, StringComparison.OrdinalIgnoreCase)
            && string.Equals(cachedSourceName, expectedAudioName, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(cachedAudioName)
            && string.Equals(Path.GetFileName(cachedAudioName), cachedAudioName, StringComparison.Ordinal)
            && File.Exists(Path.Combine(AppSettings.LocalAudioFolder, cachedAudioName));
        record.HasOfflineAudio = audioIsValid;
        record.LocalAudioFileName = audioIsValid ? cachedAudioName : string.Empty;
        record.AudioSourceName = audioIsValid ? cachedSourceName : string.Empty;
    }

    private static void MarkOfflineCopyUnavailable(Record record)
    {
        record.IsOfflineInfoDownloaded = false;
        record.HasOfflineAudio = false;
        record.LocalAudioFileName = string.Empty;
        record.AudioSourceName = string.Empty;
    }

    public async Task DownloadAudioAsync(Record record, IProgress<OfflineCopyProgress>? progress = null)
    {
        if (record.Id <= 0)
        {
            throw new InvalidOperationException("Only a server assignment can download server audio.");
        }

        var audioReference = record.Audio.Trim().Replace('\\', '/');
        var audioName = Path.GetFileName(audioReference);
        if (string.IsNullOrWhiteSpace(audioName)
            || audioReference.StartsWith("/", StringComparison.Ordinal)
            || audioReference.Split('/').Any(segment => segment is "." or ".."))
        {
            throw new InvalidOperationException("The server assignment contains an invalid audio filename.");
        }

        var machinePrefix = record.MachineNum.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        if (record.MachineNum is < 1 or > 999999
            || audioName.Length < 6
            || !string.Equals(audioName[..6], machinePrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Audio reference '{record.Audio}' does not begin with machine number {machinePrefix}. Verify this record's audio mapping before downloading.");
        }

        Directory.CreateDirectory(AppSettings.LocalAudioFolder);
        var extension = Path.GetExtension(audioName);
        var localAudioName = $"{record.MachineNum:D6}-{Guid.NewGuid():N}{extension}";
        var destinationPath = Path.Combine(AppSettings.LocalAudioFolder, localAudioName);
        var temporaryPath = destinationPath + ".download";
        var previousLocalAudioName = record.LocalAudioFileName;
        try
        {
            var baseUri = new Uri(AppSettings.AudioBaseUrl.EndsWith("/", StringComparison.Ordinal)
                ? AppSettings.AudioBaseUrl
                : AppSettings.AudioBaseUrl + "/", UriKind.Absolute);
            var encodedReference = string.Join(
                "/",
                audioReference.Split('/').Select(Uri.EscapeDataString));
            using var response = await HttpClient.GetAsync(
                new Uri(baseUri, encodedReference),
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var contentLength = response.Content.Headers.ContentLength;
            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long bytesReadTotal = 0;
                int bytesRead;
                progress?.Report(new OfflineCopyProgress(10, "Downloading audio"));
                while ((bytesRead = await source.ReadAsync(buffer)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead));
                    bytesReadTotal += bytesRead;
                    if (contentLength is > 0)
                    {
                        var percentage = 10 + (bytesReadTotal * 85d / contentLength.Value);
                        progress?.Report(new OfflineCopyProgress(Math.Min(95, percentage), "Downloading audio"));
                    }
                    else
                    {
                        progress?.Report(new OfflineCopyProgress(50, "Downloading audio"));
                    }
                }

                await destination.FlushAsync();
            }

            File.Move(temporaryPath, destinationPath);
            record.LocalAudioFileName = localAudioName;
            record.AudioSourceName = audioName;
            record.HasOfflineAudio = true;
            record.AudioStatus = "Downloaded";
            await SaveMetadataAsync(record);
            progress?.Report(new OfflineCopyProgress(98, "Saving offline copy"));
            try
            {
                DeleteLocalAudioIfSafe(previousLocalAudioName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Serilog.Log.Warning(ex, "Could not remove replaced cached audio {AudioFileName}", previousLocalAudioName);
            }
        }
        catch
        {
            DeleteIfExists(temporaryPath);
            DeleteIfExists(destinationPath);
            throw;
        }
    }

    public async Task SaveStatusAsync(Record record, string status, string remark)
    {
        if (!record.IsOfflineInfoDownloaded || !record.HasOfflineAudio)
        {
            throw new InvalidOperationException("Download this record's information and audio before starting or changing its offline work.");
        }

        record.Status = status;
        record.Remark = remark;
        record.HasPendingSync = !record.IsLocalOnly;
        if (status == "Finished")
        {
            record.FinishedDate ??= DateTime.Today;
        }

        await SaveMetadataAsync(record);
    }

    public Task SaveAudioPositionAsync(Record record, double positionSeconds)
    {
        if (!record.IsOfflineInfoDownloaded || !record.HasOfflineAudio)
        {
            throw new InvalidOperationException("Download this record's information and audio before saving offline progress.");
        }

        record.AudioPositionSeconds = Math.Max(0, positionSeconds);
        record.HasPendingSync = !record.IsLocalOnly;
        return SaveMetadataAsync(record);
    }

    public Task MarkSyncedAsync(Record record)
    {
        record.HasPendingSync = false;
        return SaveMetadataAsync(record);
    }

    public string GetLocalAudioPath(Record record)
    {
        var fileName = string.IsNullOrWhiteSpace(record.LocalAudioFileName)
            ? record.Audio
            : record.LocalAudioFileName;
        if (string.IsNullOrWhiteSpace(fileName)
            || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The local audio filename is invalid.");
        }

        return Path.Combine(AppSettings.LocalAudioFolder, fileName);
    }

    private static async Task SaveMetadataAsync(Record record)
    {
        var destination = GetMetadataPath(record);
        Directory.CreateDirectory(AppSettings.LocalAudioFolder);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, record, JsonOptions);
                await stream.FlushAsync();
            }

            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            DeleteIfExists(temporary);
        }
    }

    private static string GetMetadataPath(Record record)
    {
        var name = record.Id > 0
            ? $"record-{record.Id}.tms.json"
            : $"{record.Audio}.tms.json";
        return Path.Combine(AppSettings.LocalAudioFolder, name);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteLocalAudioIfSafe(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            return;
        }

        var path = Path.Combine(AppSettings.LocalAudioFolder, fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
