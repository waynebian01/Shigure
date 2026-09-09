using System.Text.Json.Nodes;

namespace Shigure;

public enum ConditionFieldType
{
    Int,
    Bool,
    String
}

public enum ConditionFieldCategory
{
    State,
    Shigure,
    Aura,
    Spell,
    DynamicUnit,
    DynamicValue
}

public static class ShigureConditionFields
{
    // 仅供条件编辑器承载规则级配置，不会写入条件表达式参与状态求值。
    public const string Delay = "$shigure.delay";
    public const string LogicDelay = "$shigure.logicDelay";
    public const string ContinueLogic = "$shigure.continueLogic";
}

public sealed record ConditionField(
    string Name,
    string DisplayName,
    ConditionFieldType Type,
    ConditionFieldCategory Category = ConditionFieldCategory.State,
    string? Classification = null,
    long? ItemId = null)
{
    public override string ToString() => DisplayName;
}

public sealed record ConditionSpell(long SpellId, int Index, string Name)
{
    public string DisplayName => $"{Name} / {SpellId}";
}

public sealed record ConditionItem(long ItemId, int Index, string Name)
{
    public string DisplayName => $"{Name} / {ItemId}";
}

public static class CooldownConditionClassifications
{
    public const string Spell = "技能";
    public const string Item = "物品";
}

public static class SpellIdConditionFields
{
    public const string OneKeyAssist = "一键辅助";
    public const string InsertSpell = "插入法术";
    public const string CastingSpell = "施法技能";
    public const string PreviousSpell = "上个技能";

    private static readonly HashSet<string> Names = new(StringComparer.Ordinal)
    {
        OneKeyAssist,
        InsertSpell,
        CastingSpell,
        PreviousSpell
    };

    public static bool Contains(string? fieldName)
    {
        var normalized = fieldName?.Trim() ?? string.Empty;
        if (normalized.StartsWith("state.", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["state.".Length..];
        }

        return Names.Contains(normalized);
    }
}

public static class ItemIdConditionFields
{
    public const string InsertItem = "插入物品";

    private static readonly HashSet<string> Names = new(StringComparer.Ordinal)
    {
        InsertItem
    };

    public static bool Contains(string? fieldName)
    {
        var normalized = fieldName?.Trim() ?? string.Empty;
        if (normalized.StartsWith("state.", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["state.".Length..];
        }

        return Names.Contains(normalized);
    }
}

/// <summary>
/// 从 config 目录构建可在条件编辑器中选择的字段目录。
/// 字段按模块当前选中的职业/专精过滤；group 队伍字段暂不收录。
/// </summary>
public sealed class ConditionFieldCatalog
{
    private static readonly HashSet<string> RemovedCastFields = new(StringComparer.Ordinal)
    {
        "施法",
        "目标施法",
        "焦点施法",
        "首领1施法",
        "首领2施法",
        "首领3施法",
        "首领4施法",
        "首领5施法"
    };

    private readonly ConfigService? _config;
    private readonly string _baseDirectory;

    private ConditionFieldCatalog(ConfigService? config, string baseDirectory)
    {
        _config = config;
        _baseDirectory = baseDirectory;
    }

    public static ConditionFieldCatalog Load(string baseDirectory)
    {
        try
        {
            return new ConditionFieldCatalog(ConfigService.LoadFromBaseDirectory(baseDirectory), baseDirectory);
        }
        catch
        {
            // config 缺失或损坏时返回空目录，编辑器降级为手动输入。
            return new ConditionFieldCatalog(null, baseDirectory);
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
        var sourceClassifications = LoadSourceClassifications(classId, specId);

        foreach (var (key, node) in stateConfig)
        {
            if (key is "group" or "spells" or "auras"
                || key == "锚点"
                || RemovedCastFields.Contains(key))
            {
                continue;
            }

            if (node is JsonObject field && field.ContainsKey("step"))
            {
                var classification = ReadClassification(field)
                    ?? sourceClassifications.GetValueOrDefault(key)
                    ?? InferStateClassification(key);
                var itemId = ReadItemId(field);
                if (IsItemCooldownField(itemId, classification))
                {
                    var displayName = ReadDisplayName(field) ?? key;
                    AddField(
                        fields,
                        seen,
                        key,
                        itemId is > 0 ? $"{displayName} / {itemId}" : displayName,
                        ReadType(field),
                        ConditionFieldCategory.Spell,
                        CooldownConditionClassifications.Item,
                        itemId);
                    continue;
                }

                AddField(
                    fields,
                    seen,
                    key,
                    key,
                    ReadType(field),
                    ConditionFieldCategory.State,
                    classification,
                    itemId);
            }
        }

        if (JsonHelpers.Get(stateConfig, "auras") is JsonObject auras)
        {
            foreach (var (auraName, node) in auras)
            {
                if (node is JsonObject field && field.ContainsKey("step"))
                {
                    var displayName = ReadDisplayName(field) ?? auraName;
                    AddField(
                        fields,
                        seen,
                        $"auras.{auraName}",
                        $"{displayName} / {ReadSpellId(field)?.ToString() ?? "?"}",
                        ReadType(field),
                        ConditionFieldCategory.Aura,
                        ReadClassification(field)
                            ?? sourceClassifications.GetValueOrDefault($"auras.{auraName}")
                            ?? InferAuraClassification(auraName));
                }
            }
        }

        if (JsonHelpers.Get(stateConfig, "spells") is JsonObject spells)
        {
            foreach (var (spellName, node) in spells)
            {
                if (node is JsonObject field && field.ContainsKey("step"))
                {
                    var displayName = ReadDisplayName(field) ?? spellName;
                    AddField(
                        fields,
                        seen,
                        $"spells.{spellName}",
                        $"{displayName} / {ReadSpellId(field)?.ToString() ?? "?"}",
                        ReadType(field),
                        ConditionFieldCategory.Spell,
                        CooldownConditionClassifications.Spell);
                }
            }
        }

        if (JsonHelpers.Get(stateConfig, "nameplates") is JsonObject nameplates)
        {
            var auraCount = (JsonHelpers.Get(nameplates, "auras") as JsonArray)?.Count ?? 0;
            for (var slot = 1; slot <= 20; slot++)
            {
                var prefix = $"nameplates.{slot}.";
                AddField(fields, seen, prefix + "存在", $"姓名板{slot} / 存在", ConditionFieldType.Bool, ConditionFieldCategory.State, "姓名板");
                if (JsonHelpers.GetInt(JsonHelpers.Get(nameplates, "healthPercent")) is > 0)
                {
                    AddField(fields, seen, prefix + "生命值", $"姓名板{slot} / 生命值", ConditionFieldType.Int, ConditionFieldCategory.State, "姓名板");
                }
                if (JsonHelpers.GetInt(JsonHelpers.Get(nameplates, "range")) is > 0)
                {
                    AddField(fields, seen, prefix + "距离", $"姓名板{slot} / 距离", ConditionFieldType.Int, ConditionFieldCategory.State, "姓名板");
                }
                for (var auraIndex = 1; auraIndex <= auraCount; auraIndex++)
                {
                    var name = $"光环{auraIndex}";
                    if (JsonHelpers.Get(nameplates, "auras") is JsonArray auraList
                        && auraIndex - 1 < auraList.Count
                        && auraList[auraIndex - 1] is JsonObject aura)
                    {
                        name = JsonHelpers.GetString(JsonHelpers.Get(aura, "name")) ?? name;
                    }

                    AddField(fields, seen, prefix + $"光环{auraIndex}", $"姓名板{slot} / {name}", ConditionFieldType.Int, ConditionFieldCategory.Aura, "姓名板");
                }
            }
        }

        // 原始“插入法术”按 config 中的 int 状态保留；转换为技能名的特殊字段单独置底。
        AddField(
            fields,
            seen,
            ModuleSpecialActions.FailedSpell,
            ModuleSpecialActions.FailedSpell,
            ConditionFieldType.String,
            ConditionFieldCategory.State,
            ClassStateCatalog.CategoryState);

        AddField(
            fields,
            seen,
            ModuleSpecialActions.FailedItem,
            ModuleSpecialActions.FailedItem,
            ConditionFieldType.String,
            ConditionFieldCategory.State,
            ClassStateCatalog.CategoryState);

        return fields;
    }

    /// <summary>
    /// 返回指定职业/专精下 group 队伍成员的字段(生命值/职责/驱散 + 该专精光环字段), 带类型。
    /// 供动态单位编辑器选择光环、以及条件编辑器构造 单位.字段 选项使用。
    /// </summary>
    public IReadOnlyList<ConditionField> GetGroupFields(int? classId, int? specId)
    {
        var fields = new List<ConditionField>();
        if (_config is null)
        {
            return fields;
        }

        var stateConfig = _config.BuildStateConfig(classId, specId);
        if (JsonHelpers.Get(stateConfig, "group") is not JsonObject group)
        {
            return fields;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, node) in group)
        {
            if (key is "start" or "num")
            {
                continue;
            }

            if (node is JsonObject field && field.ContainsKey("step") && seen.Add(key))
            {
                var displayName = ReadDisplayName(field) ?? key;
                var spellId = ReadSpellId(field);
                fields.Add(new ConditionField(
                    key,
                    spellId is null ? displayName : $"{displayName} / {spellId}",
                    ReadType(field)));
            }
        }

        // 治疗吸收由网格扫描注入，不在 config 的 group 字段里声明。
        if (seen.Add("治疗吸收"))
        {
            fields.Add(new ConditionField("治疗吸收", "治疗吸收", ConditionFieldType.Int));
        }

        return fields;
    }

    /// <summary>
    /// 返回多 ID 光环除规范 ID 外仍可解析的字段名。别名不加入下拉，避免同一逻辑光环重复显示。
    /// </summary>
    public IReadOnlySet<string> GetAuraAliasFieldNames(int? classId, int? specId, bool groupOnly)
    {
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        if (_config is null)
        {
            return aliases;
        }

        var stateConfig = _config.BuildStateConfig(classId, specId);
        if (!groupOnly && JsonHelpers.Get(stateConfig, "auras") is JsonObject auras)
        {
            foreach (var (fieldName, node) in auras)
            {
                if (node is JsonObject field)
                {
                    AddAliases(aliases, $"auras.{fieldName}", field, includeScope: true);
                }
            }
        }

        if (groupOnly && JsonHelpers.Get(stateConfig, "group") is JsonObject group)
        {
            foreach (var (fieldName, node) in group)
            {
                if (node is JsonObject field && fieldName.StartsWith("auras.", StringComparison.Ordinal))
                {
                    AddAliases(aliases, fieldName, field, includeScope: false);
                }
            }
        }

        return aliases;

        static void AddAliases(ISet<string> target, string fieldName, JsonObject field, bool includeScope)
        {
            var canonicalId = ReadSpellId(field);
            var metric = JsonHelpers.GetString(JsonHelpers.Get(field, "metric"));
            var scope = JsonHelpers.GetString(JsonHelpers.Get(field, "scope"));
            if (canonicalId is null || string.IsNullOrWhiteSpace(metric)
                || JsonHelpers.Get(field, "spellIds") is not JsonArray spellIds)
            {
                return;
            }

            foreach (var node in spellIds)
            {
                var alias = JsonHelpers.GetLong(node);
                if (alias is null || alias <= 0 || alias == canonicalId)
                {
                    continue;
                }

                target.Add(includeScope
                    ? $"auras.{scope}.{alias}.{metric}"
                    : $"auras.{alias}.{metric}");
            }
        }
    }

    private static bool IsItemCooldownField(long? itemId, string? classification)
        => itemId is > 0
           || string.Equals(classification, ClassStateCatalog.CategoryItem, StringComparison.Ordinal);

    private static void AddField(
        List<ConditionField> fields,
        HashSet<string> seen,
        string name,
        string displayName,
        ConditionFieldType type,
        ConditionFieldCategory category = ConditionFieldCategory.State,
        string? classification = null,
        long? itemId = null)
    {
        if (seen.Add(name))
        {
            fields.Add(new ConditionField(name, displayName, type, category, classification, itemId));
        }
    }

    private Dictionary<string, string> LoadSourceClassifications(int? classId, int? specId)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (classId is null || specId is null)
        {
            return result;
        }

        try
        {
            var path = Path.Combine(
                _baseDirectory,
                "Fuyutsui",
                "class",
                $"{ClassNames.GetConfigFileName(classId.Value)}.lua");
            if (!File.Exists(path))
            {
                return result;
            }

            var document = ClassBlocksStore.Load(path);
            if (!document.Specs.TryGetValue(specId.Value, out var spec))
            {
                return result;
            }

            if (spec.NestedStates)
            {
                foreach (var (classification, names) in spec.CategorizedStates)
                {
                    foreach (var sourceName in names)
                    {
                        var name = NormalizeStateName(sourceName);
                        var key = IsUnitStateClassification(classification)
                            ? classification + name
                            : name;
                        result[key] = classification;
                    }
                }

                foreach (var item in spec.Items)
                {
                    if (!string.IsNullOrWhiteSpace(item.Name))
                    {
                        result[item.Name] = ClassStateCatalog.CategoryItem;
                    }
                }
            }
            else
            {
                foreach (var sourceName in spec.FlatStates)
                {
                    var name = NormalizeStateName(sourceName);
                    result[name] = InferStateClassification(name);
                }
            }

            AddAuraClassifications(result, spec.PlayerAuras, string.Empty, "玩家");
            AddAuraClassifications(result, spec.TargetHarmfulAuras, "目标", "目标减益");
            AddAuraClassifications(result, spec.TargetHelpfulAuras, "目标", "目标增益");
            AddAuraClassifications(result, spec.FocusHarmfulAuras, "焦点", "焦点减益");
            AddAuraClassifications(result, spec.FocusHelpfulAuras, "焦点", "焦点增益");
        }
        catch
        {
            // Lua 配置暂不可读时继续使用生成 config 内的分类或名称推断。
        }

        return result;
    }

    private static void AddAuraClassifications(
        Dictionary<string, string> target,
        IEnumerable<ClassBlocksStore.AuraEntry> auras,
        string namePrefix,
        string classification)
    {
        foreach (var aura in auras)
        {
            target[$"auras.{namePrefix}{aura.Name}"] = classification;
        }
    }

    private static string? ReadClassification(JsonObject field)
        => JsonHelpers.GetString(JsonHelpers.Get(field, "category"))?.Trim() is { Length: > 0 } value
            ? value
            : null;

    private static string? ReadDisplayName(JsonObject field)
        => JsonHelpers.GetString(JsonHelpers.Get(field, "displayName"))?.Trim() is { Length: > 0 } value
            ? value
            : null;

    private static long? ReadSpellId(JsonObject field)
        => JsonHelpers.GetLong(JsonHelpers.Get(field, "spellId"));

    private static long? ReadItemId(JsonObject field)
        => JsonHelpers.GetLong(JsonHelpers.Get(field, "itemId")) is > 0 and var itemId
            ? itemId
            : null;

    private static string InferStateClassification(string name)
        => ClassStateCatalog.ClassifyField(name);

    private static string InferAuraClassification(string name)
        => name.StartsWith("目标", StringComparison.Ordinal)
            ? "目标光环"
            : name.StartsWith("焦点", StringComparison.Ordinal)
                ? "焦点光环"
                : "玩家";

    private static bool IsUnitStateClassification(string classification)
        => ClassStateCatalog.IsUnitPrefixCategory(classification);

    private static string NormalizeStateName(string name)
        => string.Equals(name, "法术失败", StringComparison.Ordinal)
            ? ModuleSpecialActions.InsertSpellState
            : name;

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
