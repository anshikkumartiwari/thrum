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
    public float[] Features { get; set; } = Array.Empty<float>();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public TapFeatureSample() { }

    public TapFeatureSample(string zoneId, float[] features)
    {
        ZoneId = zoneId;
        Features = features;
        Timestamp = DateTime.UtcNow;
    }
}
