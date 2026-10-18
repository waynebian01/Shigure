using System.Globalization;
using System.Text.RegularExpressions;

namespace Shigure;

public enum ModuleTeammateTargetKind
{
    Slot,
    StateField,
    DynamicUnit
}

public sealed class ModuleTeammateTarget
{
    public ModuleTeammateTargetKind Kind { get; set; }
    public int? Slot { get; set; }
    public string? Field { get; set; }
    public string? UnitName { get; set; }

    public ModuleTeammateTarget Clone() => new() { Kind = Kind, Slot = Slot, Field = Field, UnitName = UnitName };

    internal string ToSelectionValue() => Kind switch
    {
        ModuleTeammateTargetKind.Slot => $"slot:{Slot}",
        ModuleTeammateTargetKind.StateField => $"state:{Field}",
        ModuleTeammateTargetKind.DynamicUnit => $"unit:{UnitName}",
        _ => string.Empty
    };

    internal static ModuleTeammateTarget? ParseSelectionValue(string value)
    {
        if (value.StartsWith("slot:", StringComparison.Ordinal)
            && int.TryParse(value[5..], out var slot) && slot is >= 1 and <= GroupStateLayout.SlotCount)
            return new() { Kind = ModuleTeammateTargetKind.Slot, Slot = slot };
        if (value is "state:施法目标" or "state:队伍类型")
            return new() { Kind = ModuleTeammateTargetKind.StateField, Field = value[6..] };
        if (value.StartsWith("unit:", StringComparison.Ordinal) && value.Length > 5)
            return new() { Kind = ModuleTeammateTargetKind.DynamicUnit, UnitName = value[5..] };
        return null;
    }
}

/// <summary>按本帧队友目标调整成员字段；选人前后共享相同的写入和别名同步逻辑。</summary>
internal static class TeammateValueAdjustment
{
    internal static bool CanApplyBeforeSelection(ModuleDefinition module, ModuleValueAdjustment adjustment)
    {
        if (adjustment.TeammateTarget is null or { Kind: ModuleTeammateTargetKind.DynamicUnit }) return false;
        var lateFields = module.Units.SelectMany(unit => new[] { unit.Name, unit.ValueName })
            .Concat(module.Counts.Select(count => count.Name))
            .Concat(module.EnemyCounts.Select(count => count.Name))
            .Concat(module.AverageHealthFields.Select(field => field.Name))
            .Concat(module.ValueAdjustments.Where(value => value.IsCustom || !string.IsNullOrWhiteSpace(value.Formula))
                .Select(value => value.Field));
        // 包括条件左右两侧的动态引用，避免用尚未生成的默认值提前判断。
        return !lateFields.Where(name => !string.IsNullOrWhiteSpace(name)).Any(name =>
            Regex.IsMatch(adjustment.Condition, $@"(?<![_$\p{{L}}\p{{N}}]){Regex.Escape(name!)}(?![_$\p{{L}}\p{{N}}])"));
    }

    internal static bool Apply(ModuleDefinition module, GameState state, ModuleValueAdjustment adjustment, bool matched)
    {
        var slot = ResolveTarget(state, adjustment.TeammateTarget!);
        if (slot is null || !state.Group.TryGetValue(slot, out var member)
            || !UnitSelector.IsValidTeammate(member)
            || !member.TryGetValue(adjustment.Field, out var current)
            || !adjustment.TryGetOperand(out var operand)) return false;

        object? value;
        if (adjustment.EffectiveOperation == "=")
            value = adjustment.IsConditionBoolean ? matched : operand;
        else if (operand is int delta && TryInt(current, out var number))
            value = adjustment.EffectiveOperation == "-" ? number - delta : number + delta;
        else return false;

        // 复制后替换成员，支持只读来源且不修改扫描器持有的数据。
        var updated = new Dictionary<string, object?>(member, StringComparer.Ordinal) { [adjustment.Field] = value };
        var changedFields = new HashSet<string>(StringComparer.Ordinal) { adjustment.Field };
        if (state.Values.TryGetValue("$groupFieldAliases", out var aliasesObj)
            && aliasesObj is IReadOnlyDictionary<string, string[]> aliases
            && aliases.TryGetValue(adjustment.Field, out var aliasFields))
            foreach (var alias in aliasFields.Where(updated.ContainsKey))
            {
                updated[alias] = value;
                changedFields.Add(alias);
            }
        if (GroupDispelCatalog.Entries.FirstOrDefault(entry => entry.FieldName == adjustment.Field) is { } dispel)
        {
            updated[dispel.LegacyFieldName] = value;
            updated["驱散"] = GroupDispelCatalog.ReadLegacyType(updated);
            changedFields.Add(dispel.LegacyFieldName);
            changedFields.Add("驱散");
        }
        state.Values["group"] = new Dictionary<string, IReadOnlyDictionary<string, object?>>(state.Group, StringComparer.Ordinal)
        {
            [slot] = updated
        };
        RefreshSelectedValues(module, state, slot, changedFields);
        return true;
    }

    private static string? ResolveTarget(GameState state, ModuleTeammateTarget target)
    {
        var index = 0;
        switch (target.Kind)
        {
            case ModuleTeammateTargetKind.Slot:
                index = target.Slot.GetValueOrDefault();
                break;
            case ModuleTeammateTargetKind.StateField when target.Field is "施法目标" or "队伍类型":
                if (!TryInt(state.GetValue(target.Field), out index)) return null;
                if (target.Field == "队伍类型" && index is 0 or 46) index = 1;
                break;
            case ModuleTeammateTargetKind.DynamicUnit:
                if (target.UnitName is not null && state.Values.TryGetValue("$units", out var unitsObj)
                    && unitsObj is IReadOnlyDictionary<string, string?> units
                    && units.TryGetValue(target.UnitName, out var resolved) && int.TryParse(resolved, out index)) break;
                return null;
            default:
                return null;
        }
        return index is >= 1 and <= GroupStateLayout.SlotCount ? index.ToString(CultureInfo.InvariantCulture) : null;
    }

    private static void RefreshSelectedValues(ModuleDefinition module, GameState state, string slot, IReadOnlySet<string> changedFields)
    {
        if (!state.Values.TryGetValue("$units", out var unitsObj) || unitsObj is not IReadOnlyDictionary<string, string?> units
            || !state.Values.TryGetValue("$unithealth", out var valuesObj) || valuesObj is not Dictionary<string, object?> values) return;
        foreach (var unit in module.Units)
        {
            if (string.IsNullOrWhiteSpace(unit.ValueName) || !units.TryGetValue(unit.Name, out var selected) || selected != slot) continue;
            var field = GroupDispelCatalog.Find(unit.TargetField)?.FieldName ?? unit.TargetField switch
            {
                UnitTargetFieldKind.Health => "生命值",
                UnitTargetFieldKind.HealingAbsorb => "治疗吸收",
                UnitTargetFieldKind.Role => "职责",
                UnitTargetFieldKind.Dispel => "驱散",
                UnitTargetFieldKind.Aura when unit.TargetAuraSpellId is { } id => SpellFieldKey.AuraMember(id),
                _ => string.Empty
            };
            if (changedFields.Contains(field)) values[unit.ValueName] = UnitSelector.ResolveValue(unit, slot, state);
        }
    }

    private static bool TryInt(object? value, out int number)
    {
        if (value is bool boolean) { number = boolean ? 1 : 0; return true; }
        return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
    }
}
