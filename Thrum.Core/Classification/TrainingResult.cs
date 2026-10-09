namespace Thrum.Core.Classification;

public sealed class ConfusionMatrix
{
    public string[] ClassNames { get; set; } = Array.Empty<string>();
    public int[][] Matrix { get; set; } = Array.Empty<int[]>(); // [actual][predicted]

    public ConfusionMatrix() { }

    public ConfusionMatrix(string[] classNames)
    {
        ClassNames = classNames;
        int k = classNames.Length;
        Matrix = new int[k][];
        for (int i = 0; i < k; i++)
        {
            Matrix[i] = new int[k];
        }
    }

    public void Add(int actual, int predicted)
    {
        if (actual >= 0 && actual < Matrix.Length && predicted >= 0 && predicted < Matrix.Length)
        {
            Matrix[actual][predicted]++;
        }
    }
}

public sealed class TrainingResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public double AccuracyPercent { get; set; }
    public int TotalSamples { get; set; }
    public int TrainSamplesCount { get; set; }
    public int HeldOutSamplesCount { get; set; }
    public int RejectedCount { get; set; }
    public double MedianLatencyMs { get; set; }
    public ConfusionMatrix? ConfusionMatrix { get; set; }
}

public sealed class ClassificationResult
{
    public bool IsAccepted { get; set; }
    public string? MatchedZoneId { get; set; }
    public string? MatchedZoneName { get; set; }
    public float Confidence { get; set; }
    public float MahalanobisDistance { get; set; }
    public float DistanceThreshold { get; set; }
    public bool IsIgnoreZone { get; set; }
    public string? RejectionReason { get; set; }

    public static ClassificationResult Rejected(string reason, float confidence = 0f, float distance = 0f) =>
        new()
        {
            IsAccepted = false,
            RejectionReason = reason,
            Confidence = confidence,
            MahalanobisDistance = distance
        };

    public static ClassificationResult Accepted(
        string zoneId, string zoneName, float confidence, float distance, float threshold, bool isIgnore) =>
        new()
        {
            IsAccepted = true,
            MatchedZoneId = zoneId,
            MatchedZoneName = zoneName,
            Confidence = confidence,
            MahalanobisDistance = distance,
            DistanceThreshold = threshold,
            IsIgnoreZone = isIgnore
        };
}
