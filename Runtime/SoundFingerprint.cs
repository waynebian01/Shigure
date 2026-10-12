using NAudio.Dsp;

namespace Shigure;

// 短时频谱归一化后比较时序；音量变化不会改变匹配结果。
internal sealed class SoundFingerprint
{
    public const int SampleRate = 16000;
    internal const int Window = 512;
    internal const int Hop = 160;
    private const int Bands = 32;
    public required float[][] Frames { get; init; }

    public static SoundFingerprint Create(float[] samples)
    {
        var frames = new List<float[]>();
        for (var i = 0; i + Window <= samples.Length; i += Hop)
            frames.Add(Analyze(samples.AsSpan(i, Window)));
        var first = frames.FindIndex(frame => frame[Bands] > 0.00001f);
        var last = frames.FindLastIndex(frame => frame[Bands] > 0.00001f);
        if (first < 0 || last - first < 12)
            throw new InvalidOperationException("音效太短或没有可识别的声音，请使用至少 0.15 秒的清晰音效。");
        // 最多比较前 0.8 秒，避免等钓鱼音效的数秒尾音播完才响应。
        return new SoundFingerprint { Frames = frames.Skip(first).Take(Math.Min(80, last - first + 1)).ToArray() };
    }

    internal static float[] Analyze(ReadOnlySpan<float> samples)
    {
        var fft = new Complex[Window];
        var energy = 0f;
        for (var i = 0; i < Window; i++)
        {
            energy += samples[i] * samples[i];
            fft[i].X = samples[i] * (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Window - 1)));
        }
        FastFourierTransform.FFT(true, 9, fft);
        var result = new float[Bands + 1];
        for (var band = 0; band < Bands; band++)
        {
            var start = Math.Max(2, (int)Math.Pow(256, (double)band / Bands));
            var end = Math.Max(start + 1, (int)Math.Pow(256, (double)(band + 1) / Bands));
            for (var i = start; i < Math.Min(end, Window / 2); i++)
                result[band] += fft[i].X * fft[i].X + fft[i].Y * fft[i].Y;
            result[band] = MathF.Sqrt(result[band]);
        }
        var norm = MathF.Sqrt(result.Take(Bands).Sum(value => value * value));
        if (norm > 1e-10f)
            for (var i = 0; i < Bands; i++) result[i] /= norm;
        result[Bands] = energy / Window;
        return result;
    }

    internal double Match(IReadOnlyList<float[]> frames, int offset)
    {
        double score = 0;
        double energyDot = 0, referenceEnergy = 0, actualEnergy = 0;
        var audible = 0;
        for (var i = 0; i < Frames.Length; i++)
        {
            var reference = Frames[i];
            var actual = frames[offset + i];
            energyDot += Math.Sqrt(reference[Bands] * actual[Bands]);
            referenceEnergy += reference[Bands];
            actualEnergy += actual[Bands];
            if (reference[Bands] < 0.00001f) continue;
            audible++;
            if (actual[Bands] < 0.00000001f) continue;
            for (var band = 0; band < Bands; band++) score += reference[band] * actual[band];
        }
        var envelope = referenceEnergy > 0 && actualEnergy > 0
            ? energyDot / Math.Sqrt(referenceEnergy * actualEnergy) : 0;
        return audible == 0 ? 0 : score / audible * 0.75 + envelope * 0.25;
    }
}
