using System.Drawing;

namespace Shigure;

public sealed class StatusForm : Form
{
    private readonly List<(Button Button, Control View)> _navItems = new();

    private ListView _stateList = null!;
    private ListView _spellList = null!;
    private ListView _partyList = null!;
    private ListView _unitInfoList = null!;
    private TextBox _logTextBox = null!;
    private Panel _contentHost = null!;
    private Panel _moduleHost = null!;

    public StatusForm()
    {
        InitializeComponent();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UiTheme.ApplyDarkTitleBar(this);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnFormClosing(e);
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        Text = "Shigure - 状态";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 420);
        Size = new Size(820, 560);
        BackColor = UiTheme.Background;
        ForeColor = UiTheme.Text;
        ShowInTaskbar = false;
        TopMost = false;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Background,
            Padding = new Padding(12),
            RowCount = 2,
            ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        _moduleHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Margin = new Padding(0)
        };

        _stateList = UiTheme.CreateListView(Font, ("#", 40), ("名称", 150), ("值", 130));
        _spellList = UiTheme.CreateListView(Font, ("#", 40), ("技能", 150), ("状态", 110));

        var statusSplit = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        statusSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        statusSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        statusSplit.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _stateList.Margin = new Padding(0, 0, 5, 0);
        _spellList.Margin = new Padding(5, 0, 0, 0);
        statusSplit.Controls.Add(_stateList, 0, 0);
        statusSplit.Controls.Add(_spellList, 1, 0);
        _partyList = UiTheme.CreateListView(Font, ("单位", 110), ("摘要", 700));
        _unitInfoList = UiTheme.CreateListView(Font, ("名称", 200), ("值", 480));
        _logTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = UiTheme.Surface,
            ForeColor = UiTheme.Text,
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 9F)
        };

        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = UiTheme.Background,
            Margin = new Padding(0, 0, 0, 8)
        };

        _contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding = new Padding(10),
            Margin = new Padding(0)
        };

        AddNavItem(nav, "模块", _moduleHost);
        AddNavItem(nav, "状态", statusSplit);
        AddNavItem(nav, "队伍", _partyList);
        AddNavItem(nav, "逻辑", _unitInfoList);
        AddNavItem(nav, "日志", _logTextBox);

        root.Controls.Add(nav, 0, 0);
        root.Controls.Add(_contentHost, 0, 1);

        ResumeLayout(false);
        SelectView(0);
    }

    public void AttachModuleEditor(Control panel)
    {
        panel.Dock = DockStyle.Fill;
        _moduleHost.Controls.Add(panel);
    }

    internal WindowBounds GetCachedBounds()
    {
        return new WindowBounds
        {
            X = Left,
            Y = Top,
            Width = Width,
            Height = Height
        };
    }

    internal void ApplyCachedBounds(WindowBounds? bounds)
    {
        if (bounds is null)
        {
            return;
        }

        var restoredBounds = new Rectangle(
            bounds.X,
            bounds.Y,
            Math.Max(MinimumSize.Width, bounds.Width),
            Math.Max(MinimumSize.Height, bounds.Height));

        if (!UiCacheStore.IsBoundsVisible(restoredBounds))
        {
            return;
        }

        StartPosition = FormStartPosition.Manual;
        Bounds = restoredBounds;
    }

    private void AddNavItem(FlowLayoutPanel nav, string text, Control view)
    {
        view.Dock = DockStyle.Fill;
        _contentHost.Controls.Add(view);

        var button = new Button
        {
            Text = text,
            AutoSize = false,
            Size = new Size(92, 30),
            TextAlign = ContentAlignment.MiddleCenter,
            FlatStyle = FlatStyle.Flat,
            BackColor = UiTheme.Background,
            ForeColor = UiTheme.Muted,
            Margin = new Padding(0, 0, 6, 0),
            Padding = new Padding(0),
            TabStop = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = UiTheme.Hover;

        var index = _navItems.Count;
        button.Click += (_, _) => SelectView(index);
        _navItems.Add((button, view));
        nav.Controls.Add(button);
    }

    private void SelectView(int index)
    {
        for (var i = 0; i < _navItems.Count; i++)
        {
            var (button, view) = _navItems[i];
            var selected = i == index;
            button.BackColor = selected ? UiTheme.Surface : UiTheme.Background;
            button.ForeColor = selected ? UiTheme.Accent : UiTheme.Muted;
            if (selected)
            {
                view.BringToFront();
            }
        }
    }

    public void ShowOrActivate(RenderSnapshot? snapshot)
    {
        if (snapshot is not null)
        {
            UpdateLists(snapshot);
        }

        if (!Visible)
        {
            Show();
            EnsureNotTopmost();
        }
        else
        {
            Activate();
        }
    }

    private void EnsureNotTopmost()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        TopMost = false;
        NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HwndNotTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNomove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }

    public void ApplySnapshot(RenderSnapshot snapshot)
    {
        if (!Visible)
        {
            return;
        }

        UpdateLists(snapshot);
    }

    public void AppendLog(string message)
    {
        if (_logTextBox.IsDisposed)
        {
            return;
        }

        var line = $"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}";
        _logTextBox.AppendText(line);

        if (_logTextBox.TextLength > 24000)
        {
            _logTextBox.Text = _logTextBox.Text[^18000..];
            _logTextBox.SelectionStart = _logTextBox.TextLength;
            _logTextBox.ScrollToCaret();
        }
    }

    private void UpdateLists(RenderSnapshot snapshot)
    {
        UpdateStateList(snapshot);
        UpdateSpellList(snapshot);
        UpdatePartyList(snapshot);
        UpdateUnitInfoList(snapshot);
    }

    private void UpdateStateList(RenderSnapshot snapshot)
    {
        var items = new List<ListViewItem>();
        if (snapshot.State is null)
        {
            items.Add(new ListViewItem(new[] { "-", "状态", "等待游戏状态" }));
        }
        else
        {
            var index = 0;
            if (!string.IsNullOrWhiteSpace(snapshot.ModuleName))
            {
                index++;
                items.Add(new ListViewItem(new[] { index.ToString(), "匹配模块", snapshot.ModuleName }));
            }

            foreach (var (key, value) in snapshot.State.Values)
            {
                if (key is "spells" or "group")
                {
                    continue;
                }

                index++;
                items.Add(new ListViewItem(new[] { index.ToString(), key, UiTheme.FormatValue(value) }));
            }
        }

        ReplaceItems(_stateList, items);
    }

    private void UpdateSpellList(RenderSnapshot snapshot)
    {
        var items = new List<ListViewItem>();
        if (snapshot.State is null || snapshot.State.Spells.Count == 0)
        {
            items.Add(new ListViewItem(new[] { "-", "技能", "无数据" }));
        }
        else
        {
            var index = 0;
            foreach (var (key, value) in snapshot.State.Spells)
            {
                index++;
                items.Add(new ListViewItem(new[] { index.ToString(), key, UiTheme.FormatValue(value) }));
            }
        }

        ReplaceItems(_spellList, items);
    }

    private void UpdatePartyList(RenderSnapshot snapshot)
    {
        var items = new List<ListViewItem>();
        var partyCount = snapshot.State?.GetInt("队伍人数") ?? 0;
        if (snapshot.State is null || partyCount <= 0)
        {
            items.Add(new ListViewItem(new[] { "队伍", "无队伍数据" }));
        }
        else
        {
            for (var i = 1; i <= partyCount; i++)
            {
                var unitKey = i.ToString();
                if (!snapshot.State.Group.TryGetValue(unitKey, out var unitData))
                {
                    items.Add(new ListViewItem(new[] { $"Unit {unitKey}", "-" }));
                    continue;
                }

                var summary = string.Join("  ", unitData.Select(kv => $"{kv.Key}: {UiTheme.FormatValue(kv.Value)}"));
                items.Add(new ListViewItem(new[] { $"Unit {unitKey}", summary }));
            }
        }

        ReplaceItems(_partyList, items);
    }

    private void UpdateUnitInfoList(RenderSnapshot snapshot)
    {
        var items = new List<ListViewItem>();
        if (snapshot.UnitInfo.Count == 0)
        {
            items.Add(new ListViewItem(new[] { "逻辑信息", "无推荐目标" }));
        }
        else
        {
            foreach (var (key, value) in snapshot.UnitInfo.OrderBy(kv => kv.Key))
            {
                items.Add(new ListViewItem(new[] { key, UiTheme.FormatValue(value) }));
            }
        }

        ReplaceItems(_unitInfoList, items);
    }

    private static void ReplaceItems(ListView listView, IReadOnlyList<ListViewItem> items)
    {
        listView.BeginUpdate();
        listView.Items.Clear();
        listView.Items.AddRange(items.ToArray());
        listView.EndUpdate();
    }
}
