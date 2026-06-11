using System.Drawing;
using System.Drawing.Imaging;

namespace Shigure;

public sealed record ScreenScanResult(IReadOnlyDictionary<int, int>? RowData, IReadOnlyDictionary<int, int> BarData);

public sealed class PixelScanner
{
    private const int PixelsPerRow = 255;
    private readonly string _windowTitle;

    public PixelScanner(string windowTitle)
    {
        _windowTitle = windowTitle;
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
        var hwnd = NativeMethods.FindWindow(null, _windowTitle);
        if (hwnd == 0 || NativeMethods.IsIconic(hwnd))
        {
            return new ScreenScanResult(null, new Dictionary<int, int>());
        }

        var point = new NativeMethods.Point(0, 0);
        if (!NativeMethods.ClientToScreen(hwnd, ref point) || !NativeMethods.GetClientRect(hwnd, out var rect))
        {
            return new ScreenScanResult(null, new Dictionary<int, int>());
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return new ScreenScanResult(null, new Dictionary<int, int>());
        }

        try
        {
            var rowData = ScanTopRow(point.X, point.Y, width);
            var barData = ScanLeftMarkerRow(point.X, point.Y, width, height);
            return new ScreenScanResult(rowData.Count == 0 ? null : rowData, barData);
        }
        catch
        {
            return new ScreenScanResult(null, new Dictionary<int, int>());
        }
    }

    private static Dictionary<int, int> ScanTopRow(int baseX, int baseY, int width)
    {
        var rowData = new Dictionary<int, int>();
        using var top = Capture(baseX, baseY, width, 1);

        var startX = -1;
        for (var x = 0; x < Math.Min(PixelsPerRow, width); x++)
        {
            var color = top.GetPixel(x, 0);
            if (IsGreenMarker(color))
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
            var color = top.GetPixel(x, 0);
            if (color.R == 0 && color.G is >= 1 and <= PixelsPerRow)
            {
                rowData[color.G] = color.B;
                if (color.G == PixelsPerRow)
                {
                    break;
                }
            }
            else if (color.G > PixelsPerRow)
            {
                break;
            }
        }

        return rowData;
    }

    private static Dictionary<int, int> ScanLeftMarkerRow(int baseX, int baseY, int width, int height)
    {
        var barData = new Dictionary<int, int>();
        using var left = Capture(baseX, baseY, 1, height);
        int? markerY = null;
        for (var y = 0; y < height; y++)
        {
            if (IsRedMarker(left.GetPixel(0, y)))
            {
                markerY = y;
                break;
            }
        }

        if (markerY is null)
        {
            return barData;
        }

        using var markerRow = Capture(baseX, baseY + markerY.Value, width, 1);
        var segIndex = 0;
        var x = 0;
        var pendingRed = false;

        while (x < width)
        {
            var color = markerRow.GetPixel(x, 0);
            if (IsGrayEndMarker(color))
            {
                break;
            }

            if (pendingRed && IsRedGreenMarker(color))
            {
                pendingRed = false;
                segIndex++;
                var (value, nextX) = ConsumeValueFrom(markerRow, x + 1, alreadySawWhite: false);
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
                var prevWhite = x > 0 && IsWhite(markerRow.GetPixel(x - 1, 0));
                if (!prevWhite)
                {
                    pendingRed = false;
                    segIndex++;
                    var (value, nextX) = ConsumeValueFrom(markerRow, x + 1, alreadySawWhite: true);
                    barData[segIndex] = Math.Max(0, value - 1);
                    x = nextX;
                    continue;
                }
            }

            x++;
        }

        return barData;
    }

    private static (int Value, int NextX) ConsumeValueFrom(Bitmap row, int fromX, bool alreadySawWhite)
    {
        var sx = fromX;
        var needWhite = !alreadySawWhite;
        while (sx < row.Width)
        {
            var color = row.GetPixel(sx, 0);
            if (IsGrayEndMarker(color))
            {
                return (0, row.Width);
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

        return (0, row.Width);
    }

    private static Bitmap Capture(int x, int y, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private static bool IsRedMarker(Color color) => color.R == 1 && color.G == 0 && color.B == 0;
    private static bool IsRedGreenMarker(Color color) => color.R == 1 && color.G == 1 && color.B == 0;
    private static bool IsWhite(Color color) => color.R == 255 && color.G == 255 && color.B == 255;
    private static bool IsGreenMarker(Color color) => color.R == 0 && color.G == 1 && color.B == 0;
    private static bool IsGrayEndMarker(Color color) => color.R == 200 && color.G == 200 && color.B == 200;
}

