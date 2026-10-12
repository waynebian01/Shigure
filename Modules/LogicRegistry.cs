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
    private readonly IRuntimeSoundInput? _soundInput;
    private readonly Dictionary<string, long> _sentKeyRules = new(StringComparer.Ordinal);
    private string? _triggerModuleId;
    private SoundRecognitionEvent? _pendingSound;
    private readonly RuleMatchTracker _ruleMatches;

    public LogicRegistry(
        IKeymapResolver keymap,
        ModuleStore moduleStore,
        string? selectedModuleId,
        IEnumerable<KeyValuePair<int, IClassLogic>>? classLogics = null,
        IReadOnlyList<DefaultModuleSelection>? defaultModules = null,
        TimeProvider? timeProvider = null)
    {
        _keymap = keymap;
        _moduleStore = moduleStore;
        _selectedModuleId = string.IsNullOrWhiteSpace(selectedModuleId) ? null : selectedModuleId.Trim();
        _defaultModules = defaultModules ?? [];
        _defaultLogic = new DefaultClassLogic(keymap);
        _logicByClass = classLogics?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? new();
        _ruleMatches = new RuleMatchTracker(timeProvider ?? TimeProvider.System);
    }

    internal LogicRegistry(IKeymapResolver keymap, ModuleStore moduleStore, string? selectedModuleId,
        IReadOnlyList<DefaultModuleSelection>? defaultModules, IRuntimeSoundInput soundInput,
        TimeProvider? timeProvider = null)
        : this(keymap, moduleStore, selectedModuleId, defaultModules: defaultModules, timeProvider: timeProvider)
    {
        _soundInput = soundInput;
    }

    public void DecisionSent(LogicDecision decision)
    {
        if (decision.SendOnce && decision.RateLimitKey is { } key)
            _sentKeyRules[key] = decision.SoundEventId;
        if (decision.SoundEventId > 0 && _pendingSound?.Id == decision.SoundEventId)
            _pendingSound = null;
    }

    public void ResetTriggers()
    {
        _sentKeyRules.Clear();
        _pendingSound = null;
        _ruleMatches.Clear();
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
        if (_triggerModuleId != module?.Id || !runLogic) ResetTriggers();
        _triggerModuleId = module?.Id;
        _soundInput?.Configure(module is null ? [] : SoundConditions.GetNames(module.Rules), runLogic);
        if (_pendingSound is not null
            && DateTimeOffset.UtcNow - _pendingSound.DetectedAt >= TimeSpan.FromSeconds(1))
            _pendingSound = null;
        _pendingSound ??= runLogic ? _soundInput?.ReadEvent() : null;
        var sound = _pendingSound;
        state.Values[ShigureConditionFields.SoundRecognition] = sound?.Name ?? string.Empty;
        state.Values["$soundEventId"] = sound?.Id ?? 0L;
        if (_soundInput?.FailureReason is { } soundFailure)
            state.Values["声音识别错误"] = soundFailure;
        else state.Values.Remove("声音识别错误");
        if (module is not null)
        {
            ModuleLogic.ResolveDynamicFields(
                module,
                state,
                _keymap.GetCurrentSpellIndices(),
                _keymap.GetCurrentItemIndices());
            var ruleMatches = new Dictionary<int, bool>();
            var ruleSoundEvents = new Dictionary<int, long>();
            // 所有带命中延迟的规则独立更新，前面的规则不能挡住取消命中的计时。
            for (var index = 0; index < module.Rules.Count; index++)
            {
                var key = $"{module.Id}:{index}";
                var rule = module.Rules[index];
                if (!rule.Enabled)
                {
                    _ruleMatches.Forget(key);
                    _sentKeyRules.Remove(key);
                    continue;
                }
                if (!runLogic || rule.MatchDelayMs is not > 0 && !_sentKeyRules.ContainsKey(key)) continue;
                if (!ModuleConditionEvaluator.TryEvaluateRule(rule, state,
                        out var matched, out _, _keymap.GetCurrentFailedSpells(),
                        _keymap.GetCurrentSpellIndices(), _keymap.GetCurrentItemIndices(),
                        _keymap.GetCurrentInsertItems()))
                {
                    _ruleMatches.Forget(key);
                    _sentKeyRules.Remove(key);
                    continue;
                }
                var eventId = SoundConditions.UsesSound(rule) ? sound?.Id ?? 0L : 0L;
                var tracked = _ruleMatches.Apply(key, rule.MatchDelayMs, matched, eventId);
                ruleMatches[index] = tracked.Matched;
                ruleSoundEvents[index] = tracked.SoundEventId;
                if (!tracked.Matched) _sentKeyRules.Remove(key);
            }
            return new LogicEvaluation(
                module.Name,
                runLogic ? ModuleLogic.Run(module, state, _keymap, _sentKeyRules, ruleMatches, ruleSoundEvents) : []);
        }

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
