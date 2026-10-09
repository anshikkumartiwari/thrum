namespace Thrum.Core.Dsp;

/// <summary>
/// High-performance in-place Radix-2 Cooley-Tukey Fast Fourier Transform (FFT).
/// Allocation-free during runtime with precomputed twiddle factors and bit-reversal tables.
/// </summary>
public sealed class Radix2Fft
{
    private readonly int _fftLength;
    private readonly int[] _reversedBits;
    private readonly float[] _cosTable;
    private readonly float[] _sinTable;

    public int Length => _fftLength;

    public Radix2Fft(int fftLength = 2048)
    {
        if ((fftLength & (fftLength - 1)) != 0 || fftLength <= 0)
            throw new ArgumentException("FFT length must be a positive power of 2.", nameof(fftLength));

        _fftLength = fftLength;
        _reversedBits = new int[fftLength];
        _cosTable = new float[fftLength / 2];
        _sinTable = new float[fftLength / 2];

        int bits = (int)Math.Round(Math.Log2(fftLength));
        for (int i = 0; i < fftLength; i++)
        {
            _reversedBits[i] = ReverseBits(i, bits);
        }

        for (int i = 0; i < fftLength / 2; i++)
        {
            double angle = -2.0 * Math.PI * i / fftLength;
            _cosTable[i] = (float)Math.Cos(angle);
            _sinTable[i] = (float)Math.Sin(angle);
        }
    }

    private static int ReverseBits(int val, int bitCount)
    {
        int result = 0;
        for (int i = 0; i < bitCount; i++)
        {
            result = (result << 1) | (val & 1);
            val >>= 1;
        }
        return result;
    }

    /// <summary>
    /// Computes in-place forward FFT. real and imag must be at least Length in size.
    /// </summary>
    public void Forward(Span<float> real, Span<float> imag)
    {
        // 1. Bit-reversal permutation
        for (int i = 0; i < _fftLength; i++)
        {
            int j = _reversedBits[i];
            if (j > i)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }
        }

        // 2. Cooley-Tukey butterfly stages
        for (int halfSize = 1; halfSize < _fftLength; halfSize <<= 1)
        {
            int fullSize = halfSize << 1;
            int step = _fftLength / fullSize;

            for (int k = 0; k < _fftLength; k += fullSize)
            {
                for (int j = 0; j < halfSize; j++)
                {
                    int tableIndex = j * step;
                    float c = _cosTable[tableIndex];
                    float s = _sinTable[tableIndex];

                    int uIndex = k + j;
                    int vIndex = k + j + halfSize;

                    float tr = (c * real[vIndex]) - (s * imag[vIndex]);
                    float ti = (c * imag[vIndex]) + (s * real[vIndex]);

                    real[vIndex] = real[uIndex] - tr;
                    imag[vIndex] = imag[uIndex] - ti;
                    real[uIndex] += tr;
                    imag[uIndex] += ti;
                }
            }
        }
    }

    /// <summary>
    /// Computes magnitude spectrum from real and imag arrays.
    /// mag array length must be at least Length / 2 + 1 (positive frequencies).
    /// </summary>
    public void ComputeMagnitude(ReadOnlySpan<float> real, ReadOnlySpan<float> imag, Span<float> mag)
    {
        int half = (_fftLength / 2) + 1;
        int count = Math.Min(half, mag.Length);
        for (int i = 0; i < count; i++)
        {
            mag[i] = MathF.Sqrt((real[i] * real[i]) + (imag[i] * imag[i]));
        }
    }
}
