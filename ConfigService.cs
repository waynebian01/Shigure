using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shigure;

public sealed class ConfigService
{
    public JsonObject Root { get; }

    public ConfigService(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("找不到 config.json", configPath);
        }

        var json = File.ReadAllText(configPath);
        Root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        })?.AsObject() ?? throw new InvalidDataException("config.json 不是 JSON 对象。");
    }

    public JsonObject? GetObject(params string[] path)
    {
        JsonNode? node = Root;
        foreach (var part in path)
        {
            if (node is not JsonObject obj || !obj.TryGetPropertyValue(part, out node))
            {
                return null;
            }
        }

        return node as JsonObject;
    }

    public JsonObject BuildStateConfig(int? classId, int? specId)
    {
        var merged = new JsonObject();
        foreach (var key in new[] { "锚点", "职业", "专精" })
        {
            if (Root.TryGetPropertyValue(key, out var node) && node is JsonObject obj && obj.ContainsKey("step"))
            {
                merged[key] = obj.DeepClone();
            }
        }

        if (Root.TryGetPropertyValue("state", out var stateNode) && stateNode is JsonObject state)
        {
            CopyInto(merged, state);
        }

        if (classId is not null && specId is not null)
        {
            var spec = GetObject(classId.Value.ToString(), specId.Value.ToString());
            if (spec is not null)
            {
                CopyInto(merged, spec);
            }
        }

        return merged;
    }

    public string? GetKeymapName(int? classId)
    {
        if (classId is null)
        {
            return "keymap.json";
        }

        var classObj = GetObject(classId.Value.ToString());
        if (classObj is null || !classObj.TryGetPropertyValue("keymap", out var node))
        {
            return "keymap.json";
        }

        var value = JsonHelpers.GetString(node);
        return string.IsNullOrWhiteSpace(value) ? "keymap.json" : value;
    }

    private static void CopyInto(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source)
        {
            target[key] = value?.DeepClone();
        }
    }
}

