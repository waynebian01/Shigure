using System.Drawing;

namespace Shigure;

internal sealed record SpellSuggestion(long SpellId, string Name);

/// <summary>
/// 技能名称/ID 到技能图标的只读目录。完整目录只来自外置数据包；数据包缺失或
/// 损坏时，技能图标与 spellId 联想均不可用。
/// </summary>
internal static class SpellIconCatalog
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<long, Image> Icons = new();
    private static readonly Dictionary<string, Image?> NamedIcons = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, long> RegisteredSpellIdsByName = new(StringComparer.Ordinal);

    private static readonly Dictionary<long, string> SpellIdIconResources = new()
    {
        [35395] = $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.crusader-strike.png"
    };

    private static readonly Dictionary<string, string> NamedIconResources = new(StringComparer.Ordinal)
    {
        ["银月城生命药水"] = $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.silvermoon-city-health-potion.png",
        [ModuleSpecialActions.OneKeySpell] = $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.one-key-spell.png",
        [ModuleSpecialActions.PauseSpell] = $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.pause.png",
        [ModuleSpecialActions.FailedSpell] = $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.auto-insert-spell.png",
        ["鲁莽药水"] = $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.recklessness-potion.jpg",
        ["圣光潜力"] = $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.lights-potential.jpg",
        ["光注法力药水"] = $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.light-infused-mana-potion.jpg",
        ["十字军打击"] = $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.crusader-strike.png",
        ["停止施法"] = $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.stop-casting.png"
    };

    private static readonly string LastRuleRowIconResource =
        $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.last-rule-row.png";

    private static SpellIconPackage? _package;
    private static Dictionary<string, long> _spellIdsByName = new(StringComparer.Ordinal);
    private static SpellSuggestion[] _suggestionsBySpellId = [];

    static SpellIconCatalog()
    {
        _package = SpellIconPackage.TryOpen(PackagePath);
        RebuildIndexesLocked();
    }

    internal static event Action? CatalogChanged;

    internal static string PackagePath => Path.Combine(
        AppContext.BaseDirectory,
        "data",
        "SpellIcons.shgpack");

    internal static bool IsPackageAvailable
    {
        get
        {
            lock (SyncRoot)
            {
                return _package is not null;
            }
        }
    }

    public static Image? Get(long spellId)
    {
        if (spellId <= 0)
        {
            return null;
        }

        lock (SyncRoot)
        {
            if (_package is null)
            {
                return null;
            }

            if (Icons.TryGetValue(spellId, out var cached))
            {
                return cached;
            }

            Image? icon = null;
            if (SpellIdIconResources.TryGetValue(spellId, out var resourceName))
            {
                icon = LoadResource(resourceName);
            }

            icon ??= _package.LoadIcon(spellId);
            return icon is null ? null : CacheLocked(spellId, icon);
        }
    }

    public static Image? Get(string? spellName)
    {
        var normalized = spellName?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        lock (SyncRoot)
        {
            if (_package is null)
            {
                return null;
            }

            if (NamedIconResources.TryGetValue(normalized, out var resourceName))
            {
                return GetNamedIconLocked(normalized, resourceName);
            }

            return _spellIdsByName.TryGetValue(normalized, out var spellId)
                ? Get(spellId)
                : null;
        }
    }

    public static void Register(long spellId, string? spellName)
    {
        var normalized = spellName?.Trim();
        if (spellId <= 0 || string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        lock (SyncRoot)
        {
            RegisteredSpellIdsByName[normalized] = spellId;
            _spellIdsByName[normalized] = spellId;
        }
    }

    internal static IReadOnlyList<SpellSuggestion> SearchByIdPrefix(string? prefix, int limit)
    {
        lock (SyncRoot)
        {
            var normalized = prefix;
            if (_package is null
                || limit <= 0
                || string.IsNullOrEmpty(normalized)
                || normalized.Length > 19
                || normalized[0] == '0'
                || _suggestionsBySpellId.Length == 0)
            {
                return Array.Empty<SpellSuggestion>();
            }

            long prefixValue = 0;
            foreach (var character in normalized)
            {
                if (character is < '0' or > '9')
                {
                    return Array.Empty<SpellSuggestion>();
                }

                var digit = character - '0';
                if (prefixValue > (long.MaxValue - digit) / 10)
                {
                    return Array.Empty<SpellSuggestion>();
                }

                prefixValue = prefixValue * 10 + digit;
            }

            if (prefixValue <= 0)
            {
                return Array.Empty<SpellSuggestion>();
            }

            var maximumSpellId = _suggestionsBySpellId[^1].SpellId;
            var maximumDigits = 1;
            for (var remaining = maximumSpellId; remaining >= 10; remaining /= 10)
            {
                maximumDigits++;
            }

            var maximumSuffixDigits = maximumDigits - normalized.Length;
            if (maximumSuffixDigits < 0)
            {
                return Array.Empty<SpellSuggestion>();
            }

            var matches = new List<SpellSuggestion>(Math.Min(limit, 8));
            long scale = 1;
            for (var suffixDigits = 0; suffixDigits <= maximumSuffixDigits; suffixDigits++)
            {
                if (prefixValue > long.MaxValue / scale)
                {
                    break;
                }

                var start = prefixValue * scale;
                var intervalLength = scale - 1;
                var end = intervalLength > long.MaxValue - start
                    ? long.MaxValue
                    : start + intervalLength;
                var index = LowerBoundSuggestionLocked(start);
                while (index < _suggestionsBySpellId.Length
                       && _suggestionsBySpellId[index].SpellId <= end)
                {
                    matches.Add(_suggestionsBySpellId[index]);
                    if (matches.Count >= limit)
                    {
                        return matches;
                    }

                    index++;
                }

                if (scale > long.MaxValue / 10)
                {
                    break;
                }

                scale *= 10;
            }

            return matches;
        }
    }

    public static Image? GetLastRuleRowIcon()
    {
        lock (SyncRoot)
        {
            return _package is null
                ? null
                : GetNamedIconLocked("last-rule-row", LastRuleRowIconResource);
        }
    }

    internal static void ValidatePackage(string path)
    {
        using var package = SpellIconPackage.Open(path);
    }

    internal static void InstallPackage(string downloadedPath)
    {
        ValidatePackage(downloadedPath);

        var targetPath = PackagePath;
        var targetExisted = File.Exists(targetPath);
        var backupPath = $"{targetPath}.{Guid.NewGuid():N}.backup";
        Exception? failure = null;

        lock (SyncRoot)
        {
            var oldPackage = _package;
            _package = null;
            oldPackage?.Dispose();
            DisposeImageCachesLocked();

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                if (targetExisted)
                {
                    File.Replace(downloadedPath, targetPath, backupPath, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(downloadedPath, targetPath);
                }

                _package = SpellIconPackage.Open(targetPath);
                RebuildIndexesLocked();
            }
            catch (Exception ex)
            {
                failure = ex;
                TryRestorePreviousPackage(targetPath, backupPath, targetExisted);
                _package = SpellIconPackage.TryOpen(targetPath);
                RebuildIndexesLocked();
            }
        }

        TryDeleteFile(backupPath);
        CatalogChanged?.Invoke();

        if (failure is not null)
        {
            throw new IOException("安装技能图标数据包失败，已尝试恢复原数据包。", failure);
        }
    }

    private static void TryRestorePreviousPackage(string targetPath, string backupPath, bool targetExisted)
    {
        try
        {
            if (targetExisted && File.Exists(backupPath))
            {
                File.Move(backupPath, targetPath, overwrite: true);
            }
            else if (!targetExisted)
            {
                TryDeleteFile(targetPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 后续重新打开会把无法恢复的状态视为“未安装”。
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 临时/备份文件清理失败不覆盖主要操作结果。
        }
    }

    private static int LowerBoundSuggestionLocked(long spellId)
    {
        var low = 0;
        var high = _suggestionsBySpellId.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (_suggestionsBySpellId[middle].SpellId < spellId)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static Image CacheLocked(long spellId, Image icon)
    {
        if (Icons.TryGetValue(spellId, out var cached))
        {
            icon.Dispose();
            return cached;
        }

        Icons[spellId] = icon;
        return icon;
    }

    private static Image? GetNamedIconLocked(string cacheKey, string resourceName)
    {
        if (NamedIcons.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var icon = LoadResource(resourceName);
        NamedIcons[cacheKey] = icon;
        return icon;
    }

    private static Image? LoadResource(string resourceName)
    {
        using var stream = typeof(SpellIconCatalog).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        try
        {
            using var source = Image.FromStream(stream);
            return new Bitmap(source);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static void RebuildIndexesLocked()
    {
        _spellIdsByName = new Dictionary<string, long>(RegisteredSpellIdsByName, StringComparer.Ordinal);
        if (_package is null)
        {
            _suggestionsBySpellId = [];
            return;
        }

        foreach (var (name, spellId) in _package.SpellIdsByName)
        {
            _spellIdsByName.TryAdd(name, spellId);
        }

        _suggestionsBySpellId = _package.SpellNamesById
            .Where(pair => pair.Key > 0 && !string.IsNullOrWhiteSpace(pair.Value))
            .OrderBy(pair => pair.Key)
            .Select(pair => new SpellSuggestion(pair.Key, pair.Value))
            .ToArray();
    }

    private static void DisposeImageCachesLocked()
    {
        foreach (var image in Icons.Values)
        {
            image.Dispose();
        }

        Icons.Clear();
        foreach (var image in NamedIcons.Values)
        {
            image?.Dispose();
        }

        NamedIcons.Clear();
    }

    private sealed class SpellIconPackage : IDisposable
    {
        private static readonly byte[] Magic = "SHGICN1\0"u8.ToArray();
        private const int Version = 1;
        private const int HeaderSize = 56;
        private const int RecordSize = 12;

        private readonly FileStream _stream;
        private readonly long[] _spellIds;
        private readonly int[] _iconIndices;
        private readonly long[] _iconOffsets;
        private readonly int[] _iconLengths;

        private SpellIconPackage(string path)
        {
            _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                using var reader = new BinaryReader(_stream, System.Text.Encoding.UTF8, leaveOpen: true);
                if (!reader.ReadBytes(Magic.Length).SequenceEqual(Magic)
                    || reader.ReadInt32() != Version)
                {
                    throw new InvalidDataException("Unsupported spell icon package.");
                }

                var spellCount = reader.ReadInt32();
                var iconCount = reader.ReadInt32();
                var nameCount = reader.ReadInt32();
                var spellMapOffset = reader.ReadInt64();
                var iconIndexOffset = reader.ReadInt64();
                var nameIndexOffset = reader.ReadInt64();
                var dataOffset = reader.ReadInt64();
                if (spellCount is < 1 or > 2_000_000
                    || iconCount is < 1 or > 100_000
                    || nameCount is < 0 or > 2_000_000
                    || spellMapOffset != HeaderSize
                    || iconIndexOffset != spellMapOffset + (long)spellCount * RecordSize
                    || nameIndexOffset != iconIndexOffset + (long)iconCount * RecordSize
                    || dataOffset < nameIndexOffset
                    || dataOffset > _stream.Length)
                {
                    throw new InvalidDataException("Invalid spell icon package header.");
                }

                _spellIds = new long[spellCount];
                _iconIndices = new int[spellCount];
                _stream.Position = spellMapOffset;
                for (var index = 0; index < spellCount; index++)
                {
                    var spellId = reader.ReadInt64();
                    var iconIndex = reader.ReadInt32();
                    if (spellId <= 0
                        || index > 0 && spellId <= _spellIds[index - 1]
                        || iconIndex < 0
                        || iconIndex >= iconCount)
                    {
                        throw new InvalidDataException("Invalid spell map in icon package.");
                    }

                    _spellIds[index] = spellId;
                    _iconIndices[index] = iconIndex;
                }

                _iconOffsets = new long[iconCount];
                _iconLengths = new int[iconCount];
                _stream.Position = iconIndexOffset;
                for (var index = 0; index < iconCount; index++)
                {
                    var offset = reader.ReadInt64();
                    var length = reader.ReadInt32();
                    if (offset < dataOffset
                        || length is < 512 or > 10 * 1024 * 1024
                        || offset > _stream.Length - length)
                    {
                        throw new InvalidDataException("Invalid image index in icon package.");
                    }

                    _iconOffsets[index] = offset;
                    _iconLengths[index] = length;
                }

                SpellIdsByName = new Dictionary<string, long>(StringComparer.Ordinal);
                SpellNamesById = new Dictionary<long, string>();
                _stream.Position = nameIndexOffset;
                for (var index = 0; index < nameCount; index++)
                {
                    var spellId = reader.ReadInt64();
                    var byteLength = reader.ReadInt32();
                    if (spellId <= 0
                        || byteLength is < 1 or > 4096
                        || _stream.Position > dataOffset - byteLength)
                    {
                        throw new InvalidDataException("Invalid name index in icon package.");
                    }

                    var name = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(byteLength));
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        SpellIdsByName.TryAdd(name, spellId);
                        SpellNamesById.TryAdd(spellId, name);
                    }
                }

                if (_stream.Position != dataOffset)
                {
                    throw new InvalidDataException("Spell icon package index size mismatch.");
                }
            }
            catch
            {
                _stream.Dispose();
                throw;
            }
        }

        public Dictionary<string, long> SpellIdsByName { get; }
        public Dictionary<long, string> SpellNamesById { get; }

        public static SpellIconPackage Open(string path) => new(path);

        public static SpellIconPackage? TryOpen(string path)
        {
            try
            {
                return File.Exists(path) ? Open(path) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or InvalidDataException or ArgumentException)
            {
                return null;
            }
        }

        public Image? LoadIcon(long spellId)
        {
            var spellIndex = Array.BinarySearch(_spellIds, spellId);
            if (spellIndex < 0)
            {
                return null;
            }

            var iconIndex = _iconIndices[spellIndex];
            var bytes = new byte[_iconLengths[iconIndex]];
            try
            {
                _stream.Position = _iconOffsets[iconIndex];
                _stream.ReadExactly(bytes);
                using var memory = new MemoryStream(bytes, writable: false);
                using var source = Image.FromStream(memory);
                return new Bitmap(source);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or ObjectDisposedException)
            {
                return null;
            }
        }

        public void Dispose() => _stream.Dispose();
    }
}
