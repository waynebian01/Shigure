namespace Shigure;

/// <summary>Remembers confirmed casts for JSON rule conditions.</summary>
internal sealed class ModuleActionCounterTracker
{
    private readonly Dictionary<string, CounterState> _counters = new(StringComparer.Ordinal);
    private string? _moduleId;
    private int _lastSerial;

    public void Reset()
    {
        _moduleId = null;
        _lastSerial = 0;
        _counters.Clear();
    }

    public void Apply(ModuleDefinition module, GameState state)
    {
        if (module.ActionCounters.Count == 0)
        {
            Reset();
            return;
        }

        var serial = state.GetInt("成功施法序号");
        if (_moduleId != module.Id || state.GetInt("战斗时间") <= 0 || serial == 0)
        {
            _moduleId = module.Id;
            _lastSerial = serial;
            _counters.Clear();
        }
        else
        {
            RefreshCoverage(module, state);
            if (serial != _lastSerial)
            {
                _lastSerial = serial;
                var spellId = state.GetInt("成功施法ID低位")
                    | (state.GetInt("成功施法ID中位") << 8)
                    | (state.GetInt("成功施法ID高位") << 16);
                ProcessCast(module, state, spellId);
            }
        }

        var values = state.Values.TryGetValue("$dynamicvalues", out var existing)
            && existing is IReadOnlyDictionary<string, object?> dictionary
            ? new Dictionary<string, object?>(dictionary, StringComparer.Ordinal)
            : new Dictionary<string, object?>(StringComparer.Ordinal);
        state.Values["$dynamicvalues"] = values;
        foreach (var definition in module.ActionCounters)
        {
            values[definition.Name] = Counter(definition.Name).Value;
        }
    }

    private void ProcessCast(ModuleDefinition module, GameState state, int spellId)
    {
        if (spellId <= 0)
        {
            return;
        }

        var ids = (module.Dependencies?.Config.Spec.Spells ?? [])
            .Where(spell => !string.IsNullOrWhiteSpace(spell.Name))
            .GroupBy(spell => spell.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().SpellId, StringComparer.Ordinal);

        foreach (var definition in module.ActionCounters)
        {
            var counter = Counter(definition.Name);
            foreach (var spell in definition.SetAfterSpells)
            {
                if (ids.TryGetValue(spell, out var id) && id == spellId)
                {
                    counter.SeenSetSpells.Add(spell);
                }
            }

            if (definition.SetAfterSpells.Count > 0
                && definition.SetAfterSpells.All(counter.SeenSetSpells.Contains))
            {
                counter.Value = FormulaEvaluator.TryEvaluateInt(definition.SetFormula, state, out var value, out _)
                    ? Math.Max(0, value) : 0;
                if (UsesCoverage(definition))
                {
                    var targetCount = Math.Max(definition.BaseCoverage, state.GetInt(definition.CoverageField));
                    counter.CoveredCount = Math.Min(definition.BaseCoverage, targetCount);
                    counter.LastTargetCount = targetCount;
                }
                counter.SeenSetSpells.Clear();
            }

            if (definition.DecrementSpells.Any(spell => ids.TryGetValue(spell, out var id) && id == spellId))
            {
                if (UsesCoverage(definition))
                {
                    var targetCount = Math.Max(definition.BaseCoverage, state.GetInt(definition.CoverageField));
                    counter.CoveredCount = Math.Min(targetCount, counter.CoveredCount + definition.CoveragePerDecrement);
                    counter.Value = PendingCoverage(definition, counter, targetCount);
                }
                else
                {
                    counter.Value = Math.Max(0, counter.Value - 1);
                }
            }
        }
    }

    private void RefreshCoverage(ModuleDefinition module, GameState state)
    {
        foreach (var definition in module.ActionCounters.Where(UsesCoverage))
        {
            var counter = Counter(definition.Name);
            var targetCount = Math.Max(definition.BaseCoverage, state.GetInt(definition.CoverageField));
            if (!counter.CoverageInitialized)
            {
                counter.CoverageInitialized = true;
                counter.CoveredCount = Math.Min(definition.BaseCoverage, targetCount);
            }
            else if (targetCount < counter.LastTargetCount)
            {
                counter.CoveredCount = Math.Min(counter.CoveredCount, targetCount);
            }

            counter.LastTargetCount = targetCount;
            counter.Value = PendingCoverage(definition, counter, targetCount);
        }
    }

    private static bool UsesCoverage(ModuleActionCounter definition) =>
        !string.IsNullOrWhiteSpace(definition.CoverageField)
        && definition.BaseCoverage > 0
        && definition.CoveragePerDecrement > 0;

    private static int PendingCoverage(ModuleActionCounter definition, CounterState counter, int targetCount)
    {
        var uncovered = Math.Max(0, targetCount - counter.CoveredCount);
        return (uncovered + definition.CoveragePerDecrement - 1) / definition.CoveragePerDecrement;
    }

    private CounterState Counter(string name)
    {
        if (!_counters.TryGetValue(name, out var counter))
        {
            counter = new CounterState();
            _counters[name] = counter;
        }
        return counter;
    }

    private sealed class CounterState
    {
        public int Value;
        public HashSet<string> SeenSetSpells { get; } = new(StringComparer.Ordinal);
        public bool CoverageInitialized;
        public int CoveredCount;
        public int LastTargetCount;
    }
}
