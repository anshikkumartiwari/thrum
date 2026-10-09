using Thrum.Core.Dsp;
using Xunit;

namespace Thrum.Tests;

public sealed class DspTests
{
    [Fact]
    public void AdaptiveNoiseFloor_AdaptsDuringQuiet_AndFreezesDuringLoud()
    {
        var noiseFloor = new AdaptiveNoiseFloor(initialFloor: 0.05f, alpha: 0.1f, quietMultiplier: 2.0f);

        // Feed very quiet frames (RMS ~ 0.002)
        float[] quietFrame = SyntheticAudioGenerator.GenerateAmbientNoise(16000, 20f, 0.002f);
        for (int i = 0; i < 50; i++)
        {
            noiseFloor.ProcessFrame(quietFrame);
        }

        // Noise floor should have adapted downward significantly
        Assert.True(noiseFloor.CurrentNoiseFloor < 0.02f, $"Expected floor < 0.02, was {noiseFloor.CurrentNoiseFloor}");
        float baselineFloor = noiseFloor.CurrentNoiseFloor;

        // Feed loud frame (RMS ~ 0.4)
        float[] loudFrame = SyntheticAudioGenerator.GenerateSustainedTone(16000, 20f, 400f, 0.4f);
        bool wasQuiet = noiseFloor.ProcessFrame(loudFrame);

        // Loud frame should be flagged as not quiet and should NOT inflate the floor
        Assert.False(wasQuiet);
        Assert.Equal(baselineFloor, noiseFloor.CurrentNoiseFloor);
    }

    [Fact]
    public void OnsetDetector_DetectsImpulseThump()
    {
        var ringBuffer = new AudioRingBuffer(16000);
        var noiseFloor = new AdaptiveNoiseFloor(initialFloor: 0.005f);
        var onsetDetector = new OnsetDetector(sampleRate: 16000, preRollMs: 30f, postOnsetMs: 90f);

        // Populate ring buffer with 100ms ambient quiet
        float[] preSilence = SyntheticAudioGenerator.GenerateAmbientNoise(16000, 100f, 0.002f);
        ringBuffer.Write(preSilence);
        noiseFloor.ProcessFrame(preSilence);

        // Generate synthetic thump with 20ms onset delay
        float[] thump = SyntheticAudioGenerator.GenerateDecayingThump(
            16000, durationMs: 150f, resonanceFreq: 300f, decayTimeMs: 15f, peakAmplitude: 0.5f, onsetDelayMs: 20f);

        int eventsFired = 0;
        float[]? capturedWindow = null;

        // Process audio in 10ms chunks
        int chunkSize = 160;
        for (int offset = 0; offset < thump.Length; offset += chunkSize)
        {
            int len = Math.Min(chunkSize, thump.Length - offset);
            ReadOnlySpan<float> chunk = thump.AsSpan(offset, len);

            ringBuffer.Write(chunk);
            onsetDetector.Process(chunk, ringBuffer, noiseFloor, (window, floor) =>
            {
                eventsFired++;
                capturedWindow = window;
            });
        }

        Assert.Equal(1, eventsFired);
        Assert.NotNull(capturedWindow);
        Assert.Equal(onsetDetector.TotalWindowSamples, capturedWindow.Length);
    }

    [Fact]
    public void ImpulseGate_AcceptsCleanThump_AndRejectsSustainedTone()
    {
        var gate = new ImpulseGate(sampleRate: 16000);
        int preRollSamples = 480; // 30ms

        // 1. Clean decaying thump
        float[] thump = SyntheticAudioGenerator.GenerateDecayingThump(
            16000, durationMs: 120f, resonanceFreq: 400f, decayTimeMs: 18f, peakAmplitude: 0.6f, onsetDelayMs: 30f);

        var thumpResult = gate.Evaluate(thump, preRollSamples, noiseFloor: 0.005f);
        Assert.True(thumpResult.IsAccepted, $"Expected accepted, got {thumpResult.Reason}: {thumpResult.RejectionMessage}");
        Assert.Equal(ImpulseRejectionReason.None, thumpResult.Reason);

        // 2. Sustained tone (speech / hum)
        float[] tone = SyntheticAudioGenerator.GenerateSustainedTone(
            16000, durationMs: 120f, freqHz: 500f, amplitude: 0.4f);

        var toneResult = gate.Evaluate(tone, preRollSamples, noiseFloor: 0.005f);
        Assert.False(toneResult.IsAccepted);
        Assert.True(
            toneResult.Reason == ImpulseRejectionReason.SustainedSound ||
            toneResult.Reason == ImpulseRejectionReason.LowCrestFactor ||
            toneResult.Reason == ImpulseRejectionReason.TooLong,
            $"Expected rejection for sustained tone, got {toneResult.Reason}");
    }

    [Fact]
    public void RefractoryPeriod_SuppressesDuplicateTriggers()
    {
        var ringBuffer = new AudioRingBuffer(16000);
        var noiseFloor = new AdaptiveNoiseFloor(initialFloor: 0.005f);
        // Refractory: 140ms
        var onsetDetector = new OnsetDetector(sampleRate: 16000, preRollMs: 30f, postOnsetMs: 90f, refractoryMs: 140f);

        // Generate signal with two thumps spaced only 50ms apart (within refractory period)
        float[] thump1 = SyntheticAudioGenerator.GenerateDecayingThump(
            16000, durationMs: 250f, resonanceFreq: 350f, decayTimeMs: 15f, peakAmplitude: 0.5f, onsetDelayMs: 30f);
        float[] thump2 = SyntheticAudioGenerator.GenerateDecayingThump(
            16000, durationMs: 250f, resonanceFreq: 350f, decayTimeMs: 15f, peakAmplitude: 0.5f, onsetDelayMs: 80f);

        float[] combined = new float[thump1.Length];
        for (int i = 0; i < combined.Length; i++)
        {
            combined[i] = thump1[i] + thump2[i];
        }

        int onsets = 0;
        int chunkSize = 160;
        for (int offset = 0; offset < combined.Length; offset += chunkSize)
        {
            int len = Math.Min(chunkSize, combined.Length - offset);
            ReadOnlySpan<float> chunk = combined.AsSpan(offset, len);
            ringBuffer.Write(chunk);
            onsetDetector.Process(chunk, ringBuffer, noiseFloor, (_, _) => onsets++);
        }

        // Only 1 onset should have fired because second thump is inside the 140ms refractory window
        Assert.Equal(1, onsets);
    }

    [Fact]
    public void FeatureExtractor_ExtractsValid27Features_WithoutNaNs()
    {
        var extractor = new FeatureExtractor(sampleRate: 16000);
        float[] thump = SyntheticAudioGenerator.GenerateDecayingThump(
            16000, durationMs: 120f, resonanceFreq: 500f, decayTimeMs: 20f, peakAmplitude: 0.5f, onsetDelayMs: 30f);

        float[] features = new float[extractor.FeatureCount];
        extractor.ExtractFeatures(thump, preRollSamples: 480, features);

        Assert.Equal(27, features.Length);
        for (int i = 0; i < features.Length; i++)
        {
            Assert.False(float.IsNaN(features[i]), $"Feature [{i}] is NaN");
            Assert.False(float.IsInfinity(features[i]), $"Feature [{i}] is Infinity");
        }

        // Verify peak and crest factor sanity
        Assert.True(features[0] > 0.1f, "Peak amplitude should be positive");
        Assert.True(features[2] > 2.0f, "Crest factor should be impulsive");
    }
}
