namespace Shigure;

// 保留系统菜单的悬停展开与键盘导航，长列表按鼠标所在层滚动，所有颜色来自统一主题。
internal static partial class UiCascadingMenu
{
    private static readonly object HeaderMarker = new();

    public static ContextMenuStrip Create(Control owner)
    {
        var menu = new CascadingContextMenu();
        Style(menu, owner);
        return menu;
    }

    public static void Style(ToolStripDropDown menu, Control owner)
    {
        menu.Font = owner.Font;
        menu.BackColor = UiTheme.Surface;
        menu.ForeColor = UiTheme.Text;
        menu.Padding = new Padding(1);
        if (menu is ToolStripDropDownMenu dropDownMenu)
        {
            dropDownMenu.ShowCheckMargin = false;
            dropDownMenu.ShowImageMargin = menu.Items.OfType<ToolStripMenuItem>()
                .Any(item => item.Image is not null || item.Checked);
        }
        menu.Renderer = new MenuRenderer();
        menu.ImageScalingSize = new Size(UiTheme.Scale(owner, 24), UiTheme.Scale(owner, 24));
        menu.MaximumSize = new Size(0, Math.Max(UiTheme.Scale(owner, 80), Screen.FromControl(owner).WorkingArea.Height - 32));
        ConfigureScrolling(menu, owner);
    }

    public static ToolStripMenuItem Item(Control owner, string text, Action? click = null, Image? icon = null)
    {
        var item = new CascadingMenuItem(text, icon)
        {
            ForeColor = UiTheme.Text, BackColor = UiTheme.Surface,
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(UiTheme.Scale(owner, 16), UiTheme.Scale(owner, 7), UiTheme.Scale(owner, 16), UiTheme.Scale(owner, 7)),
            ToolTipText = text
        };
        if (click is not null) item.Click += (_, _) => click();
        item.DropDownOpening += (_, _) => Style(item.DropDown, owner);
        item.DropDownOpened += (_, _) =>
        {
            AttachSubMenu(item, owner);
            // WinForms 展开时还会进行一次系统定位，消息结束后按最终尺寸再贴合。
            if (owner.IsHandleCreated && !owner.IsDisposed)
                owner.BeginInvoke(() =>
                {
                    if (!item.IsDisposed && item.DropDown.Visible) AttachSubMenu(item, owner);
                });
        };
        return item;
    }

    // 只为有子项的菜单添加占位箭头；真正展开时才创建下一层，避免点击根菜单就构建全部字段和图标。
    public static ToolStripMenuItem LazyItem(Control owner, string text, Action<ToolStripItemCollection> populate)
    {
        var item = Item(owner, text);
        var placeholder = new ToolStripMenuItem();
        item.DropDownItems.Add(placeholder);
        var populated = false;
        item.DropDownOpening += (_, _) =>
        {
            if (populated) return;
            var menu = item.DropDown;
            menu.SuspendLayout();
            try
            {
                menu.Items.Clear();
                placeholder.Dispose();
                populate(menu.Items);
                Style(menu, owner);
                populated = true;
            }
            finally { menu.ResumeLayout(performLayout: true); }
        };
        return item;
    }

    public static ToolStripMenuItem Header(Control owner, string text)
    {
        var font = new Font(owner.Font, owner.Font.Style | FontStyle.Bold);
        var item = new ToolStripMenuItem(text)
        {
            Enabled = false, Tag = HeaderMarker, TextAlign = ContentAlignment.MiddleCenter,
            Font = font,
            BackColor = UiTheme.Background, ForeColor = UiTheme.Muted,
            Padding = new Padding(UiTheme.Scale(owner, 16), UiTheme.Scale(owner, 4),
                UiTheme.Scale(owner, 16), UiTheme.Scale(owner, 4))
        };
        item.Disposed += (_, _) => font.Dispose();
        return item;
    }

    private static void AttachSubMenu(ToolStripMenuItem item, Control owner)
    {
        if (item.Owner is not ToolStripDropDown parent) return;
        var child = item.DropDown;
        if (!NativeMethods.GetWindowRect(parent.Handle, out var parentRect)
            || !NativeMethods.GetWindowRect(child.Handle, out var childRect)) return;
        var parentBounds = Rectangle.FromLTRB(parentRect.Left, parentRect.Top, parentRect.Right, parentRect.Bottom);
        var childWidth = childRect.Right - childRect.Left;
        var workingArea = Screen.FromRectangle(parentBounds).WorkingArea;
        var overlap = UiTheme.Scale(owner, 1);
        var right = parentBounds.Right - overlap;
        var left = parentBounds.Left - childWidth + overlap;
        // 保留系统在屏幕边缘向左展开的选择，让两侧边框直接贴合。
        var openLeft = right + childWidth > workingArea.Right
            || childRect.Left < parentBounds.Left && left >= workingArea.Left;
        var x = openLeft ? left : right;
        const uint noZOrder = 0x0004;
        NativeMethods.SetWindowPos(child.Handle, 0,
            Math.Clamp(x, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - childWidth)),
            childRect.Top, 0, 0, NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | noZOrder);
    }

    private sealed class MenuRenderer() : ToolStripProfessionalRenderer(new MenuColors())
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            => e.Graphics.Clear(UiTheme.Surface);

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (ReferenceEquals(e.Item.Tag, HeaderMarker))
            {
                using var headerFill = new SolidBrush(UiTheme.Background);
                e.Graphics.FillRectangle(headerFill, FullRowBounds(e.Item));
                return;
            }
            if (!e.Item.Selected && !e.Item.Pressed) return;
            using var fill = new SolidBrush(UiTheme.AccentSoft);
            e.Graphics.FillRectangle(fill, FullRowBounds(e.Item));
        }

        private static Rectangle FullRowBounds(ToolStripItem item)
            => new(1 - item.Bounds.Left, 0, Math.Max(0, (item.Owner?.ClientSize.Width ?? item.Width) - 2), item.Height);

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var color = e.Item.Enabled ? e.Item.Selected ? UiTheme.Accent : UiTheme.Text : UiTheme.Muted;
            // 不采用系统为图标和箭头预留的不对称文字区域，按整行中心绘制。
            var menuWidth = e.Item.Owner?.ClientSize.Width ?? e.Item.Width;
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont ?? e.Item.Font,
                new Rectangle(4 - e.Item.Bounds.Left, 0, Math.Max(0, menuWidth - 8), e.Item.Height), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item?.Enabled == false ? UiTheme.Muted : UiTheme.Text;
            base.OnRenderArrow(e);
        }
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => UiTheme.Surface;
        public override Color MenuItemSelected => UiTheme.AccentSoft;
        public override Color MenuItemBorder => UiTheme.Border;
        public override Color ToolStripBorder => UiTheme.Border;
        public override Color ImageMarginGradientBegin => UiTheme.Surface;
        public override Color ImageMarginGradientMiddle => UiTheme.Surface;
        public override Color ImageMarginGradientEnd => UiTheme.Surface;
        public override Color SeparatorDark => UiTheme.Border;
        public override Color SeparatorLight => UiTheme.Border;
    }
}
