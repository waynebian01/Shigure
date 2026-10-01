using System.Drawing;

namespace Shigure;

/// <summary>
/// 对来自不同捕获后端的 0xAARRGGBB 扫描线执行统一解码。
/// </summary>
internal static class PixelScanDecoder
{
    private const int TopRowBlockCount = MainPixelLayout.MaxCapacity;
    private const int TopRowSchemeSpan = MainPixelLayout.SchemeSpan;
    // 第 1 格总在客户区最左侧（最小档位下也只有几个像素宽），搜索窗口不随档位放大，避免误认游戏画面。
    private const int TopRowStartSearchWidth = 510;
    private const int HealAbsorbMaxRows = 8;
    private const int HealAbsorbMaxUnits = GroupStateLayout.SlotCount;
    private const int HealAbsorbBarUnits = 25;
    private const int HealAbsorbPercentPerUnit = 100 / HealAbsorbBarUnits;

    public static Dictionary<int, int> DecodeTopRow(ReadOnlySpan<int> pixels)
    {
        var rowData = new Dictionary<int, int>();
        var startX = -1;
        for (var x = 0; x < Math.Min(TopRowStartSearchWidth, pixels.Length); x++)
        {
            var color = Color.FromArgb(pixels[x]);
            if (TryDecodeTopRowBlock(color, out var step, out _) && step == 1)
            {
                startX = x;
                break;
            }
        }

        if (startX < 0)
        {
            return rowData;
        }

        for (var x = startX; x < pixels.Length;)
        {
            var color = Color.FromArgb(pixels[x]);
            if (IsTopRowEndMarker(color))
            {
                break;
            }

            if (TryDecodeRgbSpellMarker(color, out var rgbStep, out var capacity))
            {
                // 标记格携带档位；直接采样相邻格中央，避免数据 RGB 被误认成普通格或终止格。
                var divisor = capacity + 1;
                var payloadX = (int)(((long)(2 * rgbStep + 1) * pixels.Length) / (2L * divisor));
                if (payloadX < pixels.Length)
                {
                    rowData[rgbStep] = pixels[payloadX] & 0xFFFFFF;
                }

                // 截屏像素按中心点归属纹理；从下一格的第一个像素继续扫描。
                var nextCellNumerator = 2L * (rgbStep + 1) * pixels.Length - divisor;
                var payloadEnd = (int)((nextCellNumerator + 2L * divisor - 1) / (2L * divisor));
                x = Math.Max(x + 1, payloadEnd);
                continue;
            }

            if (TryDecodeTopRowBlock(color, out var step, out var value))
            {
                rowData[step] = value;
                if (step == TopRowBlockCount)
                {
                    break;
                }
            }

            x++;
        }

        return rowData;
    }

    public static int? FindHealAbsorbGridY(ReadOnlySpan<int> pixels, int width, int height)
    {
        if (width <= 0 || height <= 1 || pixels.Length < width * height)
        {
            return null;
        }

        // 首槽始终为 player 或 raid1，其前锚点编码为 (0, 1, 0)。跳过顶部状态行。
        for (var y = 1; y < height; y++)
        {
            var color = Color.FromArgb(pixels[y * width]);
            if (color.R == 0 && color.G == 1 && color.B == 0)
            {
                return y;
            }
        }

        return null;
    }

    // 兼容旧版插件的独立计数条定位格。
    public static int? FindCountBarsMarkerY(ReadOnlySpan<int> pixels, int width, int height)
    {
        if (width <= 0 || height <= 0 || pixels.Length < width * height)
        {
            return null;
        }

        for (var y = 0; y < height; y++)
        {
            if (IsRedMarker(Color.FromArgb(pixels[y * width])))
            {
                return y;
            }
        }

        return null;
    }

    public static Dictionary<int, int> DecodeMarkerRow(ReadOnlySpan<int> rowPixels)
    {
        var barData = new Dictionary<int, int>();
        var segIndex = 0;
        var x = 0;
        var pendingRed = false;

        while (x < rowPixels.Length)
        {
            var color = Color.FromArgb(rowPixels[x]);
            if (IsGrayEndMarker(color))
            {
                break;
            }

            if (pendingRed && IsRedGreenMarker(color))
            {
                pendingRed = false;
                segIndex++;
                var (value, nextX) = ConsumeValueFrom(rowPixels, x + 1, alreadySawWhite: false);
                barData[segIndex] = Math.Max(0, value - 1);
                x = nextX;
                continue;
            }

            if (IsRedMarker(color))
            {
                pendingRed = true;
                x++;
                continue;
            }

            if (IsWhite(color))
            {
                var prevWhite = x > 0 && IsWhite(Color.FromArgb(rowPixels[x - 1]));
                if (!prevWhite)
                {
                    pendingRed = false;
                    segIndex++;
                    var (value, nextX) = ConsumeValueFrom(rowPixels, x + 1, alreadySawWhite: true);
                    barData[segIndex] = Math.Max(0, value - 1);
                    x = nextX;
                    continue;
                }
            }

            x++;
        }

        return barData;
    }

    public static Dictionary<int, int> DecodeHealAbsorbGrid(
        ReadOnlySpan<int> pixels,
        int width,
        int height,
        int firstRowY)
    {
        var result = new Dictionary<int, int>();
        if (width <= 0 || height <= 0 || pixels.Length < width * height)
        {
            return result;
        }

        for (var row = 0; row < HealAbsorbMaxRows; row++)
        {
            var rowY = firstRowY + row;
            if (rowY >= height)
            {
                break;
            }

            var rowPixels = pixels.Slice(rowY * width, width);
            var x = 0;
            while (x < rowPixels.Length)
            {
                var color = Color.FromArgb(rowPixels[x]);
                if (!IsWhite(color))
                {
                    x++;
                    continue;
                }

                var prevWhite = x > 0 && IsWhite(Color.FromArgb(rowPixels[x - 1]));
                if (prevWhite)
                {
                    x++;
                    continue;
                }

                var (sample, nextX) = ConsumeHealAbsorbPixel(rowPixels, x + 1);
                if (sample.R <= 1 &&
                    sample.G is >= 1 and <= HealAbsorbBarUnits + 1 &&
                    sample.B is >= 1 and <= HealAbsorbMaxUnits)
                {
                    result[sample.B] = Math.Clamp(
                        (sample.G - 1) * HealAbsorbPercentPerUnit, 0, 100);
                }

                x = nextX;
            }
        }

        return result;
    }

    private static (int Value, int NextX) ConsumeValueFrom(
        ReadOnlySpan<int> row,
        int fromX,
        bool alreadySawWhite)
    {
        var sx = fromX;
        var needWhite = !alreadySawWhite;
        while (sx < row.Length)
        {
            var color = Color.FromArgb(row[sx]);
            if (IsGrayEndMarker(color))
            {
                return (0, row.Length);
            }

            if (IsRedMarker(color))
            {
                return (0, sx);
            }

            if (needWhite)
            {
                if (IsWhite(color))
                {
                    needWhite = false;
                }

                sx++;
                continue;
            }

            if (IsWhite(color))
            {
                sx++;
                continue;
            }

            return (color.G, sx + 1);
        }

        return (0, row.Length);
    }

    private static (Color Color, int NextX) ConsumeHealAbsorbPixel(
        ReadOnlySpan<int> row,
        int fromX)
    {
        var sx = fromX;
        while (sx < row.Length)
        {
            var color = Color.FromArgb(row[sx]);
            if (IsWhite(color))
            {
                sx++;
                continue;
            }

            return (color, sx + 1);
        }

        return (Color.Empty, row.Length);
    }

    private static bool IsRedMarker(Color color) => color.R == 1 && color.G == 0 && color.B == 0;
    private static bool IsRedGreenMarker(Color color) => color.R == 1 && color.G == 1 && color.B == 0;
    private static bool IsWhite(Color color) => color.R == 255 && color.G == 255 && color.B == 255;
    private static bool IsGrayEndMarker(Color color) => color.R == 200 && color.G == 200 && color.B == 200;
    private static bool IsTopRowEndMarker(Color color)
        => color.R == MainPixelLayout.EndMarkerRed && color.G == 0 && color.B == 0;

    private static bool TryDecodeRgbSpellMarker(Color color, out int step, out int capacity)
    {
        step = 0;
        capacity = 0;
        if (color.R is < 4 or > 7 || color.G == 0 || color.B is < 2 or > 4)
        {
            return false;
        }

        step = (color.R - 4) * TopRowSchemeSpan + color.G;
        capacity = color.B * TopRowSchemeSpan;
        return step >= 1 && step < capacity && capacity <= TopRowBlockCount;
    }

    private static bool TryDecodeTopRowBlock(Color color, out int step, out int value)
    {
        step = 0;
        value = 0;

        if (color.G is < 1 or > TopRowSchemeSpan || color.R > MainPixelLayout.MaxScheme)
        {
            return false;
        }

        step = (color.R * TopRowSchemeSpan) + color.G;
        if (step is < 1 or > TopRowBlockCount)
        {
            step = 0;
            return false;
        }

        value = color.B;
        return true;
    }
}
