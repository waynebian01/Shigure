namespace Shigure;

/// <summary>
/// 姓名板像素布局固定：每个单位第 1 格生命值、第 2 格距离、第 3 格战斗，光环从第 4 格开始。
/// 插件 (Fuyutsui/main.lua)、config 转换与运行时状态构建共用这套常量，不再随专精配置变化。
/// </summary>
internal static class NameplateStateLayout
{
    public const int HealthPercentOffset = 1;
    public const int RangeOffset = 2;
    public const int CombatOffset = 3;
    public const int AuraStartOffset = 4;

    /// <summary>固定字段（生命值/距离/战斗）占用的像素格数。</summary>
    public const int FixedFieldCount = AuraStartOffset - 1;
}
