namespace Shigure;

public static class RecognizedAuraFields
{
    public const string StateKey = "recognizedAuras";
    public const string Prefix = "ra.";
    public const string LongPrefix = "recognizedAuras.";
    public const string LegacyPrefix = "recognizedAura.";

    public static bool TryGetName(string fieldName, out string name)
    {
        var key = fieldName.Trim();
        foreach (var prefix in new[] { Prefix, LongPrefix, LegacyPrefix })
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                name = key[prefix.Length..].Trim();
                return name.Length > 0;
            }
        }

        name = string.Empty;
        return false;
    }

    public static bool IsBarePrefix(string text)
    {
        var trimmed = text.Trim();
        return string.Equals(trimmed, Prefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, LongPrefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, LegacyPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static string ToFieldName(string name)
    {
        var trimmed = name.Trim();
        return trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith(LongPrefix, StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase)
            ? Prefix + (TryGetName(trimmed, out var auraName) ? auraName : string.Empty)
            : Prefix + trimmed;
    }

    public static Dictionary<string, object?> BuildValueMap(IEnumerable<RecognizedAuraInfo> auras)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var aura in auras)
        {
            var name = aura.Name.Trim();
            if (name.Length == 0 || name == "-")
            {
                continue;
            }

            if (values.TryGetValue(name, out var existing) && existing is int current)
            {
                values[name] = Math.Max(current, aura.Value);
            }
            else
            {
                values[name] = aura.Value;
            }
        }

        return values;
    }
}
