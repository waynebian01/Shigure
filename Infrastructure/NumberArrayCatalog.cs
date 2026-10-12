using System.Text.Json;

namespace Shigure;

internal sealed record NumberCatalogEntry(string Name, long Id, long SourceId);

internal sealed record NumberCatalog(IReadOnlyList<NumberCatalogEntry> Entries, string? Error)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static NumberCatalog LoadBosses() => Load("bosses.json");

    public static NumberCatalog LoadMaps() => Load("maps.json");

    public string? FindName(long id)
        => Entries.FirstOrDefault(entry => entry.Id == id)?.Name;

    private static NumberCatalog Load(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Retail", "config", fileName);
        if (!File.Exists(path))
        {
            return new NumberCatalog([], $"未找到 Retail/config/{fileName}");
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
            return new NumberCatalog([], $"Retail/config/{fileName} 格式不正确");
        }
    }
}
