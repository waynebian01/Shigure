using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shigure;

public sealed class KeymapService
{
    private readonly string _baseDirectory;
    private readonly ConfigService _config;
    private readonly Dictionary<(int Unit, string Spell), string> _hotkeys = new();
    private int? _currentClassId;

    public KeymapService(string baseDirectory, ConfigService config)
    {
        _baseDirectory = baseDirectory;
        _config = config;
    }

    public void SelectForClass(int? classId)
    {
        if (_currentClassId == classId && _hotkeys.Count > 0)
        {
            return;
        }

        _currentClassId = classId;
        _hotkeys.Clear();

        var keymapName = _config.GetKeymapName(classId) ?? "keymap.json";
        if (keymapName.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
        {
            keymapName = Path.ChangeExtension(keymapName, ".json");
        }

        var path = Path.IsPathRooted(keymapName)
            ? keymapName
            : Path.Combine(_baseDirectory, "keymap", keymapName);

        if (!File.Exists(path))
        {
            path = Path.Combine(_baseDirectory, "keymap", "keymap.json");
        }

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

            var unit = JsonHelpers.GetInt(JsonHelpers.Get(entry, "unit")) ?? 0;
            var spell = JsonHelpers.GetString(JsonHelpers.Get(entry, "spell"))
                ?? JsonHelpers.GetString(JsonHelpers.Get(entry, "技能"));
            var hotkey = JsonHelpers.GetString(JsonHelpers.Get(entry, "hotkey"))
                ?? JsonHelpers.GetString(JsonHelpers.Get(entry, "热键"));

            if (!string.IsNullOrWhiteSpace(spell) && !string.IsNullOrWhiteSpace(hotkey))
            {
                _hotkeys[(unit, spell)] = hotkey;
            }
        }
    }

    public string? GetHotkey(int? unit, string spell)
    {
        var normalizedUnit = unit.GetValueOrDefault();
        return _hotkeys.TryGetValue((normalizedUnit, spell), out var hotkey) ? hotkey : null;
    }
}

