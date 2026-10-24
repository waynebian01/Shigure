namespace Shigure;

/// <summary>正式服时间轴固定五个事件，每个事件占用两格技能 ID 和一格剩余时间。</summary>
internal static class EncounterTimelineStateLayout
{
    public const int SlotCount = 5;
    public const int PixelsPerSlot = 3;
    public const int PixelCount = SlotCount * PixelsPerSlot;
    public const string Category = "时间轴";
    public const string AnyClassification = "任意时间轴";
    public const string AnySpellIdField = "timeline.any.spellID";
    public const string AnyRemainingField = "timeline.any.remaining";

    public static readonly string[] Classifications = Enumerable.Range(1, SlotCount)
        .Select(slot => $"时间轴{slot}").Append(AnyClassification).ToArray();

    public static string SpellIdField(int slot) => $"时间轴{slot}技能ID";
    public static string RemainingField(int slot) => $"时间轴{slot}剩余时间";

    // slot=0 表示任意事件；固定槽仍使用原字段键，兼容已有模块条件。
    public static bool TryParseField(string? name, out int slot, out bool remaining)
    {
        var key = NormalizeFieldName(name);
        slot = 0;
        remaining = key.Equals(AnyRemainingField, StringComparison.OrdinalIgnoreCase);
        if (remaining || key.Equals(AnySpellIdField, StringComparison.OrdinalIgnoreCase)) return true;
        if (!key.StartsWith(Category, StringComparison.Ordinal)) return false;
        for (var candidate = 1; candidate <= SlotCount; candidate++)
        {
            remaining = key == RemainingField(candidate);
            if (remaining || key == SpellIdField(candidate))
            {
                slot = candidate;
                return true;
            }
        }
        return false;
    }

    public static bool IsAnyField(string? name)
    {
        var key = NormalizeFieldName(name);
        return key.Equals(AnySpellIdField, StringComparison.OrdinalIgnoreCase)
            || key.Equals(AnyRemainingField, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFieldName(string? name)
    {
        var key = name?.Trim() ?? string.Empty;
        return key.StartsWith("state.", StringComparison.OrdinalIgnoreCase) ? key["state.".Length..] : key;
    }

    public static string Classification(int slot) => slot == 0 ? AnyClassification : $"时间轴{slot}";

    public static bool IsField(string name)
        => TryParseField(name, out _, out _);

    public static bool IsSupported(string? classDirectory)
        => string.Equals(Path.GetFileName(Path.GetDirectoryName(classDirectory)),
            "Fuyutsui", StringComparison.OrdinalIgnoreCase);
}
