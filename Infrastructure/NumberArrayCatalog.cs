using System.Text.Json;

namespace Shigure;

internal sealed record NumberCatalogEntry(string Name, long Id, long SourceId, string? Note = null)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Note) ? Name : $"{Name}（{Note}）";
}

internal sealed record NumberCatalog(IReadOnlyList<NumberCatalogEntry> Entries, string? Error)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static NumberCatalog LoadBosses(string version = "Retail")
    {
        var bosses = BossCatalog.Load(version);
        return new(bosses.Entries.SelectMany(entry => entry.GetEncounterIds()
            .Select(id => new NumberCatalogEntry(entry.Name, id, id, entry.InstanceName)))
            .DistinctBy(entry => entry.Id).ToArray(), bosses.Error);
    }

    public static NumberCatalog LoadDungeons(string version = "Retail", string? baseDirectory = null)
        => Load("dungeons.json", version, baseDirectory);

    public static NumberCatalog LoadRaids(string version = "Retail", string? baseDirectory = null)
        => Load("raids.json", version, baseDirectory);

    public string? FindName(long id)
        => Entries.FirstOrDefault(entry => entry.Id == id)?.DisplayName;

    private static NumberCatalog Load(string fileName, string version = "Retail", string? baseDirectory = null)
    {
        var dataVersion = version.Equals("Forever", StringComparison.OrdinalIgnoreCase) ? "Forever" : "Retail";
        var path = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, dataVersion, "config", fileName);
        if (!File.Exists(path))
        {
            return new NumberCatalog([], $"未找到 {dataVersion}/config/{fileName}");
        }

        try
        {
            var entries = JsonSerializer.Deserialize<List<NumberCatalogEntry>>(File.ReadAllText(path), JsonOptions)
                ?? [];
            var cleaned = entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name) && entry.Id > 0)
                .GroupBy(entry => entry.Id)
                .Select(group => group.First())
                .OrderBy(entry => entry.Id)
                .ToArray();
            return new NumberCatalog(cleaned, null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new NumberCatalog([], $"{dataVersion}/config/{fileName} 格式不正确");
        }
    }
}
