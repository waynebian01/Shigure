using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shigure;

/// <summary>
/// 从职业 keymap 文件构建模块编辑器可选择的技能与目标(unit)目录。
/// 同名技能只保留一个；unit 去重后升序排列。
/// 只读取职业级 keymap；有效条目规则与 KeymapService 保持一致。
/// </summary>
public sealed class KeymapCatalog
{
    private readonly string _baseDirectory;
    private readonly ConfigService? _config;
    private readonly Dictionary<string, KeymapEntries> _cache = new(StringComparer.OrdinalIgnoreCase);

    private KeymapCatalog(string baseDirectory, ConfigService? config)
    {
        _baseDirectory = baseDirectory;
        _config = config;
    }

    public static KeymapCatalog Load(string baseDirectory)
    {
        try
        {
            return new KeymapCatalog(baseDirectory, ConfigService.LoadFromBaseDirectory(baseDirectory));
        }
        catch
        {
            // config 缺失或损坏时仍可回退到 keymap/keymap.json。
            return new KeymapCatalog(baseDirectory, null);
        }
    }

    /// <summary>该职业 keymap 中出现过的技能名, 去重后按文件内首次出现的顺序排列。</summary>
    public IReadOnlyList<string> GetSpells(int? classId)
    {
        return GetEntries(classId).Spells;
    }

    /// <summary>该职业 keymap 中出现过的 unit 编号, 去重后升序排列。</summary>
    public IReadOnlyList<int> GetUnits(int? classId)
    {
        return GetEntries(classId).Units;
    }

    /// <summary>
    /// 指定技能在该职业 keymap 中实际配置过的 unit 编号(去重升序)。
    /// 技能为空时返回该职业全部 unit; 技能不在 keymap 中时返回空列表。
    /// </summary>
    public IReadOnlyList<int> GetUnitsForSpell(int? classId, string? spell)
    {
        var entries = GetEntries(classId);
        if (string.IsNullOrEmpty(spell))
        {
            return entries.Units;
        }

        return entries.UnitsBySpell.TryGetValue(spell, out var units) ? units : [];
    }

    public IReadOnlyList<int> GetUnitsForSpells(int? classId, IEnumerable<string> spells)
    {
        var entries = GetEntries(classId);
        var units = new SortedSet<int>();
        foreach (var spell in spells)
        {
            if (!string.IsNullOrWhiteSpace(spell)
                && entries.UnitsBySpell.TryGetValue(spell, out var spellUnits))
            {
                foreach (var unit in spellUnits)
                {
                    units.Add(unit);
                }
            }
        }

        return units.ToList();
    }

    /// <summary>指定技能和 unit 在 keymap 中配置过的宏条件，按文件内首次出现顺序返回。</summary>
    public IReadOnlyList<string> GetMacroConditions(int? classId, string? spell, int? unit)
    {
        if (string.IsNullOrWhiteSpace(spell))
        {
            return [];
        }

        var entries = GetEntries(classId);
        return entries.MacroConditions.TryGetValue((spell, unit.GetValueOrDefault()), out var conditions)
            ? conditions
            : [];
    }

    public IReadOnlyCollection<string> GetFailedSpellNames(int? classId)
    {
        if (classId is null || _config is null)
        {
            return [];
        }

        var ids = _config.GetFailedSpells(classId).Values.ToHashSet();
        var path = Path.Combine(
            _baseDirectory,
            "Fuyutsui",
            "class",
            $"{ClassNames.GetConfigFileName(classId.Value)}.lua");
        try
        {
            return ClassBlocksStore.Load(path).SpellsList
                .Where(spell => ids.Contains(spell.SpellId) && !string.IsNullOrWhiteSpace(spell.Name))
                .Select(spell => spell.Name.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public IReadOnlyCollection<string> GetFailedItemNames(int? classId)
    {
        if (classId is null || _config is null)
        {
            return [];
        }

        var ids = _config.GetInsertItems(classId).Values.ToHashSet();
        var path = Path.Combine(
            _baseDirectory,
            "Fuyutsui",
            "class",
            $"{ClassNames.GetConfigFileName(classId.Value)}.lua");
        try
        {
            return ClassBlocksStore.Load(path).ItemsList
                .Where(item => ids.Contains(item.ItemId) && !string.IsNullOrWhiteSpace(item.Name))
                .Select(item => item.Name.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private KeymapEntries GetEntries(int? classId)
    {
        var path = ResolveKeymapPath(classId);
        if (path is null)
        {
            return KeymapEntries.Empty;
        }

        if (_cache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var entries = ParseKeymap(path);
        _cache[path] = entries;
        return entries;
    }

    private string? ResolveKeymapPath(int? classId)
    {
        var path = ResolveKeymapFilePath(_baseDirectory, _config?.GetKeymapName(classId));
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// keymap 文件路径解析, 由 KeymapCatalog 与 KeymapService 共用以保证行为一致:
    /// .yml 改写为 .json; 绝对路径直接使用, 否则相对 baseDirectory/keymap; 找不到回退 keymap/keymap.json。
    /// 返回的是候选路径(可能仍不存在), 由调用方自行判断 File.Exists。
    /// </summary>
    internal static string ResolveKeymapFilePath(string baseDirectory, string? keymapName)
    {
        var name = keymapName ?? "keymap.json";
        if (name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
        {
            name = Path.ChangeExtension(name, ".json");
        }

        var path = Path.IsPathRooted(name)
            ? name
            : Path.Combine(baseDirectory, "keymap", name);

        return File.Exists(path)
            ? path
            : Path.Combine(baseDirectory, "keymap", "keymap.json");
    }

    private static KeymapEntries ParseKeymap(string path)
    {
        var spells = new List<string>();
        var units = new List<int>();
        var unitsBySpell = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var macroConditions = new Dictionary<(string Spell, int Unit), List<string>>();
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }) as JsonObject;

            if (root is null)
            {
                return KeymapEntries.Empty;
            }

            var seenSpells = new HashSet<string>(StringComparer.Ordinal);
            var seenUnits = new HashSet<int>();
            AddMap(root);

            units.Sort();
            foreach (var spellUnits in unitsBySpell.Values)
            {
                spellUnits.Sort();
            }

            void AddMap(JsonObject map)
            {
                foreach (var (_, node) in map)
                {
                    if (node is not JsonObject entry)
                    {
                        continue;
                    }

                    var spell = JsonHelpers.GetString(JsonHelpers.Get(entry, "spell"))
                        ?? JsonHelpers.GetString(JsonHelpers.Get(entry, "技能"));
                    var hotkey = JsonHelpers.GetString(JsonHelpers.Get(entry, "hotkey"))
                        ?? JsonHelpers.GetString(JsonHelpers.Get(entry, "热键"));

                    // 与运行时一致: 只有技能和热键都非空的条目才能被查到并发送。
                    if (string.IsNullOrWhiteSpace(spell) || string.IsNullOrWhiteSpace(hotkey))
                    {
                        continue;
                    }

                    var rawUnit = JsonHelpers.GetInt(JsonHelpers.Get(entry, "unit")) ?? 0;
                    var normalizedMacro = MacroConditionText.NormalizeLegacyUnit(
                        rawUnit,
                        JsonHelpers.GetString(JsonHelpers.Get(entry, "宏条件")));
                    var unit = normalizedMacro.Unit;
                    var macroCondition = normalizedMacro.Condition;
                    if (seenSpells.Add(spell))
                    {
                        spells.Add(spell);
                    }

                    if (seenUnits.Add(unit))
                    {
                        units.Add(unit);
                    }

                    if (!unitsBySpell.TryGetValue(spell, out var spellUnits))
                    {
                        spellUnits = new List<int>();
                        unitsBySpell[spell] = spellUnits;
                    }

                    if (!spellUnits.Contains(unit))
                    {
                        spellUnits.Add(unit);
                    }

                    var conditionKey = (spell, unit);
                    if (!macroConditions.TryGetValue(conditionKey, out var conditions))
                    {
                        conditions = new List<string>();
                        macroConditions[conditionKey] = conditions;
                    }

                    if (!conditions.Contains(macroCondition, StringComparer.Ordinal))
                    {
                        conditions.Add(macroCondition);
                    }
                }
            }
        }
        catch
        {
            // keymap 损坏时返回空目录, 下拉降级为只有留空项。
            return KeymapEntries.Empty;
        }

        return new KeymapEntries(
            spells,
            units,
            unitsBySpell.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<int>)kvp.Value, StringComparer.Ordinal),
            macroConditions.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<string>)kvp.Value));
    }

    private sealed record KeymapEntries(
        IReadOnlyList<string> Spells,
        IReadOnlyList<int> Units,
        IReadOnlyDictionary<string, IReadOnlyList<int>> UnitsBySpell,
        IReadOnlyDictionary<(string Spell, int Unit), IReadOnlyList<string>> MacroConditions)
    {
        public static readonly KeymapEntries Empty = new(
            [],
            [],
            new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal),
            new Dictionary<(string Spell, int Unit), IReadOnlyList<string>>());
    }
}
