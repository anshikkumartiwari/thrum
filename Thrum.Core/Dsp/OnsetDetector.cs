namespace Thrum.Core.Dsp;

/// <summary>
/// Detects sudden acoustic transients (chassis taps) using low-pass-filtered envelope energy,
/// adaptive noise-floor thresholding, pre-roll buffering, and refractory blanking.
/// </summary>
public sealed class OnsetDetector
{
    private readonly int _sampleRate;
    private readonly int _preRollSamples;
    private readonly int _postOnsetSamples;
    private readonly int _totalWindowSamples;
    private readonly int _refractorySamples;

    private float _envelope;
    private readonly float _iirAlpha;
    private int _samplesSinceLastOnset;
    private bool _isCapturingEvent;
    private int _capturedPostSamples;
    private readonly float[] _eventBuffer;

    private float _sensitivity; // 0.0 (least sensitive) to 1.0 (most sensitive)
    private float _noiseFloorAtOnset;

    public int SampleRate => _sampleRate;
    public int PreRollSamples => _preRollSamples;
    public int PostOnsetSamples => _postOnsetSamples;
    public int TotalWindowSamples => _totalWindowSamples;
    public float CurrentEnvelope => _envelope;

    /// <summary>
    /// Sensitivity value between 0.0 (requires firm tap) and 1.0 (triggers on light tap).
    /// Default is 0.5.
    /// </summary>
    public float Sensitivity
    {
        get => _sensitivity;
        set => _sensitivity = Math.Clamp(value, 0.0f, 1.0f);
    }

    /// <param name="sampleRate">Working audio sample rate (default: 16000 Hz).</param>
    /// <param name="preRollMs">Pre-roll duration before onset (default: 30 ms).</param>
    /// <param name="postOnsetMs">Capture duration after onset (default: 90 ms).</param>
    /// <param name="refractoryMs">Refractory duration where duplicate triggers are suppressed (default: 140 ms).</param>
    /// <param name="envelopeCutoffHz">Cutoff frequency for envelope low-pass filter (default: 250 Hz).</param>
    public OnsetDetector(
        int sampleRate = 16000,
        float preRollMs = 30f,
        float postOnsetMs = 90f,
        float refractoryMs = 140f,
        float envelopeCutoffHz = 250f)
    {
        _sampleRate = sampleRate;
        _preRollSamples = (int)(sampleRate * (preRollMs / 1000f));
        _postOnsetSamples = (int)(sampleRate * (postOnsetMs / 1000f));
        _totalWindowSamples = _preRollSamples + _postOnsetSamples;
        _refractorySamples = (int)(sampleRate * (refractoryMs / 1000f));

        _eventBuffer = new float[_totalWindowSamples];
        _samplesSinceLastOnset = _refractorySamples; // Allow immediate onset
        _sensitivity = 0.5f;

        // Discrete 1st-order IIR filter coefficient: alpha = 2*pi*fc / (2*pi*fc + fs)
        float rc = 1.0f / (2.0f * MathF.PI * envelopeCutoffHz);
        float dt = 1.0f / sampleRate;
        _iirAlpha = dt / (rc + dt);
    }

    public void Reset()
    {
        _envelope = 0f;
        _samplesSinceLastOnset = _refractorySamples;
        _isCapturingEvent = false;
        _capturedPostSamples = 0;
        Array.Clear(_eventBuffer, 0, _eventBuffer.Length);
    }

    /// <summary>
    /// Calculates the dynamic energy threshold given current noise floor and sensitivity.
    /// Sensitivity 0.0 -> 8.0x noise floor; 0.5 -> 4.5x; 1.0 -> 2.0x.
    /// </summary>
    public float GetThreshold(float currentNoiseFloor)
    {
        float multiplier = 8.0f - (_sensitivity * 6.0f);
        // Minimum absolute threshold to guard against dead silence triggering on noise
        return Math.Max(currentNoiseFloor * multiplier, 0.003f);
    }

    /// <summary>
    /// Feeds incoming audio samples into the onset detector.
    /// </summary>
    /// <param name="samples">Incoming mono samples.</param>
    /// <param name="ringBuffer">Ring buffer containing pre-roll history.</param>
    /// <param name="noiseFloor">Current noise floor tracker.</param>
    /// <param name="onCompleteEvent">Callback triggered when an onset window (pre-roll + post-onset) finishes capturing.</param>
    public void Process(
        ReadOnlySpan<float> samples,
        AudioRingBuffer ringBuffer,
        AdaptiveNoiseFloor noiseFloor,
        Action<float[], float> onCompleteEvent)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            float s = samples[i];
            float absSample = MathF.Abs(s);

            // Envelope low-pass filter
            _envelope = ((1.0f - _iirAlpha) * _envelope) + (_iirAlpha * absSample);
            _samplesSinceLastOnset++;

            if (_isCapturingEvent)
            {
                // Continue capturing the post-onset window
                int destIndex = _preRollSamples + _capturedPostSamples;
                if (destIndex < _eventBuffer.Length)
                {
                    _eventBuffer[destIndex] = s;
                }
                _capturedPostSamples++;

                if (_capturedPostSamples >= _postOnsetSamples)
                {
                    _isCapturingEvent = false;
                    float[] completedWindow = new float[_totalWindowSamples];
                    Array.Copy(_eventBuffer, completedWindow, _totalWindowSamples);
                    onCompleteEvent?.Invoke(completedWindow, _noiseFloorAtOnset);
                }
            }
            else
            {
                float threshold = GetThreshold(noiseFloor.CurrentNoiseFloor);

                // Onset condition: envelope exceeds threshold and refractory period has elapsed
                if (_envelope > threshold && _samplesSinceLastOnset >= _refractorySamples)
                {
                    _samplesSinceLastOnset = 0;
                    _isCapturingEvent = true;
                    _capturedPostSamples = 0;
                    _noiseFloorAtOnset = noiseFloor.CurrentNoiseFloor;

                    // Extract pre-roll audio from ring buffer
                    Span<float> preRollSpan = _eventBuffer.AsSpan(0, _preRollSamples);
                    int peeked = ringBuffer.PeekLatest(preRollSpan);
                    if (peeked < _preRollSamples)
                    {
                        // If ring buffer had fewer samples than preRoll, zero the head
                        preRollSpan.Slice(0, _preRollSamples - peeked).Clear();
                    }
                }
            }
        }
    }
}
