using System.Globalization;

namespace Shigure;

/// <summary>
/// keymap 中团队槽位 1-40 之外的保留单位，以及模块编辑器使用的中文显示名称。
/// </summary>
internal static class ReservedUnit
{
    public const string MappingVersionPropertyName = "UnitMappingVersion";
    public const int CurrentMappingVersion = 4;
    public const int None = 0;
    public const int Player = 41;
    public const int Target = 42;
    public const int Focus = 43;
    public const int Cursor = 44;
    public const int Mouseover = 45;
    public const int Boss1 = 46;
    public const int Boss2 = 47;
    public const int Boss3 = 48;
    public const int Boss4 = 49;
    public const int Boss5 = 50;
    public const int Arena1 = 51;
    public const int Arena2 = 52;
    public const int Arena3 = 53;
    public const int Arena4 = 54;
    public const int Arena5 = 55;

    public static string ToDisplayText(int unit)
    {
        return unit switch
        {
            None => "无目标",
            Player => "玩家",
            Target => "目标",
            Focus => "焦点",
            Cursor => "地面",
            Mouseover => "鼠标",
            Boss1 => "首领1",
            Boss2 => "首领2",
            Boss3 => "首领3",
            Boss4 => "首领4",
            Boss5 => "首领5",
            Arena1 => "竞技场1",
            Arena2 => "竞技场2",
            Arena3 => "竞技场3",
            Arena4 => "竞技场4",
            Arena5 => "竞技场5",
            _ => unit.ToString(CultureInfo.InvariantCulture)
        };
    }

    public static int? ParseDisplayText(string? text)
    {
        var value = text?.Trim() ?? string.Empty;
        return value switch
        {
            "无目标" => None,
            "玩家" => Player,
            "目标" => Target,
            "焦点" => Focus,
            "地面" => Cursor,
            "鼠标" => Mouseover,
            "首领1" or "boss1" => Boss1,
            "首领2" or "boss2" => Boss2,
            "首领3" or "boss3" => Boss3,
            "首领4" or "boss4" => Boss4,
            "首领5" or "boss5" => Boss5,
            "竞技场1" or "arena1" => Arena1,
            "竞技场2" or "arena2" => Arena2,
            "竞技场3" or "arena3" => Arena3,
            "竞技场4" or "arena4" => Arena4,
            "竞技场5" or "arena5" => Arena5,
            _ => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unit)
                ? unit
                : null
        };
    }
}

/// <summary>宏条件在 keymap、模块和界面中统一使用原始标识。</summary>
internal static class MacroConditionText
{
    private const int LegacyPlayerUnit = 31;
    private const int LegacyTargetUnit = 32;
    private const int LegacyFocusUnit = 33;
    private const int LegacyCursorUnit = 34;
    private const int LegacyMouseoverUnit = 35;
    private const int LegacyChannelingUnit = 36;
    private const int LegacyNoChannelingUnit = 37;
    public const string Channeling = "channeling";
    public const string NoChanneling = "nochanneling";

    /// <summary>兼容旧版误把引导条件写入 unit=36/37 的 keymap 与模块。</summary>
    public static (int Unit, string Condition) NormalizeLegacyUnit(int unit, string? condition)
    {
        var normalizedCondition = Normalize(condition);
        return unit switch
        {
            LegacyChannelingUnit => (ReservedUnit.None,
                normalizedCondition.Length == 0 ? Channeling : normalizedCondition),
            LegacyNoChannelingUnit => (ReservedUnit.None,
                normalizedCondition.Length == 0 ? NoChanneling : normalizedCondition),
            _ => (unit, normalizedCondition)
        };
    }

    /// <summary>
    /// 旧 keymap 没有版本字段，按 v3 解释：31-35 是保留单位，36/37 是历史宏条件。
    /// v4 起 31-40 都是团队槽位，不再执行这些兼容转换。
    /// </summary>
    public static (int Unit, string Condition) NormalizeKeymapUnit(
        int unit,
        string? condition,
        int mappingVersion)
    {
        if (mappingVersion >= ReservedUnit.CurrentMappingVersion)
        {
            return (unit, Normalize(condition));
        }

        var legacy = NormalizeLegacyUnit(unit, condition);
        return legacy.Unit switch
        {
            LegacyPlayerUnit => (ReservedUnit.Player, legacy.Condition),
            LegacyTargetUnit => (ReservedUnit.Target, legacy.Condition),
            LegacyFocusUnit => (ReservedUnit.Focus, legacy.Condition),
            LegacyCursorUnit => (ReservedUnit.Cursor, legacy.Condition),
            LegacyMouseoverUnit => (ReservedUnit.Mouseover, legacy.Condition),
            _ => legacy
        };
    }

    public static string Normalize(string? text)
    {
        var parts = (text ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.ToLowerInvariant() switch
            {
                // 只兼容读取旧版中文值；新数据及界面统一保留 WoW 宏条件名称。
                "channeling" or "引导中" => Channeling,
                "nochanneling" or "非引导" => NoChanneling,
                _ => part
            });
        return string.Join(", ", parts);
    }

    public static string ToDisplayText(string? text)
    {
        return Normalize(text);
    }

    public static string ParseDisplayText(string? text)
    {
        return Normalize(text);
    }

}
