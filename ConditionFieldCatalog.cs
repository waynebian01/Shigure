using System.Text.Json.Nodes;

namespace Shigure;

public enum ConditionFieldType
{
    Int,
    Bool,
    String
}

public sealed record ConditionField(string Name, string DisplayName, ConditionFieldType Type)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// 从 config.json 构建可在条件编辑器中选择的字段目录。
/// 字段按模块当前选中的职业/专精过滤；group 队伍字段暂不收录。
/// </summary>
public sealed class ConditionFieldCatalog
{
    private readonly ConfigService? _config;

    private ConditionFieldCatalog(ConfigService? config)
    {
        _config = config;
    }

    public static ConditionFieldCatalog Load(string baseDirectory)
    {
        try
        {
            var configPath = Path.Combine(baseDirectory, "config.json");
            return new ConditionFieldCatalog(new ConfigService(configPath));
        }
        catch
        {
            // config 缺失或损坏时返回空目录，编辑器降级为手动输入。
            return new ConditionFieldCatalog(null);
        }
    }

    /// <summary>
    /// 返回指定职业/专精下可用的条件字段。classId/specId 为空时只返回公共 state 字段。
    /// </summary>
    public IReadOnlyList<ConditionField> GetFields(int? classId, int? specId)
    {
        var fields = new List<ConditionField>();
        if (_config is null)
        {
            return fields;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stateConfig = _config.BuildStateConfig(classId, specId);

        foreach (var (key, node) in stateConfig)
        {
            if (key is "group" or "spells")
            {
                continue;
            }

            if (node is JsonObject field && field.ContainsKey("step"))
            {
                AddField(fields, seen, key, key, ReadType(field));
            }
        }

        if (JsonHelpers.Get(stateConfig, "spells") is JsonObject spells)
        {
            foreach (var (spellName, node) in spells)
            {
                if (node is JsonObject field && field.ContainsKey("step"))
                {
                    AddField(fields, seen, $"spells.{spellName}", $"技能: {spellName}", ReadType(field));
                }
            }
        }

        return fields;
    }

    private static void AddField(
        List<ConditionField> fields,
        HashSet<string> seen,
        string name,
        string displayName,
        ConditionFieldType type)
    {
        if (seen.Add(name))
        {
            fields.Add(new ConditionField(name, displayName, type));
        }
    }

    private static ConditionFieldType ReadType(JsonObject field)
    {
        return JsonHelpers.GetString(JsonHelpers.Get(field, "type")) switch
        {
            "bool" => ConditionFieldType.Bool,
            "string" => ConditionFieldType.String,
            _ => ConditionFieldType.Int
        };
    }
}
