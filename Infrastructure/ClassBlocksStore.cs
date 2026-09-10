using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using static Shigure.LuaLiteParser;

namespace Shigure;

/// <summary>
/// 读写 Fuyutsui class/*.lua 中的 ClassBlocks（states/auras/spells/items/group/nameplates），
/// 同时读写 spellsList 与 itemsList；保存时替换 ClassBlocks 表字面量，并原位更新列表条目。
/// </summary>
internal static class ClassBlocksStore
{
    public const string AssignmentName = "Fuyutsui.ClassBlocks";
    public const string SpellsListAssignmentName = "Fuyutsui.spellsList";
    public const string ItemsListAssignmentName = "Fuyutsui.itemsList";
    private static readonly string[] StateCategories =
    [
        ClassStateCatalog.CategoryState,
        ClassStateCatalog.CategorySpecial,
        ClassStateCatalog.CategoryResource,
        ClassStateCatalog.CategoryConfig,
        ClassStateCatalog.CategoryTarget,
        ClassStateCatalog.CategoryFocus,
        ClassStateCatalog.CategoryMouseover,
        ClassStateCatalog.CategoryPet,
        ClassStateCatalog.CategoryBoss1,
        ClassStateCatalog.CategoryBoss2,
        ClassStateCatalog.CategoryBoss3,
        ClassStateCatalog.CategoryBoss4,
        ClassStateCatalog.CategoryBoss5
    ];

    public sealed class ClassFileDocument
    {
        public string FilePath { get; set; } = string.Empty;
        public string SourceText { get; set; } = string.Empty;
        public int TableStart { get; set; }
        public int TableEndExclusive { get; set; }
        public Dictionary<int, SpecBlocks> Specs { get; set; } = new();
        public List<SpellsListEntry> SpellsList { get; set; } = new();
        public HashSet<long> DeletedSpellsListOriginalIds { get; } = new();
        public List<ItemsListEntry> ItemsList { get; set; } = new();
        public HashSet<long> DeletedItemsListOriginalIds { get; } = new();
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

    public sealed class ItemsListEntry
    {
        public long ItemId { get; set; }
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public long OriginalItemId { get; set; }
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
            [ClassStateCatalog.CategorySpecial] = new List<string>(),
            [ClassStateCatalog.CategoryResource] = new List<string>(),
            [ClassStateCatalog.CategoryConfig] = new List<string>(),
            [ClassStateCatalog.CategoryTarget] = new List<string>(),
            [ClassStateCatalog.CategoryFocus] = new List<string>(),
            [ClassStateCatalog.CategoryMouseover] = new List<string>(),
            [ClassStateCatalog.CategoryPet] = new List<string>(),
            [ClassStateCatalog.CategoryBoss1] = new List<string>(),
            [ClassStateCatalog.CategoryBoss2] = new List<string>(),
            [ClassStateCatalog.CategoryBoss3] = new List<string>(),
            [ClassStateCatalog.CategoryBoss4] = new List<string>(),
            [ClassStateCatalog.CategoryBoss5] = new List<string>()
        };

        public List<AuraEntry> PlayerAuras { get; } = new();
        public List<ItemEntry> Items { get; } = new();
        public List<AuraEntry> TargetHarmfulAuras { get; } = new();
        public List<AuraEntry> TargetHelpfulAuras { get; } = new();
        public List<AuraEntry> FocusHarmfulAuras { get; } = new();
        public List<AuraEntry> FocusHelpfulAuras { get; } = new();
        public List<SpellEntry> Spells { get; } = new();
        public GroupBlocks? Group { get; set; }
        public NameplateBlocks? Nameplates { get; set; }
    }

    public sealed class AuraEntry
    {
        public string Name { get; set; } = string.Empty;
        public long? SpellId { get; set; }
        public List<long> SpellIds { get; } = new();
        public int? MaxApps { get; set; }
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
        public List<string> State { get; } = new();
        public List<GroupAuraEntry> Auras { get; } = new();
    }

    public sealed class GroupAuraEntry
    {
        public string Name { get; set; } = string.Empty;
        public long? SpellId { get; set; }
        public List<long> SpellIds { get; } = new();
    }

    public sealed class NameplateBlocks
    {
        public List<string> State { get; } = new();
        public List<AuraEntry> Auras { get; } = new();
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
        var itemsList = ParseItemsList(ExtractAssignedTable(source, ItemsListAssignmentName));
        return new ClassFileDocument
        {
            FilePath = filePath,
            SourceText = source,
            TableStart = start,
            TableEndExclusive = end,
            Specs = specs,
            SpellsList = spellsList,
            ItemsList = itemsList,
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

    private static List<ItemsListEntry> ParseItemsList(TableValue? table)
    {
        var result = new List<ItemsListEntry>();
        if (table is null)
        {
            return result;
        }

        foreach (var (key, value) in table.Entries)
        {
            if (key is not long itemId || itemId <= 0)
            {
                continue;
            }

            int index = 0;
            string? name = null;
            switch (value)
            {
                case StringValue text:
                    name = text.Value.Trim();
                    break;
                case TableValue item:
                    name = item.GetString("name")?.Trim();
                    var indexValue = item.GetNumber("index");
                    if (indexValue is { } number
                        && number > 0
                        && number <= int.MaxValue
                        && number == Math.Truncate(number))
                    {
                        index = (int)number;
                    }

                    break;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            result.Add(new ItemsListEntry
            {
                ItemId = itemId,
                Index = index,
                Name = name,
                OriginalItemId = itemId,
                OriginalIndex = index,
                OriginalName = name
            });
        }

        AssignMissingItemsListIndices(result);
        return result;
    }

    private static void AssignMissingItemsListIndices(List<ItemsListEntry> entries)
    {
        var used = entries.Where(entry => entry.Index > 0).Select(entry => entry.Index).ToHashSet();
        var next = 1;
        foreach (var entry in entries.Where(entry => entry.Index <= 0))
        {
            while (used.Contains(next))
            {
                next++;
            }

            entry.Index = next;
            used.Add(next);
            next++;
        }
    }

    public static void Save(ClassFileDocument document)
    {
        if (!document.IsModernFormat)
        {
            throw new InvalidOperationException("当前文件仍是旧版稀疏索引 ClassBlocks，无法用图形编辑器保存。");
        }

        var updated = UpdateSpellsListEntries(
            document.SourceText,
            document.SpellsList,
            document.DeletedSpellsListOriginalIds);
        updated = UpdateItemsListEntries(
            updated,
            document.ItemsList,
            document.DeletedItemsListOriginalIds);
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

        document.DeletedSpellsListOriginalIds.Clear();
        foreach (var item in document.ItemsList)
        {
            item.OriginalItemId = item.ItemId;
            item.OriginalIndex = item.Index;
            item.OriginalName = item.Name;
        }

        document.DeletedItemsListOriginalIds.Clear();
    }

    public sealed class ItemEntry
    {
        public long? ItemId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsEquipped { get; set; }
    }

    private static string UpdateSpellsListEntries(
        string source,
        IReadOnlyList<SpellsListEntry> entries,
        IReadOnlySet<long> deletedOriginalIds)
    {
        var newEntries = entries.Where(entry => entry.OriginalSpellId == 0).ToArray();
        var changedEntries = entries
            .Where(entry => entry.OriginalSpellId != 0
                && (entry.SpellId != entry.OriginalSpellId
                || entry.Index != entry.OriginalIndex
                || !string.Equals(entry.Name, entry.OriginalName, StringComparison.Ordinal)))
            .ToDictionary(entry => entry.OriginalSpellId);
        if (changedEntries.Count == 0 && newEntries.Length == 0 && deletedOriginalIds.Count == 0)
        {
            return source;
        }

        if (!TryExtractAssignedTable(source, SpellsListAssignmentName, out _, out var tableStart, out var tableEnd))
        {
            throw new InvalidOperationException($"当前文件中未找到 {SpellsListAssignmentName}，无法保存技能列表。");
        }

        var tableText = source[tableStart..tableEnd];
        var updatedOriginalIds = new HashSet<long>();
        var deletedIdsFound = new HashSet<long>();
        var pattern = new Regex(
            """^(?<prefix>[ \t]*\[[ \t]*)(?<spellId>\d+)(?<beforeIndex>[ \t]*\][ \t]*=[ \t]*\{[ \t]*index[ \t]*=[ \t]*)(?<index>\d+)(?<beforeName>[ \t]*,[ \t]*name[ \t]*=[ \t]*)(?<name>"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*')(?<suffix>[^\r\n]*)(?<lineEnd>\r?\n|$)""",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        var updatedTable = pattern.Replace(tableText, match =>
        {
            if (!long.TryParse(match.Groups["spellId"].Value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var originalSpellId))
            {
                return match.Value;
            }

            if (deletedOriginalIds.Contains(originalSpellId))
            {
                deletedIdsFound.Add(originalSpellId);
                return string.Empty;
            }

            if (!changedEntries.TryGetValue(originalSpellId, out var entry))
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
                + match.Groups["suffix"].Value
                + match.Groups["lineEnd"].Value;
        });

        var missing = changedEntries.Keys.Where(id => !updatedOriginalIds.Contains(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"无法在 {SpellsListAssignmentName} 中定位法术 ID {string.Join(", ", missing)} 的原始条目。");
        }

        var missingDeleted = deletedOriginalIds.Where(id => !deletedIdsFound.Contains(id)).ToArray();
        if (missingDeleted.Length > 0)
        {
            throw new InvalidOperationException(
                $"无法在 {SpellsListAssignmentName} 中定位待删除的法术 ID {string.Join(", ", missingDeleted)}。");
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

    private static string UpdateItemsListEntries(
        string source,
        IReadOnlyList<ItemsListEntry> entries,
        IReadOnlySet<long> deletedOriginalIds)
    {
        var newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        if (!TryExtractAssignedTable(source, ItemsListAssignmentName, out _, out var tableStart, out var tableEnd))
        {
            if (!TryExtractAssignedTable(source, SpellsListAssignmentName, out _, out _, out var spellsEnd))
            {
                throw new InvalidOperationException(
                    $"当前文件中未找到 {SpellsListAssignmentName}，无法写入物品列表。");
            }

            return source[..spellsEnd]
                + newline
                + newline
                + SerializeItemsListAssignment(entries, newline)
                + source[spellsEnd..];
        }

        var newEntries = entries.Where(entry => entry.OriginalItemId == 0).ToArray();
        var changedEntries = entries
            .Where(entry => entry.OriginalItemId != 0
                && (entry.ItemId != entry.OriginalItemId
                    || entry.Index != entry.OriginalIndex
                    || !string.Equals(entry.Name, entry.OriginalName, StringComparison.Ordinal)))
            .ToDictionary(entry => entry.OriginalItemId);
        var tableText = source[tableStart..tableEnd];
        var usesIndexedObjectEntries = tableText.Contains("index =", StringComparison.Ordinal);
        if (changedEntries.Count == 0
            && newEntries.Length == 0
            && deletedOriginalIds.Count == 0
            && (entries.Count == 0 || usesIndexedObjectEntries))
        {
            return source;
        }

        return source[..tableStart]
            + SerializeItemsListTableLiteral(entries, newline)
            + source[tableEnd..];
    }

    private static string SerializeItemsListAssignment(IReadOnlyList<ItemsListEntry> entries, string newline)
        => ItemsListAssignmentName + " = " + SerializeItemsListTableLiteral(entries, newline);

    private static string SerializeItemsListTableLiteral(IReadOnlyList<ItemsListEntry> entries, string newline)
    {
        if (entries.Count == 0)
        {
            return "{}";
        }

        var builder = new StringBuilder();
        builder.Append('{').Append(newline);
        foreach (var entry in entries.OrderBy(item => item.Index).ThenBy(item => item.ItemId))
        {
            builder.Append("    [")
                .Append(entry.ItemId.ToString(CultureInfo.InvariantCulture))
                .Append("] = { index = ")
                .Append(entry.Index.ToString(CultureInfo.InvariantCulture))
                .Append(", name = \"")
                .Append(EscapeLuaString(entry.Name, '"'))
                .Append("\" },")
                .Append(newline);
        }

        builder.Append('}');
        return builder.ToString();
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
            || spec.GetTable("items") is not null
            || spec.GetTable("group") is not null
            || spec.GetTable("nameplates") is not null;

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

                RelocateSpecialStateFields(result);
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

        if (spec.GetTable("items") is { } items)
        {
            AppendItems(items, result.Items);
        }

        if (spec.GetTable("group") is { } group)
        {
            var groupBlocks = new GroupBlocks();
            groupBlocks.State.AddRange(GroupStateLayout.Read(group));

            if (group.GetTable("aura") is { } auraOffsets)
            {
                foreach (var (key, value) in auraOffsets.Entries.OrderBy(pair => pair.Key is long n ? n : long.MaxValue))
                {
                    if (key is not long || value is not TableValue auraInfo)
                    {
                        continue;
                    }

                    var entry = new GroupAuraEntry
                    {
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

            }

            result.Group = groupBlocks;
        }

        if (spec.GetTable("nameplates") is { } nameplates)
        {
            var blocks = new NameplateBlocks();
            blocks.State.AddRange(NameplateStateLayout.Read(nameplates));
            AppendAuraList(nameplates.GetTable("auras"), blocks.Auras);
            result.Nameplates = blocks;
        }

        return result;
    }

    private static void AppendItems(TableValue list, List<ItemEntry> target)
    {
        var seenIds = new HashSet<long>();
        foreach (var (key, value) in list.Entries)
        {
            if (key is not long itemId
                || itemId <= 0
                || value is not TableValue itemTable
                || !seenIds.Add(itemId))
            {
                continue;
            }

            var name = itemTable.GetString("name")?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                seenIds.Remove(itemId);
                continue;
            }

            target.Add(new ItemEntry
            {
                ItemId = itemId,
                Name = name,
                IsEquipped = itemTable.GetBool("isEquipped") == true
            });
        }

        target.Sort((left, right) => CompareItemIds(left.ItemId, right.ItemId));
    }

    private static int CompareItemIds(long? left, long? right)
        => left switch
        {
            null when right is null => 0,
            null => 1,
            _ when right is null => -1,
            _ => left.Value.CompareTo(right.Value)
        };

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
                MaxApps = aura.GetNumber("maxApps") is { } maxApps ? (int)maxApps : null
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
        RelocateSpecialStateFields(spec);
        ValidateItemsForSerialization(spec);

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

        // items
        if (spec.Items.Count > 0)
        {
            sb.Append(indent).AppendLine("items = {");
            foreach (var item in spec.Items.OrderBy(item => item.ItemId))
            {
                if (item.ItemId is not { } itemId || itemId <= 0 || string.IsNullOrWhiteSpace(item.Name))
                {
                    throw new InvalidOperationException("物品配置包含无效的 itemId 或空名称。");
                }

                sb.Append(indent).Append("    [").Append(itemId).Append("] = { name = \"")
                    .Append(Escape(item.Name)).Append("\", isEquipped = ")
                    .Append(item.IsEquipped ? "true" : "false").AppendLine(" },");
            }

            sb.Append(indent).AppendLine("},");
        }

        // group
        if (spec.Group is { } group)
        {
            sb.Append(indent).AppendLine("group = {");
            sb.Append(indent).Append("    state = {");
            foreach (var field in group.State)
            {
                sb.Append(" \"").Append(Escape(field)).Append("\",");
            }
            sb.AppendLine(" },");

            if (group.Auras.Count > 0)
            {
                sb.Append(indent).AppendLine("    aura = {");
                foreach (var aura in group.Auras)
                {
                    sb.Append(indent).Append("        {");
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

        if (spec.Nameplates is { } nameplates)
        {
            sb.Append(indent).AppendLine("nameplates = {");
            sb.Append(indent).Append("    state = {");
            foreach (var field in nameplates.State)
            {
                sb.Append(" \"").Append(Escape(field)).Append("\",");
            }

            sb.AppendLine(" },");

            if (nameplates.Auras.Count > 0)
            {
                sb.Append(indent).AppendLine("    auras = {");
                foreach (var aura in nameplates.Auras)
                {
                    WriteAuraEntry(sb, aura, indent + "        ");
                }

                sb.Append(indent).AppendLine("    },");
            }

            sb.Append(indent).AppendLine("},");
        }
    }

    internal static void RelocateSpecialStateFields(SpecBlocks spec)
    {
        if (!spec.NestedStates)
        {
            return;
        }

        if (!spec.CategorizedStates.TryGetValue(ClassStateCatalog.CategoryState, out var stateList)
            || !spec.CategorizedStates.TryGetValue(ClassStateCatalog.CategorySpecial, out var specialList))
        {
            return;
        }

        for (var index = 0; index < stateList.Count;)
        {
            var name = stateList[index];
            if (!ClassStateCatalog.IsKnown(ClassStateCatalog.CategorySpecial, name)
                || ClassStateCatalog.IsKnown(ClassStateCatalog.CategoryState, name))
            {
                index++;
                continue;
            }

            stateList.RemoveAt(index);
            if (!specialList.Contains(name, StringComparer.Ordinal))
            {
                specialList.Add(name);
            }
        }
    }

    private static void ValidateItemsForSerialization(SpecBlocks spec)
    {
        if (spec.Items.Count == 0)
        {
            return;
        }

        var ids = new HashSet<long>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var bareNames = new HashSet<string>(StringComparer.Ordinal);
        if (spec.NestedStates)
        {
            foreach (var category in new[]
                     {
                         ClassStateCatalog.CategoryState,
                         ClassStateCatalog.CategorySpecial,
                         ClassStateCatalog.CategoryResource,
                         ClassStateCatalog.CategoryConfig
                     })
            {
                bareNames.UnionWith(spec.CategorizedStates.GetValueOrDefault(category) ?? []);
            }
        }
        else
        {
            bareNames.UnionWith(spec.FlatStates);
        }

        foreach (var item in spec.Items)
        {
            if (item.ItemId is not { } itemId || itemId <= 0 || string.IsNullOrWhiteSpace(item.Name))
            {
                throw new InvalidOperationException("物品配置包含无效的 itemId 或空名称。");
            }

            if (!ids.Add(itemId))
            {
                throw new InvalidOperationException($"物品 itemId {itemId} 重复。");
            }

            if (!names.Add(item.Name))
            {
                throw new InvalidOperationException($"物品名称“{item.Name}”重复。");
            }

            if (bareNames.Contains(item.Name))
            {
                throw new InvalidOperationException($"物品名称“{item.Name}”与状态、特殊、能量或配置开关字段重名。");
            }
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
