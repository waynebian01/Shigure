using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Shigure;

/// <summary>
/// 顶部横向职业图标条：固定格宽，不随窗口拉伸；可选「全部」项（ClassId=null）。
/// </summary>
internal sealed class ClassIconStrip : Panel
{
    public const int CellSize = 48;
    public const int CellGap = 6;
    public const int StripPadding = 8;

    private readonly FlowLayoutPanel _flow;
    private readonly ToolTip _toolTip = new();
    private readonly List<ClassIconButton> _buttons = new();
    private int _selectedIndex = -1;
    private bool _suppressSelection;

    public ClassIconStrip()
    {
        DoubleBuffered = true;
        BackColor = UiTheme.SurfaceRaised;
        Margin = new Padding(0, 0, 0, UiTheme.PageGap);
        Padding = new Padding(0);
        Height = StripHeight;
        MinimumSize = new Size(0, StripHeight);
        MaximumSize = new Size(int.MaxValue, StripHeight);

        _flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(StripPadding, StripPadding, StripPadding, StripPadding)
        };
        Controls.Add(_flow);
    }

    public static int StripHeight => CellSize + (StripPadding * 2);

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex => _selectedIndex;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int? SelectedClassId
        => _selectedIndex >= 0 && _selectedIndex < _buttons.Count
            ? _buttons[_selectedIndex].ClassId
            : null;

    public event EventHandler? SelectionChanged;

    public void SetItems(IReadOnlyList<(int? ClassId, string Tooltip)> items)
    {
        _suppressSelection = true;
        try
        {
            _flow.SuspendLayout();
            _flow.Controls.Clear();
            _buttons.Clear();
            _selectedIndex = -1;

            for (var i = 0; i < items.Count; i++)
            {
                var (classId, tooltip) = items[i];
                var index = i;
                var button = new ClassIconButton(classId)
                {
                    Margin = new Padding(0, 0, CellGap, 0),
                    Size = new Size(CellSize, CellSize)
                };
                button.Click += (_, _) => SelectIndex(index, raiseEvent: true);
                _toolTip.SetToolTip(button, tooltip);
                _buttons.Add(button);
                _flow.Controls.Add(button);
            }

            ApplySelectionVisuals();
            _flow.ResumeLayout(true);
        }
        finally
        {
            _suppressSelection = false;
        }
    }

    public void SelectIndex(int index, bool raiseEvent = false)
    {
        if (index < -1 || index >= _buttons.Count)
        {
            return;
        }

        var changed = _selectedIndex != index;
        _selectedIndex = index;
        ApplySelectionVisuals();

        if (raiseEvent && changed && !_suppressSelection)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SelectClassId(int? classId, bool raiseEvent = false)
    {
        for (var i = 0; i < _buttons.Count; i++)
        {
            if (_buttons[i].ClassId == classId)
            {
                SelectIndex(i, raiseEvent);
                return;
            }
        }
    }

    private void ApplySelectionVisuals()
    {
        for (var i = 0; i < _buttons.Count; i++)
        {
            _buttons[i].Selected = i == _selectedIndex;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed class ClassIconButton : Control
    {
        private bool _selected;
        private bool _hovered;

        public ClassIconButton(int? classId)
        {
            ClassId = classId;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint
                | ControlStyles.SupportsTransparentBackColor,
                true);
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
            TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = classId is null
                ? "全部"
                : ClassNames.GetClassAndSpecName(classId, null).ClassName ?? $"职业{classId}";
        }

        public int? ClassId { get; }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value)
                {
                    return;
                }

                _selected = value;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var bounds = ClientRectangle;
            var fill = _selected
                ? UiTheme.Hover
                : _hovered
                    ? UiTheme.Field
                    : UiTheme.SurfaceRaised;
            using (var brush = new SolidBrush(fill))
            {
                g.FillRectangle(brush, bounds);
            }

            if (_selected || _hovered)
            {
                using var indicator = new SolidBrush(_selected ? Color.White : UiTheme.Muted);
                g.FillRectangle(indicator, 0, 6, 3, bounds.Height - 12);
            }

            var inset = 6;
            var iconBounds = new Rectangle(
                inset,
                inset,
                Math.Max(8, bounds.Width - inset * 2),
                Math.Max(8, bounds.Height - inset * 2));

            if (ClassId is { } classId)
            {
                var icon = UiTheme.GetClassIcon(classId);
                if (icon is not null)
                {
                    g.DrawImage(icon, iconBounds);
                }
                else
                {
                    TextRenderer.DrawText(
                        g,
                        "?",
                        Font,
                        iconBounds,
                        UiTheme.Muted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }

                using var border = new Pen(_selected ? UiTheme.Accent : UiTheme.Border, _selected ? 2f : 1f);
                g.DrawRectangle(
                    border,
                    iconBounds.X,
                    iconBounds.Y,
                    iconBounds.Width - 1,
                    iconBounds.Height - 1);
            }
            else
            {
                using var border = new Pen(_selected ? UiTheme.Accent : UiTheme.Border, _selected ? 2f : 1f);
                g.DrawRectangle(
                    border,
                    iconBounds.X,
                    iconBounds.Y,
                    iconBounds.Width - 1,
                    iconBounds.Height - 1);
                TextRenderer.DrawText(
                    g,
                    "全部",
                    Font,
                    iconBounds,
                    _selected ? UiTheme.Text : UiTheme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
