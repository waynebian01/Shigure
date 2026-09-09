using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static Shigure.LuaLiteParser;

namespace Shigure;

/// <summary>
/// 将 Fuyutsui core/classmacros.lua 的 ClassMacros 展开为 keymap/*.json
/// （对齐 core/macro.lua CreateMacro 的槽位与热键池）。
/// </summary>
internal static partial class FuyutsuiKeymapConverter
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly string[] Modifiers =
    [
        // 保留原有 14 组顺序，随后追加左右混合组合，避免已有槽位整体平移。
        "RCTRL", "RALT", "RSHIFT",
        "RALT-RCTRL", "RALT-RSHIFT", "RCTRL-RSHIFT",
        "RALT-RCTRL-RSHIFT",
        "LCTRL", "LALT", "LSHIFT",
        "LALT-LCTRL", "LALT-LSHIFT", "LCTRL-LSHIFT",
        "LALT-LCTRL-LSHIFT",
        "LALT-RCTRL", "RALT-LCTRL",
        "LALT-RSHIFT", "RALT-LSHIFT",
        "LCTRL-RSHIFT", "RCTRL-LSHIFT",
        "LALT-LCTRL-RSHIFT", "LALT-RCTRL-LSHIFT", "LALT-RCTRL-RSHIFT",
        "RALT-LCTRL-LSHIFT", "RALT-LCTRL-RSHIFT", "RALT-RCTRL-LSHIFT"
    ];

    private static readonly string[] Keys =
    [
        "NUMPAD1", "NUMPAD2", "NUMPAD3", "NUMPAD4", "NUMPAD5",
        "NUMPAD6", "NUMPAD7", "NUMPAD8", "NUMPAD9", "NUMPAD0",
        "NUMPADDECIMAL", "NUMPADPLUS", "NUMPADMINUS", "NUMPADMULTIPLY", "NUMPADDIVIDE",
        "F1", "F2", "F3", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        ",", ".", ";", "'", "[", "]", "\\", "=", "-",
        "INSERT", "DELETE", "HOME", "END", "PAGEUP", "PAGEDOWN",
        "UP", "DOWN", "LEFT", "RIGHT"
    ];

    private static readonly string[] MacroKind = BuildMacroKind();

    internal static int MacroSlotCapacity => Modifiers.Length * Keys.Length;

    private static readonly Dictionary<string, int> ClassFileToId = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WARRIOR"] = 1,
        ["PALADIN"] = 2,
        ["HUNTER"] = 3,
        ["ROGUE"] = 4,
        ["PRIEST"] = 5,
        ["DEATHKNIGHT"] = 6,
        ["SHAMAN"] = 7,
        ["MAGE"] = 8,
        ["WARLOCK"] = 9,
        ["MONK"] = 10,
        ["DRUID"] = 11,
        ["DEMONHUNTER"] = 12,
        ["EVOKER"] = 13
    };

    public sealed record UpdateResult(
        string ClassMacrosPath,
        IReadOnlyList<string> UpdatedFiles,
        IReadOnlyList<string> Warnings);

    public static UpdateResult UpdateFromClassMacros(string classMacrosPath, string keymapDirectory)
    {
        if (!File.Exists(classMacrosPath))
        {
            throw new FileNotFoundException($"找不到 classmacros.lua: {classMacrosPath}", classMacrosPath);
        }

        Directory.CreateDirectory(keymapDirectory);
        var lua = File.ReadAllText(classMacrosPath, Encoding.UTF8);
        var classMacros = ExtractAssignedTable(lua, "Fuyutsui.ClassMacros")
            ?? throw new InvalidDataException("classmacros.lua 中未找到 Fuyutsui.ClassMacros");

        var updated = new List<string>();
        var warnings = new List<string>();

        foreach (var (classFile, classId) in ClassFileToId)
        {
            if (classMacros.GetTable(classFile) is not { } classTable)
            {
                warnings.Add($"跳过 {classFile}: ClassMacros 中无此职业表");
                continue;
            }

            var fileName = ClassNames.GetConfigFileName(classId).ToLowerInvariant() + ".json";
            var jsonPath = Path.Combine(keymapDirectory, fileName);
            var existing = LoadExistingSpellNames(jsonPath);

            var (root, classWarnings) = CompileClassKeymap(classTable, existing, classFile);
            warnings.AddRange(classWarnings);

            File.WriteAllText(jsonPath, root.ToJsonString(WriteOptions) + Environment.NewLine, Encoding.UTF8);
            updated.Add(jsonPath);
        }

        if (updated.Count == 0)
        {
            throw new InvalidOperationException("未成功转换任何职业 keymap。");
        }

        return new UpdateResult(classMacrosPath, updated, warnings);
    }

    private static (JsonObject Root, List<string> Warnings) CompileClassKeymap(
        TableValue classTable,
        ExistingSpellNames existingSpellNames,
        string classFile)
    {
        var warnings = new List<string>();
        var dynamicTable = classTable.GetTable("dynamicSpells");
        var staticSpells = ReadArrayEntries(classTable.GetTable("staticSpells"));
        var specialSpells = ReadArrayEntries(classTable.GetTable("specialSpells"));

        if (IsSpecializedDynamicFormat(dynamicTable))
        {
            throw new InvalidDataException(
                $"{classFile} 仍包含专精动态宏；请先将 dynamicSpells 展平为职业级数组。");
        }

        var dynamicSpells = ReadArrayStrings(dynamicTable);
        return (
            CompileSlotMap(
                dynamicSpells,
                staticSpells,
                specialSpells,
                existingSpellNames,
                classFile,
                warnings),
            warnings);
    }

    private static JsonObject CompileSlotMap(
        IReadOnlyList<string> dynamicSpells,
        IReadOnlyList<MacroEntry> staticSpells,
        IReadOnlyList<MacroEntry> specialSpells,
        ExistingSpellNames existingSpellNames,
        string warningContext,
        List<string> warnings)
    {
        var dynamicSlots = dynamicSpells.Count * 30;
        var requiredSlots = (long)dynamicSpells.Count * 30 + staticSpells.Count + specialSpells.Count;
        if (requiredSlots > MacroKind.Length)
        {
            warnings.Add(
                $"{warningContext}: 槽位容量溢出，需要 {requiredSlots} 个，最多 {MacroKind.Length} 个；" +
                $"末尾 {requiredSlots - MacroKind.Length} 个槽位不会写入 keymap");
        }

        var root = new JsonObject();
        for (var i = 1; i <= MacroKind.Length; i++)
        {
            var hotkey = MacroKind[i - 1];
            var unit = 0;
            var spell = string.Empty;
            var macroCondition = string.Empty;

            if (i <= dynamicSlots)
            {
                var groupIndex = (i - 1) / 30;
                var raidIdx = ((i - 1) % 30) + 1;
                if (groupIndex < dynamicSpells.Count
                    && !string.IsNullOrWhiteSpace(dynamicSpells[groupIndex]))
                {
                    spell = dynamicSpells[groupIndex];
                    unit = raidIdx;
                }
            }
            else
            {
                var relativeIndex = i - dynamicSlots - 1;
                MacroEntry? entry = null;
                var isStaticEntry = false;
                if (relativeIndex < staticSpells.Count)
                {
                    entry = staticSpells[relativeIndex];
                    isStaticEntry = true;
                }
                else
                {
                    var specialIndex = relativeIndex - staticSpells.Count;
                    if (specialIndex < specialSpells.Count)
                    {
                        entry = specialSpells[specialIndex];
                    }
                }

                if (entry is { Body.Length: > 0 } macroEntry)
                {
                    if (isStaticEntry)
                    {
                        var parsed = ParseStaticMacro(macroEntry.Body, macroEntry.Comment);
                        unit = parsed.Unit;
                        spell = parsed.Spell;
                        macroCondition = parsed.Condition;
                    }
                    else
                    {
                        var parsed = ParseSpecialMacro(macroEntry.Body, macroEntry.Comment);
                        unit = parsed.Unit;
                        spell = parsed.Spell;
                        macroCondition = parsed.Condition;
                    }
                }

                if (isStaticEntry
                    && entry is { Body.Length: > 0 }
                    && IsWeakSpellName(spell)
                    && TryGetExistingSpellName(existingSpellNames, i, out var preserved)
                    && !string.IsNullOrWhiteSpace(preserved)
                    && !IsWeakSpellName(preserved))
                {
                    warnings.Add($"{warningContext}[{i}]: 保留原技能名「{preserved}」（宏推导为「{spell}」）");
                    spell = preserved;
                }
            }

            root[i.ToString()] = new JsonObject
            {
                ["unit"] = unit,
                ["宏条件"] = macroCondition,
                ["技能"] = spell,
                ["热键"] = hotkey
            };
        }

        return root;
    }

    private static bool IsSpecializedDynamicFormat(TableValue? dynamicTable)
    {
        if (dynamicTable is null)
        {
            return false;
        }

        return dynamicTable.GetTable("common") is not null
            || GetDynamicSpecIndexes(dynamicTable).Count > 0;
    }

    private static IReadOnlyList<int> GetDynamicSpecIndexes(TableValue? table)
    {
        if (table is null)
        {
            return [];
        }

        return table.Entries
            .Where(entry => entry.Value is TableValue)
            .Select(entry => entry.Key switch
            {
                long value when value is > 0 and <= int.MaxValue => (int?)value,
                int value when value > 0 => value,
                _ => null
            })
            .Where(index => index is not null)
            .Select(index => index!.Value)
            .Distinct()
            .OrderBy(index => index)
            .ToList();
    }

    private static bool IsWeakSpellName(string? spell)
    {
        if (string.IsNullOrWhiteSpace(spell))
        {
            return true;
        }

        return spell.StartsWith("item:", StringComparison.OrdinalIgnoreCase);
    }

    internal readonly record struct ParsedMacro(int Unit, string Spell, string Condition);

    /// <summary>
    /// 解析静态宏供 keymap 与宏列表共用。只有方括号内以 @ 开头的项属于目标。
    /// </summary>
    internal static ParsedMacro ParseStaticMacro(string raw, string? comment = null)
    {
        var target = StaticTargetRegex().Match(raw);
        var unit = target.Success
            ? ResolveUnitName(target.Groups["unit"].Value)
            : ReservedUnit.None;

        return new ParsedMacro(
            unit,
            ResolveSpellName(new MacroEntry(raw, comment)),
            ResolveConditions(raw));
    }

    /// <summary>
    /// 特殊宏不解析宏正文。技能名必须由编辑器手工填写并保存在同行注释中，
    /// keymap 固定使用无目标、无宏条件。
    /// </summary>
    internal static ParsedMacro ParseSpecialMacro(string _, string? comment = null)
        => new(ReservedUnit.None, comment?.Trim() ?? string.Empty, string.Empty);

    /// <summary>方括号中以 @ 开头的是目标，其余逗号分隔项作为只读条件摘要。</summary>
    private static string ResolveConditions(string raw)
    {
        var conditions = ConditionRegex().Matches(raw)
            .SelectMany(bracket => bracket.Value.Length < 2
                ? []
                : bracket.Value[1..^1]
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Where(item => !item.StartsWith('@'));
        return MacroConditionText.Normalize(string.Join(", ", conditions));
    }

    private static int ResolveUnitName(string raw)
    {
        var normalized = raw.Trim().TrimStart('@').ToLowerInvariant();
        if (normalized.StartsWith("party", StringComparison.Ordinal)
            && int.TryParse(normalized[5..], out var partyIndex)
            && partyIndex is >= 1 and <= 4)
        {
            // Fuyutsui 队伍槽位：player=1，party1..4=2..5。
            return partyIndex + 1;
        }

        if (normalized.StartsWith("raid", StringComparison.Ordinal)
            && int.TryParse(normalized[4..], out var raidIndex)
            && raidIndex is >= 1 and <= 30)
        {
            return raidIndex;
        }

        return normalized switch
        {
            // "player" => 1,
            // "玩家" or "31" => ReservedUnit.Player,
            "player" or "玩家" or "31" => ReservedUnit.Player,
            "target" or "目标" or "32" => ReservedUnit.Target,
            "focus" or "焦点" or "33" => ReservedUnit.Focus,
            "cursor" or "地面" or "34" => ReservedUnit.Cursor,
            "mouseover" or "鼠标" or "35" => ReservedUnit.Mouseover,
            _ => ReservedUnit.None
        };
    }

    /// <summary>同行 `--` 注释优先作为技能名；否则从宏文本推导。</summary>
    private static string ResolveSpellName(MacroEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Comment))
        {
            return entry.Comment.Trim();
        }

        return DeriveSpellName(entry.Body);
    }

    private readonly record struct MacroEntry(string Body, string? Comment);

    internal static string DeriveSpellName(string raw)
    {
        var text = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (text.Length == 0)
        {
            return string.Empty;
        }

        if (StopCastingRegex().IsMatch(text))
        {
            return "停止施法";
        }

        var castSequence = CastSequenceRegex().Match(text);
        if (castSequence.Success)
        {
            var sequenceBody = castSequence.Groups[1].Value.Trim();
            sequenceBody = ResetOptionRegex().Replace(sequenceBody, string.Empty).Trim();
            foreach (var rawPart in SplitTopLevel(sequenceBody, ','))
            {
                var part = rawPart.Trim();
                if (part.Length == 0)
                {
                    continue;
                }

                if (part.Equals("x", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return StripConditions(part);
            }
        }

        // cancelaura 后再 /cast：取最后一个 /cast 段；纯物品宏保留首行 item:
        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > 0 && lines[0].StartsWith("item:", StringComparison.OrdinalIgnoreCase))
        {
            return lines[0].Trim();
        }

        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i];
            if (line.StartsWith("/cast", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("/castsequence", StringComparison.OrdinalIgnoreCase))
            {
                text = line["/cast".Length..].TrimStart();
                break;
            }

            if (i == 0 && !line.StartsWith('/'))
            {
                text = line;
            }
        }

        if (text.StartsWith("/cast", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("/castsequence", StringComparison.OrdinalIgnoreCase))
        {
            text = text["/cast".Length..].TrimStart();
        }

        // 取 ; 分支中第一段（专精/条件分支）
        var firstBranch = text.Split(';', 2, StringSplitOptions.TrimEntries)[0];
        var spell = StripConditions(firstBranch);

        if (string.IsNullOrWhiteSpace(spell))
        {
            return string.Empty;
        }

        return spell;
    }

    /// <summary>按顶层分隔符切分，方括号内的逗号不作为技能分隔符。</summary>
    private static IEnumerable<string> SplitTopLevel(string text, char separator)
    {
        var start = 0;
        var bracketDepth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '[':
                    bracketDepth++;
                    break;
                case ']' when bracketDepth > 0:
                    bracketDepth--;
                    break;
                default:
                    if (text[i] == separator && bracketDepth == 0)
                    {
                        yield return text[start..i];
                        start = i + 1;
                    }

                    break;
            }
        }

        yield return text[start..];
    }

    private static string StripConditions(string text)
    {
        var stripped = ConditionRegex().Replace(text, string.Empty).Trim();
        return stripped;
    }

    private static List<string> ReadArrayStrings(TableValue? table)
    {
        var result = new List<string>();
        if (table is null)
        {
            return result;
        }

        foreach (var item in table.IPairs())
        {
            if (item is StringValue s)
            {
                result.Add(s.Value.Trim());
            }
            else
            {
                break;
            }
        }

        return result;
    }

    private static List<MacroEntry> ReadArrayEntries(TableValue? table)
    {
        var result = new List<MacroEntry>();
        if (table is null)
        {
            return result;
        }

        var index = 1;
        foreach (var value in table.IPairs())
        {
            if (value is not StringValue s)
            {
                break;
            }

            result.Add(new MacroEntry(s.Value, table.GetTrailingComment((long)index)));
            index++;
        }

        return result;
    }

    private sealed record ExistingSpellNames(
        IReadOnlyDictionary<int, string> Fallback)
    {
        public static readonly ExistingSpellNames Empty = new(new Dictionary<int, string>());
    }

    private static ExistingSpellNames LoadExistingSpellNames(string jsonPath)
    {
        if (!File.Exists(jsonPath))
        {
            return ExistingSpellNames.Empty;
        }

        try
        {
            if (JsonNode.Parse(File.ReadAllText(jsonPath)) is not JsonObject root)
            {
                return ExistingSpellNames.Empty;
            }

            var fallback = ReadExistingSpellNames(root);
            return new ExistingSpellNames(fallback);
        }
        catch
        {
            // 旧 keymap 损坏时忽略，按宏全量重建。
            return ExistingSpellNames.Empty;
        }
    }

    private static IReadOnlyDictionary<int, string> ReadExistingSpellNames(JsonObject map)
    {
        var result = new Dictionary<int, string>();
        foreach (var (key, node) in map)
        {
            if (!int.TryParse(key, out var id) || node is not JsonObject entry)
            {
                continue;
            }

            var spell = JsonHelpers.GetString(JsonHelpers.Get(entry, "技能"))
                ?? JsonHelpers.GetString(JsonHelpers.Get(entry, "spell"));
            if (!string.IsNullOrWhiteSpace(spell))
            {
                result[id] = spell;
            }
        }

        return result;
    }

    private static bool TryGetExistingSpellName(
        ExistingSpellNames existingSpellNames,
        int slot,
        out string spell)
        => existingSpellNames.Fallback.TryGetValue(slot, out spell!);

    private static string[] BuildMacroKind()
    {
        var list = new string[MacroSlotCapacity];
        var i = 0;
        foreach (var modifier in Modifiers)
        {
            foreach (var key in Keys)
            {
                list[i++] = $"{modifier}-{key}";
            }
        }

        return list;
    }

    [GeneratedRegex(@"^\s*/stopcasting\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StopCastingRegex();

    [GeneratedRegex(@"^\s*/castsequence\b\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex CastSequenceRegex();

    [GeneratedRegex(@"\breset\s*=\s*\S+\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ResetOptionRegex();

    [GeneratedRegex(@"\[[^\]]*\]", RegexOptions.CultureInvariant)]
    private static partial Regex ConditionRegex();

    [GeneratedRegex(@"\[[^\]]*@(?<unit>cursor|target|focus|player|mouseover|party[1-4]|raid(?:[1-9]|[12][0-9]|30))\b[^\]]*\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StaticTargetRegex();

}
