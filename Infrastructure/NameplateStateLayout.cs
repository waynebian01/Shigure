namespace Shigure;

/// <summary>
/// 姓名板像素布局：先 7 个单位映射格，再按槽位排列生命值/距离/战斗/光环；
/// 可选施法技能在槽位末尾占定位格及紧邻的 RGB 数据格。
/// 插件 (Fuyutsui/main.lua、nameplates.lua)、config 转换与运行时状态构建共用这套常量。
/// </summary>
internal static class NameplateStateLayout
{
    public const int SlotCount = 40;
    public const int MappingFieldCount = 7;

    /// <summary>每个单位槽位内：第 1 格生命值、第 2 格距离、第 3 格战斗，光环从第 4 格开始。</summary>
    public const int HealthPercentOffset = 1;
    public const int RangeOffset = 2;
    public const int CombatOffset = 3;
    public const int AuraStartOffset = 4;

    /// <summary>固定字段（生命值/距离/战斗）占用的像素格数。</summary>
    public const int FixedFieldCount = AuraStartOffset - 1;

    public const string ImprovedGarroteField = "强化锁喉";
    public const string ThreatField = "仇恨值";
    public const string CastSpellField = "施法技能";

    // 专精配置使用职业内的一基序号，奇袭为潜行者的第 1 专精。
    public static bool SupportsImprovedGarrote(string? classDirectory, int? classId, int? specIndex)
        => classId == 4 && specIndex == 1
            && string.Equals(Path.GetFileName(Path.GetDirectoryName(classDirectory)),
                "Fuyutsui", StringComparison.OrdinalIgnoreCase);

    public const string MappingClassification = "姓名板";

    public static readonly string[] MappingFieldNames =
    [
        "姓名板目标",
        "姓名板焦点",
        "姓名板首领1",
        "姓名板首领2",
        "姓名板首领3",
        "姓名板首领4",
        "姓名板首领5"
    ];

    public static readonly UnitTtdAlias[] UnitTtdAliases =
    [
        new("姓名板目标", "目标TTD", ClassStateCatalog.CategoryTarget),
        new("姓名板焦点", "焦点TTD", ClassStateCatalog.CategoryFocus),
        new("姓名板首领1", "首领1TTD", ClassStateCatalog.CategoryBoss1),
        new("姓名板首领2", "首领2TTD", ClassStateCatalog.CategoryBoss2),
        new("姓名板首领3", "首领3TTD", ClassStateCatalog.CategoryBoss3),
        new("姓名板首领4", "首领4TTD", ClassStateCatalog.CategoryBoss4),
        new("姓名板首领5", "首领5TTD", ClassStateCatalog.CategoryBoss5)
    ];

    public sealed record UnitTtdAlias(string MappingField, string TtdField, string Classification);

    public static int MappingPixelIndex(int regionStart, int mappingIndex1Based)
        => regionStart + mappingIndex1Based - 1;

    public static int SlotPixelIndex(int regionStart, int fieldCount, int slot, int offset)
        => regionStart + MappingFieldCount + (slot - 1) * fieldCount + offset - 1;

    public static int TotalPixelCount(int perSlotFieldCount)
        => MappingFieldCount + SlotCount * perSlotFieldCount;

    public static bool IsMappingField(string? fieldName)
    {
        var key = StripStatePrefix(fieldName);
        return MappingFieldNames.Any(name => string.Equals(name, key, StringComparison.Ordinal));
    }

    public static bool IsComputedTtdField(string? fieldName)
    {
        var key = StripStatePrefix(fieldName);
        if (UnitTtdAliases.Any(alias => string.Equals(alias.TtdField, key, StringComparison.Ordinal)))
        {
            return true;
        }

        if (!key.StartsWith("nameplates.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = key.Split('.', 3);
        return parts.Length == 3
            && int.TryParse(parts[1], out var slot)
            && slot is >= 1 and <= SlotCount
            && string.Equals(parts[2], "TTD", StringComparison.Ordinal);
    }

    private static string StripStatePrefix(string? fieldName)
    {
        var key = fieldName?.Trim() ?? string.Empty;
        return key.StartsWith("state.", StringComparison.OrdinalIgnoreCase)
            ? key["state.".Length..]
            : key;
    }
}
