using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Shigure;

internal sealed record SpellSuggestion(long SpellId, string Name);

internal sealed record ItemSuggestion(long ItemId, string Name);

/// <summary>
/// 技能/物品名称与 ID 到图标的只读目录。完整目录只来自外置数据包；数据包缺失或
/// 损坏时，技能图标与 spellId 联想均不可用。仅技能旧包仍可加载技能，此时物品搜索库关闭。
/// </summary>
internal static class SpellIconCatalog
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<long, Image> Icons = new();
    private static readonly Dictionary<long, Image> ItemIcons = new();
    private static readonly Dictionary<string, Image?> NamedIcons = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, long> RegisteredSpellIdsByName = new(StringComparer.Ordinal);
    private static readonly Dictionary<long, string> RegisteredSpellNamesById = new();
    private static readonly Dictionary<string, long> RegisteredItemIdsByName = new(StringComparer.Ordinal);
    private static readonly Dictionary<long, string> RegisteredItemNamesById = new();
    private static readonly Regex ItemReferenceRegex = new(
        @"item:(\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

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
        [ModuleSpecialActions.FailedItem] = $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.auto-insert-spell.png",
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
    private static Dictionary<string, long> _itemIdsByName = new(StringComparer.Ordinal);
    private static IReadOnlyList<SpellSuggestion> _suggestionsBySpellId = Array.Empty<SpellSuggestion>();
    private static IReadOnlyList<ItemSuggestion> _suggestionsByItemId = Array.Empty<ItemSuggestion>();

    static SpellIconCatalog()
    {
        PromotePendingPackage();
        _package = SpellIconPackage.TryOpen(PackagePath);
        RebuildIndexesLocked();
    }

    internal static event Action? CatalogChanged;

    internal static string PackagePath => Path.Combine(
        AppContext.BaseDirectory,
        "data",
        "SpellIcons.shgpack");

    internal static string PendingPackagePath => $"{PackagePath}.pending";

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

    internal static bool IsItemDatabaseAvailable
    {
        get
        {
            lock (SyncRoot)
            {
                return _package is { HasItemDatabase: true };
            }
        }
    }

    private static void PromotePendingPackage()
    {
        if (!File.Exists(PendingPackagePath))
        {
            return;
        }

        try
        {
            using (SpellIconPackage.Open(PendingPackagePath))
            {
            }

            Directory.CreateDirectory(Path.GetDirectoryName(PackagePath)!);
            File.Move(PendingPackagePath, PackagePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or InvalidDataException or ArgumentException)
        {
            // 保留当前可用数据包；待下次启动时再次尝试应用已校验的名称修复包。
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

            icon ??= _package.LoadSpellIcon(spellId);
            return icon is null ? null : CacheLocked(Icons, spellId, icon);
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

    public static Image? GetItem(long itemId)
    {
        if (itemId <= 0)
        {
            return null;
        }

        lock (SyncRoot)
        {
            if (_package is not { HasItemDatabase: true })
            {
                return null;
            }

            if (ItemIcons.TryGetValue(itemId, out var cached))
            {
                return cached;
            }

            var icon = _package.LoadItemIcon(itemId);
            return icon is null ? null : CacheLocked(ItemIcons, itemId, icon);
        }
    }

    public static Image? GetItem(string? itemName)
    {
        return TryResolveItemId(itemName, out var itemId)
            ? GetItem(itemId)
            : null;
    }

    public static void Register(long spellId, string? spellName, bool overwriteIdName = false)
    {
        var normalized = spellName?.Trim();
        if (spellId <= 0 || string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        lock (SyncRoot)
        {
            RegisteredSpellIdsByName[normalized] = spellId;
            if (overwriteIdName)
            {
                RegisteredSpellNamesById[spellId] = normalized;
            }
            else
            {
                RegisteredSpellNamesById.TryAdd(spellId, normalized);
            }

            _spellIdsByName[normalized] = spellId;
        }
    }

    public static void RegisterItem(long itemId, string? itemName)
    {
        var normalized = itemName?.Trim();
        if (itemId <= 0 || string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        lock (SyncRoot)
        {
            RegisteredItemIdsByName[normalized] = itemId;
            RegisteredItemNamesById.TryAdd(itemId, normalized);
            _itemIdsByName[normalized] = itemId;
        }
    }

    internal static string? ResolveSuggestionName(long spellId, string? packageName)
    {
        if (!string.IsNullOrWhiteSpace(packageName))
        {
            return packageName;
        }

        lock (SyncRoot)
        {
            return RegisteredSpellNamesById.GetValueOrDefault(spellId);
        }
    }

    internal static string? GetSpellName(long spellId)
    {
        lock (SyncRoot)
        {
            return _package?.SpellNamesById.GetValueOrDefault(spellId)
                ?? RegisteredSpellNamesById.GetValueOrDefault(spellId);
        }
    }

    internal static string? ResolveItemSuggestionName(long itemId, string? packageName)
    {
        if (!string.IsNullOrWhiteSpace(packageName))
        {
            return packageName;
        }

        lock (SyncRoot)
        {
            return RegisteredItemNamesById.GetValueOrDefault(itemId)
                   ?? _package?.ItemNamesById.GetValueOrDefault(itemId);
        }
    }

    internal static IReadOnlyDictionary<long, string> GetRegisteredSpellNamesSnapshot()
    {
        lock (SyncRoot)
        {
            return new Dictionary<long, string>(RegisteredSpellNamesById);
        }
    }

    internal static IReadOnlyDictionary<long, string> GetRegisteredItemNamesSnapshot()
    {
        lock (SyncRoot)
        {
            return new Dictionary<long, string>(RegisteredItemNamesById);
        }
    }

    internal static IReadOnlyList<SpellSuggestion> GetSuggestionsSnapshot()
    {
        lock (SyncRoot)
        {
            return _package is null
                ? Array.Empty<SpellSuggestion>()
                : _suggestionsBySpellId;
        }
    }

    internal static IReadOnlyList<ItemSuggestion> GetItemSuggestionsSnapshot()
    {
        lock (SyncRoot)
        {
            return _package is { HasItemDatabase: true }
                ? _suggestionsByItemId
                : Array.Empty<ItemSuggestion>();
        }
    }

    internal static bool TryResolveItemId(string? text, out long itemId)
    {
        itemId = 0;
        var normalized = text?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (TryParseItemReference(normalized, out itemId))
        {
            return true;
        }

        lock (SyncRoot)
        {
            return _itemIdsByName.TryGetValue(normalized, out itemId);
        }
    }

    internal static bool TryParseItemReference(string? text, out long itemId)
    {
        itemId = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = ItemReferenceRegex.Match(text);
        return match.Success
               && long.TryParse(
                   match.Groups[1].Value,
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out itemId)
               && itemId > 0;
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
            throw new IOException("安装技能/物品图标数据包失败，已尝试恢复原数据包。", failure);
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

    private static Image CacheLocked(Dictionary<long, Image> cache, long id, Image icon)
    {
        if (cache.TryGetValue(id, out var cached))
        {
            icon.Dispose();
            return cached;
        }

        cache[id] = icon;
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
        _itemIdsByName = new Dictionary<string, long>(RegisteredItemIdsByName, StringComparer.Ordinal);
        if (_package is null)
        {
            _suggestionsBySpellId = Array.Empty<SpellSuggestion>();
            _suggestionsByItemId = Array.Empty<ItemSuggestion>();
            return;
        }

        foreach (var (name, spellId) in _package.SpellIdsByName)
        {
            _spellIdsByName.TryAdd(name, spellId);
        }

        _suggestionsBySpellId = Array.AsReadOnly(
            _package.SpellIds
                .Select(spellId => new SpellSuggestion(
                    spellId,
                    _package.SpellNamesById.GetValueOrDefault(spellId) ?? string.Empty))
                .ToArray());

        if (!_package.HasItemDatabase)
        {
            _suggestionsByItemId = Array.Empty<ItemSuggestion>();
            return;
        }

        foreach (var (name, itemId) in _package.ItemIdsByName)
        {
            _itemIdsByName.TryAdd(name, itemId);
        }

        _suggestionsByItemId = Array.AsReadOnly(
            _package.ItemIds
                .Select(itemId => new ItemSuggestion(
                    itemId,
                    _package.ItemNamesById.GetValueOrDefault(itemId) ?? string.Empty))
                .ToArray());
    }

    private static void DisposeImageCachesLocked()
    {
        foreach (var image in Icons.Values)
        {
            image.Dispose();
        }

        Icons.Clear();
        foreach (var image in ItemIcons.Values)
        {
            image.Dispose();
        }

        ItemIcons.Clear();
        foreach (var image in NamedIcons.Values)
        {
            image?.Dispose();
        }

        NamedIcons.Clear();
    }

    private sealed class SpellIconPackage : IDisposable
    {
        private static readonly byte[] Magic = "SHGICN1\0"u8.ToArray();
        private static readonly byte[] ItemFooterMagic = "SHGITM1\0"u8.ToArray();
        private const int Version = 1;
        private const int HeaderSize = 56;
        private const int RecordSize = 12;
        private const int ItemFooterSize = 48;

        private readonly FileStream _stream;
        private readonly long[] _spellIds;
        private readonly int[] _spellIconIndices;
        private readonly long[] _itemIds;
        private readonly int[] _itemIconIndices;
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
                _spellIconIndices = new int[spellCount];
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
                    _spellIconIndices[index] = iconIndex;
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

                ItemIdsByName = new Dictionary<string, long>(StringComparer.Ordinal);
                ItemNamesById = new Dictionary<long, string>();
                if (!TryReadItemExtension(reader, dataOffset, iconCount, out _itemIds, out _itemIconIndices))
                {
                    _itemIds = [];
                    _itemIconIndices = [];
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
        public Dictionary<string, long> ItemIdsByName { get; }
        public Dictionary<long, string> ItemNamesById { get; }
        public IReadOnlyList<long> SpellIds => _spellIds;
        public IReadOnlyList<long> ItemIds => _itemIds;
        public bool HasItemDatabase => _itemIds.Length > 0;

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

        public Image? LoadSpellIcon(long spellId)
        {
            var spellIndex = Array.BinarySearch(_spellIds, spellId);
            return spellIndex < 0 ? null : LoadIconBlob(_spellIconIndices[spellIndex]);
        }

        public Image? LoadItemIcon(long itemId)
        {
            var itemIndex = Array.BinarySearch(_itemIds, itemId);
            return itemIndex < 0 ? null : LoadIconBlob(_itemIconIndices[itemIndex]);
        }

        public void Dispose() => _stream.Dispose();

        private bool TryReadItemExtension(
            BinaryReader reader,
            long dataOffset,
            int iconCount,
            out long[] itemIds,
            out int[] itemIconIndices)
        {
            itemIds = [];
            itemIconIndices = [];
            if (_stream.Length < dataOffset + ItemFooterSize)
            {
                return false;
            }

            var footerOffset = _stream.Length - ItemFooterSize;
            if (footerOffset < dataOffset)
            {
                return false;
            }

            _stream.Position = footerOffset;
            var magic = reader.ReadBytes(ItemFooterMagic.Length);
            if (!magic.SequenceEqual(ItemFooterMagic))
            {
                return false;
            }

            var itemCount = reader.ReadInt32();
            var itemNameCount = reader.ReadInt32();
            var itemMapOffset = reader.ReadInt64();
            var itemNameOffset = reader.ReadInt64();
            _ = reader.ReadInt64();
            _ = reader.ReadInt64();
            if (itemCount is < 1 or > 2_000_000
                || itemNameCount is < 0 or > 2_000_000
                || itemMapOffset < dataOffset
                || itemNameOffset != itemMapOffset + (long)itemCount * RecordSize
                || itemNameOffset > footerOffset)
            {
                throw new InvalidDataException("Invalid item extension footer in icon package.");
            }

            itemIds = new long[itemCount];
            itemIconIndices = new int[itemCount];
            _stream.Position = itemMapOffset;
            for (var index = 0; index < itemCount; index++)
            {
                var itemId = reader.ReadInt64();
                var iconIndex = reader.ReadInt32();
                if (itemId <= 0
                    || index > 0 && itemId <= itemIds[index - 1]
                    || iconIndex < 0
                    || iconIndex >= iconCount)
                {
                    throw new InvalidDataException("Invalid item map in icon package.");
                }

                itemIds[index] = itemId;
                itemIconIndices[index] = iconIndex;
            }

            _stream.Position = itemNameOffset;
            for (var index = 0; index < itemNameCount; index++)
            {
                var itemId = reader.ReadInt64();
                var byteLength = reader.ReadInt32();
                if (itemId <= 0
                    || byteLength is < 1 or > 4096
                    || _stream.Position > footerOffset - byteLength)
                {
                    throw new InvalidDataException("Invalid item name index in icon package.");
                }

                var name = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(byteLength));
                if (!string.IsNullOrWhiteSpace(name))
                {
                    ItemIdsByName.TryAdd(name, itemId);
                    ItemNamesById.TryAdd(itemId, name);
                }
            }

            if (_stream.Position != footerOffset)
            {
                throw new InvalidDataException("Item name table size mismatch in icon package.");
            }

            return true;
        }

        private Image? LoadIconBlob(int iconIndex)
        {
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
    }
}
