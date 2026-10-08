using HidSharp;
using Serilog;

namespace TranscriberClient.Services;

public class PedalService : IDisposable
{
    private const int PhilipsVendorId = 0x0911;
    private const int Acc2330ProductId = 0x091A;
    private const byte RewindMask = 0x04;
    private const byte PlayPauseMask = 0x02;
    private const byte ForwardMask = 0x01;

    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private readonly object _syncLock = new();

    public event Action? RewindPressed;
    public event Action? PlayPausePressed;
    public event Action? ForwardPressed;
    public event Action<bool>? ConnectionChanged;

    public bool IsConnected { get; private set; }

    public void Start()
    {
        lock (_syncLock)
        {
            if (_cts is { IsCancellationRequested: false })
            {
                return;
            }

            var cancellation = new CancellationTokenSource();
            _cts = cancellation;
            _listenTask = Task.Run(() => ListenAsync(cancellation.Token));
        }
    }

    public void Stop()
    {
        lock (_syncLock)
        {
            _cts?.Cancel();
            _cts = null;
        }
    }

    private async Task ListenAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var device = DeviceList.Local
                    .GetHidDevices(PhilipsVendorId, Acc2330ProductId)
                    .FirstOrDefault(candidate => candidate.GetMaxInputReportLength() > 0);

                if (device == null)
                {
                    UpdateConnectionState(false);
                    await Task.Delay(3000, token);
                    continue;
                }

                using var stream = device.Open();
                stream.ReadTimeout = 1000;
                var buffer = new byte[device.GetMaxInputReportLength()];
                byte previousButtons = 0;
                UpdateConnectionState(true);
                Log.Information("Philips ACC2330 foot pedal connected; input report size {ReportSize}", buffer.Length);

                while (!token.IsCancellationRequested)
                {
                    int bytesRead;
                    try
                    {
                        bytesRead = stream.Read(buffer, 0, buffer.Length);
                    }
                    catch (TimeoutException)
                    {
                        continue;
                    }

                    if (bytesRead <= 0)
                    {
                        break;
                    }

                    byte currentButtons = 0;
                    for (var index = 0; index < bytesRead; index++)
                    {
                        currentButtons |= (byte)(buffer[index] & (RewindMask | PlayPauseMask | ForwardMask));
                    }

                    var newlyPressed = (byte)(currentButtons & ~previousButtons);
                    if ((newlyPressed & RewindMask) != 0)
                    {
                        RewindPressed?.Invoke();
                        Log.Debug("Foot pedal rewind pressed");
                    }

                    if ((newlyPressed & PlayPauseMask) != 0)
                    {
                        PlayPausePressed?.Invoke();
                        Log.Debug("Foot pedal play/pause pressed");
                    }

                    if ((newlyPressed & ForwardMask) != 0)
                    {
                        ForwardPressed?.Invoke();
                        Log.Debug("Foot pedal forward pressed");
                    }

                    previousButtons = currentButtons;
                }

                UpdateConnectionState(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Philips ACC2330 foot pedal listener disconnected; retrying");
                UpdateConnectionState(false);
                try
                {
                    await Task.Delay(3000, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        UpdateConnectionState(false);
    }

    private void UpdateConnectionState(bool connected)
    {
        if (IsConnected == connected)
        {
            return;
        }

        lock (_syncLock)
        {
            IsConnected = connected;
        }

        ConnectionChanged?.Invoke(connected);
    }

    public void Dispose()
    {
        Stop();
    }
}
