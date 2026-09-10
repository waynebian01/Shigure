using System.Text.Json.Nodes;

namespace Shigure;

public sealed class StateBuilder : IRuntimeStateBuilder
{
    private readonly ConfigService _config;

    public StateBuilder(ConfigService config)
    {
        _config = config;
    }

    public GameState Build(
        IReadOnlyDictionary<int, int> rowData,
        IReadOnlyDictionary<int, int> barData,
        IReadOnlyDictionary<int, int>? healAbsorbData = null)
    {
        var classId = rowData.TryGetValue(2, out var cid) ? cid : 0;
        var specId = rowData.TryGetValue(3, out var sid) ? sid : 0;
        var stateConfig = _config.BuildStateConfig(classId, specId);
        var result = new Dictionary<string, object?>();
        healAbsorbData ??= new Dictionary<int, int>();

        var itemIds = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (key, node) in stateConfig)
        {
            if (key is "group" or "spells" or "auras" || node is not JsonObject field || !field.ContainsKey("step"))
            {
                continue;
            }

            result[key] = ConvertRawValue(ResolveRaw(field, rowData, barData), JsonHelpers.GetString(JsonHelpers.Get(field, "type")));
            var itemId = JsonHelpers.GetLong(JsonHelpers.Get(field, "itemId"));
            if (itemId is > 0)
            {
                itemIds[key] = itemId.Value;
            }
        }

        if (itemIds.Count > 0)
        {
            result["$itemIds"] = itemIds;
        }

        if (JsonHelpers.Get(stateConfig, "spells") is JsonObject spellsConfig)
        {
            result["spells"] = BuildFieldMap(spellsConfig, rowData, barData);
            result["$spellDisplayTypes"] = BuildSpellDisplayTypes(spellsConfig);
        }

        if (JsonHelpers.Get(stateConfig, "auras") is JsonObject aurasConfig)
        {
            result["auras"] = BuildFieldMap(aurasConfig, rowData, barData);
        }

        if (JsonHelpers.Get(stateConfig, "group") is JsonObject groupConfig)
        {
            var group = BuildGroup(groupConfig, rowData, barData, healAbsorbData);
            result["group"] = group;
        }

        if (JsonHelpers.Get(stateConfig, "nameplates") is JsonObject nameplateConfig)
        {
            result["nameplates"] = BuildNameplates(nameplateConfig, rowData);
        }

        return new GameState(result);
    }

    private static Dictionary<string, IReadOnlyDictionary<string, object?>> BuildNameplates(
        JsonObject config,
        IReadOnlyDictionary<int, int> rowData)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, object?>>();
        var start = JsonHelpers.GetInt(JsonHelpers.Get(config, "start")) ?? 0;
        var fieldCount = JsonHelpers.GetInt(JsonHelpers.Get(config, "num")) ?? 0;
        // 生命值/距离/光环起点都是固定偏移，与插件分配一致。
        const int healthOffset = NameplateStateLayout.HealthPercentOffset;
        const int rangeOffset = NameplateStateLayout.RangeOffset;
        const int auraStart = NameplateStateLayout.AuraStartOffset;
        var auraConfigs = JsonHelpers.Get(config, "auras") as JsonArray;

        for (var slot = 1; slot <= 20; slot++)
        {
            var firstPixel = start + (slot - 1) * fieldCount;
            var present = start > 0 && fieldCount > 0 && rowData.ContainsKey(firstPixel);
            int ReadField(int offset) => present && offset > 0 && offset <= fieldCount
                && rowData.TryGetValue(firstPixel + offset - 1, out var value) ? value : 0;
            var values = new Dictionary<string, object?>
            {
                ["存在"] = present,
                ["生命值"] = ReadField(healthOffset),
                ["距离"] = ReadField(rangeOffset)
            };

            if (auraConfigs is not null)
            {
                for (var auraIndex = 0; auraIndex < auraConfigs.Count; auraIndex++)
                {
                    var value = ReadField(auraStart + auraIndex);
                    var ordinalKey = $"光环{auraIndex + 1}";
                    values[ordinalKey] = value;
                    if (auraConfigs[auraIndex] is JsonObject aura)
                    {
                        var name = JsonHelpers.GetString(JsonHelpers.Get(aura, "name"));
                        if (!string.IsNullOrWhiteSpace(name) && !values.ContainsKey(name))
                        {
                            values[name] = value;
                        }

                        // 按 spellId 再暴露一份, 供敌人数量字段与队伍光环用同一套键查找。
                        AddNameplateAuraIds(values, aura, value);
                    }
                }
            }

            result[slot.ToString()] = values;
        }

        return result;
    }

    private static Dictionary<string, object?> BuildFieldMap(
        JsonObject fieldsConfig,
        IReadOnlyDictionary<int, int> rowData,
        IReadOnlyDictionary<int, int> barData)
    {
        var values = new Dictionary<string, object?>();
        foreach (var (fieldName, node) in fieldsConfig)
        {
            if (node is not JsonObject field || !field.ContainsKey("step"))
            {
                continue;
            }

            var value = ConvertRawValue(ResolveRaw(field, rowData, barData), JsonHelpers.GetString(JsonHelpers.Get(field, "type")));
            values[fieldName] = value;
            AddAuraAliases(values, field, value, includeScope: true);
        }

        return values;
    }

    private static Dictionary<string, string> BuildSpellDisplayTypes(JsonObject fieldsConfig)
    {
        var values = new Dictionary<string, string>();
        foreach (var (fieldName, node) in fieldsConfig)
        {
            if (node is not JsonObject field)
            {
                continue;
            }

            var displayType = JsonHelpers.GetString(JsonHelpers.Get(field, "displayType"));
            if (!string.IsNullOrWhiteSpace(displayType))
            {
                values[fieldName] = displayType;
            }
        }

        return values;
    }

    private static Dictionary<string, IReadOnlyDictionary<string, object?>> BuildGroup(
        JsonObject groupConfig,
        IReadOnlyDictionary<int, int> rowData,
        IReadOnlyDictionary<int, int> barData,
        IReadOnlyDictionary<int, int> healAbsorbData)
    {
        var start = JsonHelpers.GetInt(JsonHelpers.Get(groupConfig, "start")) ?? 26;
        var numParams = groupConfig
            .Where(pair => pair.Key is not "start" and not "num" && pair.Value is JsonObject field && field.ContainsKey("step"))
            .Count();
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

                var value = ConvertRawValue(raw, JsonHelpers.GetString(JsonHelpers.Get(field, "type")));
                sub[fieldName] = value;
                AddAuraAliases(sub, field, value, includeScope: false);
            }

            // 治疗吸收来自网格扫描：白块右侧像素的 B=单位编号，G-1=吸收值。
            // 插件像素里的生命值含吸收盾，这里折算为真实生命：生命值 -= 治疗吸收。
            // 保留 0 和负数，供最低生命值选择器比较治疗吸收后的有效生命值。
            var absorb = healAbsorbData.TryGetValue(i, out var absorbValue) ? absorbValue : 0;
            sub["治疗吸收"] = absorb;
            if (absorb != 0 && sub.TryGetValue("生命值", out var healthObj) && healthObj is int health)
            {
                sub["生命值"] = health - absorb;
            }

            group[i.ToString()] = sub;
        }

        return group;
    }

    // 姓名板光环配置形如 { name, spellId, spellIds? }; 规范 ID 与别名都写成 auras.{spellId}.value。
    private static void AddNameplateAuraIds(
        IDictionary<string, object?> target,
        JsonObject aura,
        object? value)
    {
        var canonicalId = JsonHelpers.GetLong(JsonHelpers.Get(aura, "spellId"));
        if (canonicalId is > 0)
        {
            target[SpellFieldKey.AuraMember(canonicalId.Value)] = value;
        }

        if (JsonHelpers.Get(aura, "spellIds") is not JsonArray aliases)
        {
            return;
        }

        foreach (var node in aliases)
        {
            var alias = JsonHelpers.GetLong(node);
            if (alias is > 0 && alias != canonicalId)
            {
                target[SpellFieldKey.AuraMember(alias.Value)] = value;
            }
        }
    }

    private static void AddAuraAliases(
        IDictionary<string, object?> target,
        JsonObject field,
        object? value,
        bool includeScope)
    {
        var canonicalId = JsonHelpers.GetLong(JsonHelpers.Get(field, "spellId"));
        var metric = JsonHelpers.GetString(JsonHelpers.Get(field, "metric"));
        var scope = JsonHelpers.GetString(JsonHelpers.Get(field, "scope"));
        if (canonicalId is null || string.IsNullOrWhiteSpace(metric)
            || JsonHelpers.Get(field, "spellIds") is not JsonArray aliases)
        {
            return;
        }

        foreach (var node in aliases)
        {
            var alias = JsonHelpers.GetLong(node);
            if (alias is null || alias == canonicalId)
            {
                continue;
            }

            var key = includeScope
                ? $"{scope}.{alias}.{metric}"
                : $"auras.{alias}.{metric}";
            target[key] = value;
        }
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
