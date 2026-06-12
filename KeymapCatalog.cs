using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shigure;

/// <summary>
/// 从职业 keymap 文件构建模块编辑器可选择的技能与目标(unit)目录。
/// 同名技能只保留一个；unit 去重后升序排列。
/// 文件解析规则与 KeymapService.SelectForClass 保持一致。
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
            var configPath = Path.Combine(baseDirectory, "config.json");
            return new KeymapCatalog(baseDirectory, new ConfigService(configPath));
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
        var keymapName = _config?.GetKeymapName(classId) ?? "keymap.json";
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

        return File.Exists(path) ? path : null;
    }

    private static KeymapEntries ParseKeymap(string path)
    {
        var spells = new List<string>();
        var units = new List<int>();
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
            foreach (var (_, node) in root)
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

                var unit = JsonHelpers.GetInt(JsonHelpers.Get(entry, "unit")) ?? 0;
                if (seenSpells.Add(spell))
                {
                    spells.Add(spell);
                }

                if (seenUnits.Add(unit))
                {
                    units.Add(unit);
                }
            }

            units.Sort();
        }
        catch
        {
            // keymap 损坏时返回空目录, 下拉降级为只有留空项。
            return KeymapEntries.Empty;
        }

        return new KeymapEntries(spells, units);
    }

    private sealed record KeymapEntries(IReadOnlyList<string> Spells, IReadOnlyList<int> Units)
    {
        public static readonly KeymapEntries Empty = new([], []);
    }
}
