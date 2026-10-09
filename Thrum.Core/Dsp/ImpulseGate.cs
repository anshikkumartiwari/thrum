namespace Thrum.Core.Dsp;

public enum ImpulseRejectionReason
{
    None,
    TooQuiet,
    TooLong,
    SustainedSound,
    LowCrestFactor
}

public readonly struct ImpulseGateResult
{
    public bool IsAccepted => Reason == ImpulseRejectionReason.None;
    public ImpulseRejectionReason Reason { get; }
    public string RejectionMessage { get; }
    public float PeakAmplitude { get; }
    public float Rms { get; }
    public float CrestFactor { get; }
    public float LateEarlyEnergyRatio { get; }
    public float EffectiveDurationMs { get; }
    public float SnrRatio { get; }

    public ImpulseGateResult(
        ImpulseRejectionReason reason,
        string rejectionMessage,
        float peak,
        float rms,
        float crestFactor,
        float lateEarlyRatio,
        float effectiveDurationMs,
        float snrRatio)
    {
        Reason = reason;
        RejectionMessage = rejectionMessage;
        PeakAmplitude = peak;
        Rms = rms;
        CrestFactor = crestFactor;
        LateEarlyEnergyRatio = lateEarlyRatio;
        EffectiveDurationMs = effectiveDurationMs;
        SnrRatio = snrRatio;
    }

    public static ImpulseGateResult Accept(
        float peak, float rms, float crestFactor, float lateEarlyRatio, float effectiveDurationMs, float snrRatio) =>
        new(ImpulseRejectionReason.None, "Accepted", peak, rms, crestFactor, lateEarlyRatio, effectiveDurationMs, snrRatio);

    public static ImpulseGateResult Reject(
        ImpulseRejectionReason reason, string message,
        float peak, float rms, float crestFactor, float lateEarlyRatio, float effectiveDurationMs, float snrRatio) =>
        new(reason, message, peak, rms, crestFactor, lateEarlyRatio, effectiveDurationMs, snrRatio);
}

/// <summary>
/// Gating filter that distinguishes authentic mechanical impulses (chassis taps)
/// from sustained environmental sounds (speech, music, sustained key presses, hum).
/// </summary>
public sealed class ImpulseGate
{
    private readonly int _sampleRate;
    private readonly float _minPeakAmplitude;
    private readonly float _minSnrMultiplier;
    private readonly float _minCrestFactor;
    private readonly float _maxLateEarlyEnergyRatio;
    private readonly float _maxEffectiveDurationMs;

    public ImpulseGate(
        int sampleRate = 16000,
        float minPeakAmplitude = 0.0020f,
        float minSnrMultiplier = 1.5f,
        float minCrestFactor = 1.8f,
        float maxLateEarlyEnergyRatio = 0.90f,
        float maxEffectiveDurationMs = 95.0f)
    {
        _sampleRate = sampleRate;
        _minPeakAmplitude = minPeakAmplitude;
        _minSnrMultiplier = minSnrMultiplier;
        _minCrestFactor = minCrestFactor;
        _maxLateEarlyEnergyRatio = maxLateEarlyEnergyRatio;
        _maxEffectiveDurationMs = maxEffectiveDurationMs;
    }

    /// <summary>
    /// Analyzes a captured event window and evaluates impulse gate constraints.
    /// </summary>
    /// <param name="eventWindow">Total captured window (pre-roll + post-onset, ~120ms).</param>
    /// <param name="preRollSamples">Number of samples before the onset.</param>
    /// <param name="noiseFloor">Background noise floor estimate at the time of onset.</param>
    public ImpulseGateResult Evaluate(
        ReadOnlySpan<float> eventWindow,
        int preRollSamples,
        float noiseFloor)
    {
        if (eventWindow.IsEmpty)
            return ImpulseGateResult.Reject(ImpulseRejectionReason.TooQuiet, "Empty window", 0, 0, 0, 0, 0, 0);

        // 1. Peak & RMS
        float peak = 0f;
        float sumSquares = 0f;
        for (int i = 0; i < eventWindow.Length; i++)
        {
            float abs = MathF.Abs(eventWindow[i]);
            if (abs > peak) peak = abs;
            sumSquares += abs * abs;
        }

        float rms = MathF.Sqrt(sumSquares / eventWindow.Length);
        float crestFactor = rms > 1e-6f ? (peak / rms) : 0f;
        float snr = noiseFloor > 1e-6f ? (peak / noiseFloor) : 100f;

        // Check Minimum Signal Level (SNR & Absolute Peak)
        if (peak < _minPeakAmplitude || snr < _minSnrMultiplier)
        {
            return ImpulseGateResult.Reject(
                ImpulseRejectionReason.TooQuiet,
                $"Too quiet (peak {peak:0.000} < {_minPeakAmplitude:0.000})",
                peak, rms, crestFactor, 0f, 0f, snr);
        }

        // Check Crest Factor
        if (crestFactor < _minCrestFactor)
        {
            return ImpulseGateResult.Reject(
                ImpulseRejectionReason.LowCrestFactor,
                $"Low sharpness (crest {crestFactor:0.1} < {_minCrestFactor:0.1})",
                peak, rms, crestFactor, 0f, 0f, snr);
        }

        // 2. Late / Early Energy Ratio
        // Early window: from preRollSamples for 30ms (~480 samples at 16kHz)
        int postSamples = eventWindow.Length - preRollSamples;
        int earlyCount = Math.Min((int)(_sampleRate * 0.030f), postSamples / 2);
        int lateCount = postSamples - earlyCount;

        float earlyEnergy = 1e-7f;
        for (int i = 0; i < earlyCount; i++)
        {
            float s = eventWindow[preRollSamples + i];
            earlyEnergy += s * s;
        }

        float lateEnergy = 0f;
        for (int i = 0; i < lateCount; i++)
        {
            float s = eventWindow[preRollSamples + earlyCount + i];
            lateEnergy += s * s;
        }

        float lateEarlyRatio = lateEnergy / earlyEnergy;

        if (lateEarlyRatio > _maxLateEarlyEnergyRatio)
        {
            return ImpulseGateResult.Reject(
                ImpulseRejectionReason.SustainedSound,
                $"Sustained noise (ratio {lateEarlyRatio:0.2} > {_maxLateEarlyEnergyRatio:0.2})",
                peak, rms, crestFactor, lateEarlyRatio, 0f, snr);
        }

        // 3. Effective Duration: time during which amplitude remains > 20% of peak
        float threshold20Pct = peak * 0.20f;
        int activeSamples = 0;
        for (int i = preRollSamples; i < eventWindow.Length; i++)
        {
            if (MathF.Abs(eventWindow[i]) >= threshold20Pct)
            {
                activeSamples++;
            }
        }

        float durationMs = (activeSamples / (float)_sampleRate) * 1000f;

        if (durationMs > _maxEffectiveDurationMs)
        {
            return ImpulseGateResult.Reject(
                ImpulseRejectionReason.TooLong,
                $"Too long ({durationMs:0}ms > {_maxEffectiveDurationMs:0}ms)",
                peak, rms, crestFactor, lateEarlyRatio, durationMs, snr);
        }

        return ImpulseGateResult.Accept(peak, rms, crestFactor, lateEarlyRatio, durationMs, snr);
    }
}
