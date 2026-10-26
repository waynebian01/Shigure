using System.Globalization;

namespace Shigure;

internal static class TimeValueEncoding
{
    public const string Deciseconds = "deciseconds";
    public const string Centiseconds = "centiseconds";

    // 只识别时间字段，不能把施法技能、可打断状态或蓄力层数当作时间。
    public static string? StateType(string name)
        => name == "公共冷却" ? Centiseconds
            : name.EndsWith("施法(正计时)", StringComparison.Ordinal)
                || name.EndsWith("施法(倒计时)", StringComparison.Ordinal)
                || name.EndsWith("引导", StringComparison.Ordinal)
                || name == "蓄力" ? Deciseconds : null;

    public static object Decode(int raw, string? type) => type switch
    {
        Deciseconds => raw / 10d,
        Centiseconds => raw / 100d,
        _ => raw
    };

    // 时间经解码后才是 double；整数计数、层数和普通精度值不补零。
    public static string Format(object? value, bool centiseconds = false)
        => value is double seconds
            ? seconds.ToString(centiseconds ? "0.00" : "00.0", CultureInfo.InvariantCulture)
            : UiTheme.FormatValue(value);
}
