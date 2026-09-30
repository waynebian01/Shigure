using System.Text.Json;
using Shigure;

var directory = Path.Combine(Path.GetTempPath(), "shigure-action-counter-" + Guid.NewGuid());
Directory.CreateDirectory(directory);
try
{
    var module = ModuleDefinition.CreateDefault("施法计数测试");
    module.Match = new ModuleMatch { ClassId = 4, SpecId = 2 };
    module.ActionCounters.Add(new ModuleActionCounter
    {
        Name = "剩余次数",
        SetAfterSpells = ["冲动", "影舞"],
        SetFormula = "2",
        DecrementSpells = ["眉心"]
    });
    module.ActionCounters.Add(new ModuleActionCounter
    {
        Name = "铺血剩余",
        CoverageField = "敌人数",
        BaseCoverage = 1,
        CoveragePerDecrement = 1,
        DecrementSpells = ["眉心"]
    });
    module.Dependencies = new ModuleDependencySnapshot();
    module.Dependencies.Config.Spec.Spells =
    [
        new ModuleSpellSnapshot { Name = "冲动", SpellId = 13750 },
        new ModuleSpellSnapshot { Name = "影舞", SpellId = 185313 },
        new ModuleSpellSnapshot { Name = "眉心", SpellId = 315341 }
    ];
    File.WriteAllText(Path.Combine(directory, "module.json"), JsonSerializer.Serialize(module));

    var registry = new LogicRegistry(new EmptyKeymap(), new ModuleStore(directory), module.Id);
    Check(0, 0, 0, 0, 0);
    Check(1, 1, 13750, 0, 2);
    Check(1, 2, 185313, 2, 2);
    Check(1, 3, 315341, 1, 1);
    Check(1, 3, 315341, 1, 1); // 同一事件重复扫描
    Check(1, 4, 315341, 0, 0);
    Check(0, 4, 315341, 0, 0); // 脱战归零
    Console.WriteLine("ActionCounterSmoke: OK");

    void Check(int combatTime, int serial, int spellId, int expected, int coverageExpected)
    {
        var state = new GameState(new Dictionary<string, object?>
        {
            ["战斗时间"] = combatTime,
            ["敌人数"] = 3,
            ["成功施法序号"] = serial,
            ["成功施法ID低位"] = spellId & 255,
            ["成功施法ID中位"] = (spellId >> 8) & 255,
            ["成功施法ID高位"] = (spellId >> 16) & 255
        });
        registry.Evaluate(4, 2, null, state, runLogic: false);
        if (!ModuleConditionEvaluator.TryResolveInt(state, "剩余次数", out var actual) || actual != expected)
        {
            throw new Exception($"serial={serial}: expected {expected}, got {actual}");
        }
        if (!ModuleConditionEvaluator.TryResolveInt(state, "铺血剩余", out var coverage) || coverage != coverageExpected)
        {
            throw new Exception($"serial={serial}: expected coverage {coverageExpected}, got {coverage}");
        }
    }
}
finally
{
    Directory.Delete(directory, recursive: true);
}

internal sealed class EmptyKeymap : IKeymapResolver
{
    public void SelectForClass(int? classId, int? specId) { }
    public string? GetHotkey(int? unit, string spell, string? macroCondition = null) => null;
    public string? GetHotkey(int? unit, long spellId, string? macroCondition = null) => null;
    public IReadOnlyDictionary<int, long> GetCurrentFailedSpells() => new Dictionary<int, long>();
    public IReadOnlyDictionary<int, long> GetCurrentOneKeySpells() => new Dictionary<int, long>();
    public IReadOnlyDictionary<int, long> GetCurrentInsertItems() => new Dictionary<int, long>();
    public IReadOnlyDictionary<long, int> GetCurrentSpellIndices() => new Dictionary<long, int>();
    public IReadOnlyDictionary<long, string> GetCurrentSpellNames() => new Dictionary<long, string>();
    public IReadOnlyDictionary<long, int> GetCurrentItemIndices() => new Dictionary<long, int>();
    public IReadOnlyDictionary<long, string> GetCurrentItemNames() => new Dictionary<long, string>();
}
