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
        using var top = Capture(baseX, baseY, width, 1);
        var pixels = ReadPixels(top);
        return PixelScanDecoder.DecodeTopRow(pixels);
    }

    private static int? FindCountBarsMarkerY(int baseX, int baseY, int height)
    {
        using var left = Capture(baseX, baseY, 1, height);
        var leftPixels = ReadPixels(left);
        return PixelScanDecoder.FindCountBarsMarkerY(leftPixels, 1, height);
    }

    private static Dictionary<int, int> ScanLeftMarkerRow(int baseX, int rowScreenY, int width)
    {
        using var markerRow = Capture(baseX, rowScreenY, width, 1);
        var rowPixels = ReadPixels(markerRow);
        return PixelScanDecoder.DecodeMarkerRow(rowPixels);
    }

    /// <summary>
    /// 扫描 CountBars 下方的治疗吸收网格。
    /// 与层数条相同：读纯白块右侧第一个非白像素；G-1 为吸收值，B 为单位编号（1..30）。
    /// </summary>
    private static Dictionary<int, int> ScanHealAbsorbGrid(int baseX, int baseY, int width, int height, int countBarsY)
    {
        var rows = Math.Min(6, Math.Max(0, height - countBarsY - 1));
        if (rows == 0)
        {
            return new Dictionary<int, int>();
        }

        var pixels = new int[width * (1 + rows)];
        for (var row = 0; row < rows; row++)
        {
            var rowY = countBarsY + 1 + row;
            using var rowBmp = Capture(baseX, baseY + rowY, width, 1);
            ReadPixels(rowBmp).CopyTo(pixels, (row + 1) * width);
        }

        return PixelScanDecoder.DecodeHealAbsorbGrid(pixels, width, 1 + rows, 0);
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

    public void Dispose()
    {
    }
}
