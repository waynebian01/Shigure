namespace Shigure;

/// <summary>随模块分发的职业配置与宏快照。</summary>
public sealed class ModuleDependencySnapshot
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public int ClassId { get; set; }
    public int SpecId { get; set; }
    public ModuleConfigSnapshot Config { get; set; } = new();
    public ModuleMacrosSnapshot Macros { get; set; } = new();

    public ModuleDependencySnapshot Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        ClassId = ClassId,
        SpecId = SpecId,
        Config = Config?.Clone() ?? new ModuleConfigSnapshot(),
        Macros = Macros?.Clone() ?? new ModuleMacrosSnapshot()
    };
}

public sealed class ModuleConfigSnapshot
{
    public ModuleSpecSnapshot Spec { get; set; } = new();
    public List<ModuleSpellListEntrySnapshot> SpellsList { get; set; } = new();
    public List<ModuleItemListEntrySnapshot> ItemsList { get; set; } = new();

    public ModuleConfigSnapshot Clone() => new()
    {
        Spec = Spec?.Clone() ?? new ModuleSpecSnapshot(),
        SpellsList = (SpellsList ?? []).Where(entry => entry is not null).Select(entry => entry.Clone()).ToList(),
        ItemsList = (ItemsList ?? []).Where(entry => entry is not null).Select(entry => entry.Clone()).ToList()
    };
}

public sealed class ModuleSpecSnapshot
{
    public bool NestedStates { get; set; } = true;
    public List<string> FlatStates { get; set; } = new();
    public Dictionary<string, List<string>> CategorizedStates { get; set; } = new(StringComparer.Ordinal);
    public List<ModuleItemSnapshot> Items { get; set; } = new();
    public List<ModuleAuraSnapshot> PlayerAuras { get; set; } = new();
    public List<ModuleAuraSnapshot> TargetHarmfulAuras { get; set; } = new();
    public List<ModuleAuraSnapshot> TargetHelpfulAuras { get; set; } = new();
    public List<ModuleAuraSnapshot> FocusHarmfulAuras { get; set; } = new();
    public List<ModuleAuraSnapshot> FocusHelpfulAuras { get; set; } = new();
    public List<ModuleSpellSnapshot> Spells { get; set; } = new();
    public ModuleGroupSnapshot? Group { get; set; }

    public ModuleSpecSnapshot Clone() => new()
    {
        NestedStates = NestedStates,
        FlatStates = new List<string>(FlatStates ?? []),
        CategorizedStates = (CategorizedStates ?? new Dictionary<string, List<string>>()).ToDictionary(
            pair => pair.Key,
            pair => new List<string>(pair.Value ?? []),
            StringComparer.Ordinal),
        Items = (Items ?? []).Where(entry => entry is not null).Select(entry => entry.Clone()).ToList(),
        PlayerAuras = CloneEntries(PlayerAuras),
        TargetHarmfulAuras = CloneEntries(TargetHarmfulAuras),
        TargetHelpfulAuras = CloneEntries(TargetHelpfulAuras),
        FocusHarmfulAuras = CloneEntries(FocusHarmfulAuras),
        FocusHelpfulAuras = CloneEntries(FocusHelpfulAuras),
        Spells = (Spells ?? []).Where(entry => entry is not null).Select(entry => entry.Clone()).ToList(),
        Group = Group?.Clone()
    };

    private static List<ModuleAuraSnapshot> CloneEntries(IEnumerable<ModuleAuraSnapshot>? entries)
        => (entries ?? []).Where(entry => entry is not null).Select(entry => entry.Clone()).ToList();
}

public sealed class ModuleItemSnapshot
{
    public long ItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsEquipped { get; set; }

    public ModuleItemSnapshot Clone() => (ModuleItemSnapshot)MemberwiseClone();
}

public sealed class ModuleAuraSnapshot
{
    public string Name { get; set; } = string.Empty;
    public long? SpellId { get; set; }
    public List<long> SpellIds { get; set; } = new();
    public int? MaxApps { get; set; }

    public ModuleAuraSnapshot Clone() => new()
    {
        Name = Name,
        SpellId = SpellId,
        SpellIds = new List<long>(SpellIds ?? []),
        MaxApps = MaxApps
    };
}

public sealed class ModuleSpellSnapshot
{
    public string Name { get; set; } = string.Empty;
    public long SpellId { get; set; }
    public bool Charge { get; set; }
    public int? MaxCharge { get; set; }
    public int? CastCount { get; set; }
    public bool ForcedKnown { get; set; }
    public bool InSpellBook { get; set; }

    public ModuleSpellSnapshot Clone() => (ModuleSpellSnapshot)MemberwiseClone();
}

public sealed class ModuleSpellListEntrySnapshot
{
    public long SpellId { get; set; }
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;

    public ModuleSpellListEntrySnapshot Clone() => (ModuleSpellListEntrySnapshot)MemberwiseClone();
}

public sealed class ModuleItemListEntrySnapshot
{
    public long ItemId { get; set; }
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;

    public ModuleItemListEntrySnapshot Clone() => (ModuleItemListEntrySnapshot)MemberwiseClone();
}

public sealed class ModuleGroupSnapshot
{
    public List<string>? State { get; set; }
    // 兼容旧模块快照；新快照通过 State 保存字段及其顺序。
    public int? HealthPercent { get; set; }
    public int? Role { get; set; }
    public int? Dispel { get; set; }
    public List<ModuleGroupAuraSnapshot> Auras { get; set; } = new();

    public ModuleGroupSnapshot Clone() => new()
    {
        State = State is null ? null : new List<string>(State),
        HealthPercent = HealthPercent,
        Role = Role,
        Dispel = Dispel,
        Auras = (Auras ?? []).Where(entry => entry is not null).Select(entry => entry.Clone()).ToList()
    };
}

public sealed class ModuleGroupAuraSnapshot
{
    public string Name { get; set; } = string.Empty;
    public long? SpellId { get; set; }
    public List<long> SpellIds { get; set; } = new();

    public ModuleGroupAuraSnapshot Clone() => new()
    {
        Name = Name,
        SpellId = SpellId,
        SpellIds = new List<long>(SpellIds ?? [])
    };
}

public sealed class ModuleMacrosSnapshot
{
    // 兼容历史模块文件；新格式只使用 DynamicCommon 作为职业级动态宏列表。
    public bool UsesSpecDynamicSpells { get; set; }
    public List<string> DynamicCommon { get; set; } = new();
    // 旧版专精动态宏，仅在导入牧师/圣骑士模块时迁移一次。
    public List<string> DynamicForSpec { get; set; } = new();
    public List<ModuleMacroEntrySnapshot> StaticSpells { get; set; } = new();
    public List<ModuleMacroEntrySnapshot> SpecialSpells { get; set; } = new();

    public ModuleMacrosSnapshot Clone() => new()
    {
        UsesSpecDynamicSpells = UsesSpecDynamicSpells,
        DynamicCommon = new List<string>(DynamicCommon ?? []),
        DynamicForSpec = new List<string>(DynamicForSpec ?? []),
        StaticSpells = (StaticSpells ?? []).Where(entry => entry is not null).Select(entry => entry.Clone()).ToList(),
        SpecialSpells = (SpecialSpells ?? []).Where(entry => entry is not null).Select(entry => entry.Clone()).ToList()
    };
}

public sealed class ModuleMacroEntrySnapshot
{
    public string Text { get; set; } = string.Empty;

    // 对特殊宏表示必须手工维护的技能名，并以 Lua 行尾注释持久化。
    public string? Comment { get; set; }

    public ModuleMacroEntrySnapshot Clone() => (ModuleMacroEntrySnapshot)MemberwiseClone();
}
