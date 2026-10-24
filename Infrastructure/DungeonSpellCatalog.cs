using System.Globalization;
using System.Text.Json;

namespace Shigure;

internal sealed record DungeonSpellInfo(
    string Name, long SpellId, int? EncounterId, int? InstanceId, string Type,
    string IconName, long IconId, IReadOnlyList<string> Npcs, IReadOnlyList<string> Classes);

internal sealed record DungeonSpellInstance(NumberCatalogEntry Entry, bool IsRaid)
{
    public string DisplayName => $"{(IsRaid ? "团队副本" : "地下城")} {Entry.Id} · {Entry.DisplayName}";
}

internal sealed record DungeonSpellData(
    IReadOnlyList<DungeonSpellInfo> Spells,
    IReadOnlyDictionary<int, DungeonSpellInstance> Instances,
    IReadOnlyDictionary<int, BossCatalogEntry> Bosses,
    string? Error);

internal enum DungeonSpellFilter { Instance, Boss, Type, Npc, Class }

/// <summary>技能按实例 ID、Encounter ID 关联新目录；界面显示副本编号和副本内首领序号。</summary>
internal static class DungeonSpellCatalog
{
    public static DungeonSpellData Load(string baseDirectory)
    {
        var instances = new Dictionary<int, DungeonSpellInstance>();
        var dungeons = NumberCatalog.LoadDungeons("Retail", baseDirectory);
        var raids = NumberCatalog.LoadRaids("Retail", baseDirectory);
        var bossCatalog = BossCatalog.Load("Retail", baseDirectory);
        var bosses = bossCatalog.Entries.SelectMany(entry => (entry.EncounterIds ?? [entry.SourceId])
            .Select(id => (Id: id, Entry: entry))).ToDictionary(pair => pair.Id, pair => pair.Entry);
        if (dungeons.Error is not null || raids.Error is not null || bossCatalog.Error is not null)
            return new([], instances, bosses, dungeons.Error ?? raids.Error ?? bossCatalog.Error);
        foreach (var (catalog, isRaid) in new[] { (dungeons, false), (raids, true) })
            foreach (var entry in catalog.Entries.Where(entry => entry.SourceId is > 0 and <= int.MaxValue))
                instances[(int)entry.SourceId] = new(entry, isRaid);
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
                    ReadNullableId(item, "encounterId"), ReadNullableId(item, "instanceId"), ReadText(item, "Type"),
                    ReadText(item, "iconName"), ReadId(item, "iconId"),
                    ReadNames(item, "npc"), ReadNames(item, "class")));
            }

            return new(spells.OrderBy(item => InstanceOrder(item.InstanceId, instances))
                .ThenBy(item => item.EncounterId is null).ThenBy(item => BossOrder(item.EncounterId, bosses))
                .ThenBy(item => item.SpellId).ToArray(), instances, bosses, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new([], instances, bosses, "无法读取副本技能数据，请检查 Retail/spell_icons.json 是否存在且格式正确。");
        }
        catch (InvalidDataException ex)
        {
            return new([], instances, bosses, ex.Message);
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

    // null 表示“全部”；空字符串表示“未指定”，两者不可合并。
    public static IReadOnlyList<string> Values(DungeonSpellInfo spell, DungeonSpellFilter field)
        => field switch
        {
            DungeonSpellFilter.Instance => [spell.InstanceId?.ToString(CultureInfo.InvariantCulture) ?? ""],
            DungeonSpellFilter.Boss => [spell.EncounterId?.ToString(CultureInfo.InvariantCulture) ?? ""],
            DungeonSpellFilter.Type => [spell.Type],
            DungeonSpellFilter.Npc => spell.Npcs.Count == 0 ? [""] : spell.Npcs,
            DungeonSpellFilter.Class => spell.Classes.Count == 0 ? [""] : spell.Classes,
            _ => []
        };

    public static bool Matches(DungeonSpellInfo spell,
        IReadOnlyDictionary<DungeonSpellFilter, string?> selections, DungeonSpellFilter? except = null)
        => selections.All(pair => pair.Key == except || pair.Value is null
            || Values(spell, pair.Key).Contains(pair.Value, StringComparer.Ordinal));

    public static string DescribeBoss(int? encounterId, IReadOnlyDictionary<int, BossCatalogEntry> bosses)
        => encounterId is not { } id ? "未指定"
            : bosses.TryGetValue(id, out var boss) ? $"{boss.Id} · {boss.Name}" : $"首领战 {id}";

    public static int BossOrder(int? encounterId, IReadOnlyDictionary<int, BossCatalogEntry> bosses)
        => encounterId is { } id && bosses.TryGetValue(id, out var boss) ? boss.Id : int.MaxValue;

    public static string DescribeInstance(int? instanceId, IReadOnlyDictionary<int, DungeonSpellInstance> instances)
        => instanceId is not { } id ? "未指定"
            : instances.TryGetValue(id, out var instance) ? instance.DisplayName : $"实例 {id}";

    public static (int Type, long Number) InstanceOrder(int? instanceId,
        IReadOnlyDictionary<int, DungeonSpellInstance> instances)
        => instanceId is not { } id ? (3, 0)
            : instances.TryGetValue(id, out var instance) ? (instance.IsRaid ? 1 : 0, instance.Entry.Id)
            : (2, id);

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
