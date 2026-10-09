using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Thrum.Core.Capture;

public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault);

public interface IAudioCapture : IDisposable
{
    event Action<byte[], int, WaveFormat>? DataAvailable;
    event Action<Exception>? CaptureError;
    event Action? DeviceDisconnected;

    bool IsCapturing { get; }
    WaveFormat? Format { get; }
    string? CurrentDeviceId { get; }

    IReadOnlyList<AudioDeviceInfo> GetInputDevices();
    void Start(string? deviceId = null);
    void Stop();
}

public sealed class WasapiAudioCapture : IAudioCapture
{
    private WasapiCapture? _capture;
    private readonly object _lock = new();
    private bool _isCapturing;
    private string? _currentDeviceId;

    public event Action<byte[], int, WaveFormat>? DataAvailable;
    public event Action<Exception>? CaptureError;
    public event Action? DeviceDisconnected;

    public bool IsCapturing => _isCapturing;
    public WaveFormat? Format => _capture?.WaveFormat;
    public string? CurrentDeviceId => _currentDeviceId;

    public IReadOnlyList<AudioDeviceInfo> GetInputDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            MMDevice? defaultDevice = null;
            try
            {
                defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            }
            catch
            {
                // No default endpoint
            }

            var collection = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var dev in collection)
            {
                bool isDef = defaultDevice != null && dev.ID == defaultDevice.ID;
                devices.Add(new AudioDeviceInfo(dev.ID, dev.FriendlyName, isDef));
            }
        }
        catch (Exception ex)
        {
            CaptureError?.Invoke(ex);
        }

        return devices;
    }

    public void Start(string? deviceId = null)
    {
        lock (_lock)
        {
            Stop();

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                MMDevice? targetDevice = null;

                if (!string.IsNullOrEmpty(deviceId))
                {
                    try
                    {
                        targetDevice = enumerator.GetDevice(deviceId);
                    }
                    catch
                    {
                        // Fall back to default
                    }
                }

                targetDevice ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
                _currentDeviceId = targetDevice.ID;

                // Shared WASAPI capture mode
                _capture = new WasapiCapture(targetDevice)
                {
                    ShareMode = AudioClientShareMode.Shared
                };

                _capture.DataAvailable += (s, e) =>
                {
                    if (_isCapturing && e.BytesRecorded > 0)
                    {
                        DataAvailable?.Invoke(e.Buffer, e.BytesRecorded, _capture.WaveFormat);
                    }
                };

                _capture.RecordingStopped += (s, e) =>
                {
                    _isCapturing = false;
                    if (e.Exception != null)
                    {
                        CaptureError?.Invoke(e.Exception);
                    }
                    DeviceDisconnected?.Invoke();
                };

                _capture.StartRecording();
                _isCapturing = true;
            }
            catch (Exception ex)
            {
                _isCapturing = false;
                CaptureError?.Invoke(ex);
                throw;
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _isCapturing = false;
            if (_capture != null)
            {
                try
                {
                    _capture.StopRecording();
                }
                catch
                {
                    // Ignore during stop
                }
                finally
                {
                    _capture.Dispose();
                    _capture = null;
                }
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
