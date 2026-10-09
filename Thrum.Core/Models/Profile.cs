using Thrum.Core.Classification;

namespace Thrum.Core.Models;

public sealed class Profile
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Default Profile";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastTrainedAt { get; set; }
    public double? TrainingAccuracy { get; set; }

    public List<Zone> Zones { get; set; } = new();
    public List<TapFeatureSample> TrainingSamples { get; set; } = new();

    // Trained model parameters
    public FeatureStats? FeatureStats { get; set; }
    public float[]? ModelWeights { get; set; }
    public float[]? ModelBiases { get; set; }
    public float[][]? OutlierCentroids { get; set; }
    public float[][]? OutlierVariances { get; set; }
    public float[]? OutlierThresholds { get; set; }
    public List<string>? TrainedZoneIds { get; set; }

    /// <summary>
    /// Restores a trained TapClassifier instance from this profile if weights exist.
    /// </summary>
    public TapClassifier? RestoreClassifier()
    {
        if (FeatureStats == null || OutlierCentroids == null || OutlierVariances == null ||
            OutlierThresholds == null || TrainedZoneIds == null || TrainedZoneIds.Count < 1)
        {
            return null;
        }

        var zonesById = Zones.ToDictionary(z => z.Id);
        var trainedZones = new List<Zone>();
        foreach (var zid in TrainedZoneIds)
        {
            if (zonesById.TryGetValue(zid, out var z))
            {
                trainedZones.Add(z);
            }
            else
            {
                return null; // A trained zone was removed
            }
        }

        var classifier = new TapClassifier
        {
            Stats = FeatureStats,
            Model = (ModelWeights != null && ModelBiases != null) ? new MultinomialLogisticRegression
            {
                NumClasses = trainedZones.Count,
                NumFeatures = FeatureStats.FeatureDimension,
                Weights = (float[])ModelWeights.Clone(),
                Biases = (float[])ModelBiases.Clone()
            } : null,
            OutlierDetector = new OutlierDetector
            {
                NumClasses = trainedZones.Count,
                NumFeatures = FeatureStats.FeatureDimension,
                Centroids = OutlierCentroids,
                Variances = OutlierVariances,
                MaxToleratedDistances = OutlierThresholds
            },
            TrainedZones = trainedZones
        };

        // Reflect trained status
        typeof(TapClassifier).GetProperty(nameof(TapClassifier.IsTrained))?
            .SetValue(classifier, true);

        return classifier;
    }

    /// <summary>
    /// Saves trained classifier parameters into this profile.
    /// </summary>
    public void SaveClassifier(TapClassifier classifier, double accuracy)
    {
        if (!classifier.IsTrained || classifier.Stats == null || classifier.OutlierDetector == null) return;

        FeatureStats = classifier.Stats;
        ModelWeights = classifier.Model != null ? (float[])classifier.Model.Weights.Clone() : null;
        ModelBiases = classifier.Model != null ? (float[])classifier.Model.Biases.Clone() : null;
        OutlierCentroids = classifier.OutlierDetector.Centroids;
        OutlierVariances = classifier.OutlierDetector.Variances;
        OutlierThresholds = classifier.OutlierDetector.MaxToleratedDistances;
        TrainedZoneIds = classifier.TrainedZones.Select(z => z.Id).ToList();

        LastTrainedAt = DateTime.UtcNow;
        TrainingAccuracy = accuracy;
    }
}
