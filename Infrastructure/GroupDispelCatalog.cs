namespace Shigure;

/// <summary>队伍驱散的配置键、像素字段和模块字段共用同一映射。</summary>
internal static class GroupDispelCatalog
{
    internal sealed record Entry(string ConfigName, string Name, string AuraType, int TypeId,
        UnitTargetFieldKind TargetField, CountConditionFieldKind ConditionField)
    {
        public string FieldName => "驱散" + Name;
    }

    public static readonly Entry[] Entries =
    [
        new("dispelMagic", "魔法", "Magic", 1, UnitTargetFieldKind.DispelMagic, CountConditionFieldKind.DispelMagic),
        new("dispelCurse", "诅咒", "Curse", 2, UnitTargetFieldKind.DispelCurse, CountConditionFieldKind.DispelCurse),
        new("dispelDisease", "疾病", "Disease", 3, UnitTargetFieldKind.DispelDisease, CountConditionFieldKind.DispelDisease),
        new("dispelPoison", "中毒", "Poison", 4, UnitTargetFieldKind.DispelPoison, CountConditionFieldKind.DispelPoison),
        new("dispelBleed", "流血", "Bleed", 11, UnitTargetFieldKind.DispelBleed, CountConditionFieldKind.DispelBleed)
    ];

    public static Entry? Find(UnitTargetFieldKind field) => Entries.FirstOrDefault(entry => entry.TargetField == field);
    public static Entry? Find(CountConditionFieldKind field) => Entries.FirstOrDefault(entry => entry.ConditionField == field);

    // 旧字段不占像素，只为旧条件文本和单位值名称保留确定的类型编号。
    public static int ReadLegacyType(IReadOnlyDictionary<string, object?> data)
        => Entries.FirstOrDefault(entry => data.TryGetValue(entry.FieldName, out var value)
            && value is int duration && duration > 0)?.TypeId ?? 0;

    public static void MigrateCondition(ModuleCountCondition condition)
    {
        if (condition.Field != CountConditionFieldKind.Dispel
            || condition.ValueKind != CountConditionValueKind.Constant
            || condition.Comparison is not (CountConditionComparisonKind.Equal or CountConditionComparisonKind.NotEqual))
            return;
        var entry = Entries.FirstOrDefault(entry => entry.TypeId == condition.Value);
        if (entry is null) return;
        condition.Field = entry.ConditionField;
        condition.Comparison = condition.Comparison == CountConditionComparisonKind.Equal
            ? CountConditionComparisonKind.GreaterThan : CountConditionComparisonKind.Equal;
        condition.Value = 0;
    }
}
