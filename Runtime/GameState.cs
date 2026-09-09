namespace Shigure;

public sealed class GameState
{
    public GameState(Dictionary<string, object?> values)
    {
        Values = values;
    }

    public Dictionary<string, object?> Values { get; }

    public IReadOnlyDictionary<string, object?> Spells =>
        Values.TryGetValue("spells", out var value) && value is IReadOnlyDictionary<string, object?> spells
            ? spells
            : new Dictionary<string, object?>();

    public IReadOnlyDictionary<string, object?> Auras =>
        Values.TryGetValue("auras", out var value) && value is IReadOnlyDictionary<string, object?> auras
            ? auras
            : new Dictionary<string, object?>();

    public IReadOnlyDictionary<string, string> SpellDisplayTypes =>
        Values.TryGetValue("$spellDisplayTypes", out var value) && value is IReadOnlyDictionary<string, string> displayTypes
            ? displayTypes
            : new Dictionary<string, string>();

    public IReadOnlyDictionary<string, long> ItemIds =>
        Values.TryGetValue("$itemIds", out var value) && value is IReadOnlyDictionary<string, long> itemIds
            ? itemIds
            : new Dictionary<string, long>();

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> Group =>
        Values.TryGetValue("group", out var value) && value is IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> group
            ? group
            : new Dictionary<string, IReadOnlyDictionary<string, object?>>();

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> Nameplates =>
        Values.TryGetValue("nameplates", out var value) && value is IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> nameplates
            ? nameplates
            : new Dictionary<string, IReadOnlyDictionary<string, object?>>();

    public int GetInt(string key, int defaultValue = 0)
    {
        var value = GetValue(key);
        if (value is null)
        {
            return defaultValue;
        }

        return value switch
        {
            int i => i,
            long l => (int)l,
            bool b => b ? 1 : 0,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => defaultValue
        };
    }

    public bool GetBool(string key, bool defaultValue = false)
    {
        var value = GetValue(key);
        if (value is null)
        {
            return defaultValue;
        }

        return value switch
        {
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            string s when int.TryParse(s, out var parsed) => parsed != 0,
            _ => defaultValue
        };
    }

    public object? GetValue(string key)
    {
        var normalized = key.Trim();
        if (normalized.StartsWith("state.", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["state.".Length..];
        }

        if (normalized.StartsWith("spells.", StringComparison.OrdinalIgnoreCase))
        {
            return Spells.TryGetValue(normalized["spells.".Length..], out var value) ? value : null;
        }

        if (normalized.StartsWith("spell.", StringComparison.OrdinalIgnoreCase))
        {
            return Spells.TryGetValue(normalized["spell.".Length..], out var value) ? value : null;
        }

        if (normalized.StartsWith("auras.", StringComparison.OrdinalIgnoreCase))
        {
            return Auras.TryGetValue(normalized["auras.".Length..], out var value) ? value : null;
        }

        if (normalized.StartsWith("aura.", StringComparison.OrdinalIgnoreCase))
        {
            return Auras.TryGetValue(normalized["aura.".Length..], out var value) ? value : null;
        }

        if (normalized.StartsWith("nameplates.", StringComparison.OrdinalIgnoreCase))
        {
            var parts = normalized["nameplates.".Length..].Split('.', 2);
            if (parts.Length == 2 && Nameplates.TryGetValue(parts[0], out var plate))
            {
                return plate.TryGetValue(parts[1], out var value) ? value : null;
            }
        }

        return Values.TryGetValue(normalized, out var directValue) ? directValue : null;
    }
}
