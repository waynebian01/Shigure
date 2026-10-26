using System.Runtime.CompilerServices;

namespace Shigure;

internal static partial class UiCascadingMenu
{
    private const int MaximumVisibleItems = 15;
    private const int MouseWheelMessage = 0x020A;
    private static readonly ConditionalWeakTable<ToolStripDropDown, MenuViewport> Viewports = new();
    [ThreadStatic] private static bool _navigationClick;

    private static void ConfigureScrolling(ToolStripDropDown menu, Control owner)
        => Viewports.GetValue(menu, key => new MenuViewport(key, owner));

    private sealed class CascadingContextMenu : ContextMenuStrip
    {
        protected override bool ProcessDialogKey(Keys keyData)
            => Viewports.TryGetValue(this, out var viewport) && viewport.Navigate(keyData)
                || base.ProcessDialogKey(keyData);

        protected override void OnMouseWheel(MouseEventArgs e) => RouteWheel(this, e.Delta);
    }

    private sealed class CascadingDropDownMenu : ToolStripDropDownMenu
    {
        protected override bool ProcessDialogKey(Keys keyData)
            => Viewports.TryGetValue(this, out var viewport) && viewport.Navigate(keyData)
                || base.ProcessDialogKey(keyData);

        protected override void OnMouseWheel(MouseEventArgs e) => RouteWheel(this, e.Delta);
    }

    private sealed class CascadingMenuItem(string text, Image? icon) : ToolStripMenuItem(text, icon)
    {
        protected override ToolStripDropDown CreateDefaultDropDown() => new CascadingDropDownMenu();
    }

    private sealed class ScrollNavigationItem : ToolStripMenuItem;

    private sealed class MenuViewport
    {
        private readonly ToolStripDropDown _menu;
        private readonly Control _owner;
        private readonly ScrollNavigationItem _up;
        private readonly ScrollNavigationItem _down;
        private ToolStripItem[] _items = [];
        private int _start;
        private int _visibleCount = MaximumVisibleItems;
        private int _wheelRemainder;
        private bool _updating;
        private MenuWheelFilter? _filter;

        public MenuViewport(ToolStripDropDown menu, Control owner)
        {
            _menu = menu;
            _owner = owner;
            _up = NavigationItem("▲", -1);
            _down = NavigationItem("▼", 1);
            menu.Opening += (_, _) => Prepare();
            menu.Opened += (_, _) =>
            {
                Prepare();
                if (menu is CascadingContextMenu && _filter is null)
                {
                    _filter = new MenuWheelFilter(menu);
                    Application.AddMessageFilter(_filter);
                }
            };
            menu.ItemClicked += (_, e) =>
            {
                if (e.ClickedItem is not ScrollNavigationItem) return;
                _navigationClick = true;
                if (owner.IsHandleCreated && !owner.IsDisposed)
                    owner.BeginInvoke(() => _navigationClick = false);
            };
            menu.Closing += (_, e) =>
            {
                if (_navigationClick && e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true;
            };
            menu.Closed += (_, _) => RemoveFilter();
            menu.Disposed += (_, _) =>
            {
                RemoveFilter();
                _up.Dispose();
                _down.Dispose();
            };
        }

        private ScrollNavigationItem NavigationItem(string text, int direction)
        {
            var item = new ScrollNavigationItem
            {
                Text = text,
                Padding = Padding.Empty, Margin = Padding.Empty,
                ForeColor = UiTheme.Muted, BackColor = UiTheme.Surface,
                TextAlign = ContentAlignment.MiddleCenter,
                AccessibleName = direction < 0 ? "向上滚动菜单" : "向下滚动菜单"
            };
            item.Click += (_, _) => Scroll(direction);
            return item;
        }

        private void RemoveFilter()
        {
            if (_filter is null) return;
            Application.RemoveMessageFilter(_filter);
            _filter = null;
        }

        private void Prepare()
        {
            if (_updating || _menu.IsDisposed) return;
            _items = _menu.Items.Cast<ToolStripItem>().Where(item => item is not ScrollNavigationItem).ToArray();
            if (_items.Length == 0) return;
            var rowHeight = _items.Max(item => Math.Max(item.Height, item.GetPreferredSize(Size.Empty).Height));
            var workingArea = Screen.FromControl(_owner).WorkingArea;
            // 小屏幕可少于 15 项，为边框与上下滚动控件预留空间。
            _visibleCount = Math.Clamp((workingArea.Height - UiTheme.Scale(_owner, 96)) / Math.Max(1, rowHeight),
                1, MaximumVisibleItems);
            if (_items.Length > _visibleCount)
            {
                if (!_menu.Items.Contains(_up)) _menu.Items.Insert(0, _up);
                if (!_menu.Items.Contains(_down)) _menu.Items.Add(_down);
                // 菜单自身按完整 Items 集合测量宽度（包含暂时隐藏的项）。
                // 不额外设置最小宽度，避免菜单比其内部行宽，留下无法高亮的空白。
            }
            _start = Math.Clamp(_start, 0, Math.Max(0, _items.Length - _visibleCount));
            ApplyWindow();
        }

        private void ApplyWindow()
        {
            _updating = true;
            _menu.SuspendLayout();
            try
            {
                var scrollable = _items.Length > _visibleCount;
                _up.Available = _down.Available = scrollable;
                _up.Enabled = _start > 0;
                _down.Enabled = _start + _visibleCount < _items.Length;
                for (var i = 0; i < _items.Length; i++)
                    _items[i].Available = i >= _start && i < _start + _visibleCount;
            }
            finally
            {
                _menu.ResumeLayout(performLayout: true);
                _updating = false;
            }
            _menu.Invalidate();
        }

        private void Scroll(int rows)
        {
            var next = Math.Clamp(_start + rows, 0, Math.Max(0, _items.Length - _visibleCount));
            if (next == _start) return;
            // 父菜单行的位置将变化，关闭旧的子菜单，下一次悬停再按新行展开。
            foreach (var item in _items.OfType<ToolStripMenuItem>())
                if (item.HasDropDownItems && item.DropDown.Visible) item.HideDropDown();
            _start = next;
            ApplyWindow();
        }

        public void Wheel(int delta)
        {
            _wheelRemainder += delta;
            var notches = _wheelRemainder / SystemInformation.MouseWheelScrollDelta;
            _wheelRemainder %= SystemInformation.MouseWheelScrollDelta;
            if (notches == 0) return;
            var lines = SystemInformation.MouseWheelScrollLines;
            Scroll(-notches * (lines < 0 ? _visibleCount : Math.Max(1, lines)));
        }

        public bool Navigate(Keys keyData)
        {
            if (_items.Length <= _visibleCount || (keyData & (Keys.Control | Keys.Alt)) != 0) return false;
            var key = keyData & Keys.KeyCode;
            var visible = _items.Where(item => item.Available && item.CanSelect && item.Enabled).ToArray();
            if (visible.Length == 0) return false;
            var selected = _menu.Items.Cast<ToolStripItem>().FirstOrDefault(item => item.Selected);
            switch (key)
            {
                case Keys.Down when ReferenceEquals(selected, visible[^1]) || ReferenceEquals(selected, _down):
                    Scroll(1);
                    _items.LastOrDefault(item => item.Available && item.CanSelect && item.Enabled)?.Select();
                    return true;
                case Keys.Up when ReferenceEquals(selected, visible[0]) || ReferenceEquals(selected, _up):
                    Scroll(-1);
                    _items.FirstOrDefault(item => item.Available && item.CanSelect && item.Enabled)?.Select();
                    return true;
                case Keys.Home:
                case Keys.PageUp:
                    Scroll(key == Keys.Home ? -_items.Length : -_visibleCount);
                    _items.FirstOrDefault(item => item.Available && item.CanSelect && item.Enabled)?.Select();
                    return true;
                case Keys.End:
                case Keys.PageDown:
                    Scroll(key == Keys.End ? _items.Length : _visibleCount);
                    _items.LastOrDefault(item => item.Available && item.CanSelect && item.Enabled)?.Select();
                    return true;
                default:
                    return false;
            }
        }
    }

    private sealed class MenuWheelFilter(ToolStripDropDown root) : IMessageFilter
    {
        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != MouseWheelMessage || !root.Visible) return false;
            var delta = unchecked((short)((long)m.WParam >> 16));
            return RouteWheel(root, delta);
        }
    }

    private static bool RouteWheel(ToolStripDropDown menu, int delta)
    {
        while (menu.OwnerItem?.Owner is ToolStripDropDown parent) menu = parent;
        if (!NativeMethods.GetCursorPos(out var cursor)) return false;
        var hovered = HoveredMenu(menu, new Point(cursor.X, cursor.Y));
        if (hovered is null || !Viewports.TryGetValue(hovered, out var viewport)) return false;
        viewport.Wheel(delta);
        return true;
    }

    private static ToolStripDropDown? HoveredMenu(ToolStripDropDown menu, Point cursor)
    {
        if (!menu.Visible) return null;
        foreach (var item in menu.Items.OfType<ToolStripMenuItem>())
            if (item.HasDropDownItems && item.DropDown.Visible && HoveredMenu(item.DropDown, cursor) is { } child)
                return child;
        if (!NativeMethods.GetWindowRect(menu.Handle, out var bounds)) return null;
        return Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom).Contains(cursor) ? menu : null;
    }
}
