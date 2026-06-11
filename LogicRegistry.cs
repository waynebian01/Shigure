namespace Shigure;

public sealed record LogicDecision(
    string? Hotkey,
    string Step,
    IReadOnlyDictionary<string, object?> UnitInfo,
    string? ModuleName = null);

public interface IClassLogic
{
    LogicDecision Run(GameState state, string? specName);
}

public sealed class LogicRegistry
{
    private readonly Dictionary<int, IClassLogic> _logicByClass = new();
    private readonly IClassLogic _defaultLogic;
    private readonly KeymapService _keymap;
    private readonly ModuleStore _moduleStore;

    public LogicRegistry(KeymapService keymap, ModuleStore moduleStore)
    {
        _keymap = keymap;
        _moduleStore = moduleStore;
        _defaultLogic = new DefaultClassLogic(keymap);
    }

    public LogicDecision Run(int? classId, int? specId, string? specName, GameState state)
    {
        var module = _moduleStore.FindBestMatch(
            classId,
            specId,
            state.GetInt("队伍类型"),
            state.GetInt("英雄天赋"));
        if (module is not null)
        {
            return ModuleLogic.Run(module, state, _keymap);
        }

        if (classId is not null && _logicByClass.TryGetValue(classId.Value, out var logic))
        {
            return logic.Run(state, specName);
        }

        return _defaultLogic.Run(state, specName);
    }
}

public sealed class DefaultClassLogic : IClassLogic
{
    private readonly KeymapService _keymap;

    public DefaultClassLogic(KeymapService keymap)
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
