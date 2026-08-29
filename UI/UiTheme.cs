using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Shigure;

internal static class UiTheme
{
    public const int PageGap = 12;
    public const int CardPadding = 14;
    public const int ActionButtonHeight = 36;
    public const int GridRowHeight = 40;
    public const int TabBarHeight = 42;
    public const int CardCornerRadius = 10;
    public const int ControlCornerRadius = 8;

    private static readonly Dictionary<int, Image?> ClassIcons = new();
    private static readonly Dictionary<(int ClassId, int SpecId), Image?> SpecIcons = new();
    private static readonly ConditionalWeakTable<ListView, ListColumnLayoutState> ListColumnLayouts = new();

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaMicaEffect = 1029;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;
    private const int DwmsbtMainWindow = 2;
    private const int DwmsbtTransientWindow = 3;
    private const int WcaAccentPolicy = 19;
    private const int AccentEnableBlurBehind = 3;
    private const int AccentEnableAcrylicBlurBehind = 4;

    public static readonly Color Background = Color.FromArgb(12, 15, 19);
    public static readonly Color Surface = Color.FromArgb(23, 28, 35);
    public static readonly Color SurfaceRaised = Color.FromArgb(28, 35, 43);
    public static readonly Color Field = Color.FromArgb(34, 42, 51);
    public static readonly Color Hover = Color.FromArgb(42, 53, 63);
    public static readonly Color Pressed = Color.FromArgb(50, 63, 74);
    public static readonly Color Border = Color.FromArgb(48, 58, 69);
    public static readonly Color RowAlt = Color.FromArgb(26, 32, 39);
    public static readonly Color Text = Color.FromArgb(231, 237, 243);
    public static readonly Color Muted = Color.FromArgb(152, 164, 178);
    public static readonly Color Accent = Color.FromArgb(82, 224, 209);
    public static readonly Color AccentSoft = Color.FromArgb(24, 63, 64);
    public static readonly Color Success = Color.FromArgb(103, 211, 145);
    public static readonly Color Warning = Color.FromArgb(232, 196, 106);
    public static readonly Color Danger = Color.FromArgb(240, 122, 122);
    public static readonly Color DangerSoft = Color.FromArgb(64, 30, 35);

    internal enum ButtonKind
    {
        Secondary,
        Primary,
        Danger
    }

    internal readonly record struct ListColumn(
        string Text,
        int MinimumWidth,
        int MaximumWidth = 480,
        bool FillRemaining = false,
        bool FixedWidth = false);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint hObject);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(nint hwnd, ref WindowCompositionAttributeData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int LeftWidth;
        public int RightWidth;
        public int TopHeight;
        public int BottomHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public nint Data;
        public int SizeOfData;
    }

    public static void ApplyDarkTitleBar(Form form)
    {
        var dark = 1;
        _ = DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
    }

    public static bool ApplyRoundedCorners(Form form)
    {
        var preference = DwmwcpRound;
        if (DwmSetWindowAttribute(form.Handle, DwmwaWindowCornerPreference, ref preference, sizeof(int)) == 0)
        {
            return true;
        }

        ApplyFallbackRoundedCorners(form);
        return false;
    }

    public static void ApplyFallbackRoundedCorners(Form form)
    {
        if (form.Width <= 0 || form.Height <= 0)
        {
            return;
        }

        var regionHandle = CreateRoundRectRgn(0, 0, form.Width + 1, form.Height + 1, 16, 16);
        if (regionHandle == 0)
        {
            return;
        }

        try
        {
            var previousRegion = form.Region;
            form.Region = Region.FromHrgn(regionHandle);
            previousRegion?.Dispose();
        }
        finally
        {
            _ = DeleteObject(regionHandle);
        }
    }

    public static void ApplyTranslucentBackground(Form form)
    {
        form.BackColor = Color.FromArgb(18, 21, 26);

        var margins = new Margins
        {
            LeftWidth = -1,
            RightWidth = -1,
            TopHeight = -1,
            BottomHeight = -1
        };
        _ = DwmExtendFrameIntoClientArea(form.Handle, ref margins);

        // Windows 11: transient backdrop is Acrylic-like and does not affect child control opacity.
        var backdrop = DwmsbtTransientWindow;
        var hr = DwmSetWindowAttribute(form.Handle, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
        if (hr != 0)
        {
            backdrop = DwmsbtMainWindow;
            _ = DwmSetWindowAttribute(form.Handle, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
        }

        // Windows 10/older fallback: apply Acrylic blur behind the form background only.
        if (!TryApplyAccentPolicy(form.Handle, AccentEnableAcrylicBlurBehind, Color.FromArgb(18, 21, 26), 10))
        {
            _ = TryApplyAccentPolicy(form.Handle, AccentEnableBlurBehind, Color.FromArgb(18, 21, 26), 150);
        }

        // Fallback for older Windows 11 builds where system backdrop exists but acrylic fails.
        var enable = 1;
        _ = DwmSetWindowAttribute(form.Handle, DwmwaMicaEffect, ref enable, sizeof(int));
    }

    private static bool TryApplyAccentPolicy(nint hwnd, int accentState, Color tint, byte alpha)
    {
        var policy = new AccentPolicy
        {
            AccentState = accentState,
            AccentFlags = 2,
            GradientColor = ToAbgr(tint, alpha),
            AnimationId = 0
        };

        var policySize = Marshal.SizeOf<AccentPolicy>();
        var policyPointer = Marshal.AllocHGlobal(policySize);
        try
        {
            Marshal.StructureToPtr(policy, policyPointer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = policyPointer,
                SizeOfData = policySize
            };

            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(policyPointer);
        }
    }

    private static int ToAbgr(Color color, byte alpha)
    {
        return unchecked((int)(((uint)alpha << 24) | ((uint)color.B << 16) | ((uint)color.G << 8) | color.R));
    }

    public static Button CreateButton(string text, Color backColor, Color foreColor)
    {
        var button = new Button();
        StyleButton(button, text, backColor, foreColor);
        return button;
    }

    public static void StyleButton(Button button, string text, Color backColor, Color foreColor)
    {
        button.Text = text;
        button.AutoSize = true;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = backColor;
        button.ForeColor = foreColor;
        button.Padding = new Padding(10, 2, 10, 2);
        button.Margin = new Padding(6, 0, 0, 0);
        button.UseVisualStyleBackColor = false;
        button.Cursor = Cursors.Hand;
        button.TabStop = false;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = backColor == Accent ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = backColor == Accent ? Color.FromArgb(112, 234, 221) : Hover;
        button.FlatAppearance.MouseDownBackColor = backColor == Accent ? Color.FromArgb(62, 194, 181) : Pressed;
        ApplyControlRoundedRegion(button, ControlCornerRadius);

        if (backColor == Accent)
        {
            // WinForms ignores ForeColor for a disabled flat button and falls back to
            // the system disabled-text color, which is nearly black on our dark theme.
            button.Paint += (_, e) => PaintDisabledPrimaryButton(button, e);
        }

        button.EnabledChanged += (_, _) =>
        {
            button.Cursor = button.Enabled ? Cursors.Hand : Cursors.Default;
            button.ForeColor = button.Enabled ? foreColor : Muted;
            button.BackColor = button.Enabled ? backColor : SurfaceRaised;
            button.FlatAppearance.BorderColor = button.Enabled ? (backColor == Accent ? Accent : Border) : Border;
            button.Invalidate();
        };
    }

    private static void PaintDisabledPrimaryButton(Button button, PaintEventArgs e)
    {
        if (button.Enabled || button.ClientSize.Width <= 1 || button.ClientSize.Height <= 1)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, button.ClientSize.Width - 1, button.ClientSize.Height - 1);
        using var path = CreateRoundedRectanglePath(bounds, Scale(button, ControlCornerRadius));
        using var backgroundBrush = new SolidBrush(SurfaceRaised);
        using var borderPen = new Pen(Border);
        e.Graphics.FillPath(backgroundBrush, path);
        e.Graphics.DrawPath(borderPen, path);
        TextRenderer.DrawText(
            e.Graphics,
            button.Text,
            button.Font,
            button.ClientRectangle,
            Muted,
            TextFormatFlags.HorizontalCenter
            | TextFormatFlags.VerticalCenter
            | TextFormatFlags.SingleLine
            | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPadding);
    }

    public static Button CreateButton(string text, ButtonKind kind)
    {
        var button = kind switch
        {
            ButtonKind.Primary => CreateButton(text, Accent, Color.FromArgb(10, 31, 31)),
            ButtonKind.Danger => CreateButton(text, Field, Danger),
            _ => CreateButton(text, Field, Text)
        };
        button.TabStop = true;
        return button;
    }

    public static int Scale(Control control, int logicalPixels)
        => Math.Max(1, (int)Math.Round(logicalPixels * control.DeviceDpi / 96F));

    public static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void DrawExternalLinkIcon(
        Graphics graphics,
        Rectangle clientBounds,
        string text,
        Font font,
        Color color,
        float scale)
    {
        var iconSize = 17F * scale;
        var iconGap = 6F * scale;
        var textSize = TextRenderer.MeasureText(
            graphics,
            text,
            font,
            Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        var groupWidth = textSize.Width + iconGap + iconSize;
        var left = clientBounds.Left
            + ((clientBounds.Width - groupWidth) / 2F)
            + textSize.Width
            + iconGap;
        var top = clientBounds.Top + (clientBounds.Height - iconSize) / 2F;

        var previousSmoothingMode = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, 1.8F * scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        PointF Point(float x, float y) => new(left + (x * scale), top + (y * scale));

        graphics.DrawLines(
            pen,
            [
                Point(7, 2),
                Point(4, 2),
                Point(2, 4),
                Point(2, 13),
                Point(4, 15),
                Point(13, 15),
                Point(15, 13),
                Point(15, 9)
            ]);
        graphics.DrawLine(pen, Point(8, 9), Point(15, 2));
        graphics.DrawLines(pen, [Point(10, 2), Point(15, 2), Point(15, 7)]);
        graphics.SmoothingMode = previousSmoothingMode;
    }

    public static void ApplyControlRoundedRegion(Control control, int logicalRadius = 8)
    {
        void UpdateRegion()
        {
            if (control.Width <= 0 || control.Height <= 0)
            {
                return;
            }

            var diameter = Math.Min(
                Scale(control, logicalRadius) * 2,
                Math.Min(control.Width, control.Height));
            var regionHandle = CreateRoundRectRgn(0, 0, control.Width + 1, control.Height + 1, diameter, diameter);
            if (regionHandle == 0)
            {
                return;
            }

            try
            {
                var previous = control.Region;
                control.Region = Region.FromHrgn(regionHandle);
                previous?.Dispose();
            }
            finally
            {
                _ = DeleteObject(regionHandle);
            }
        }

        control.Resize -= OnRoundedRegionResize;
        control.Resize += OnRoundedRegionResize;
        control.HandleCreated -= OnRoundedRegionHandleCreated;
        control.HandleCreated += OnRoundedRegionHandleCreated;
        if (control.IsHandleCreated)
        {
            UpdateRegion();
        }

        void OnRoundedRegionResize(object? sender, EventArgs e) => UpdateRegion();
        void OnRoundedRegionHandleCreated(object? sender, EventArgs e) => UpdateRegion();
    }

    public static void StyleTextBox(TextBox textBox)
    {
        textBox.BackColor = Field;
        textBox.ForeColor = Text;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.Margin = new Padding(0);
        textBox.EnabledChanged += (_, _) =>
        {
            textBox.BackColor = textBox.Enabled ? Field : SurfaceRaised;
            textBox.ForeColor = textBox.Enabled ? Text : Muted;
        };
    }

    public static void StyleNumericUpDown(NumericUpDown numeric)
    {
        numeric.BackColor = Field;
        numeric.ForeColor = Text;
        numeric.BorderStyle = BorderStyle.FixedSingle;
        numeric.Margin = new Padding(0);
        numeric.EnabledChanged += (_, _) =>
        {
            numeric.BackColor = numeric.Enabled ? Field : SurfaceRaised;
            numeric.ForeColor = numeric.Enabled ? Text : Muted;
        };
    }

    public static void StyleCheckedListBox(CheckedListBox listBox)
    {
        listBox.BackColor = Field;
        listBox.ForeColor = Text;
        listBox.BorderStyle = BorderStyle.FixedSingle;
        listBox.CheckOnClick = true;
        listBox.IntegralHeight = false;
        listBox.ItemHeight = Math.Max(30, listBox.Font.Height + 14);
    }

    public static void StyleCheckBox(CheckBox checkBox, Color? backColor = null)
    {
        checkBox.AutoSize = true;
        checkBox.ForeColor = Text;
        checkBox.BackColor = backColor ?? Color.Transparent;
        checkBox.Cursor = Cursors.Hand;
        checkBox.FlatStyle = FlatStyle.Flat;
        checkBox.FlatAppearance.BorderColor = Border;
        checkBox.FlatAppearance.CheckedBackColor = AccentSoft;
        checkBox.FlatAppearance.MouseOverBackColor = Hover;
        checkBox.UseVisualStyleBackColor = false;
        checkBox.EnabledChanged += (_, _) =>
        {
            checkBox.Cursor = checkBox.Enabled ? Cursors.Hand : Cursors.Default;
            checkBox.ForeColor = checkBox.Enabled ? Text : Muted;
        };
    }

    public static void StyleActionButton(Button button, int width = 110)
    {
        button.AutoSize = false;
        button.Size = new Size(width, ActionButtonHeight);
        button.TextAlign = ContentAlignment.MiddleCenter;
    }

    public static Label CreateSectionTitle(Font baseFont, string text)
        => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = Text,
            BackColor = Color.Transparent,
            Font = new Font(baseFont.FontFamily, 10F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        };

    public static Label CreateDescription(string text)
        => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = Muted,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        };

    public static void StyleComboBox(UiDropDown comboBox)
    {
        comboBox.BackColor = Field;
        comboBox.ForeColor = Text;
        comboBox.Margin = new Padding(0);
        comboBox.EnabledChanged += (_, _) => comboBox.Invalidate();
    }

    public static void StyleListBox(
        ListBox listBox,
        Font font,
        Func<int, (int? ClassId, int? SpecId)>? moduleMatchSelector = null,
        bool showClassIconWithSpec = true,
        int? logicalIconSize = null,
        Func<int, Color?>? itemForeColorSelector = null)
    {
        var logicalItemHeight = logicalIconSize is { } requestedIconSize
            ? Math.Max(40, Math.Clamp(requestedIconSize, 24, 64) + 16)
            : 36;
        listBox.BackColor = Surface;
        listBox.ForeColor = Text;
        listBox.BorderStyle = BorderStyle.None;
        listBox.DrawMode = DrawMode.OwnerDrawFixed;
        listBox.ItemHeight = Math.Max(logicalItemHeight, font.Height + 14);
        listBox.IntegralHeight = false;
        listBox.HandleCreated += (_, _) => listBox.ItemHeight = Math.Max(
            Scale(listBox, logicalItemHeight),
            font.Height + Scale(listBox, 14));

        var hoveredIndex = -1;
        listBox.DrawItem += (_, e) =>
        {
            if (e.Index < 0)
            {
                return;
            }

            var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            var backgroundColor = selected
                ? Hover
                : e.Index == hoveredIndex
                    ? SurfaceRaised
                : e.Index % 2 == 0
                    ? listBox.BackColor
                    : RowAlt;
            using (var background = new SolidBrush(backgroundColor))
            {
                e.Graphics.FillRectangle(background, e.Bounds);
            }

            if (selected)
            {
                using var accent = new SolidBrush(Accent);
                e.Graphics.FillRectangle(accent, e.Bounds.Left, e.Bounds.Top + 5, 3, e.Bounds.Height - 10);
            }

            var textLeft = e.Bounds.Left + 10;
            if (moduleMatchSelector is not null)
            {
                var (classId, specId) = moduleMatchSelector(e.Index);
                Image?[] icons = showClassIconWithSpec
                    ?
                    [
                        classId is { } matchedClassId ? GetClassIcon(matchedClassId) : null,
                        classId is { } matchedSpecClassId && specId is { } matchedSpecId
                            ? GetSpecIcon(matchedSpecClassId, matchedSpecId)
                            : null
                    ]
                    :
                    [
                        classId is { } singleClassId
                            ? specId is { } singleSpecId
                                ? GetSpecIcon(singleClassId, singleSpecId)
                                : GetClassIcon(singleClassId)
                            : null
                    ];
                var iconSize = Math.Min(
                    logicalIconSize is { } requestedSize
                        ? Scale(listBox, Math.Clamp(requestedSize, 24, 64))
                        : font.Height,
                    e.Bounds.Height - Scale(listBox, 8));
                foreach (var icon in icons)
                {
                    if (icon is null)
                    {
                        continue;
                    }

                    var iconBounds = new Rectangle(
                        textLeft,
                        e.Bounds.Top + (e.Bounds.Height - iconSize) / 2,
                        iconSize,
                        iconSize);
                    e.Graphics.DrawImage(icon, iconBounds);
                    textLeft = iconBounds.Right + 4;
                }
            }

            var textBounds = new Rectangle(
                textLeft,
                e.Bounds.Top,
                Math.Max(0, e.Bounds.Right - textLeft - 4),
                e.Bounds.Height);
            TextRenderer.DrawText(
                e.Graphics,
                listBox.Items[e.Index]?.ToString() ?? string.Empty,
                font,
                textBounds,
                itemForeColorSelector?.Invoke(e.Index) ?? (selected ? Text : Muted),
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };

        listBox.MouseMove += (_, e) =>
        {
            var index = listBox.IndexFromPoint(e.Location);
            if (index == hoveredIndex)
            {
                return;
            }

            hoveredIndex = index;
            listBox.Invalidate();
        };
        listBox.MouseLeave += (_, _) =>
        {
            hoveredIndex = -1;
            listBox.Invalidate();
        };
    }

    public static void StyleClassIconListBox(
        ListBox listBox,
        Func<object?, int?> classIdSelector,
        int iconSize = 64)
    {
        var logicalIconSize = Math.Clamp(iconSize, 24, 64);
        var logicalItemHeight = Math.Max(40, logicalIconSize + 16);
        listBox.BackColor = Surface;
        listBox.ForeColor = Text;
        listBox.BorderStyle = BorderStyle.None;
        listBox.DrawMode = DrawMode.OwnerDrawFixed;
        listBox.ItemHeight = Math.Max(logicalItemHeight, listBox.Font.Height + 14);
        listBox.IntegralHeight = false;
        listBox.HandleCreated += (_, _) => listBox.ItemHeight = Math.Max(
            Scale(listBox, logicalItemHeight),
            listBox.Font.Height + Scale(listBox, 14));

        var hoveredIndex = -1;
        listBox.DrawItem += (_, e) =>
        {
            if (e.Index < 0)
            {
                return;
            }

            var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            var hovered = e.Index == hoveredIndex;
            var backgroundColor = selected
                ? Hover
                : e.Index % 2 == 0
                    ? listBox.BackColor
                    : RowAlt;
            using (var background = new SolidBrush(backgroundColor))
            {
                e.Graphics.FillRectangle(background, e.Bounds);
            }

            if (selected || hovered)
            {
                using var indicator = new SolidBrush(selected ? Color.White : Muted);
                var indicatorInset = Math.Max(6, (e.Bounds.Height - Scale(listBox, logicalIconSize)) / 2);
                e.Graphics.FillRectangle(
                    indicator,
                    e.Bounds.Left,
                    e.Bounds.Top + indicatorInset,
                    4,
                    e.Bounds.Height - (indicatorInset * 2));
            }

            var item = listBox.Items[e.Index];
            var classId = classIdSelector(item);
            var icon = classId is null ? null : GetClassIcon(classId.Value);
            var iconSize = Math.Min(
                Scale(listBox, logicalIconSize),
                e.Bounds.Height - Scale(listBox, 8));
            var visibleWidth = GetVisibleClientWidth(listBox);
            var iconBounds = new Rectangle(
                e.Bounds.Left + (visibleWidth - iconSize) / 2,
                e.Bounds.Top + (e.Bounds.Height - iconSize) / 2,
                iconSize,
                iconSize);

            if (icon is not null)
            {
                e.Graphics.DrawImage(icon, iconBounds);
                using var border = new Pen(selected ? Accent : Border);
                e.Graphics.DrawRectangle(border, iconBounds);
            }
            else
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    "?",
                    listBox.Font,
                    iconBounds,
                    selected ? Text : Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        };

        listBox.MouseMove += (_, e) =>
        {
            var index = listBox.IndexFromPoint(e.Location);
            if (index == hoveredIndex)
            {
                return;
            }

            hoveredIndex = index;
            listBox.Invalidate();
        };
        listBox.MouseLeave += (_, _) =>
        {
            hoveredIndex = -1;
            listBox.Invalidate();
        };
    }

    private static int GetVisibleClientWidth(Control control)
    {
        var visibleBounds = control.RectangleToScreen(control.ClientRectangle);
        for (var parent = control.Parent; parent is not null; parent = parent.Parent)
        {
            visibleBounds = Rectangle.Intersect(
                visibleBounds,
                parent.RectangleToScreen(parent.ClientRectangle));
        }

        return Math.Max(0, visibleBounds.Width);
    }

    private static Image? GetClassIcon(int classId)
    {
        if (ClassIcons.TryGetValue(classId, out var cached))
        {
            return cached;
        }

        var fileName = ClassNames.GetConfigFileName(classId).ToLowerInvariant();
        var resourceName = $"{typeof(UiTheme).Namespace}.Assets.Class.{fileName}.jpg";
        using var stream = typeof(UiTheme).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            ClassIcons[classId] = null;
            return null;
        }

        using var source = Image.FromStream(stream);
        var icon = new Bitmap(source);
        ClassIcons[classId] = icon;
        return icon;
    }

    public static void StyleSpecIconListBox(
        ListBox listBox,
        Func<object?, (int ClassId, int SpecId)?> specIdSelector,
        int iconSize = 56)
    {
        var logicalIconSize = Math.Clamp(iconSize, 24, 64);
        var logicalItemHeight = Math.Max(40, logicalIconSize + 16);
        listBox.BackColor = Surface;
        listBox.ForeColor = Text;
        listBox.BorderStyle = BorderStyle.None;
        listBox.DrawMode = DrawMode.OwnerDrawFixed;
        listBox.ItemHeight = Math.Max(logicalItemHeight, listBox.Font.Height + 14);
        listBox.IntegralHeight = false;
        listBox.HandleCreated += (_, _) => listBox.ItemHeight = Math.Max(
            Scale(listBox, logicalItemHeight),
            listBox.Font.Height + Scale(listBox, 14));

        var hoveredIndex = -1;
        listBox.DrawItem += (_, e) =>
        {
            if (e.Index < 0)
            {
                return;
            }

            var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            var hovered = e.Index == hoveredIndex;
            var backgroundColor = selected
                ? Hover
                : hovered
                    ? SurfaceRaised
                    : e.Index % 2 == 0
                        ? listBox.BackColor
                        : RowAlt;
            using (var background = new SolidBrush(backgroundColor))
            {
                e.Graphics.FillRectangle(background, e.Bounds);
            }

            if (selected || hovered)
            {
                using var indicator = new SolidBrush(selected ? Accent : Muted);
                var indicatorInset = Math.Max(6, (e.Bounds.Height - Scale(listBox, logicalIconSize)) / 2);
                e.Graphics.FillRectangle(
                    indicator,
                    e.Bounds.Left,
                    e.Bounds.Top + indicatorInset,
                    4,
                    e.Bounds.Height - (indicatorInset * 2));
            }

            var item = listBox.Items[e.Index];
            var specIds = specIdSelector(item);
            var icon = specIds is { } ids ? GetSpecIcon(ids.ClassId, ids.SpecId) : null;
            var drawnIconSize = Math.Min(
                Scale(listBox, logicalIconSize),
                e.Bounds.Height - Scale(listBox, 12));
            var iconBounds = new Rectangle(
                e.Bounds.Left + 12,
                e.Bounds.Top + (e.Bounds.Height - drawnIconSize) / 2,
                drawnIconSize,
                drawnIconSize);

            if (icon is not null)
            {
                e.Graphics.DrawImage(icon, iconBounds);
                using var border = new Pen(selected ? Accent : Border);
                e.Graphics.DrawRectangle(border, iconBounds);
            }
            else
            {
                using var placeholder = new SolidBrush(Field);
                e.Graphics.FillRectangle(placeholder, iconBounds);
                TextRenderer.DrawText(
                    e.Graphics,
                    "?",
                    listBox.Font,
                    iconBounds,
                    selected ? Text : Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            var textBounds = new Rectangle(
                iconBounds.Right + 12,
                e.Bounds.Top,
                Math.Max(0, e.Bounds.Right - iconBounds.Right - 18),
                e.Bounds.Height);
            TextRenderer.DrawText(
                e.Graphics,
                item?.ToString() ?? string.Empty,
                listBox.Font,
                textBounds,
                selected ? Text : Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
                TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        };

        listBox.MouseMove += (_, e) =>
        {
            var index = listBox.IndexFromPoint(e.Location);
            if (index == hoveredIndex)
            {
                return;
            }

            hoveredIndex = index;
            listBox.Invalidate();
        };
        listBox.MouseLeave += (_, _) =>
        {
            hoveredIndex = -1;
            listBox.Invalidate();
        };
    }

    private static Image? GetSpecIcon(int classId, int specId)
    {
        var key = (classId, specId);
        if (SpecIcons.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var fileName = ClassNames.GetSpecIconFileName(classId, specId);
        if (fileName is null)
        {
            SpecIcons[key] = null;
            return null;
        }

        var resourceName = $"{typeof(UiTheme).Namespace}.Assets.Spec.{fileName}.jpg";
        using var stream = typeof(UiTheme).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            SpecIcons[key] = null;
            return null;
        }

        using var source = Image.FromStream(stream);
        var icon = new Bitmap(source);
        SpecIcons[key] = icon;
        return icon;
    }

    public static void StyleListView(ListView listView, Font font)
    {
        listView.Dock = DockStyle.Fill;
        listView.View = View.Details;
        listView.FullRowSelect = true;
        listView.GridLines = false;
        listView.HideSelection = false;
        listView.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        listView.BackColor = Surface;
        listView.ForeColor = Text;
        listView.BorderStyle = BorderStyle.None;
        listView.OwnerDraw = true;
        listView.ShowItemToolTips = true;
        ApplyListViewMetrics(listView, font);
        typeof(Control)
            .GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(listView, true);

        listView.DrawColumnHeader += (_, e) =>
        {
            using var brush = new SolidBrush(Field);
            e.Graphics.FillRectangle(brush, e.Bounds);
            using var border = new Pen(Border);
            e.Graphics.DrawLine(border, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            TextRenderer.DrawText(
                e.Graphics,
                e.Header?.Text ?? string.Empty,
                font,
                new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height),
                Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
        listView.DrawItem += (_, _) =>
        {
            // Sub-items draw the full row in Details view.
        };
        listView.DrawSubItem += (_, e) =>
        {
            if (e.Item is null)
            {
                return;
            }

            var selected = e.Item.Selected;
            var rowBack = selected
                ? Hover
                : e.Item.Index % 2 == 0
                    ? Surface
                    : RowAlt;
            using (var brush = new SolidBrush(rowBack))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            if (selected && e.ColumnIndex == 0)
            {
                using var accent = new SolidBrush(Accent);
                e.Graphics.FillRectangle(accent, e.Bounds.Left, e.Bounds.Top + 4, 3, e.Bounds.Height - 8);
            }

            var textBounds = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height);
            TextRenderer.DrawText(
                e.Graphics,
                e.SubItem?.Text ?? string.Empty,
                font,
                textBounds,
                selected ? Text : e.ColumnIndex == 0 ? Muted : Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };

        listView.HandleCreated += (_, _) => ApplyListViewMetrics(listView, font);
    }

    public static void StyleDataGridView(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.Margin = new Padding(0);
        grid.BackgroundColor = Surface;
        grid.BorderStyle = BorderStyle.None;
        grid.GridColor = Border;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.ColumnHeadersHeight = Math.Max(38, grid.Font.Height + 16);
        grid.ColumnHeadersDefaultCellStyle.BackColor = Field;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Field;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Muted;
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
        grid.DefaultCellStyle.BackColor = Surface;
        grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = Hover;
        grid.DefaultCellStyle.SelectionForeColor = Text;
        grid.DefaultCellStyle.Padding = new Padding(8, 6, 8, 6);
        grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        grid.AlternatingRowsDefaultCellStyle.BackColor = RowAlt;
        grid.AlternatingRowsDefaultCellStyle.ForeColor = Text;
        grid.RowTemplate.Height = Math.Max(GridRowHeight, grid.Font.Height + 16);
        grid.RowHeadersVisible = false;
        grid.AllowUserToResizeRows = false;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ShowCellToolTips = true;

        void ApplyMetrics()
        {
            var verticalPadding = Scale(grid, 12);
            grid.ColumnHeadersHeight = Math.Max(Scale(grid, 38), grid.Font.Height + verticalPadding);
            grid.RowTemplate.Height = Math.Max(Scale(grid, GridRowHeight), grid.Font.Height + verticalPadding);
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (!row.IsNewRow)
                {
                    row.Height = Math.Max(row.Height, grid.RowTemplate.Height);
                }
            }

            EnsureGridColumnsReadable(grid);
        }

        grid.HandleCreated += (_, _) => ApplyMetrics();
        grid.ColumnAdded += (_, _) => EnsureGridColumnsReadable(grid);
        grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Value is null)
            {
                return;
            }

            grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ToolTipText = e.Value.ToString();
        };
    }

    // 避免 WinForms 按系统主题绘制高亮白色方块，统一成深色圆角按钮和青色箭头。
    public static void PaintDataGridViewComboBoxCell(
        DataGridView grid,
        DataGridViewCellPaintingEventArgs e,
        bool showButton = true)
    {
        e.Paint(e.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border);
        if (e.Graphics is null)
        {
            e.Handled = true;
            return;
        }

        var selected = (grid.Rows[e.RowIndex].Cells[e.ColumnIndex].State & DataGridViewElementStates.Selected) != 0;
        var cellStyle = e.CellStyle ?? grid.DefaultCellStyle;
        var textColor = selected ? cellStyle.SelectionForeColor : cellStyle.ForeColor;
        var buttonBounds = GetDropDownButtonBounds(grid, e.CellBounds);
        var textBounds = new Rectangle(
            e.CellBounds.Left + Scale(grid, 10),
            e.CellBounds.Top,
            Math.Max(
                0,
                (showButton ? buttonBounds.Left : e.CellBounds.Right)
                - e.CellBounds.Left
                - Scale(grid, 16)),
            e.CellBounds.Height);

        TextRenderer.DrawText(
            e.Graphics,
            e.FormattedValue?.ToString() ?? string.Empty,
            cellStyle.Font ?? grid.Font,
            textBounds,
            textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

        if (showButton)
        {
            PaintDropDownButton(e.Graphics, grid, buttonBounds, selected, enabled: true, hovered: false);
        }

        e.Handled = true;
    }

    public static Rectangle GetDropDownButtonBounds(Control control, Rectangle bounds)
    {
        var buttonSize = Math.Min(
            Scale(control, 24),
            Math.Max(Scale(control, 18), bounds.Height - Scale(control, 12)));
        return new Rectangle(
            bounds.Right - buttonSize - Scale(control, 7),
            bounds.Top + (bounds.Height - buttonSize) / 2,
            buttonSize,
            buttonSize);
    }

    public static void PaintDropDownButton(
        Graphics graphics,
        Control control,
        Rectangle buttonBounds,
        bool selected,
        bool enabled,
        bool hovered)
    {
        var oldSmoothingMode = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var backgroundColor = !enabled
            ? SurfaceRaised
            : selected
                ? Pressed
                : Hover;
        using (var path = CreateRoundedRectanglePath(buttonBounds, Scale(control, 4)))
        using (var background = new SolidBrush(backgroundColor))
        using (var border = new Pen(enabled && (selected || hovered) ? Accent : Border))
        {
            graphics.FillPath(background, path);
            graphics.DrawPath(border, path);
        }

        var centerX = buttonBounds.Left + buttonBounds.Width / 2;
        var centerY = buttonBounds.Top + buttonBounds.Height / 2 + 1;
        var arrowHalfWidth = Scale(control, 4);
        var arrowTop = Scale(control, 2);
        var arrowBottom = Scale(control, 3);
        var arrow = new[]
        {
            new Point(centerX - arrowHalfWidth, centerY - arrowTop),
            new Point(centerX + arrowHalfWidth, centerY - arrowTop),
            new Point(centerX, centerY + arrowBottom)
        };
        using (var arrowBrush = new SolidBrush(enabled && (selected || hovered) ? Accent : Muted))
        {
            graphics.FillPolygon(arrowBrush, arrow);
        }

        graphics.SmoothingMode = oldSmoothingMode;
    }

    public static ListView CreateListView(Font font, params (string Text, int Width)[] columns)
    {
        var layouts = columns
            .Select((column, index) => new ListColumn(
                column.Text,
                column.Width,
                Math.Max(column.Width, index == columns.Length - 1 ? 1200 : 420),
                FillRemaining: index == columns.Length - 1,
                FixedWidth: index == 0 && column.Text == "#"))
            .ToArray();
        return CreateListView(font, layouts);
    }

    public static ListView CreateListView(Font font, params ListColumn[] columns)
    {
        var listView = new ListView();
        ConfigureListViewColumns(listView, font, null, columns);
        return listView;
    }

    public static ListView CreateListView(Font font, string cacheKey, params ListColumn[] columns)
    {
        var listView = new ListView();
        ConfigureListViewColumns(listView, font, cacheKey, columns);
        return listView;
    }

    public static void ConfigureListViewColumns(
        ListView listView,
        Font font,
        string? cacheKey,
        params ListColumn[] columns)
    {
        StyleListView(listView, font);

        foreach (var column in columns)
        {
            listView.Columns.Add(column.Text, column.MinimumWidth);
        }

        ListColumnLayouts.Add(listView, new ListColumnLayoutState(columns));
        listView.Resize += (_, _) => FitListViewColumns(listView);
        listView.HandleCreated += (_, _) =>
        {
            // ListView 在 OnHandleCreated 内部会重建原生 item；此时 Items.Count 已更新，
            // 但索引器可能短暂返回 null。延迟到本轮消息结束后再按内容测量列宽。
            listView.BeginInvoke(() =>
            {
                if (!listView.IsDisposed && listView.IsHandleCreated)
                {
                    FitListViewColumns(listView);
                }
            });
        };

        if (!string.IsNullOrWhiteSpace(cacheKey))
        {
            CacheListViewColumnWidths(listView, cacheKey);
        }
    }

    public static void CacheDataGridViewColumnWidths(DataGridView grid, string cacheKey)
    {
        var isApplying = false;
        var isInitializing = true;

        void ApplyCachedWidths()
        {
            var widths = UiCacheStore.LoadColumnWidths(cacheKey);
            if (widths is null || widths.Count == 0)
            {
                return;
            }

            isApplying = true;
            try
            {
                foreach (DataGridViewColumn column in grid.Columns)
                {
                    if (column.AutoSizeMode == DataGridViewAutoSizeColumnMode.Fill
                        || !widths.TryGetValue(column.Name, out var width)
                        || width <= 0)
                    {
                        continue;
                    }

                    column.Width = Math.Max(column.MinimumWidth, width);
                }
            }
            finally
            {
                isApplying = false;
            }
        }

        grid.ColumnWidthChanged += (_, e) =>
        {
            if (isInitializing
                || isApplying
                || e.Column.AutoSizeMode == DataGridViewAutoSizeColumnMode.Fill
                || string.IsNullOrWhiteSpace(e.Column.Name))
            {
                return;
            }

            UiCacheStore.SaveColumnWidth(cacheKey, e.Column.Name, e.Column.Width);
        };
        grid.HandleCreated += (_, _) =>
        {
            ApplyCachedWidths();
            isInitializing = false;
        };
        ApplyCachedWidths();
        if (grid.IsHandleCreated)
        {
            isInitializing = false;
        }
    }

    public static void CacheListViewColumnWidths(ListView listView, string cacheKey)
    {
        if (!ListColumnLayouts.TryGetValue(listView, out var state)
            || state.Columns.Count != listView.Columns.Count
            || listView.Columns.Count == 0)
        {
            return;
        }

        state.CachedWidths = UiCacheStore.LoadColumnWidths(cacheKey)
            ?? new Dictionary<string, int>(StringComparer.Ordinal);

        void ApplyCachedWidths()
        {
            state.IsApplyingCachedWidths = true;
            try
            {
                for (var columnIndex = 0; columnIndex < state.Columns.Count; columnIndex++)
                {
                    var layout = state.Columns[columnIndex];
                    if (layout.FillRemaining
                        || !state.CachedWidths.TryGetValue(layout.Text, out var width)
                        || width <= 0)
                    {
                        continue;
                    }

                    var minimum = Scale(listView, layout.MinimumWidth);
                    var maximum = Scale(listView, Math.Max(layout.MinimumWidth, layout.MaximumWidth));
                    listView.Columns[columnIndex].Width = Math.Clamp(width, minimum, maximum);
                }
            }
            finally
            {
                state.IsApplyingCachedWidths = false;
            }
        }

        listView.ColumnWidthChanged += (_, e) =>
        {
            if (state.IsApplyingCachedWidths
                || state.IsFitting
                || e.ColumnIndex < 0
                || e.ColumnIndex >= state.Columns.Count)
            {
                return;
            }

            var layout = state.Columns[e.ColumnIndex];
            if (layout.FillRemaining)
            {
                return;
            }

            state.CachedWidths[layout.Text] = listView.Columns[e.ColumnIndex].Width;
            UiCacheStore.SaveColumnWidth(cacheKey, layout.Text, listView.Columns[e.ColumnIndex].Width);
        };

        ApplyCachedWidths();
    }

    public static void FitListViewColumns(ListView listView)
    {
        if (listView.IsDisposed
            || !ListColumnLayouts.TryGetValue(listView, out var state)
            || state.Columns.Count != listView.Columns.Count
            || listView.Columns.Count == 0)
        {
            return;
        }

        state.IsFitting = true;
        try
        {
            var availableWidth = Math.Max(0, listView.ClientSize.Width - Scale(listView, 2));
            var widths = new int[state.Columns.Count];
            var fillIndexes = new List<int>();
            var usedWidth = 0;

            for (var columnIndex = 0; columnIndex < state.Columns.Count; columnIndex++)
            {
                var layout = state.Columns[columnIndex];
                var minimum = Scale(listView, layout.MinimumWidth);
                var maximum = Scale(listView, Math.Max(layout.MinimumWidth, layout.MaximumWidth));
                var cachedWidth = 0;
                var hasCachedWidth = !layout.FillRemaining
                    && state.CachedWidths is not null
                    && state.CachedWidths.TryGetValue(layout.Text, out cachedWidth);
                var measured = MeasureListColumn(listView, columnIndex, layout.Text);
                var width = hasCachedWidth
                    ? Math.Clamp(cachedWidth, minimum, maximum)
                    : layout.FixedWidth ? minimum : Math.Clamp(measured, minimum, maximum);
                widths[columnIndex] = width;
                if (layout.FillRemaining)
                {
                    fillIndexes.Add(columnIndex);
                }
                else
                {
                    usedWidth += width;
                }
            }

            if (fillIndexes.Count > 0)
            {
                var remaining = Math.Max(0, availableWidth - usedWidth);
                var share = remaining / fillIndexes.Count;
                foreach (var index in fillIndexes)
                {
                    var layout = state.Columns[index];
                    widths[index] = Math.Clamp(
                        share,
                        Scale(listView, layout.MinimumWidth),
                        Scale(listView, Math.Max(layout.MinimumWidth, layout.MaximumWidth)));
                }
            }

            for (var i = 0; i < widths.Length; i++)
            {
                listView.Columns[i].Width = widths[i];
            }
        }
        finally
        {
            state.IsFitting = false;
        }
    }

    private static void ApplyListViewMetrics(ListView listView, Font font)
    {
        var rowHeight = Math.Max(Scale(listView, 36), font.Height + Scale(listView, 14));
        if (listView.SmallImageList is null)
        {
            listView.SmallImageList = new ImageList();
        }

        listView.SmallImageList.ImageSize = new Size(1, rowHeight);
    }

    private static int MeasureListColumn(ListView listView, int columnIndex, string header)
    {
        var width = TextRenderer.MeasureText(header, listView.Font).Width + Scale(listView, 24);
        var sampleCount = Math.Min(listView.Items.Count, 100);
        for (var rowIndex = 0; rowIndex < sampleCount; rowIndex++)
        {
            ListViewItem? item;
            try
            {
                item = listView.Items[rowIndex];
            }
            catch (ArgumentOutOfRangeException)
            {
                // 数据刷新和句柄重建都可能让采样数量在本轮测量中发生变化。
                break;
            }

            if (item is null || columnIndex >= item.SubItems.Count)
            {
                continue;
            }

            var subItem = item.SubItems[columnIndex];
            if (subItem is null)
            {
                continue;
            }

            width = Math.Max(
                width,
                TextRenderer.MeasureText(subItem.Text ?? string.Empty, listView.Font).Width + Scale(listView, 24));
        }

        return width;
    }

    private static void EnsureGridColumnsReadable(DataGridView grid)
    {
        foreach (DataGridViewColumn column in grid.Columns)
        {
            var headerWidth = TextRenderer.MeasureText(column.HeaderText ?? string.Empty, grid.Font).Width
                + Scale(grid, 24);
            var typeMinimum = column is DataGridViewButtonColumn
                ? Scale(grid, 36)
                : column is DataGridViewCheckBoxColumn
                    ? Scale(grid, 56)
                    : Scale(grid, 72);
            column.MinimumWidth = Math.Max(column.MinimumWidth, Math.Max(headerWidth, typeMinimum));
            column.Width = Math.Max(column.Width, column.MinimumWidth);
        }
    }

    private sealed class ListColumnLayoutState(IReadOnlyList<ListColumn> columns)
    {
        public IReadOnlyList<ListColumn> Columns { get; } = columns;
        public Dictionary<string, int>? CachedWidths { get; set; }
        public bool IsApplyingCachedWidths { get; set; }
        public bool IsFitting { get; set; }
    }

    public static string FormatValue(object? value)
    {
        return value switch
        {
            null => "-",
            bool b => b ? "是" : "否",
            _ => value.ToString() ?? "-"
        };
    }
}
