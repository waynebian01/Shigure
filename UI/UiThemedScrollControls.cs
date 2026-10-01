namespace Shigure;

// WinForms 的自定义控件需在读取 CreateParams 前选择深色主题，
// 否则其原生滚动条可能继续使用浅色系统外观。
internal class UiThemedPanel : Panel
{
    protected override CreateParams CreateParams
    {
        get
        {
            SetStyle(ControlStyles.ApplyThemingImplicitly, true);
            return base.CreateParams;
        }
    }
}

internal class UiThemedFlowLayoutPanel : FlowLayoutPanel
{
    protected override CreateParams CreateParams
    {
        get
        {
            SetStyle(ControlStyles.ApplyThemingImplicitly, true);
            return base.CreateParams;
        }
    }
}

internal class UiThemedListBox : ListBox
{
    protected override CreateParams CreateParams
    {
        get
        {
            SetStyle(ControlStyles.ApplyThemingImplicitly, true);
            return base.CreateParams;
        }
    }
}

internal class UiThemedListView : ListView
{
    private const int WmPaint = 0x000F;

    protected override CreateParams CreateParams
    {
        get
        {
            SetStyle(ControlStyles.ApplyThemingImplicitly, true);
            return base.CreateParams;
        }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WmPaint)
        {
            PaintEmptyRowBands();
        }
    }

    // 条目下面的空白区按行交替底色，和已有数据行的横向色差接上。
    private void PaintEmptyRowBands()
    {
        if (!IsHandleCreated || View != View.Details || ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        var rowHeight = Items.Count > 0
            ? Math.Max(1, GetItemRect(0).Height)
            : Math.Max(UiTheme.Scale(this, 36), Font.Height + UiTheme.Scale(this, 14));
        var top = Items.Count == 0 ? 0 : GetItemRect(Items.Count - 1).Bottom;
        if (top >= ClientSize.Height)
        {
            return;
        }

        using var surface = new SolidBrush(UiTheme.Surface);
        using var alternate = new SolidBrush(UiTheme.RowAlt);
        using var graphics = CreateGraphics();
        var index = Items.Count;
        while (top < ClientSize.Height)
        {
            var height = Math.Min(rowHeight, ClientSize.Height - top);
            graphics.FillRectangle(
                index % 2 == 0 ? surface : alternate,
                0,
                top,
                ClientSize.Width,
                height);
            top += rowHeight;
            index++;
        }
    }
}

internal class UiThemedDataGridView : DataGridView
{
    protected override CreateParams CreateParams
    {
        get
        {
            SetStyle(ControlStyles.ApplyThemingImplicitly, true);
            return base.CreateParams;
        }
    }
}

internal class UiThemedTextBox : TextBox
{
    protected override CreateParams CreateParams
    {
        get
        {
            SetStyle(ControlStyles.ApplyThemingImplicitly, true);
            return base.CreateParams;
        }
    }
}
