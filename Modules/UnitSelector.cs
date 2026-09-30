namespace Shigure;

/// <summary>
/// 把 <see cref="ModuleUnit"/> / <see cref="ModuleCountField"/> 定义在当前 group 状态下解析为
/// 单位槽位或数量。
/// 队友单位：职责 != 0 → 条件组筛选 → 按目标字段唯一选取；并列取最小槽位。
/// </summary>
public static class UnitSelector
{

    /// <summary>解析动态单位为 group 槽位("1".."40"), 无匹配返回 null。</summary>
    public static string? Resolve(ModuleUnit unit, GameState state)
    {
        if (!IsValidUnitSelection(unit)
            || !IsValidCountFilterConfiguration(unit.FilterGroups, enemy: false))
        {
            return null;
        }

        if (unit.TargetField == UnitTargetFieldKind.Aura
            && (unit.TargetAuraSpellId is not > 0
                || !GroupContainsAuraField(state.Group, unit.TargetAuraSpellId.Value)))
        {
            return null;
        }

        var referencedAuraIds = (unit.FilterGroups ?? [])
            .SelectMany(group => group.Conditions ?? [])
            .Where(condition => condition.Enabled && condition.Field == CountConditionFieldKind.Aura)
            .Select(condition => condition.AuraSpellId.GetValueOrDefault())
            .Where(id => id > 0);
        if (referencedAuraIds.Any(id => !GroupContainsAuraField(state.Group, id)))
        {
            return null;
        }

        var candidates = new List<(string Key, IReadOnlyDictionary<string, object?> Data)>();
        for (var i = 1; i <= GroupStateLayout.SlotCount; i++)
        {
            var key = i.ToString();
            if (!state.Group.TryGetValue(key, out var data)
                || !RoleNotZero(data)
                || !MatchesCountFilterGroups(data, state, unit.FilterGroups))
            {
                continue;
            }

            candidates.Add((key, data));
        }

        return SelectUniqueUnit(unit, candidates);
    }

    /// <summary>读取已解析单位的目标字段值；单位未解析时返回 null。</summary>
    public static object? ResolveValue(ModuleUnit unit, string? slot, GameState state)
    {
        if (slot is null
            || !state.Group.TryGetValue(slot, out var data)
            || !IsValidUnitSelection(unit))
        {
            return null;
        }

        return unit.TargetField switch
        {
            UnitTargetFieldKind.Health => TryInt(GetField(data, "生命值"), out var health) ? health : null,
            UnitTargetFieldKind.HealingAbsorb => TryInt(GetField(data, "治疗吸收"), out var absorb) ? absorb : null,
            UnitTargetFieldKind.Role => TryInt(GetField(data, "职责"), out var role) ? role : null,
            UnitTargetFieldKind.Dispel => TryInt(GetField(data, "驱散"), out var dispel) ? dispel : 0,
            UnitTargetFieldKind.Aura => unit.TargetAuraSpellId is { } auraId
                ? GetAuraDuration(data, auraId)
                : null,
            _ => null
        };
    }

    private static bool IsValidUnitSelection(ModuleUnit unit)
    {
        return unit.TargetField switch
        {
            UnitTargetFieldKind.Health or UnitTargetFieldKind.HealingAbsorb
                => unit.SelectionMode is UnitSelectionMode.Lowest or UnitSelectionMode.Highest,
            UnitTargetFieldKind.Role or UnitTargetFieldKind.Dispel
                => unit.SelectionMode is UnitSelectionMode.Ascending or UnitSelectionMode.Descending,
            UnitTargetFieldKind.Aura
                => (unit.SelectionMode is UnitSelectionMode.Longest or UnitSelectionMode.Shortest
                    or UnitSelectionMode.Ascending or UnitSelectionMode.Descending)
                    && unit.TargetAuraSpellId is > 0,
            _ => false
        };
    }

    private static string? SelectUniqueUnit(
        ModuleUnit unit,
        IReadOnlyList<(string Key, IReadOnlyDictionary<string, object?> Data)> candidates)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        return unit.TargetField switch
        {
            UnitTargetFieldKind.Health => SelectByField(
                candidates,
                "生命值",
                smallest: unit.SelectionMode == UnitSelectionMode.Lowest),
            UnitTargetFieldKind.HealingAbsorb => SelectByField(
                candidates,
                "治疗吸收",
                smallest: unit.SelectionMode == UnitSelectionMode.Lowest),
            UnitTargetFieldKind.Role or UnitTargetFieldKind.Dispel
                => unit.SelectionMode == UnitSelectionMode.Descending
                    ? candidates[^1].Key
                    : candidates[0].Key,
            UnitTargetFieldKind.Aura => unit.SelectionMode switch
            {
                UnitSelectionMode.Ascending => SelectByAuraSlot(candidates, unit.TargetAuraSpellId!.Value, descending: false),
                UnitSelectionMode.Descending => SelectByAuraSlot(candidates, unit.TargetAuraSpellId!.Value, descending: true),
                _ => SelectByAuraDuration(
                    candidates,
                    unit.TargetAuraSpellId!.Value,
                    shortest: unit.SelectionMode == UnitSelectionMode.Shortest)
            },
            _ => null
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

            // 数值并列时保留先出现的最小槽位。
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
        long auraSpellId,
        bool shortest)
    {
        string? bestKey = null;
        var bestValue = shortest ? int.MaxValue : int.MinValue;
        foreach (var candidate in candidates)
        {
            var duration = GetAuraDuration(candidate.Data, auraSpellId);
            if (duration <= 0)
            {
                continue;
            }

            if (bestKey is null || (shortest ? duration < bestValue : duration > bestValue))
            {
                bestKey = candidate.Key;
                bestValue = duration;
            }
        }

        return bestKey;
    }

    private static string? SelectByAuraSlot(
        IReadOnlyList<(string Key, IReadOnlyDictionary<string, object?> Data)> candidates,
        long auraSpellId,
        bool descending)
    {
        if (descending)
        {
            for (var i = candidates.Count - 1; i >= 0; i--)
            {
                if (GetAuraDuration(candidates[i].Data, auraSpellId) > 0)
                {
                    return candidates[i].Key;
                }
            }

            return null;
        }

        foreach (var candidate in candidates)
        {
            if (GetAuraDuration(candidate.Data, auraSpellId) > 0)
            {
                return candidate.Key;
            }
        }

        return null;
    }

    /// <summary>解析数量字段为整数。</summary>
    public static int Resolve(ModuleCountField count, GameState state)
    {
        return IsValidCountFilterConfiguration(count.FilterGroups, enemy: false)
            ? CountUnits(state.Group, data => MatchesCountFilterGroups(data, state, count.FilterGroups))
            : 0;
    }

    /// <summary>
    /// 解析敌人数量字段为整数: 统计姓名板中距离 &gt; 0 且满足全部已启用筛选(生命值 / 光环 / 距离 / 战斗)的敌人。
    /// 姓名板未配置距离时该字段恒为 0, 结果也恒为 0。
    /// </summary>
    public static int Resolve(ModuleEnemyCountField count, GameState state)
    {
        if (!IsValidCountFilterConfiguration(count.FilterGroups, enemy: true))
        {
            return 0;
        }

        var nameplates = state.Nameplates;
        var result = 0;
        for (var i = 1; i <= NameplateStateLayout.SlotCount; i++)
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

            if (!MatchesCountFilterGroups(data, state, count.FilterGroups))
            {
                continue;
            }

            result++;
        }

        return result;
    }

    /// <summary>解析经过条件组筛选后的平均生命值；无匹配单位时返回 0。</summary>
    public static int Resolve(ModuleAverageHealthField field, GameState state)
    {
        var enemy = field.Target == AverageHealthTargetKind.Enemies;
        if (!IsValidCountFilterConfiguration(field.FilterGroups, enemy))
        {
            return 0;
        }

        return enemy
            ? ResolveEnemyAverageHealth(field, state)
            : ResolveAllyAverageHealth(field, state);
    }

    private static int ResolveAllyAverageHealth(ModuleAverageHealthField field, GameState state)
    {
        var referencedAuraIds = EnabledAuraSpellIds(field.FilterGroups);
        if (referencedAuraIds.Any(id => !GroupContainsAuraField(state.Group, id)))
        {
            return 0;
        }

        long total = 0;
        var count = 0;
        for (var i = 1; i <= GroupStateLayout.SlotCount; i++)
        {
            if (!state.Group.TryGetValue(i.ToString(), out var data)
                || !RoleNotZero(data)
                || !MatchesCountFilterGroups(data, state, field.FilterGroups)
                || !TryInt(GetField(data, "生命值"), out var health))
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
        long total = 0;
        var count = 0;
        for (var i = 1; i <= NameplateStateLayout.SlotCount; i++)
        {
            if (!state.Nameplates.TryGetValue(i.ToString(), out var data)
                || GetField(data, "存在") is bool present && !present
                || !TryInt(GetField(data, "距离"), out var range)
                || range <= 0
                || !MatchesCountFilterGroups(data, state, field.FilterGroups)
                || !TryInt(GetField(data, "生命值"), out var health))
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

    private static IEnumerable<long> EnabledAuraSpellIds(IReadOnlyList<ModuleCountConditionGroup>? groups)
        => (groups ?? [])
            .SelectMany(group => group.Conditions ?? [])
            .Where(condition => condition.Enabled && condition.Field == CountConditionFieldKind.Aura)
            .Select(condition => condition.AuraSpellId.GetValueOrDefault())
            .Where(id => id > 0);

    /// <summary>统计职责 != 0 且满足 predicate 的单位数量。</summary>
    private static int CountUnits(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> group,
        Func<IReadOnlyDictionary<string, object?>, bool> predicate)
    {
        var count = 0;
        for (var i = 1; i <= GroupStateLayout.SlotCount; i++)
        {
            if (group.TryGetValue(i.ToString(), out var data) && RoleNotZero(data) && predicate(data))
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsValidCountFilterConfiguration(
        IReadOnlyList<ModuleCountConditionGroup>? groups,
        bool enemy)
    {
        foreach (var condition in (groups ?? []).SelectMany(group => group.Conditions ?? []).Where(condition => condition.Enabled))
        {
            var allowed = enemy
                ? condition.Field is CountConditionFieldKind.Health
                    or CountConditionFieldKind.Range
                    or CountConditionFieldKind.Threat
                    or CountConditionFieldKind.Combat
                    or CountConditionFieldKind.ImprovedGarrote
                    or CountConditionFieldKind.Aura
                : condition.Field is CountConditionFieldKind.Health
                    or CountConditionFieldKind.HealingAbsorb
                    or CountConditionFieldKind.Role
                    or CountConditionFieldKind.Dispel
                    or CountConditionFieldKind.Class
                    or CountConditionFieldKind.Aura;
            if (!allowed
                || condition.Field == CountConditionFieldKind.Aura && condition.AuraSpellId is not > 0
                || condition.Field != CountConditionFieldKind.Aura && condition.AuraSpellId is not null
                || (condition.Field is CountConditionFieldKind.Role
                        or CountConditionFieldKind.Dispel
                        or CountConditionFieldKind.Class
                        or CountConditionFieldKind.ImprovedGarrote
                        or CountConditionFieldKind.Combat)
                    && condition.Comparison is not (CountConditionComparisonKind.Equal
                        or CountConditionComparisonKind.NotEqual)
                || condition.ValueKind == CountConditionValueKind.StateField
                    && (condition.Field is CountConditionFieldKind.Role
                            or CountConditionFieldKind.Dispel
                            or CountConditionFieldKind.Class
                            or CountConditionFieldKind.ImprovedGarrote
                            or CountConditionFieldKind.Combat
                        || string.IsNullOrWhiteSpace(condition.ValueField)))
            {
                return false;
            }
            if (condition.Field == CountConditionFieldKind.Threat
                && condition.ValueKind == CountConditionValueKind.Constant && condition.Value is < 0 or > 3)
            {
                return false;
            }
            if (condition.Field == CountConditionFieldKind.ImprovedGarrote && condition.Value is < 0 or > 2)
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesCountFilterGroups(
        IReadOnlyDictionary<string, object?> data,
        GameState state,
        IReadOnlyList<ModuleCountConditionGroup>? groups)
    {
        foreach (var group in groups ?? [])
        {
            var enabled = (group.Conditions ?? []).Where(condition => condition.Enabled).ToArray();
            if (enabled.Length == 0)
            {
                continue;
            }

            var matches = group.Mode == CountConditionGroupMode.Any
                ? enabled.Any(condition => MatchesCountCondition(data, state, condition))
                : enabled.All(condition => MatchesCountCondition(data, state, condition));
            if (!matches)
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesCountCondition(
        IReadOnlyDictionary<string, object?> data,
        GameState state,
        ModuleCountCondition condition)
    {
        if (!TryReadCountConditionValue(data, condition, out var actual))
        {
            return false;
        }

        var expected = condition.ValueKind == CountConditionValueKind.StateField
            ? ReadReferencedValue(state, condition.ValueField ?? string.Empty)
            : condition.Value;
        if (condition.Field == CountConditionFieldKind.Aura && actual == 0)
        {
            return condition.Comparison == CountConditionComparisonKind.Equal && expected == 0;
        }

        return condition.Comparison switch
        {
            CountConditionComparisonKind.Equal => actual == expected,
            CountConditionComparisonKind.NotEqual => actual != expected,
            CountConditionComparisonKind.GreaterThan => actual > expected,
            CountConditionComparisonKind.LessThan => actual < expected,
            CountConditionComparisonKind.GreaterThanOrEqual => actual >= expected,
            CountConditionComparisonKind.LessThanOrEqual => actual <= expected,
            _ => false
        };
    }

    // 公式动态数值写在 $dynamicvalues；数量和单位值名称分别在 $counts / $unithealth。
    private static int ReadReferencedValue(GameState state, string field)
    {
        var key = field.Trim();
        if (TryReadNamedInt(state, "$dynamicvalues", key, out var number)
            || TryReadNamedInt(state, "$counts", key, out number)
            || TryReadNamedInt(state, "$unithealth", key, out number))
        {
            return number;
        }

        return state.GetInt(key);
    }

    private static bool TryReadNamedInt(GameState state, string dictionaryKey, string field, out int value)
    {
        value = 0;
        if (field.Length == 0 || !state.Values.TryGetValue(dictionaryKey, out var obj))
        {
            return false;
        }

        if (obj is IReadOnlyDictionary<string, int> ints && ints.TryGetValue(field, out value))
        {
            return true;
        }

        return obj is IReadOnlyDictionary<string, object?> values
            && values.TryGetValue(field, out var raw)
            && TryInt(raw, out value);
    }

    private static bool TryReadCountConditionValue(
        IReadOnlyDictionary<string, object?> data,
        ModuleCountCondition condition,
        out int value)
    {
        if (condition.Field == CountConditionFieldKind.Aura)
        {
            value = condition.AuraSpellId is { } spellId ? GetAuraDuration(data, spellId) : 0;
            return condition.AuraSpellId is > 0;
        }

        if (condition.Field == CountConditionFieldKind.Combat)
        {
            value = GetField(data, "战斗") is bool inCombat && inCombat ? 1 : 0;
            return true;
        }

        var field = condition.Field switch
        {
            CountConditionFieldKind.Health => "生命值",
            CountConditionFieldKind.HealingAbsorb => "治疗吸收",
            CountConditionFieldKind.Role => "职责",
            CountConditionFieldKind.Dispel => "驱散",
            CountConditionFieldKind.Class => "职业",
            CountConditionFieldKind.Range => "距离",
            CountConditionFieldKind.ImprovedGarrote => NameplateStateLayout.ImprovedGarroteField,
            CountConditionFieldKind.Threat => NameplateStateLayout.ThreatField,
            _ => string.Empty
        };
        if (field.Length == 0)
        {
            value = 0;
            return false;
        }

        if (TryInt(GetField(data, field), out value))
        {
            return condition.Field switch
            {
                CountConditionFieldKind.Class => value is >= 1 and <= 13,
                CountConditionFieldKind.ImprovedGarrote => value is >= 0 and <= 2,
                CountConditionFieldKind.Threat => value is >= 0 and <= 3,
                _ => true
            };
        }

        // 未检测到驱散值时等价于 0，使“驱散 != 某类型”保持原有语义。
        value = 0;
        return condition.Field == CountConditionFieldKind.Dispel;
    }

    private static int GetAuraDuration(IReadOnlyDictionary<string, object?> data, long auraSpellId)
        => TryInt(GetField(data, SpellFieldKey.AuraMember(auraSpellId)), out var duration) ? duration : 0;

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

    private static bool GroupContainsAuraField(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> group,
        long spellId)
    {
        var key = SpellFieldKey.AuraMember(spellId);
        return group.Values.Any(member => member.ContainsKey(key));
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
