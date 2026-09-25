using System.Reflection;
using System.Text.Json;

namespace Shigure;

internal sealed record ExBossEventInfo(
    int Key,
    int EventId,
    long SpellId,
    string Name,
    string MechanicType,
    int MapId,
    string MapName,
    int EncounterId,
    string BossName);

internal sealed record ExBossMechanicType(int Value, string Name);

/// <summary>
/// EXBoss 事件键目录。说明页与条件编辑器共用该目录，确保显示内容和像素编码一致。
/// </summary>
internal static class ExBossEventCatalog
{
    private const string ResourceSuffix = ".wiki.EXBoss技能分类.json";
    private static readonly Lazy<IReadOnlyList<ExBossEventInfo>> LazyEvents = new(LoadEvents);

    public static IReadOnlyList<ExBossMechanicType> MechanicTypes { get; } =
    [
        new(0, "无 / 未知"),
        new(1, "其他"),
        new(2, "坦克"),
        new(3, "治疗"),
        new(4, "点名"),
        new(5, "机制"),
        new(6, "特殊")
    ];

    public static IReadOnlyList<ExBossEventInfo> Events => LazyEvents.Value;

    public static ExBossMechanicType? FindMechanicType(string? value)
        => int.TryParse(value?.Trim(), out var number)
            ? MechanicTypes.FirstOrDefault(option => option.Value == number)
            : null;

    public static ExBossEventInfo? FindEvent(string? value)
        => int.TryParse(value?.Trim(), out var key)
            ? Events.FirstOrDefault(item => item.Key == key)
            : null;

    private static IReadOnlyList<ExBossEventInfo> LoadEvents()
    {
        var assembly = typeof(ExBossEventCatalog).Assembly;
        var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(name =>
            name.EndsWith(ResourceSuffix, StringComparison.Ordinal));
        if (resourceName is null)
        {
            return [];
        }

        try
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                return [];
            }

            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("eventKeyTable", out var table)
                || table.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var events = new List<ExBossEventInfo>(table.GetArrayLength());
            foreach (var item in table.EnumerateArray())
            {
                events.Add(new ExBossEventInfo(
                    ReadInt(item, "key"),
                    ReadInt(item, "eventId"),
                    ReadLong(item, "spellId"),
                    ReadString(item, "name"),
                    ReadString(item, "mechanicType"),
                    ReadInt(item, "mapId"),
                    ReadString(item, "mapName"),
                    ReadInt(item, "encounterId"),
                    ReadString(item, "bossName")));
            }

            return events
                .Where(item => item.Key > 0)
                .OrderBy(item => item.Key)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static int ReadInt(JsonElement item, string propertyName)
        => item.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var number)
            ? number
            : 0;

    private static long ReadLong(JsonElement item, string propertyName)
        => item.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var number)
            ? number
            : 0;

    private static string ReadString(JsonElement item, string propertyName)
        => item.TryGetProperty(propertyName, out var value)
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;
}
