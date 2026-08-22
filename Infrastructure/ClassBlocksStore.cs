using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using static Shigure.LuaLiteParser;

namespace Shigure;

/// <summary>
/// 读写 Fuyutsui class/*.lua 中的 ClassBlocks（states/auras/spells/group），
/// 同时读写 spellsList；保存时替换 ClassBlocks 表字面量，并原位更新 spellsList 中已编辑的条目。
/// </summary>
internal static class ClassBlocksStore
{
    public const string AssignmentName = "Fuyutsui.ClassBlocks";
    public const string SpellsListAssignmentName = "Fuyutsui.spellsList";
    private static readonly string[] StateCategories =
    [
        ClassStateCatalog.CategoryState,
        ClassStateCatalog.CategoryResource,
        ClassStateCatalog.CategoryItem,
        ClassStateCatalog.CategoryConfig,
        ClassStateCatalog.CategoryTarget,
        ClassStateCatalog.CategoryFocus
    ];

    public sealed class ClassFileDocument
    {
        public string FilePath { get; set; } = string.Empty;
        public string SourceText { get; set; } = string.Empty;
        public int TableStart { get; set; }
        public int TableEndExclusive { get; set; }
        public Dictionary<int, SpecBlocks> Specs { get; set; } = new();
        public List<SpellsListEntry> SpellsList { get; set; } = new();
        public bool IsModernFormat { get; set; }
    }

    public sealed class SpellsListEntry
    {
        public long SpellId { get; set; }
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public long OriginalSpellId { get; set; }
        public int OriginalIndex { get; set; }
        public string OriginalName { get; set; } = string.Empty;
    }

    public sealed class SpecBlocks
    {
        public bool NestedStates { get; set; } = true;
        public List<string> FlatStates { get; } = new();
        public Dictionary<string, List<string>> CategorizedStates { get; } = new(StringComparer.Ordinal)
        {
            [ClassStateCatalog.CategoryState] = new List<string>(),
            [ClassStateCatalog.CategoryResource] = new List<string>(),
            [ClassStateCatalog.CategoryItem] = new List<string>(),
            [ClassStateCatalog.CategoryConfig] = new List<string>(),
            [ClassStateCatalog.CategoryTarget] = new List<string>(),
            [ClassStateCatalog.CategoryFocus] = new List<string>()
        };

        public List<AuraEntry> PlayerAuras { get; } = new();
        public List<AuraEntry> TargetHarmfulAuras { get; } = new();
        public List<AuraEntry> TargetHelpfulAuras { get; } = new();
        public List<AuraEntry> FocusHarmfulAuras { get; } = new();
        public List<AuraEntry> FocusHelpfulAuras { get; } = new();
        public List<SpellEntry> Spells { get; } = new();
        public GroupBlocks? Group { get; set; }
    }

    public sealed class AuraEntry
    {
        public string Name { get; set; } = string.Empty;
        public long? SpellId { get; set; }
        public List<long> SpellIds { get; } = new();
        public int? MaxApps { get; set; }
        public string? Filter { get; set; }
    }

    public sealed class SpellEntry
    {
        public string Name { get; set; } = string.Empty;
        public long SpellId { get; set; }
        public bool Charge { get; set; }
        public int? MaxCharge { get; set; }
        public int? CastCount { get; set; }
        public bool ForcedKnown { get; set; }
        public bool InSpellBook { get; set; }
    }

    public sealed class GroupBlocks
    {
        public int Num { get; set; } = 5;
        public int? HealthPercent { get; set; } = 1;
        public int? Role { get; set; } = 2;
        public int? Dispel { get; set; }
        public List<GroupAuraEntry> Auras { get; } = new();
    }

    public sealed class GroupAuraEntry
    {
        public int Offset { get; set; }
        public string Name { get; set; } = string.Empty;
        public long? SpellId { get; set; }
        public List<long> SpellIds { get; } = new();
    }

    public static ClassFileDocument Load(string filePath)
    {
        var source = File.ReadAllText(filePath, Encoding.UTF8);
        if (!TryExtractAssignedTable(source, AssignmentName, out var table, out var start, out var end))
        {
            throw new InvalidDataException($"{Path.GetFileName(filePath)} 中未找到 {AssignmentName}");
        }

        var specs = new Dictionary<int, SpecBlocks>();
        var modern = false;
        foreach (var (key, value) in table.Entries)
        {
            if (key is not long specId || value is not TableValue specTable)
            {
                continue;
            }

            var spec = ParseSpec(specTable, out var specModern);
            modern |= specModern;
            specs[(int)specId] = spec;
        }

        var spellsList = ParseSpellsList(ExtractAssignedTable(source, SpellsListAssignmentName));
        return new ClassFileDocument
        {
            FilePath = filePath,
            SourceText = source,
            TableStart = start,
            TableEndExclusive = end,
            Specs = specs,
            SpellsList = spellsList,
            IsModernFormat = modern
        };
    }

    private static List<SpellsListEntry> ParseSpellsList(TableValue? table)
    {
        var result = new List<SpellsListEntry>();
        if (table is null)
        {
            return result;
        }

        foreach (var (key, value) in table.Entries)
        {
            if (key is not long spellId || value is not TableValue spell)
            {
                continue;
            }

            var indexValue = spell.GetNumber("index");
            var name = spell.GetString("name")?.Trim();
            if (indexValue is null
                || indexValue.Value <= 0
                || indexValue.Value > int.MaxValue
                || indexValue.Value != Math.Truncate(indexValue.Value)
                || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            result.Add(new SpellsListEntry
            {
                SpellId = spellId,
                Index = (int)indexValue.Value,
                Name = name,
                OriginalSpellId = spellId,
                OriginalIndex = (int)indexValue.Value,
                OriginalName = name
            });
        }

        return result;
    }

    public static void Save(ClassFileDocument document)
    {
        if (!document.IsModernFormat)
        {
            throw new InvalidOperationException("当前文件仍是旧版稀疏索引 ClassBlocks，无法用图形编辑器保存。");
        }

        var updated = UpdateSpellsListEntries(document.SourceText, document.SpellsList);
        if (!TryExtractAssignedTable(updated, AssignmentName, out _, out var classBlocksStart, out var classBlocksEnd))
        {
            throw new InvalidOperationException("保存前无法重新定位 ClassBlocks 表。");
        }

        var serialized = SerializeClassBlocks(document.Specs);
        updated = updated[..classBlocksStart]
            + serialized
            + updated[classBlocksEnd..];
        AtomicFile.WriteAllText(document.FilePath, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (!TryExtractAssignedTable(updated, AssignmentName, out _, out var start, out var end))
        {
            throw new InvalidOperationException("保存后无法重新定位 ClassBlocks 表。");
        }

        document.SourceText = updated;
        document.TableStart = start;
        document.TableEndExclusive = end;
        foreach (var spell in document.SpellsList)
        {
            spell.OriginalSpellId = spell.SpellId;
            spell.OriginalIndex = spell.Index;
            spell.OriginalName = spell.Name;
        }
    }

    private static string UpdateSpellsListEntries(string source, IReadOnlyList<SpellsListEntry> entries)
    {
        var newEntries = entries.Where(entry => entry.OriginalSpellId == 0).ToArray();
        var changedEntries = entries
            .Where(entry => entry.OriginalSpellId != 0
                && (entry.SpellId != entry.OriginalSpellId
                || entry.Index != entry.OriginalIndex
                || !string.Equals(entry.Name, entry.OriginalName, StringComparison.Ordinal)))
            .ToDictionary(entry => entry.OriginalSpellId);
        if (changedEntries.Count == 0 && newEntries.Length == 0)
        {
            return source;
        }

        if (!TryExtractAssignedTable(source, SpellsListAssignmentName, out _, out var tableStart, out var tableEnd))
        {
            throw new InvalidOperationException($"当前文件中未找到 {SpellsListAssignmentName}，无法保存技能列表。");
        }

        var tableText = source[tableStart..tableEnd];
        var updatedOriginalIds = new HashSet<long>();
        var pattern = new Regex(
            """^(?<prefix>[ \t]*\[[ \t]*)(?<spellId>\d+)(?<beforeIndex>[ \t]*\][ \t]*=[ \t]*\{[ \t]*index[ \t]*=[ \t]*)(?<index>\d+)(?<beforeName>[ \t]*,[ \t]*name[ \t]*=[ \t]*)(?<name>"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*')(?<suffix>[^\n]*)$""",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        var updatedTable = pattern.Replace(tableText, match =>
        {
            if (!long.TryParse(match.Groups["spellId"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var originalSpellId)
                || !changedEntries.TryGetValue(originalSpellId, out var entry))
            {
                return match.Value;
            }

            updatedOriginalIds.Add(originalSpellId);
            var quotedName = match.Groups["name"].Value;
            var quote = quotedName[0];
            return match.Groups["prefix"].Value
                + entry.SpellId.ToString(CultureInfo.InvariantCulture)
                + match.Groups["beforeIndex"].Value
                + entry.Index.ToString(CultureInfo.InvariantCulture)
                + match.Groups["beforeName"].Value
                + quote
                + EscapeLuaString(entry.Name, quote)
                + quote
                + match.Groups["suffix"].Value;
        });

        var missing = changedEntries.Keys.Where(id => !updatedOriginalIds.Contains(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"无法在 {SpellsListAssignmentName} 中定位法术 ID {string.Join(", ", missing)} 的原始条目。");
        }

        if (newEntries.Length > 0)
        {
            var newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var closingBraceIndex = updatedTable.Length - 1;
            var closingLineBreak = updatedTable.LastIndexOf('\n', Math.Max(0, closingBraceIndex - 1));
            var insertionIndex = closingLineBreak >= 0 ? closingLineBreak + 1 : closingBraceIndex;
            var insertion = new StringBuilder();
            if (closingLineBreak < 0)
            {
                insertion.Append(newline);
            }

            foreach (var entry in newEntries.OrderBy(entry => entry.Index))
            {
                insertion.Append("    [")
                    .Append(entry.SpellId.ToString(CultureInfo.InvariantCulture))
                    .Append("] = { index = ")
                    .Append(entry.Index.ToString(CultureInfo.InvariantCulture))
                    .Append(", name = \"")
                    .Append(EscapeLuaString(entry.Name, '"'))
                    .Append("\" },")
                    .Append(newline);
            }

            updatedTable = updatedTable.Insert(insertionIndex, insertion.ToString());
        }

        return source[..tableStart] + updatedTable + source[tableEnd..];
    }

    private static string EscapeLuaString(string value, char quote)
    {
        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
        return quote == '\''
            ? escaped.Replace("'", "\\'", StringComparison.Ordinal)
            : escaped.Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static SpecBlocks ParseSpec(TableValue spec, out bool isModern)
    {
        var result = new SpecBlocks();
        isModern = spec.GetTable("states") is not null
            || spec.GetTable("auras") is not null
            || spec.GetTable("spells") is not null
            || spec.GetTable("group") is not null;

        if (!isModern)
        {
            return result;
        }

        if (spec.GetTable("states") is { } states)
        {
            var nested = StateCategories.Any(category => states.GetTable(category) is not null);
            result.NestedStates = nested;
            if (nested)
            {
                foreach (var category in StateCategories)
                {
                    if (states.GetTable(category) is not { } list)
                    {
                        continue;
                    }

                    var target = result.CategorizedStates[category];
                    foreach (var item in list.IPairs())
                    {
                        if (item is StringValue name && !string.IsNullOrWhiteSpace(name.Value))
                        {
                            target.Add(name.Value);
                        }
                    }
                }
            }
            else
            {
                foreach (var item in states.IPairs())
                {
                    if (item is StringValue name && !string.IsNullOrWhiteSpace(name.Value))
                    {
                        result.FlatStates.Add(name.Value);
                    }
                }
            }
        }

        if (spec.GetTable("auras") is { } auras)
        {
            var nested = auras.GetTable("player") is not null
                || auras.GetTable("target") is not null
                || auras.GetTable("focus") is not null;
            if (nested)
            {
                AppendAuraList(auras.GetTable("player"), result.PlayerAuras);
                if (auras.GetTable("target") is { } target)
                {
                    AppendAuraList(target.GetTable("harmful"), result.TargetHarmfulAuras);
                    AppendAuraList(target.GetTable("helpful"), result.TargetHelpfulAuras);
                }

                if (auras.GetTable("focus") is { } focus)
                {
                    AppendAuraList(focus.GetTable("harmful"), result.FocusHarmfulAuras);
                    AppendAuraList(focus.GetTable("helpful"), result.FocusHelpfulAuras);
                }
            }
            else
            {
                AppendAuraList(auras, result.PlayerAuras);
            }
        }

        if (spec.GetTable("spells") is { } spells)
        {
            foreach (var item in spells.IPairs())
            {
                if (item is not TableValue spell)
                {
                    continue;
                }

                var spellId = spell.GetNumber("spellId");
                if (spellId is null)
                {
                    continue;
                }

                result.Spells.Add(new SpellEntry
                {
                    SpellId = (long)spellId.Value,
                    Name = spell.GetString("name")?.Trim() ?? string.Empty,
                    Charge = spell.GetBool("charge") == true,
                    MaxCharge = spell.GetNumber("maxCharge") is { } maxCharge ? (int)maxCharge : null,
                    CastCount = spell.GetNumber("castCount") is { } castCount ? (int)castCount : null,
                    ForcedKnown = spell.GetBool("forcedKnown") == true,
                    InSpellBook = spell.GetBool("inSpellBook") == true
                });
            }
        }

        if (spec.GetTable("group") is { } group)
        {
            var groupBlocks = new GroupBlocks
            {
                Num = (int)(group.GetNumber("num") ?? 5),
                HealthPercent = group.GetNumber("healthPercent") is { } hp ? (int)hp : null,
                Role = group.GetNumber("role") is { } role ? (int)role : null,
                Dispel = group.GetNumber("dispel") is { } dispel ? (int)dispel : null
            };

            if (group.GetTable("aura") is { } auraOffsets)
            {
                foreach (var (key, value) in auraOffsets.Entries)
                {
                    if (key is not long offset || value is not TableValue auraInfo)
                    {
                        continue;
                    }

                    var entry = new GroupAuraEntry
                    {
                        Offset = (int)offset,
                        Name = auraInfo.GetString("name")?.Trim() ?? string.Empty
                    };
                    if (auraInfo.GetNumber("spellId") is { } sid)
                    {
                        entry.SpellId = (long)sid;
                    }

                    if (auraInfo.Get("spellIds") is TableValue ids)
                    {
                        foreach (var idItem in ids.IPairs())
                        {
                            if (idItem is NumberValue n)
                            {
                                entry.SpellIds.Add(n.AsInt());
                            }
                        }
                    }

                    groupBlocks.Auras.Add(entry);
                }

                groupBlocks.Auras.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            }

            result.Group = groupBlocks;
        }

        return result;
    }

    private static void AppendAuraList(TableValue? list, List<AuraEntry> target)
    {
        if (list is null)
        {
            return;
        }

        foreach (var item in list.IPairs())
        {
            if (item is not TableValue aura)
            {
                continue;
            }

            var entry = new AuraEntry
            {
                Name = aura.GetString("name")?.Trim() ?? string.Empty,
                MaxApps = aura.GetNumber("maxApps") is { } maxApps ? (int)maxApps : null,
                Filter = aura.GetString("filter")?.Trim()
            };
            if (aura.GetNumber("spellId") is { } sid)
            {
                entry.SpellId = (long)sid;
            }

            if (aura.Get("spellIds") is TableValue ids)
            {
                foreach (var idItem in ids.IPairs())
                {
                    if (idItem is NumberValue n)
                    {
                        entry.SpellIds.Add(n.AsInt());
                    }
                }
            }

            target.Add(entry);
        }
    }

    public static string SerializeClassBlocks(IReadOnlyDictionary<int, SpecBlocks> specs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        foreach (var specId in specs.Keys.OrderBy(x => x))
        {
            sb.Append("    [").Append(specId).AppendLine("] = {");
            WriteSpec(sb, specs[specId], "        ");
            sb.AppendLine("    },");
        }

        sb.Append('}');
        return sb.ToString();
    }

    private static void WriteSpec(StringBuilder sb, SpecBlocks spec, string indent)
    {
        // states
        sb.Append(indent).AppendLine("states = {");
        if (spec.NestedStates)
        {
            foreach (var category in StateCategories)
            {
                var list = spec.CategorizedStates.GetValueOrDefault(category);
                if (list is null || list.Count == 0)
                {
                    continue;
                }

                sb.Append(indent).Append("    [\"").Append(Escape(category)).AppendLine("\"] = {");
                foreach (var name in list)
                {
                    sb.Append(indent).Append("        \"").Append(Escape(name)).AppendLine("\",");
                }

                sb.Append(indent).AppendLine("    },");
            }
        }
        else
        {
            foreach (var name in spec.FlatStates)
            {
                sb.Append(indent).Append("    \"").Append(Escape(name)).AppendLine("\",");
            }
        }

        sb.Append(indent).AppendLine("},");

        // auras
        var hasAuras = spec.PlayerAuras.Count > 0
            || spec.TargetHarmfulAuras.Count > 0
            || spec.TargetHelpfulAuras.Count > 0
            || spec.FocusHarmfulAuras.Count > 0
            || spec.FocusHelpfulAuras.Count > 0;
        if (hasAuras)
        {
            sb.Append(indent).AppendLine("auras = {");
            WriteAuraUnit(sb, "player", null, spec.PlayerAuras, indent + "    ");
            WriteAuraSplitUnit(sb, "target", spec.TargetHarmfulAuras, spec.TargetHelpfulAuras, indent + "    ");
            WriteAuraSplitUnit(sb, "focus", spec.FocusHarmfulAuras, spec.FocusHelpfulAuras, indent + "    ");
            sb.Append(indent).AppendLine("},");
        }

        // spells
        if (spec.Spells.Count > 0)
        {
            sb.Append(indent).AppendLine("spells = {");
            foreach (var spell in spec.Spells)
            {
                sb.Append(indent).Append("    { spellId = ").Append(spell.SpellId);
                if (!string.IsNullOrWhiteSpace(spell.Name))
                {
                    sb.Append(", name = \"").Append(Escape(spell.Name)).Append('"');
                }

                if (spell.Charge)
                {
                    sb.Append(", charge = true");
                }

                if (spell.MaxCharge is { } maxCharge)
                {
                    sb.Append(", maxCharge = ").Append(maxCharge);
                }

                if (spell.CastCount is { } castCount)
                {
                    sb.Append(", castCount = ").Append(castCount);
                }

                if (spell.ForcedKnown)
                {
                    sb.Append(", forcedKnown = true");
                }

                if (spell.InSpellBook)
                {
                    sb.Append(", inSpellBook = true");
                }

                sb.AppendLine(" },");
            }

            sb.Append(indent).AppendLine("},");
        }

        // group
        if (spec.Group is { } group)
        {
            sb.Append(indent).AppendLine("group = {");
            sb.Append(indent).Append("    num = ").Append(group.Num).AppendLine(",");
            if (group.HealthPercent is { } hp)
            {
                sb.Append(indent).Append("    healthPercent = ").Append(hp).AppendLine(",");
            }

            if (group.Role is { } role)
            {
                sb.Append(indent).Append("    role = ").Append(role).AppendLine(",");
            }

            if (group.Dispel is { } dispel)
            {
                sb.Append(indent).Append("    dispel = ").Append(dispel).AppendLine(",");
            }

            if (group.Auras.Count > 0)
            {
                sb.Append(indent).AppendLine("    aura = {");
                foreach (var aura in group.Auras.OrderBy(a => a.Offset))
                {
                    sb.Append(indent).Append("        [").Append(aura.Offset).Append("] = {");
                    if (!string.IsNullOrWhiteSpace(aura.Name))
                    {
                        sb.Append(" name = \"").Append(Escape(aura.Name)).Append("\",");
                    }

                    WriteSpellIdFields(sb, aura.SpellId, aura.SpellIds);
                    sb.AppendLine(" },");
                }

                sb.Append(indent).AppendLine("    },");
            }

            sb.Append(indent).AppendLine("},");
        }
    }

    private static void WriteAuraUnit(StringBuilder sb, string unit, string? filter, List<AuraEntry> list, string indent)
    {
        if (list.Count == 0)
        {
            return;
        }

        if (filter is null)
        {
            sb.Append(indent).Append(unit).AppendLine(" = {");
            foreach (var aura in list)
            {
                WriteAuraEntry(sb, aura, indent + "    ");
            }

            sb.Append(indent).AppendLine("},");
        }
    }

    private static void WriteAuraSplitUnit(
        StringBuilder sb,
        string unit,
        List<AuraEntry> harmful,
        List<AuraEntry> helpful,
        string indent)
    {
        if (harmful.Count == 0 && helpful.Count == 0)
        {
            return;
        }

        sb.Append(indent).Append(unit).AppendLine(" = {");
        if (harmful.Count > 0)
        {
            sb.Append(indent).AppendLine("    harmful = {");
            foreach (var aura in harmful)
            {
                WriteAuraEntry(sb, aura, indent + "        ");
            }

            sb.Append(indent).AppendLine("    },");
        }

        if (helpful.Count > 0)
        {
            sb.Append(indent).AppendLine("    helpful = {");
            foreach (var aura in helpful)
            {
                WriteAuraEntry(sb, aura, indent + "        ");
            }

            sb.Append(indent).AppendLine("    },");
        }

        sb.Append(indent).AppendLine("},");
    }

    private static void WriteAuraEntry(StringBuilder sb, AuraEntry aura, string indent)
    {
        sb.Append(indent).Append("{");
        if (!string.IsNullOrWhiteSpace(aura.Name))
        {
            sb.Append(" name = \"").Append(Escape(aura.Name)).Append("\",");
        }

        WriteSpellIdFields(sb, aura.SpellId, aura.SpellIds);
        if (aura.MaxApps is { } maxApps)
        {
            sb.Append(" maxApps = ").Append(maxApps).Append(',');
        }
        if (!string.IsNullOrWhiteSpace(aura.Filter))
        {
            sb.Append(" filter = \"").Append(Escape(aura.Filter)).Append("\",");
        }

        sb.AppendLine(" },");
    }

    private static void WriteSpellIdFields(StringBuilder sb, long? spellId, List<long> spellIds)
    {
        if (spellIds.Count > 0)
        {
            sb.Append(" spellIds = { ");
            sb.Append(string.Join(", ", spellIds.Select(id => id.ToString(CultureInfo.InvariantCulture))));
            sb.Append(" },");
        }
        else if (spellId is { } id)
        {
            sb.Append(" spellId = ").Append(id).Append(',');
        }
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
