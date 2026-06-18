using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Shigure;

public sealed class ModuleDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = "新模块";
    public string Author { get; set; } = string.Empty;
    // 保存时写入当时的 Shigure 版本(AppInfo.Version)。
    public string Version { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public ModuleMatch Match { get; set; } = new();
    public List<ModuleUnit> Units { get; set; } = new();
    public List<ModuleCountField> Counts { get; set; } = new();
    public List<ModuleValueAdjustment> ValueAdjustments { get; set; } = new();
    public List<ModuleRule> Rules { get; set; } = new();

    [JsonIgnore]
    public string? FilePath { get; set; }

    public ModuleDefinition Clone()
    {
        return new ModuleDefinition
        {
            Id = Id,
            Name = Name,
            Author = Author,
            Version = Version,
            Enabled = Enabled,
            FilePath = FilePath,
            Match = Match.Clone(),
            Units = Units.Select(unit => unit.Clone()).ToList(),
            Counts = Counts.Select(count => count.Clone()).ToList(),
            ValueAdjustments = ValueAdjustments.Select(adjustment => adjustment.Clone()).ToList(),
            Rules = Rules.Select(rule => rule.Clone()).ToList()
        };
    }

    public static ModuleDefinition CreateDefault(string name = "新模块")
    {
        return new ModuleDefinition
        {
            Id = ModuleStore.CreateModuleId(name),
            Name = name,
            Enabled = true,
            Rules =
            [
                new ModuleRule
                {
                    Enabled = true,
                    Condition = "一键辅助 == 10",
                    Unit = 0,
                    Spell = "一键辅助",
                    Step = "施放 一键辅助"
                }
            ]
        };
    }
}

public sealed class ModuleMatch
{
    public int? ClassId { get; set; }
    public int? SpecId { get; set; }
    public string? PartyType { get; set; }
    public int? HeroTalent { get; set; }

    [JsonIgnore]
    public int Specificity =>
        Count(ClassId) + Count(SpecId) + Count(PartyType) + Count(HeroTalent);

    public bool Matches(int? classId, int? specId, int? partyType, int? heroTalent)
    {
        return MatchesOne(ClassId, classId)
            && MatchesOne(SpecId, specId)
            && MatchesPartyType(PartyType, partyType)
            && MatchesOne(HeroTalent, heroTalent);
    }

    public ModuleMatch Clone()
    {
        return new ModuleMatch
        {
            ClassId = ClassId,
            SpecId = SpecId,
            PartyType = NormalizePartyTypeValue(PartyType),
            HeroTalent = HeroTalent
        };
    }

    public static string? NormalizePartyTypeValue(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text)
            || text == "*"
            || string.Equals(text, "any", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(text, "单人", StringComparison.OrdinalIgnoreCase))
        {
            return "0";
        }

        if (string.Equals(text, "团队", StringComparison.OrdinalIgnoreCase))
        {
            return "1-40";
        }

        if (string.Equals(text, "队伍", StringComparison.OrdinalIgnoreCase))
        {
            return "46";
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            || int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out number))
        {
            return number is >= 1 and <= 40 ? "1-40" : number.ToString(CultureInfo.InvariantCulture);
        }

        var rangeParts = text.Split('-', 2, StringSplitOptions.TrimEntries);
        if (rangeParts.Length == 2
            && int.TryParse(rangeParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var start)
            && int.TryParse(rangeParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var end))
        {
            return start <= end
                ? $"{start.ToString(CultureInfo.InvariantCulture)}-{end.ToString(CultureInfo.InvariantCulture)}"
                : $"{end.ToString(CultureInfo.InvariantCulture)}-{start.ToString(CultureInfo.InvariantCulture)}";
        }

        return text;
    }

    private static bool MatchesOne(int? expected, int? actual)
    {
        return expected is null || actual == expected;
    }

    private static bool MatchesPartyType(string? expected, int? actual)
    {
        var normalized = NormalizePartyTypeValue(expected);
        if (normalized is null)
        {
            return true;
        }

        if (actual is null)
        {
            return false;
        }

        var rangeParts = normalized.Split('-', 2, StringSplitOptions.TrimEntries);
        if (rangeParts.Length == 2
            && int.TryParse(rangeParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var start)
            && int.TryParse(rangeParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var end))
        {
            return actual.Value >= start && actual.Value <= end;
        }

        return int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var exact)
            && actual.Value == exact;
    }

    private static int Count(int? value)
    {
        return value is null ? 0 : 1;
    }

    private static int Count(string? value)
    {
        return NormalizePartyTypeValue(value) is null ? 0 : 1;
    }
}

public sealed class ModuleRule
{
    public bool Enabled { get; set; } = true;
    public string Condition { get; set; } = string.Empty;
    public int? Unit { get; set; }
    public string? UnitName { get; set; }
    public string Spell { get; set; } = string.Empty;
    public string Hotkey { get; set; } = string.Empty;
    public string Step { get; set; } = string.Empty;

    // 子条件: 与主条件是「且」关系, 子条件之间是「或」关系。
    // 命中 = 主条件成立 && (无子条件 || 任一子条件成立)。用于表达求值器写不出的 主 && (A || B)。
    // 旧模块无此字段 → 反序列化为 null; 序列化时 null 会被 WhenWritingNull 忽略。
    public List<string>? SubConditions { get; set; }

    public ModuleRule Clone()
    {
        return new ModuleRule
        {
            Enabled = Enabled,
            Condition = Condition,
            Unit = Unit,
            UnitName = UnitName,
            Spell = Spell,
            Hotkey = Hotkey,
            Step = Step,
            SubConditions = SubConditions is null ? null : new List<string>(SubConditions)
        };
    }

    // 供求值日志与规则表显示复用的可读描述, 避免在 UI 里另写一份。
    public string DescribeCondition()
    {
        if (SubConditions is not { Count: > 0 })
        {
            return Condition;
        }

        var any = string.Join(" | ", SubConditions);
        return string.IsNullOrWhiteSpace(Condition)
            ? $"任一({any})"
            : $"{Condition}  且任一({any})";
    }
}

public sealed class ModuleValueAdjustment
{
    public bool Enabled { get; set; } = true;
    public string Condition { get; set; } = string.Empty;
    public string Field { get; set; } = string.Empty;
    public int Delta { get; set; }
    public string Formula { get; set; } = string.Empty;

    public ModuleValueAdjustment Clone()
    {
        return new ModuleValueAdjustment
        {
            Enabled = Enabled,
            Condition = Condition,
            Field = Field,
            Delta = Delta,
            Formula = Formula
        };
    }
}

public sealed class ModuleStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(),
            new StringOrNumberJsonConverter()
        }
    };

    private readonly object _gate = new();
    private List<ModuleDefinition> _modules = new();

    public ModuleStore(string moduleDirectory)
    {
        ModuleDirectory = moduleDirectory;
        Directory.CreateDirectory(ModuleDirectory);
        Reload();
    }

    public string ModuleDirectory { get; }

    public static string ResolveModuleDirectory(string baseDirectory)
    {
        var currentModuleDirectory = Path.Combine(baseDirectory, "module");
        if (Directory.Exists(currentModuleDirectory) || File.Exists(Path.Combine(baseDirectory, "Shigure.csproj")))
        {
            return currentModuleDirectory;
        }

        return Path.Combine(baseDirectory, "module");
    }

    public IReadOnlyList<ModuleDefinition> GetModules()
    {
        lock (_gate)
        {
            return _modules.Select(module => module.Clone()).ToList();
        }
    }

    public void Reload()
    {
        Directory.CreateDirectory(ModuleDirectory);
        var loaded = new List<ModuleDefinition>();
        foreach (var file in Directory.EnumerateFiles(ModuleDirectory, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                var module = JsonSerializer.Deserialize<ModuleDefinition>(File.ReadAllText(file), JsonOptions);
                if (module is null)
                {
                    continue;
                }

                Normalize(module);
                module.FilePath = file;
                loaded.Add(module);
            }
            catch
            {
                // 单个模块损坏时跳过，避免影响其它模块加载。
            }
        }

        lock (_gate)
        {
            _modules = SortModules(loaded).ToList();
        }
    }

    public ModuleDefinition? FindSelectedOrBestMatch(string? selectedModuleId, int? classId, int? specId, int? partyType, int? heroTalent)
    {
        lock (_gate)
        {
            var matches = SortMatches(_modules, classId, specId, partyType, heroTalent).ToList();
            if (!string.IsNullOrWhiteSpace(selectedModuleId))
            {
                var selected = matches.FirstOrDefault(module =>
                    string.Equals(module.Id, selectedModuleId, StringComparison.OrdinalIgnoreCase));
                if (selected is not null)
                {
                    return selected.Clone();
                }
            }

            return matches.FirstOrDefault()?.Clone();
        }
    }

    public IReadOnlyList<ModuleDefinition> FindMatches(int? classId, int? specId, int? partyType, int? heroTalent)
    {
        lock (_gate)
        {
            return SortMatches(_modules, classId, specId, partyType, heroTalent)
                .Select(module => module.Clone())
                .ToList();
        }
    }

    public ModuleDefinition Save(ModuleDefinition module)
    {
        Normalize(module);
        var oldPath = module.FilePath;
        var path = BuildModulePath(module);
        lock (_gate)
        {
            if (_modules.Any(existing =>
                !string.Equals(existing.Id, module.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Name, module.Name, StringComparison.CurrentCultureIgnoreCase)))
            {
                throw new InvalidOperationException($"模块名称“{module.Name}”已存在。");
            }
        }

        if (File.Exists(path)
            && (string.IsNullOrWhiteSpace(oldPath) || !PathsEqual(oldPath, path)))
        {
            throw new InvalidOperationException($"模块文件“{Path.GetFileName(path)}”已存在，请使用其他名称。");
        }

        if (!string.IsNullOrWhiteSpace(oldPath)
            && IsInsideModuleDirectory(oldPath)
            && !PathsEqual(oldPath, path)
            && File.Exists(oldPath))
        {
            File.Delete(oldPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(module, JsonOptions));
        module.FilePath = path;

        lock (_gate)
        {
            _modules.RemoveAll(existing =>
                string.Equals(existing.Id, module.Id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(existing.FilePath, path, StringComparison.OrdinalIgnoreCase));
            _modules.Add(module.Clone());
            _modules = SortModules(_modules).ToList();
        }

        return module.Clone();
    }

    public void Delete(ModuleDefinition module)
    {
        if (!string.IsNullOrWhiteSpace(module.FilePath) && IsInsideModuleDirectory(module.FilePath) && File.Exists(module.FilePath))
        {
            File.Delete(module.FilePath);
        }

        lock (_gate)
        {
            _modules.RemoveAll(existing =>
                string.Equals(existing.Id, module.Id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(existing.FilePath, module.FilePath, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static string CreateModuleId(string name)
    {
        return $"{SanitizeFileName(name)}-{DateTimeOffset.Now:yyyyMMddHHmmssfff}";
    }

    public string CreateNextModuleName()
    {
        lock (_gate)
        {
            for (var index = 1; ; index++)
            {
                var name = $"新模块{index.ToString(CultureInfo.InvariantCulture)}";
                if (!_modules.Any(module => string.Equals(module.Name, name, StringComparison.CurrentCultureIgnoreCase)))
                {
                    return name;
                }
            }
        }
    }

    private static IEnumerable<ModuleDefinition> SortModules(IEnumerable<ModuleDefinition> modules)
    {
        return modules
            .OrderBy(module => module.Match.ClassId ?? int.MaxValue)
            .ThenBy(module => module.Match.SpecId ?? int.MaxValue)
            .ThenBy(module => PartyTypeSortKey(module.Match.PartyType))
            .ThenBy(module => module.Match.HeroTalent ?? int.MaxValue)
            .ThenBy(module => module.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    private static IEnumerable<ModuleDefinition> SortMatches(
        IEnumerable<ModuleDefinition> modules,
        int? classId,
        int? specId,
        int? partyType,
        int? heroTalent)
    {
        return modules
            .Where(module => module.Match.Matches(classId, specId, partyType, heroTalent))
            .OrderByDescending(module => module.Match.Specificity)
            .ThenBy(module => module.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    private string BuildModulePath(ModuleDefinition module)
    {
        var fileName = $"{SanitizeFileName(module.Name)}.json";
        return Path.Combine(ModuleDirectory, fileName);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static void Normalize(ModuleDefinition module)
    {
        if (string.IsNullOrWhiteSpace(module.Name))
        {
            module.Name = "新模块";
        }

        if (string.IsNullOrWhiteSpace(module.Id))
        {
            module.Id = CreateModuleId(module.Name);
        }

        module.Match ??= new ModuleMatch();
        module.Match.PartyType = ModuleMatch.NormalizePartyTypeValue(module.Match.PartyType);
        module.Units ??= new List<ModuleUnit>();
        module.Counts ??= new List<ModuleCountField>();
        module.ValueAdjustments ??= new List<ModuleValueAdjustment>();
        module.Units.RemoveAll(unit => string.IsNullOrWhiteSpace(unit.Name));
        module.Counts.RemoveAll(count => string.IsNullOrWhiteSpace(count.Name));
        module.ValueAdjustments.RemoveAll(adjustment => string.IsNullOrWhiteSpace(adjustment.Field));
        foreach (var unit in module.Units)
        {
            unit.Name = unit.Name.Trim();
            unit.HealthName = string.IsNullOrWhiteSpace(unit.HealthName) ? null : unit.HealthName.Trim();
            unit.HealthThresholdField = string.IsNullOrWhiteSpace(unit.HealthThresholdField) ? null : unit.HealthThresholdField.Trim();
        }

        foreach (var count in module.Counts)
        {
            count.Name = count.Name.Trim();
            count.HealthThresholdField = string.IsNullOrWhiteSpace(count.HealthThresholdField) ? null : count.HealthThresholdField.Trim();
        }

        foreach (var adjustment in module.ValueAdjustments)
        {
            adjustment.Field = adjustment.Field.Trim();
            adjustment.Condition = adjustment.Condition?.Trim() ?? string.Empty;
            adjustment.Formula = adjustment.Formula?.Trim() ?? string.Empty;
        }

        module.Rules ??= new List<ModuleRule>();
        foreach (var rule in module.Rules)
        {
            if (rule.SubConditions is null)
            {
                continue;
            }

            // 去空白、丢空项; 整组为空则回到 null, 保持文件干净且求值不见空子条件。
            rule.SubConditions = rule.SubConditions
                .Select(sub => sub?.Trim() ?? string.Empty)
                .Where(sub => sub.Length > 0)
                .ToList();
            if (rule.SubConditions.Count == 0)
            {
                rule.SubConditions = null;
            }
        }
    }

    private bool IsInsideModuleDirectory(string path)
    {
        var fullDirectory = Path.GetFullPath(ModuleDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeFileName(string value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "module" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            text = text.Replace(invalid, '-');
        }

        text = Regex.Replace(text, @"\s+", "-");
        return text.Length > 64 ? text[..64] : text;
    }

    private static int PartyTypeSortKey(string? value)
    {
        return ModuleMatch.NormalizePartyTypeValue(value) switch
        {
            null => int.MaxValue,
            "0" => 0,
            "1-40" => 1,
            "46" => 46,
            var other when int.TryParse(other, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => int.MaxValue - 1
        };
    }

    private sealed class StringOrNumberJsonConverter : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.Null => null,
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number when reader.TryGetInt64(out var integer) => integer.ToString(CultureInfo.InvariantCulture),
                JsonTokenType.Number when reader.TryGetDouble(out var number) => number.ToString(CultureInfo.InvariantCulture),
                JsonTokenType.True => "true",
                JsonTokenType.False => "false",
                _ => throw new JsonException($"无法将 {reader.TokenType} 转换为字符串。")
            };
        }

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStringValue(value);
        }
    }
}

public static class ModuleLogic
{
    public static LogicDecision Run(ModuleDefinition module, GameState state, KeymapService keymap)
    {
        var info = CreateInfo(module, state);
        var unitSlots = ResolveDynamicFields(module, state);
        var failedSpells = keymap.GetCurrentFailedSpells();
        var oneKeySpells = keymap.GetCurrentOneKeySpells();

        foreach (var rule in module.Rules.Where(rule => rule.Enabled))
        {
            if (!ModuleConditionEvaluator.TryEvaluateRule(rule, state, out var conditionMatched, out var error, failedSpells))
            {
                info["条件错误"] = error;
                info["规则条件"] = rule.DescribeCondition();
                return new LogicDecision(null, $"{module.Name}: 条件错误", info, module.Name);
            }

            if (!conditionMatched)
            {
                continue;
            }

            if (ModuleSpecialActions.IsPauseSpell(rule.Spell))
            {
                info["命中条件"] = string.IsNullOrWhiteSpace(rule.DescribeCondition()) ? "始终" : rule.DescribeCondition();
                info["动作技能"] = ModuleSpecialActions.PauseSpell;
                info["动作按键"] = "-";
                info["动作单位"] = "-";
                return new LogicDecision(null, $"{module.Name}: 暂停", info, module.Name);
            }

            var resolvedUnit = rule.Unit;
            if (!string.IsNullOrWhiteSpace(rule.UnitName))
            {
                // 动态目标: 选择器没选中任何单位时跳过该规则(等同条件未命中)。
                var slot = unitSlots.TryGetValue(rule.UnitName, out var s) ? s : null;
                if (slot is null)
                {
                    continue;
                }

                resolvedUnit = int.TryParse(slot, out var slotUnit) ? slotUnit : 0;
            }

            var actionSpell = rule.Spell;
            if (ModuleSpecialActions.IsFailedSpell(actionSpell))
            {
                actionSpell = ModuleSpecialActions.GetFailedSpell(state, failedSpells);
                if (string.IsNullOrWhiteSpace(actionSpell))
                {
                    continue;
                }
            }
            else if (ModuleSpecialActions.IsOneKeySpell(actionSpell))
            {
                actionSpell = ModuleSpecialActions.GetOneKeySpell(state, oneKeySpells);
                if (string.IsNullOrWhiteSpace(actionSpell))
                {
                    continue;
                }

                resolvedUnit = 0;
            }

            var hotkey = string.IsNullOrWhiteSpace(rule.Hotkey)
                ? string.IsNullOrWhiteSpace(actionSpell) ? null : keymap.GetHotkey(resolvedUnit, actionSpell)
                : rule.Hotkey.Trim();
            var step = BuildStep(module, rule, hotkey, actionSpell);
            info["命中条件"] = string.IsNullOrWhiteSpace(rule.Condition) ? "始终" : rule.Condition;
            info["动作技能"] = string.IsNullOrWhiteSpace(actionSpell) ? "-" : actionSpell;
            info["动作按键"] = string.IsNullOrWhiteSpace(hotkey) ? "-" : hotkey;
            info["动作单位"] = string.IsNullOrWhiteSpace(rule.UnitName)
                ? resolvedUnit.GetValueOrDefault()
                : $"{rule.UnitName} → {resolvedUnit.GetValueOrDefault()}";
            return new LogicDecision(hotkey, step, info, module.Name);
        }

        info["命中条件"] = "-";
        return new LogicDecision(null, $"{module.Name}: 无匹配规则", info, module.Name);
    }

    // 把模块定义的动态单位/数量各解析一次, 写入当前帧 state.Values 供条件求值与目标解析使用。
    public static Dictionary<string, string?> ResolveDynamicFields(ModuleDefinition module, GameState state)
    {
        if (IsDynamicFieldsResolved(module, state)
            && state.Values.TryGetValue("$units", out var existingUnitsObj)
            && existingUnitsObj is Dictionary<string, string?> existingUnits)
        {
            return existingUnits;
        }

        var earlyAppliedAdjustments = ApplyValueAdjustments(
            module,
            state,
            adjustment => IsEarlyThresholdAdjustment(module, state, adjustment));
        var unitSlots = ResolveUnits(module, state);
        ResolveCounts(module, state);
        ApplyValueAdjustments(module, state, adjustment => !earlyAppliedAdjustments.Contains(adjustment));
        state.Values["$dynamicModuleId"] = module.Id;
        return unitSlots;
    }

    private static bool IsDynamicFieldsResolved(ModuleDefinition module, GameState state)
    {
        return state.Values.TryGetValue("$dynamicModuleId", out var value)
            && string.Equals(value?.ToString(), module.Id, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string?> ResolveUnits(ModuleDefinition module, GameState state)
    {
        var unitSlots = new Dictionary<string, string?>(StringComparer.Ordinal);
        // 生命值名 → 该单位槽位的 生命值 值(未解析则为 null), 供条件直接按名引用。
        var unitHealth = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var unit in module.Units)
        {
            if (string.IsNullOrWhiteSpace(unit.Name))
            {
                continue;
            }

            var slot = UnitSelector.Resolve(unit, state);
            unitSlots[unit.Name] = slot;

            if (!string.IsNullOrWhiteSpace(unit.HealthName))
            {
                unitHealth[unit.HealthName] = slot is not null
                    && state.Group.TryGetValue(slot, out var member)
                    && member.TryGetValue("生命值", out var value)
                        ? value
                        : null;
            }
        }

        state.Values["$units"] = unitSlots;
        state.Values["$unithealth"] = unitHealth;
        return unitSlots;
    }

    private static void ResolveCounts(ModuleDefinition module, GameState state)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var count in module.Counts)
        {
            if (!string.IsNullOrWhiteSpace(count.Name))
            {
                counts[count.Name] = UnitSelector.Resolve(count, state);
            }
        }

        state.Values["$counts"] = counts;
    }

    private static HashSet<ModuleValueAdjustment> ApplyValueAdjustments(
        ModuleDefinition module,
        GameState state,
        Func<ModuleValueAdjustment, bool>? include = null)
    {
        var applied = new HashSet<ModuleValueAdjustment>();
        foreach (var adjustment in module.ValueAdjustments.Where(adjustment => adjustment.Enabled))
        {
            if (string.IsNullOrWhiteSpace(adjustment.Field)
                || (adjustment.Delta == 0 && string.IsNullOrWhiteSpace(adjustment.Formula)))
            {
                continue;
            }

            if (include is not null && !include(adjustment))
            {
                continue;
            }

            if (!ModuleConditionEvaluator.TryEvaluate(adjustment.Condition, state, out var matched, out _)
                || !matched)
            {
                continue;
            }

            if (!ApplyValueAdjustment(state, adjustment))
            {
                continue;
            }

            applied.Add(adjustment);
        }

        return applied;
    }

    private static bool IsEarlyThresholdAdjustment(
        ModuleDefinition module,
        GameState state,
        ModuleValueAdjustment adjustment)
    {
        var key = adjustment.Field.Trim();
        return key.Length > 0
            && !key.Contains('.')
            && !key.StartsWith('$')
            && DynamicThresholdFields(module).Contains(key);
    }

    private static HashSet<string> DynamicThresholdFields(ModuleDefinition module)
    {
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var unit in module.Units)
        {
            if (!string.IsNullOrWhiteSpace(unit.HealthThresholdField))
            {
                fields.Add(unit.HealthThresholdField.Trim());
            }
        }

        foreach (var count in module.Counts)
        {
            if (!string.IsNullOrWhiteSpace(count.HealthThresholdField))
            {
                fields.Add(count.HealthThresholdField.Trim());
            }
        }

        return fields;
    }

    private static bool ApplyValueAdjustment(GameState state, ModuleValueAdjustment adjustment)
    {
        if (!string.IsNullOrWhiteSpace(adjustment.Formula))
        {
            if (!FormulaEvaluator.TryEvaluateInt(adjustment.Formula, state, out var value, out _))
            {
                return false;
            }

            SetDynamicValue(state, adjustment.Field, value);
            return true;
        }

        ApplyValueDelta(state, adjustment.Field, adjustment.Delta);
        return true;
    }

    private static void SetDynamicValue(GameState state, string field, object? value)
    {
        var key = field.Trim();
        if (key.Length == 0)
        {
            return;
        }

        if (key.StartsWith("auras.", StringComparison.OrdinalIgnoreCase))
        {
            GetOrCreateMutableDict(state, "auras")[key["auras.".Length..]] = value;
            return;
        }

        if (key.StartsWith("spells.", StringComparison.OrdinalIgnoreCase))
        {
            GetOrCreateMutableDict(state, "spells")[key["spells.".Length..]] = value;
            return;
        }

        GetOrCreateDynamicValues(state)[key] = value;
    }

    private static Dictionary<string, object?> GetOrCreateDynamicValues(GameState state)
    {
        if (state.Values.TryGetValue("$dynamicvalues", out var dynamicObj))
        {
            if (dynamicObj is Dictionary<string, object?> dynamicValues)
            {
                return dynamicValues;
            }

            if (dynamicObj is IReadOnlyDictionary<string, object?> existingValues)
            {
                var copiedValues = new Dictionary<string, object?>(existingValues, StringComparer.Ordinal);
                state.Values["$dynamicvalues"] = copiedValues;
                return copiedValues;
            }
        }

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        state.Values["$dynamicvalues"] = values;
        return values;
    }

    private static Dictionary<string, object?> GetOrCreateMutableDict(GameState state, string dictKey)
    {
        if (state.Values.TryGetValue(dictKey, out var obj))
        {
            if (obj is Dictionary<string, object?> mutable)
            {
                return mutable;
            }

            if (obj is IReadOnlyDictionary<string, object?> readOnly)
            {
                var copy = new Dictionary<string, object?>(readOnly, StringComparer.Ordinal);
                state.Values[dictKey] = copy;
                return copy;
            }
        }

        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        state.Values[dictKey] = dict;
        return dict;
    }

    private static void ApplyValueDelta(GameState state, string field, int delta)
    {
        var key = field.Trim();
        if (key.Length == 0)
        {
            return;
        }

        if (key.StartsWith("auras.", StringComparison.OrdinalIgnoreCase))
        {
            var auraKey = key["auras.".Length..];
            var dict = GetOrCreateMutableDict(state, "auras");
            var current = dict.TryGetValue(auraKey, out var v) ? v : null;
            dict[auraKey] = AddDelta(current, delta);
            return;
        }

        if (key.StartsWith("spells.", StringComparison.OrdinalIgnoreCase))
        {
            var spellKey = key["spells.".Length..];
            var dict = GetOrCreateMutableDict(state, "spells");
            var current = dict.TryGetValue(spellKey, out var v) ? v : null;
            dict[spellKey] = AddDelta(current, delta);
            return;
        }

        if (state.Values.TryGetValue("$counts", out var countsObj)
            && countsObj is Dictionary<string, int> counts
            && counts.TryGetValue(key, out var countValue))
        {
            counts[key] = countValue + delta;
            return;
        }

        if (state.Values.TryGetValue("$unithealth", out var healthObj)
            && healthObj is Dictionary<string, object?> unitHealth
            && unitHealth.ContainsKey(key))
        {
            unitHealth[key] = AddDelta(unitHealth[key], delta);
            return;
        }

        state.Values[key] = AddDelta(state.Values.TryGetValue(key, out var value) ? value : null, delta);
    }

    private static int AddDelta(object? value, int delta)
    {
        return TryToInt(value, out var number) ? number + delta : delta;
    }

    private static Dictionary<string, object?> CreateInfo(ModuleDefinition module, GameState state)
    {
        return new Dictionary<string, object?>
        {
            ["模块"] = module.Name,
            ["职业"] = module.Match.ClassId?.ToString() ?? "*",
            ["专精"] = module.Match.SpecId?.ToString() ?? "*",
            ["队伍类型"] = state.GetInt("队伍类型"),
            ["英雄天赋"] = state.GetInt("英雄天赋"),
            ["规则数"] = module.Rules.Count
        };
    }

    private static bool TryToInt(object? value, out int number)
    {
        switch (value)
        {
            case int i:
                number = i;
                return true;
            case long l:
                number = (int)l;
                return true;
            case double d:
                number = (int)d;
                return true;
            case decimal m:
                number = (int)m;
                return true;
            case bool b:
                number = b ? 1 : 0;
                return true;
            case string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                || int.TryParse(s, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsed):
                number = parsed;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    private static string BuildStep(ModuleDefinition module, ModuleRule rule, string? hotkey, string? actionSpell)
    {
        if (!string.IsNullOrWhiteSpace(rule.Step))
        {
            return $"{module.Name}: {rule.Step.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(rule.Spell))
        {
            var spell = string.IsNullOrWhiteSpace(actionSpell) ? rule.Spell.Trim() : actionSpell.Trim();
            return string.IsNullOrWhiteSpace(hotkey)
                ? $"{module.Name}: 未找到按键 {spell}"
                : $"{module.Name}: 施放 {spell}";
        }

        return string.IsNullOrWhiteSpace(hotkey)
            ? $"{module.Name}: 命中规则"
            : $"{module.Name}: 发送 {hotkey}";
    }
}

public static class ModuleConditionEvaluator
{
    private static readonly Regex InRegex = new(
        @"^\s*(?<field>.+?)\s+(?<op>not\s+in|in)\s*\((?<value>.*?)\)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ComparisonRegex = new(
        @"^\s*(?<field>.+?)\s*(?<op>==|!=|>=|<=|>|<)\s*(?<value>.+?)\s*$",
        RegexOptions.Compiled);

    public static bool TryEvaluate(
        string? expression,
        GameState state,
        out bool matched,
        out string? error,
        IReadOnlyDictionary<int, string>? failedSpells = null)
    {
        matched = false;
        error = null;

        if (string.IsNullOrWhiteSpace(expression))
        {
            matched = true;
            return true;
        }

        foreach (var orPart in Regex.Split(expression, @"\s*\|\|\s*"))
        {
            var allAndMatched = true;
            foreach (var andPart in Regex.Split(orPart, @"\s*&&\s*"))
            {
                if (!TryEvaluateTerm(andPart, state, out var termMatched, out error, failedSpells))
                {
                    return false;
                }

                if (!termMatched)
                {
                    allAndMatched = false;
                    break;
                }
            }

            if (allAndMatched)
            {
                matched = true;
                return true;
            }
        }

        matched = false;
        return true;
    }

    // 整条规则的命中判定: 主条件成立 且 (无子条件 || 任一子条件成立)。
    // 子条件之间是「或」, 与主条件是「且」; 任一子条件求值出错都按错误返回。
    public static bool TryEvaluateRule(
        ModuleRule rule,
        GameState state,
        out bool matched,
        out string? error,
        IReadOnlyDictionary<int, string>? failedSpells = null)
    {
        if (!TryEvaluate(rule.Condition, state, out matched, out error, failedSpells))
        {
            return false;
        }

        if (!matched || rule.SubConditions is not { Count: > 0 })
        {
            return true;
        }

        foreach (var sub in rule.SubConditions)
        {
            if (string.IsNullOrWhiteSpace(sub))
            {
                continue;
            }

            if (!TryEvaluate(sub, state, out var subMatched, out error, failedSpells))
            {
                matched = false;
                return false;
            }

            if (subMatched)
            {
                matched = true;
                return true;
            }
        }

        matched = false;
        return true;
    }

    public static bool TryResolveInt(GameState state, string fieldName, out int value)
    {
        if (TryResolveDouble(state, fieldName, out var number))
        {
            value = (int)number;
            return true;
        }

        value = 0;
        return false;
    }

    public static bool TryResolveDouble(GameState state, string fieldName, out double value)
        => TryToDouble(ResolveValue(state, fieldName), out value);

    private static bool TryEvaluateTerm(
        string term,
        GameState state,
        out bool matched,
        out string? error,
        IReadOnlyDictionary<int, string>? failedSpells)
    {
        matched = false;
        error = null;
        var trimmed = term.Trim();
        if (trimmed.Length == 0)
        {
            matched = true;
            return true;
        }

        var inMatch = InRegex.Match(trimmed);
        if (inMatch.Success)
        {
            var inLeft = ResolveValue(state, inMatch.Groups["field"].Value.Trim(), failedSpells);
            var inOp = NormalizeOperator(inMatch.Groups["op"].Value);
            var values = ParseListLiterals(inMatch.Groups["value"].Value);
            return TryCompareIn(inLeft, inOp, values, out matched, out error);
        }

        var comparison = ComparisonRegex.Match(trimmed);
        if (!comparison.Success)
        {
            var invert = trimmed.StartsWith('!');
            var fieldName = invert ? trimmed[1..].Trim() : trimmed;
            var value = ResolveValue(state, fieldName, failedSpells);
            matched = invert ? !IsTruthy(value) : IsTruthy(value);
            return true;
        }

        var left = ResolveValue(state, comparison.Groups["field"].Value.Trim(), failedSpells);
        var op = comparison.Groups["op"].Value;
        var right = ParseLiteral(comparison.Groups["value"].Value.Trim());
        return TryCompare(left, op, right, out matched, out error);
    }

    private static object? ResolveValue(
        GameState state,
        string fieldName,
        IReadOnlyDictionary<int, string>? failedSpells = null)
    {
        var key = fieldName.Trim();
        if (key.StartsWith("state.", StringComparison.OrdinalIgnoreCase))
        {
            key = key["state.".Length..];
        }

        if (key.StartsWith("spells.", StringComparison.OrdinalIgnoreCase))
        {
            return state.Spells.TryGetValue(key["spells.".Length..], out var value) ? value : null;
        }

        if (key.StartsWith("spell.", StringComparison.OrdinalIgnoreCase))
        {
            return state.Spells.TryGetValue(key["spell.".Length..], out var value) ? value : null;
        }

        if (key.StartsWith("auras.", StringComparison.OrdinalIgnoreCase))
        {
            return state.Auras.TryGetValue(key["auras.".Length..], out var value) ? value : null;
        }

        if (key.StartsWith("aura.", StringComparison.OrdinalIgnoreCase))
        {
            return state.Auras.TryGetValue(key["aura.".Length..], out var value) ? value : null;
        }

        if (ModuleSpecialActions.IsFailedSpell(key))
        {
            return ModuleSpecialActions.GetFailedSpell(state, failedSpells);
        }

        if (key.StartsWith("group.", StringComparison.OrdinalIgnoreCase))
        {
            var parts = key.Split('.', 3);
            if (parts.Length == 3
                && state.Group.TryGetValue(parts[1], out var unit)
                && unit.TryGetValue(parts[2], out var value))
            {
                return value;
            }

            return null;
        }

        // 数量字段(整名匹配): 如 低血量人数。
        if (state.Values.TryGetValue("$counts", out var countsObj)
            && countsObj is Dictionary<string, int> counts
            && counts.TryGetValue(key, out var countValue))
        {
            return countValue;
        }

        // 生命值名(整名匹配): 动态单位的 生命值 直接命名, 如 最低血量 < 50。未解析返回 null。
        if (state.Values.TryGetValue("$unithealth", out var healthObj)
            && healthObj is Dictionary<string, object?> unitHealth
            && unitHealth.TryGetValue(key, out var healthValue))
        {
            return healthValue;
        }

        // 动态单位字段引用: <单位名>.<字段>, 解析槽位后读 group[槽位][字段]; 单位未解析返回 null。
        var dot = key.IndexOf('.');
        if (dot > 0
            && state.Values.TryGetValue("$units", out var unitsObj)
            && unitsObj is Dictionary<string, string?> units)
        {
            var unitName = key[..dot];
            if (units.TryGetValue(unitName, out var slot))
            {
                if (slot is null)
                {
                    return null;
                }

                var field = key[(dot + 1)..];
                return state.Group.TryGetValue(slot, out var member) && member.TryGetValue(field, out var value)
                    ? value
                    : null;
            }
        }

        // 裸单位名作为存在性布尔: 解析到槽位即 true。
        if (state.Values.TryGetValue("$units", out var unitsObj2)
            && unitsObj2 is Dictionary<string, string?> units2
            && units2.TryGetValue(key, out var bareSlot))
        {
            return bareSlot is not null;
        }

        if (state.Values.TryGetValue("$dynamicvalues", out var dynamicObj)
            && dynamicObj is IReadOnlyDictionary<string, object?> dynamicValues
            && dynamicValues.TryGetValue(key, out var dynamicValue))
        {
            return dynamicValue;
        }

        return state.GetValue(key);
    }

    private static object? ParseLiteral(string value)
    {
        var text = value.Trim();
        if ((text.StartsWith('"') && text.EndsWith('"')) || (text.StartsWith('\'') && text.EndsWith('\'')))
        {
            return text[1..^1];
        }

        return text.ToLowerInvariant() switch
        {
            "null" or "nil" or "空" => null,
            "true" or "yes" or "是" => true,
            "false" or "no" or "否" => false,
            _ => TryParseNumber(text, out var number) ? number : text
        };
    }

    private static bool TryCompare(object? left, string op, object? right, out bool matched, out string? error)
    {
        matched = false;
        error = null;

        if (TryToDouble(left, out var leftNumber) && TryToDouble(right, out var rightNumber))
        {
            matched = op switch
            {
                "==" => leftNumber == rightNumber,
                "!=" => leftNumber != rightNumber,
                ">" => leftNumber > rightNumber,
                ">=" => leftNumber >= rightNumber,
                "<" => leftNumber < rightNumber,
                "<=" => leftNumber <= rightNumber,
                _ => false
            };
            return true;
        }

        if (op is "==" or "!=")
        {
            var equals = string.Equals(FormatComparable(left), FormatComparable(right), StringComparison.OrdinalIgnoreCase);
            matched = op == "==" ? equals : !equals;
            return true;
        }

        // 关系比较(> < >= <=)遇到非数字/缺失值(如未解析的动态单位字段)时不报错, 视为不命中, 继续判断下一条规则。
        matched = false;
        return true;
    }

    private static bool TryCompareIn(object? left, string op, IReadOnlyList<object?> values, out bool matched, out string? error)
    {
        matched = false;
        error = null;

        foreach (var value in values)
        {
            if (!TryCompare(left, "==", value, out var equals, out error))
            {
                return false;
            }

            if (equals)
            {
                matched = op == "in";
                return true;
            }
        }

        matched = op == "not in";
        return true;
    }

    private static IReadOnlyList<object?> ParseListLiterals(string value)
    {
        var values = new List<object?>();
        foreach (var item in SplitList(value))
        {
            var trimmed = item.Trim();
            if (trimmed.Length > 0)
            {
                values.Add(ParseLiteral(trimmed));
            }
        }

        return values;
    }

    private static IEnumerable<string> SplitList(string value)
    {
        var start = 0;
        var quote = '\0';
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (ch is '"' or '\'')
            {
                quote = ch;
                continue;
            }

            if (ch == ',')
            {
                yield return value[start..i];
                start = i + 1;
            }
        }

        yield return value[start..];
    }

    private static string NormalizeOperator(string op)
    {
        return Regex.Replace(op.Trim().ToLowerInvariant(), @"\s+", " ");
    }

    private static bool IsTruthy(object? value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            double d => Math.Abs(d) > double.Epsilon,
            string s => !string.IsNullOrWhiteSpace(s)
                && !string.Equals(s, "0", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(s, "false", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private static bool TryParseNumber(string text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    private static bool TryToDouble(object? value, out double number)
    {
        switch (value)
        {
            case int i:
                number = i;
                return true;
            case long l:
                number = l;
                return true;
            case double d:
                number = d;
                return true;
            case bool b:
                number = b ? 1 : 0;
                return true;
            case string s:
                return TryParseNumber(s, out number);
            default:
                number = 0;
                return false;
        }
    }

    private static string FormatComparable(object? value)
    {
        return value switch
        {
            null => string.Empty,
            bool b => b ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }
}
