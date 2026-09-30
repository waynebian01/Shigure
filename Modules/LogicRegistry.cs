namespace Shigure;

public interface IClassLogic
{
    LogicDecision Run(GameState state, string? specName);
}

public sealed class LogicRegistry : IRuntimeLogic
{
    private readonly Dictionary<int, IClassLogic> _logicByClass;
    private readonly IClassLogic _defaultLogic;
    private readonly IKeymapResolver _keymap;
    private readonly ModuleStore _moduleStore;
    private readonly string? _selectedModuleId;
    private readonly IReadOnlyList<DefaultModuleSelection> _defaultModules;
    private readonly ModuleActionCounterTracker _actionCounters = new();

    public LogicRegistry(
        IKeymapResolver keymap,
        ModuleStore moduleStore,
        string? selectedModuleId,
        IEnumerable<KeyValuePair<int, IClassLogic>>? classLogics = null,
        IReadOnlyList<DefaultModuleSelection>? defaultModules = null)
    {
        _keymap = keymap;
        _moduleStore = moduleStore;
        _selectedModuleId = string.IsNullOrWhiteSpace(selectedModuleId) ? null : selectedModuleId.Trim();
        _defaultModules = defaultModules ?? [];
        _defaultLogic = new DefaultClassLogic(keymap);
        _logicByClass = classLogics?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? new();
    }

    public LogicEvaluation Evaluate(
        int? classId,
        int? specId,
        string? specName,
        GameState state,
        bool runLogic)
    {
        _keymap.SelectForClass(classId, specId);
        var module = FindModule(classId, specId, state);
        if (module is not null)
        {
            _actionCounters.Apply(module, state);
            ModuleLogic.ResolveDynamicFields(
                module,
                state,
                _keymap.GetCurrentSpellIndices(),
                _keymap.GetCurrentItemIndices());
            return new LogicEvaluation(
                module.Name,
                runLogic ? ModuleLogic.Run(module, state, _keymap) : []);
        }

        _actionCounters.Reset();

        if (!runLogic)
        {
            return new LogicEvaluation(null, []);
        }

        if (classId is not null && _logicByClass.TryGetValue(classId.Value, out var logic))
        {
            return new LogicEvaluation(null, [logic.Run(state, specName)]);
        }

        return new LogicEvaluation(null, [_defaultLogic.Run(state, specName)]);
    }

    private ModuleDefinition? FindModule(int? classId, int? specId, GameState state)
    {
        var partyType = state.GetInt("队伍类型");
        var heroTalent = state.GetInt("英雄天赋");
        var defaultModuleId = _defaultModules
            .Select((selection, index) => (Selection: selection, Index: index))
            .Where(item => item.Selection.Matches(classId, specId, partyType, heroTalent))
            .OrderByDescending(item => item.Selection.Specificity)
            .ThenByDescending(item => item.Index)
            .Select(item => item.Selection.ModuleId)
            .FirstOrDefault(moduleId => !string.IsNullOrWhiteSpace(moduleId));

        return _moduleStore.FindSelectedOrBestMatch(
            _selectedModuleId ?? defaultModuleId,
            classId,
            specId,
            partyType,
            heroTalent);
    }
}

public sealed class DefaultClassLogic : IClassLogic
{
    private readonly IKeymapResolver _keymap;

    public DefaultClassLogic(IKeymapResolver keymap)
    {
        _keymap = keymap;
    }

    public LogicDecision Run(GameState state, string? specName)
    {
        var oneKeyAssist = state.GetInt("一键辅助");
        if (oneKeyAssist == 10)
        {
            var hotkey = _keymap.GetHotkey(0, "一键辅助");
            if (!string.IsNullOrWhiteSpace(hotkey))
            {
                return new LogicDecision(hotkey, "施放 一键辅助", EmptyInfo);
            }
        }

        return new LogicDecision(null, "C# 职业逻辑尚未迁移", EmptyInfo);
    }

    private static readonly IReadOnlyDictionary<string, object?> EmptyInfo = new Dictionary<string, object?>();
}
