namespace Shigure;

public sealed class ModuleSubLogic
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public List<ModuleRule> Rules { get; set; } = [];

    public ModuleSubLogic Clone() => new()
    {
        Id = Id, Name = Name, Rules = Rules.Select(rule => rule.Clone()).ToList()
    };
}

// 统一规则坐标，主逻辑保留旧限流键和索引，分支使用稳定 ID 隔离计时状态。
internal readonly record struct ModuleRuleEntry(ModuleRule Rule, string LogicId, string LogicName,
    int LocalIndex, int Index, string Key);

internal static class ModuleApl
{
    public const string CallAction = "调用APL";
    public const string RunAction = "运行APL";
    public static bool IsCall(ModuleRule rule) => rule.Spell == CallAction;
    public static bool IsRun(ModuleRule rule) => rule.Spell == RunAction;
    public static bool IsInvocation(ModuleRule rule) => IsCall(rule) || IsRun(rule);

    public static string? ValidationError(ModuleDefinition module)
    {
        var names = new HashSet<string>(StringComparer.Ordinal) { "主逻辑" };
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var logic in module.SubLogics)
            if (string.IsNullOrWhiteSpace(logic.Name) || !names.Add(logic.Name.Trim())
                || string.IsNullOrWhiteSpace(logic.Id) || !ids.Add(logic.Id))
                return $"次逻辑“{logic.Name}”的名称或 ID 为空、重复或使用了保留名称。";
        foreach (var entry in Entries(module).Where(entry => entry.Rule.Enabled && IsInvocation(entry.Rule)))
            if (CallError(module, entry.LogicId, entry.Rule.CallTargetId) is { } error)
                return $"{entry.LogicName}第 {entry.LocalIndex + 1} 行：{error}。";
        return null;
    }

    public static IEnumerable<ModuleRuleEntry> Entries(ModuleDefinition module)
    {
        var index = 0;
        for (var i = 0; i < module.Rules.Count; i++)
            yield return new(module.Rules[i], "", "主逻辑", i, index++, $"{module.Id}:{i}");
        foreach (var logic in module.SubLogics)
            for (var i = 0; i < logic.Rules.Count; i++)
                yield return new(logic.Rules[i], logic.Id, logic.Name, i, index++, $"{module.Id}:apl:{logic.Id}:{i}");
    }

    public static string? CallError(ModuleDefinition module, string sourceId, string? targetId)
    {
        if (string.IsNullOrEmpty(targetId) || !module.SubLogics.Any(logic => logic.Id == targetId))
            return "目标已删除或不存在";
        if (sourceId.Length == 0) return null;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(targetId);
        while (pending.TryPop(out var id))
        {
            if (id == sourceId) return "不允许自调或循环调用";
            if (!visited.Add(id)) continue;
            var logic = module.SubLogics.FirstOrDefault(item => item.Id == id);
            if (logic is null) continue;
            foreach (var rule in logic.Rules.Where(IsInvocation))
                if (!string.IsNullOrEmpty(rule.CallTargetId)) pending.Push(rule.CallTargetId);
        }
        return null;
    }

    public static void ClearCallSettings(ModuleRule rule)
    {
        rule.Unit = null;
        rule.UnitName = null;
        rule.MacroCondition = null;
        rule.Hotkey = string.Empty;
        rule.DelayMs = rule.LogicDelayMs = rule.HitDelayMs = rule.MatchDelayMs = null;
        rule.ContinueLogic = null;
    }

    // 只判断入口是否仍成立，不受优先级和单击批次过滤；用于取消已离开分支的等待和锁定。
    public static HashSet<string> Reachable(ModuleDefinition module, GameState state, IKeymapResolver keymap)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal) { "" };
        var pending = new Stack<(string Id, List<ModuleRule> Rules)>();
        pending.Push(("", module.Rules));
        while (pending.TryPop(out var current))
        {
            foreach (var rule in current.Rules.Where(rule => rule.Enabled && IsInvocation(rule)))
            {
                if (CallError(module, current.Id, rule.CallTargetId) is not null) continue;
                if (!ModuleConditionEvaluator.TryEvaluateRule(rule, state, out var matched, out _,
                        keymap.GetCurrentFailedSpells(), keymap.GetCurrentSpellIndices(),
                        keymap.GetCurrentItemIndices(), keymap.GetCurrentInsertItems()) || !matched) continue;
                var target = module.SubLogics.First(logic => logic.Id == rule.CallTargetId);
                if (reachable.Add(target.Id)) pending.Push((target.Id, target.Rules));
            }
        }
        return reachable;
    }
}
