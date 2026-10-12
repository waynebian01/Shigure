using static Shigure.LuaLiteParser;

namespace Shigure;

internal static class GroupStateLayout
{
    public const int SlotCount = 40;

    public static readonly string[] SupportedFields = ["healthPercent", "role", .. GroupDispelCatalog.Entries.Select(entry => entry.ConfigName), "class"];
    public static readonly string[] LegacyFields = ["healthPercent", "role", "dispel", "class", .. GroupDispelCatalog.Entries.Select(entry => entry.ConfigName)];
    public static readonly string[] RequiredFields = ["healthPercent", "role"];

    // 显式 state 优先；旧配置只在缺少 state 时迁移。启用 group 后生命值与职责始终占位。
    public static List<string> Read(TableValue group)
    {
        if (group.GetTable("state") is { } states)
        {
            return EnsureRequired(states.IPairs().OfType<StringValue>().Select(value => value.Value));
        }

        return EnsureRequired(LegacyFields.Where(field => group.GetNumber(field) is >= 0));
    }

    public static List<string> EnsureRequired(IEnumerable<string> fields)
    {
        var result = fields.SelectMany(field => field == "dispel"
                ? GroupDispelCatalog.Entries.Select(entry => entry.ConfigName) : [field])
            .Where(SupportedFields.Contains).Distinct(StringComparer.Ordinal).ToList();
        foreach (var field in RequiredFields)
        {
            if (!result.Contains(field, StringComparer.Ordinal))
            {
                result.Add(field);
            }
        }

        return result;
    }

    public static string DisplayName(string field) => field switch
    {
        "healthPercent" => "生命值",
        "role" => "职责",
        "dispel" => "驱散",
        "class" => "职业",
        _ => GroupDispelCatalog.Entries.FirstOrDefault(entry => entry.ConfigName == field)?.FieldName
            ?? throw new ArgumentOutOfRangeException(nameof(field))
    };
}
