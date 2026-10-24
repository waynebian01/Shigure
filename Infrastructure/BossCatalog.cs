using System.Text.Json;

namespace Shigure;

internal sealed record BossCatalogEntry(
    string Name, int Id, int SourceId, int InstanceId, string InstanceName,
    string InstanceType, string? Note = null, int[]? EncounterIds = null,
    int? Expansion = null, string? ExpansionName = null, int InstanceNumber = 0)
{
    public string DisplayName => $"{InstanceName} / {Name}";
}

/// <summary>各版本的副本首领目录；Id 是副本内序号，SourceId 是客户端 Encounter ID。</summary>
internal sealed record BossCatalog(IReadOnlyList<BossCatalogEntry> Entries, string? Error)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Dictionary<string, (DateTime Modified, BossCatalog Catalog)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheGate = new();

    public static BossCatalog Load(string version = "Retail", string? baseDirectory = null)
    {
        var dataVersion = version.Equals("Forever", StringComparison.OrdinalIgnoreCase) ? "Forever" : "Retail";
        var path = Path.Combine(baseDirectory ?? AppPaths.BaseDirectory, dataVersion, "config", "bosses.json");
        lock (CacheGate)
        {
            try
            {
                var modified = File.GetLastWriteTimeUtc(path);
                if (Cache.TryGetValue(path, out var cached) && cached.Modified == modified) return cached.Catalog;
                var entries = JsonSerializer.Deserialize<List<BossCatalogEntry>>(File.ReadAllText(path), JsonOptions) ?? [];
                var catalog = new BossCatalog(entries.Where(entry => entry.Id > 0 && !string.IsNullOrWhiteSpace(entry.Name)).ToArray(), null);
                Cache[path] = (modified, catalog);
                return catalog;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return new([], $"无法读取 {dataVersion}/config/bosses.json，请检查文件是否存在且格式正确。");
            }
        }
    }

    public string DescribeNumber(int number, int? instanceId = null)
    {
        if (number == 0) return "非首领战";
        if (instanceId is null) return $"{number}号首领（副本内序号）";
        var matches = Entries.Where(entry => entry.Id == number && (instanceId is null || entry.InstanceId == instanceId)).ToArray();
        return matches.Length == 1 ? matches[0].Name : $"{number}号首领（副本内序号）";
    }

    // 只有全部“且”且明确选中一个副本时，才能把局部序号解释为具体首领。
    public static int? ResolveInstance(string version, IReadOnlyList<ConditionTerm> terms)
    {
        if (terms.Any(term => term.OrWithPrevious)) return null;
        var instances = new HashSet<int>();
        foreach (var term in terms)
        {
            var field = term.Field.StartsWith("state.", StringComparison.OrdinalIgnoreCase) ? term.Field[6..] : term.Field;
            if (term.Op != "==" || field is not ("地下城" or "团队副本")
                || !long.TryParse(term.Value, out var number) || number <= 0) continue;
            var catalog = field == "地下城" ? NumberCatalog.LoadDungeons(version) : NumberCatalog.LoadRaids(version);
            var entry = catalog.Entries.FirstOrDefault(entry => entry.Id == number);
            if (entry is { SourceId: > 0 and <= int.MaxValue }) instances.Add((int)entry.SourceId);
        }
        return instances.Count == 1 ? instances.Single() : null;
    }
}
