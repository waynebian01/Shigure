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
    public bool Enabled { get; set; } = true;
    public ModuleMatch Match { get; set; } = new();
    public List<ModuleRule> Rules { get; set; } = new();

    [JsonIgnore]
    public string? FilePath { get; set; }

    public ModuleDefinition Clone()
    {
        return new ModuleDefinition
        {
            Id = Id,
            Name = Name,
            Enabled = Enabled,
            FilePath = FilePath,
            Match = Match.Clone(),
            Rules = Rules.Select(rule => rule.Clone()).ToList()
        };
    }

    public static ModuleDefinition CreateDefault()
    {
        return new ModuleDefinition
        {
            Id = ModuleStore.CreateModuleId("新模块"),
            Name = "新模块",
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
    public int? PartyType { get; set; }
    public int? HeroTalent { get; set; }

    [JsonIgnore]
    public int Specificity =>
        Count(ClassId) + Count(SpecId) + Count(PartyType) + Count(HeroTalent);

    public bool Matches(int? classId, int? specId, int? partyType, int? heroTalent)
    {
        return MatchesOne(ClassId, classId)
            && MatchesOne(SpecId, specId)
            && MatchesOne(PartyType, partyType)
            && MatchesOne(HeroTalent, heroTalent);
    }

    public ModuleMatch Clone()
    {
        return new ModuleMatch
        {
            ClassId = ClassId,
            SpecId = SpecId,
            PartyType = PartyType,
            HeroTalent = HeroTalent
        };
    }

    private static bool MatchesOne(int? expected, int? actual)
    {
        return expected is null || actual == expected;
    }

    private static int Count(int? value)
    {
        return value is null ? 0 : 1;
    }
}

public sealed class ModuleRule
{
    public bool Enabled { get; set; } = true;
    public string Condition { get; set; } = string.Empty;
    public int? Unit { get; set; }
    public string Spell { get; set; } = string.Empty;
    public string Hotkey { get; set; } = string.Empty;
    public string Step { get; set; } = string.Empty;

    public ModuleRule Clone()
    {
        return new ModuleRule
        {
            Enabled = Enabled,
            Condition = Condition,
            Unit = Unit,
            Spell = Spell,
            Hotkey = Hotkey,
            Step = Step
        };
    }
}

public sealed class ModuleStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
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
        var currentModuleDirectory = Path.Combine(Environment.CurrentDirectory, "module");
        if (Directory.Exists(currentModuleDirectory) || File.Exists(Path.Combine(Environment.CurrentDirectory, "Shigure.csproj")))
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

    public ModuleDefinition? FindBestMatch(int? classId, int? specId, int? partyType, int? heroTalent)
    {
        lock (_gate)
        {
            return _modules
                .Where(module => module.Enabled && module.Match.Matches(classId, specId, partyType, heroTalent))
                .OrderByDescending(module => module.Match.Specificity)
                .ThenBy(module => module.Name, StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault()
                ?.Clone();
        }
    }

    public ModuleDefinition Save(ModuleDefinition module)
    {
        Normalize(module);
        var oldPath = module.FilePath;
        var path = BuildModulePath(module);
        if (!string.IsNullOrWhiteSpace(oldPath)
            && IsInsideModuleDirectory(oldPath)
            && !string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)
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

    private static IEnumerable<ModuleDefinition> SortModules(IEnumerable<ModuleDefinition> modules)
    {
        return modules
            .OrderByDescending(module => module.Enabled)
            .ThenBy(module => module.Match.ClassId ?? int.MaxValue)
            .ThenBy(module => module.Match.SpecId ?? int.MaxValue)
            .ThenBy(module => module.Match.PartyType ?? int.MaxValue)
            .ThenBy(module => module.Match.HeroTalent ?? int.MaxValue)
            .ThenBy(module => module.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    private string BuildModulePath(ModuleDefinition module)
    {
        var match = module.Match;
        var directory = Path.Combine(
            ModuleDirectory,
            MatchPart(match.ClassId),
            MatchPart(match.SpecId),
            MatchPart(match.PartyType),
            MatchPart(match.HeroTalent));
        var fileName = $"{SanitizeFileName(module.Name)}-{SanitizeFileName(module.Id)}.json";
        return Path.Combine(directory, fileName);
    }

    private static string MatchPart(int? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? "any";
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
        module.Rules ??= new List<ModuleRule>();
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
}

public static class ModuleLogic
{
    public static LogicDecision Run(ModuleDefinition module, GameState state, KeymapService keymap)
    {
        var info = CreateInfo(module, state);
        foreach (var rule in module.Rules.Where(rule => rule.Enabled))
        {
            if (!ModuleConditionEvaluator.TryEvaluate(rule.Condition, state, out var conditionMatched, out var error))
            {
                info["条件错误"] = error;
                info["规则条件"] = rule.Condition;
                return new LogicDecision(null, $"{module.Name}: 条件错误", info, module.Name);
            }

            if (!conditionMatched)
            {
                continue;
            }

            var hotkey = string.IsNullOrWhiteSpace(rule.Hotkey)
                ? string.IsNullOrWhiteSpace(rule.Spell) ? null : keymap.GetHotkey(rule.Unit, rule.Spell)
                : rule.Hotkey.Trim();
            var step = BuildStep(module, rule, hotkey);
            info["命中条件"] = string.IsNullOrWhiteSpace(rule.Condition) ? "始终" : rule.Condition;
            info["动作技能"] = string.IsNullOrWhiteSpace(rule.Spell) ? "-" : rule.Spell;
            info["动作按键"] = string.IsNullOrWhiteSpace(hotkey) ? "-" : hotkey;
            info["动作单位"] = rule.Unit.GetValueOrDefault();
            return new LogicDecision(hotkey, step, info, module.Name);
        }

        info["命中条件"] = "-";
        return new LogicDecision(null, $"{module.Name}: 无匹配规则", info, module.Name);
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

    private static string BuildStep(ModuleDefinition module, ModuleRule rule, string? hotkey)
    {
        if (!string.IsNullOrWhiteSpace(rule.Step))
        {
            return $"{module.Name}: {rule.Step.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(rule.Spell))
        {
            return string.IsNullOrWhiteSpace(hotkey)
                ? $"{module.Name}: 未找到按键 {rule.Spell.Trim()}"
                : $"{module.Name}: 施放 {rule.Spell.Trim()}";
        }

        return string.IsNullOrWhiteSpace(hotkey)
            ? $"{module.Name}: 命中规则"
            : $"{module.Name}: 发送 {hotkey}";
    }
}

public static class ModuleConditionEvaluator
{
    private static readonly Regex ComparisonRegex = new(
        @"^\s*(?<field>.+?)\s*(?<op>==|!=|>=|<=|>|<)\s*(?<value>.+?)\s*$",
        RegexOptions.Compiled);

    public static bool TryEvaluate(string? expression, GameState state, out bool matched, out string? error)
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
                if (!TryEvaluateTerm(andPart, state, out var termMatched, out error))
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

    private static bool TryEvaluateTerm(string term, GameState state, out bool matched, out string? error)
    {
        matched = false;
        error = null;
        var trimmed = term.Trim();
        if (trimmed.Length == 0)
        {
            matched = true;
            return true;
        }

        var comparison = ComparisonRegex.Match(trimmed);
        if (!comparison.Success)
        {
            var invert = trimmed.StartsWith('!');
            var fieldName = invert ? trimmed[1..].Trim() : trimmed;
            var value = ResolveValue(state, fieldName);
            matched = invert ? !IsTruthy(value) : IsTruthy(value);
            return true;
        }

        var left = ResolveValue(state, comparison.Groups["field"].Value.Trim());
        var op = comparison.Groups["op"].Value;
        var right = ParseLiteral(comparison.Groups["value"].Value.Trim());
        return TryCompare(left, op, right, out matched, out error);
    }

    private static object? ResolveValue(GameState state, string fieldName)
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

        error = $"无法比较: {FormatComparable(left)} {op} {FormatComparable(right)}";
        return false;
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
