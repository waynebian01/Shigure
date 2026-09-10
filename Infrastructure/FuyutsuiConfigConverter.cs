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
        EnsureCommonConfig(configDirectory);
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
            var itemsList = ExtractAssignedTable(lua, "Fuyutsui.itemsList");

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

            if (itemsList is null)
            {
                warnings.Add($"{fileName}: 未找到 Fuyutsui.itemsList，已保留现有一键物品");
            }
            else
            {
                CompileItemMaps(itemsList, root, warnings, fileName);
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

    private static void EnsureCommonConfig(string configDirectory)
    {
        var commonPath = Path.Combine(configDirectory, ConfigService.CommonConfigFileName);
        if (File.Exists(commonPath))
        {
            return;
        }

        var root = new JsonObject
        {
            ["锚点"] = new JsonObject
            {
                ["step"] = 1,
                ["type"] = "bool"
            },
            ["职业"] = new JsonObject
            {
                ["step"] = 2,
                ["type"] = "int"
            },
            ["专精"] = new JsonObject
            {
                ["step"] = 3,
                ["type"] = "int"
            }
        };

        File.WriteAllText(commonPath, root.ToJsonString(WriteOptions) + Environment.NewLine, Encoding.UTF8);
    }

    private static void PreserveMeta(JsonObject existing, JsonObject target)
    {
        foreach (var key in new[] { "keymap", "一键法术", ModuleSpecialActions.OneKeyItem })
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
        var oneKeySpells = new SortedDictionary<int, long>();

        foreach (var (key, value) in spellsList.Entries)
        {
            if (value is not TableValue spell)
            {
                continue;
            }

            var indexValue = spell.GetNumber("index");
            var spellIdValue = key switch
            {
                long number => (double)number,
                int number => number,
                double number => number,
                NumberValue number => (double)number.AsInt(),
                _ => spell.GetNumber("spellId")
            };
            if (indexValue is null
                || indexValue.Value <= 0
                || indexValue.Value > int.MaxValue
                || indexValue.Value != Math.Truncate(indexValue.Value)
                || spellIdValue is null
                || spellIdValue.Value <= 0
                || spellIdValue.Value != Math.Truncate(spellIdValue.Value))
            {
                warnings.Add($"{label}: spellsList 条目缺少有效 index/spellId，已跳过");
                continue;
            }

            var index = (int)indexValue.Value;
            AddSpellMapEntry(oneKeySpells, index, (long)spellIdValue.Value, "一键法术", warnings, label);
        }

        target[ModuleSpecialActions.OneKeySpell] = ToSpellMap(oneKeySpells);
    }

    private static void CompileItemMaps(
        TableValue itemsList,
        JsonObject target,
        List<string> warnings,
        string label)
    {
        var oneKeyItems = new SortedDictionary<int, long>();

        foreach (var (key, value) in itemsList.Entries)
        {
            if (value is not TableValue item)
            {
                continue;
            }

            var indexValue = item.GetNumber("index");
            var itemIdValue = key switch
            {
                long number => (double)number,
                int number => number,
                double number => number,
                NumberValue number => (double)number.AsInt(),
                _ => item.GetNumber("itemId")
            };
            if (indexValue is null
                || indexValue.Value <= 0
                || indexValue.Value > int.MaxValue
                || indexValue.Value != Math.Truncate(indexValue.Value)
                || itemIdValue is null
                || itemIdValue.Value <= 0
                || itemIdValue.Value != Math.Truncate(itemIdValue.Value))
            {
                warnings.Add($"{label}: itemsList 条目缺少有效 index/itemId，已跳过");
                continue;
            }

            var index = (int)indexValue.Value;
            AddSpellMapEntry(oneKeyItems, index, (long)itemIdValue.Value, ModuleSpecialActions.OneKeyItem, warnings, label);
        }

        target[ModuleSpecialActions.OneKeyItem] = ToSpellMap(oneKeyItems);
    }

    private static void AddSpellMapEntry(
        IDictionary<int, long> target,
        int index,
        long spellId,
        string mapName,
        List<string> warnings,
        string label)
    {
        if (!target.TryGetValue(index, out var existingName))
        {
            target[index] = spellId;
            return;
        }

        if (existingName != spellId)
        {
            warnings.Add(
                $"{label}: {mapName} index {index} 同时对应 id {existingName} 和 {spellId}，已保留前者");
        }
    }

    private static JsonObject ToSpellMap(IEnumerable<KeyValuePair<int, long>> spells)
    {
        var result = new JsonObject();
        foreach (var (index, spellId) in spells)
        {
            result[index.ToString()] = spellId;
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
                        var key = IsUnitStateCategory(category)
                            ? category + stateName
                            : stateName;
                        AddStateField(result, key, index, skipCommon: true, category);
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

                    var stateName = NormalizeStateName(nameValue.Value);
                    AddStateField(
                        result,
                        stateName,
                        index,
                        skipCommon: true,
                        ClassStateCatalog.FindCategory(stateName) ?? ClassStateCatalog.CategoryState);
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
                AppendAuraList(auras.GetTable("player"), "player", "玩家", true, aurasObject, ref index, playerAuraBarNames, warnings, label);
                if (auras.GetTable("target") is { } target)
                {
                    AppendAuraList(target.GetTable("harmful"), "target", "目标减益", true, aurasObject, ref index, playerAuraBarNames, warnings, label);
                    AppendAuraList(target.GetTable("helpful"), "target", "目标增益", false, aurasObject, ref index, playerAuraBarNames, warnings, label);
                }

                if (auras.GetTable("focus") is { } focus)
                {
                    AppendAuraList(focus.GetTable("harmful"), "focus", "焦点减益", true, aurasObject, ref index, playerAuraBarNames, warnings, label);
                    AppendAuraList(focus.GetTable("helpful"), "focus", "焦点增益", false, aurasObject, ref index, playerAuraBarNames, warnings, label);
                }
            }
            else
            {
                AppendAuraList(auras, "player", "玩家", true, aurasObject, ref index, playerAuraBarNames, warnings, label);
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
                var id = (long)spellId.Value;
                spellsObject[$"{id}.{SpellFieldKey.SpellCooldown}"] = SpellField(
                    index,
                    name,
                    id,
                    SpellFieldKey.SpellCooldown,
                    "冷却");
                index++;

                var charge = spell.GetBool("charge") == true;
                if (charge)
                {
                    spellsObject[$"{id}.{SpellFieldKey.SpellChargeCooldown}"] = SpellField(
                        index,
                        EnsureSuffix(name, "充能"),
                        id,
                        SpellFieldKey.SpellChargeCooldown,
                        "充能");
                    index++;
                }

                var maxCharge = spell.GetNumber("maxCharge");
                if (charge && maxCharge is not null)
                {
                    if (barSpellIds.Add(id))
                    {
                        spellsObject[$"{id}.{SpellFieldKey.SpellCount}"] = SpellBarField(
                            barIndex++,
                            EnsureSuffix(name, "层数"),
                            id,
                            SpellFieldKey.SpellCount,
                            "充能层数");
                    }
                }

                var castCount = spell.GetNumber("castCount");
                if (castCount is not null && castCount.Value > 0)
                {
                    if (barSpellIds.Add(id))
                    {
                        spellsObject[$"{id}.{SpellFieldKey.SpellCount}"] = SpellBarField(
                            barIndex++,
                            EnsureSuffix(name, "层数"),
                            id,
                            SpellFieldKey.SpellCount,
                            "施法次数");
                    }
                }
            }
        }

        foreach (var barName in playerAuraBarNames)
        {
            if (aurasObject[barName] is JsonObject metadata)
            {
                metadata["step"] = "bar";
                metadata["bar"] = barIndex++;
            }
        }

        if (spec.GetTable("items") is { } items)
        {
            foreach (var item in ReadItems(items, warnings, label))
            {
                if (result.ContainsKey(item.Name))
                {
                    warnings.Add($"{label}: 物品名称“{item.Name}”与已有状态字段重复，已跳过该物品字段");
                }
                else
                {
                    var field = Field(index, "int", ClassStateCatalog.CategoryItem);
                    field["itemId"] = item.ItemId;
                    field["isEquipped"] = item.IsEquipped;
                    result[item.Name] = field;
                    index++;
                }
            }
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
                ["start"] = index
            };
            var groupFieldCount = 0;
            foreach (var field in GroupStateLayout.Read(group))
            {
                AddGroupOffset(groupJson, ++groupFieldCount, GroupStateLayout.DisplayName(field));
            }

            if (group.GetTable("aura") is { } auraOffsets)
            {
                foreach (var (key, value) in auraOffsets.Entries.OrderBy(pair => pair.Key is long n ? n : long.MaxValue))
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

                    var ids = ReadAuraIds(auraInfo);
                    var canonicalId = SpellFieldKey.CanonicalAuraId(
                        auraInfo.GetNumber("spellId") is { } primary ? (long)primary : null,
                        ids);
                    if (canonicalId is null)
                    {
                        warnings.Add($"{label}: group aura“{auraName}”缺少有效 spellId，已跳过");
                        continue;
                    }

                    groupJson[$"auras.{canonicalId}.{SpellFieldKey.AuraValue}"] = AuraField(
                        ++groupFieldCount,
                        auraName,
                        canonicalId.Value,
                        "group",
                        SpellFieldKey.AuraValue,
                        ids);
                }
            }

            if (groupFieldCount > 0)
            {
                result["group"] = groupJson;
                // 自动计算的步长与插件一致，预留插件实际处理的 30 个成员。
                index += 30 * groupFieldCount + 1;
            }
        }

        if (spec.GetTable("nameplates") is { } nameplates)
        {
            // 生命值/距离/战斗是固定偏移，与插件 LoadPlayerBlocks 的分配保持一致。
            var nameplateJson = new JsonObject
            {
                ["start"] = index,
                ["healthPercent"] = NameplateStateLayout.HealthPercentOffset,
                ["range"] = NameplateStateLayout.RangeOffset,
                ["combat"] = NameplateStateLayout.CombatOffset,
                ["auraStart"] = NameplateStateLayout.AuraStartOffset
            };
            var fieldCount = NameplateStateLayout.FixedFieldCount;
            if (nameplates.GetTable("auras") is { } nameplateAuras)
            {
                var auraArray = new JsonArray();
                foreach (var item in nameplateAuras.IPairs())
                {
                    if (item is not TableValue aura)
                    {
                        continue;
                    }

                    var ids = ReadAuraIds(aura);
                    if (ids.Count == 0)
                    {
                        warnings.Add($"{label}: nameplates aura 缺少有效 spellId，已跳过");
                        continue;
                    }

                    var auraJson = new JsonObject
                    {
                        ["name"] = aura.GetString("name")?.Trim() ?? string.Empty,
                        ["spellId"] = ids[0]
                    };
                    if (ids.Count > 1)
                    {
                        var aliases = new JsonArray();
                        foreach (var id in ids.Skip(1))
                        {
                            aliases.Add(id);
                        }

                        auraJson["spellIds"] = aliases;
                    }

                    auraArray.Add(auraJson);
                }

                nameplateJson["auras"] = auraArray;
                fieldCount += auraArray.Count;
            }

            nameplateJson["num"] = fieldCount;
            if (index + 20 * fieldCount - 1 > MainPixelLayout.MaxCapacity)
            {
                warnings.Add($"{label}: 姓名板需要 {20 * fieldCount} 格，超过主像素行 {MainPixelLayout.MaxCapacity} 格上限，已停用姓名板");
            }
            else
            {
                result["nameplates"] = nameplateJson;
            }
        }

        return (result, warnings);
    }

    private static List<(long ItemId, string Name, bool IsEquipped)> ReadItems(
        TableValue list,
        List<string> warnings,
        string label)
    {
        var result = new List<(long ItemId, string Name, bool IsEquipped)>();
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
                warnings.Add($"{label}: itemId {itemId} 缺少名称，已跳过");
                continue;
            }

            result.Add((
                itemId,
                name,
                itemTable.GetBool("isEquipped") == true));
        }

        result.Sort((left, right) => left.ItemId.CompareTo(right.ItemId));
        return result;
    }

    private static void AppendAuraList(
        TableValue? list,
        string unit,
        string classification,
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

            var ids = ReadAuraIds(aura);
            var canonicalId = SpellFieldKey.CanonicalAuraId(
                aura.GetNumber("spellId") is { } primary ? (long)primary : null,
                ids);
            if (canonicalId is null)
            {
                warnings.Add($"{label}: aura“{name}”缺少有效 spellId，已跳过");
                continue;
            }

            var scope = classification switch
            {
                "目标减益" => "target.harmful",
                "目标增益" => "target.helpful",
                "焦点减益" => "focus.harmful",
                "焦点增益" => "focus.helpful",
                _ => "player"
            };
            var valueKey = $"{scope}.{canonicalId}.{SpellFieldKey.AuraValue}";
            aurasObject[valueKey] = AuraField(
                index,
                name,
                canonicalId.Value,
                scope,
                SpellFieldKey.AuraValue,
                ids,
                classification);
            index++;

            if (aura.GetNumber("maxApps") is not null && includeApplicationBars)
            {
                var appsKey = $"{scope}.{canonicalId}.{SpellFieldKey.AuraApplications}";
                aurasObject[appsKey] = AuraField(
                    0,
                    EnsureSuffix(name, "层数"),
                    canonicalId.Value,
                    scope,
                    SpellFieldKey.AuraApplications,
                    ids,
                    classification);
                playerAuraBarNames.Add(appsKey);
            }
        }
    }

    private static List<long> ReadAuraIds(TableValue aura)
    {
        var result = new List<long>();
        if (aura.GetNumber("spellId") is { } spellId && spellId > 0)
        {
            result.Add((long)spellId);
        }

        if (aura.GetTable("spellIds") is { } spellIds)
        {
            foreach (var item in spellIds.IPairs())
            {
                var id = item switch
                {
                    NumberValue number => number.AsInt(),
                    _ => (long?)null
                };
                if (id is > 0 && !result.Contains(id.Value))
                {
                    result.Add(id.Value);
                }
            }
        }

        return result;
    }

    private static void AddStateField(
        JsonObject result,
        string name,
        int step,
        bool skipCommon,
        string classification)
    {
        if (skipCommon && CommonStateNames.Contains(name))
        {
            return;
        }

        result[name] = Field(step, BoolFieldNames.Contains(name) ? "bool" : "int", classification);
    }

    private static void AddGroupOffset(JsonObject groupJson, double? offset, string name)
    {
        if (offset is null)
        {
            return;
        }

        groupJson[name] = Field((int)offset.Value, "int");
    }

    private static JsonObject Field(int step, string type, string? classification = null)
    {
        var field = new JsonObject
        {
            ["step"] = step,
            ["type"] = type
        };
        if (!string.IsNullOrWhiteSpace(classification))
        {
            field["category"] = classification;
        }

        return field;
    }

    private static JsonObject BarField(int bar) => new()
    {
        ["step"] = "bar",
        ["bar"] = bar,
        ["type"] = "int"
    };

    private static JsonObject SpellField(
        int step,
        string displayName,
        long spellId,
        string metric,
        string displayType)
    {
        var field = Field(step, "int");
        AddSpellMetadata(field, displayName, spellId, metric, displayType);
        return field;
    }

    private static JsonObject SpellBarField(
        int bar,
        string displayName,
        long spellId,
        string metric,
        string displayType)
    {
        var field = BarField(bar);
        AddSpellMetadata(field, displayName, spellId, metric, displayType);
        return field;
    }

    private static void AddSpellMetadata(
        JsonObject field,
        string displayName,
        long spellId,
        string metric,
        string displayType)
    {
        field["displayName"] = displayName;
        field["spellId"] = spellId;
        field["metric"] = metric;
        field["displayType"] = displayType;
    }

    private static JsonObject AuraField(
        int step,
        string displayName,
        long spellId,
        string scope,
        string metric,
        IEnumerable<long> aliases,
        string? classification = null)
    {
        var field = Field(step, "int", classification);
        field["displayName"] = displayName;
        field["spellId"] = spellId;
        field["scope"] = scope;
        field["metric"] = metric;
        field["spellIds"] = new JsonArray(aliases.Where(id => id > 0).Distinct().Select(id => JsonValue.Create(id)).ToArray());
        return field;
    }

    private static string EnsureSuffix(string name, string suffix)
        => name.EndsWith(suffix, StringComparison.Ordinal) ? name : name + suffix;

    private static string NormalizeStateName(string name)
        => string.Equals(name, "法术失败", StringComparison.Ordinal)
            ? ModuleSpecialActions.InsertSpellState
            : name;

    private static bool IsUnitStateCategory(string category)
        => category is ClassStateCatalog.CategoryTarget
            or ClassStateCatalog.CategoryFocus
            or ClassStateCatalog.CategoryMouseover
            or ClassStateCatalog.CategoryPet
            or ClassStateCatalog.CategoryBoss1
            or ClassStateCatalog.CategoryBoss2
            or ClassStateCatalog.CategoryBoss3
            or ClassStateCatalog.CategoryBoss4
            or ClassStateCatalog.CategoryBoss5;
}
