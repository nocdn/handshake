using NAudio.Wave;

namespace HsWin.App.Audio;

/// <summary>
/// Peak/RMS accumulation for captured buffers. WASAPI shared-mode capture almost always reports
/// <see cref="WaveFormatEncoding.Extensible"/>; the real sample type lives in its sub-format.
/// </summary>
internal static class AudioLevelCalculator
{
    private static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    public static AudioLevelStats Calculate(byte[] buffer, int bytesRecorded, WaveFormat waveFormat)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(waveFormat);

        return (ResolveEncoding(waveFormat), waveFormat.BitsPerSample) switch
        {
            (WaveFormatEncoding.IeeeFloat, 32) => CalculateFloat32(buffer, bytesRecorded),
            (WaveFormatEncoding.Pcm, 8) => CalculatePcm8(buffer, bytesRecorded),
            (WaveFormatEncoding.Pcm, 16) => CalculatePcm16(buffer, bytesRecorded),
            (WaveFormatEncoding.Pcm, 24) => CalculatePcm24(buffer, bytesRecorded),
            (WaveFormatEncoding.Pcm, 32) => CalculatePcm32(buffer, bytesRecorded),
            _ => AudioLevelStats.Empty
        };
    }

    internal static WaveFormatEncoding ResolveEncoding(WaveFormat waveFormat)
    {
        if (waveFormat is not WaveFormatExtensible extensible)
        {
            return waveFormat.Encoding;
        }

        if (extensible.SubFormat == IeeeFloatSubFormat)
        {
            return WaveFormatEncoding.IeeeFloat;
        }

        return extensible.SubFormat == PcmSubFormat ? WaveFormatEncoding.Pcm : WaveFormatEncoding.Extensible;
    }

    private static AudioLevelStats CalculateFloat32(byte[] buffer, int bytesRecorded)
    {
        var stats = AudioLevelStats.Empty;
        for (var offset = 0; offset + sizeof(float) <= bytesRecorded; offset += sizeof(float))
        {
            stats = stats.AddSample(BitConverter.ToSingle(buffer, offset));
        }

        return stats;
    }

    private static AudioLevelStats CalculatePcm8(byte[] buffer, int bytesRecorded)
    {
        var stats = AudioLevelStats.Empty;
        for (var offset = 0; offset < bytesRecorded; offset++)
        {
            stats = stats.AddSample((buffer[offset] - 128) / 128.0);
        }

        return stats;
    }

    private static AudioLevelStats CalculatePcm16(byte[] buffer, int bytesRecorded)
    {
        var stats = AudioLevelStats.Empty;
        for (var offset = 0; offset + sizeof(short) <= bytesRecorded; offset += sizeof(short))
        {
            stats = stats.AddSample(BitConverter.ToInt16(buffer, offset) / 32768.0);
        }

        return stats;
    }

    private static AudioLevelStats CalculatePcm24(byte[] buffer, int bytesRecorded)
    {
        var stats = AudioLevelStats.Empty;
        for (var offset = 0; offset + 3 <= bytesRecorded; offset += 3)
        {
            var value = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
            if ((value & 0x800000) != 0)
            {
                value |= unchecked((int)0xFF000000);
            }

            stats = stats.AddSample(value / 8388608.0);
        }

        return stats;
    }

    private static AudioLevelStats CalculatePcm32(byte[] buffer, int bytesRecorded)
    {
        var stats = AudioLevelStats.Empty;
        for (var offset = 0; offset + sizeof(int) <= bytesRecorded; offset += sizeof(int))
        {
            stats = stats.AddSample(BitConverter.ToInt32(buffer, offset) / 2147483648.0);
        }

        return stats;
    }
}

internal readonly record struct AudioLevelStats(long SampleCount, double SumSquares, double Peak)
{
    public static AudioLevelStats Empty { get; } = new(0, 0, 0);

    public double Rms => SampleCount == 0 ? 0 : Math.Sqrt(SumSquares / SampleCount);

    public AudioLevelStats AddSample(double sample)
    {
        if (!double.IsFinite(sample))
        {
            return this;
        }

        var clamped = Math.Clamp(sample, -1, 1);
        return new AudioLevelStats(SampleCount + 1, SumSquares + (clamped * clamped), Math.Max(Peak, Math.Abs(clamped)));
    }

    public AudioLevelStats Merge(AudioLevelStats other) =>
        new(SampleCount + other.SampleCount, SumSquares + other.SumSquares, Math.Max(Peak, other.Peak));
}
