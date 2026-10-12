using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Shigure;

/// <summary>
/// 技能/物品名称与 ID 到图标的只读目录。完整目录只来自外置数据包；数据包缺失或
/// 损坏时，技能图标与 spellId 联想均不可用。仅技能旧包仍可加载技能，此时物品搜索库关闭。
/// </summary>
internal sealed class VersionedSpellIconCatalog
{
    private object SyncRoot => SpellIconCatalog.SyncRoot;
    private readonly Dictionary<long, Image> Icons = new();
    private readonly Dictionary<long, Image> ItemIcons = new();
    private readonly Dictionary<string, Image?> NamedIcons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> RegisteredSpellIdsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<long, string> RegisteredSpellNamesById = new();
    private readonly Dictionary<string, long> RegisteredItemIdsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<long, string> RegisteredItemNamesById = new();
    private static readonly Regex ItemReferenceRegex = new(
        @"item:(\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly Dictionary<long, string> SpellIdIconResources = new()
    {
        [35395] = $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.crusader-strike.png"
    };

    private readonly Dictionary<string, string> NamedIconResources = new(StringComparer.Ordinal)
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

    private ISpellIconPackageCatalog? _package => SpellIconCatalog.Package?.ForVersion(Version);
    internal string Version { get; }
    internal VersionedSpellIconCatalog(string version) { Version = version; }
    internal GameDataCatalogInfo? Info { get { lock (SyncRoot) return SpellIconCatalog.Package?.GetInfo(Version); } }
    internal bool IsPackageAvailable { get { lock (SyncRoot) return _package is not null; } }
    internal bool IsItemDatabaseAvailable { get { lock (SyncRoot) return _package is { HasItemDatabase: true }; } }
    private Dictionary<string, long> _spellIdsByName = new(StringComparer.Ordinal);
    private Dictionary<string, long> _itemIdsByName = new(StringComparer.Ordinal);
    private IReadOnlyList<SpellSuggestion> _suggestionsBySpellId = Array.Empty<SpellSuggestion>();
    private IReadOnlyList<ItemSuggestion> _suggestionsByItemId = Array.Empty<ItemSuggestion>();

    public Image? Get(long spellId)
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
            if (Version == "Retail" && SpellIdIconResources.TryGetValue(spellId, out var resourceName))
            {
                icon = LoadResource(resourceName);
            }

            icon ??= _package.LoadSpellIcon(spellId);
            return icon is null
                ? (_package.SpellNamesById.ContainsKey(spellId) ? GetPlaceholderIconLocked() : null)
                : CacheLocked(Icons, spellId, icon);
        }
    }

    public Image? Get(string? spellName)
    {
        var normalized = spellName?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        lock (SyncRoot)
        {
            if ((Version == "Retail" || normalized is ModuleSpecialActions.OneKeySpell or ModuleSpecialActions.PauseSpell
                or ModuleSpecialActions.FailedSpell or ModuleSpecialActions.FailedItem or "停止施法")
                && NamedIconResources.TryGetValue(normalized, out var resourceName))
            {
                return GetNamedIconLocked(normalized, resourceName);
            }

            return _package is not null && _spellIdsByName.TryGetValue(normalized, out var spellId)
                ? Get(spellId)
                : null;
        }
    }

    public Image? GetItem(long itemId)
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
            return icon is null
                ? (_package.ItemNamesById.ContainsKey(itemId) ? GetPlaceholderIconLocked() : null)
                : CacheLocked(ItemIcons, itemId, icon);
        }
    }

    public Image? GetItem(string? itemName)
    {
        return TryResolveItemId(itemName, out var itemId)
            ? GetItem(itemId)
            : null;
    }

    public void Register(long spellId, string? spellName, bool overwriteIdName = false)
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

    public void RegisterItem(long itemId, string? itemName)
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

    internal string? ResolveSuggestionName(long spellId, string? packageName)
    {
        if (!string.IsNullOrWhiteSpace(packageName))
        {
            return packageName;
        }

        lock (SyncRoot)
        {
            return RegisteredSpellNamesById.GetValueOrDefault(spellId)
                ?? _package?.SpellNamesById.GetValueOrDefault(spellId);
        }
    }

    internal string? GetSpellName(long spellId)
    {
        lock (SyncRoot)
        {
            return _package?.SpellNamesById.GetValueOrDefault(spellId)
                ?? RegisteredSpellNamesById.GetValueOrDefault(spellId);
        }
    }

    internal string? ResolveItemSuggestionName(long itemId, string? packageName)
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

    internal IReadOnlyDictionary<long, string> GetRegisteredSpellNamesSnapshot()
    {
        lock (SyncRoot)
        {
            return new Dictionary<long, string>(RegisteredSpellNamesById);
        }
    }

    internal IReadOnlyDictionary<long, string> GetRegisteredItemNamesSnapshot()
    {
        lock (SyncRoot)
        {
            return new Dictionary<long, string>(RegisteredItemNamesById);
        }
    }

    internal IReadOnlyList<SpellSuggestion> GetSuggestionsSnapshot()
    {
        lock (SyncRoot)
        {
            return _package is null
                ? Array.Empty<SpellSuggestion>()
                : _suggestionsBySpellId;
        }
    }

    internal IReadOnlyList<ItemSuggestion> GetItemSuggestionsSnapshot()
    {
        lock (SyncRoot)
        {
            return _package is { HasItemDatabase: true }
                ? _suggestionsByItemId
                : Array.Empty<ItemSuggestion>();
        }
    }

    internal bool TryResolveItemId(string? text, out long itemId)
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

    internal bool TryParseItemReference(string? text, out long itemId)
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

    public Image? GetLastRuleRowIcon()
    {
        lock (SyncRoot)
        {
            return GetNamedIconLocked("last-rule-row", LastRuleRowIconResource);
        }
    }

    private Image GetPlaceholderIconLocked()
    {
        if (NamedIcons.TryGetValue("missing-record-icon", out var cached) && cached is not null) return cached;
        var icon = new Bitmap(24, 24);
        using var graphics = Graphics.FromImage(icon);
        graphics.Clear(UiTheme.Field);
        using var font = new Font("Segoe UI", 13F, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(UiTheme.Muted);
        graphics.DrawString("?", font, brush, 6, 3);
        NamedIcons["missing-record-icon"] = icon;
        return icon;
    }

    private Image CacheLocked(Dictionary<long, Image> cache, long id, Image icon)
    {
        if (cache.TryGetValue(id, out var cached))
        {
            icon.Dispose();
            return cached;
        }

        cache[id] = icon;
        return icon;
    }

    private Image? GetNamedIconLocked(string cacheKey, string resourceName)
    {
        if (NamedIcons.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var icon = LoadResource(resourceName);
        NamedIcons[cacheKey] = icon;
        return icon;
    }

    private Image? LoadResource(string resourceName)
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

    internal void RebuildIndexesLocked()
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

    internal void DisposeImageCachesLocked()
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

}
