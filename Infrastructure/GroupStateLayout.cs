using static Shigure.LuaLiteParser;

namespace Shigure;

internal static class GroupStateLayout
{
    public static readonly string[] SupportedFields = ["healthPercent", "role", "dispel"];

    // 显式 state（包括空列表）优先；旧配置只在缺少 state 时迁移。
    public static List<string> Read(TableValue group)
    {
        if (group.GetTable("state") is { } states)
        {
            return states.IPairs().OfType<StringValue>().Select(value => value.Value)
                .Where(SupportedFields.Contains).Distinct(StringComparer.Ordinal).ToList();
        }

        return SupportedFields.Where(field => group.GetNumber(field) is >= 0).ToList();
    }

    public static string DisplayName(string field) => field switch
    {
        "healthPercent" => "生命值",
        "role" => "职责",
        "dispel" => "驱散",
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };
}
