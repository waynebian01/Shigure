using System.Globalization;
using System.Text.Json;

namespace Shigure;

internal sealed record DungeonSpellInfo(
    string Name, long SpellId, int? Boss, int? MapId, string Type,
    string IconName, long IconId, IReadOnlyList<string> Npcs, IReadOnlyList<string> Classes);

internal sealed record DungeonSpellData(
    IReadOnlyList<DungeonSpellInfo> Spells,
    IReadOnlyDictionary<int, string> MapNames,
    IReadOnlyDictionary<int, string> BossNames,
    string? Error);

internal enum DungeonSpellFilter { Map, Boss, Type, Npc, Class }

/// <summary>副本技能参考数据；编号使用 Lua 表的值，而非地图或遭遇的原始 ID。</summary>
internal static class DungeonSpellCatalog
{
    public static DungeonSpellData Load(string baseDirectory)
    {
        var (maps, bosses) = LoadNames(Path.Combine(baseDirectory, "Retail", "Fuyutsui", "core", "config.lua"));
        try
        {
            using var stream = File.OpenRead(Path.Combine(baseDirectory, "Retail", "spell_icons.json"));
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("副本技能数据结构错误：根节点应为数组。");

            var spells = new List<DungeonSpellInfo>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("副本技能数据结构错误：技能记录应为对象。");
                spells.Add(new DungeonSpellInfo(
                    ReadText(item, "name"), ReadId(item, "spellId"),
                    ReadNullableId(item, "boss"), ReadNullableId(item, "mapId"), ReadText(item, "Type"),
                    ReadText(item, "iconName"), ReadId(item, "iconId"),
                    ReadNames(item, "npc"), ReadNames(item, "class")));
            }

            return new(spells.OrderBy(item => item.MapId is null).ThenBy(item => item.MapId)
                .ThenBy(item => item.Boss is null).ThenBy(item => item.Boss)
                .ThenBy(item => item.SpellId).ToArray(), maps, bosses, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new([], maps, bosses, "无法读取副本技能数据，请检查 Retail/spell_icons.json 是否存在且格式正确。");
        }
        catch (InvalidDataException ex)
        {
            return new([], maps, bosses, ex.Message);
        }
    }

    private static string ReadText(JsonElement item, string field)
    {
        if (item.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString()))
            return value.GetString()!;
        throw new InvalidDataException($"副本技能数据结构错误：{field} 应为非空文本。");
    }

    private static long ReadId(JsonElement item, string field)
    {
        if (item.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var id) && id > 0)
            return id;
        throw new InvalidDataException($"副本技能数据结构错误：{field} 应为正整数。");
    }

    private static int? ReadNullableId(JsonElement item, string field)
    {
        if (!item.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var id) && id >= 0)
            return id;
        throw new InvalidDataException($"副本技能数据结构错误：{field} 应为整数或空值。");
    }

    private static IReadOnlyList<string> ReadNames(JsonElement item, string field)
    {
        if (!item.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null)
            return [];
        if (value.ValueKind == JsonValueKind.String)
            return string.IsNullOrWhiteSpace(value.GetString()) ? [] : [value.GetString()!];
        if (value.ValueKind == JsonValueKind.Array)
        {
            var names = new List<string>();
            foreach (var element in value.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
                    throw new InvalidDataException($"副本技能数据结构错误：{field} 数组应仅包含非空文本。");
                names.Add(element.GetString()!);
            }
            return names.ToArray();
        }
        throw new InvalidDataException($"副本技能数据结构错误：{field} 应为文本、文本数组或空值。");
    }

    private static (Dictionary<int, string> Maps, Dictionary<int, string> Bosses) LoadNames(string path)
    {
        var maps = new Dictionary<int, string>();
        var bosses = new Dictionary<int, string>();
        try
        {
            var source = File.ReadAllText(path);
            ReadTable("Fuyutsui.mapIndex", maps);
            ReadTable("Fuyutsui.bossID", bosses);

            void ReadTable(string assignment, Dictionary<int, string> names)
            {
                if (!LuaLiteParser.TryExtractAssignedTable(source, assignment, out var table, out _, out _))
                    return;
                foreach (var (key, value) in table.Entries)
                {
                    if (key is not null && value is LuaLiteParser.NumberValue number
                        && table.GetTrailingComment(key) is { Length: > 0 } comment)
                        names[number.AsInt()] = comment;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            // 名称缺失时仍可用 JSON 中的编号浏览技能。
        }
        return (maps, bosses);
    }

    // null 表示“全部”；空字符串表示“未指定”，两者不可合并。
    public static IReadOnlyList<string> Values(DungeonSpellInfo spell, DungeonSpellFilter field)
        => field switch
        {
            DungeonSpellFilter.Map => [spell.MapId?.ToString(CultureInfo.InvariantCulture) ?? ""],
            DungeonSpellFilter.Boss => [spell.Boss?.ToString(CultureInfo.InvariantCulture) ?? ""],
            DungeonSpellFilter.Type => [spell.Type],
            DungeonSpellFilter.Npc => spell.Npcs.Count == 0 ? [""] : spell.Npcs,
            DungeonSpellFilter.Class => spell.Classes.Count == 0 ? [""] : spell.Classes,
            _ => []
        };

    public static bool Matches(DungeonSpellInfo spell,
        IReadOnlyDictionary<DungeonSpellFilter, string?> selections, DungeonSpellFilter? except = null)
        => selections.All(pair => pair.Key == except || pair.Value is null
            || Values(spell, pair.Key).Contains(pair.Value, StringComparer.Ordinal));

    public static string DescribeNumber(int? number, IReadOnlyDictionary<int, string> names)
        => number is not { } id ? "未指定"
            : names.TryGetValue(id, out var name) ? $"{id} · {name}" : id.ToString(CultureInfo.InvariantCulture);

    public static string DescribeType(string type) => type switch
    {
        "cast" => "施法（cast）", "aura" => "光环（aura）", _ => type
    };

    public static string DescribeClass(string token)
    {
        foreach (var (id, name) in ClassNames.GetClasses())
            if (string.Equals(ClassNames.GetConfigFileName(id), token, StringComparison.OrdinalIgnoreCase))
                return name;
        return token;
    }
}
