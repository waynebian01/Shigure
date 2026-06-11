using System.Text.Json.Nodes;

namespace Shigure;

public static class JsonHelpers
{
    public static int? GetInt(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        try
        {
            if (node is JsonValue valueNode)
            {
                if (valueNode.TryGetValue<int>(out var intValue))
                {
                    return intValue;
                }

                if (valueNode.TryGetValue<long>(out var longValue))
                {
                    return (int)longValue;
                }

                if (valueNode.TryGetValue<string>(out var stringValue) && int.TryParse(stringValue, out var parsed))
                {
                    return parsed;
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    public static string? GetString(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        try
        {
            if (node is JsonValue valueNode && valueNode.TryGetValue<string>(out var value))
            {
                return value;
            }
        }
        catch
        {
            return null;
        }

        return node.ToJsonString();
    }

    public static JsonNode? Get(JsonObject obj, string key)
    {
        return obj.TryGetPropertyValue(key, out var node) ? node : null;
    }
}

