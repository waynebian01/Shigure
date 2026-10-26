using System.Globalization;

namespace Shigure;

// 显式的右侧字段引用与旧的文本常量区分，旧条件文本继续按原语义读取。
internal static class ConditionOperand
{
    private const string ReferencePrefix = "$field[";

    public static string Reference(string name) => ReferencePrefix + name + "]";

    public static bool TryReference(string? text, out string name)
    {
        var value = text?.Trim() ?? string.Empty;
        name = value.StartsWith(ReferencePrefix, StringComparison.Ordinal) && value.EndsWith(']')
            ? value[ReferencePrefix.Length..^1] : string.Empty;
        return name.Length > 0;
    }

    public static bool TryNumber(string? text, out double number)
        => double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            && double.IsFinite(number);
}
