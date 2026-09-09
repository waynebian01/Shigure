using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shigure;

public sealed class KeymapService : IKeymapResolver
{
    private readonly string _baseDirectory;
    private readonly ConfigService _config;
    private readonly Dictionary<(int Unit, string Spell, string MacroCondition), string> _hotkeys = new();
    private readonly Dictionary<(int Unit, string Spell), string> _fallbackHotkeys = new();
    private readonly Dictionary<long, int> _spellIndices = new();
    private readonly Dictionary<long, string> _spellNames = new();
    private readonly Dictionary<long, int> _itemIndices = new();
    private readonly Dictionary<long, string> _itemNames = new();
    private int? _currentClassId;

    public KeymapService(string baseDirectory, ConfigService config)
    {
        _baseDirectory = baseDirectory;
        _config = config;
    }

    public void SelectForClass(int? classId)
    {
        SelectForClass(classId, null);
    }

    public void SelectForClass(int? classId, int? specId)
    {
        // keymap 现在只有职业级映射；专精变化不需要重新读取同一份文件。
        if (_currentClassId == classId && _hotkeys.Count > 0)
        {
            return;
        }

        _currentClassId = classId;
        _hotkeys.Clear();
        _fallbackHotkeys.Clear();
        _spellIndices.Clear();
        _spellNames.Clear();
        _itemIndices.Clear();
        _itemNames.Clear();

        LoadSpellIndices(classId);

        var path = KeymapCatalog.ResolveKeymapFilePath(_baseDirectory, _config.GetKeymapName(classId));
        if (!File.Exists(path))
        {
            return;
        }

        var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) as JsonObject;

        if (root is null)
        {
            return;
        }

        foreach (var (_, node) in root)
        {
            if (node is not JsonObject entry)
            {
                continue;
            }

            var rawUnit = JsonHelpers.GetInt(JsonHelpers.Get(entry, "unit")) ?? 0;
            var spell = JsonHelpers.GetString(JsonHelpers.Get(entry, "spell"))
                ?? JsonHelpers.GetString(JsonHelpers.Get(entry, "技能"));
            var hotkey = JsonHelpers.GetString(JsonHelpers.Get(entry, "hotkey"))
                ?? JsonHelpers.GetString(JsonHelpers.Get(entry, "热键"));
            var normalizedMacro = MacroConditionText.NormalizeLegacyUnit(
                rawUnit,
                JsonHelpers.GetString(JsonHelpers.Get(entry, "宏条件")));
            var unit = normalizedMacro.Unit;
            var macroCondition = normalizedMacro.Condition;

            if (!string.IsNullOrWhiteSpace(spell) && !string.IsNullOrWhiteSpace(hotkey))
            {
                _hotkeys[(unit, spell, macroCondition)] = hotkey;
                // 兼容未保存“宏条件”的旧模块：保留旧版按单位+技能查询时的最后一项行为。
                _fallbackHotkeys[(unit, spell)] = hotkey;
            }
        }
    }

    public string? GetHotkey(int? unit, string spell, string? macroCondition = null)
    {
        var normalizedUnit = unit.GetValueOrDefault();
        // null 表示旧模块根本没有该字段，严格沿用升级前“单位+技能”的最后一项匹配。
        if (macroCondition is null)
        {
            return _fallbackHotkeys.TryGetValue((normalizedUnit, spell), out var legacyHotkey)
                ? legacyHotkey
                : null;
        }

        var normalizedCondition = MacroConditionText.Normalize(macroCondition);
        if (_hotkeys.TryGetValue((normalizedUnit, spell, normalizedCondition), out var exactHotkey))
        {
            return exactHotkey;
        }

        return null;
    }

    public string? GetHotkey(int? unit, long spellId, string? macroCondition = null)
        => _spellNames.TryGetValue(spellId, out var spell)
            ? GetHotkey(unit, spell, macroCondition)
            : null;

    public IReadOnlyDictionary<int, long> GetCurrentFailedSpells()
    {
        return _config.GetFailedSpells(_currentClassId);
    }

    public IReadOnlyDictionary<int, long> GetCurrentOneKeySpells()
    {
        return _config.GetOneKeySpells(_currentClassId);
    }

    public IReadOnlyDictionary<int, long> GetCurrentInsertItems()
    {
        return _config.GetInsertItems(_currentClassId);
    }

    public IReadOnlyDictionary<long, int> GetCurrentSpellIndices()
    {
        return _spellIndices;
    }

    public IReadOnlyDictionary<long, string> GetCurrentSpellNames()
    {
        return _spellNames;
    }

    public IReadOnlyDictionary<long, int> GetCurrentItemIndices()
    {
        return _itemIndices;
    }

    public IReadOnlyDictionary<long, string> GetCurrentItemNames()
    {
        return _itemNames;
    }

    private void LoadSpellIndices(int? classId)
    {
        if (classId is null)
        {
            return;
        }

        var classPath = Path.Combine(
            _baseDirectory,
            "Fuyutsui",
            "class",
            $"{ClassNames.GetConfigFileName(classId.Value)}.lua");
        try
        {
            var document = ClassBlocksStore.Load(classPath);
            foreach (var spell in document.SpellsList.Where(spell => spell.SpellId > 0))
            {
                _spellIndices.TryAdd(spell.SpellId, spell.Index);
                if (!string.IsNullOrWhiteSpace(spell.Name))
                {
                    _spellNames.TryAdd(spell.SpellId, spell.Name.Trim());
                }
            }

            foreach (var item in document.ItemsList.Where(item => item.ItemId > 0))
            {
                _itemIndices.TryAdd(item.ItemId, item.Index);
                if (!string.IsNullOrWhiteSpace(item.Name))
                {
                    _itemNames.TryAdd(item.ItemId, item.Name.Trim());
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or InvalidDataException or ArgumentException)
        {
            // 职业技能列表不可用时保持空映射；引用 spellId 的条件会安全地按不命中处理。
        }
    }
}
