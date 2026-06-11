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

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> Group =>
        Values.TryGetValue("group", out var value) && value is IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> group
            ? group
            : new Dictionary<string, IReadOnlyDictionary<string, object?>>();

    public int GetInt(string key, int defaultValue = 0)
    {
        if (!Values.TryGetValue(key, out var value) || value is null)
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
        if (!Values.TryGetValue(key, out var value) || value is null)
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
        return Values.TryGetValue(key, out var value) ? value : null;
    }
}

