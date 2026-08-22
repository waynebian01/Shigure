using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Shigure.LuaLiteParser;

namespace Shigure;

/// <summary>
/// 将 Fuyutsui class/*.lua 的 ClassBlocks 编译为 config/*.json（对齐 LoadPlayerBlocks 占位顺序）。
/// </summary>
internal static class FuyutsuiConfigConverter
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly HashSet<string> CommonStateNames = new(StringComparer.Ordinal)
    {
        "锚点", "职业", "专精"
    };

    private static readonly HashSet<string> BoolFieldNames = new(StringComparer.Ordinal)
    {
        "锚点", "有效性", "移动"
    };

    private static readonly string[] StateCategoryOrder =
    [
        ClassStateCatalog.CategoryState,
        ClassStateCatalog.CategoryResource,
        ClassStateCatalog.CategoryItem,
        ClassStateCatalog.CategoryConfig,
        ClassStateCatalog.CategoryTarget,
        ClassStateCatalog.CategoryFocus
    ];

    public sealed record UpdateResult(
        string ClassDirectory,
        IReadOnlyList<string> UpdatedFiles,
        IReadOnlyList<string> Warnings);

    public static UpdateResult UpdateFromClassDirectory(string classDirectory, string configDirectory)
    {
        if (!Directory.Exists(classDirectory))
        {
            throw new DirectoryNotFoundException($"找不到 Fuyutsui class 目录: {classDirectory}");
        }

        Directory.CreateDirectory(configDirectory);
        var updated = new List<string>();
        var warnings = new List<string>();

        foreach (var (classId, _) in ClassNames.GetClasses())
        {
            var fileName = ClassNames.GetConfigFileName(classId);
            var luaPath = Path.Combine(classDirectory, $"{fileName}.lua");
            if (!File.Exists(luaPath))
            {
                warnings.Add($"跳过 {fileName}: 未找到 {luaPath}");
                continue;
            }

            var jsonPath = Path.Combine(configDirectory, $"{fileName}.json");
            var existing = File.Exists(jsonPath)
                ? JsonNode.Parse(File.ReadAllText(jsonPath)) as JsonObject ?? new JsonObject()
                : new JsonObject();

            var lua = File.ReadAllText(luaPath, Encoding.UTF8);
            var classBlocks = ExtractAssignedTable(lua, "Fuyutsui.ClassBlocks")
                ?? throw new InvalidDataException($"{fileName}.lua 中未找到 Fuyutsui.ClassBlocks");
            var spellsList = ExtractAssignedTable(lua, "Fuyutsui.spellsList");

            var root = new JsonObject();
            PreserveMeta(existing, root);
            root["keymap"] ??= fileName.ToLowerInvariant() + ".json";
            if (spellsList is null)
            {
                warnings.Add($"{fileName}: 未找到 Fuyutsui.spellsList，已保留现有一键法术");
            }
            else
            {
                CompileSpellMaps(spellsList, root, warnings, fileName);
            }

            for (var specId = 1; specId <= 4; specId++)
            {
                if (classBlocks.Get((long)specId) is not TableValue specTable)
                {
                    continue;
                }

                var (specJson, specWarnings) = CompileSpec(specTable, $"{fileName}[{specId}]");
                warnings.AddRange(specWarnings);
                if (specJson.Count > 0)
                {
                    root[specId.ToString()] = specJson;
                }
            }

            File.WriteAllText(jsonPath, root.ToJsonString(WriteOptions) + Environment.NewLine, Encoding.UTF8);
            updated.Add(jsonPath);
        }

        if (updated.Count == 0)
        {
            throw new InvalidOperationException("未成功转换任何职业配置。");
        }

        return new UpdateResult(classDirectory, updated, warnings);
    }

    private static void PreserveMeta(JsonObject existing, JsonObject target)
    {
        foreach (var key in new[] { "keymap", "一键法术" })
        {
            if (existing[key] is { } node)
            {
                target[key] = node.DeepClone();
            }
        }
    }

    private static void CompileSpellMaps(
        TableValue spellsList,
        JsonObject target,
        List<string> warnings,
        string label)
    {
        var oneKeySpells = new SortedDictionary<int, string>();

        foreach (var (_, value) in spellsList.Entries)
        {
            if (value is not TableValue spell)
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
                warnings.Add($"{label}: spellsList 条目缺少有效 index/name，已跳过");
                continue;
            }

            var index = (int)indexValue.Value;
            AddSpellMapEntry(oneKeySpells, index, name, "一键法术", warnings, label);
        }

        target[ModuleSpecialActions.OneKeySpell] = ToSpellMap(oneKeySpells);
    }

    private static void AddSpellMapEntry(
        IDictionary<int, string> target,
        int index,
        string name,
        string mapName,
        List<string> warnings,
        string label)
    {
        if (!target.TryGetValue(index, out var existingName))
        {
            target[index] = name;
            return;
        }

        if (!string.Equals(existingName, name, StringComparison.Ordinal))
        {
            warnings.Add(
                $"{label}: {mapName} index {index} 同时对应“{existingName}”和“{name}”，已保留前者");
        }
    }

    private static JsonObject ToSpellMap(IEnumerable<KeyValuePair<int, string>> spells)
    {
        var result = new JsonObject();
        foreach (var (index, name) in spells)
        {
            result[index.ToString()] = name;
        }

        return result;
    }

    private static (JsonObject Spec, List<string> Warnings) CompileSpec(TableValue spec, string label)
    {
        var warnings = new List<string>();
        var result = new JsonObject();
        var index = 1;

        // states
        if (spec.GetTable("states") is { } states)
        {
            var nested = StateCategoryOrder.Any(category => states.GetTable(category) is not null);

            if (nested)
            {
                foreach (var category in StateCategoryOrder)
                {
                    if (states.GetTable(category) is not { } list)
                    {
                        continue;
                    }

                    foreach (var item in list.IPairs())
                    {
                        if (item is not StringValue nameValue || string.IsNullOrWhiteSpace(nameValue.Value))
                        {
                            continue;
                        }

                        var stateName = NormalizeStateName(nameValue.Value);
                        var key = category is ClassStateCatalog.CategoryTarget or ClassStateCatalog.CategoryFocus
                            ? category + stateName
                            : stateName;
                        AddStateField(result, key, index, skipCommon: true);
                        index++;
                    }
                }
            }
            else
            {
                foreach (var item in states.IPairs())
                {
                    if (item is not StringValue nameValue || string.IsNullOrWhiteSpace(nameValue.Value))
                    {
                        continue;
                    }

                    AddStateField(result, NormalizeStateName(nameValue.Value), index, skipCommon: true);
                    index++;
                }
            }
        }

        var aurasObject = new JsonObject();
        var playerAuraBarNames = new List<string>();

        // auras：主色块按 player → target → focus；层数条按 player → target harmful → focus harmful，排在 spell 条之后
        if (spec.GetTable("auras") is { } auras)
        {
            var nested = auras.GetTable("player") is not null
                || auras.GetTable("target") is not null
                || auras.GetTable("focus") is not null;

            if (nested)
            {
                AppendAuraList(auras.GetTable("player"), "player", true, aurasObject, ref index, playerAuraBarNames, warnings, label);
                if (auras.GetTable("target") is { } target)
                {
                    AppendAuraList(target.GetTable("harmful"), "target", true, aurasObject, ref index, playerAuraBarNames, warnings, label);
                    AppendAuraList(target.GetTable("helpful"), "target", false, aurasObject, ref index, playerAuraBarNames, warnings, label);
                }

                if (auras.GetTable("focus") is { } focus)
                {
                    AppendAuraList(focus.GetTable("harmful"), "focus", true, aurasObject, ref index, playerAuraBarNames, warnings, label);
                    AppendAuraList(focus.GetTable("helpful"), "focus", false, aurasObject, ref index, playerAuraBarNames, warnings, label);
                }
            }
            else
            {
                AppendAuraList(auras, "player", true, aurasObject, ref index, playerAuraBarNames, warnings, label);
            }
        }

        var spellsObject = new JsonObject();
        var barIndex = 1;
        var barSpellIds = new HashSet<long>();

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
                    warnings.Add($"{label}: spell 缺少 spellId，已跳过");
                    continue;
                }

                var name = spell.GetString("name")?.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = ((long)spellId.Value).ToString();
                }

                // 主色块顺序与 LoadPlayerBlocks 一致：
                // 所有法术先占一个冷却格；充能法术再紧接着占一个充能冷却格。
                spellsObject[name] = Field(index, "int");
                index++;

                var charge = spell.GetBool("charge") == true;
                if (charge)
                {
                    spellsObject[EnsureSuffix(name, "充能")] = Field(index, "int");
                    index++;
                }

                var maxCharge = spell.GetNumber("maxCharge");
                if (charge && maxCharge is not null)
                {
                    var id = (long)spellId.Value;
                    if (barSpellIds.Add(id))
                    {
                        spellsObject[EnsureSuffix(name, "层数")] = BarField(barIndex++);
                    }
                }

                var castCount = spell.GetNumber("castCount");
                if (castCount is not null && castCount.Value > 0)
                {
                    var id = (long)spellId.Value;
                    if (barSpellIds.Add(id))
                    {
                        spellsObject[EnsureSuffix(name, "层数")] = BarField(barIndex++);
                    }
                }
            }
        }

        foreach (var barName in playerAuraBarNames)
        {
            aurasObject[barName] = BarField(barIndex++);
        }

        if (aurasObject.Count > 0)
        {
            result["auras"] = aurasObject;
        }

        if (spellsObject.Count > 0)
        {
            result["spells"] = spellsObject;
        }

        // group
        if (spec.GetTable("group") is { } group)
        {
            var groupJson = new JsonObject
            {
                ["start"] = index,
                ["num"] = (int)(group.GetNumber("num") ?? 5)
            };

            AddGroupOffset(groupJson, group.GetNumber("healthPercent"), "生命值");
            AddGroupOffset(groupJson, group.GetNumber("role"), "职责");
            AddGroupOffset(groupJson, group.GetNumber("dispel"), "驱散");

            if (group.GetTable("aura") is { } auraOffsets)
            {
                foreach (var (key, value) in auraOffsets.Entries)
                {
                    var offset = key switch
                    {
                        long l => l,
                        int i => i,
                        double d => (long)d,
                        NumberValue n => n.AsInt(),
                        _ => (long?)null
                    };
                    if (offset is null || value is not TableValue auraInfo)
                    {
                        continue;
                    }

                    var auraName = auraInfo.GetString("name")?.Trim();
                    if (string.IsNullOrWhiteSpace(auraName))
                    {
                        auraName = $"光环{offset}";
                    }

                    groupJson[auraName] = Field((int)offset.Value, "int");
                }
            }

            result["group"] = groupJson;
        }

        return (result, warnings);
    }

    private static void AppendAuraList(
        TableValue? list,
        string unit,
        bool includeApplicationBars,
        JsonObject aurasObject,
        ref int index,
        List<string> playerAuraBarNames,
        List<string> warnings,
        string label)
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

            if (aura.GetNumber("spellId") is null && aura.Get("spellIds") is null)
            {
                warnings.Add($"{label}: aura 缺少 spellId/spellIds，已跳过");
                continue;
            }

            var name = aura.GetString("name")?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "未命名光环";
            }

            var fieldName = unit switch
            {
                "target" => "目标" + name,
                "focus" => "焦点" + name,
                _ => name
            };

            aurasObject[fieldName] = Field(index, "int");
            index++;

            if (aura.GetNumber("maxApps") is not null && includeApplicationBars)
            {
                playerAuraBarNames.Add(EnsureSuffix(fieldName, "层数"));
            }
        }
    }

    private static void AddStateField(JsonObject result, string name, int step, bool skipCommon)
    {
        if (skipCommon && CommonStateNames.Contains(name))
        {
            return;
        }

        result[name] = Field(step, BoolFieldNames.Contains(name) ? "bool" : "int");
    }

    private static void AddGroupOffset(JsonObject groupJson, double? offset, string name)
    {
        if (offset is null)
        {
            return;
        }

        groupJson[name] = Field((int)offset.Value, "int");
    }

    private static JsonObject Field(int step, string type) => new()
    {
        ["step"] = step,
        ["type"] = type
    };

    private static JsonObject BarField(int bar) => new()
    {
        ["step"] = "bar",
        ["bar"] = bar,
        ["type"] = "int"
    };

    private static string EnsureSuffix(string name, string suffix)
        => name.EndsWith(suffix, StringComparison.Ordinal) ? name : name + suffix;

    private static string NormalizeStateName(string name)
        => string.Equals(name, "法术失败", StringComparison.Ordinal)
            ? ModuleSpecialActions.InsertSpellState
            : name;
}
