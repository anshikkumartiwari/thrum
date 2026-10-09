namespace Thrum.Core.Dsp;

/// <summary>
/// Tracks ambient background noise floor using an Exponential Moving Average (EMA).
/// Crucially, updates ONLY during quiet frames to prevent loud taps or speech from inflating the noise floor.
/// </summary>
public sealed class AdaptiveNoiseFloor
{
    private float _noiseFloor;
    private readonly float _alpha;
    private readonly float _quietMultiplier;
    private readonly float _minFloor;
    private readonly float _maxFloor;

    public float CurrentNoiseFloor => _noiseFloor;
    public float CurrentFrameRms { get; private set; }
    public bool LastFrameWasQuiet { get; private set; }

    /// <param name="initialFloor">Initial estimate of background noise floor.</param>
    /// <param name="alpha">EMA smoothing factor during quiet frames (default: 0.02 for slow adaptation).</param>
    /// <param name="quietMultiplier">Maximum ratio of frame RMS to noise floor to be considered quiet (default: 2.2x).</param>
    /// <param name="minFloor">Absolute minimum clamp to prevent zero/negative floor (default: 0.0001f).</param>
    /// <param name="maxFloor">Absolute maximum clamp (default: 0.5f).</param>
    public AdaptiveNoiseFloor(
        float initialFloor = 0.005f,
        float alpha = 0.02f,
        float quietMultiplier = 2.2f,
        float minFloor = 0.0001f,
        float maxFloor = 0.5f)
    {
        _noiseFloor = Math.Clamp(initialFloor, minFloor, maxFloor);
        _alpha = Math.Clamp(alpha, 0.001f, 1.0f);
        _quietMultiplier = quietMultiplier;
        _minFloor = minFloor;
        _maxFloor = maxFloor;
    }

    public void Reset(float floor = 0.005f)
    {
        _noiseFloor = Math.Clamp(floor, _minFloor, _maxFloor);
        CurrentFrameRms = 0f;
        LastFrameWasQuiet = true;
    }

    /// <summary>
    /// Processes a frame of audio samples and updates the noise floor if the frame is deemed quiet.
    /// </summary>
    /// <param name="frameSamples">Frame samples (e.g. 10ms - 20ms of audio).</param>
    /// <returns>True if frame was quiet and noise floor was updated; false if rejected as loud/active.</returns>
    public bool ProcessFrame(ReadOnlySpan<float> frameSamples)
    {
        if (frameSamples.IsEmpty) return false;

        float sumSquares = 0f;
        for (int i = 0; i < frameSamples.Length; i++)
        {
            float s = frameSamples[i];
            sumSquares += s * s;
        }

        float rms = MathF.Sqrt(sumSquares / frameSamples.Length);
        CurrentFrameRms = rms;

        // A frame is quiet if its RMS does not significantly exceed the current noise floor
        bool isQuiet = rms <= (_noiseFloor * _quietMultiplier);
        LastFrameWasQuiet = isQuiet;

        if (isQuiet)
        {
            // Update via slow EMA
            _noiseFloor = ((1.0f - _alpha) * _noiseFloor) + (_alpha * rms);
            _noiseFloor = Math.Clamp(_noiseFloor, _minFloor, _maxFloor);
        }
        else
        {
            // If the room actually got quieter and current noise floor is high, allow slow downward drift
            if (rms < _noiseFloor)
            {
                _noiseFloor = ((1.0f - _alpha) * _noiseFloor) + (_alpha * rms);
                _noiseFloor = Math.Clamp(_noiseFloor, _minFloor, _maxFloor);
            }
        }

        return isQuiet;
    }

    /// <summary>
    /// Computes RMS directly for any arbitrary sample slice.
    /// </summary>
    public static float CalculateRms(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return 0f;
        float sum = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            sum += samples[i] * samples[i];
        }
        return MathF.Sqrt(sum / samples.Length);
    }
}
