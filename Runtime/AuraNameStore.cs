using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Shigure;

internal static class AuraNameStore
{
    private const string HashFileName = "AurasHash.json";
    private const string LegacyFileName = "names.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string NameFilePath => Path.Combine(AuraIconRecognizer.DefaultAuraDirectory, HashFileName);

    public static string GetNameFilePath(string auraDirectory)
        => Path.Combine(
            string.IsNullOrWhiteSpace(auraDirectory) ? AuraIconRecognizer.DefaultAuraDirectory : auraDirectory.Trim(),
            HashFileName);

    public static Dictionary<string, string> Load() => Load(NameFilePath);

    public static Dictionary<string, string> Load(string nameFilePath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var existingPath = ResolveExistingNameFilePath(nameFilePath);
            if (existingPath is null)
            {
                return result;
            }

            var json = File.ReadAllText(existingPath, Encoding.UTF8);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            LoadNames(document.RootElement, result);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return result;
    }

    public static void Save(IReadOnlyDictionary<string, string> names) => Save(NameFilePath, names);

    public static void Save(string nameFilePath, IReadOnlyDictionary<string, string> names)
    {
        var directory = Path.GetDirectoryName(nameFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var grouped = new SortedDictionary<string, SortedSet<string>>(StringComparer.CurrentCulture);
        foreach (var (hash, name) in names)
        {
            if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var normalizedHash = NormalizeHash(hash);
            if (!IsHashString(normalizedHash))
            {
                continue;
            }

            var displayName = name.Trim();
            if (!grouped.TryGetValue(displayName, out var hashes))
            {
                hashes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                grouped[displayName] = hashes;
            }

            hashes.Add(normalizedHash);
        }

        var output = new SortedDictionary<string, SortedDictionary<string, bool>>(StringComparer.CurrentCulture);
        foreach (var (name, hashes) in grouped)
        {
            output[name] = new SortedDictionary<string, bool>(
                hashes.ToDictionary(hash => hash, _ => true, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        }

        var json = JsonSerializer.Serialize(output, JsonOptions);
        File.WriteAllText(nameFilePath, json, Encoding.UTF8);
    }

    private static string? ResolveExistingNameFilePath(string nameFilePath)
    {
        if (File.Exists(nameFilePath))
        {
            return nameFilePath;
        }

        if (!HashFileName.Equals(Path.GetFileName(nameFilePath), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(nameFilePath);
        var legacyPath = Path.Combine(
            string.IsNullOrWhiteSpace(directory) ? AuraIconRecognizer.DefaultAuraDirectory : directory,
            LegacyFileName);
        return File.Exists(legacyPath) ? legacyPath : null;
    }

    private static void LoadNames(JsonElement root, Dictionary<string, string> result)
    {
        if (IsLegacyNameMap(root))
        {
            LoadLegacyNames(root, result);
            return;
        }

        foreach (var aura in root.EnumerateObject())
        {
            var name = aura.Name.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            foreach (var hash in EnumerateHashes(aura.Value))
            {
                var normalizedHash = NormalizeHash(hash);
                if (IsHashString(normalizedHash))
                {
                    result[normalizedHash] = name;
                }
            }
        }
    }

    private static void LoadLegacyNames(JsonElement root, Dictionary<string, string> result)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var name = property.Value.GetString();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var normalizedHash = NormalizeHash(property.Name);
            if (IsHashString(normalizedHash))
            {
                result[normalizedHash] = name.Trim();
            }
        }
    }

    private static bool IsLegacyNameMap(JsonElement root)
    {
        var hasProperty = false;
        foreach (var property in root.EnumerateObject())
        {
            hasProperty = true;
            if (!IsHashString(property.Name) || property.Value.ValueKind != JsonValueKind.String)
            {
                return false;
            }
        }

        return hasProperty;
    }

    private static IEnumerable<string> EnumerateHashes(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { } hash)
                    {
                        yield return hash;
                    }
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    if (IsHashString(property.Name))
                    {
                        yield return property.Name;
                    }
                    else if (property.Value.ValueKind == JsonValueKind.String
                        && property.Value.GetString() is { } hash)
                    {
                        yield return hash;
                    }
                }

                break;
            case JsonValueKind.String:
                if (value.GetString() is { } singleHash)
                {
                    yield return singleHash;
                }

                break;
        }
    }

    private static string NormalizeHash(string hash) => hash.Trim().ToUpperInvariant();

    private static bool IsHashString(string value)
        => value.Length == 16 && value.All(Uri.IsHexDigit);
}
