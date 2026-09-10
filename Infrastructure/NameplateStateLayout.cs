using static Shigure.LuaLiteParser;

namespace Shigure;

internal static class NameplateStateLayout
{
    public static readonly string[] SupportedFields = ["healthPercent", "range"];

    // 显式 state（包括空列表）优先；旧配置只在缺少 state 时按正偏移迁移。
    public static List<string> Read(TableValue nameplates)
    {
        if (nameplates.GetTable("state") is { } states)
        {
            return states.IPairs().OfType<StringValue>().Select(value => value.Value)
                .Where(SupportedFields.Contains).Distinct(StringComparer.Ordinal).ToList();
        }

        return SupportedFields.Where(field => nameplates.GetNumber(field) is > 0).ToList();
    }

    public static string DisplayName(string field) => field switch
    {
        "healthPercent" => "生命值",
        "range" => "距离",
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };
}
