using Thrum.Core.Classification;
using Thrum.Core.Dsp;
using Thrum.Core.Models;
using Xunit;

namespace Thrum.Tests;

public sealed class ClassifierTests
{
    [Fact]
    public void Classifier_SeparatesTwoSyntheticClasses()
    {
        var extractor = new FeatureExtractor(16000);
        var zoneA = new Zone { Id = "zone-a", Name = "Left Palm Rest", X = 0.2, Y = 0.8 };
        var zoneB = new Zone { Id = "zone-b", Name = "Right Palm Rest", X = 0.8, Y = 0.8 };

        var samples = new List<TapFeatureSample>();

        // Generate 15 samples for Zone A (chassis resonance 250 Hz, slow decay 25ms)
        for (int i = 0; i < 15; i++)
        {
            float jitterFreq = 240f + (i * 1.5f);
            float strikeForce = 0.35f + ((i % 5) * 0.05f);
            float[] audio = SyntheticAudioGenerator.GenerateDecayingThump(
                16000, 120f, resonanceFreq: jitterFreq, decayTimeMs: 25f, peakAmplitude: strikeForce, onsetDelayMs: 30f);
            float[] feats = new float[extractor.FeatureCount];
            extractor.ExtractFeatures(audio, 480, feats);
            samples.Add(new TapFeatureSample(zoneA.Id, feats));
        }

        // Generate 15 samples for Zone B (chassis resonance 900 Hz, fast decay 12ms)
        for (int i = 0; i < 15; i++)
        {
            float jitterFreq = 880f + (i * 2.0f);
            float strikeForce = 0.35f + ((i % 5) * 0.05f);
            float[] audio = SyntheticAudioGenerator.GenerateDecayingThump(
                16000, 120f, resonanceFreq: jitterFreq, decayTimeMs: 12f, peakAmplitude: strikeForce, onsetDelayMs: 30f);
            float[] feats = new float[extractor.FeatureCount];
            extractor.ExtractFeatures(audio, 480, feats);
            samples.Add(new TapFeatureSample(zoneB.Id, feats));
        }

        var classifier = new TapClassifier();
        var trainResult = classifier.Train(new[] { zoneA, zoneB }, samples);

        Assert.True(trainResult.Success, trainResult.Message);
        Assert.True(trainResult.AccuracyPercent >= 80.0, $"Expected >=80% accuracy, got {trainResult.AccuracyPercent}%");

        // Test inference on unseen Zone A tap
        float[] testAudioA = SyntheticAudioGenerator.GenerateDecayingThump(16000, 120f, 245f, 25f, 0.45f, onsetDelayMs: 30f);
        float[] testFeatA = new float[extractor.FeatureCount];
        extractor.ExtractFeatures(testAudioA, 480, testFeatA);

        var resultA = classifier.Classify(testFeatA);
        Assert.True(resultA.IsAccepted, $"ResultA rejected: {resultA.RejectionReason}, Conf: {resultA.Confidence}, Dist: {resultA.MahalanobisDistance}/{resultA.DistanceThreshold}");
        Assert.Equal(zoneA.Id, resultA.MatchedZoneId);

        // Test inference on unseen Zone B tap
        float[] testAudioB = SyntheticAudioGenerator.GenerateDecayingThump(16000, 120f, 895f, 12f, 0.45f, onsetDelayMs: 30f);
        float[] testFeatB = new float[extractor.FeatureCount];
        extractor.ExtractFeatures(testAudioB, 480, testFeatB);

        var resultB = classifier.Classify(testFeatB);
        Assert.True(resultB.IsAccepted);
        Assert.Equal(zoneB.Id, resultB.MatchedZoneId);
    }

    [Fact]
    public void Classifier_RejectsUnlikeEvent_ViaMahalanobisOrConfidence()
    {
        var extractor = new FeatureExtractor(16000);
        var zoneA = new Zone { Id = "zone-a", Name = "Left Palm Rest" };
        var zoneB = new Zone { Id = "zone-b", Name = "Right Palm Rest" };

        var samples = new List<TapFeatureSample>();
        for (int i = 0; i < 15; i++)
        {
            float[] aAudio = SyntheticAudioGenerator.GenerateDecayingThump(16000, 120f, 250f + i, 20f, 0.5f, onsetDelayMs: 30f);
            float[] aFeat = new float[extractor.FeatureCount];
            extractor.ExtractFeatures(aAudio, 480, aFeat);
            samples.Add(new TapFeatureSample(zoneA.Id, aFeat));

            float[] bAudio = SyntheticAudioGenerator.GenerateDecayingThump(16000, 120f, 800f + i, 20f, 0.5f, onsetDelayMs: 30f);
            float[] bFeat = new float[extractor.FeatureCount];
            extractor.ExtractFeatures(bAudio, 480, bFeat);
            samples.Add(new TapFeatureSample(zoneB.Id, bFeat));
        }

        var classifier = new TapClassifier();
        classifier.Train(new[] { zoneA, zoneB }, samples);

        // Generate an unlike sound: high frequency resonance at 3500 Hz
        float[] alienAudio = SyntheticAudioGenerator.GenerateDecayingThump(16000, 120f, 3500f, 20f, 0.5f, onsetDelayMs: 30f);
        float[] alienFeats = new float[extractor.FeatureCount];
        extractor.ExtractFeatures(alienAudio, 480, alienFeats);

        var result = classifier.Classify(alienFeats);

        // Unlike sound must be rejected (either low confidence or Mahalanobis outlier)
        Assert.False(result.IsAccepted, "Outlier event should be rejected by classifier.");
    }

    [Fact]
    public void Profile_SavesAndRestores_TrainedClassifier()
    {
        var extractor = new FeatureExtractor(16000);
        var zoneA = new Zone { Id = "zone-1", Name = "Area 1" };
        var zoneB = new Zone { Id = "zone-2", Name = "Area 2" };

        var samples = new List<TapFeatureSample>();
        for (int i = 0; i < 12; i++)
        {
            float[] aAudio = SyntheticAudioGenerator.GenerateDecayingThump(16000, 120f, 280f, 20f, 0.5f, onsetDelayMs: 30f);
            float[] aFeat = new float[extractor.FeatureCount];
            extractor.ExtractFeatures(aAudio, 480, aFeat);
            samples.Add(new TapFeatureSample(zoneA.Id, aFeat));

            float[] bAudio = SyntheticAudioGenerator.GenerateDecayingThump(16000, 120f, 750f, 20f, 0.5f, onsetDelayMs: 30f);
            float[] bFeat = new float[extractor.FeatureCount];
            extractor.ExtractFeatures(bAudio, 480, bFeat);
            samples.Add(new TapFeatureSample(zoneB.Id, bFeat));
        }

        var classifier = new TapClassifier();
        var trainResult = classifier.Train(new[] { zoneA, zoneB }, samples);

        var profile = new Profile
        {
            Zones = new List<Zone> { zoneA, zoneB },
            TrainingSamples = samples
        };
        profile.SaveClassifier(classifier, trainResult.AccuracyPercent);

        // Restore
        var restoredClassifier = profile.RestoreClassifier();
        Assert.NotNull(restoredClassifier);
        Assert.True(restoredClassifier.IsTrained);

        // Verify prediction works on restored classifier
        float[] testAudio = SyntheticAudioGenerator.GenerateDecayingThump(16000, 120f, 280f, 20f, 0.5f, onsetDelayMs: 30f);
        float[] testFeat = new float[extractor.FeatureCount];
        extractor.ExtractFeatures(testAudio, 480, testFeat);

        var result = restoredClassifier.Classify(testFeat);
        Assert.True(result.IsAccepted);
        Assert.Equal(zoneA.Id, result.MatchedZoneId);
    }
}
