using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Shigure;

public sealed record AuraRecognitionScanResult(
    bool WindowFound,
    bool MarkerFound,
    Rectangle? ClientBounds,
    Point? MarkerLocation,
    int MarkerCellSize,
    double Scale,
    string TemplateDirectory,
    int TemplateCount,
    IReadOnlyList<AuraSlotRecognition> Slots,
    string Message);

public sealed record AuraSlotRecognition(
    string Row,
    int Index,
    int RemainingB,
    ulong IconHash,
    Rectangle SlotBounds,
    Rectangle IconBounds,
    string? SavedIconPath,
    string? Name,
    int? HashDistance,
    double? TemplateScore,
    IReadOnlyList<AuraRecognitionCandidate> Candidates);

public sealed record AuraRecognitionCandidate(
    string Name,
    int HashDistance,
    double TemplateScore,
    string TemplatePath);

public sealed class AuraIconRecognizer : IDisposable
{
    private const int BuffRowR = 2;
    private const int DebuffRowR = 3;
    private const int BaseSlotSize = 28;
    private const int BaseBorderWidth = 2;
    private const int BaseWhiteBorderWidth = 2;
    private const int MatchSize = 32;
    public const int DefaultMaxHashDistance = 10;
    public const int MaxHashDistance = 64;
    private const int DefaultTopCandidates = 12;
    private const int DefaultMaxSlotsPerRow = 32;
    private const int MinSlotSize = 16;
    private const int MaxSlotSize = 112;
    private const int LocalScanPaddingX = 160;
    private const int LocalScanPaddingY = 80;
    private const int MinLocalScanWidth = 960;
    private const int MinLocalScanHeight = 240;
    private const int MaxLocalizedMisses = 2;
    private const int FullScanInterval = 10;
    private const double MinEdgeMatchRatio = 0.82;
    private const double MinWhiteMatchRatio = 0.62;

    private readonly string _windowTitle;
    private readonly string _templateDirectory;
    private readonly string _auraDirectory;
    private readonly int _topCandidates;
    private readonly int _maxSlotsPerRow;
    private readonly int _maxHashDistance;
    private List<AuraTemplate>? _templates;
    private Rectangle? _preferredClientScanBounds;
    private int _localizedMisses;
    private int _localizedScansSinceFull;

    public AuraIconRecognizer(
        string windowTitle,
        string templateDirectory,
        int topCandidates = DefaultTopCandidates,
        int maxSlotsPerRow = DefaultMaxSlotsPerRow,
        int maxHashDistance = DefaultMaxHashDistance,
        string? auraDirectory = null)
    {
        _windowTitle = string.IsNullOrWhiteSpace(windowTitle) ? "魔兽世界" : windowTitle.Trim();
        _templateDirectory = string.IsNullOrWhiteSpace(templateDirectory)
            ? DefaultAuraDirectory
            : templateDirectory.Trim();
        _auraDirectory = string.IsNullOrWhiteSpace(auraDirectory)
            ? DefaultTempAuraDirectory
            : auraDirectory.Trim();
        _topCandidates = Math.Clamp(topCandidates, 1, 64);
        _maxSlotsPerRow = Math.Clamp(maxSlotsPerRow, 1, 255);
        _maxHashDistance = Math.Clamp(maxHashDistance, 0, MaxHashDistance);

        try
        {
            NativeMethods.SetProcessDPIAware();
        }
        catch
        {
            // DPI awareness is best effort.
        }
    }

    public static string DefaultTemplateDirectory => DefaultAuraDirectory;
    public static string DefaultTempAuraDirectory => Path.Combine(AppPaths.BaseDirectory, "tmp", "auras");
    public static string DefaultAuraDirectory => Path.Combine(AppPaths.BaseDirectory, "auras");

    public AuraRecognitionScanResult Scan()
    {
        var hwnd = NativeMethods.FindWindow(null, _windowTitle);
        if (hwnd == 0 || NativeMethods.IsIconic(hwnd))
        {
            return Empty(windowFound: false, markerFound: false, "未找到目标窗口");
        }

        var point = new NativeMethods.Point(0, 0);
        if (!NativeMethods.ClientToScreen(hwnd, ref point) || !NativeMethods.GetClientRect(hwnd, out var rect))
        {
            return Empty(windowFound: true, markerFound: false, "无法读取目标窗口区域");
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        var clientBounds = new Rectangle(point.X, point.Y, width, height);
        if (width <= 0 || height <= 0)
        {
            return Empty(windowFound: true, markerFound: false, "目标窗口区域为空", clientBounds);
        }

        List<AuraTemplate> templates;
        try
        {
            templates = EnsureTemplates();
        }
        catch (Exception ex)
        {
            return Empty(windowFound: true, markerFound: false, $"无法读取已命名图标目录: {ex.Message}", clientBounds);
        }

        var captureBounds = GetCaptureClientBounds(width, height);
        var captureOffset = new Point(captureBounds.X, captureBounds.Y);
        using var capture = Capture(
            point.X + captureBounds.X,
            point.Y + captureBounds.Y,
            captureBounds.Width,
            captureBounds.Height);
        var pixels = ReadPixels(capture);
        var detectedSlots = FindEncodedSlots(pixels, capture.Width, capture.Height);
        if (detectedSlots.Count == 0)
        {
            RegisterScanMiss(IsLocalizedCapture(captureBounds, width, height));
            return new AuraRecognitionScanResult(
                true,
                false,
                clientBounds,
                null,
                0,
                1,
                _templateDirectory,
                templates.Count,
                [],
                "未找到新布局光环定位框");
        }

        RegisterScanHit(detectedSlots, captureOffset, width, height);

        var slots = OffsetSlots(ScanSlots(capture, detectedSlots, templates), captureOffset);
        var message = templates.Count == 0
            ? "已定位新布局，已命名图标为空"
            : slots.Count == 0
                ? "已定位新布局，未检测到可裁剪的光环槽位"
                : $"已识别 {slots.Count} 个光环槽位";

        var anchor = OffsetPoint(GetLayoutAnchor(detectedSlots), captureOffset);
        var borderWidth = Math.Max(1, (int)Math.Round(detectedSlots.Average(slot => slot.BorderWidth)));
        var scale = detectedSlots.Average(slot => slot.Scale);

        return new AuraRecognitionScanResult(
            true,
            true,
            clientBounds,
            anchor,
            borderWidth,
            scale,
            _templateDirectory,
            templates.Count,
            slots,
            message);
    }

    public void ReloadTemplates()
    {
        DisposeTemplates();
        _templates = LoadTemplates(_templateDirectory);
    }

    public void Dispose()
    {
        DisposeTemplates();
        GC.SuppressFinalize(this);
    }

    private List<AuraTemplate> EnsureTemplates()
    {
        _templates ??= LoadTemplates(_templateDirectory);
        return _templates;
    }

    private void DisposeTemplates()
    {
        if (_templates is null)
        {
            return;
        }

        foreach (var template in _templates)
        {
            template.Dispose();
        }

        _templates = null;
    }

    private Rectangle GetCaptureClientBounds(int clientWidth, int clientHeight)
    {
        var fullClient = new Rectangle(0, 0, clientWidth, clientHeight);
        if (_preferredClientScanBounds is null
            || _localizedMisses >= MaxLocalizedMisses
            || _localizedScansSinceFull >= FullScanInterval)
        {
            _localizedScansSinceFull = 0;
            return fullClient;
        }

        _localizedScansSinceFull++;
        return ClampToClient(_preferredClientScanBounds.Value, clientWidth, clientHeight);
    }

    private static bool IsLocalizedCapture(Rectangle captureBounds, int clientWidth, int clientHeight)
        => captureBounds.X != 0
            || captureBounds.Y != 0
            || captureBounds.Width != clientWidth
            || captureBounds.Height != clientHeight;

    private void RegisterScanMiss(bool wasLocalizedCapture)
    {
        if (wasLocalizedCapture)
        {
            _localizedMisses++;
            if (_localizedMisses >= MaxLocalizedMisses)
            {
                _preferredClientScanBounds = null;
            }

            return;
        }

        _localizedMisses = 0;
        _preferredClientScanBounds = null;
    }

    private void RegisterScanHit(
        IReadOnlyList<DetectedAuraSlot> detectedSlots,
        Point captureOffset,
        int clientWidth,
        int clientHeight)
    {
        _localizedMisses = 0;
        if (detectedSlots.Count == 0)
        {
            return;
        }

        var left = detectedSlots.Min(slot => slot.SlotBounds.Left + captureOffset.X);
        var top = detectedSlots.Min(slot => slot.SlotBounds.Top + captureOffset.Y);
        var right = detectedSlots.Max(slot => slot.SlotBounds.Right + captureOffset.X);
        var bottom = detectedSlots.Max(slot => slot.SlotBounds.Bottom + captureOffset.Y);
        _preferredClientScanBounds = BuildPreferredScanBounds(
            Rectangle.FromLTRB(left, top, right, bottom),
            clientWidth,
            clientHeight);
    }

    private static Rectangle BuildPreferredScanBounds(Rectangle contentBounds, int clientWidth, int clientHeight)
    {
        var expanded = Rectangle.FromLTRB(
            contentBounds.Left - LocalScanPaddingX,
            contentBounds.Top - LocalScanPaddingY,
            contentBounds.Right + LocalScanPaddingX,
            contentBounds.Bottom + LocalScanPaddingY);

        if (expanded.Width < MinLocalScanWidth)
        {
            expanded = ExpandToWidth(expanded, MinLocalScanWidth);
        }

        if (expanded.Height < MinLocalScanHeight)
        {
            expanded = ExpandToHeight(expanded, MinLocalScanHeight);
        }

        return ClampToClient(expanded, clientWidth, clientHeight);
    }

    private static Rectangle ExpandToWidth(Rectangle rectangle, int targetWidth)
    {
        var extra = targetWidth - rectangle.Width;
        return Rectangle.FromLTRB(
            rectangle.Left - extra / 2,
            rectangle.Top,
            rectangle.Right + extra - extra / 2,
            rectangle.Bottom);
    }

    private static Rectangle ExpandToHeight(Rectangle rectangle, int targetHeight)
    {
        var extra = targetHeight - rectangle.Height;
        return Rectangle.FromLTRB(
            rectangle.Left,
            rectangle.Top - extra / 2,
            rectangle.Right,
            rectangle.Bottom + extra - extra / 2);
    }

    private static Rectangle ClampToClient(Rectangle rectangle, int clientWidth, int clientHeight)
    {
        var width = Math.Min(clientWidth, Math.Max(1, rectangle.Width));
        var height = Math.Min(clientHeight, Math.Max(1, rectangle.Height));
        var x = Math.Clamp(rectangle.X, 0, Math.Max(0, clientWidth - width));
        var y = Math.Clamp(rectangle.Y, 0, Math.Max(0, clientHeight - height));
        return new Rectangle(x, y, width, height);
    }

    private static Point OffsetPoint(Point point, Point offset) => new(point.X + offset.X, point.Y + offset.Y);

    private static Rectangle OffsetRectangle(Rectangle rectangle, Point offset)
        => new(rectangle.X + offset.X, rectangle.Y + offset.Y, rectangle.Width, rectangle.Height);

    private static List<AuraSlotRecognition> OffsetSlots(List<AuraSlotRecognition> slots, Point offset)
    {
        if (offset == Point.Empty)
        {
            return slots;
        }

        return slots
            .Select(slot => slot with
            {
                SlotBounds = OffsetRectangle(slot.SlotBounds, offset),
                IconBounds = OffsetRectangle(slot.IconBounds, offset)
            })
            .ToList();
    }

    private AuraRecognitionScanResult Empty(
        bool windowFound,
        bool markerFound,
        string message,
        Rectangle? clientBounds = null)
    {
        return new AuraRecognitionScanResult(
            windowFound,
            markerFound,
            clientBounds,
            null,
            0,
            1,
            _templateDirectory,
            0,
            [],
            message);
    }

    private List<AuraSlotRecognition> ScanSlots(
        Bitmap capture,
        IReadOnlyList<DetectedAuraSlot> detectedSlots,
        IReadOnlyList<AuraTemplate> templates)
    {
        var slots = new List<AuraSlotRecognition>();
        var auraDirectory = _auraDirectory;
        try
        {
            Directory.CreateDirectory(auraDirectory);
        }
        catch
        {
            auraDirectory = string.Empty;
        }

        foreach (var detected in detectedSlots)
        {
            if (!IsInside(detected.IconBounds, capture.Width, capture.Height))
            {
                continue;
            }

            using var icon = capture.Clone(detected.IconBounds, PixelFormat.Format32bppArgb);
            var iconHash = ComputeDHash(icon);
            var savedIconPath = SaveAuraIcon(icon, iconHash, auraDirectory);
            var candidates = RecognizeIcon(icon, iconHash, templates);
            var best = candidates.FirstOrDefault();

            slots.Add(new AuraSlotRecognition(
                RowName(detected.RowR),
                detected.Index,
                detected.RemainingB,
                iconHash,
                detected.SlotBounds,
                detected.IconBounds,
                savedIconPath,
                best?.Name,
                best?.HashDistance,
                best?.TemplateScore,
                candidates));
        }

        return slots;
    }

    private static string? SaveAuraIcon(
        Bitmap icon,
        ulong iconHash,
        string auraDirectory)
    {
        if (string.IsNullOrWhiteSpace(auraDirectory))
        {
            return null;
        }

        try
        {
            var fileName = $"{FormatHash(iconHash)}.png";
            var path = Path.Combine(auraDirectory, fileName);
            if (File.Exists(path))
            {
                return path;
            }

            icon.Save(path, ImageFormat.Png);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private List<AuraRecognitionCandidate> RecognizeIcon(
        Bitmap icon,
        ulong iconHash,
        IReadOnlyList<AuraTemplate> templates)
    {
        if (templates.Count == 0)
        {
            return [];
        }

        var byHash = templates
            .Select(template => new
            {
                Template = template,
                HashDistance = HammingDistance(iconHash, template.DHash)
            })
            .Where(match => match.HashDistance <= _maxHashDistance)
            .OrderBy(match => match.HashDistance)
            .Take(_topCandidates)
            .ToList();

        return byHash
            .Select(match => new AuraRecognitionCandidate(
                match.Template.Name,
                match.HashDistance,
                TemplateSimilarity(icon, match.Template.Bitmap),
                match.Template.Path))
            .OrderByDescending(candidate => candidate.TemplateScore)
            .ThenBy(candidate => candidate.HashDistance)
            .ToList();
    }

    private static List<AuraTemplate> LoadTemplates(string templateDirectory)
    {
        Directory.CreateDirectory(templateDirectory);
        var templates = new List<AuraTemplate>();
        var names = AuraNameStore.Load(AuraNameStore.GetNameFilePath(templateDirectory));
        foreach (var (hash, name) in names.OrderBy(pair => pair.Value, StringComparer.CurrentCultureIgnoreCase))
        {
            if (!IsHashString(hash))
            {
                continue;
            }

            var path = Path.Combine(templateDirectory, $"{hash}.png");
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using var source = Image.FromFile(path);
                var bitmap = new Bitmap(source);
                templates.Add(new AuraTemplate(
                    name,
                    path,
                    Convert.ToUInt64(hash, 16),
                    bitmap));
            }
            catch
            {
                // Ignore broken or unsupported image files so one bad template does not disable scanning.
            }
        }

        return templates;
    }

    private static bool IsHashString(string value)
        => value.Length == 16 && value.All(Uri.IsHexDigit);

    private List<DetectedAuraSlot> FindEncodedSlots(int[] pixels, int width, int height)
    {
        var slots = new List<DetectedAuraSlot>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = GetArgb(pixels, width, x, y);
                if (!TryReadEncodedSlotColor(color, _maxSlotsPerRow, out _, out _, out _))
                {
                    continue;
                }

                if (x > 0 && IsSameRgb(GetArgb(pixels, width, x - 1, y), color))
                {
                    continue;
                }

                if (y > 0 && IsSameRgb(GetArgb(pixels, width, x, y - 1), color))
                {
                    continue;
                }

                if (TryReadEncodedSlotAt(pixels, width, height, x, y, color, out var slot))
                {
                    slots.Add(slot);
                }
            }
        }

        return DeduplicateSlots(slots)
            .OrderBy(slot => slot.SlotBounds.Top)
            .ThenBy(slot => slot.SlotBounds.Left)
            .ThenBy(slot => slot.Index)
            .ToList();
    }

    private bool TryReadEncodedSlotAt(
        int[] pixels,
        int width,
        int height,
        int x,
        int y,
        int color,
        out DetectedAuraSlot slot)
    {
        slot = default!;
        if (!TryReadEncodedSlotColor(color, _maxSlotsPerRow, out var rowR, out var index, out var remainingB))
        {
            return false;
        }

        var slotWidth = CountRunRight(pixels, width, x, y, color, MaxSlotSize + 1);
        var slotHeight = CountRunDown(pixels, width, height, x, y, color, MaxSlotSize + 1);
        if (slotWidth < MinSlotSize || slotHeight < MinSlotSize || slotWidth > MaxSlotSize || slotHeight > MaxSlotSize)
        {
            return false;
        }

        var sizeDelta = Math.Abs(slotWidth - slotHeight);
        if (sizeDelta > Math.Max(5, Math.Max(slotWidth, slotHeight) / 5))
        {
            return false;
        }

        var slotBounds = new Rectangle(x, y, slotWidth, slotHeight);
        if (!IsInside(slotBounds, width, height))
        {
            return false;
        }

        if (MatchRatioHorizontal(pixels, width, x, y, slotWidth, color) < MinEdgeMatchRatio
            || MatchRatioHorizontal(pixels, width, x, y + slotHeight - 1, slotWidth, color) < MinEdgeMatchRatio
            || MatchRatioVertical(pixels, width, x, y, slotHeight, color) < MinEdgeMatchRatio
            || MatchRatioVertical(pixels, width, x + slotWidth - 1, y, slotHeight, color) < MinEdgeMatchRatio)
        {
            return false;
        }

        var maxBorderWidth = Math.Max(2, Math.Min(slotWidth, slotHeight) / 4);
        var topBorder = MeasureTopBorder(pixels, width, x, y, slotWidth, color, maxBorderWidth);
        var bottomBorder = MeasureBottomBorder(pixels, width, x, y, slotWidth, slotHeight, color, maxBorderWidth);
        var leftBorder = MeasureLeftBorder(pixels, width, x, y, slotHeight, color, maxBorderWidth);
        var rightBorder = MeasureRightBorder(pixels, width, x, y, slotWidth, slotHeight, color, maxBorderWidth);
        if (topBorder <= 0 || bottomBorder <= 0 || leftBorder <= 0 || rightBorder <= 0)
        {
            return false;
        }

        var innerBounds = Rectangle.FromLTRB(
            x + leftBorder,
            y + topBorder,
            x + slotWidth - rightBorder,
            y + slotHeight - bottomBorder);
        if (innerBounds.Width < 8 || innerBounds.Height < 8)
        {
            return false;
        }

        if (WhiteRatioHorizontal(pixels, width, innerBounds.Left, innerBounds.Top, innerBounds.Width) < MinWhiteMatchRatio
            || WhiteRatioVertical(pixels, width, innerBounds.Left, innerBounds.Top, innerBounds.Height) < MinWhiteMatchRatio)
        {
            return false;
        }

        var scale = Math.Max(slotWidth, slotHeight) / (double)BaseSlotSize;
        var expectedWhiteWidth = ScaleValue(BaseWhiteBorderWidth, scale);
        var maxWhiteWidth = Math.Max(1, expectedWhiteWidth + 1);
        var whiteLeft = FallbackIfZero(MeasureWhiteLeft(pixels, width, innerBounds, maxWhiteWidth), expectedWhiteWidth);
        var whiteRight = FallbackIfZero(MeasureWhiteRight(pixels, width, innerBounds, maxWhiteWidth), expectedWhiteWidth);
        var whiteTop = FallbackIfZero(MeasureWhiteTop(pixels, width, innerBounds, maxWhiteWidth), expectedWhiteWidth);
        var whiteBottom = FallbackIfZero(MeasureWhiteBottom(pixels, width, innerBounds, maxWhiteWidth), expectedWhiteWidth);

        var iconBounds = Rectangle.FromLTRB(
            innerBounds.Left + whiteLeft,
            innerBounds.Top + whiteTop,
            innerBounds.Right - whiteRight,
            innerBounds.Bottom - whiteBottom);
        if (iconBounds.Width < 8 || iconBounds.Height < 8 || !IsInside(iconBounds, width, height))
        {
            return false;
        }

        var borderWidth = Math.Max(1, (topBorder + bottomBorder + leftBorder + rightBorder) / 4);
        slot = new DetectedAuraSlot(rowR, index, remainingB, slotBounds, iconBounds, borderWidth, scale);
        return true;
    }

    private static List<DetectedAuraSlot> DeduplicateSlots(IEnumerable<DetectedAuraSlot> slots)
    {
        var result = new List<DetectedAuraSlot>();
        foreach (var slot in slots.OrderBy(slot => slot.SlotBounds.Top).ThenBy(slot => slot.SlotBounds.Left))
        {
            if (result.Any(existing => existing.SlotBounds.IntersectsWith(slot.SlotBounds)))
            {
                continue;
            }

            result.Add(slot);
        }

        return result;
    }

    private static bool TryReadEncodedSlotColor(
        int argb,
        int maxSlotsPerRow,
        out int rowR,
        out int index,
        out int remainingB)
    {
        rowR = GetR(argb);
        index = GetG(argb);
        remainingB = GetB(argb);
        return rowR is BuffRowR or DebuffRowR
            && index >= 1
            && index <= maxSlotsPerRow;
    }

    private static int CountRunRight(int[] pixels, int width, int x, int y, int color, int maxCount)
    {
        var count = 0;
        while (x + count < width && count < maxCount && IsSameRgb(GetArgb(pixels, width, x + count, y), color))
        {
            count++;
        }

        return count;
    }

    private static int CountRunDown(int[] pixels, int width, int height, int x, int y, int color, int maxCount)
    {
        var count = 0;
        while (y + count < height && count < maxCount && IsSameRgb(GetArgb(pixels, width, x, y + count), color))
        {
            count++;
        }

        return count;
    }

    private static int MeasureTopBorder(
        int[] pixels,
        int width,
        int x,
        int y,
        int slotWidth,
        int color,
        int maxBorderWidth)
    {
        var border = 0;
        while (border < maxBorderWidth
            && MatchRatioHorizontal(pixels, width, x, y + border, slotWidth, color) >= MinEdgeMatchRatio)
        {
            border++;
        }

        return border;
    }

    private static int MeasureBottomBorder(
        int[] pixels,
        int width,
        int x,
        int y,
        int slotWidth,
        int slotHeight,
        int color,
        int maxBorderWidth)
    {
        var border = 0;
        while (border < maxBorderWidth
            && MatchRatioHorizontal(pixels, width, x, y + slotHeight - 1 - border, slotWidth, color) >= MinEdgeMatchRatio)
        {
            border++;
        }

        return border;
    }

    private static int MeasureLeftBorder(
        int[] pixels,
        int width,
        int x,
        int y,
        int slotHeight,
        int color,
        int maxBorderWidth)
    {
        var border = 0;
        while (border < maxBorderWidth
            && MatchRatioVertical(pixels, width, x + border, y, slotHeight, color) >= MinEdgeMatchRatio)
        {
            border++;
        }

        return border;
    }

    private static int MeasureRightBorder(
        int[] pixels,
        int width,
        int x,
        int y,
        int slotWidth,
        int slotHeight,
        int color,
        int maxBorderWidth)
    {
        var border = 0;
        while (border < maxBorderWidth
            && MatchRatioVertical(pixels, width, x + slotWidth - 1 - border, y, slotHeight, color) >= MinEdgeMatchRatio)
        {
            border++;
        }

        return border;
    }

    private static int MeasureWhiteLeft(int[] pixels, int width, Rectangle bounds, int maxWhiteWidth)
    {
        var white = 0;
        while (white < maxWhiteWidth
            && white < bounds.Width
            && WhiteRatioVertical(pixels, width, bounds.Left + white, bounds.Top, bounds.Height) >= MinWhiteMatchRatio)
        {
            white++;
        }

        return white;
    }

    private static int MeasureWhiteRight(int[] pixels, int width, Rectangle bounds, int maxWhiteWidth)
    {
        var white = 0;
        while (white < maxWhiteWidth
            && white < bounds.Width
            && WhiteRatioVertical(pixels, width, bounds.Right - 1 - white, bounds.Top, bounds.Height) >= MinWhiteMatchRatio)
        {
            white++;
        }

        return white;
    }

    private static int MeasureWhiteTop(int[] pixels, int width, Rectangle bounds, int maxWhiteWidth)
    {
        var white = 0;
        while (white < maxWhiteWidth
            && white < bounds.Height
            && WhiteRatioHorizontal(pixels, width, bounds.Left, bounds.Top + white, bounds.Width) >= MinWhiteMatchRatio)
        {
            white++;
        }

        return white;
    }

    private static int MeasureWhiteBottom(int[] pixels, int width, Rectangle bounds, int maxWhiteWidth)
    {
        var white = 0;
        while (white < maxWhiteWidth
            && white < bounds.Height
            && WhiteRatioHorizontal(pixels, width, bounds.Left, bounds.Bottom - 1 - white, bounds.Width) >= MinWhiteMatchRatio)
        {
            white++;
        }

        return white;
    }

    private static double MatchRatioHorizontal(int[] pixels, int width, int x, int y, int length, int color)
    {
        if (length <= 0)
        {
            return 0;
        }

        var matches = 0;
        for (var dx = 0; dx < length; dx++)
        {
            if (IsSameRgb(GetArgb(pixels, width, x + dx, y), color))
            {
                matches++;
            }
        }

        return matches / (double)length;
    }

    private static double MatchRatioVertical(int[] pixels, int width, int x, int y, int length, int color)
    {
        if (length <= 0)
        {
            return 0;
        }

        var matches = 0;
        for (var dy = 0; dy < length; dy++)
        {
            if (IsSameRgb(GetArgb(pixels, width, x, y + dy), color))
            {
                matches++;
            }
        }

        return matches / (double)length;
    }

    private static double WhiteRatioHorizontal(int[] pixels, int width, int x, int y, int length)
    {
        if (length <= 0)
        {
            return 0;
        }

        var matches = 0;
        for (var dx = 0; dx < length; dx++)
        {
            if (IsWhite(GetArgb(pixels, width, x + dx, y)))
            {
                matches++;
            }
        }

        return matches / (double)length;
    }

    private static double WhiteRatioVertical(int[] pixels, int width, int x, int y, int length)
    {
        if (length <= 0)
        {
            return 0;
        }

        var matches = 0;
        for (var dy = 0; dy < length; dy++)
        {
            if (IsWhite(GetArgb(pixels, width, x, y + dy)))
            {
                matches++;
            }
        }

        return matches / (double)length;
    }

    private static double TemplateSimilarity(Bitmap icon, Bitmap template)
    {
        var a = ToGrayscale(icon, MatchSize, MatchSize);
        var b = ToGrayscale(template, MatchSize, MatchSize);
        var meanA = a.Average(value => (double)value);
        var meanB = b.Average(value => (double)value);
        var numerator = 0.0;
        var sumA = 0.0;
        var sumB = 0.0;

        for (var i = 0; i < a.Length; i++)
        {
            var da = a[i] - meanA;
            var db = b[i] - meanB;
            numerator += da * db;
            sumA += da * da;
            sumB += db * db;
        }

        if (sumA <= double.Epsilon || sumB <= double.Epsilon)
        {
            return 0;
        }

        var correlation = numerator / Math.Sqrt(sumA * sumB);
        return Math.Max(0, Math.Min(1, (correlation + 1) / 2));
    }

    private static ulong ComputeDHash(Bitmap bitmap)
    {
        var gray = ToGrayscale(bitmap, 9, 8);
        var hash = 0UL;
        var bit = 0;
        for (var y = 0; y < 8; y++)
        {
            var rowOffset = y * 9;
            for (var x = 0; x < 8; x++)
            {
                if (gray[rowOffset + x] > gray[rowOffset + x + 1])
                {
                    hash |= 1UL << bit;
                }

                bit++;
            }
        }

        return hash;
    }

    private static byte[] ToGrayscale(Bitmap source, int width, int height)
    {
        using var resized = ResizeBitmap(source, width, height);
        var pixels = ReadPixels(resized);
        var gray = new byte[width * height];
        for (var i = 0; i < pixels.Length; i++)
        {
            var argb = pixels[i];
            var alpha = (argb >> 24) & 0xFF;
            if (alpha == 0)
            {
                gray[i] = 0;
                continue;
            }

            gray[i] = (byte)((GetR(argb) * 299 + GetG(argb) * 587 + GetB(argb) * 114) / 1000);
        }

        return gray;
    }

    private static Bitmap ResizeBitmap(Bitmap source, int width, int height)
    {
        var resized = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(resized);
        graphics.Clear(Color.Black);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        return resized;
    }

    private static int HammingDistance(ulong left, ulong right) => BitOperations.PopCount(left ^ right);

    private static Bitmap Capture(int x, int y, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        return bitmap;
    }

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

    private static Point GetLayoutAnchor(IReadOnlyList<DetectedAuraSlot> slots)
    {
        var minX = slots.Min(slot => slot.SlotBounds.Left);
        var minY = slots.Min(slot => slot.SlotBounds.Top);
        return new Point(minX, minY);
    }

    private static bool IsInside(Rectangle rectangle, int width, int height)
        => rectangle.Left >= 0
            && rectangle.Top >= 0
            && rectangle.Right <= width
            && rectangle.Bottom <= height;

    private static int GetArgb(int[] pixels, int width, int x, int y) => pixels[y * width + x];
    private static int GetR(int argb) => (argb >> 16) & 0xFF;
    private static int GetG(int argb) => (argb >> 8) & 0xFF;
    private static int GetB(int argb) => argb & 0xFF;

    private static bool IsSameRgb(int left, int right) => (left & 0x00FFFFFF) == (right & 0x00FFFFFF);

    private static bool IsWhite(int argb) => GetR(argb) >= 240 && GetG(argb) >= 240 && GetB(argb) >= 240;

    private static int ScaleValue(int value, double scale) => Math.Max(1, (int)Math.Round(value * scale));

    private static int FallbackIfZero(int value, int fallback) => value <= 0 ? fallback : value;

    private static string RowName(int rowR) => rowR == BuffRowR ? "增益" : "减益";

    public static string FormatHash(ulong hash) => hash.ToString("X16");

    private sealed record DetectedAuraSlot(
        int RowR,
        int Index,
        int RemainingB,
        Rectangle SlotBounds,
        Rectangle IconBounds,
        int BorderWidth,
        double Scale);

    private sealed class AuraTemplate : IDisposable
    {
        public AuraTemplate(string name, string path, ulong dHash, Bitmap bitmap)
        {
            Name = name;
            Path = path;
            DHash = dHash;
            Bitmap = bitmap;
        }

        public string Name { get; }
        public string Path { get; }
        public ulong DHash { get; }
        public Bitmap Bitmap { get; }

        public void Dispose()
        {
            Bitmap.Dispose();
        }
    }
}
