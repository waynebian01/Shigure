using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Shigure;

/// <summary>
/// 顶部横向职业图标条：图标固定尺寸、自左向右顺序排布（不均分整行）。
/// </summary>
internal sealed class ClassIconStrip : Panel
{
    public const int IconSize = UiTheme.ClassSpecIconSize;
    public const int CellSize = UiTheme.ClassSpecIconCellSize;
    public const int StripPadding = 2;
    public const int CellGap = UiTheme.IconStripCellGap;

    private readonly ToolTip _toolTip = new();
    private readonly List<ClassIconButton> _buttons = new();
    private int _selectedIndex = -1;
    private bool _suppressSelection;

    public ClassIconStrip()
    {
        DoubleBuffered = true;
        BackColor = Color.Transparent;
        Margin = Padding.Empty;
        Padding = new Padding(0);
        ApplyScaledMetrics();
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
            SuspendLayout();
            Controls.Clear();
            _buttons.Clear();
            _selectedIndex = -1;

            for (var i = 0; i < items.Count; i++)
            {
                var (classId, tooltip) = items[i];
                var index = i;
                var button = new ClassIconButton(classId);
                button.Click += (_, _) => SelectIndex(index, raiseEvent: true);
                _toolTip.SetToolTip(button, tooltip);
                _buttons.Add(button);
                Controls.Add(button);
            }

            ApplySelectionVisuals();
            LayoutButtons();
            ResumeLayout(true);
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

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyScaledMetrics();
        LayoutButtons();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyScaledMetrics();
        LayoutButtons();
    }

    private Size _lastLaidOutClientSize;

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        // 按钮 X 仅依赖索引与固有 cell 宽，宽度变化（顶栏裁切叠盖）无需重排。
        if (ClientSize.Height == _lastLaidOutClientSize.Height
            && ClientSize.Height > 0
            && _buttons.Count > 0)
        {
            return;
        }

        LayoutButtons();
    }

    private void ApplyScaledMetrics()
    {
        var height = ScaledStripHeight();
        Height = height;
        MinimumSize = new Size(0, height);
        MaximumSize = new Size(int.MaxValue, height);
    }

    private void LayoutButtons()
    {
        var count = _buttons.Count;
        if (count == 0)
        {
            return;
        }

        var pad = UiTheme.Scale(this, StripPadding);
        var cell = ScaledCellSize();
        var gap = UiTheme.Scale(this, CellGap);
        var y = Math.Max(0, (ClientSize.Height - cell) / 2);
        for (var i = 0; i < count; i++)
        {
            var x = pad + i * (cell + gap);
            _buttons[i].Bounds = new Rectangle(x, y, cell, cell);
        }

        _lastLaidOutClientSize = ClientSize;
    }

    private int ScaledCellSize() => UiTheme.Scale(this, CellSize);

    private int ScaledStripHeight()
        => ScaledCellSize() + (UiTheme.Scale(this, StripPadding) * 2);

    public int ScaledHeight => ScaledStripHeight();

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
            var drawnIconSize = Math.Min(
                UiTheme.Scale(this, IconSize),
                Math.Min(bounds.Width, bounds.Height) - UiTheme.Scale(this, 4));
            drawnIconSize = Math.Max(8, drawnIconSize);

            var iconBounds = new Rectangle(
                (bounds.Width - drawnIconSize) / 2,
                (bounds.Height - drawnIconSize) / 2,
                drawnIconSize,
                drawnIconSize);

            var frame = new Rectangle(
                iconBounds.X,
                iconBounds.Y,
                Math.Max(1, iconBounds.Width - 1),
                Math.Max(1, iconBounds.Height - 1));
            var radius = UiTheme.RoundedIconRadius(this, frame);
            using var shape = UiTheme.CreateRoundedRectanglePath(frame, radius);
            if (_selected || _hovered)
            {
                using var brush = new SolidBrush(_selected ? UiTheme.Hover : UiTheme.Field);
                g.FillPath(brush, shape);
            }
            if (ClassId is { } classId)
            {
                var icon = UiTheme.GetClassIcon(classId);
                if (icon is not null)
                {
                    UiTheme.DrawImageRounded(g, icon, frame, radius);
                }
                else
                {
                    TextRenderer.DrawText(
                        g,
                        "?",
                        Font,
                        frame,
                        UiTheme.Muted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
            else
            {
                TextRenderer.DrawText(
                    g,
                    "全部",
                    Font,
                    frame,
                    _selected ? UiTheme.Text : UiTheme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            using var border = new Pen(_selected ? UiTheme.Accent : UiTheme.Border);
            g.DrawPath(border, shape);
        }
    }
}
