using System.Text.RegularExpressions;

namespace Shigure;

internal enum ModuleComputationStage
{
    Adjustment,
    DynamicValue,
    Formula,
    Custom,
    PostAdjustment,
    Logic
}

// 编辑器和运行时共用阶段依赖检查，不能用尚未生成的字段默认值提前命中条件。
internal sealed class ModuleComputationStages
{
    private readonly Dictionary<string, ModuleComputationStage> _fields = new(StringComparer.Ordinal);
    private readonly HashSet<string> _units = new(StringComparer.Ordinal);

    internal ModuleComputationStages(ModuleDefinition module)
    {
        foreach (var unit in module.Units)
        {
            Add(unit.Name, ModuleComputationStage.DynamicValue);
            Add(unit.ValueName, ModuleComputationStage.DynamicValue);
            _units.Add(unit.Name);
        }
        foreach (var name in module.Counts.Select(value => value.Name)
                     .Concat(module.EnemyCounts.Select(value => value.Name))
                     .Concat(module.AverageHealthFields.Select(value => value.Name)))
            Add(name, ModuleComputationStage.DynamicValue);
        foreach (var value in module.ValueAdjustments)
        {
            if (!string.IsNullOrWhiteSpace(value.Formula)) Add(value.Field, ModuleComputationStage.Formula);
            else if (value.IsCustom) Add(value.Field, ModuleComputationStage.Custom);
        }
    }

    private void Add(string? name, ModuleComputationStage stage)
    {
        if (!string.IsNullOrWhiteSpace(name)) _fields.TryAdd(name.Trim(), stage);
    }

    internal bool IsAvailable(string field, ModuleComputationStage stage)
    {
        var name = field.Trim();
        if (name.StartsWith("state.", StringComparison.OrdinalIgnoreCase)) name = name[6..];
        if (_fields.TryGetValue(name, out var source)) return source <= stage;
        var dot = name.IndexOf('.');
        return dot < 0 || !_units.Contains(name[..dot]) || ModuleComputationStage.DynamicValue <= stage;
    }

    internal string? ExpressionError(string expression, ModuleComputationStage stage, bool formula = false)
    {
        if (formula) expression = FormulaEvaluator.NormalizeExpression(expression);
        // 引号内是字面量，命名列表是静态数据，不属于跨阶段字段引用。
        expression = Regex.Replace(expression, "\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'", " ");
        foreach (var (name, source) in _fields)
        {
            if (source <= stage) continue;
            if (Regex.IsMatch(expression,
                    $@"(?<![_$\p{{L}}\p{{N}}.])(?i:state\.)?{Regex.Escape(name)}(?![_$\p{{L}}\p{{N}}])"))
                return $"字段“{name}”在后续阶段计算，当前阶段不可引用。";
        }
        return null;
    }

    internal bool IsCustomField(string field)
    {
        var name = field.Trim();
        if (name.StartsWith("state.", StringComparison.OrdinalIgnoreCase)) name = name[6..];
        return _fields.TryGetValue(name, out var source) && source == ModuleComputationStage.Custom;
    }

    internal string? AdjustmentError(ModuleValueAdjustment value)
    {
        var stage = !string.IsNullOrWhiteSpace(value.Formula) ? ModuleComputationStage.Formula
            : value.IsCustom ? ModuleComputationStage.Custom
            : value.IsPostAdjustment ? ModuleComputationStage.PostAdjustment : ModuleComputationStage.Adjustment;
        if (stage == ModuleComputationStage.Adjustment)
        {
            if (value.TeammateTarget?.Kind == ModuleTeammateTargetKind.DynamicUnit)
                return "动态单位在调整之后计算，不能作为调整目标。";
            if (value.TeammateTarget is null && !IsAvailable(value.Field, stage))
                return $"字段“{value.Field}”在调整之后计算，不能作为调整目标。";
        }
        if (stage == ModuleComputationStage.PostAdjustment && value.TeammateTarget is null)
        {
            var name = value.Field.Trim();
            if (name.StartsWith("state.", StringComparison.OrdinalIgnoreCase)) name = name[6..];
            if (_units.Contains(name)) return "动态单位名用于存在判断，请选择动态单位的值名称或队友字段。";
            if (IsCustomField(name)
                && (value.EffectiveOperation != "=" || !value.TryGetOperand(out var operand) || operand is not bool))
                return "自定义字段只能使用“=”赋布尔值。";
            if (_fields.TryGetValue(name, out var source) && source is ModuleComputationStage.DynamicValue or ModuleComputationStage.Formula
                && (!value.TryGetOperand(out var number) || number is not int))
                return "动态数值和公式结果只能调整为整数。";
        }
        return ExpressionError(value.Condition, stage)
            ?? ExpressionError(value.Formula, stage, formula: true);
    }

    internal string? FilterError(IEnumerable<ModuleCountConditionGroup> groups)
    {
        foreach (var value in groups.SelectMany(group => group.Conditions)
                     .Where(value => value.Enabled && value.ValueKind == CountConditionValueKind.StateField))
            if (!IsAvailable(value.ValueField ?? string.Empty, ModuleComputationStage.DynamicValue))
                return $"字段“{value.ValueField}”在动态数值之后计算，不能作为筛选阈值。";
        return null;
    }
}
