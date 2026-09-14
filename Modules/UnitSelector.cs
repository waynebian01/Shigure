namespace Shigure;

/// <summary>
/// 把 <see cref="ModuleUnit"/> / <see cref="ModuleCountField"/> 定义在当前 group 状态下解析为
/// 单位槽位或数量。逻辑忠实移植自旧 Python 项目 utils.py:
/// - 只考虑职责 != 0 的单位;
/// - 最低生命值选择器保留并比较 0 和负生命值;
/// - 生命值数量字段仍只统计 0 &lt; 生命值 &lt; 阈值;
/// - 按 "1".."30" 升序遍历, 保证首/末语义稳定。
/// </summary>
public static class UnitSelector
{
    private const int DefaultThreshold = 100;

    /// <summary>解析动态单位为 group 槽位("1".."30"), 无匹配返回 null。</summary>
    public static string? Resolve(ModuleUnit unit, GameState state)
    {
        if (unit.FilterVersion == ModuleUnit.CurrentFilterVersion)
        {
            return ResolveFilteredUnit(unit, state);
        }

        var group = state.Group;
        var threshold = ResolveThreshold(
            unit.HealthThreshold,
            unit.HealthThresholdField,
            state,
            IsHealingAbsorbKind(unit.Kind) ? 0 : DefaultThreshold);
        var aura = FirstAura(unit.AuraSpellIds);
        if (RequiresAura(unit.Kind)
            && (unit.AuraSpellIds is not { Count: > 0 }
                || unit.AuraSpellIds.Any(id => !GroupContainsAuraField(group, id))))
        {
            return null;
        }

        return unit.Kind switch
        {
            UnitSelectorKind.LowestHealth => LowestHealth(
                group,
                threshold,
                data => MatchesRoleFilter(data, unit.RoleFilter, unit.Role)),
            UnitSelectorKind.LowestHealthWithAnyAura => unit.AuraSpellIds is { Count: > 0 } names
                ? LowestHealth(
                    group,
                    threshold,
                    data => MatchesRoleFilter(data, unit.RoleFilter, unit.Role) && HasAnyAura(data, names))
                : null,
            UnitSelectorKind.LowestHealthWithoutAnyAura => unit.AuraSpellIds is { Count: > 0 } names
                ? LowestHealth(
                    group,
                    threshold,
                    data => MatchesRoleFilter(data, unit.RoleFilter, unit.Role) && !HasAnyAura(data, names))
                : null,
            UnitSelectorKind.LowestHealthWithoutAura => aura is null
                ? null
                : LowestHealth(
                    group,
                    threshold,
                    data => MatchesRoleFilter(data, unit.RoleFilter, unit.Role) && !HasAura(data, aura.Value)),
            UnitSelectorKind.LowestHealthWithAura => aura is null
                ? null
                : LowestHealth(
                    group,
                    threshold,
                    data => MatchesRoleFilter(data, unit.RoleFilter, unit.Role) && HasAura(data, aura.Value)),
            UnitSelectorKind.LowestHealthWithAuraCount => aura is null || unit.AuraCount is null
                ? null
                : LowestHealth(
                    group,
                    threshold,
                    data => MatchesRoleFilter(data, unit.RoleFilter, unit.Role)
                        && AuraEquals(data, aura.Value, unit.AuraCount.Value)),
            UnitSelectorKind.UnitWithRole => unit.Role is null
                ? null
                : UnitWithRole(group, unit.Role.Value, unit.Reverse, _ => true),
            UnitSelectorKind.UnitWithRoleWithoutAura => unit.Role is null || aura is null
                ? null
                : UnitWithRole(group, unit.Role.Value, unit.Reverse, data => !HasAura(data, aura.Value)),
            UnitSelectorKind.UnitWithAura => aura is null ? null : UnitWithAura(group, aura.Value, shortest: false),
            UnitSelectorKind.UnitWithAuraShortest => aura is null ? null : UnitWithAura(group, aura.Value, shortest: true),
            UnitSelectorKind.UnitWithDispelType => unit.DispelType is null
                ? null
                : UnitWithDispelType(group, unit.DispelType.Value),
            UnitSelectorKind.HighestHealingAbsorb => HighestHealingAbsorb(group, threshold, _ => true),
            UnitSelectorKind.HighestHealingAbsorbWithAnyAura => unit.AuraSpellIds is { Count: > 0 } names
                ? HighestHealingAbsorb(group, threshold, data => HasAnyAura(data, names))
                : null,
            UnitSelectorKind.HighestHealingAbsorbWithoutAnyAura => unit.AuraSpellIds is { Count: > 0 } names
                ? HighestHealingAbsorb(group, threshold, data => !HasAnyAura(data, names))
                : null,
            UnitSelectorKind.HighestHealingAbsorbWithoutAura => aura is null
                ? null
                : HighestHealingAbsorb(group, threshold, data => !HasAura(data, aura.Value)),
            UnitSelectorKind.HighestHealingAbsorbWithAura => aura is null
                ? null
                : HighestHealingAbsorb(group, threshold, data => HasAura(data, aura.Value)),
            UnitSelectorKind.HighestHealingAbsorbWithAuraCount => aura is null || unit.AuraCount is null
                ? null
                : HighestHealingAbsorb(group, threshold, data => AuraEquals(data, aura.Value, unit.AuraCount.Value)),
            _ => null
        };
    }

    /// <summary>解析数量字段为整数。</summary>
    public static int Resolve(ModuleCountField count, GameState state)
    {
        if (count.FilterVersion == ModuleCountField.CurrentFilterVersion)
        {
            return ResolveFilteredAllies(count, state);
        }

        var group = state.Group;
        var threshold = ResolveThreshold(
            count.HealthThreshold,
            count.HealthThresholdField,
            state,
            IsHealingAbsorbKind(count.Kind) ? 0 : DefaultThreshold);
        if (RequiresAura(count.Kind)
            && (count.AuraSpellId is null || !GroupContainsAuraField(group, count.AuraSpellId.Value)))
        {
            return 0;
        }

        return count.Kind switch
        {
            CountKind.UnitsBelowHealth => CountUnits(
                group,
                data => MatchesRoleFilter(data, count.RoleFilter, count.Role) && BelowThreshold(data, threshold)),
            CountKind.UnitsWithoutAuraBelowHealth => count.AuraSpellId is null
                ? 0
                : CountUnits(
                    group,
                    data => MatchesRoleFilter(data, count.RoleFilter, count.Role)
                        && !HasAura(data, count.AuraSpellId.Value)
                        && BelowThreshold(data, threshold)),
            CountKind.UnitsWithAura => count.AuraSpellId is null
                ? 0
                : CountUnits(
                    group,
                    data => MatchesRoleFilter(data, count.RoleFilter, count.Role)
                        && HasAura(data, count.AuraSpellId.Value)),
            CountKind.UnitsWithAuraBelowHealth => count.AuraSpellId is null
                ? 0
                : CountUnits(
                    group,
                    data => MatchesRoleFilter(data, count.RoleFilter, count.Role)
                        && HasAura(data, count.AuraSpellId.Value)
                        && BelowThreshold(data, threshold)),
            CountKind.UnitsAboveHealingAbsorb => CountUnits(
                group,
                data => MatchesRoleFilter(data, count.RoleFilter, count.Role)
                    && AboveHealingAbsorbThreshold(data, threshold)),
            CountKind.UnitsWithoutAuraAboveHealingAbsorb => count.AuraSpellId is null
                ? 0
                : CountUnits(
                    group,
                    data => MatchesRoleFilter(data, count.RoleFilter, count.Role)
                        && !HasAura(data, count.AuraSpellId.Value)
                        && AboveHealingAbsorbThreshold(data, threshold)),
            CountKind.UnitsWithAuraAboveHealingAbsorb => count.AuraSpellId is null
                ? 0
                : CountUnits(
                    group,
                    data => MatchesRoleFilter(data, count.RoleFilter, count.Role)
                        && HasAura(data, count.AuraSpellId.Value)
                        && AboveHealingAbsorbThreshold(data, threshold)),
            _ => 0
        };
    }

    private static string? ResolveFilteredUnit(ModuleUnit unit, GameState state)
    {
        var auras = unit.AuraSpellIds ?? [];
        var durationAuraSpellId = unit.AuraDurationSpellId
            ?? (unit.AuraDurationFilter != AuraDurationFilterKind.None && auras.Count > 0 ? auras[0] : null);
        var selectorUsesAura = unit.Kind is UnitSelectorKind.UnitWithAura or UnitSelectorKind.UnitWithAuraShortest;
        if (RequiresAura(unit.AuraFilter)
            && (auras.Count == 0 || auras.Any(id => !GroupContainsAuraField(state.Group, id))))
        {
            return null;
        }

        if ((unit.AuraDurationFilter != AuraDurationFilterKind.None
                && (durationAuraSpellId is null
                    || !GroupContainsAuraField(state.Group, durationAuraSpellId.Value)))
            || selectorUsesAura
                && (auras.Count == 0 || auras.Any(id => !GroupContainsAuraField(state.Group, id))))
        {
            return null;
        }

        var healthThreshold = ResolveThreshold(unit.HealthThreshold, unit.HealthThresholdField, state, 0);
        var healingAbsorbThreshold = ResolveThreshold(
            unit.HealingAbsorbThreshold,
            unit.HealingAbsorbThresholdField,
            state,
            0);
        var auraDurationThreshold = ResolveThreshold(
            unit.AuraDurationThreshold,
            unit.AuraDurationThresholdField,
            state,
            0);
        var candidates = new List<(string Key, IReadOnlyDictionary<string, object?> Data)>();
        for (var i = 1; i <= 30; i++)
        {
            var key = i.ToString();
            if (!state.Group.TryGetValue(key, out var data)
                || !RoleNotZero(data)
                || !MatchesOptionalThreshold(data, "生命值", unit.HealthFilter, healthThreshold)
                || !MatchesOptionalThreshold(data, "治疗吸收", unit.HealingAbsorbFilter, healingAbsorbThreshold)
                || !MatchesRoleFilter(data, unit.RoleFilter, unit.Role)
                || !MatchesDispelFilter(data, unit.DispelFilter, unit.DispelType)
                || !MatchesAuraFilter(data, unit.AuraFilter, auras)
                || !MatchesAuraDuration(
                    data,
                    unit.AuraDurationFilter,
                    durationAuraSpellId,
                    auraDurationThreshold))
            {
                continue;
            }

            candidates.Add((key, data));
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        if (unit.AuraDurationFilter is AuraDurationFilterKind.Longest or AuraDurationFilterKind.Shortest)
        {
            candidates = FilterByAuraDurationExtremum(
                candidates,
                durationAuraSpellId!.Value,
                unit.AuraDurationFilter == AuraDurationFilterKind.Shortest);
            if (candidates.Count == 0)
            {
                return null;
            }
        }

        return unit.Kind switch
        {
            UnitSelectorKind.LowestHealth => SelectByField(candidates, "生命值", smallest: true),
            UnitSelectorKind.HighestHealingAbsorb => SelectByField(candidates, "治疗吸收", smallest: false),
            UnitSelectorKind.UnitWithAura => SelectByAuraDuration(candidates, auras, shortest: false),
            UnitSelectorKind.UnitWithAuraShortest => SelectByAuraDuration(candidates, auras, shortest: true),
            UnitSelectorKind.UnitWithRole => unit.Reverse ? candidates[^1].Key : candidates[0].Key,
            _ => candidates[0].Key
        };
    }

    private static string? SelectByField(
        IReadOnlyList<(string Key, IReadOnlyDictionary<string, object?> Data)> candidates,
        string field,
        bool smallest)
    {
        string? bestKey = null;
        var bestValue = smallest ? int.MaxValue : int.MinValue;
        foreach (var candidate in candidates)
        {
            if (!TryInt(GetField(candidate.Data, field), out var value))
            {
                continue;
            }

            if (bestKey is null || (smallest ? value < bestValue : value > bestValue))
            {
                bestKey = candidate.Key;
                bestValue = value;
            }
        }

        return bestKey;
    }

    private static string? SelectByAuraDuration(
        IReadOnlyList<(string Key, IReadOnlyDictionary<string, object?> Data)> candidates,
        IReadOnlyList<long> auraSpellIds,
        bool shortest)
    {
        string? bestKey = null;
        var bestValue = shortest ? int.MaxValue : int.MinValue;
        foreach (var candidate in candidates)
        {
            var durations = auraSpellIds
                .Select(id => GetAuraDuration(candidate.Data, id))
                .Where(duration => duration > 0)
                .ToArray();
            if (durations.Length == 0)
            {
                continue;
            }

            var value = shortest ? durations.Min() : durations.Max();
            if (bestKey is null || (shortest ? value < bestValue : value > bestValue))
            {
                bestKey = candidate.Key;
                bestValue = value;
            }
        }

        return bestKey;
    }

    private static List<(string Key, IReadOnlyDictionary<string, object?> Data)> FilterByAuraDurationExtremum(
        IReadOnlyList<(string Key, IReadOnlyDictionary<string, object?> Data)> candidates,
        long auraSpellId,
        bool shortest)
    {
        var withAura = candidates
            .Select(candidate => (Candidate: candidate, Duration: GetAuraDuration(candidate.Data, auraSpellId)))
            .Where(item => item.Duration > 0)
            .ToList();
        if (withAura.Count == 0)
        {
            return [];
        }

        var target = shortest
            ? withAura.Min(item => item.Duration)
            : withAura.Max(item => item.Duration);
        return withAura
            .Where(item => item.Duration == target)
            .Select(item => item.Candidate)
            .ToList();
    }

    private static int ResolveFilteredAllies(ModuleCountField count, GameState state)
    {
        var auras = count.AuraSpellIds ?? [];
        if (RequiresAura(count.AuraFilter)
            && (auras.Count == 0 || auras.Any(id => !GroupContainsAuraField(state.Group, id))))
        {
            return 0;
        }

        var healthThreshold = ResolveThreshold(count.HealthThreshold, count.HealthThresholdField, state, 0);
        var healingAbsorbThreshold = ResolveThreshold(
            count.HealingAbsorbThreshold,
            count.HealingAbsorbThresholdField,
            state,
            0);
        return CountUnits(state.Group, data =>
            (!count.PositiveHealthOnly
                || TryInt(GetField(data, "生命值"), out var positiveHealth) && positiveHealth > 0)
            && MatchesOptionalThreshold(data, "生命值", count.HealthFilter, healthThreshold)
            && MatchesOptionalThreshold(data, "治疗吸收", count.HealingAbsorbFilter, healingAbsorbThreshold)
            && MatchesRoleFilter(data, count.RoleFilter, count.Role)
            && MatchesDispelFilter(data, count.DispelFilter, count.DispelType)
            && MatchesAuraFilter(data, count.AuraFilter, auras));
    }

    /// <summary>
    /// 解析敌人数量字段为整数: 统计姓名板中距离 &gt; 0 且满足全部已启用筛选(生命值 / 光环 / 距离 / 战斗)的敌人。
    /// 姓名板未配置距离时该字段恒为 0, 结果也恒为 0。
    /// </summary>
    public static int Resolve(ModuleEnemyCountField count, GameState state)
    {
        var nameplates = state.Nameplates;
        var healthThreshold = ResolveThreshold(count.HealthThreshold, count.HealthThresholdField, state, 0);
        var rangeThreshold = ResolveThreshold(count.RangeThreshold, count.RangeThresholdField, state, 0);
        var auras = count.AuraSpellIds ?? [];
        if (RequiresAura(count.AuraFilter) && auras.Count == 0)
        {
            return 0;
        }

        var result = 0;
        for (var i = 1; i <= 20; i++)
        {
            if (!nameplates.TryGetValue(i.ToString(), out var data))
            {
                continue;
            }

            // 默认基线: 只统计存在且距离 > 0 的敌人。
            if (GetField(data, "存在") is bool present && !present)
            {
                continue;
            }

            if (!TryInt(GetField(data, "距离"), out var range) || range <= 0)
            {
                continue;
            }

            if (count.HealthFilter != EnemyThresholdFilterKind.None
                && (!TryInt(GetField(data, "生命值"), out var health)
                    || !MatchesThreshold(count.HealthFilter, health, healthThreshold)))
            {
                continue;
            }

            if (!MatchesThreshold(count.RangeFilter, range, rangeThreshold))
            {
                continue;
            }

            if (!MatchesAuraFilter(data, count.AuraFilter, auras))
            {
                continue;
            }

            if (!MatchesCombatFilter(data, count.CombatFilter))
            {
                continue;
            }

            result++;
        }

        return result;
    }

    /// <summary>解析经过队友或敌人筛选后的平均生命值；无匹配单位时返回 0。</summary>
    public static int Resolve(ModuleAverageHealthField field, GameState state)
    {
        return field.Target == AverageHealthTargetKind.Enemies
            ? ResolveEnemyAverageHealth(field, state)
            : ResolveAllyAverageHealth(field, state);
    }

    private static int ResolveAllyAverageHealth(ModuleAverageHealthField field, GameState state)
    {
        var healthThreshold = ResolveThreshold(field.HealthThreshold, field.HealthThresholdField, state, 0);
        var auras = field.AuraSpellIds ?? [];
        if (RequiresAura(field.AuraFilter)
            && (auras.Count == 0 || auras.Any(id => !GroupContainsAuraField(state.Group, id))))
        {
            return 0;
        }

        long total = 0;
        var count = 0;
        for (var i = 1; i <= 30; i++)
        {
            if (!state.Group.TryGetValue(i.ToString(), out var data)
                || !RoleNotZero(data)
                || !MatchesRoleFilter(data, field.RoleFilter, field.Role)
                || !TryInt(GetField(data, "生命值"), out var health)
                || !MatchesThreshold(field.HealthFilter, health, healthThreshold)
                || !MatchesAuraFilter(data, field.AuraFilter, auras))
            {
                continue;
            }

            total += health;
            count++;
        }

        return RoundedAverage(total, count);
    }

    private static int ResolveEnemyAverageHealth(ModuleAverageHealthField field, GameState state)
    {
        var healthThreshold = ResolveThreshold(field.HealthThreshold, field.HealthThresholdField, state, 0);
        var rangeThreshold = ResolveThreshold(field.RangeThreshold, field.RangeThresholdField, state, 0);
        var auras = field.AuraSpellIds ?? [];
        if (RequiresAura(field.AuraFilter) && auras.Count == 0)
        {
            return 0;
        }

        long total = 0;
        var count = 0;
        for (var i = 1; i <= 20; i++)
        {
            if (!state.Nameplates.TryGetValue(i.ToString(), out var data)
                || GetField(data, "存在") is bool present && !present
                || !TryInt(GetField(data, "距离"), out var range)
                || range <= 0
                || !TryInt(GetField(data, "生命值"), out var health)
                || !MatchesThreshold(field.HealthFilter, health, healthThreshold)
                || !MatchesThreshold(field.RangeFilter, range, rangeThreshold)
                || !MatchesAuraFilter(data, field.AuraFilter, auras)
                || !MatchesCombatFilter(data, field.CombatFilter))
            {
                continue;
            }

            total += health;
            count++;
        }

        return RoundedAverage(total, count);
    }

    private static int RoundedAverage(long total, int count)
        => count == 0
            ? 0
            : (int)Math.Round((double)total / count, MidpointRounding.AwayFromZero);

    private static bool MatchesThreshold(EnemyThresholdFilterKind filter, int value, int threshold)
    {
        return filter switch
        {
            EnemyThresholdFilterKind.Above => value > threshold,
            EnemyThresholdFilterKind.Below => value < threshold,
            _ => true
        };
    }

    private static bool MatchesOptionalThreshold(
        IReadOnlyDictionary<string, object?> data,
        string field,
        EnemyThresholdFilterKind filter,
        int threshold)
    {
        return filter == EnemyThresholdFilterKind.None
            || TryInt(GetField(data, field), out var value) && MatchesThreshold(filter, value, threshold);
    }

    private static bool MatchesDispelFilter(
        IReadOnlyDictionary<string, object?> data,
        AllyDispelFilterKind filter,
        int? dispelType)
    {
        if (filter == AllyDispelFilterKind.None)
        {
            return true;
        }

        if (dispelType is null)
        {
            return false;
        }

        var matches = TryInt(GetField(data, "驱散"), out var actual) && actual == dispelType.Value;
        return filter == AllyDispelFilterKind.WithType ? matches : !matches;
    }

    private static bool MatchesAuraFilter(
        IReadOnlyDictionary<string, object?> data,
        EnemyAuraFilterKind filter,
        IReadOnlyList<long> auraSpellIds)
    {
        return filter switch
        {
            EnemyAuraFilterKind.WithAura => HasAura(data, auraSpellIds[0]),
            EnemyAuraFilterKind.WithoutAura => !HasAura(data, auraSpellIds[0]),
            EnemyAuraFilterKind.WithAnyAura => HasAnyAura(data, auraSpellIds),
            EnemyAuraFilterKind.WithoutAnyAura => !HasAnyAura(data, auraSpellIds),
            _ => true
        };
    }

    private static bool MatchesAuraDuration(
        IReadOnlyDictionary<string, object?> data,
        AuraDurationFilterKind filter,
        long? auraSpellId,
        int threshold)
    {
        if (filter is AuraDurationFilterKind.None
            or AuraDurationFilterKind.Longest
            or AuraDurationFilterKind.Shortest)
        {
            return true;
        }

        if (auraSpellId is null)
        {
            return false;
        }

        var duration = GetAuraDuration(data, auraSpellId.Value);
        return filter switch
        {
            AuraDurationFilterKind.Above => duration > threshold,
            AuraDurationFilterKind.Below => duration > 0 && duration < threshold,
            AuraDurationFilterKind.Equal => duration == threshold,
            _ => true
        };
    }

    private static int GetAuraDuration(IReadOnlyDictionary<string, object?> data, long auraSpellId)
        => TryInt(GetField(data, SpellFieldKey.AuraMember(auraSpellId)), out var duration) ? duration : 0;

    private static bool MatchesCombatFilter(
        IReadOnlyDictionary<string, object?> data,
        EnemyCombatFilterKind filter)
    {
        if (filter == EnemyCombatFilterKind.None)
        {
            return true;
        }

        // 姓名板未配置战斗格时该字段恒为 false, 「战斗中」筛选自然统计不到人。
        var inCombat = GetField(data, "战斗") is bool flag && flag;
        return filter == EnemyCombatFilterKind.InCombat ? inCombat : !inCombat;
    }

    private static bool RequiresAura(EnemyAuraFilterKind filter)
        => filter is EnemyAuraFilterKind.WithAura
            or EnemyAuraFilterKind.WithoutAura
            or EnemyAuraFilterKind.WithAnyAura
            or EnemyAuraFilterKind.WithoutAnyAura;

    /// <summary>在职责 != 0 的单位里, 取生命值 &lt; 阈值且满足 predicate 的最低血量单位（含 0 和负数）。</summary>
    private static string? LowestHealth(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> group,
        int threshold,
        Func<IReadOnlyDictionary<string, object?>, bool> predicate)
    {
        string? lowestUnit = null;
        var lowestPct = threshold;
        for (var i = 1; i <= 30; i++)
        {
            var key = i.ToString();
            if (!group.TryGetValue(key, out var data) || !RoleNotZero(data) || !predicate(data))
            {
                continue;
            }

            if (!TryInt(GetField(data, "生命值"), out var pct))
            {
                continue;
            }

            if (pct < threshold && pct < lowestPct)
            {
                lowestUnit = key;
                lowestPct = pct;
            }
        }

        return lowestUnit;
    }

    /// <summary>按职责取首个(reverse=false)或逆序首个(reverse=true)且满足 predicate 的单位。</summary>
    private static string? UnitWithRole(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> group,
        int role,
        bool reverse,
        Func<IReadOnlyDictionary<string, object?>, bool> predicate)
    {
        string? first = null;
        string? last = null;
        for (var i = 1; i <= 30; i++)
        {
            var key = i.ToString();
            if (!group.TryGetValue(key, out var data))
            {
                continue;
            }

            if (!TryInt(GetField(data, "职责"), out var r) || r != role || !predicate(data))
            {
                continue;
            }

            first ??= key;
            last = key;
        }

        return reverse ? last : first;
    }

    /// <summary>取拥有某光环(数值 &gt; 0)且持续时间最长或最短的单位。</summary>
    private static string? UnitWithAura(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> group,
        long auraSpellId,
        bool shortest)
    {
        string? bestUnit = null;
        var bestDuration = shortest ? int.MaxValue : 0;
        for (var i = 1; i <= 30; i++)
        {
            var key = i.ToString();
            if (!group.TryGetValue(key, out var data) || !RoleNotZero(data))
            {
                continue;
            }

            if (!TryInt(GetField(data, SpellFieldKey.AuraMember(auraSpellId)), out var duration) || duration <= 0)
            {
                continue;
            }

            var better = shortest ? duration < bestDuration : duration > bestDuration;
            if (bestUnit is null || better)
            {
                bestUnit = key;
                bestDuration = duration;
            }
        }

        return bestUnit;
    }

    /// <summary>取拥有指定驱散类型的首个单位。</summary>
    private static string? UnitWithDispelType(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> group,
        int dispelType)
    {
        for (var i = 1; i <= 30; i++)
        {
            var key = i.ToString();
            if (!group.TryGetValue(key, out var data) || !RoleNotZero(data))
            {
                continue;
            }

            if (TryInt(GetField(data, "驱散"), out var val) && val == dispelType)
            {
                return key;
            }
        }

        return null;
    }

    /// <summary>
    /// 在职责 != 0 且满足 predicate 的单位里，取治疗吸收 &gt; 阈值的最高单位；
    /// 没有符合阈值的单位时返回 null。
    /// </summary>
    private static string? HighestHealingAbsorb(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> group,
        int threshold,
        Func<IReadOnlyDictionary<string, object?>, bool> predicate)
    {
        string? bestUnit = null;
        var highestAbsorb = 0;
        for (var i = 1; i <= 30; i++)
        {
            var key = i.ToString();
            if (!group.TryGetValue(key, out var data)
                || !RoleNotZero(data)
                || !predicate(data))
            {
                continue;
            }

            if (TryInt(GetField(data, "治疗吸收"), out var absorb)
                && absorb > 0
                && absorb > threshold
                && absorb > highestAbsorb)
            {
                bestUnit = key;
                highestAbsorb = absorb;
            }
        }

        return bestUnit;
    }

    /// <summary>统计职责 != 0 且满足 predicate 的单位数量。</summary>
    private static int CountUnits(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> group,
        Func<IReadOnlyDictionary<string, object?>, bool> predicate)
    {
        var count = 0;
        for (var i = 1; i <= 30; i++)
        {
            if (group.TryGetValue(i.ToString(), out var data) && RoleNotZero(data) && predicate(data))
            {
                count++;
            }
        }

        return count;
    }

    private static bool BelowThreshold(IReadOnlyDictionary<string, object?> data, int threshold)
    {
        return TryInt(GetField(data, "生命值"), out var pct) && pct > 0 && pct < threshold;
    }

    private static bool AboveHealingAbsorbThreshold(
        IReadOnlyDictionary<string, object?> data,
        int threshold)
    {
        return TryInt(GetField(data, "治疗吸收"), out var absorb)
            && absorb > threshold;
    }

    private static int ResolveThreshold(
        int? fixedValue,
        string? fieldName,
        GameState state,
        int defaultValue = DefaultThreshold)
    {
        return !string.IsNullOrWhiteSpace(fieldName)
            && ModuleConditionEvaluator.TryResolveInt(state, fieldName, out var dynamicValue)
                ? dynamicValue
                : fixedValue ?? defaultValue;
    }

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

    private static bool AuraEquals(IReadOnlyDictionary<string, object?> data, long auraSpellId, int target)
    {
        return TryInt(GetField(data, SpellFieldKey.AuraMember(auraSpellId)), out var val) && val == target;
    }

    private static bool MatchesRoleFilter(
        IReadOnlyDictionary<string, object?> data,
        UnitRoleFilterKind? filter,
        int? role)
    {
        if (filter is null)
        {
            return true;
        }

        if (role is null || !TryInt(GetField(data, "职责"), out var actualRole))
        {
            return false;
        }

        return filter == UnitRoleFilterKind.Include
            ? actualRole == role.Value
            : actualRole != role.Value;
    }

    // 职责为 None/无法解析时视为不跳过(返回 true), 与 utils.py 的 _role_not_zero 一致。
    private static bool RoleNotZero(IReadOnlyDictionary<string, object?> data)
    {
        var role = GetField(data, "职责");
        if (role is null)
        {
            return true;
        }

        return !TryInt(role, out var r) || r != 0;
    }

    private static bool HasAura(IReadOnlyDictionary<string, object?> data, long auraSpellId)
    {
        return TryInt(GetField(data, SpellFieldKey.AuraMember(auraSpellId)), out var n) && n != 0;
    }

    private static bool HasAnyAura(IReadOnlyDictionary<string, object?> data, IEnumerable<long> auraSpellIds)
    {
        foreach (var spellId in auraSpellIds)
        {
            if (HasAura(data, spellId))
            {
                return true;
            }
        }

        return false;
    }

    private static bool GroupContainsAuraField(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> group,
        long spellId)
    {
        var key = SpellFieldKey.AuraMember(spellId);
        return group.Values.Any(member => member.ContainsKey(key));
    }

    private static bool RequiresAura(UnitSelectorKind kind)
        => kind is UnitSelectorKind.LowestHealthWithAnyAura
            or UnitSelectorKind.LowestHealthWithoutAnyAura
            or UnitSelectorKind.LowestHealthWithoutAura
            or UnitSelectorKind.LowestHealthWithAura
            or UnitSelectorKind.LowestHealthWithAuraCount
            or UnitSelectorKind.UnitWithRoleWithoutAura
            or UnitSelectorKind.UnitWithAura
            or UnitSelectorKind.UnitWithAuraShortest
            or UnitSelectorKind.HighestHealingAbsorbWithAnyAura
            or UnitSelectorKind.HighestHealingAbsorbWithoutAnyAura
            or UnitSelectorKind.HighestHealingAbsorbWithoutAura
            or UnitSelectorKind.HighestHealingAbsorbWithAura
            or UnitSelectorKind.HighestHealingAbsorbWithAuraCount;

    private static bool RequiresAura(CountKind kind)
        => kind is CountKind.UnitsWithoutAuraBelowHealth
            or CountKind.UnitsWithAura
            or CountKind.UnitsWithAuraBelowHealth
            or CountKind.UnitsWithoutAuraAboveHealingAbsorb
            or CountKind.UnitsWithAuraAboveHealingAbsorb;

    private static long? FirstAura(List<long>? auraSpellIds)
    {
        return auraSpellIds is { Count: > 0 } ? auraSpellIds[0] : null;
    }

    private static object? GetField(IReadOnlyDictionary<string, object?> data, string field)
    {
        return data.TryGetValue(field, out var value) ? value : null;
    }

    // 模仿 Python int() 的 try/except: null 或无法解析返回 false, 调用侧据此跳过。
    private static bool TryInt(object? value, out int result)
    {
        switch (value)
        {
            case int i:
                result = i;
                return true;
            case long l:
                result = (int)l;
                return true;
            case bool b:
                result = b ? 1 : 0;
                return true;
            case string s when int.TryParse(s, out var parsed):
                result = parsed;
                return true;
            default:
                result = 0;
                return false;
        }
    }
}
