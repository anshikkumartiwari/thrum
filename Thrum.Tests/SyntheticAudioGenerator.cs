namespace Thrum.Tests;

/// <summary>
/// Generates synthetic audio signals for testing DSP algorithms offline:
/// decaying chassis thumps of differing resonant frequencies, white/pink noise, and sustained tones.
/// </summary>
public static class SyntheticAudioGenerator
{
    /// <summary>
    /// Generates a decaying resonant impulse (simulating a tap on a laptop chassis).
    /// </summary>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <param name="durationMs">Total signal duration in ms.</param>
    /// <param name="resonanceFreq">Chassis natural resonant frequency in Hz (e.g. 200 - 1500 Hz).</param>
    /// <param name="decayTimeMs">Exponential decay constant (tau) in ms.</param>
    /// <param name="peakAmplitude">Peak amplitude.</param>
    /// <param name="noiseLevel">Background noise floor standard deviation.</param>
    /// <param name="onsetDelayMs">Delay before tap impact occurs.</param>
    public static float[] GenerateDecayingThump(
        int sampleRate = 16000,
        float durationMs = 200f,
        float resonanceFreq = 350f,
        float decayTimeMs = 20f,
        float peakAmplitude = 0.5f,
        float noiseLevel = 0.002f,
        float onsetDelayMs = 40f,
        int? seed = null)
    {
        int totalSamples = (int)(sampleRate * (durationMs / 1000f));
        int onsetSample = (int)(sampleRate * (onsetDelayMs / 1000f));
        float tauSamples = sampleRate * (decayTimeMs / 1000f);

        float[] buffer = new float[totalSamples];
        var random = seed.HasValue ? new Random(seed.Value) : new Random();

        for (int i = 0; i < totalSamples; i++)
        {
            // Gaussian background noise via Box-Muller
            double u1 = 1.0 - random.NextDouble();
            double u2 = 1.0 - random.NextDouble();
            float randStdNormal = (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2));
            float noise = randStdNormal * noiseLevel;

            if (i >= onsetSample)
            {
                int t = i - onsetSample;
                float decay = MathF.Exp(-t / tauSamples);
                float oscillation = MathF.Sin(2.0f * MathF.PI * resonanceFreq * (t / (float)sampleRate));
                buffer[i] = (peakAmplitude * decay * oscillation) + noise;
            }
            else
            {
                buffer[i] = noise;
            }
        }

        return buffer;
    }

    /// <summary>
    /// Generates a sustained sine tone (simulating human speech, humming, whistle, or fan noise).
    /// </summary>
    public static float[] GenerateSustainedTone(
        int sampleRate = 16000,
        float durationMs = 200f,
        float freqHz = 440f,
        float amplitude = 0.3f,
        float noiseLevel = 0.002f)
    {
        int totalSamples = (int)(sampleRate * (durationMs / 1000f));
        float[] buffer = new float[totalSamples];
        var random = new Random(42);

        for (int i = 0; i < totalSamples; i++)
        {
            float noise = ((float)random.NextDouble() - 0.5f) * 2f * noiseLevel;
            float tone = amplitude * MathF.Sin(2.0f * MathF.PI * freqHz * (i / (float)sampleRate));
            buffer[i] = tone + noise;
        }

        return buffer;
    }

    /// <summary>
    /// Generates pure ambient noise.
    /// </summary>
    public static float[] GenerateAmbientNoise(
        int sampleRate = 16000,
        float durationMs = 500f,
        float noiseLevel = 0.003f)
    {
        int totalSamples = (int)(sampleRate * (durationMs / 1000f));
        float[] buffer = new float[totalSamples];
        var random = new Random(1337);

        for (int i = 0; i < totalSamples; i++)
        {
            buffer[i] = ((float)random.NextDouble() - 0.5f) * 2f * noiseLevel;
        }

        return buffer;
    }
}
