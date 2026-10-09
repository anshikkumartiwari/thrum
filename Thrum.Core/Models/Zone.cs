namespace Thrum.Core.Models;

public sealed class Zone
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Area";
    public string Color { get; set; } = "#3B82F6"; // Default modern blue

    /// <summary>
    /// Relative horizontal position on the laptop diagram (0.0 to 1.0).
    /// </summary>
    public double X { get; set; } = 0.5;

    /// <summary>
    /// Relative vertical position on the laptop diagram (0.0 to 1.0).
    /// </summary>
    public double Y { get; set; } = 0.5;

    /// <summary>
    /// Action triggered when a tap in this zone is classified.
    /// </summary>
    public ZoneAction Action { get; set; } = new();

    /// <summary>
    /// Whether this zone represents the "Ignore" class (negative samples: typing, mouse clicks, speech).
    /// </summary>
    public bool IsIgnore { get; set; } = false;

    /// <summary>
    /// Number of recorded training tap samples for this zone.
    /// </summary>
    public int SampleCount { get; set; } = 0;
}

public sealed class TapFeatureSample
{
    public string ZoneId { get; set; } = string.Empty;
    public string ZoneName { get; set; } = string.Empty;
    public int TapIndex { get; set; } = 1;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Acoustic Diagnostic Metrics
    public float PeakAmplitude { get; set; }
    public float Rms { get; set; }
    public float CrestFactor { get; set; }
    public float LateEarlyEnergyRatio { get; set; }
    public float EffectiveDurationMs { get; set; }
    public float SnrRatio { get; set; }

    public float[] Features { get; set; } = Array.Empty<float>();

    public TapFeatureSample() { }

    public TapFeatureSample(string zoneId, float[] features)
    {
        ZoneId = zoneId;
        Features = features;
        Timestamp = DateTime.UtcNow;
    }

    public TapFeatureSample(
        string zoneId,
        string zoneName,
        int tapIndex,
        float[] features,
        float peak,
        float rms,
        float crest,
        float lateEarlyRatio,
        float durationMs,
        float snr)
    {
        ZoneId = zoneId;
        ZoneName = zoneName;
        TapIndex = tapIndex;
        Features = features;
        Timestamp = DateTime.UtcNow;
        PeakAmplitude = peak;
        Rms = rms;
        CrestFactor = crest;
        LateEarlyEnergyRatio = lateEarlyRatio;
        EffectiveDurationMs = durationMs;
        SnrRatio = snr;
    }
}
