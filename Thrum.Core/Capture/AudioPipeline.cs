using NAudio.Wave;
using Thrum.Core.Classification;
using Thrum.Core.Dsp;

namespace Thrum.Core.Capture;

public enum PipelineMode
{
    Idle,
    Recording,
    Live
}

public sealed class AudioPipeline : IDisposable
{
    private readonly IAudioCapture _capture;
    private readonly AudioRingBuffer _ringBuffer;
    private readonly AudioFormatConverter _converter;
    private readonly AdaptiveNoiseFloor _noiseFloor;
    private readonly OnsetDetector _onsetDetector;
    private readonly ImpulseGate _impulseGate;
    private readonly FeatureExtractor _featureExtractor;

    private readonly Thread _processingThread;
    private readonly AutoResetEvent _dataSignal = new(false);
    private volatile bool _isRunning;

    // Resampling temporary buffers
    private float[] _decodeBuffer = new float[4096];
    private float[] _resampleBuffer = new float[4096];
    private readonly object _stateLock = new();

    // Mode state
    public PipelineMode Mode { get; private set; } = PipelineMode.Idle;
    public string? RecordingZoneId { get; private set; }
    public int RecordingTargetCount { get; private set; } = 15;
    public int RecordedSampleCount { get; private set; } = 0;

    public TapClassifier Classifier { get; set; } = new();

    // Events
    public event Action<float, float>? AudioLevelUpdated; // (currentLevel, noiseFloor)
    public event Action<ImpulseRejectionReason, string>? TapRejected;
    public event Action<float[], ImpulseGateResult>? TapAccepted;
    public event Action<string, int, int>? SampleRecorded; // (zoneId, currentCount, targetCount)
    public event Action<ClassificationResult>? TapClassified;

    public float CurrentNoiseFloor => _noiseFloor.CurrentNoiseFloor;
    public float Sensitivity
    {
        get => _onsetDetector.Sensitivity;
        set => _onsetDetector.Sensitivity = value;
    }

    public AudioPipeline(IAudioCapture? capture = null)
    {
        _capture = capture ?? new WasapiAudioCapture();
        _ringBuffer = new AudioRingBuffer(32000); // 2 seconds ring buffer
        _converter = new AudioFormatConverter(AudioFormatConverter.DefaultTargetSampleRate);
        _noiseFloor = new AdaptiveNoiseFloor();
        _onsetDetector = new OnsetDetector(AudioFormatConverter.DefaultTargetSampleRate);
        _impulseGate = new ImpulseGate(AudioFormatConverter.DefaultTargetSampleRate);
        _featureExtractor = new FeatureExtractor(AudioFormatConverter.DefaultTargetSampleRate);

        _capture.DataAvailable += OnAudioDataAvailable;

        _isRunning = true;
        _processingThread = new Thread(ProcessingLoop)
        {
            Name = "Thrum.AudioDspThread",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        _processingThread.Start();
    }

    public bool IsCapturing => _capture.IsCapturing;
    public string? CurrentDeviceId => _capture.CurrentDeviceId;

    public IReadOnlyList<AudioDeviceInfo> GetInputDevices() => _capture.GetInputDevices();

    public void StartCapture(string? deviceId = null)
    {
        if (_capture.IsCapturing && (string.IsNullOrEmpty(deviceId) || deviceId == _capture.CurrentDeviceId))
        {
            return; // Already capturing seamlessly
        }

        _ringBuffer.Clear();
        _noiseFloor.Reset();
        _onsetDetector.Reset();
        _converter.Reset();
        _capture.Start(deviceId);
    }

    public void StopCapture()
    {
        _capture.Stop();
        _ringBuffer.Clear();
    }

    public void SetModeIdle()
    {
        lock (_stateLock)
        {
            Mode = PipelineMode.Idle;
            RecordingZoneId = null;
        }
    }

    public void StartRecording(string zoneId, int targetCount = 15)
    {
        lock (_stateLock)
        {
            Mode = PipelineMode.Recording;
            RecordingZoneId = zoneId;
            RecordingTargetCount = targetCount;
            RecordedSampleCount = 0;
        }
    }

    public void StartLiveMode()
    {
        lock (_stateLock)
        {
            Mode = PipelineMode.Live;
            RecordingZoneId = null;
        }
    }

    private void OnAudioDataAvailable(byte[] rawBytes, int bytesRecorded, WaveFormat format)
    {
        if (bytesRecorded <= 0) return;

        // Ensure decode buffer size
        int neededFrames = bytesRecorded / (format.BlockAlign > 0 ? format.BlockAlign : 4);
        if (_decodeBuffer.Length < neededFrames)
        {
            _decodeBuffer = new float[neededFrames * 2];
        }

        // 1. Decode to mono float at native rate
        int decodedCount = AudioFormatConverter.DecodeToMonoFloat(
            rawBytes, bytesRecorded, format, _decodeBuffer);

        if (decodedCount <= 0) return;

        // 2. Resample to 16 kHz
        int targetRate = AudioFormatConverter.DefaultTargetSampleRate;
        int maxOutFrames = (int)Math.Ceiling(decodedCount * ((double)targetRate / format.SampleRate)) + 32;
        if (_resampleBuffer.Length < maxOutFrames)
        {
            _resampleBuffer = new float[maxOutFrames * 2];
        }

        int resampledCount = _converter.Resample(
            _decodeBuffer.AsSpan(0, decodedCount), format.SampleRate, _resampleBuffer);

        if (resampledCount > 0)
        {
            _ringBuffer.Write(_resampleBuffer.AsSpan(0, resampledCount));
            _dataSignal.Set();
        }
    }

    private void ProcessingLoop()
    {
        // 10ms frame at 16kHz is 160 samples
        const int frameSize = 160;
        float[] frame = new float[frameSize];

        while (_isRunning)
        {
            while (_ringBuffer.AvailableRead >= frameSize)
            {
                int read = _ringBuffer.Read(frame);
                if (read <= 0) break;

                Span<float> frameSpan = frame.AsSpan(0, read);

                // Update adaptive noise floor
                _noiseFloor.ProcessFrame(frameSpan);

                // Calculate current visual audio level (smoothed peak)
                float framePeak = 0f;
                for (int i = 0; i < read; i++)
                {
                    float a = MathF.Abs(frame[i]);
                    if (a > framePeak) framePeak = a;
                }

                AudioLevelUpdated?.Invoke(framePeak, _noiseFloor.CurrentNoiseFloor);

                // Feed into Onset Detector
                _onsetDetector.Process(frameSpan, _ringBuffer, _noiseFloor, OnOnsetCompleted);
            }

            _dataSignal.WaitOne(10);
        }
    }

    private void OnOnsetCompleted(float[] eventWindow, float noiseFloorAtOnset)
    {
        // 1. Evaluate with Impulse Gate
        int preRollSamples = _onsetDetector.PreRollSamples;
        var gateResult = _impulseGate.Evaluate(eventWindow, preRollSamples, noiseFloorAtOnset);

        if (!gateResult.IsAccepted)
        {
            TapRejected?.Invoke(gateResult.Reason, gateResult.RejectionMessage);
            return;
        }

        // 2. Extract 27-dimensional acoustic feature vector
        float[] featureVector = new float[_featureExtractor.FeatureCount];
        _featureExtractor.ExtractFeatures(eventWindow, preRollSamples, featureVector);

        TapAccepted?.Invoke(featureVector, gateResult);

        // 3. Dispatch according to active mode
        lock (_stateLock)
        {
            if (Mode == PipelineMode.Recording && RecordingZoneId != null)
            {
                RecordedSampleCount++;
                SampleRecorded?.Invoke(RecordingZoneId, RecordedSampleCount, RecordingTargetCount);
            }
            else if (Mode == PipelineMode.Live)
            {
                var result = Classifier.Classify(featureVector);
                TapClassified?.Invoke(result);
            }
        }
    }

    public void Dispose()
    {
        _isRunning = false;
        _dataSignal.Set();
        _capture.Stop();
        _capture.Dispose();
        _dataSignal.Dispose();
    }
}
