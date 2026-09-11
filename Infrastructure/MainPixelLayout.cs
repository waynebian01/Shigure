namespace Shigure;

/// <summary>
/// 顶部主色块行的容量与索引编码契约，与插件 Fuyutsui/core/block.lua 保持一致。
/// 插件按当前专精实际占用的格数选择容量档位（默认 510，超出后升到 765，再超出升到 1020），
/// 并在最后一个数据格之后额外画一个恒定纯红的终止色块；扫描端读到纯红即结束本次主色条扫描。
/// </summary>
internal static class MainPixelLayout
{
    /// <summary>每套索引方案（红通道）容纳的索引数量。</summary>
    public const int SchemeSpan = 255;

    /// <summary>容量档位，由小到大；插件取第一个装得下的档位。</summary>
    public static readonly int[] CapacityTiers = [510, 765, 1020];

    /// <summary>最大档位，即 step 的绝对上限。</summary>
    public const int MaxCapacity = 1020;

    /// <summary>红通道可用的最大方案序号（0 表示 1..255，3 表示 766..1020）。</summary>
    public const int MaxScheme = MaxCapacity / SchemeSpan - 1;

    /// <summary>终止色块颜色：纯红 (255, 0, 0)。数据格的绿通道恒 ≥ 1，不会与之混淆。</summary>
    public const int EndMarkerRed = 255;
}
