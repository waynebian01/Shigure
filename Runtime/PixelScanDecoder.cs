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

        for (var x = startX; x < pixels.Length; x++)
        {
            var color = Color.FromArgb(pixels[x]);
            if (IsTopRowEndMarker(color))
            {
                break;
            }

            if (TryDecodeTopRowBlock(color, out var step, out var value))
            {
                rowData[step] = value;
                if (step == TopRowBlockCount)
                {
                    break;
                }
            }
        }

        return rowData;
    }

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
        int countBarsY)
    {
        var result = new Dictionary<int, int>();
        if (width <= 0 || height <= 0 || pixels.Length < width * height)
        {
            return result;
        }

        for (var row = 0; row < HealAbsorbMaxRows; row++)
        {
            var rowY = countBarsY + 1 + row;
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

                var (green, blue, nextX) = ConsumeHealAbsorbPixel(rowPixels, x + 1);
                if (blue is >= 1 and <= HealAbsorbMaxUnits)
                {
                    result[blue] = Math.Max(0, green - 1);
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

    private static (int Green, int Blue, int NextX) ConsumeHealAbsorbPixel(
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

            return (color.G, color.B, sx + 1);
        }

        return (0, 0, row.Length);
    }

    private static bool IsRedMarker(Color color) => color.R == 1 && color.G == 0 && color.B == 0;
    private static bool IsRedGreenMarker(Color color) => color.R == 1 && color.G == 1 && color.B == 0;
    private static bool IsWhite(Color color) => color.R == 255 && color.G == 255 && color.B == 255;
    private static bool IsGrayEndMarker(Color color) => color.R == 200 && color.G == 200 && color.B == 200;
    private static bool IsTopRowEndMarker(Color color)
        => color.R == MainPixelLayout.EndMarkerRed && color.G == 0 && color.B == 0;

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
