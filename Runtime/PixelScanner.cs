using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Shigure;

public sealed record ScreenScanResult(
    IReadOnlyDictionary<int, int>? RowData,
    IReadOnlyDictionary<int, int> BarData,
    IReadOnlyDictionary<int, int> HealAbsorbData,
    string? FailureReason)
{
    internal nint TargetWindowHandle { get; init; }
}

public sealed class PixelScanner : IRuntimeScreenScanner
{
    private const int TopRowBlockCount = MainPixelLayout.MaxCapacity;
    private const int TopRowSchemeSpan = MainPixelLayout.SchemeSpan;
    // 第 1 格总在客户区最左侧（最小档位下也只有几个像素宽），搜索窗口不随档位放大，避免误认游戏画面。
    private const int TopRowStartSearchWidth = 510;
    private const int HealAbsorbMaxRows = 6;
    private const int HealAbsorbMaxUnits = 30;
    private readonly WowProcessLocator _processLocator;

    internal PixelScanner(WowProcessLocator processLocator)
    {
        _processLocator = processLocator;
        try
        {
            NativeMethods.SetProcessDPIAware();
        }
        catch
        {
            // DPI awareness is best effort.
        }
    }

    public ScreenScanResult ScanScreenData()
    {
        var emptyBars = new Dictionary<int, int>();
        var emptyAbsorb = new Dictionary<int, int>();
        var hwnd = _processLocator.FindFrontmostWindow();
        if (hwnd == 0)
        {
            return new ScreenScanResult(
                null,
                emptyBars,
                emptyAbsorb,
                $"未找到目标进程的可见窗口（wow_process.txt: {_processLocator.DescribeConfiguredProcesses()}）");
        }

        if (NativeMethods.IsIconic(hwnd))
        {
            return new ScreenScanResult(null, emptyBars, emptyAbsorb, "最靠前的目标进程窗口已最小化");
        }

        var point = new NativeMethods.Point(0, 0);
        if (!NativeMethods.ClientToScreen(hwnd, ref point))
        {
            return new ScreenScanResult(
                null,
                emptyBars,
                emptyAbsorb,
                $"无法获取目标窗口的屏幕坐标，Win32 错误码: {Marshal.GetLastWin32Error()}");
        }

        if (!NativeMethods.GetClientRect(hwnd, out var rect))
        {
            return new ScreenScanResult(
                null,
                emptyBars,
                emptyAbsorb,
                $"无法获取目标窗口的客户区尺寸，Win32 错误码: {Marshal.GetLastWin32Error()}");
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return new ScreenScanResult(null, emptyBars, emptyAbsorb, $"目标窗口客户区尺寸无效: {width}×{height}");
        }

        try
        {
            var rowData = ScanTopRow(point.X, point.Y, width);
            var markerY = FindCountBarsMarkerY(point.X, point.Y, height);
            var barData = markerY is null
                ? emptyBars
                : ScanLeftMarkerRow(point.X, point.Y + markerY.Value, width);
            var healAbsorbData = markerY is null
                ? emptyAbsorb
                : ScanHealAbsorbGrid(point.X, point.Y, width, height, markerY.Value);
            var result = rowData.Count == 0
                ? new ScreenScanResult(null, barData, healAbsorbData, "未找到有效的状态像素起始标记")
                : new ScreenScanResult(
                    rowData,
                    barData,
                    healAbsorbData,
                    markerY is null ? "未找到 CountBars 标记，层数条和治疗吸收数据未采集" : null);
            return result with { TargetWindowHandle = hwnd };
        }
        catch (Exception ex)
        {
            return new ScreenScanResult(null, emptyBars, emptyAbsorb, $"{ex.GetType().Name}: {ex.Message}");
        }
    }


    private static Dictionary<int, int> ScanTopRow(int baseX, int baseY, int width)
    {
        var rowData = new Dictionary<int, int>();
        using var top = Capture(baseX, baseY, width, 1);
        var pixels = ReadPixels(top);

        var startX = -1;
        for (var x = 0; x < Math.Min(TopRowStartSearchWidth, width); x++)
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

        for (var x = startX; x < width; x++)
        {
            var color = Color.FromArgb(pixels[x]);
            // 插件在当前容量档位的最后一格画恒定纯红；读到它就是整行的终点。
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

    private static int? FindCountBarsMarkerY(int baseX, int baseY, int height)
    {
        using var left = Capture(baseX, baseY, 1, height);
        var leftPixels = ReadPixels(left);
        for (var y = 0; y < height; y++)
        {
            if (IsRedMarker(Color.FromArgb(leftPixels[y])))
            {
                return y;
            }
        }

        return null;
    }

    private static Dictionary<int, int> ScanLeftMarkerRow(int baseX, int rowScreenY, int width)
    {
        var barData = new Dictionary<int, int>();
        using var markerRow = Capture(baseX, rowScreenY, width, 1);
        var rowPixels = ReadPixels(markerRow);
        var segIndex = 0;
        var x = 0;
        var pendingRed = false;

        while (x < width)
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

    /// <summary>
    /// 扫描 CountBars 下方的治疗吸收网格。
    /// 与层数条相同：读纯白块右侧第一个非白像素；G-1 为吸收值，B 为单位编号（1..30）。
    /// </summary>
    private static Dictionary<int, int> ScanHealAbsorbGrid(int baseX, int baseY, int width, int height, int countBarsY)
    {
        var result = new Dictionary<int, int>();

        for (var row = 0; row < HealAbsorbMaxRows; row++)
        {
            var rowY = countBarsY + 1 + row;
            if (rowY >= height)
            {
                break;
            }

            using var rowBmp = Capture(baseX, baseY + rowY, width, 1);
            var rowPixels = ReadPixels(rowBmp);
            var x = 0;
            while (x < width)
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

    private static (int Value, int NextX) ConsumeValueFrom(int[] row, int fromX, bool alreadySawWhite)
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

    /// <summary>
    /// 白条已开始：跳过后续白像素，读右侧第一个非白像素的 G（吸收索引）与 B（单位编号）。
    /// </summary>
    private static (int Green, int Blue, int NextX) ConsumeHealAbsorbPixel(int[] row, int fromX)
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

    private static Bitmap Capture(int x, int y, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    // 32bpp 位图 stride 恒为 width*4(无填充), 一次 LockBits + Marshal.Copy 读完整张为 0xAARRGGBB,
    // 取代逐像素 GetPixel(每次都会 Lock/UnlockBits)。Color.FromArgb 还原后 R/G/B 与原先一致。
    private static int[] ReadPixels(Bitmap bitmap)
    {
        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new int[bitmap.Width * bitmap.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            return pixels;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
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

        // 红通道是索引方案序号：0 → 1..255，1 → 256..510，2 → 511..765，3 → 766..1020。
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
