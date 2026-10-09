namespace Thrum.Core.Classification;

/// <summary>
/// Computes Mahalanobis distances against per-class centroids and variances to reject
/// unknown or out-of-distribution sounds (typing, coughs, ambient bangs) before triggering actions.
/// </summary>
public sealed class OutlierDetector
{
    public int NumClasses { get; set; }
    public int NumFeatures { get; set; }
    public float[][] Centroids { get; set; } = Array.Empty<float[]>();
    public float[][] Variances { get; set; } = Array.Empty<float[]>();
    public float[] MaxToleratedDistances { get; set; } = Array.Empty<float>();

    public OutlierDetector() { }

    public OutlierDetector(int numClasses, int numFeatures)
    {
        NumClasses = numClasses;
        NumFeatures = numFeatures;
        Centroids = new float[numClasses][];
        Variances = new float[numClasses][];
        MaxToleratedDistances = new float[numClasses];

        for (int i = 0; i < numClasses; i++)
        {
            Centroids[i] = new float[numFeatures];
            Variances[i] = new float[numFeatures];
            Array.Fill(Variances[i], 1.0f);
            MaxToleratedDistances[i] = 9.0f; // Default threshold
        }
    }

    public static OutlierDetector Fit(
        IReadOnlyList<float[]> xNormalized,
        IReadOnlyList<int> y,
        int numClasses,
        int numFeatures)
    {
        var detector = new OutlierDetector(numClasses, numFeatures);

        // Group by class
        var classCounts = new int[numClasses];
        for (int i = 0; i < xNormalized.Count; i++)
        {
            int c = y[i];
            classCounts[c]++;
            var sample = xNormalized[i];
            for (int d = 0; d < numFeatures; d++)
            {
                detector.Centroids[c][d] += sample[d];
            }
        }

        // Compute centroids
        for (int c = 0; c < numClasses; c++)
        {
            int count = Math.Max(1, classCounts[c]);
            for (int d = 0; d < numFeatures; d++)
            {
                detector.Centroids[c][d] /= count;
            }
        }

        // Compute diagonal variances
        for (int i = 0; i < xNormalized.Count; i++)
        {
            int c = y[i];
            var sample = xNormalized[i];
            for (int d = 0; d < numFeatures; d++)
            {
                float diff = sample[d] - detector.Centroids[c][d];
                detector.Variances[c][d] += diff * diff;
            }
        }

        for (int c = 0; c < numClasses; c++)
        {
            int count = Math.Max(1, classCounts[c]);
            for (int d = 0; d < numFeatures; d++)
            {
                float rawVar = detector.Variances[c][d] / count;
                // Shrinkage towards global normalized variance (1.0) with shrinkage factor 0.35
                float regularizedVar = (0.65f * rawVar) + (0.35f * 1.0f);
                detector.Variances[c][d] = regularizedVar;
            }
        }

        // Auto-calibrate distance thresholds based on training samples
        for (int c = 0; c < numClasses; c++)
        {
            float maxTrainDist = 0f;
            for (int i = 0; i < xNormalized.Count; i++)
            {
                if (y[i] == c)
                {
                    float dist = detector.ComputeDistance(xNormalized[i], c);
                    if (dist > maxTrainDist) maxTrainDist = dist;
                }
            }

            // Margin multiplier (2.5x max training distance or default 15.0 for 27 dimensions)
            detector.MaxToleratedDistances[c] = Math.Max(maxTrainDist * 2.5f, 15.0f);
        }

        return detector;
    }

    /// <summary>
    /// Computes diagonal Mahalanobis distance of a normalized vector to the centroid of class k.
    /// </summary>
    public float ComputeDistance(ReadOnlySpan<float> normalizedVector, int classIndex)
    {
        if (classIndex < 0 || classIndex >= NumClasses) return float.MaxValue;

        var centroid = Centroids[classIndex];
        var variance = Variances[classIndex];

        float sum = 0f;
        int dCount = Math.Min(normalizedVector.Length, NumFeatures);
        for (int d = 0; d < dCount; d++)
        {
            float diff = normalizedVector[d] - centroid[d];
            sum += (diff * diff) / variance[d];
        }

        return MathF.Sqrt(sum);
    }

    /// <summary>
    /// Checks whether the sample is an outlier with respect to the given class.
    /// </summary>
    public bool IsOutlier(ReadOnlySpan<float> normalizedVector, int classIndex, out float distance, out float threshold)
    {
        distance = ComputeDistance(normalizedVector, classIndex);
        threshold = (classIndex >= 0 && classIndex < NumClasses)
            ? MaxToleratedDistances[classIndex]
            : 8.0f;

        return distance > threshold;
    }
}
