namespace Shigure;

// 动态数值、公式、自定义共用依赖图；页签顺序不再限制它们之间的引用方向。
internal sealed class ModuleDerivedComputation
{
    private sealed class Node
    {
        internal required string[] Names { get; init; }
        internal ModuleUnit? Unit { get; init; }
        internal ModuleCountField? Count { get; init; }
        internal ModuleEnemyCountField? EnemyCount { get; init; }
        internal ModuleAverageHealthField? Average { get; init; }
        internal List<ModuleValueAdjustment> Values { get; } = [];
        internal HashSet<Node> Dependencies { get; } = [];
        internal string? Error { get; set; }
        internal bool Custom => Values.Count > 0 && Values[0].IsCustom && string.IsNullOrWhiteSpace(Values[0].Formula);
        internal IEnumerable<ModuleCountConditionGroup> Filters
            => Unit?.FilterGroups ?? Count?.FilterGroups ?? EnemyCount?.FilterGroups ?? Average?.FilterGroups ?? [];
    }

    private readonly List<Node> _nodes = [];
    private readonly List<Node> _ordered = [];
    private readonly Dictionary<string, Node> _producers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Node> _units = new(StringComparer.Ordinal);
    internal const string ErrorsKey = "$derivedFieldErrors";

    internal ModuleDerivedComputation(ModuleDefinition module)
    {
        foreach (var unit in module.Units)
        {
            var node = new Node { Names = Names(unit.Name, unit.ValueName), Unit = unit };
            Add(node);
            if (!string.IsNullOrWhiteSpace(unit.Name)) _units.TryAdd(unit.Name, node);
        }
        foreach (var count in module.Counts) Add(new() { Names = Names(count.Name), Count = count });
        foreach (var count in module.EnemyCounts) Add(new() { Names = Names(count.Name), EnemyCount = count });
        foreach (var average in module.AverageHealthFields) Add(new() { Names = Names(average.Name), Average = average });
        foreach (var group in module.ValueAdjustments
                     .Where(value => value.IsCustom || !string.IsNullOrWhiteSpace(value.Formula))
                     .GroupBy(value => value.Field, StringComparer.Ordinal))
        {
            var node = new Node { Names = Names(group.Key) };
            node.Values.AddRange(group);
            Add(node);
        }
        foreach (var node in _nodes)
        {
            foreach (var condition in node.Filters.SelectMany(group => group.Conditions)
                         .Where(value => value.Enabled && value.ValueKind == CountConditionValueKind.StateField))
                Depend(node, condition.ValueField ?? string.Empty);
            foreach (var value in node.Values.Where(value => value.Enabled))
            {
                foreach (var reference in ModuleConditionEvaluator.ReferencedFields(value.Condition)) Depend(node, reference);
                foreach (var reference in FormulaEvaluator.ReferencedFields(value.Formula)) Depend(node, reference);
            }
        }
        var visited = new HashSet<Node>();
        var path = new List<Node>();
        foreach (var node in _nodes) Visit(node, visited, path);
    }

    private static string[] Names(params string?[] names)
        => names.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!.Trim()).Distinct(StringComparer.Ordinal).ToArray();

    private void Add(Node node)
    {
        if (node.Names.Length == 0) return;
        _nodes.Add(node);
        foreach (var name in node.Names)
            if (!_producers.TryAdd(name, node))
                node.Error = _producers[name].Error = $"计算字段“{name}”名称重复。";
    }

    private Node? Producer(string reference)
    {
        var name = reference.Trim();
        if (name.StartsWith("state.", StringComparison.OrdinalIgnoreCase)) name = name[6..];
        if (_producers.TryGetValue(name, out var exact)) return exact;
        var dot = name.IndexOf('.');
        return dot > 0 && _units.TryGetValue(name[..dot], out var unit) ? unit : null;
    }

    private void Depend(Node node, string reference)
    {
        if (Producer(reference) is not { } dependency) return;
        // 同名自定义多行仍按原顺序覆盖，本字段的条件可以读取前行结果/初始 false。
        if (dependency == node && node.Custom) return;
        node.Dependencies.Add(dependency);
    }

    private void Visit(Node node, HashSet<Node> visited, List<Node> path)
    {
        if (visited.Contains(node)) return;
        var start = path.IndexOf(node);
        if (start >= 0)
        {
            var cycle = path.Skip(start).Append(node).Select(value => value.Names[0]);
            var error = $"循环引用：{string.Join(" → ", cycle)}";
            foreach (var value in path.Skip(start)) value.Error = error;
            return;
        }
        path.Add(node);
        foreach (var dependency in node.Dependencies)
        {
            Visit(dependency, visited, path);
            if (dependency.Error is not null) node.Error ??= $"依赖“{dependency.Names[0]}”不可用：{dependency.Error}";
        }
        path.RemoveAt(path.Count - 1);
        visited.Add(node);
        _ordered.Add(node);
    }

    internal string? ErrorFor(string reference) => Producer(reference)?.Error;

    internal Dictionary<string, string?> Resolve(GameState state,
        IReadOnlyDictionary<long, int>? spellIndices, IReadOnlyDictionary<long, int>? itemIndices)
    {
        var units = new Dictionary<string, string?>(StringComparer.Ordinal);
        var unitValues = new Dictionary<string, object?>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        state.Values["$units"] = units;
        state.Values["$unithealth"] = unitValues;
        state.Values["$counts"] = counts;
        state.Values[ErrorsKey] = errors;
        foreach (var node in _ordered)
        {
            var error = node.Error;
            foreach (var dependency in node.Dependencies)
                if (dependency != node && dependency.Names.Any(errors.ContainsKey))
                    error ??= $"依赖“{dependency.Names[0]}”本帧计算失败。";
            if (error is null) error = Evaluate(node, state, units, unitValues, counts, spellIndices, itemIndices);
            if (error is not null)
                foreach (var name in node.Names) errors[name] = error;
        }
        if (errors.Count == 0) state.Values.Remove("动态计算错误");
        else state.Values["动态计算错误"] = string.Join("；", errors.Select(pair => $"{pair.Key}：{pair.Value}"));
        return units;
    }

    private static string? Evaluate(Node node, GameState state, Dictionary<string, string?> units,
        Dictionary<string, object?> unitValues, Dictionary<string, int> counts,
        IReadOnlyDictionary<long, int>? spellIndices, IReadOnlyDictionary<long, int>? itemIndices)
    {
        if (node.Unit is { } unit)
        {
            var slot = UnitSelector.Resolve(unit, state);
            units[unit.Name] = slot;
            if (!string.IsNullOrWhiteSpace(unit.ValueName)) unitValues[unit.ValueName] = UnitSelector.ResolveValue(unit, slot, state);
        }
        else if (node.Count is { } count) counts[count.Name] = UnitSelector.Resolve(count, state);
        else if (node.EnemyCount is { } enemyCount) counts[enemyCount.Name] = UnitSelector.Resolve(enemyCount, state);
        else if (node.Average is { } average) counts[average.Name] = UnitSelector.Resolve(average, state);
        else
        {
            if (node.Custom) ModuleLogic.SetDynamicValue(state, node.Names[0], false);
            else if (!node.Values.Any(value => value.Enabled)) return "公式未启用。";
            foreach (var value in node.Values.Where(value => value.Enabled))
            {
                if (!ModuleConditionEvaluator.TryEvaluate(value.Condition, state, out var matched, out var conditionError,
                        spellIndices: spellIndices, itemIndices: itemIndices)) return conditionError ?? "条件计算失败。";
                if (!matched && !value.IsConditionBoolean) continue;
                if (node.Custom) ModuleLogic.SetDynamicValue(state, value.Field, matched);
                else
                {
                    if (!FormulaEvaluator.TryEvaluateInt(value.Formula, state, out var number, out var formulaError))
                        return formulaError ?? "公式计算失败。";
                    ModuleLogic.SetDynamicValue(state, value.Field, number);
                }
            }
        }
        return null;
    }

    internal static string? UnavailableReference(GameState state, string reference)
    {
        if (!state.Values.TryGetValue(ErrorsKey, out var raw) || raw is not IReadOnlyDictionary<string, string> errors) return null;
        var name = reference.Trim();
        if (name.StartsWith("state.", StringComparison.OrdinalIgnoreCase)) name = name[6..];
        if (errors.TryGetValue(name, out var error)) return error;
        var dot = name.IndexOf('.');
        return dot > 0 ? errors.GetValueOrDefault(name[..dot]) : null;
    }
}
