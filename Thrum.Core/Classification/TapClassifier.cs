using System.Diagnostics;
using Thrum.Core.Models;

namespace Thrum.Core.Classification;

/// <summary>
/// Acoustic classifier for chassis tap zones with z-score normalization,
/// regularized multinomial logistic regression, and Mahalanobis outlier rejection.
/// </summary>
public sealed class TapClassifier
{
    public float MinConfidenceThreshold { get; set; } = 0.58f;
    public bool IsTrained { get; private set; }

    public FeatureStats? Stats { get; set; }
    public MultinomialLogisticRegression? Model { get; set; }
    public OutlierDetector? OutlierDetector { get; set; }
    public List<Zone> TrainedZones { get; set; } = new();

    public TrainingResult Train(IReadOnlyList<Zone> zones, IReadOnlyList<TapFeatureSample> samples)
    {
        if (zones.Count < 2)
        {
            return new TrainingResult
            {
                Success = false,
                Message = "At least 2 zones are required for classification."
            };
        }

        // Validate that each zone has at least 10 samples (except ignore if optional)
        var zoneSampleMap = new Dictionary<string, List<float[]>>();
        foreach (var z in zones)
        {
            zoneSampleMap[z.Id] = new List<float[]>();
        }

        foreach (var sample in samples)
        {
            if (zoneSampleMap.TryGetValue(sample.ZoneId, out var list))
            {
                list.Add(sample.Features);
            }
        }

        foreach (var z in zones)
        {
            if (zoneSampleMap[z.Id].Count < 10)
            {
                return new TrainingResult
                {
                    Success = false,
                    Message = $"Zone '{z.Name}' has {zoneSampleMap[z.Id].Count} taps. Need ≥ 10 taps each."
                };
            }
        }

        TrainedZones = zones.ToList();
        int numClasses = TrainedZones.Count;
        int numFeatures = samples[0].Features.Length;

        // Perform stratified 70% Train / 30% Held-Out Split
        var trainX = new List<float[]>();
        var trainY = new List<int>();
        var testX = new List<float[]>();
        var testY = new List<int>();

        var random = new Random(42);

        for (int c = 0; c < numClasses; c++)
        {
            var classFeatures = zoneSampleMap[TrainedZones[c].Id].OrderBy(_ => random.Next()).ToList();
            int heldOutCount = Math.Max(1, (int)Math.Round(classFeatures.Count * 0.30));
            int trainCount = classFeatures.Count - heldOutCount;

            for (int i = 0; i < trainCount; i++)
            {
                trainX.Add(classFeatures[i]);
                trainY.Add(c);
            }
            for (int i = trainCount; i < classFeatures.Count; i++)
            {
                testX.Add(classFeatures[i]);
                testY.Add(c);
            }
        }

        // 1. Compute Feature Normalization Stats from Training Split
        Stats = FeatureStats.Compute(trainX, numFeatures);

        // Normalize Train and Test features
        var trainXNorm = new List<float[]>(trainX.Count);
        for (int i = 0; i < trainX.Count; i++)
        {
            trainXNorm.Add(Stats.Normalize(trainX[i]));
        }

        var testXNorm = new List<float[]>(testX.Count);
        for (int i = 0; i < testX.Count; i++)
        {
            testXNorm.Add(Stats.Normalize(testX[i]));
        }

        // 2. Fit Ridge Multinomial Logistic Regression Model
        Model = MultinomialLogisticRegression.Fit(trainXNorm, trainY, numClasses, numFeatures);

        // 3. Fit Mahalanobis Outlier Detector
        OutlierDetector = OutlierDetector.Fit(trainXNorm, trainY, numClasses, numFeatures);

        // 4. Evaluate Held-out Split
        var classNames = TrainedZones.Select(z => z.Name).ToArray();
        var confusionMatrix = new ConfusionMatrix(classNames);

        int correctPredictions = 0;
        int rejectedCount = 0;
        var latencies = new List<double>();
        var probSpan = new float[numClasses];

        for (int i = 0; i < testXNorm.Count; i++)
        {
            var sample = testXNorm[i];
            int actualClass = testY[i];

            var sw = Stopwatch.StartNew();
            Model.PredictProbabilities(sample, probSpan);

            // Find best class
            int predictedClass = 0;
            float maxProb = probSpan[0];
            for (int c = 1; c < numClasses; c++)
            {
                if (probSpan[c] > maxProb)
                {
                    maxProb = probSpan[c];
                    predictedClass = c;
                }
            }

            bool isOutlier = OutlierDetector.IsOutlier(sample, predictedClass, out float dist, out _);
            sw.Stop();
            latencies.Add(sw.Elapsed.TotalMilliseconds);

            if (maxProb < MinConfidenceThreshold || isOutlier)
            {
                rejectedCount++;
            }
            else
            {
                confusionMatrix.Add(actualClass, predictedClass);
                if (predictedClass == actualClass)
                {
                    correctPredictions++;
                }
            }
        }

        latencies.Sort();
        double medianLatency = latencies.Count > 0 ? latencies[latencies.Count / 2] : 0.0;
        double accuracy = testXNorm.Count > 0 ? (correctPredictions / (double)testXNorm.Count) * 100.0 : 0.0;

        IsTrained = true;

        return new TrainingResult
        {
            Success = true,
            Message = $"Model successfully trained across {numClasses} zones with {accuracy:0.0}% accuracy.",
            AccuracyPercent = accuracy,
            TotalSamples = samples.Count,
            TrainSamplesCount = trainX.Count,
            HeldOutSamplesCount = testX.Count,
            RejectedCount = rejectedCount,
            MedianLatencyMs = medianLatency,
            ConfusionMatrix = confusionMatrix
        };
    }

    /// <summary>
    /// Classifies an unnormalized acoustic feature vector from a live chassis tap.
    /// </summary>
    public ClassificationResult Classify(ReadOnlySpan<float> rawFeatures)
    {
        if (!IsTrained || Model == null || Stats == null || OutlierDetector == null || TrainedZones.Count == 0)
        {
            return ClassificationResult.Rejected("Classifier is not trained.");
        }

        int numClasses = TrainedZones.Count;
        Span<float> normalized = stackalloc float[rawFeatures.Length];
        rawFeatures.CopyTo(normalized);
        Stats.NormalizeInPlace(normalized);

        Span<float> probs = stackalloc float[numClasses];
        Model.PredictProbabilities(normalized, probs);

        int bestClass = 0;
        float maxProb = probs[0];
        for (int c = 1; c < numClasses; c++)
        {
            if (probs[c] > maxProb)
            {
                maxProb = probs[c];
                bestClass = c;
            }
        }

        bool isOutlier = OutlierDetector.IsOutlier(normalized, bestClass, out float distance, out float threshold);

        if (maxProb < MinConfidenceThreshold)
        {
            return ClassificationResult.Rejected(
                $"Low confidence ({(maxProb * 100f):0.0}%)", maxProb, distance);
        }

        if (isOutlier)
        {
            return ClassificationResult.Rejected(
                $"Outlier acoustic signature (dist {distance:0.1} > {threshold:0.1})", maxProb, distance);
        }

        var matchedZone = TrainedZones[bestClass];
        return ClassificationResult.Accepted(
            matchedZone.Id,
            matchedZone.Name,
            maxProb,
            distance,
            threshold,
            matchedZone.IsIgnore);
    }
}
