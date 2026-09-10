namespace Shigure;

/// <summary>
/// 动态单位选择器类型, 对应 utils.py 中返回单位槽位的函数。
/// </summary>
public enum UnitSelectorKind
{
    /// <summary>生命值最低的单位。get_lowest_health_unit</summary>
    LowestHealth,

    /// <summary>拥有任一光环且生命值最低。get_lowest_health_unit_with_any_aura</summary>
    LowestHealthWithAnyAura,

    /// <summary>不拥有所选任一光环且生命值最低。</summary>
    LowestHealthWithoutAnyAura,

    /// <summary>不带某光环且生命值最低。get_lowest_health_unit_without_aura</summary>
    LowestHealthWithoutAura,

    /// <summary>带某光环且生命值最低。get_lowest_health_unit_with_aura</summary>
    LowestHealthWithAura,

    /// <summary>某光环值等于指定值且生命值最低。get_lowest_health_unit_with_aura_count</summary>
    LowestHealthWithAuraCount,

    /// <summary>按职责取首个/逆序首个。get_unit_with_role</summary>
    UnitWithRole,

    /// <summary>按职责且不带某光环取首个/逆序首个。get_unit_with_role_and_without_aura_name</summary>
    UnitWithRoleWithoutAura,

    /// <summary>带某光环(取持续最久)。get_unit_with_aura</summary>
    UnitWithAura,

    /// <summary>带某光环(取持续最短)。</summary>
    UnitWithAuraShortest,

    /// <summary>带某驱散类型的首个单位。get_unit_with_dispel_type</summary>
    UnitWithDispelType,

    /// <summary>治疗吸收高于阈值且治疗吸收最高的单位。</summary>
    HighestHealingAbsorb,

    /// <summary>拥有任一光环、治疗吸收高于阈值且治疗吸收最高的单位。</summary>
    HighestHealingAbsorbWithAnyAura,

    /// <summary>不拥有所选任一光环、治疗吸收高于阈值且治疗吸收最高的单位。</summary>
    HighestHealingAbsorbWithoutAnyAura,

    /// <summary>不带某光环、治疗吸收高于阈值且治疗吸收最高的单位。</summary>
    HighestHealingAbsorbWithoutAura,

    /// <summary>带某光环、治疗吸收高于阈值且治疗吸收最高的单位。</summary>
    HighestHealingAbsorbWithAura,

    /// <summary>某光环值等于指定值、治疗吸收高于阈值且治疗吸收最高的单位。</summary>
    HighestHealingAbsorbWithAuraCount
}

/// <summary>最低生命值选择器使用的职责筛选方式。</summary>
public enum UnitRoleFilterKind
{
    /// <summary>只包含指定职责。</summary>
    Include,

    /// <summary>排除指定职责。</summary>
    Exclude
}

/// <summary>
/// 模块内定义的命名动态单位。运行时由 <see cref="UnitSelector"/> 解析为 group 槽位("1".."30")。
/// 单光环类用 AuraSpellIds[0]; WithAnyAura 用整个列表。
/// </summary>
public sealed class ModuleUnit
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 可选的"生命值名": 非空时把该单位解析出槽位的 生命值 暴露成一个同名数值条件字段,
    /// 例如取名 最低血量 后条件里可直接写 最低血量 &lt; 50, 等价于 单位名.生命值 &lt; 50。
    /// </summary>
    public string? HealthName { get; set; }

    public UnitSelectorKind Kind { get; set; } = UnitSelectorKind.LowestHealth;
    public int? HealthThreshold { get; set; }
    public string? HealthThresholdField { get; set; }
    public UnitRoleFilterKind? RoleFilter { get; set; }
    public int? Role { get; set; }
    public bool Reverse { get; set; }
    public List<long>? AuraSpellIds { get; set; }
    // 仅用于读取并迁移旧模块；当前版本保存前必须转换并清空。
    public List<string>? AuraNames { get; set; }
    public int? AuraCount { get; set; }
    public int? DispelType { get; set; }

    public ModuleUnit Clone()
    {
        return new ModuleUnit
        {
            Name = Name,
            HealthName = HealthName,
            Kind = Kind,
            HealthThreshold = HealthThreshold,
            HealthThresholdField = HealthThresholdField,
            RoleFilter = RoleFilter,
            Role = Role,
            Reverse = Reverse,
            AuraSpellIds = AuraSpellIds is null ? null : new List<long>(AuraSpellIds),
            AuraNames = AuraNames is null ? null : new List<string>(AuraNames),
            AuraCount = AuraCount,
            DispelType = DispelType
        };
    }
}

/// <summary>
/// 数量统计类型, 对应 utils.py 中返回整数的统计函数。
/// </summary>
public enum CountKind
{
    /// <summary>生命值低于阈值的人数。count_units_below_health</summary>
    UnitsBelowHealth,

    /// <summary>不带某光环且生命值低于阈值的人数。count_units_without_aura_below_health</summary>
    UnitsWithoutAuraBelowHealth,

    /// <summary>拥有某光环的人数。count_units_with_aura</summary>
    UnitsWithAura,

    /// <summary>拥有某光环且生命值低于阈值的人数。</summary>
    UnitsWithAuraBelowHealth,

    /// <summary>治疗吸收大于阈值的人数。</summary>
    UnitsAboveHealingAbsorb,

    /// <summary>不带某光环且治疗吸收大于阈值的人数。</summary>
    UnitsWithoutAuraAboveHealingAbsorb,

    /// <summary>拥有某光环且治疗吸收大于阈值的人数。</summary>
    UnitsWithAuraAboveHealingAbsorb
}

/// <summary>敌人数量的阈值筛选方式(生命值 / 距离共用)。</summary>
public enum EnemyThresholdFilterKind
{
    /// <summary>不筛选。</summary>
    None,

    /// <summary>大于阈值。</summary>
    Above,

    /// <summary>小于阈值。</summary>
    Below
}

/// <summary>敌人数量的光环筛选方式。</summary>
public enum EnemyAuraFilterKind
{
    /// <summary>不筛选。</summary>
    None,

    /// <summary>带指定光环。</summary>
    WithAura,

    /// <summary>不带指定光环。</summary>
    WithoutAura,

    /// <summary>带所选任一光环。</summary>
    WithAnyAura,

    /// <summary>不带所选任何光环。</summary>
    WithoutAnyAura
}

/// <summary>
/// 模块内定义的命名敌人数量字段。统计姓名板(nameplates)中满足筛选条件的敌人数,
/// 仅用于条件(如 近身敌人数 &gt;= 3), 不能作为目标。
/// 三组筛选(生命值 / 光环 / 距离)彼此独立, 同时生效时是「且」关系;
/// 无论如何都只统计生命值 &gt; 0 的敌人。
/// </summary>
public sealed class ModuleEnemyCountField
{
    public string Name { get; set; } = string.Empty;

    public EnemyThresholdFilterKind HealthFilter { get; set; } = EnemyThresholdFilterKind.None;
    public int? HealthThreshold { get; set; }
    public string? HealthThresholdField { get; set; }

    public EnemyAuraFilterKind AuraFilter { get; set; } = EnemyAuraFilterKind.None;
    /// <summary>单光环筛选取 [0]; 任一/无任一筛选取整个列表。</summary>
    public List<long>? AuraSpellIds { get; set; }

    public EnemyThresholdFilterKind RangeFilter { get; set; } = EnemyThresholdFilterKind.None;
    public int? RangeThreshold { get; set; }
    public string? RangeThresholdField { get; set; }

    public ModuleEnemyCountField Clone()
    {
        return new ModuleEnemyCountField
        {
            Name = Name,
            HealthFilter = HealthFilter,
            HealthThreshold = HealthThreshold,
            HealthThresholdField = HealthThresholdField,
            AuraFilter = AuraFilter,
            AuraSpellIds = AuraSpellIds is null ? null : new List<long>(AuraSpellIds),
            RangeFilter = RangeFilter,
            RangeThreshold = RangeThreshold,
            RangeThresholdField = RangeThresholdField
        };
    }
}

/// <summary>
/// 模块内定义的命名数量字段。仅用于条件(如 低血量人数 &gt;= 3), 不能作为目标。
/// </summary>
public sealed class ModuleCountField
{
    public string Name { get; set; } = string.Empty;
    public CountKind Kind { get; set; } = CountKind.UnitsBelowHealth;
    public int? HealthThreshold { get; set; }
    public string? HealthThresholdField { get; set; }
    public long? AuraSpellId { get; set; }
    // 仅用于读取并迁移旧模块；当前版本保存前必须转换并清空。
    public string? AuraName { get; set; }

    public ModuleCountField Clone()
    {
        return new ModuleCountField
        {
            Name = Name,
            Kind = Kind,
            HealthThreshold = HealthThreshold,
            HealthThresholdField = HealthThresholdField,
            AuraSpellId = AuraSpellId,
            AuraName = AuraName
        };
    }
}
