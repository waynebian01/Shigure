using System.Drawing;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Shigure;

internal interface ISpellIconPackageCatalog
{
    Dictionary<string, long> SpellIdsByName { get; }
    Dictionary<long, string> SpellNamesById { get; }
    Dictionary<string, long> ItemIdsByName { get; }
    Dictionary<long, string> ItemNamesById { get; }
    IReadOnlyList<long> SpellIds { get; }
    IReadOnlyList<long> ItemIds { get; }
    bool HasItemDatabase { get; }
    Image? LoadSpellIcon(long spellId);
    Image? LoadItemIcon(long itemId);
}

internal sealed record GameDataCatalogInfo(string Version, string? Build, string Locale, int SpellCount, int ItemCount);

/// <summary>v1 仅属于 Retail；v2 用版本索引引用共享的 SHA-256 图标内容。</summary>
internal sealed class GameDataPackage : IDisposable
{
    private readonly LegacySpellIconPackage? _legacy;
    private readonly ZipArchive? _archive;
    private readonly Dictionary<string, ISpellIconPackageCatalog> _catalogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GameDataCatalogInfo> _infos = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _iconHashes = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private sealed record Manifest(int FormatVersion, int IconCount, CatalogDescriptor[] Catalogs);
    private sealed record CatalogDescriptor(string Version, string? Build, string Locale, int SpellCount, int ItemCount, string Spells, string Items);
    private sealed record Entry(long Id, string Name, string? Icon);

    private GameDataPackage(string path)
    {
        using var probe = File.OpenRead(path);
        Span<byte> magic = stackalloc byte[8];
        probe.ReadExactly(magic);
        if (magic.SequenceEqual("SHGICN1\0"u8))
        {
            _legacy = LegacySpellIconPackage.Open(path);
            _catalogs.Add("Retail", _legacy);
            _infos.Add("Retail", new("Retail", null, "原包导入", _legacy.SpellIds.Count, _legacy.ItemIds.Count));
            return;
        }

        try
        {
            _archive = ZipFile.OpenRead(path);
            if (_archive.Entries.Count > 100_005
                || _archive.Entries.Select(e => e.FullName).Distinct(StringComparer.Ordinal).Count() != _archive.Entries.Count)
            {
                throw new InvalidDataException("数据包条目数量无效或路径重复。");
            }
            var manifest = ReadJson<Manifest>("manifest.json", 1024 * 1024);
            if (manifest.FormatVersion != 2 || manifest.Catalogs is not { Length: 2 }
                || manifest.IconCount is < 1 or > 100_000)
            {
                throw new InvalidDataException("不支持的数据包版本或版本目录。");
            }
            var iconEntries = _archive.Entries.Where(e => e.FullName.StartsWith("icons/", StringComparison.Ordinal)).ToArray();
            if (iconEntries.Length != manifest.IconCount || _archive.Entries.Count != manifest.IconCount + 5)
            {
                throw new InvalidDataException("数据包图标数量不一致。");
            }
            foreach (var icon in iconEntries)
            {
                var hash = Path.GetFileNameWithoutExtension(icon.FullName);
                if (!IsHash(hash) || icon.FullName != $"icons/{hash}.jpg" || icon.Length is < 512 or > 10 * 1024 * 1024)
                {
                    throw new InvalidDataException("图标索引无效。");
                }
                _iconHashes.Add(hash, hash);
            }
            foreach (var descriptor in manifest.Catalogs)
            {
                if (descriptor is null || descriptor.Version is not ("Retail" or "Forever") || string.IsNullOrWhiteSpace(descriptor.Locale)
                    || descriptor.SpellCount is < 1 or > 2_000_000 || descriptor.ItemCount is < 0 or > 2_000_000
                    || descriptor.Spells != $"catalogs/{descriptor.Version}/spells.json"
                    || descriptor.Items != $"catalogs/{descriptor.Version}/items.json"
                    || _catalogs.ContainsKey(descriptor.Version))
                {
                    throw new InvalidDataException("技能/物品版本索引无效。");
                }
                var spells = ReadJson<Entry[]>(descriptor.Spells, 256L * 1024 * 1024);
                var items = ReadJson<Entry[]>(descriptor.Items, 256L * 1024 * 1024);
                if (spells.Length != descriptor.SpellCount || items.Length != descriptor.ItemCount)
                {
                    throw new InvalidDataException("版本目录记录数量不一致。");
                }
                _catalogs.Add(descriptor.Version, new Catalog(this, spells, items));
                _infos.Add(descriptor.Version, new(descriptor.Version, descriptor.Build, descriptor.Locale, spells.Length, items.Length));
            }
            if (!_catalogs.ContainsKey("Retail") || !_catalogs.ContainsKey("Forever"))
            {
                throw new InvalidDataException("v2 数据包必须包含 Retail 和 Forever。");
            }
        }
        catch
        {
            _archive?.Dispose();
            throw;
        }
    }

    internal ISpellIconPackageCatalog? ForVersion(string version) => _catalogs.GetValueOrDefault(version);
    internal GameDataCatalogInfo? GetInfo(string version) => _infos.GetValueOrDefault(version);
    internal bool HasForever => _catalogs.ContainsKey("Forever");
    internal static GameDataPackage Open(string path) => new(path);
    internal static GameDataPackage? TryOpen(string path, bool verifyIcons = false)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var package = Open(path);
            try
            {
                if (verifyIcons) package.VerifyIconHashes();
                return package;
            }
            catch { package.Dispose(); throw; }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException
            or ArgumentException or JsonException or NotSupportedException) { return null; }
    }

    internal void VerifyIconHashes()
    {
        if (_archive is null) return;
        foreach (var entry in _archive.Entries.Where(e => e.FullName.StartsWith("icons/", StringComparison.Ordinal)))
        {
            using var stream = entry.Open();
            if (!Convert.ToHexStringLower(SHA256.HashData(stream)).Equals(Path.GetFileNameWithoutExtension(entry.FullName), StringComparison.Ordinal))
            {
                throw new InvalidDataException($"图标内容校验失败：{entry.FullName}");
            }
        }
    }

    private T ReadJson<T>(string name, long maxLength)
    {
        var entry = _archive!.GetEntry(name) ?? throw new InvalidDataException($"数据包缺少 {name}。");
        if (entry.Length <= 0 || entry.Length > maxLength) throw new InvalidDataException("目录内容大小无效。");
        using var stream = entry.Open();
        return JsonSerializer.Deserialize<T>(stream, JsonOptions) ?? throw new InvalidDataException("目录内容为空。");
    }

    private static bool IsHash(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private Image? LoadIcon(string? hash)
    {
        if (hash is null) return null;
        try
        {
            using var stream = _archive!.GetEntry($"icons/{hash}.jpg")!.Open();
            using var source = Image.FromStream(stream);
            return new Bitmap(source);
        }
        catch (Exception error) when (error is IOException or ArgumentException or ObjectDisposedException) { return null; }
    }

    public void Dispose() { _archive?.Dispose(); _legacy?.Dispose(); }

    private sealed class Catalog : ISpellIconPackageCatalog
    {
        private readonly GameDataPackage _owner;
        private readonly Dictionary<long, string?> _spellIcons = new();
        private readonly Dictionary<long, string?> _itemIcons = new();
        public Dictionary<string, long> SpellIdsByName { get; } = new(StringComparer.Ordinal);
        public Dictionary<long, string> SpellNamesById { get; } = new();
        public Dictionary<string, long> ItemIdsByName { get; } = new(StringComparer.Ordinal);
        public Dictionary<long, string> ItemNamesById { get; } = new();
        public IReadOnlyList<long> SpellIds { get; }
        public IReadOnlyList<long> ItemIds { get; }
        public bool HasItemDatabase => ItemIds.Count > 0;
        internal Catalog(GameDataPackage owner, Entry[] spells, Entry[] items)
        {
            _owner = owner;
            AddEntries(spells, SpellNamesById, SpellIdsByName, _spellIcons);
            AddEntries(items, ItemNamesById, ItemIdsByName, _itemIcons);
            SpellIds = Array.AsReadOnly(spells.Select(e => e.Id).ToArray());
            ItemIds = Array.AsReadOnly(items.Select(e => e.Id).ToArray());
        }
        private void AddEntries(Entry[] entries, Dictionary<long, string> names, Dictionary<string, long> ids, Dictionary<long, string?> icons)
        {
            long previous = 0;
            foreach (var entry in entries)
            {
                if (entry is null || entry.Id <= previous || entry.Name is null || entry.Name.Length > 4096
                    || entry.Icon is not null && !_owner._iconHashes.ContainsKey(entry.Icon))
                {
                    throw new InvalidDataException("记录顺序、名称或图标引用无效。");
                }
                previous = entry.Id;
                names.Add(entry.Id, entry.Name);
                if (!string.IsNullOrWhiteSpace(entry.Name)) ids.TryAdd(entry.Name, entry.Id);
                // 数十万条记录复用图标摘要字符串，避免重复持有整份 JSON 中的摘要文本。
                icons.Add(entry.Id, entry.Icon is null ? null : _owner._iconHashes[entry.Icon]);
            }
        }
        public Image? LoadSpellIcon(long id) => _owner.LoadIcon(_spellIcons.GetValueOrDefault(id));
        public Image? LoadItemIcon(long id) => _owner.LoadIcon(_itemIcons.GetValueOrDefault(id));
    }
}
