using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;

namespace Shigure;

internal enum SettingsPage
{
    General,
    Config,
    Macros,
    Modules,
    Status,
    Party,
    Logic,
    Logs,
    About
}

internal enum SettingsNavIcon
{
    General,
    Config,
    Macros,
    Modules,
    Status,
    Party,
    Logic,
    Logs,
    About
}

public sealed class StatusForm : Form
{
    private const string AboutWatermarkResourcePath = "Assets.arasaka-icon-transparent.png";
    private const int AboutWatermarkSize = 440;
    private const int AboutWatermarkTopMargin = 16;
    private const float AboutWatermarkOpacity = 0.08F;

    private readonly List<(SettingsNavButton Button, Control View, SettingsPage Page)> _navItems = new();
    private readonly Dictionary<ListView, Label> _listCounts = new();
    private readonly HashSet<SettingsPage> _dirtyPages = new();
    private readonly ToolTip _toolTip = new();
    private RenderSnapshot? _lastSnapshot;
    private bool _hasKnownBounds;
    private bool _autoScrollLog = true;
    private SettingsPage _selectedPage = SettingsPage.General;

    private ListView _stateList = null!;
    private ListView _auraList = null!;
    private ListView _dynamicUnitList = null!;
    private ListView _spellList = null!;
    private ListView _partyList = null!;
    private ListView _unitInfoList = null!;
    private TextBox _logTextBox = null!;
    private Panel _contentHost = null!;
    private Panel _settingsHost = null!;
    private Panel _configHost = null!;
    private Panel _macrosHost = null!;
    private Panel _moduleHost = null!;
    private Panel _aboutHost = null!;

    internal string SelectedPageKey => _selectedPage.ToString();

    public StatusForm()
    {
        InitializeComponent();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UiTheme.ApplyDarkTitleBar(this);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_hasKnownBounds)
        {
            return;
        }

        var workingArea = Screen.FromControl(this).WorkingArea;
        var targetWidth = Math.Min(1280, Math.Max(MinimumSize.Width, workingArea.Width - 80));
        var targetHeight = Math.Min(800, Math.Max(MinimumSize.Height, workingArea.Height - 80));
        Size = new Size(targetWidth, targetHeight);
        Location = new Point(
            workingArea.Left + Math.Max(0, (workingArea.Width - Width) / 2),
            workingArea.Top + Math.Max(0, (workingArea.Height - Height) / 2));
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
        MinimumSize = new Size(1040, 640);
        Size = new Size(1280, 800);
        BackColor = UiTheme.Background;
        ForeColor = UiTheme.Text;
        ShowInTaskbar = false;
        TopMost = false;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Background,
            Padding = new Padding(0),
            RowCount = 1,
            ColumnCount = 2,
            Margin = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 216));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        _settingsHost = CreatePageHost();
        _configHost = CreatePageHost();
        _macrosHost = CreatePageHost();
        _moduleHost = CreatePageHost();
        _aboutHost = CreatePageHost();

        _stateList = UiTheme.CreateListView(Font, "status-state",
            new UiTheme.ListColumn("#", 48, 48, FixedWidth: true),
            new UiTheme.ListColumn("名称", 110, 260),
            new UiTheme.ListColumn("值", 100, 900, FillRemaining: true));
        _auraList = UiTheme.CreateListView(Font, "status-aura",
            new UiTheme.ListColumn("#", 48, 48, FixedWidth: true),
            new UiTheme.ListColumn("光环", 120, 300),
            new UiTheme.ListColumn("值", 96, 900, FillRemaining: true));
        _dynamicUnitList = UiTheme.CreateListView(Font, "status-dynamic-unit",
            new UiTheme.ListColumn("类型", 72, 160),
            new UiTheme.ListColumn("名称", 92, 280),
            new UiTheme.ListColumn("值", 96, 900, FillRemaining: true));
        _spellList = UiTheme.CreateListView(Font, "status-spell",
            new UiTheme.ListColumn("#", 48, 48, FixedWidth: true),
            new UiTheme.ListColumn("技能", 120, 300),
            new UiTheme.ListColumn("状态", 100, 900, FillRemaining: true));

        _partyList = UiTheme.CreateListView(Font, "status-party",
            new UiTheme.ListColumn("单位", 120, 180, FixedWidth: true),
            new UiTheme.ListColumn("摘要", 320, 1600, FillRemaining: true));
        _unitInfoList = UiTheme.CreateListView(Font, "status-unit-info",
            new UiTheme.ListColumn("名称", 180, 320),
            new UiTheme.ListColumn("值", 320, 1400, FillRemaining: true));
        _logTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = UiTheme.Surface,
            ForeColor = UiTheme.Text,
            BorderStyle = BorderStyle.None,
            Font = new Font("Cascadia Mono", 9.5F, FontStyle.Regular, GraphicsUnit.Point)
        };

        var navShell = BuildNavigationShell(out var nav);

        _contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding = new Padding(20),
            Margin = new Padding(0)
        };

        AddNavGroup(nav, "常用");
        AddNavItem(nav, SettingsPage.General, SettingsNavIcon.General, "通用", CreatePageShell("通用", "运行控制、配置同步与模块选择", _settingsHost));
        AddNavGroup(nav, "编辑");
        AddNavItem(nav, SettingsPage.Config, SettingsNavIcon.Config, "配置", CreatePageShell("配置", "编辑职业、专精和扫描字段", _configHost));
        AddNavItem(nav, SettingsPage.Macros, SettingsNavIcon.Macros, "宏", CreatePageShell("宏", "维护职业动态宏、静态宏与特殊宏", _macrosHost));
        AddNavItem(nav, SettingsPage.Modules, SettingsNavIcon.Modules, "模块", CreatePageShell("模块", "创建、匹配并维护运行模块", _moduleHost));
        AddNavGroup(nav, "监控");
        AddNavItem(nav, SettingsPage.Status, SettingsNavIcon.Status, "状态", CreatePageShell("状态", string.Empty, BuildStatusPage()));
        AddNavItem(nav, SettingsPage.Party, SettingsNavIcon.Party, "队伍", CreatePageShell("队伍", "当前队伍单位与扫描字段摘要", BuildSection("队伍成员", _partyList, "实时队伍数据")));
        AddNavItem(nav, SettingsPage.Logic, SettingsNavIcon.Logic, "逻辑", CreatePageShell("逻辑", "运行时推荐目标与调试值", BuildSection("逻辑信息", _unitInfoList, "当前模块的决策输出")));
        AddNavItem(nav, SettingsPage.Logs, SettingsNavIcon.Logs, "日志", CreatePageShell("日志", "运行、模块匹配与施放记录", BuildLogPage()));
        AddNavGroup(nav, "系统");
        AddNavItem(nav, SettingsPage.About, SettingsNavIcon.About, "关于", CreatePageShell("关于", "应用信息与状态字段参考", _aboutHost));
        _aboutHost.Controls.Add(BuildAboutPanel());

        root.Controls.Add(navShell, 0, 0);
        root.Controls.Add(_contentHost, 1, 0);

        InitializeEmptyLists();
        ResumeLayout(false);
        SelectView(SettingsPage.General);
    }

    private void InitializeEmptyLists()
    {
        ReplaceItems(_stateList, [new ListViewItem(["-", "状态", "等待游戏状态"])]);
        ReplaceItems(_auraList, [new ListViewItem(["-", "光环", "无数据"])]);
        ReplaceItems(_spellList, [new ListViewItem(["-", "技能", "无数据"])]);
        ReplaceItems(_dynamicUnitList, [new ListViewItem(["-", "动态单位", "等待游戏状态"])]);
        ReplaceItems(_partyList, [new ListViewItem(["队伍", "无队伍数据"])]);
        ReplaceItems(_unitInfoList, [new ListViewItem(["逻辑信息", "无推荐目标"])]);
    }

    private Control BuildNavigationShell(out FlowLayoutPanel nav)
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Background,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12, 16, 12, 12),
            Margin = new Padding(0)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        var brand = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Background,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(10, 0, 0, 0)
        };
        brand.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        brand.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        brand.Controls.Add(new Label
        {
            Text = "SHIGURE",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Text,
            Font = new Font(Font.FontFamily, 13F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        }, 0, 0);
        brand.Controls.Add(new Label
        {
            Text = "CONTROL CENTER",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Accent,
            Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Padding(0)
        }, 0, 1);
        shell.Controls.Add(brand, 0, 0);

        nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = UiTheme.Background,
            Margin = new Padding(0)
        };
        shell.Paint += (_, e) =>
        {
            using var divider = new Pen(UiTheme.Border);
            e.Graphics.DrawLine(divider, shell.ClientSize.Width - 1, 0, shell.ClientSize.Width - 1, shell.ClientSize.Height);
        };
        shell.Controls.Add(nav, 0, 1);
        shell.Controls.Add(new Label
        {
            Text = $"v{AppInfo.Version}",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
            TextAlign = ContentAlignment.BottomLeft,
            Padding = new Padding(4, 0, 0, 0),
            Margin = new Padding(0)
        }, 0, 2);
        return shell;
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

    private Control CreatePageShell(string title, string subtitle, Control content)
    {
        var hasSubtitle = !string.IsNullOrWhiteSpace(subtitle);
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0)
        };
        // 预留标题实际高度与 header 的 14px 下边距，避免高 DPI 下标题被裁切。
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, hasSubtitle ? 84 : 60));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 1,
            RowCount = hasSubtitle ? 2 : 1,
            Margin = new Padding(0, 0, 0, 14)
        };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        header.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Text,
            Font = new Font(Font.FontFamily, 16F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        }, 0, 0);
        if (hasSubtitle)
        {
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            header.Controls.Add(new Label
            {
                Text = subtitle,
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.Muted,
                Font = new Font(Font.FontFamily, 9.5F, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            }, 0, 1);
        }
        shell.Controls.Add(header, 0, 0);

        content.Dock = DockStyle.Fill;
        content.Margin = new Padding(0);
        shell.Controls.Add(content, 0, 1);
        return shell;
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
        for (var i = 0; i < 4; i++)
        {
            statusSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }
        statusSplit.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var state = BuildSection("状态", _stateList, "基础字段与当前模块");
        state.Margin = new Padding(0, 0, 6, 0);
        var aura = BuildSection("光环", _auraList, "光环数值状态");
        aura.Margin = new Padding(6, 0, 6, 0);
        var spell = BuildSection("技能", _spellList, "冷却与可用状态");
        spell.Margin = new Padding(6, 0, 6, 0);
        var units = BuildSection("动态单位", _dynamicUnitList, "模块运行时计算值");
        units.Margin = new Padding(6, 0, 0, 0);
        statusSplit.Controls.Add(state, 0, 0);
        statusSplit.Controls.Add(aura, 1, 0);
        statusSplit.Controls.Add(spell, 2, 0);
        statusSplit.Controls.Add(units, 3, 0);
        return statusSplit;
    }

    private TableLayoutPanel BuildSection(string title, Control content, string subtitle)
    {
        var section = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16),
            Margin = new Padding(0)
        };
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        section.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var heading = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        heading.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Text,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        }, 0, 0);
        if (content is ListView listView)
        {
            var countLabel = new Label
            {
                Text = "0 项",
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.Accent,
                BackColor = UiTheme.AccentSoft,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(4, 2, 0, 2)
            };
            _listCounts[listView] = countLabel;
            heading.Controls.Add(countLabel, 1, 0);
        }
        section.Controls.Add(heading, 0, 0);
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

    private Control BuildLogPage()
    {
        var card = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(16),
            Margin = new Padding(0)
        };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        var copyButton = UiTheme.CreateButton("复制全部", UiTheme.ButtonKind.Secondary);
        copyButton.Margin = new Padding(0, 0, 8, 6);
        copyButton.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_logTextBox.Text))
            {
                Clipboard.SetText(_logTextBox.Text);
            }
        };
        var clearButton = UiTheme.CreateButton("清空显示", UiTheme.ButtonKind.Danger);
        clearButton.Margin = new Padding(0, 0, 16, 6);
        clearButton.Click += (_, _) => _logTextBox.Clear();
        var autoScroll = new CheckBox
        {
            Text = "自动滚动",
            Checked = true,
            AutoSize = true,
            ForeColor = UiTheme.Text,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0, 8, 0, 0)
        };
        autoScroll.CheckedChanged += (_, _) => _autoScrollLog = autoScroll.Checked;
        toolbar.Controls.Add(copyButton);
        toolbar.Controls.Add(clearButton);
        toolbar.Controls.Add(autoScroll);
        card.Controls.Add(toolbar, 0, 0);
        card.Controls.Add(_logTextBox, 0, 1);
        return card;
    }

    public void AttachSettingsPanel(Control panel)
    {
        panel.Dock = DockStyle.Fill;
        _settingsHost.Controls.Add(panel);
    }

    public void AttachConfigEditor(Control panel)
    {
        panel.Dock = DockStyle.Fill;
        _configHost.Controls.Add(panel);
    }

    public void AttachMacrosEditor(Control panel)
    {
        panel.Dock = DockStyle.Fill;
        _macrosHost.Controls.Add(panel);
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

        var requestedBounds = new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        if (!UiCacheStore.IsBoundsVisible(requestedBounds))
        {
            return;
        }

        var workingArea = Screen.FromRectangle(requestedBounds).WorkingArea;
        var width = Math.Min(Math.Max(MinimumSize.Width, bounds.Width), workingArea.Width);
        var height = Math.Min(Math.Max(MinimumSize.Height, bounds.Height), workingArea.Height);
        var restoredBounds = new Rectangle(
            Math.Clamp(bounds.X, workingArea.Left, workingArea.Right - width),
            Math.Clamp(bounds.Y, workingArea.Top, workingArea.Bottom - height),
            width,
            height);

        StartPosition = FormStartPosition.Manual;
        Bounds = restoredBounds;
        _hasKnownBounds = true;
    }

    internal bool HasKnownBounds => _hasKnownBounds || Visible;

    internal void ApplyCachedPage(string? pageKey)
    {
        if (Enum.TryParse<SettingsPage>(pageKey, ignoreCase: true, out var page))
        {
            SelectView(page);
        }
    }

    internal void SetPageDirty(SettingsPage page, bool dirty)
    {
        if (dirty)
        {
            _dirtyPages.Add(page);
        }
        else
        {
            _dirtyPages.Remove(page);
        }

        var navItem = _navItems.FirstOrDefault(item => item.Page == page).Button;
        if (navItem is not null)
        {
            navItem.IsDirty = dirty;
        }
    }

    private void AddNavGroup(FlowLayoutPanel nav, string text)
    {
        nav.Controls.Add(new Label
        {
            Text = text,
            AutoSize = false,
            Size = new Size(192, 30),
            ForeColor = UiTheme.Muted,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
            TextAlign = ContentAlignment.BottomLeft,
            Padding = new Padding(10, 0, 0, 4),
            Margin = new Padding(0, nav.Controls.Count == 0 ? 0 : 10, 0, 3)
        });
    }

    private void AddNavItem(FlowLayoutPanel nav, SettingsPage page, SettingsNavIcon icon, string text, Control view)
    {
        view.Dock = DockStyle.Fill;
        view.Visible = false;
        _contentHost.Controls.Add(view);

        var button = new SettingsNavButton(icon)
        {
            Text = text,
            AutoSize = false,
            Size = new Size(192, 44),
            Font = new Font(Font.FontFamily, 10F, FontStyle.Regular),
            Margin = new Padding(0, 0, 0, 3),
            Cursor = Cursors.Hand,
            TabStop = true,
            AccessibleName = text
        };

        button.Click += (_, _) => SelectView(page);
        _navItems.Add((button, view, page));
        nav.Controls.Add(button);
    }

    private void SelectView(SettingsPage page)
    {
        _selectedPage = page;
        foreach (var (button, view, itemPage) in _navItems)
        {
            var selected = itemPage == page;
            button.IsSelected = selected;
            view.Visible = selected;
            if (selected)
            {
                view.BringToFront();
            }
        }
    }

    private Control BuildAboutPanel()
    {
        var scrollHost = new WatermarkPanel(
            GetEmbeddedResourceName(AboutWatermarkResourcePath),
            AboutWatermarkSize,
            AboutWatermarkTopMargin,
            AboutWatermarkOpacity)
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            AutoScroll = true,
            Margin = new Padding(0)
        };

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var infoCard = new UiCardPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(14),
            Margin = new Padding(0, 0, 0, 12)
        };
        infoCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        infoCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var heading = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 16)
        };
        heading.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        heading.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        heading.Controls.Add(new Label
        {
            Text = "Shigure",
            AutoSize = true,
            ForeColor = UiTheme.Text,
            BackColor = Color.Transparent,
            Font = new Font(Font.FontFamily, 18F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4)
        }, 0, 0);
        heading.Controls.Add(new Label
        {
            Text = "世界上的大多数人想到荒坂公司时，脑中浮现的景象便是被众多企业、组织、权势雇佣的黑衣保安。",
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            BackColor = Color.Transparent,
            Font = new Font(Font.FontFamily, 9.5F, FontStyle.Regular),
            Margin = new Padding(0)
        }, 0, 1);
        infoCard.Controls.Add(heading, 0, 0);

        var assembly = Assembly.GetExecutingAssembly();
        var version = AppInfo.Version;
        var company = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;
        company = string.IsNullOrWhiteSpace(company) ? "Arasaka Corporation" : company;
        var details = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 0,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddAboutRow(details, "产品", "Shigure");
        AddAboutRow(details, "公司", company);
        AddAboutRow(details, "版本", version);
        AddAboutRow(details, "类型", "冲锋枪");
        AddAboutRow(details, "介绍", "它一分钟打出去的子弹比荒坂偷的税还要多。");
        AddAboutRow(details, "用途", "有时人们只想把子弹全打出去，在硝烟过后品味眼前的一片狼藉。");
        var modulePath = ModuleStore.ResolveModuleDirectory();
        var configPath = ConfigService.ResolveConfigPath(AppPaths.BaseDirectory);
        AddAboutRow(details, "模块目录", FormatAboutPath(modulePath), modulePath);
        AddAboutRow(details, "配置目录", FormatAboutPath(configPath), configPath);
        infoCard.Controls.Add(details, 0, 1);
        panel.Controls.Add(infoCard, 0, 0);

        panel.Controls.Add(new Label
        {
            Text = "可用状态字段",
            AutoSize = true,
            ForeColor = UiTheme.Text,
            BackColor = Color.Transparent,
            Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
            Margin = new Padding(2, 6, 0, 12)
        }, 0, 1);

        var fields = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 3,
            Margin = new Padding(0)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        fields.Controls.Add(CreateAboutFieldCard(
            "状态",
            [
                "有效性", "战斗时间", "移动", "生命值", "一键辅助", "插入法术",
                "队伍类型", "队伍人数", "首领战", "难度", "英雄天赋", "施法目标",
                "施法技能", "敌人数量", "敌人数-无仇恨", "敌人数-有仇恨", "施法",
                "引导", "蓄力", "蓄力层数", "酒池", "符文", "姿态",
                "救赎之魂1", "救赎之魂2", "神圣军备", "自律", "英勇打击",
                "收割者战刃"
            ],
            150), 0, 0);
        fields.Controls.Add(CreateAboutFieldCard(
            "能量",
            [
                "法力值", "怒气值", "集中值", "能量值", "符文", "符文能量",
                "星界能量", "漩涡值", "狂乱值", "恶魔之怒", "痛苦值",
                "连击点", "神圣能量", "精华能量", "灵魂碎片", "真气", "增压层数"
            ],
            150), 1, 0);
        fields.Controls.Add(CreateAboutFieldCard(
            "配置开关",
            ["爆发开关", "AOE开关", "输出模式", "爆发药水开关", "延迟"],
            92), 0, 1);
        fields.Controls.Add(CreateAboutFieldCard(
            "物品",
            ["治疗药水", "魔法药水", "治疗石", "鲁莽药水", "圣光潜力"],
            92), 1, 1);
        fields.Controls.Add(CreateAboutFieldCard(
            "目标",
            ["类型", "生命值", "距离", "施法", "施法可打断", "引导", "引导可打断"],
            104), 0, 2);
        fields.Controls.Add(CreateAboutFieldCard(
            "焦点",
            ["类型", "生命值", "距离", "施法", "施法可打断", "引导", "引导可打断"],
            104), 1, 2);

        panel.Controls.Add(fields, 0, 2);
        scrollHost.Controls.Add(panel);
        return scrollHost;
    }

    private static string GetEmbeddedResourceName(string resourcePath)
        => $"{typeof(StatusForm).Namespace}.{resourcePath}";

    private Control CreateAboutFieldCard(string title, IReadOnlyList<string> items, int minimumHeight)
    {
        var card = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(14),
            Margin = new Padding(0, 0, 12, 12),
            MinimumSize = new Size(0, minimumHeight)
        };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        card.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Accent,
            BackColor = Color.Transparent,
            Font = new Font(Font.FontFamily, 10.5F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        }, 0, 0);
        card.Controls.Add(new Label
        {
            Text = string.Join("  ·  ", items),
            Dock = DockStyle.Fill,
            AutoSize = false,
            ForeColor = UiTheme.Text,
            BackColor = Color.Transparent,
            Font = new Font(Font.FontFamily, 9.5F, FontStyle.Regular),
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Padding(0, 6, 0, 0)
        }, 0, 1);
        return card;
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
            // 路径位于 exe 目录之外时直接显示完整路径，避免冗长的 ..\..\ 相对路径文本。
            if (relativePath == ".."
                || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return fullPath;
            }

            return string.IsNullOrWhiteSpace(relativePath) ? "." : relativePath;
        }
        catch
        {
            return path;
        }
    }

    private void AddAboutRow(TableLayoutPanel panel, string name, string value, string? tooltip = null)
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
            BackColor = Color.Transparent,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 18, 14)
        }, 0, row);
        var valueLabel = new Label
        {
            Text = value,
            Dock = DockStyle.Fill,
            AutoSize = false,
            AutoEllipsis = true,
            ForeColor = UiTheme.Text,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 0, 14)
        };
        _toolTip.SetToolTip(valueLabel, tooltip ?? value);
        panel.Controls.Add(valueLabel, 1, row);
    }

    private sealed class WatermarkPanel : Panel
    {
        private readonly Bitmap? _watermark;
        private readonly int _watermarkSize;
        private readonly int _topMargin;
        private readonly float _opacity;

        public WatermarkPanel(string resourceName, int watermarkSize, int topMargin, float opacity)
        {
            _watermarkSize = watermarkSize;
            _topMargin = topMargin;
            _opacity = Math.Clamp(opacity, 0F, 1F);
            DoubleBuffered = true;
            ResizeRedraw = true;

            using var stream = typeof(StatusForm).Assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                return;
            }

            using var image = Image.FromStream(stream);
            _watermark = new Bitmap(image);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_watermark is null)
            {
                return;
            }

            var preferredLeft = ClientSize.Width / 2;
            var bounds = new Rectangle(
                Math.Min(preferredLeft, Math.Max(0, ClientSize.Width - _watermarkSize)),
                _topMargin,
                _watermarkSize,
                _watermarkSize);

            using var attributes = new ImageAttributes();
            var colorMatrix = new ColorMatrix
            {
                Matrix33 = _opacity
            };
            attributes.SetColorMatrix(
                colorMatrix,
                ColorMatrixFlag.Default,
                ColorAdjustType.Bitmap);

            e.Graphics.DrawImage(
                _watermark,
                bounds,
                0,
                0,
                _watermark.Width,
                _watermark.Height,
                GraphicsUnit.Pixel,
                attributes);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _watermark?.Dispose();
            }

            base.Dispose(disposing);
        }
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
        }

        if (_autoScrollLog)
        {
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
                if (key is "spells" or "auras" or "group"
                    || key.StartsWith('$'))
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
                items.Add(new ListViewItem(new[]
                {
                    index.ToString(),
                    key,
                    UiTheme.FormatValue(value)
                }));
            }
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

    private void ReplaceItems(ListView listView, IReadOnlyList<ListViewItem> items)
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
            UpdateListPresentation(listView, items);
            return;
        }

        if (CanUpdateInPlace(listView, items))
        {
            UpdateItemsInPlace(listView, items);
            UpdateListPresentation(listView, items);
            return;
        }

        listView.BeginUpdate();
        listView.Items.Clear();
        listView.Items.AddRange(items.ToArray());
        listView.EndUpdate();
        UpdateListPresentation(listView, items);
    }

    private void UpdateListPresentation(ListView listView, IReadOnlyList<ListViewItem> items)
    {
        var isPlaceholder = items.Count == 1
            && items[0].SubItems.Count > 0
            && items[0].SubItems[0].Text is "-" or "队伍" or "逻辑信息";
        if (_listCounts.TryGetValue(listView, out var countLabel))
        {
            countLabel.Text = $"{(isPlaceholder ? 0 : items.Count)} 项";
        }

        UiTheme.FitListViewColumns(listView);
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

    private sealed class SettingsNavButton : Button
    {
        private readonly SettingsNavIcon _icon;
        private bool _hovered;
        private bool _pressed;
        private bool _isSelected;
        private bool _isDirty;

        public SettingsNavButton(SettingsNavIcon icon)
        {
            _icon = icon;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = UiTheme.Background;
            ForeColor = UiTheme.Muted;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
                true);
        }

        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                Invalidate();
            }
        }

        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (_isDirty == value)
                {
                    return;
                }

                _isDirty = value;
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
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            base.OnMouseDown(mevent);
            if (mevent.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            base.OnMouseUp(mevent);
            _pressed = false;
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var graphics = pevent.Graphics;
            graphics.Clear(UiTheme.Background);
            var scale = Math.Max(1f, DeviceDpi / 96f);
            var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            var oldSmoothingMode = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var backgroundColor = _isSelected
                ? UiTheme.AccentSoft
                : _pressed
                    ? UiTheme.Pressed
                    : _hovered
                        ? UiTheme.Hover
                        : UiTheme.Background;
            using (var path = UiTheme.CreateRoundedRectanglePath(bounds, Math.Max(6, (int)Math.Round(8 * scale))))
            using (var background = new SolidBrush(backgroundColor))
            {
                graphics.FillPath(background, path);
                if (_isSelected)
                {
                    using var border = new Pen(Color.FromArgb(90, UiTheme.Accent));
                    graphics.DrawPath(border, path);
                }
            }

            var iconSize = Math.Max(18, (int)Math.Round(20 * scale));
            var iconBounds = new Rectangle(
                (int)Math.Round(12 * scale),
                (Height - iconSize) / 2,
                iconSize,
                iconSize);
            var iconColor = _isSelected ? UiTheme.Accent : _hovered ? UiTheme.Text : UiTheme.Muted;
            DrawIcon(graphics, _icon, iconBounds, iconColor, scale);
            graphics.SmoothingMode = oldSmoothingMode;

            var textLeft = iconBounds.Right + (int)Math.Round(12 * scale);
            var dirtySpace = _isDirty ? (int)Math.Round(24 * scale) : (int)Math.Round(10 * scale);
            var textBounds = new Rectangle(
                textLeft,
                0,
                Math.Max(0, Width - textLeft - dirtySpace),
                Height);
            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                textBounds,
                _isSelected || _hovered ? UiTheme.Text : UiTheme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (_isDirty)
            {
                var dotSize = Math.Max(6, (int)Math.Round(7 * scale));
                using var warning = new SolidBrush(UiTheme.Warning);
                graphics.FillEllipse(
                    warning,
                    Width - (int)Math.Round(16 * scale) - dotSize,
                    (Height - dotSize) / 2,
                    dotSize,
                    dotSize);
            }

            if (Focused && ShowFocusCues)
            {
                var focusBounds = Rectangle.Inflate(bounds, -(int)Math.Round(4 * scale), -(int)Math.Round(4 * scale));
                ControlPaint.DrawFocusRectangle(graphics, focusBounds, UiTheme.Text, backgroundColor);
            }
        }

        private static void DrawIcon(Graphics graphics, SettingsNavIcon icon, Rectangle bounds, Color color, float scale)
        {
            var x = bounds.Left;
            var y = bounds.Top;
            var w = bounds.Width;
            var h = bounds.Height;
            using var pen = new Pen(color, Math.Max(1.4f, 1.55f * scale))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            using var brush = new SolidBrush(color);

            switch (icon)
            {
                case SettingsNavIcon.General:
                    graphics.DrawEllipse(pen, x + w * 0.22f, y + h * 0.22f, w * 0.56f, h * 0.56f);
                    graphics.DrawEllipse(pen, x + w * 0.42f, y + h * 0.42f, w * 0.16f, h * 0.16f);
                    for (var i = 0; i < 8; i++)
                    {
                        var angle = i * Math.PI / 4;
                        var cx = x + w / 2f;
                        var cy = y + h / 2f;
                        graphics.DrawLine(
                            pen,
                            cx + (float)Math.Cos(angle) * w * 0.32f,
                            cy + (float)Math.Sin(angle) * h * 0.32f,
                            cx + (float)Math.Cos(angle) * w * 0.43f,
                            cy + (float)Math.Sin(angle) * h * 0.43f);
                    }

                    break;
                case SettingsNavIcon.Config:
                    DrawSlider(graphics, pen, brush, x, y + h * 0.28f, w, 0.34f);
                    DrawSlider(graphics, pen, brush, x, y + h * 0.50f, w, 0.68f);
                    DrawSlider(graphics, pen, brush, x, y + h * 0.72f, w, 0.46f);
                    break;
                case SettingsNavIcon.Macros:
                    graphics.DrawLines(pen, [
                        new PointF(x + w * 0.34f, y + h * 0.22f),
                        new PointF(x + w * 0.12f, y + h * 0.50f),
                        new PointF(x + w * 0.34f, y + h * 0.78f)]);
                    graphics.DrawLines(pen, [
                        new PointF(x + w * 0.66f, y + h * 0.22f),
                        new PointF(x + w * 0.88f, y + h * 0.50f),
                        new PointF(x + w * 0.66f, y + h * 0.78f)]);
                    graphics.DrawLine(pen, x + w * 0.57f, y + h * 0.18f, x + w * 0.43f, y + h * 0.82f);
                    break;
                case SettingsNavIcon.Modules:
                    for (var row = 0; row < 2; row++)
                    {
                        for (var column = 0; column < 2; column++)
                        {
                            graphics.DrawRectangle(
                                pen,
                                x + w * (0.12f + column * 0.46f),
                                y + h * (0.12f + row * 0.46f),
                                w * 0.30f,
                                h * 0.30f);
                        }
                    }

                    break;
                case SettingsNavIcon.Status:
                    graphics.DrawLines(pen, [
                        new PointF(x + w * 0.08f, y + h * 0.55f),
                        new PointF(x + w * 0.28f, y + h * 0.55f),
                        new PointF(x + w * 0.40f, y + h * 0.24f),
                        new PointF(x + w * 0.56f, y + h * 0.78f),
                        new PointF(x + w * 0.69f, y + h * 0.45f),
                        new PointF(x + w * 0.92f, y + h * 0.45f)]);
                    break;
                case SettingsNavIcon.Party:
                    graphics.DrawEllipse(pen, x + w * 0.36f, y + h * 0.10f, w * 0.28f, h * 0.28f);
                    graphics.DrawArc(pen, x + w * 0.20f, y + h * 0.40f, w * 0.60f, h * 0.50f, 190, 160);
                    graphics.DrawEllipse(pen, x + w * 0.08f, y + h * 0.28f, w * 0.20f, h * 0.20f);
                    graphics.DrawEllipse(pen, x + w * 0.72f, y + h * 0.28f, w * 0.20f, h * 0.20f);
                    break;
                case SettingsNavIcon.Logic:
                    graphics.DrawLine(pen, x + w * 0.28f, y + h * 0.30f, x + w * 0.70f, y + h * 0.20f);
                    graphics.DrawLine(pen, x + w * 0.28f, y + h * 0.36f, x + w * 0.70f, y + h * 0.72f);
                    graphics.FillEllipse(brush, x + w * 0.14f, y + h * 0.24f, w * 0.22f, h * 0.22f);
                    graphics.FillEllipse(brush, x + w * 0.64f, y + h * 0.10f, w * 0.22f, h * 0.22f);
                    graphics.FillEllipse(brush, x + w * 0.64f, y + h * 0.64f, w * 0.22f, h * 0.22f);
                    break;
                case SettingsNavIcon.Logs:
                    graphics.DrawRectangle(pen, x + w * 0.18f, y + h * 0.10f, w * 0.64f, h * 0.80f);
                    graphics.DrawLine(pen, x + w * 0.32f, y + h * 0.34f, x + w * 0.68f, y + h * 0.34f);
                    graphics.DrawLine(pen, x + w * 0.32f, y + h * 0.52f, x + w * 0.68f, y + h * 0.52f);
                    graphics.DrawLine(pen, x + w * 0.32f, y + h * 0.70f, x + w * 0.58f, y + h * 0.70f);
                    break;
                case SettingsNavIcon.About:
                    graphics.DrawEllipse(pen, x + w * 0.12f, y + h * 0.12f, w * 0.76f, h * 0.76f);
                    graphics.FillEllipse(brush, x + w * 0.46f, y + h * 0.28f, w * 0.08f, h * 0.08f);
                    graphics.DrawLine(pen, x + w * 0.50f, y + h * 0.47f, x + w * 0.50f, y + h * 0.70f);
                    break;
            }
        }

        private static void DrawSlider(Graphics graphics, Pen pen, Brush brush, float x, float y, float width, float knobPosition)
        {
            graphics.DrawLine(pen, x + width * 0.10f, y, x + width * 0.90f, y);
            var knobSize = width * 0.14f;
            graphics.FillEllipse(brush, x + width * knobPosition - knobSize / 2, y - knobSize / 2, knobSize, knobSize);
        }
    }
}
