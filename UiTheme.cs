using System.Drawing;
using System.Runtime.InteropServices;

namespace Shigure;

internal static class UiTheme
{
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

    public static readonly Color Background = Color.FromArgb(13, 15, 18);
    public static readonly Color Surface = Color.FromArgb(22, 25, 31);
    public static readonly Color Field = Color.FromArgb(31, 35, 42);
    public static readonly Color Hover = Color.FromArgb(40, 45, 53);
    public static readonly Color Text = Color.FromArgb(225, 229, 235);
    public static readonly Color Muted = Color.FromArgb(128, 136, 148);
    public static readonly Color Accent = Color.FromArgb(86, 205, 192);
    public static readonly Color Danger = Color.FromArgb(235, 108, 108);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

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

    public static void ApplyRoundedCorners(Form form)
    {
        var preference = DwmwcpRound;
        if (DwmSetWindowAttribute(form.Handle, DwmwaWindowCornerPreference, ref preference, sizeof(int)) != 0)
        {
            // Windows 10 回退: 用 Region 裁剪圆角。
            form.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, form.Width + 1, form.Height + 1, 16, 16));
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
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = backColor,
            ForeColor = foreColor,
            Padding = new Padding(10, 2, 10, 2),
            Margin = new Padding(6, 0, 0, 0),
            UseVisualStyleBackColor = false,
            TabStop = false
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    public static void StyleComboBox(ComboBox comboBox)
    {
        comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBox.FlatStyle = FlatStyle.Flat;
        comboBox.BackColor = Field;
        comboBox.ForeColor = Text;
        comboBox.DrawMode = DrawMode.OwnerDrawFixed;
        comboBox.ItemHeight = 26;

        comboBox.DrawItem += (_, e) =>
        {
            if (e.Index < 0)
            {
                return;
            }

            var isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            var isEditBox = (e.State & DrawItemState.ComboBoxEdit) == DrawItemState.ComboBoxEdit;

            using (var background = new SolidBrush(isEditBox ? Field : isSelected ? Hover : Surface))
            {
                e.Graphics.FillRectangle(background, e.Bounds);
            }

            var textColor = !isEditBox && isSelected ? Accent : Text;
            var textBounds = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
            TextRenderer.DrawText(
                e.Graphics,
                comboBox.Items[e.Index]?.ToString(),
                comboBox.Font,
                textBounds,
                textColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
    }

    public static ListView CreateListView(Font font, params (string Text, int Width)[] columns)
    {
        var listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BackColor = Surface,
            ForeColor = Text,
            BorderStyle = BorderStyle.None,
            OwnerDraw = true
        };

        foreach (var (text, width) in columns)
        {
            listView.Columns.Add(text, width);
        }

        listView.DrawColumnHeader += (_, e) =>
        {
            using var brush = new SolidBrush(Field);
            e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(
                e.Graphics,
                e.Header?.Text,
                font,
                new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height),
                Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
        listView.DrawItem += (_, e) => e.DrawDefault = true;
        listView.DrawSubItem += (_, e) => e.DrawDefault = true;

        // 最后一列拉伸填满, 避免表头右侧露出系统默认的白色区域。
        void StretchLastColumn()
        {
            if (listView.Columns.Count == 0)
            {
                return;
            }

            var othersWidth = 0;
            for (var i = 0; i < listView.Columns.Count - 1; i++)
            {
                othersWidth += listView.Columns[i].Width;
            }

            var lastWidth = listView.ClientSize.Width - othersWidth;
            if (lastWidth > 60)
            {
                listView.Columns[^1].Width = lastWidth;
            }
        }

        listView.Resize += (_, _) => StretchLastColumn();
        listView.HandleCreated += (_, _) => StretchLastColumn();

        return listView;
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
