using System.Buffers.Binary;
using NAudio.Wave;

namespace Thrum.Core.Dsp;

/// <summary>
/// Converts raw WASAPI audio buffers (PCM 16/24/32-bit, IEEE float, arbitrary channel counts)
/// into mono 16 kHz 32-bit floating-point audio with allocation-friendly pooling.
/// </summary>
public sealed class AudioFormatConverter
{
    public const int DefaultTargetSampleRate = 16000;

    private readonly int _targetSampleRate;
    private double _resamplePhase;

    public int TargetSampleRate => _targetSampleRate;

    public AudioFormatConverter(int targetSampleRate = DefaultTargetSampleRate)
    {
        _targetSampleRate = targetSampleRate;
    }

    public void Reset()
    {
        _resamplePhase = 0;
    }

    private static readonly Guid SubTypeIeeeFloat = new("00000003-0000-0010-8000-00aa00389b71");

    /// <summary>
    /// Decodes raw WASAPI buffer bytes into mono float samples at original sample rate.
    /// </summary>
    public static int DecodeToMonoFloat(
        byte[] rawBytes,
        int bytesRecorded,
        WaveFormat format,
        Span<float> monoFloatDest)
    {
        if (bytesRecorded <= 0 || format == null) return 0;

        int channels = format.Channels;
        int bitsPerSample = format.BitsPerSample;
        int bytesPerSample = bitsPerSample / 8;
        int frameSize = format.BlockAlign > 0 ? format.BlockAlign : channels * bytesPerSample;
        int totalFrames = bytesRecorded / frameSize;
        int framesToProcess = Math.Min(totalFrames, monoFloatDest.Length);

        ReadOnlySpan<byte> byteSpan = rawBytes.AsSpan(0, bytesRecorded);

        bool isFloat32 = (bitsPerSample == 32) && (
            format.Encoding == WaveFormatEncoding.IeeeFloat ||
            format.Encoding == WaveFormatEncoding.Extensible ||
            (format is WaveFormatExtensible ext && ext.SubFormat == SubTypeIeeeFloat));

        if (isFloat32)
        {
            for (int f = 0; f < framesToProcess; f++)
            {
                int frameOffset = f * frameSize;
                float sum = 0f;
                for (int ch = 0; ch < channels; ch++)
                {
                    int sampleOffset = frameOffset + (ch * 4);
                    float sample = BitConverter.ToSingle(byteSpan.Slice(sampleOffset, 4));
                    sum += sample;
                }
                monoFloatDest[f] = sum / channels;
            }
            return framesToProcess;
        }
        else if (bitsPerSample == 16)
        {
            for (int f = 0; f < framesToProcess; f++)
            {
                int frameOffset = f * frameSize;
                float sum = 0f;
                for (int ch = 0; ch < channels; ch++)
                {
                    int sampleOffset = frameOffset + (ch * 2);
                    short sample = BinaryPrimitives.ReadInt16LittleEndian(byteSpan.Slice(sampleOffset, 2));
                    sum += sample / 32768f;
                }
                monoFloatDest[f] = sum / channels;
            }
            return framesToProcess;
        }
        else if (bitsPerSample == 24)
        {
            for (int f = 0; f < framesToProcess; f++)
            {
                int frameOffset = f * frameSize;
                float sum = 0f;
                for (int ch = 0; ch < channels; ch++)
                {
                    int sampleOffset = frameOffset + (ch * 3);
                    int sample24 = (byteSpan[sampleOffset + 2] << 24) |
                                   (byteSpan[sampleOffset + 1] << 16) |
                                   (byteSpan[sampleOffset] << 8);
                    sum += (sample24 >> 8) / 8388608f;
                }
                monoFloatDest[f] = sum / channels;
            }
            return framesToProcess;
        }
        else if (bitsPerSample == 32 && format.Encoding == WaveFormatEncoding.Pcm)
        {
            for (int f = 0; f < framesToProcess; f++)
            {
                int frameOffset = f * frameSize;
                float sum = 0f;
                for (int ch = 0; ch < channels; ch++)
                {
                    int sampleOffset = frameOffset + (ch * 4);
                    int sample = BinaryPrimitives.ReadInt32LittleEndian(byteSpan.Slice(sampleOffset, 4));
                    sum += sample / 2147483648f;
                }
                monoFloatDest[f] = sum / channels;
            }
            return framesToProcess;
        }

        // Fallback: unsupported format
        return 0;
    }

    /// <summary>
    /// Resamples a mono float stream from sourceRate to _targetSampleRate using stateful linear interpolation.
    /// </summary>
    public int Resample(
        ReadOnlySpan<float> input,
        int sourceRate,
        Span<float> output)
    {
        if (sourceRate == _targetSampleRate)
        {
            int count = Math.Min(input.Length, output.Length);
            input.Slice(0, count).CopyTo(output);
            return count;
        }

        double step = (double)sourceRate / _targetSampleRate;
        int outIndex = 0;

        while (outIndex < output.Length)
        {
            int inIndex0 = (int)Math.Floor(_resamplePhase);
            int inIndex1 = inIndex0 + 1;

            if (inIndex0 >= input.Length)
            {
                _resamplePhase -= input.Length;
                break;
            }

            float s0 = input[inIndex0];
            float s1 = (inIndex1 < input.Length) ? input[inIndex1] : s0;
            float fraction = (float)(_resamplePhase - inIndex0);

            output[outIndex++] = s0 + fraction * (s1 - s0);
            _resamplePhase += step;
        }

        return outIndex;
    }
}
