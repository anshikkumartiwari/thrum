namespace Thrum.Core.Dsp;

/// <summary>
/// Extracts a rich acoustic feature vector (time-domain and frequency-domain log-bands)
/// from a captured chassis tap event window.
/// </summary>
public sealed class FeatureExtractor
{
    public const int DefaultFftSize = 2048;
    public const int NumFilterBands = 20;
    public const int TotalFeatureCount = 5 + NumFilterBands + 2; // 27 features

    private readonly int _sampleRate;
    private readonly int _fftSize;
    private readonly Radix2Fft _fft;
    private readonly float[] _hannWindow;
    private readonly float[] _realBuffer;
    private readonly float[] _imagBuffer;
    private readonly float[] _magnitudeBuffer;
    private readonly (int StartBin, int EndBin)[] _filterBands;

    public int FeatureCount => TotalFeatureCount;

    public FeatureExtractor(int sampleRate = 16000, int fftSize = DefaultFftSize)
    {
        _sampleRate = sampleRate;
        _fftSize = fftSize;
        _fft = new Radix2Fft(fftSize);

        _realBuffer = new float[fftSize];
        _imagBuffer = new float[fftSize];
        _magnitudeBuffer = new float[(fftSize / 2) + 1];

        // Precompute Hann window
        _hannWindow = new float[fftSize];
        for (int i = 0; i < fftSize; i++)
        {
            _hannWindow[i] = 0.5f * (1.0f - MathF.Cos((2.0f * MathF.PI * i) / (fftSize - 1)));
        }

        // Precompute 20 log-spaced frequency bands from 60 Hz to 7500 Hz
        _filterBands = ComputeLogBands(60f, 7500f, NumFilterBands, sampleRate, fftSize);
    }

    private static (int StartBin, int EndBin)[] ComputeLogBands(
        float minFreq, float maxFreq, int numBands, int sampleRate, int fftSize)
    {
        var bands = new (int StartBin, int EndBin)[numBands];
        float binWidth = (float)sampleRate / fftSize;

        float minLog = MathF.Log(minFreq);
        float maxLog = MathF.Log(maxFreq);
        float step = (maxLog - minLog) / numBands;

        for (int i = 0; i < numBands; i++)
        {
            float fLow = MathF.Exp(minLog + (i * step));
            float fHigh = MathF.Exp(minLog + ((i + 1) * step));

            int startBin = Math.Clamp((int)MathF.Floor(fLow / binWidth), 1, (fftSize / 2) - 1);
            int endBin = Math.Clamp((int)MathF.Ceiling(fHigh / binWidth), startBin + 1, fftSize / 2);

            bands[i] = (startBin, endBin);
        }

        return bands;
    }

    /// <summary>
    /// Extracts a 27-element feature vector from the event window.
    /// </summary>
    /// <param name="eventWindow">Event audio window (pre-roll + post-onset).</param>
    /// <param name="preRollSamples">Number of pre-roll samples.</param>
    /// <param name="outputVector">Destination span of size >= TotalFeatureCount.</param>
    public void ExtractFeatures(
        ReadOnlySpan<float> eventWindow,
        int preRollSamples,
        Span<float> outputVector)
    {
        if (outputVector.Length < TotalFeatureCount)
            throw new ArgumentException($"Destination span must be at least {TotalFeatureCount} floats.", nameof(outputVector));

        // 1. Time-Domain Metrics
        float peak = 0f;
        int peakIndex = preRollSamples;
        float sumSquares = 0f;

        for (int i = 0; i < eventWindow.Length; i++)
        {
            float abs = MathF.Abs(eventWindow[i]);
            if (abs > peak)
            {
                peak = abs;
                peakIndex = i;
            }
            sumSquares += abs * abs;
        }

        float rms = MathF.Sqrt(sumSquares / Math.Max(1, eventWindow.Length));
        float crestFactor = rms > 1e-6f ? (peak / rms) : 0f;

        // Decay time: samples from peak until amplitude drops below 20% of peak
        float decayThreshold = peak * 0.20f;
        int decaySamples = 0;
        for (int i = peakIndex; i < eventWindow.Length; i++)
        {
            if (MathF.Abs(eventWindow[i]) >= decayThreshold)
            {
                decaySamples = i - peakIndex;
            }
        }
        float decayTimeMs = (decaySamples / (float)_sampleRate) * 1000f;

        // Early energy ratio: energy in first 25ms after onset vs total post-onset energy
        int earlySamples = Math.Min((int)(_sampleRate * 0.025f), eventWindow.Length - preRollSamples);
        float earlyEnergy = 1e-7f;
        for (int i = 0; i < earlySamples; i++)
        {
            float s = eventWindow[preRollSamples + i];
            earlyEnergy += s * s;
        }

        float totalPostEnergy = 1e-7f;
        for (int i = preRollSamples; i < eventWindow.Length; i++)
        {
            float s = eventWindow[i];
            totalPostEnergy += s * s;
        }
        float earlyEnergyRatio = earlyEnergy / totalPostEnergy;

        // Write Time-Domain Features (Indices 0 - 4)
        outputVector[0] = peak;
        outputVector[1] = rms;
        outputVector[2] = crestFactor;
        outputVector[3] = decayTimeMs;
        outputVector[4] = earlyEnergyRatio;

        // 2. Frequency-Domain (FFT)
        // Window the event signal and zero-pad to _fftSize
        Array.Clear(_realBuffer, 0, _realBuffer.Length);
        Array.Clear(_imagBuffer, 0, _imagBuffer.Length);

        int copyLen = Math.Min(eventWindow.Length, _fftSize);
        for (int i = 0; i < copyLen; i++)
        {
            _realBuffer[i] = eventWindow[i] * _hannWindow[i];
        }

        _fft.Forward(_realBuffer, _imagBuffer);
        _fft.ComputeMagnitude(_realBuffer, _imagBuffer, _magnitudeBuffer);

        // Compute 20 Log-Band Energies (Indices 5 - 24)
        float totalSpectralMag = 1e-6f;
        float weightedFreqSum = 0f;
        float binWidth = (float)_sampleRate / _fftSize;
        int halfBins = (_fftSize / 2) + 1;

        for (int b = 0; b < NumFilterBands; b++)
        {
            var (startBin, endBin) = _filterBands[b];
            float bandEnergy = 0f;
            for (int k = startBin; k < endBin && k < halfBins; k++)
            {
                bandEnergy += _magnitudeBuffer[k] * _magnitudeBuffer[k];
            }
            outputVector[5 + b] = MathF.Log(bandEnergy + 1e-6f);
        }

        // Spectral Centroid and Rolloff
        for (int k = 1; k < halfBins; k++)
        {
            float mag = _magnitudeBuffer[k];
            totalSpectralMag += mag;
            weightedFreqSum += (k * binWidth) * mag;
        }

        float nyquist = _sampleRate / 2.0f;
        float centroid = (weightedFreqSum / totalSpectralMag) / nyquist; // Normalized 0..1

        // Rolloff (85% energy threshold)
        float rolloffThreshold = totalSpectralMag * 0.85f;
        float accumulated = 0f;
        float rolloffFreq = 0f;
        for (int k = 1; k < halfBins; k++)
        {
            accumulated += _magnitudeBuffer[k];
            if (accumulated >= rolloffThreshold)
            {
                rolloffFreq = (k * binWidth) / nyquist;
                break;
            }
        }

        // Write Spectral Features (Indices 25 - 26)
        outputVector[25] = centroid;
        outputVector[26] = rolloffFreq;
    }
}
