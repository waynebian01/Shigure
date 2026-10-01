namespace Shigure;

/// <summary>
/// 把动态单位 / 数量字段渲染成人类可读摘要 (如 "(生命值&lt;80 且 [恢复]!=0) → 生命值最低")。
/// 单位列表的"摘要"列与单位编辑器的实时预览共用同一套措辞, 避免两处描述漂移。
/// </summary>
internal static class UnitSummary
{
    public static string Describe(ModuleUnit unit, Func<long, string?>? resolveAuraName = null)
    {
        var filterText = DescribeFilterGroupsBody(unit.FilterGroups, resolveAuraName);
        var selection = DescribeUnitSelection(unit, resolveAuraName);
        return string.IsNullOrEmpty(filterText)
            ? selection
            : $"{filterText} → {selection}";
    }

    public static string Describe(ModuleCountField count, Func<long, string?>? resolveAuraName = null)
        => DescribeCountGroups(count.FilterGroups, "队友人数", resolveAuraName);

    public static string Describe(ModuleEnemyCountField count, Func<long, string?>? resolveAuraName = null)
        => DescribeCountGroups(count.FilterGroups, "敌人数", resolveAuraName);

    public static string DescribeTargetField(UnitTargetFieldKind field)
        => field switch
        {
            UnitTargetFieldKind.Health => "生命值",
            UnitTargetFieldKind.HealingAbsorb => "治疗吸收",
            UnitTargetFieldKind.Role => "职责",
            UnitTargetFieldKind.Dispel => "驱散",
            UnitTargetFieldKind.Aura => "光环",
            _ => "?"
        };

    public static string DescribeSelectionMode(UnitTargetFieldKind field, UnitSelectionMode mode)
        => (field, mode) switch
        {
            (UnitTargetFieldKind.Health or UnitTargetFieldKind.HealingAbsorb, UnitSelectionMode.Lowest) => "最低",
            (UnitTargetFieldKind.Health or UnitTargetFieldKind.HealingAbsorb, UnitSelectionMode.Highest) => "最高",
            (UnitTargetFieldKind.Role or UnitTargetFieldKind.Dispel, UnitSelectionMode.Ascending) => "正序",
            (UnitTargetFieldKind.Role or UnitTargetFieldKind.Dispel, UnitSelectionMode.Descending) => "倒序",
            (UnitTargetFieldKind.Aura, UnitSelectionMode.Longest) => "最长",
            (UnitTargetFieldKind.Aura, UnitSelectionMode.Shortest) => "最短",
            (UnitTargetFieldKind.Aura, UnitSelectionMode.Ascending) => "正序",
            (UnitTargetFieldKind.Aura, UnitSelectionMode.Descending) => "倒序",
            _ => mode.ToString()
        };

    private static string DescribeUnitSelection(ModuleUnit unit, Func<long, string?>? resolveAuraName)
    {
        var field = DescribeTargetField(unit.TargetField);
        var mode = DescribeSelectionMode(unit.TargetField, unit.SelectionMode);
        if (unit.TargetField == UnitTargetFieldKind.Aura)
        {
            var aura = unit.TargetAuraSpellId is { } id
                ? FormatAura(id, resolveAuraName)
                : "?";
            return $"[{aura}]{mode}";
        }

        return $"{field}{mode}";
    }

    private static string DescribeCountGroups(
        IReadOnlyList<ModuleCountConditionGroup>? groups,
        string suffix,
        Func<long, string?>? resolveAuraName)
    {
        var body = DescribeFilterGroupsBody(groups, resolveAuraName);
        return string.IsNullOrEmpty(body) ? suffix : $"{body} 的{suffix}";
    }

    private static string DescribeFilterGroupsBody(
        IReadOnlyList<ModuleCountConditionGroup>? groups,
        Func<long, string?>? resolveAuraName)
    {
        var descriptions = new List<string>();
        foreach (var group in groups ?? [])
        {
            var enabled = (group.Conditions ?? []).Where(condition => condition.Enabled).ToArray();
            if (enabled.Length == 0)
            {
                continue;
            }

            var separator = group.Mode == CountConditionGroupMode.Any ? " 或 " : " 且 ";
            var body = string.Join(separator, enabled.Select(condition => DescribeCountCondition(condition, resolveAuraName)));
            descriptions.Add(enabled.Length > 1 ? $"({body})" : body);
        }

        if (descriptions.Count == 0)
        {
            return string.Empty;
        }

        // 多组时整体加括号，使 “→ 目标选择” 的关系更清晰。
        var joined = string.Join(" 且 ", descriptions);
        return descriptions.Count > 1 || joined.StartsWith('(') ? joined : $"({joined})";
    }

    private static string DescribeCountCondition(
        ModuleCountCondition condition,
        Func<long, string?>? resolveAuraName)
    {
        var field = condition.Field switch
        {
            CountConditionFieldKind.Health => "生命值",
            CountConditionFieldKind.HealingAbsorb => "治疗吸收",
            CountConditionFieldKind.Role => "职责",
            CountConditionFieldKind.Dispel => "驱散",
            CountConditionFieldKind.Class => "职业",
            CountConditionFieldKind.Range => "距离",
            CountConditionFieldKind.Combat => "战斗",
            CountConditionFieldKind.ImprovedGarrote => "强化锁喉",
            CountConditionFieldKind.Threat => "仇恨值",
            CountConditionFieldKind.CastSpell => "施法技能",
            CountConditionFieldKind.Aura => $"[{FormatAura(condition.AuraSpellId.GetValueOrDefault(), resolveAuraName)}]",
            _ => "?"
        };
        var value = condition.ValueKind == CountConditionValueKind.NumberArray
            ? condition.ValueField ?? "?"
            : condition.ValueKind == CountConditionValueKind.StateField
                ? $"[{condition.ValueField}]"
            : DescribeCountValue(condition);
        return $"{field}{CountComparisonOperator(condition.Comparison)}{value}";
    }

    private static string DescribeCountValue(ModuleCountCondition condition)
    {
        return condition.Field switch
        {
            CountConditionFieldKind.Role => condition.Value switch
            {
                1 => "坦克",
                2 => "治疗",
                3 => "输出",
                _ => condition.Value.ToString()
            },
            CountConditionFieldKind.Class => ClassNames.GetClassAndSpecName(condition.Value, null).ClassName
                ?? condition.Value.ToString(),
            CountConditionFieldKind.Combat => condition.Value == 0 ? "不在战斗中" : "战斗中",
            CountConditionFieldKind.ImprovedGarrote => condition.Value switch
            {
                0 => "无锁喉 (0)",
                1 => "强化锁喉 (1)",
                2 => "普通锁喉 (2)",
                _ => condition.Value.ToString()
            },
            _ => condition.Value.ToString()
        };
    }

    private static string CountComparisonOperator(CountConditionComparisonKind comparison)
        => comparison switch
        {
            CountConditionComparisonKind.Equal => "==",
            CountConditionComparisonKind.NotEqual => "!=",
            CountConditionComparisonKind.GreaterThan => ">",
            CountConditionComparisonKind.LessThan => "<",
            CountConditionComparisonKind.GreaterThanOrEqual => ">=",
            CountConditionComparisonKind.LessThanOrEqual => "<=",
            CountConditionComparisonKind.In => " in ",
            CountConditionComparisonKind.NotIn => " not in ",
            _ => "?"
        };

    public static string Describe(ModuleAverageHealthField field, Func<long, string?>? resolveAuraName = null)
    {
        var suffix = field.Target == AverageHealthTargetKind.Enemies ? "敌人平均血量" : "队友平均血量";
        return DescribeCountGroups(field.FilterGroups, suffix, resolveAuraName);
    }

    private static string FormatAura(long spellId, Func<long, string?>? resolveAuraName)
    {
        var name = resolveAuraName?.Invoke(spellId);
        return string.IsNullOrWhiteSpace(name) ? spellId.ToString() : $"{name} / {spellId}";
    }
}
