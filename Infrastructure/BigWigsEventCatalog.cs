using System.Reflection;
using System.Text.Json;

namespace Shigure;

internal sealed record BigWigsEventInfo(
    int Key,
    long SpellId,
    string Name,
    int MapId,
    string MapName,
    int EncounterId,
    string BossName,
    string Methods);

internal sealed record BigWigsEventType(int Value, string Name);

/// <summary>
/// BigWigs 团本首领事件目录。Lua 像素桥与本目录都按 spellId 升序生成一基键。
/// </summary>
internal static class BigWigsEventCatalog
{
    private const string ResourceSuffix = ".wiki.BigWigs团队首领事件.json";
    private static readonly Lazy<IReadOnlyList<BigWigsEventInfo>> LazyEvents = new(LoadEvents);

    public static IReadOnlyList<BigWigsEventType> EventTypes { get; } =
    [
        new(0, "无事件 / 未安装 BigWigs"),
        new(1, "精确计时"),
        new(2, "近似 CD"),
        new(3, "点名计时"),
        new(4, "施法计时"),
        new(5, "即时警告")
    ];

    public static IReadOnlyList<BigWigsEventInfo> Events => LazyEvents.Value;

    public static BigWigsEventType? FindEventType(string? value)
        => int.TryParse(value?.Trim(), out var number)
            ? EventTypes.FirstOrDefault(option => option.Value == number)
            : null;

    public static BigWigsEventInfo? FindEvent(string? value)
        => int.TryParse(value?.Trim(), out var key)
            ? Events.FirstOrDefault(item => item.Key == key)
            : null;

    private static IReadOnlyList<BigWigsEventInfo> LoadEvents()
    {
        var assembly = typeof(BigWigsEventCatalog).Assembly;
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

            return table.EnumerateArray()
                .Select(item => new BigWigsEventInfo(
                    ReadInt(item, "key"),
                    ReadLong(item, "spellId"),
                    ReadString(item, "name"),
                    ReadInt(item, "mapId"),
                    ReadString(item, "mapName"),
                    ReadInt(item, "encounterId"),
                    ReadString(item, "bossName"),
                    ReadString(item, "methods")))
                .Where(item => item.Key > 0 && item.SpellId > 0)
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
