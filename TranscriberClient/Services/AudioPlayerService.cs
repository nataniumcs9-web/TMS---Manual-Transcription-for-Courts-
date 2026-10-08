using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using NAudio.Wave;

namespace TranscriberClient.Services;

public class AudioPlayerService : IDisposable
{
    private WaveOutEvent? _waveOut;
    private WaveStream? _reader;
    private readonly HttpClient _httpClient = new();
    private string? _sourcePath;
    private bool _isDisposed;

    public event EventHandler<double>? PositionChanged;
    public event EventHandler<bool>? PlaybackStateChanged;

    public bool IsPlaying => _waveOut is { PlaybackState: PlaybackState.Playing };
    public double CurrentPositionSeconds => _reader?.CurrentTime.TotalSeconds ?? 0d;
    public double TotalSeconds => _reader?.TotalTime.TotalSeconds ?? 0d;

    public async Task LoadFromUrlAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("Audio URL is empty.");
        }

        var localPath = GetLocalAudioPath(url);
        if (!File.Exists(localPath))
        {
            await DownloadFileAsync(url, localPath);
        }

        _sourcePath = localPath;
        InitializeReader(localPath);
    }

    public Task LoadFromLocalAudioAsync(string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName) || !string.Equals(safeFileName, fileName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The assigned local audio filename is invalid.");
        }

        return LoadFromFileAsync(Path.Combine(AppSettings.LocalAudioFolder, safeFileName));
    }

    public Task LoadFromFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("The shared audio file could not be found.", filePath);
        }

        _sourcePath = filePath;
        InitializeReader(filePath);
        return Task.CompletedTask;
    }

    public void Play()
    {
        if (_waveOut == null || _reader == null)
        {
            return;
        }

        if (_waveOut.PlaybackState == PlaybackState.Paused)
        {
            _waveOut.Play();
            PlaybackStateChanged?.Invoke(this, true);
            return;
        }

        if (_waveOut.PlaybackState == PlaybackState.Stopped)
        {
            _waveOut.Play();
            PlaybackStateChanged?.Invoke(this, true);
        }
    }

    public void Pause()
    {
        if (_waveOut == null)
        {
            return;
        }

        _waveOut.Pause();
        PlaybackStateChanged?.Invoke(this, false);
    }

    public void SetVolume(double level)
    {
        if (_waveOut == null)
        {
            return;
        }

        _waveOut.Volume = (float)Math.Clamp(level, 0d, 1d);
    }

    public void Seek(double seconds)
    {
        if (_reader == null)
        {
            return;
        }

        var target = Math.Clamp(seconds, 0d, TotalSeconds);
        _reader.CurrentTime = TimeSpan.FromSeconds(target);
        PositionChanged?.Invoke(this, target);
    }

    public void Rewind()
    {
        Seek(CurrentPositionSeconds - 3d);
    }

    public void Forward()
    {
        Seek(CurrentPositionSeconds + 3d);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        _waveOut?.Stop();
        _waveOut?.Dispose();
        _reader?.Dispose();
        _httpClient.Dispose();
    }

    private void InitializeReader(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        WaveStream stream;
        try
        {
            stream = new MediaFoundationReader(filePath);
        }
        catch
        {
            stream = extension == ".wav"
                ? new WaveFileReader(filePath)
                : new Mp3FileReader(filePath);
        }

        _reader?.Dispose();
        _reader = stream;

        if (_waveOut == null)
        {
            _waveOut = new WaveOutEvent();
            _waveOut.PlaybackStopped += (_, _) => PlaybackStateChanged?.Invoke(this, false);
        }

        _waveOut.Init(_reader);
        _waveOut.Volume = 0.8f;
        PositionChanged?.Invoke(this, 0d);
    }

    private string GetLocalAudioPath(string url)
    {
        var normalized = new Uri(url).AbsolutePath;
        var fileName = Path.GetFileName(normalized);
        var folder = Path.Combine(Path.GetTempPath(), "TMSAudio");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, fileName);
    }

    private async Task DownloadFileAsync(string url, string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Unable to download audio file. HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = File.Create(destinationPath);
        await stream.CopyToAsync(fileStream);
    }
}
