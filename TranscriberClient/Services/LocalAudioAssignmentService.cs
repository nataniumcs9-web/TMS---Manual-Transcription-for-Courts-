using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using TranscriberClient.Models;

namespace TranscriberClient.Services;

public sealed class LocalAudioAssignmentService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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

        var extension = Path.GetExtension(sourcePath);
        var audioName = $"local-{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var audioPath = Path.Combine(AppSettings.LocalAudioFolder, audioName);
        var metadataPath = GetMetadataPath(audioPath);
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
                MachineNum = machineNumber == 0 ? localReference : machineNumber,
                Applicant = applicant,
                Defendant = defendant,
                WitnessType = witnessType,
                Witnesses = witnesses,
                Trial = trial,
                Judge = judge,
                Audio = audioName,
                AudioStatus = "Local audio",
                Remark = remark,
                RecDate = recordedOn?.Date,
                AppointedOn = appointedOn?.Date,
                InsertedOn = DateTime.Today,
                Recorder = username,
                Transcriber = username,
                Status = "Assigned",
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

        foreach (var metadataPath in Directory.EnumerateFiles(AppSettings.LocalAudioFolder, "*.tms.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                await using var stream = File.OpenRead(metadataPath);
                var record = await JsonSerializer.DeserializeAsync<Record>(stream, JsonOptions);
                if (record == null
                    || !string.Equals(record.Transcriber, username, StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(record.Audio)
                    || !string.Equals(Path.GetFileName(record.Audio), record.Audio, StringComparison.Ordinal))
                {
                    continue;
                }

                var audioPath = Path.Combine(AppSettings.LocalAudioFolder, record.Audio);
                if (!File.Exists(audioPath))
                {
                    Serilog.Log.Warning("Local audio metadata {MetadataPath} refers to a missing audio file", metadataPath);
                    continue;
                }

                record.IsLocalOnly = true;
                records.Add(record);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                Serilog.Log.Warning(ex, "Could not read local audio assignment metadata {MetadataPath}", metadataPath);
            }
        }

        return records.OrderByDescending(record => record.InsertedOn).ToList();
    }

    public async Task SaveStatusAsync(Record record, string status, string remark)
    {
        if (!record.IsLocalOnly)
        {
            throw new InvalidOperationException("Only local audio assignments can be updated without the server.");
        }

        record.Status = status;
        record.Remark = remark;
        if (status == "Finished")
        {
            record.FinishedDate ??= DateTime.Today;
        }

        await SaveMetadataAsync(record);
    }

    public Task SaveAudioPositionAsync(Record record, double positionSeconds)
    {
        if (!record.IsLocalOnly)
        {
            throw new InvalidOperationException("Only local audio assignments can save progress without the server.");
        }

        record.AudioPositionSeconds = Math.Max(0, positionSeconds);
        return SaveMetadataAsync(record);
    }

    private static async Task SaveMetadataAsync(Record record)
    {
        var destination = GetMetadataPath(Path.Combine(AppSettings.LocalAudioFolder, record.Audio));
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

    private static string GetMetadataPath(string audioPath) => audioPath + ".tms.json";

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
