namespace Thrum.Core.Classification;

/// <summary>
/// Stores mean and standard deviation for each feature dimension for z-score normalization.
/// </summary>
public sealed class FeatureStats
{
    public float[] Means { get; set; } = Array.Empty<float>();
    public float[] Stds { get; set; } = Array.Empty<float>();
    public int FeatureDimension => Means.Length;

    public FeatureStats() { }

    public FeatureStats(int dimension)
    {
        Means = new float[dimension];
        Stds = new float[dimension];
        Array.Fill(Stds, 1.0f);
    }

    public static FeatureStats Compute(IReadOnlyList<float[]> samples, int dimension)
    {
        var stats = new FeatureStats(dimension);
        if (samples.Count == 0) return stats;

        int n = samples.Count;
        float[] sum = new float[dimension];
        float[] sumSq = new float[dimension];

        for (int i = 0; i < n; i++)
        {
            var vector = samples[i];
            for (int d = 0; d < dimension; d++)
            {
                float val = vector[d];
                sum[d] += val;
                sumSq[d] += val * val;
            }
        }

        for (int d = 0; d < dimension; d++)
        {
            float mean = sum[d] / n;
            float variance = (sumSq[d] / n) - (mean * mean);
            float std = MathF.Sqrt(Math.Max(variance, 1e-6f));

            stats.Means[d] = mean;
            stats.Stds[d] = std;
        }

        return stats;
    }

    public void NormalizeInPlace(Span<float> vector)
    {
        int count = Math.Min(vector.Length, Means.Length);
        for (int d = 0; d < count; d++)
        {
            vector[d] = (vector[d] - Means[d]) / (Stds[d] + 1e-6f);
        }
    }

    public float[] Normalize(ReadOnlySpan<float> vector)
    {
        float[] result = new float[vector.Length];
        vector.CopyTo(result);
        NormalizeInPlace(result);
        return result;
    }
}
