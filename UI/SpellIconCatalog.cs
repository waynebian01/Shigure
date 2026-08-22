using System.Drawing;
using System.Text.Json;

namespace Shigure;

/// <summary>
/// 技能名称/ID 到本地嵌入式技能图标的只读目录。
/// 图标由 Tools/Download-WowSpellIcons.ps1 从 Wowhead tooltip 与 Zamimg 缓存生成，
/// 因而编辑器运行时不依赖网络。
/// </summary>
internal static class SpellIconCatalog
{
    private static readonly Dictionary<long, Image?> Icons = new();
    private static readonly Dictionary<string, Image?> NamedIcons = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, long> SpellIdsByName = LoadSpellIdsByName();
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

    public static Image? Get(long spellId)
    {
        if (Icons.TryGetValue(spellId, out var cached))
        {
            return cached;
        }

        var resourceName = SpellIdIconResources.GetValueOrDefault(spellId)
            ?? $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.spell-{spellId}.jpg";
        using var stream = typeof(SpellIconCatalog).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            Icons[spellId] = null;
            return null;
        }

        using var source = Image.FromStream(stream);
        var icon = new Bitmap(source);
        Icons[spellId] = icon;
        return icon;
    }

    public static Image? Get(string? spellName)
    {
        var normalized = spellName?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (NamedIconResources.TryGetValue(normalized, out var resourceName))
        {
            return GetNamedIcon(normalized, resourceName);
        }

        return TryResolveId(normalized, out var spellId) ? Get(spellId) : null;
    }

    public static Image? GetLastRuleRowIcon()
        => GetNamedIcon("last-rule-row", LastRuleRowIconResource);

    private static Image? GetNamedIcon(string cacheKey, string resourceName)
    {
        if (NamedIcons.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        using var stream = typeof(SpellIconCatalog).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            NamedIcons[cacheKey] = null;
            return null;
        }

        using var source = Image.FromStream(stream);
        var icon = new Bitmap(source);
        NamedIcons[cacheKey] = icon;
        return icon;
    }

    private static bool TryResolveId(string? spellName, out long spellId)
    {
        spellId = 0;
        var normalized = spellName?.Trim();
        return !string.IsNullOrWhiteSpace(normalized)
            && SpellIdsByName.TryGetValue(normalized, out spellId);
    }

    private static Dictionary<string, long> LoadSpellIdsByName()
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        var resourceName = $"{typeof(SpellIconCatalog).Namespace}.Assets.SpellIconManifest.json";
        using var stream = typeof(SpellIconCatalog).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("spells", out var spells)
                || spells.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var spell in spells.EnumerateArray())
            {
                if (!spell.TryGetProperty("spellId", out var idElement)
                    || !idElement.TryGetInt64(out var id)
                    || !spell.TryGetProperty("name", out var nameElement))
                {
                    continue;
                }

                var name = nameElement.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(name) && !result.ContainsKey(name))
                {
                    result[name] = id;
                }
            }
        }
        catch (JsonException)
        {
            // 缺少或损坏清单时由各表格显示空图标，不影响编辑功能。
        }

        return result;
    }
}
