using System.Text.Json.Nodes;

namespace Shigure;

public sealed class StateBuilder
{
    private readonly ConfigService _config;

    public StateBuilder(ConfigService config)
    {
        _config = config;
    }

    public GameState Build(IReadOnlyDictionary<int, int> rowData, IReadOnlyDictionary<int, int> barData)
    {
        var classId = rowData.TryGetValue(2, out var cid) ? cid : 0;
        var specId = rowData.TryGetValue(3, out var sid) ? sid : 0;
        var stateConfig = _config.BuildStateConfig(classId, specId);
        var result = new Dictionary<string, object?>();

        foreach (var (key, node) in stateConfig)
        {
            if (key is "group" or "spells" || node is not JsonObject field || !field.ContainsKey("step"))
            {
                continue;
            }

            result[key] = ConvertRawValue(ResolveRaw(field, rowData, barData), JsonHelpers.GetString(JsonHelpers.Get(field, "type")));
        }

        if (JsonHelpers.Get(stateConfig, "spells") is JsonObject spellsConfig)
        {
            var spells = new Dictionary<string, object?>();
            foreach (var (spellName, node) in spellsConfig)
            {
                if (node is not JsonObject field || !field.ContainsKey("step"))
                {
                    continue;
                }

                spells[spellName] = ConvertRawValue(ResolveRaw(field, rowData, barData), JsonHelpers.GetString(JsonHelpers.Get(field, "type")));
            }

            result["spells"] = spells;
        }

        if (JsonHelpers.Get(stateConfig, "group") is JsonObject groupConfig)
        {
            var group = BuildGroup(groupConfig, rowData, barData);
            result["group"] = group;
        }

        return new GameState(result);
    }

    private static Dictionary<string, IReadOnlyDictionary<string, object?>> BuildGroup(
        JsonObject groupConfig,
        IReadOnlyDictionary<int, int> rowData,
        IReadOnlyDictionary<int, int> barData)
    {
        var start = JsonHelpers.GetInt(JsonHelpers.Get(groupConfig, "start")) ?? 26;
        var numParams = JsonHelpers.GetInt(JsonHelpers.Get(groupConfig, "num")) ?? 5;
        var group = new Dictionary<string, IReadOnlyDictionary<string, object?>>();

        for (var i = 1; i <= 30; i++)
        {
            var baseStep = start + (i - 1) * numParams;
            var sub = new Dictionary<string, object?>();
            foreach (var (fieldName, node) in groupConfig)
            {
                if (fieldName is "start" or "num" || node is not JsonObject field || !field.ContainsKey("step"))
                {
                    continue;
                }

                int? raw;
                var stepNode = JsonHelpers.Get(field, "step");
                if (JsonHelpers.GetString(stepNode) == "bar")
                {
                    raw = ResolveRaw(field, rowData, barData);
                }
                else
                {
                    var relStep = JsonHelpers.GetInt(stepNode);
                    raw = relStep is null
                        ? null
                        : rowData.TryGetValue(baseStep + relStep.Value, out var rawValue) ? rawValue : null;
                }

                sub[fieldName] = ConvertRawValue(raw, JsonHelpers.GetString(JsonHelpers.Get(field, "type")));
            }

            group[i.ToString()] = sub;
        }

        return group;
    }

    private static int? ResolveRaw(JsonObject field, IReadOnlyDictionary<int, int> rowData, IReadOnlyDictionary<int, int> barData)
    {
        var stepNode = JsonHelpers.Get(field, "step");
        if (JsonHelpers.GetString(stepNode) == "bar")
        {
            var barIndex = JsonHelpers.GetInt(JsonHelpers.Get(field, "bar"));
            return barIndex is not null && barData.TryGetValue(barIndex.Value, out var barValue) ? barValue : null;
        }

        var step = JsonHelpers.GetInt(stepNode);
        return step is not null && rowData.TryGetValue(step.Value, out var value) ? value : null;
    }

    private static object ConvertRawValue(int? raw, string? type)
    {
        return type switch
        {
            "bool" => raw.GetValueOrDefault() != 0,
            "string" => raw?.ToString() ?? string.Empty,
            _ => raw.GetValueOrDefault()
        };
    }
}

