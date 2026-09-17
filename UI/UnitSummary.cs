namespace Shigure;

/// <summary>
/// 把动态单位 / 数量字段渲染成人类可读摘要 (如 "带[X]且血最低 (&lt;80)")。
/// 单位列表的"摘要"列与单位编辑器的实时预览共用同一套措辞, 避免两处描述漂移。
/// </summary>
internal static class UnitSummary
{
    public static string Describe(ModuleUnit unit, Func<long, string?>? resolveAuraName = null)
    {
        if (unit.FilterVersion == ModuleUnit.CurrentFilterVersion)
        {
            return DescribeFilteredUnit(unit, resolveAuraName);
        }

        var threshold = DescribeThreshold(
            unit.HealthThreshold,
            unit.HealthThresholdField,
            IsHealingAbsorbKind(unit.Kind) ? 0 : 100);
        var aura = unit.AuraSpellIds is { Count: > 0 } ? FormatAura(unit.AuraSpellIds[0], resolveAuraName) : "?";
        var auras = unit.AuraSpellIds is { Count: > 0 }
            ? string.Join("/", unit.AuraSpellIds.Select(id => FormatAura(id, resolveAuraName)))
            : "?";
        var dir = unit.Reverse ? "逆序" : "正序";
        var roleFilter = DescribeRoleFilter(unit);
        return roleFilter + (unit.Kind switch
        {
            UnitSelectorKind.LowestHealth => $"血量最低 (<{threshold})",
            UnitSelectorKind.LowestHealthWithAnyAura => $"带任一[{auras}]且血最低 (<{threshold})",
            UnitSelectorKind.LowestHealthWithoutAnyAura => $"不带任一[{auras}]且血最低 (<{threshold})",
            UnitSelectorKind.LowestHealthWithoutAura => $"不带[{aura}]且血最低 (<{threshold})",
            UnitSelectorKind.LowestHealthWithAura => $"带[{aura}]且血最低 (<{threshold})",
            UnitSelectorKind.LowestHealthWithAuraCount => $"[{aura}]={unit.AuraCount}且血最低 (<{threshold})",
            UnitSelectorKind.UnitWithRole => $"职责={unit.Role} {dir}首个",
            UnitSelectorKind.UnitWithRoleWithoutAura => $"职责={unit.Role}且不带[{aura}] {dir}",
            UnitSelectorKind.UnitWithAura => $"带[{aura}] 持续最久",
            UnitSelectorKind.UnitWithAuraShortest => $"带[{aura}] 持续最短",
            UnitSelectorKind.UnitWithDispelType => $"驱散类型={unit.DispelType}",
            UnitSelectorKind.HighestHealingAbsorb => $"治疗吸收最高 (>{threshold})",
            UnitSelectorKind.HighestHealingAbsorbWithAnyAura => $"带任一[{auras}]且治疗吸收最高 (>{threshold})",
            UnitSelectorKind.HighestHealingAbsorbWithoutAnyAura => $"不带任一[{auras}]且治疗吸收最高 (>{threshold})",
            UnitSelectorKind.HighestHealingAbsorbWithoutAura => $"不带[{aura}]且治疗吸收最高 (>{threshold})",
            UnitSelectorKind.HighestHealingAbsorbWithAura => $"带[{aura}]且治疗吸收最高 (>{threshold})",
            UnitSelectorKind.HighestHealingAbsorbWithAuraCount => $"[{aura}]={unit.AuraCount}且治疗吸收最高 (>{threshold})",
            _ => unit.Kind.ToString()
        });
    }

    public static string Describe(ModuleCountField count, Func<long, string?>? resolveAuraName = null)
    {
        if (count.FilterVersion == ModuleCountField.CurrentFilterVersion)
        {
            return DescribeFilteredAllies(count, resolveAuraName);
        }

        var threshold = DescribeThreshold(
            count.HealthThreshold,
            count.HealthThresholdField,
            IsHealingAbsorbKind(count.Kind) ? 0 : 100);
        var aura = count.AuraSpellId is { } id ? FormatAura(id, resolveAuraName) : "?";
        var roleFilter = count.RoleFilter is null
            ? string.Empty
            : count.RoleFilter == UnitRoleFilterKind.Include
                ? $"职责={count.Role}且"
                : $"职责!={count.Role}且";
        return roleFilter + (count.Kind switch
        {
            CountKind.UnitsBelowHealth => $"血量<{threshold} 的人数",
            CountKind.UnitsWithoutAuraBelowHealth => $"不带[{aura}]且血<{threshold} 的人数",
            CountKind.UnitsWithAuraBelowHealth => $"带[{aura}]且血<{threshold} 的人数",
            CountKind.UnitsWithAura => $"带[{aura}] 的人数",
            CountKind.UnitsAboveHealingAbsorb => $"治疗吸收>{threshold} 的人数",
            CountKind.UnitsWithoutAuraAboveHealingAbsorb => $"不带[{aura}]且治疗吸收>{threshold} 的人数",
            CountKind.UnitsWithAuraAboveHealingAbsorb => $"带[{aura}]且治疗吸收>{threshold} 的人数",
            _ => count.Kind.ToString()
        });
    }

    private static string DescribeFilteredUnit(
        ModuleUnit unit,
        Func<long, string?>? resolveAuraName)
    {
        var parts = new List<string>();
        if (unit.HealthFilter != EnemyThresholdFilterKind.None)
        {
            parts.Add($"血量{ThresholdOperator(unit.HealthFilter)}{DescribeThreshold(unit.HealthThreshold, unit.HealthThresholdField, 0)}");
        }

        if (unit.HealingAbsorbFilter != EnemyThresholdFilterKind.None)
        {
            parts.Add($"治疗吸收{ThresholdOperator(unit.HealingAbsorbFilter)}{DescribeThreshold(unit.HealingAbsorbThreshold, unit.HealingAbsorbThresholdField, 0)}");
        }

        if (unit.RoleFilter is not null)
        {
            parts.Add(unit.RoleFilter == UnitRoleFilterKind.Include
                ? $"职责={unit.Role}"
                : $"职责!={unit.Role}");
        }

        if (unit.DispelFilter != AllyDispelFilterKind.None)
        {
            parts.Add(unit.DispelFilter == AllyDispelFilterKind.WithType
                ? $"驱散类型={unit.DispelType}"
                : $"驱散类型!={unit.DispelType}");
        }

        var ids = unit.AuraSpellIds ?? [];
        var aura = ids.Count > 0 ? FormatAura(ids[0], resolveAuraName) : "?";
        var auras = ids.Count > 0
            ? string.Join("/", ids.Select(id => FormatAura(id, resolveAuraName)))
            : "?";
        if (unit.AuraFilter != EnemyAuraFilterKind.None)
        {
            parts.Add(unit.AuraFilter switch
            {
                EnemyAuraFilterKind.WithAura => $"带[{aura}]",
                EnemyAuraFilterKind.WithoutAura => $"不带[{aura}]",
                EnemyAuraFilterKind.WithAnyAura => $"带任一[{auras}]",
                EnemyAuraFilterKind.WithoutAnyAura => $"不带任一[{auras}]",
                _ => string.Empty
            });
        }

        if (unit.AuraDurationFilter != AuraDurationFilterKind.None)
        {
            var durationAuraId = unit.AuraDurationSpellId
                ?? (ids.Count > 0 ? ids[0] : (long?)null);
            var durationAura = durationAuraId is { } id
                ? FormatAura(id, resolveAuraName)
                : "?";
            var durationText = unit.AuraDurationFilter switch
            {
                AuraDurationFilterKind.Longest => $"[{durationAura}]持续最长",
                AuraDurationFilterKind.Shortest => $"[{durationAura}]持续最短",
                AuraDurationFilterKind.Above => $"[{durationAura}]持续时间>{DescribeThreshold(unit.AuraDurationThreshold, unit.AuraDurationThresholdField, 0)}",
                AuraDurationFilterKind.Below => $"[{durationAura}]持续时间<{DescribeThreshold(unit.AuraDurationThreshold, unit.AuraDurationThresholdField, 0)}",
                AuraDurationFilterKind.Equal => $"[{durationAura}]持续时间={DescribeThreshold(unit.AuraDurationThreshold, unit.AuraDurationThresholdField, 0)}",
                _ => string.Empty
            };
            parts.Add(durationText);
        }

        var selector = unit.Kind switch
        {
            UnitSelectorKind.LowestHealth => "生命值最低",
            UnitSelectorKind.HighestHealingAbsorb => "治疗吸收最高",
            UnitSelectorKind.UnitWithRole => unit.Reverse ? "逆序首个" : "正序首个",
            UnitSelectorKind.UnitWithAura => $"[{aura}]持续最久",
            UnitSelectorKind.UnitWithAuraShortest => $"[{aura}]持续最短",
            _ => unit.Kind.ToString()
        };
        return parts.Count == 0 ? selector : $"{string.Join("且", parts)} → {selector}";
    }

    private static string DescribeFilteredAllies(
        ModuleCountField count,
        Func<long, string?>? resolveAuraName)
    {
        var parts = new List<string>();
        if (count.HealthFilter != EnemyThresholdFilterKind.None)
        {
            parts.Add($"血量{ThresholdOperator(count.HealthFilter)}{DescribeThreshold(count.HealthThreshold, count.HealthThresholdField, 0)}");
        }

        if (count.HealingAbsorbFilter != EnemyThresholdFilterKind.None)
        {
            parts.Add($"治疗吸收{ThresholdOperator(count.HealingAbsorbFilter)}{DescribeThreshold(count.HealingAbsorbThreshold, count.HealingAbsorbThresholdField, 0)}");
        }

        if (count.RoleFilter is not null)
        {
            parts.Add(count.RoleFilter == UnitRoleFilterKind.Include
                ? $"职责={count.Role}"
                : $"职责!={count.Role}");
        }

        if (count.DispelFilter != AllyDispelFilterKind.None)
        {
            parts.Add(count.DispelFilter == AllyDispelFilterKind.WithType
                ? $"驱散类型={count.DispelType}"
                : $"驱散类型!={count.DispelType}");
        }

        if (count.AuraFilter != EnemyAuraFilterKind.None)
        {
            var ids = count.AuraSpellIds ?? [];
            var aura = ids.Count > 0 ? FormatAura(ids[0], resolveAuraName) : "?";
            var auras = ids.Count > 0
                ? string.Join("/", ids.Select(id => FormatAura(id, resolveAuraName)))
                : "?";
            parts.Add(count.AuraFilter switch
            {
                EnemyAuraFilterKind.WithAura => $"带[{aura}]",
                EnemyAuraFilterKind.WithoutAura => $"不带[{aura}]",
                EnemyAuraFilterKind.WithAnyAura => $"带任一[{auras}]",
                EnemyAuraFilterKind.WithoutAnyAura => $"不带任一[{auras}]",
                _ => string.Empty
            });

        }

        return parts.Count == 0 ? "队友人数" : $"{string.Join("且", parts)} 的队友人数";
    }

    public static string Describe(ModuleEnemyCountField count, Func<long, string?>? resolveAuraName = null)
    {
        var parts = new List<string>();
        if (count.HealthFilter != EnemyThresholdFilterKind.None)
        {
            parts.Add($"血量{ThresholdOperator(count.HealthFilter)}{DescribeThreshold(count.HealthThreshold, count.HealthThresholdField, 0)}");
        }

        if (count.AuraFilter != EnemyAuraFilterKind.None)
        {
            var ids = count.AuraSpellIds ?? [];
            var aura = ids.Count > 0 ? FormatAura(ids[0], resolveAuraName) : "?";
            var auras = ids.Count > 0
                ? string.Join("/", ids.Select(id => FormatAura(id, resolveAuraName)))
                : "?";
            parts.Add(count.AuraFilter switch
            {
                EnemyAuraFilterKind.WithAura => $"带[{aura}]",
                EnemyAuraFilterKind.WithoutAura => $"不带[{aura}]",
                EnemyAuraFilterKind.WithAnyAura => $"带任一[{auras}]",
                EnemyAuraFilterKind.WithoutAnyAura => $"不带任一[{auras}]",
                _ => string.Empty
            });

            if (count.AuraFilter == EnemyAuraFilterKind.WithAura
                && count.AuraDurationFilter is AuraDurationFilterKind.Above or AuraDurationFilterKind.Below)
            {
                var op = count.AuraDurationFilter == AuraDurationFilterKind.Above ? ">" : "<";
                parts.Add($"[{aura}]光环时间{op}{count.AuraDurationThreshold.GetValueOrDefault()}秒");
            }
        }

        if (count.RangeFilter != EnemyThresholdFilterKind.None)
        {
            parts.Add($"距离{ThresholdOperator(count.RangeFilter)}{DescribeThreshold(count.RangeThreshold, count.RangeThresholdField, 0)}");
        }

        if (count.CombatFilter != EnemyCombatFilterKind.None)
        {
            parts.Add(count.CombatFilter == EnemyCombatFilterKind.InCombat ? "战斗中" : "不在战斗中");
        }

        return parts.Count == 0
            ? "敌人数(血量>0)"
            : $"{string.Join("且", parts)} 的敌人数";
    }

    public static string Describe(ModuleAverageHealthField field, Func<long, string?>? resolveAuraName = null)
    {
        var parts = new List<string>();
        if (field.HealthFilter != EnemyThresholdFilterKind.None)
        {
            parts.Add($"血量{ThresholdOperator(field.HealthFilter)}{DescribeThreshold(field.HealthThreshold, field.HealthThresholdField, 0)}");
        }

        if (field.AuraFilter != EnemyAuraFilterKind.None)
        {
            var ids = field.AuraSpellIds ?? [];
            var aura = ids.Count > 0 ? FormatAura(ids[0], resolveAuraName) : "?";
            var auras = ids.Count > 0
                ? string.Join("/", ids.Select(id => FormatAura(id, resolveAuraName)))
                : "?";
            parts.Add(field.AuraFilter switch
            {
                EnemyAuraFilterKind.WithAura => $"带[{aura}]",
                EnemyAuraFilterKind.WithoutAura => $"不带[{aura}]",
                EnemyAuraFilterKind.WithAnyAura => $"带任一[{auras}]",
                EnemyAuraFilterKind.WithoutAnyAura => $"不带任一[{auras}]",
                _ => string.Empty
            });

        }

        if (field.Target == AverageHealthTargetKind.Allies && field.RoleFilter is not null)
        {
            parts.Add(field.RoleFilter == UnitRoleFilterKind.Include
                ? $"职责={field.Role}"
                : $"职责!={field.Role}");
        }

        if (field.Target == AverageHealthTargetKind.Enemies)
        {
            if (field.RangeFilter != EnemyThresholdFilterKind.None)
            {
                parts.Add($"距离{ThresholdOperator(field.RangeFilter)}{DescribeThreshold(field.RangeThreshold, field.RangeThresholdField, 0)}");
            }

            if (field.CombatFilter != EnemyCombatFilterKind.None)
            {
                parts.Add(field.CombatFilter == EnemyCombatFilterKind.InCombat ? "战斗中" : "不在战斗中");
            }
        }

        var target = field.Target == AverageHealthTargetKind.Enemies ? "敌人" : "队友";
        return parts.Count == 0
            ? $"{target}平均血量"
            : $"{string.Join("且", parts)} 的{target}平均血量";
    }

    private static string ThresholdOperator(EnemyThresholdFilterKind filter)
        => filter == EnemyThresholdFilterKind.Above ? ">" : "<";

    private static string FormatAura(long spellId, Func<long, string?>? resolveAuraName)
    {
        var name = resolveAuraName?.Invoke(spellId);
        return string.IsNullOrWhiteSpace(name) ? spellId.ToString() : $"{name} / {spellId}";
    }

    private static string DescribeThreshold(int? fixedValue, string? field, int defaultValue = 100)
    {
        return string.IsNullOrWhiteSpace(field)
            ? (fixedValue ?? defaultValue).ToString()
            : $"动态:{field.Trim()}";
    }

    private static string DescribeRoleFilter(ModuleUnit unit)
    {
        if (!IsLowestHealthKind(unit.Kind) || unit.RoleFilter is null)
        {
            return string.Empty;
        }

        return unit.RoleFilter == UnitRoleFilterKind.Include
            ? $"职责={unit.Role}且"
            : $"职责!={unit.Role}且";
    }

    private static bool IsLowestHealthKind(UnitSelectorKind kind)
        => kind is UnitSelectorKind.LowestHealth
            or UnitSelectorKind.LowestHealthWithAnyAura
            or UnitSelectorKind.LowestHealthWithoutAnyAura
            or UnitSelectorKind.LowestHealthWithoutAura
            or UnitSelectorKind.LowestHealthWithAura
            or UnitSelectorKind.LowestHealthWithAuraCount;

    private static bool IsHealingAbsorbKind(UnitSelectorKind kind)
        => kind is UnitSelectorKind.HighestHealingAbsorb
            or UnitSelectorKind.HighestHealingAbsorbWithAnyAura
            or UnitSelectorKind.HighestHealingAbsorbWithoutAnyAura
            or UnitSelectorKind.HighestHealingAbsorbWithoutAura
            or UnitSelectorKind.HighestHealingAbsorbWithAura
            or UnitSelectorKind.HighestHealingAbsorbWithAuraCount;

    private static bool IsHealingAbsorbKind(CountKind kind)
        => kind is CountKind.UnitsAboveHealingAbsorb
            or CountKind.UnitsWithoutAuraAboveHealingAbsorb
            or CountKind.UnitsWithAuraAboveHealingAbsorb;
}
