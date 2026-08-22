namespace Shigure;

/// <summary>随模块分发的职业配置与宏快照。</summary>
public sealed class ModuleDependencySnapshot
{
    public const int CurrentSchemaVersion = 1;

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

    public ModuleConfigSnapshot Clone() => new()
    {
        Spec = Spec?.Clone() ?? new ModuleSpecSnapshot(),
        SpellsList = (SpellsList ?? []).Where(entry => entry is not null).Select(entry => entry.Clone()).ToList()
    };
}

public sealed class ModuleSpecSnapshot
{
    public bool NestedStates { get; set; } = true;
    public List<string> FlatStates { get; set; } = new();
    public Dictionary<string, List<string>> CategorizedStates { get; set; } = new(StringComparer.Ordinal);
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

public sealed class ModuleAuraSnapshot
{
    public string Name { get; set; } = string.Empty;
    public long? SpellId { get; set; }
    public List<long> SpellIds { get; set; } = new();
    public int? MaxApps { get; set; }
    public string? Filter { get; set; }

    public ModuleAuraSnapshot Clone() => new()
    {
        Name = Name,
        SpellId = SpellId,
        SpellIds = new List<long>(SpellIds ?? []),
        MaxApps = MaxApps,
        Filter = Filter
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

public sealed class ModuleGroupSnapshot
{
    public int Num { get; set; } = 5;
    public int? HealthPercent { get; set; }
    public int? Role { get; set; }
    public int? Dispel { get; set; }
    public List<ModuleGroupAuraSnapshot> Auras { get; set; } = new();

    public ModuleGroupSnapshot Clone() => new()
    {
        Num = Num,
        HealthPercent = HealthPercent,
        Role = Role,
        Dispel = Dispel,
        Auras = (Auras ?? []).Where(entry => entry is not null).Select(entry => entry.Clone()).ToList()
    };
}

public sealed class ModuleGroupAuraSnapshot
{
    public int Offset { get; set; }
    public string Name { get; set; } = string.Empty;
    public long? SpellId { get; set; }
    public List<long> SpellIds { get; set; } = new();

    public ModuleGroupAuraSnapshot Clone() => new()
    {
        Offset = Offset,
        Name = Name,
        SpellId = SpellId,
        SpellIds = new List<long>(SpellIds ?? [])
    };
}

public sealed class ModuleMacrosSnapshot
{
    public bool UsesSpecDynamicSpells { get; set; }
    public List<string> DynamicCommon { get; set; } = new();
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
    public string? Comment { get; set; }

    public ModuleMacroEntrySnapshot Clone() => (ModuleMacroEntrySnapshot)MemberwiseClone();
}
