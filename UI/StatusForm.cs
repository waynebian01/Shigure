using System.Drawing;
using System.Reflection;

namespace Shigure;

public sealed class StatusForm : Form
{
    private readonly List<(Button Button, Control View)> _navItems = new();
    private IReadOnlyList<RecognizedAuraInfo> _recognizedAuras = Array.Empty<RecognizedAuraInfo>();
    private RenderSnapshot? _lastSnapshot;
    private bool _hasKnownBounds;

    private ListView _stateList = null!;
    private ListView _auraList = null!;
    private ListView _dynamicUnitList = null!;
    private ListView _spellList = null!;
    private ListView _partyList = null!;
    private ListView _unitInfoList = null!;
    private TextBox _logTextBox = null!;
    private Panel _contentHost = null!;
    private Panel _settingsHost = null!;
    private Panel _moduleHost = null!;
    private Panel _auraRecognitionHost = null!;
    private Panel _aboutHost = null!;

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
            _hasKnownBounds = true;
            e.Cancel = true;
            Hide();
        }

        base.OnFormClosing(e);
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        Text = "设置";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1800, 1200);
        Size = new Size(920, 640);
        BackColor = UiTheme.Background;
        ForeColor = UiTheme.Text;
        ShowInTaskbar = false;
        TopMost = false;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Background,
            Padding = new Padding(18),
            RowCount = 2,
            ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        _settingsHost = CreatePageHost();
        _moduleHost = CreatePageHost();
        _auraRecognitionHost = CreatePageHost();
        _aboutHost = CreatePageHost();

        _stateList = UiTheme.CreateListView(Font, ("#", 56), ("名称", 150), ("值", 130));
        _auraList = UiTheme.CreateListView(Font, ("#", 48), ("光环", 140), ("值", 100));
        _dynamicUnitList = UiTheme.CreateListView(Font, ("类型", 86), ("名称", 120), ("值", 160));
        _spellList = UiTheme.CreateListView(Font, ("#", 56), ("技能", 150), ("状态", 110));

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
            Font = new Font(Font.FontFamily, 10F, FontStyle.Regular, GraphicsUnit.Point)
        };

        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = UiTheme.Background,
            Margin = new Padding(0, 0, 0, 12)
        };

        _contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding = new Padding(16),
            Margin = new Padding(0)
        };

        AddNavItem(nav, "通用", _settingsHost);
        AddNavItem(nav, "模块", _moduleHost);
        AddNavItem(nav, "光环识别", _auraRecognitionHost);
        AddNavItem(nav, "状态", BuildStatusPage());
        AddNavItem(nav, "队伍", BuildSection("队伍", _partyList, "当前队伍单位与扫描到的字段摘要"));
        AddNavItem(nav, "逻辑", BuildSection("逻辑", _unitInfoList, "运行时推荐目标与调试值"));
        AddNavItem(nav, "日志", BuildSection("日志", _logTextBox, "运行、模块匹配与施放记录"));
        AddNavItem(nav, "关于", _aboutHost);
        _aboutHost.Controls.Add(BuildAboutPanel());

        root.Controls.Add(nav, 0, 0);
        root.Controls.Add(_contentHost, 0, 1);

        ResumeLayout(false);
        SelectView(0);
    }

    private static Panel CreatePageHost()
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Margin = new Padding(0)
        };
    }

    private Control BuildStatusPage()
    {
        var statusSplit = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0)
        };
        statusSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        statusSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        statusSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        statusSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        statusSplit.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        statusSplit.Controls.Add(BuildSection("状态", _stateList, "基础字段与当前模块", isLast: false), 0, 0);
        statusSplit.Controls.Add(BuildSection("光环", _auraList, "光环层数与持续状态", isLast: false), 1, 0);
        statusSplit.Controls.Add(BuildSection("技能", _spellList, "冷却与可用状态", isLast: false), 2, 0);
        statusSplit.Controls.Add(BuildSection("动态单位", _dynamicUnitList, "模块运行时计算值"), 3, 0);
        return statusSplit;
    }

    private Control BuildSection(string title, Control content, string subtitle, bool isLast = true)
    {
        var section = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14),
            Margin = new Padding(0, 0, isLast ? 0 : 12, 0)
        };
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        section.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        section.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Text,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        }, 0, 0);
        section.Controls.Add(new Label
        {
            Text = subtitle,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        }, 0, 1);

        content.Dock = DockStyle.Fill;
        content.Margin = new Padding(0, 12, 0, 0);
        section.Controls.Add(content, 0, 2);
        return section;
    }

    public void AttachSettingsPanel(Control panel)
    {
        panel.Dock = DockStyle.Fill;
        _settingsHost.Controls.Add(panel);
    }

    public void AttachModuleEditor(Control panel)
    {
        panel.Dock = DockStyle.Fill;
        _moduleHost.Controls.Add(panel);
    }

    public void AttachAuraRecognitionPanel(Control panel)
    {
        panel.Dock = DockStyle.Fill;
        _auraRecognitionHost.Controls.Add(panel);
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
        _hasKnownBounds = true;
    }

    internal bool HasKnownBounds => _hasKnownBounds || Visible;

    private void AddNavItem(FlowLayoutPanel nav, string text, Control view)
    {
        view.Dock = DockStyle.Fill;
        _contentHost.Controls.Add(view);

        var button = new Button
        {
            Text = text,
            AutoSize = false,
            Size = new Size(96, 38),
            TextAlign = ContentAlignment.MiddleCenter,
            FlatStyle = FlatStyle.Flat,
            BackColor = UiTheme.Background,
            ForeColor = UiTheme.Muted,
            Margin = new Padding(0, 0, 9, 0),
            Padding = new Padding(0),
            Cursor = Cursors.Hand,
            TabStop = false
        };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = UiTheme.Border;
        button.FlatAppearance.MouseOverBackColor = UiTheme.Hover;
        button.FlatAppearance.MouseDownBackColor = UiTheme.Pressed;

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
            button.BackColor = selected ? UiTheme.Field : UiTheme.Background;
            button.ForeColor = selected ? UiTheme.Text : UiTheme.Muted;
            button.FlatAppearance.BorderColor = selected ? UiTheme.Accent : UiTheme.Border;
            if (selected)
            {
                view.BringToFront();
            }
        }
    }

    private Control BuildAboutPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(18)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "Shigure",
            AutoSize = true,
            ForeColor = UiTheme.Text,
            Font = new Font(Font.FontFamily, 18F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 22)
        };
        panel.Controls.Add(title, 0, 0);

        var assembly = Assembly.GetExecutingAssembly();
        var version = AppInfo.Version;
        var company = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;
        company = string.IsNullOrWhiteSpace(company) ? "Arasaka Corporation" : company;
        var details = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = 2,
            RowCount = 0,
            Padding = new Padding(0)
        };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddAboutRow(details, "产品", "Shigure");
        AddAboutRow(details, "公司", company);
        AddAboutRow(details, "版本", version);
        AddAboutRow(details, "类型", "冲锋枪"); 
        AddAboutRow(details, "介绍", "它一分钟打出去的子弹比荒坂偷的税还要多。"); 
        AddAboutRow(details, "用途", "有时人们只想把子弹全打出去，在硝烟过后品味眼前的一片狼藉。");
        AddAboutRow(details, "模块目录", FormatAboutPath(ModuleStore.ResolveModuleDirectory(AppPaths.BaseDirectory)));
        AddAboutRow(details, "配置目录", FormatAboutPath(ConfigService.ResolveConfigPath(AppPaths.BaseDirectory)));

        panel.Controls.Add(details, 0, 1);
        return panel;
    }

    private static string FormatAboutPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "-";
        }

        try
        {
            var baseDirectory = Path.GetFullPath(AppPaths.BaseDirectory);
            var fullPath = Path.GetFullPath(path);
            var relativePath = Path.GetRelativePath(baseDirectory, fullPath);
            return string.IsNullOrWhiteSpace(relativePath) ? "." : relativePath;
        }
        catch
        {
            return path;
        }
    }

    private static void AddAboutRow(TableLayoutPanel panel, string name, string value)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            Text = name,
            AutoSize = false,
            Width = 104,
            Height = 26,
            ForeColor = UiTheme.Muted,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 18, 14)
        }, 0, row);
        panel.Controls.Add(new Label
        {
            Text = value,
            AutoSize = true,
            MaximumSize = new Size(580, 0),
            ForeColor = UiTheme.Text,
            Margin = new Padding(0, 0, 0, 14)
        }, 1, row);
    }

    public void ShowOrActivate(RenderSnapshot? snapshot)
    {
        if (snapshot is not null)
        {
            _lastSnapshot = snapshot;
            UpdateLists(snapshot);
        }

        if (!Visible)
        {
            Show();
            _hasKnownBounds = true;
            EnsureNotTopmost();
        }
        else
        {
            _hasKnownBounds = true;
            Activate();
        }
    }

    public void ShowSettings(RenderSnapshot? snapshot)
    {
        SelectView(0);
        ShowOrActivate(snapshot);
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
        _lastSnapshot = snapshot;
        if (!Visible)
        {
            return;
        }

        UpdateLists(snapshot);
    }

    public void SetRecognizedAuras(IReadOnlyList<RecognizedAuraInfo> auras)
    {
        _recognizedAuras = auras.Count == 0
            ? Array.Empty<RecognizedAuraInfo>()
            : auras.ToArray();

        if (Visible)
        {
            UpdateAuraList(_lastSnapshot);
        }
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
        UpdateAuraList(snapshot);
        UpdateDynamicUnitList(snapshot);
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
                if (key is "spells" or "auras" or "group" || key.StartsWith('$'))
                {
                    continue;
                }

                index++;
                items.Add(new ListViewItem(new[] { index.ToString(), key, UiTheme.FormatValue(value) }));
            }
        }

        ReplaceItems(_stateList, items);
    }

    private void UpdateAuraList(RenderSnapshot? snapshot)
    {
        var items = new List<ListViewItem>();
        var index = 0;
        if (snapshot?.State is not null)
        {
            foreach (var (key, value) in snapshot.State.Auras)
            {
                index++;
                items.Add(new ListViewItem(new[] { index.ToString(), key, UiTheme.FormatValue(value) }));
            }
        }

        foreach (var aura in _recognizedAuras)
        {
            index++;
            var item = new ListViewItem(new[]
            {
                index.ToString(),
                $"[识别] {aura.Name}",
                UiTheme.FormatValue(aura.Value)
            })
            {
                ToolTipText = FormatRecognizedAuraToolTip(aura)
            };
            items.Add(item);
        }

        if (items.Count == 0)
        {
            items.Add(new ListViewItem(new[] { "-", "光环", "无数据" }));
        }

        ReplaceItems(_auraList, items);
    }

    private void UpdateDynamicUnitList(RenderSnapshot snapshot)
    {
        var items = new List<ListViewItem>();
        if (snapshot.State is null)
        {
            items.Add(new ListViewItem(new[] { "-", "动态单位", "等待游戏状态" }));
        }
        else if (snapshot.DynamicValues.Count == 0)
        {
            items.Add(new ListViewItem(new[] { "-", "动态单位", "无数据" }));
        }
        else
        {
            foreach (var value in snapshot.DynamicValues)
            {
                items.Add(new ListViewItem(new[] { value.Kind, value.Name, value.Value }));
            }
        }

        ReplaceItems(_dynamicUnitList, items);
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

    private static string FormatRecognizedAuraToolTip(RecognizedAuraInfo aura)
    {
        var template = aura.TemplateScore is null ? "-" : aura.TemplateScore.Value.ToString("0.000");
        return $"识别光环  {aura.Row} #{aura.Index}  值: {aura.Value}  dHash: {aura.Hash}  距离: {aura.HashDistance?.ToString() ?? "-"}  相似度: {template}";
    }

    private static void ReplaceItems(ListView listView, IReadOnlyList<ListViewItem> items)
    {
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.ToolTipText))
            {
                item.ToolTipText = string.Join(
                    "  ",
                    item.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(subItem => subItem.Text));
            }
        }

        if (HasSameItems(listView, items))
        {
            return;
        }

        if (CanUpdateInPlace(listView, items))
        {
            UpdateItemsInPlace(listView, items);
            return;
        }

        listView.BeginUpdate();
        listView.Items.Clear();
        listView.Items.AddRange(items.ToArray());
        listView.EndUpdate();
    }

    private static bool HasSameItems(ListView listView, IReadOnlyList<ListViewItem> items)
    {
        if (!CanUpdateInPlace(listView, items))
        {
            return false;
        }

        for (var row = 0; row < items.Count; row++)
        {
            var current = listView.Items[row];
            var next = items[row];
            if (current.ToolTipText != next.ToolTipText)
            {
                return false;
            }

            for (var column = 0; column < next.SubItems.Count; column++)
            {
                if (current.SubItems[column].Text != next.SubItems[column].Text)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool CanUpdateInPlace(ListView listView, IReadOnlyList<ListViewItem> items)
    {
        if (listView.Items.Count != items.Count)
        {
            return false;
        }

        for (var row = 0; row < items.Count; row++)
        {
            if (listView.Items[row].SubItems.Count != items[row].SubItems.Count)
            {
                return false;
            }
        }

        return true;
    }

    private static void UpdateItemsInPlace(ListView listView, IReadOnlyList<ListViewItem> items)
    {
        listView.BeginUpdate();
        for (var row = 0; row < items.Count; row++)
        {
            var current = listView.Items[row];
            var next = items[row];
            current.ToolTipText = next.ToolTipText;
            for (var column = 0; column < next.SubItems.Count; column++)
            {
                var nextText = next.SubItems[column].Text;
                if (current.SubItems[column].Text != nextText)
                {
                    current.SubItems[column].Text = nextText;
                }
            }
        }

        listView.EndUpdate();
    }
}
