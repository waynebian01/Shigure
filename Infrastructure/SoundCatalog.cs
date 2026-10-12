using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NVorbis;

namespace Shigure;

internal static class SoundCatalog
{
    public const string DefaultFileName = "FishingBobber_ver2_1.ogg";
    public const string ImportOption = "导入音效…";
    public static string UserDirectory => Path.Combine(AppPaths.UserDataDirectory, "Sounds");
    private static readonly object InitializationGate = new();
    private static bool _initialized;

    public static IReadOnlyList<string> GetNames()
    {
        InitializeDirectory();
        return Directory.Exists(UserDirectory)
            ? Directory.EnumerateFiles(UserDirectory).Where(IsSupported).Select(Path.GetFileName).OfType<string>()
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
    }

    public static IEnumerable<string> GetFiles(string name)
    {
        InitializeDirectory();
        return Directory.Exists(UserDirectory)
            ? Directory.EnumerateFiles(UserDirectory).Where(IsSupported)
                .Where(file => string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase)).ToArray()
            : [];
    }

    private static void InitializeDirectory()
    {
        lock (InitializationGate)
        {
            if (_initialized) return;
            var isNewDirectory = !Directory.Exists(UserDirectory);
            Directory.CreateDirectory(UserDirectory);
            // 新建目录时放入初始样本；识别始终读取用户目录，后续增删和改名由用户管理。
            if (isNewDirectory)
            {
                for (var index = 1; index <= 3; index++)
                {
                    var name = $"FishingBobber_ver2_{index}.ogg";
                    using var source = typeof(SoundCatalog).Assembly.GetManifestResourceStream("Shigure.Assets.Sounds." + name)
                        ?? throw new InvalidOperationException($"找不到初始音效“{name}”。");
                    using var destination = new FileStream(Path.Combine(UserDirectory, name), FileMode.CreateNew);
                    source.CopyTo(destination);
                }
            }
            _initialized = true;
        }
    }

    public static string Import(string path)
    {
        var name = Path.GetFileName(path);
        if (!IsSupported(path) || name.Length == 0 || name == ImportOption
            || name.IndexOfAny(['&', '|', '\'', '"']) >= 0)
            throw new InvalidOperationException("请选择 WAV 或 OGG 音效；名称不能包含引号、& 或 |。");
        if (GetNames().Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"音效“{name}”已存在，请先修改文件名。");
        // 导入前解码和校验，防止损坏文件进入运行时。
        _ = SoundFingerprint.Create(ReadMono(path));
        Directory.CreateDirectory(UserDirectory);
        File.Copy(path, Path.Combine(UserDirectory, name), false);
        return name;
    }

    private static bool IsSupported(string path)
        => Path.GetExtension(path).Equals(".ogg", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase);

    internal static float[] ReadMono(string path)
    {
        if (new FileInfo(path).Length > 10 * 1024 * 1024)
            throw new InvalidOperationException("音效文件不能超过 10 MB。");
        var values = new List<float>();
        int rate;
        int channels;
        var buffer = new float[8192];
        if (Path.GetExtension(path).Equals(".ogg", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = new VorbisReader(path);
            rate = reader.SampleRate;
            channels = reader.Channels;
            int count;
            while ((count = reader.ReadSamples(buffer, 0, buffer.Length - buffer.Length % channels)) > 0)
                Add(buffer, count);
        }
        else
        {
            using var reader = new WaveFileReader(path);
            var samples = reader.ToSampleProvider();
            rate = samples.WaveFormat.SampleRate;
            channels = samples.WaveFormat.Channels;
            int count;
            while ((count = samples.Read(buffer, 0, buffer.Length - buffer.Length % channels)) > 0)
                Add(buffer, count);
        }
        var result = new float[(int)((long)values.Count * SoundFingerprint.SampleRate / rate)];
        for (var i = 0; i < result.Length; i++)
        {
            var position = (double)i * rate / SoundFingerprint.SampleRate;
            var left = (int)position;
            var right = Math.Min(left + 1, values.Count - 1);
            result[i] = values[left] + (values[right] - values[left]) * (float)(position - left);
        }
        return result;

        void Add(float[] data, int count)
        {
            if (values.Count + count / channels > rate * 5)
                throw new InvalidOperationException("请使用不超过 5 秒的短音效。");
            for (var i = 0; i + channels <= count; i += channels)
            {
                var value = 0f;
                for (var ch = 0; ch < channels; ch++) value += data[i + ch];
                values.Add(value / channels);
            }
        }
    }
}
