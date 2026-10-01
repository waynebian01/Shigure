namespace Shigure;

/// <summary>队友单位最终选取时比较的目标字段。</summary>
public enum UnitTargetFieldKind
{
    /// <summary>生命值：最低 / 最高。</summary>
    Health,

    /// <summary>治疗吸收：最低 / 最高。</summary>
    HealingAbsorb,

    /// <summary>职责：正序 / 倒序（按队伍槽位）。</summary>
    Role,

    /// <summary>驱散：正序 / 倒序（按队伍槽位）。</summary>
    Dispel,

    /// <summary>光环剩余时间：最长 / 最短；或按队伍槽位正序 / 倒序。</summary>
    Aura
}

/// <summary>队友单位在筛选后的唯一选取方式。</summary>
public enum UnitSelectionMode
{
    /// <summary>数值最低（生命值 / 治疗吸收）。</summary>
    Lowest,

    /// <summary>数值最高（生命值 / 治疗吸收）。</summary>
    Highest,

    /// <summary>正序：取最小队伍槽位（职责 / 驱散 / 光环）。</summary>
    Ascending,

    /// <summary>倒序：取最大队伍槽位（职责 / 驱散 / 光环）。</summary>
    Descending,

    /// <summary>目标光环剩余时间最长。</summary>
    Longest,

    /// <summary>目标光环剩余时间最短。</summary>
    Shortest
}

/// <summary>
/// 模块内定义的命名动态单位。运行时先按条件组筛选有效队伍单位，
/// 再按目标字段选出唯一槽位("1".."40")。
/// </summary>
public sealed class ModuleUnit
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 可选的值名称：非空时把所选单位的目标字段值暴露成同名数值条件字段。
    /// </summary>
    public string? ValueName { get; set; }

    /// <summary>与队友数量共用的条件组列表；组内全部/任一，组间按且。</summary>
    public List<ModuleCountConditionGroup> FilterGroups { get; set; } = new();

    /// <summary>筛选完成后用于比较/排序的目标字段。</summary>
    public UnitTargetFieldKind TargetField { get; set; } = UnitTargetFieldKind.Health;

    /// <summary>目标字段对应的选取方式。</summary>
    public UnitSelectionMode SelectionMode { get; set; } = UnitSelectionMode.Lowest;

    /// <summary>目标字段为光环时必须指定的队伍光环 spellId。</summary>
    public long? TargetAuraSpellId { get; set; }

    public ModuleUnit Clone()
    {
        return new ModuleUnit
        {
            Name = Name,
            ValueName = ValueName,
            FilterGroups = FilterGroups.Select(group => group.Clone()).ToList(),
            TargetField = TargetField,
            SelectionMode = SelectionMode,
            TargetAuraSpellId = TargetAuraSpellId
        };
    }
}

/// <summary>数量筛选条件组的匹配方式。</summary>
public enum CountConditionGroupMode
{
    All,
    Any
}

/// <summary>数量筛选可读取的单位字段。</summary>
public enum CountConditionFieldKind
{
    Health,
    HealingAbsorb,
    Role,
    Dispel,
    Range,
    Combat,
    Aura,
    Class,
    ImprovedGarrote,
    Threat,
    CastSpell
}

/// <summary>数量筛选的比较方式。</summary>
public enum CountConditionComparisonKind
{
    Equal,
    NotEqual,
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual,
    In,
    NotIn
}

/// <summary>数量筛选右值的来源。</summary>
public enum CountConditionValueKind
{
    Constant,
    StateField,
    NumberArray
}

/// <summary>一条通用数量筛选条件。</summary>
public sealed class ModuleCountCondition
{
    public bool Enabled { get; set; } = true;
    public CountConditionFieldKind Field { get; set; }
    public long? AuraSpellId { get; set; }
    public CountConditionComparisonKind Comparison { get; set; }
    public CountConditionValueKind ValueKind { get; set; }
    public int Value { get; set; }
    public string? ValueField { get; set; }

    public ModuleCountCondition Clone()
    {
        return new ModuleCountCondition
        {
            Enabled = Enabled,
            Field = Field,
            AuraSpellId = AuraSpellId,
            Comparison = Comparison,
            ValueKind = ValueKind,
            Value = Value,
            ValueField = ValueField
        };
    }
}

/// <summary>一组通用数量筛选条件；组内按 Mode 组合，组与组之间始终按且组合。</summary>
public sealed class ModuleCountConditionGroup
{
    public CountConditionGroupMode Mode { get; set; } = CountConditionGroupMode.All;
    public List<ModuleCountCondition> Conditions { get; set; } = new();

    public ModuleCountConditionGroup Clone()
    {
        return new ModuleCountConditionGroup
        {
            Mode = Mode,
            Conditions = Conditions.Select(condition => condition.Clone()).ToList()
        };
    }
}

/// <summary>
/// 模块内定义的命名敌人数量字段。统计姓名板(nameplates)中满足筛选条件的敌人数,
/// 仅用于条件(如 近身敌人数 &gt;= 3), 不能作为目标。
/// </summary>
public sealed class ModuleEnemyCountField
{
    public string Name { get; set; } = string.Empty;
    public List<ModuleCountConditionGroup> FilterGroups { get; set; } = new();

    public ModuleEnemyCountField Clone()
    {
        return new ModuleEnemyCountField
        {
            Name = Name,
            FilterGroups = FilterGroups.Select(group => group.Clone()).ToList()
        };
    }
}

/// <summary>平均血量的统计对象。</summary>
public enum AverageHealthTargetKind
{
    Allies,
    Enemies
}

/// <summary>
/// 模块内定义的命名平均血量字段。与数量字段共用条件组列表筛选候选，
/// 再对剩余单位的生命值取平均；无匹配单位时结果为 0。旧卡片筛选字段不迁移。
/// </summary>
public sealed class ModuleAverageHealthField
{
    public string Name { get; set; } = string.Empty;
    public AverageHealthTargetKind Target { get; set; } = AverageHealthTargetKind.Allies;
    public List<ModuleCountConditionGroup> FilterGroups { get; set; } = new();

    public ModuleAverageHealthField Clone()
    {
        return new ModuleAverageHealthField
        {
            Name = Name,
            Target = Target,
            FilterGroups = FilterGroups.Select(group => group.Clone()).ToList()
        };
    }
}

/// <summary>
/// 模块内定义的命名数量字段。仅用于条件(如 低血量人数 &gt;= 3), 不能作为目标。
/// </summary>
public sealed class ModuleCountField
{
    public string Name { get; set; } = string.Empty;
    public List<ModuleCountConditionGroup> FilterGroups { get; set; } = new();

    public ModuleCountField Clone()
    {
        return new ModuleCountField
        {
            Name = Name,
            FilterGroups = FilterGroups.Select(group => group.Clone()).ToList()
        };
    }
}
