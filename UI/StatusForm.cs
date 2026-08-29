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
    BossNumbers,
    CommonFields,
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
    BossNumbers,
    CommonFields,
    About
}

public sealed class StatusForm : Form
{
    private const string AboutWatermarkResourcePath = "Assets.arasaka-icon-transparent.png";
    private const int AboutWatermarkSize = 440;
    private const int AboutWatermarkTopMargin = 16;
    private const float AboutWatermarkOpacity = 0.08F;
    private const int BossNumberCardWidth = 400;

    private static readonly IReadOnlyList<string> CurrentSeasonDungeonNames =
    [
        "烈毒之渊",
        "潮缚石窟",
        "毒牙祭坛",
        "纳洛拉克的洞穴",
        "密谋小径",
        "夺目谷",
        "诸王之眠",
        "红玉新生法池",
        "虚空之痕竞技场",
        "塞塔里斯神庙"
    ];

    private static readonly IReadOnlyList<BossNumberGroup> BossNumberGroups =
    [
        new(string.Empty,
        [
            new("虚影尖塔",
            [
                new(1, "元首阿福扎恩", 1),
                new(2, "弗拉希乌斯", 2),
                new(3, "陨落之王萨哈达尔", 3),
                new(4, "威厄高尔和艾佐拉克", 4),
                new(5, "光盲先锋军", 5),
                new(6, "宇宙之冕", 6)
            ]),
            new("梦境裂隙", [new(1, "奇美鲁斯，未梦之神", 7)]),
            new("奎尔丹纳斯岛",
            [
                new(1, "贝洛朗，奥的子嗣", 8),
                new(2, "至暗之夜降临", 9)
            ]),
            new("世界",
            [
                new(1, "鲁阿夏尔", 10),
                new(2, "索姆贝兰", 11),
                new(3, "普雷达萨斯", 12),
                new(4, "克拉格平", 13)
            ]),
            new("孢陨幽境", [new(1, "腐沼", 14)]),
            new("潮缚石窟", [new(1, "尼姆瑞莎·唤波者", 15)]),
            new("烈毒之渊",
            [
                new(1, "盘魂者内克扎莉", 16),
                new(2, "陵寝哨兵", 17),
                new(3, "迷失的探险者", 18),
                new(4, "万毒邪祟者瓦什尼克", 19),
                new(5, "斯索拉克", 20),
                new(6, "双子毒牙", 21),
                new(7, "盘卷祭坛", 22),
                new(8, "乌拉特克", 23)
            ])
        ]),
        new("大米",
        [
            new("节点希纳斯",
            [
                new(1, "核技工程长卡斯雷瑟", 51),
                new(2, "核心守卫奈萨拉", 52),
                new(3, "洛萨克森", 53)
            ]),
            new("迈萨拉洞窟",
            [
                new(1, "姆罗金和内克拉克斯", 54),
                new(2, "沃达扎", 55),
                new(3, "拉克图尔，聚魂之器", 56)
            ]),
            new("风行者之塔",
            [
                new(1, "烬晓", 57),
                new(2, "被遗弃的二人组", 58),
                new(3, "指挥官克罗鲁科", 59),
                new(4, "无眠之心", 60)
            ]),
            new("魔导师平台",
            [
                new(1, "奥能金刚库斯托斯", 61),
                new(2, "瑟拉奈尔·日鞭", 62),
                new(3, "吉美尔鲁斯", 63),
                new(4, "迪詹崔乌斯", 64)
            ]),
            new("执政团之座",
            [
                new(1, "晋升者祖拉尔", 65),
                new(2, "萨普瑞什", 66),
                new(3, "总督奈扎尔", 67),
                new(4, "鲁拉", 68)
            ]),
            new("艾杰斯亚学院",
            [
                new(1, "维克萨姆斯", 69),
                new(2, "茂林古树", 70),
                new(3, "克罗兹", 71),
                new(4, "多拉苟萨的回响", 72)
            ]),
            new("萨隆矿坑",
            [
                new(1, "熔炉之主加弗斯特", 73),
                new(2, "伊克和科瑞克", 74),
                new(3, "天灾领主泰兰努斯", 75)
            ]),
            new("通天峰",
            [
                new(1, "兰吉特", 76),
                new(2, "阿拉卡纳斯", 77),
                new(3, "鲁克兰", 78),
                new(4, "高阶贤者维里克斯", 79)
            ]),
            new("毒牙祭坛",
            [
                new(1, "拉维", 80),
                new(2, "扭缠盘蛇", 81),
                new(3, "祖尔加", 82)
            ]),
            new("纳洛拉克的洞穴",
            [
                new(1, "囤宝狂人", 83),
                new(2, "寒冬哨兵", 84),
                new(3, "纳洛拉克", 85)
            ]),
            new("密谋小径",
            [
                new(1, "凯斯媞亚·魔力之心", 86),
                new(2, "赞恩·刃悲", 87),
                new(3, "歼灭者萨祖克斯", 88),
                new(4, "利希尔·烬怒", 89)
            ]),
            new("夺目谷",
            [
                new(1, "光明众花", 90),
                new(2, "圣光猎手伊库兹", 91),
                new(3, "护光者鲁伊亚", 92),
                new(4, "兹欧凯特", 93)
            ]),
            new("诸王之眠",
            [
                new(1, "黄金风蛇", 94),
                new(2, "部族议会", 95),
                new(3, "殓尸者姆沁巴", 96),
                new(4, "达萨大王", 97)
            ]),
            new("红玉新生法池",
            [
                new(1, "梅莉杜莎·寒妆", 98),
                new(2, "柯姬雅·焰蹄", 99),
                new(3, "基拉卡与厄克哈特·风脉", 100)
            ]),
            new("虚空之痕竞技场",
            [
                new(1, "塔兹拉尔", 101),
                new(2, "阿特洛苏斯", 102),
                new(3, "煞戎努斯", 103)
            ]),
            new("塞塔里斯神庙",
            [
                new(1, "阿德里斯和阿斯匹克斯", 104),
                new(2, "米利克萨", 105),
                new(3, "加瓦兹特", 106),
                new(4, "塞塔里斯的化身", 107)
            ])
        ])
    ];

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
        ShowInTaskbar = true;
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

        _stateList = UiTheme.CreateListView(Font, "status-state-v2",
            new UiTheme.ListColumn("#", 28, 28, FixedWidth: true),
            new UiTheme.ListColumn("名称", 40, 220),
            new UiTheme.ListColumn("值", 40, 900, FillRemaining: true));
        _auraList = UiTheme.CreateListView(Font, "status-aura-v2",
            new UiTheme.ListColumn("#", 28, 28, FixedWidth: true),
            new UiTheme.ListColumn("光环", 40, 260),
            new UiTheme.ListColumn("值", 40, 900, FillRemaining: true));
        _dynamicUnitList = UiTheme.CreateListView(Font, "status-dynamic-unit-v2",
            new UiTheme.ListColumn("类型", 40, 140),
            new UiTheme.ListColumn("名称", 40, 240),
            new UiTheme.ListColumn("值", 40, 900, FillRemaining: true));
        _spellList = UiTheme.CreateListView(Font, "status-spell-v2",
            new UiTheme.ListColumn("#", 28, 28, FixedWidth: true),
            new UiTheme.ListColumn("技能", 40, 260),
            new UiTheme.ListColumn("状态", 40, 900, FillRemaining: true));

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
            Padding = new Padding(16),
            Margin = new Padding(0)
        };

        AddNavGroup(nav, "常用");
        AddNavItem(nav, SettingsPage.General, SettingsNavIcon.General, "通用", CreatePageShell("通用", "运行控制、配置同步、数据包与模块选择", _settingsHost));
        AddNavGroup(nav, "编辑");
        AddNavItem(nav, SettingsPage.Config, SettingsNavIcon.Config, "配置", CreatePageShell("配置", "编辑职业、专精和扫描字段", _configHost));
        AddNavItem(nav, SettingsPage.Macros, SettingsNavIcon.Macros, "宏", CreatePageShell("宏", "维护职业动态宏、静态宏与特殊宏", _macrosHost));
        AddNavItem(nav, SettingsPage.Modules, SettingsNavIcon.Modules, "模块", CreatePageShell("模块", "创建、匹配并维护运行模块", _moduleHost));
        AddNavGroup(nav, "监控");
        AddNavItem(nav, SettingsPage.Status, SettingsNavIcon.Status, "状态", CreatePageShell("状态", string.Empty, BuildStatusPage()));
        AddNavItem(nav, SettingsPage.Party, SettingsNavIcon.Party, "队伍", CreatePageShell("队伍", "当前队伍单位与扫描字段摘要", BuildSection("队伍成员", _partyList, "实时队伍数据")));
        AddNavItem(nav, SettingsPage.Logic, SettingsNavIcon.Logic, "逻辑", CreatePageShell("逻辑", "运行时推荐目标与调试值", BuildSection("逻辑信息", _unitInfoList, "当前模块的决策输出")));
        AddNavItem(nav, SettingsPage.Logs, SettingsNavIcon.Logs, "日志", CreatePageShell("日志", "运行、模块匹配与施放记录", BuildLogPage()));
        AddNavGroup(nav, "说明");
        AddNavItem(nav, SettingsPage.BossNumbers, SettingsNavIcon.BossNumbers, "首领", CreatePageShell("首领编号", "副本首领的序号、名称与扫描编号", BuildBossNumbersPage()));
        AddNavItem(nav, SettingsPage.CommonFields, SettingsNavIcon.CommonFields, "字段", CreatePageShell("常用字段", "模块条件可用的状态字段参考", BuildCommonFieldsPanel()));
        AddNavGroup(nav, "系统");
        AddNavItem(nav, SettingsPage.About, SettingsNavIcon.About, "关于", CreatePageShell("关于", "应用信息", _aboutHost));
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
            Padding = new Padding(12, 10, 12, 8),
            Margin = new Padding(0)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));

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
        // 所有页面统一使用单行页头，标题与说明文字保持各自原有字号。
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = hasSubtitle ? 2 : 1,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, UiTheme.PageGap)
        };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        if (hasSubtitle)
        {
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        }

        header.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            AutoSize = true,
            ForeColor = UiTheme.Text,
            Font = new Font(Font.FontFamily, 16F, FontStyle.Bold),
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(0, 0, hasSubtitle ? 14 : 0, 0)
        }, 0, 0);
        if (hasSubtitle)
        {
            header.Controls.Add(new Label
            {
                Text = subtitle,
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                ForeColor = UiTheme.Muted,
                Font = new Font(Font.FontFamily, 9.5F, FontStyle.Regular),
                TextAlign = ContentAlignment.BottomLeft,
                Margin = new Padding(0)
            }, 1, 0);
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

        var sections = new[]
        {
            BuildSection("状态", _stateList, "基础字段与当前模块"),
            BuildSection("光环", _auraList, "光环数值状态"),
            BuildSection("技能", _spellList, "冷却与可用状态"),
            BuildSection("动态单位", _dynamicUnitList, "模块运行时计算值")
        };
        bool? usingCompactLayout = null;

        void ApplyLayout()
        {
            // 四张卡片需要为三列表头保留可读宽度；不足时切成 2×2，避免原生横向滚动条。
            // ClientSize 已是当前 WinForms 布局坐标，不再二次按 DPI 放大断点。
            var compact = statusSplit.ClientSize.Width < 1100;
            if (usingCompactLayout == compact)
            {
                return;
            }

            usingCompactLayout = compact;
            statusSplit.SuspendLayout();
            statusSplit.Controls.Clear();
            statusSplit.ColumnStyles.Clear();
            statusSplit.RowStyles.Clear();
            statusSplit.ColumnCount = compact ? 2 : 4;
            statusSplit.RowCount = compact ? 2 : 1;
            for (var column = 0; column < statusSplit.ColumnCount; column++)
            {
                statusSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / statusSplit.ColumnCount));
            }

            for (var row = 0; row < statusSplit.RowCount; row++)
            {
                statusSplit.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / statusSplit.RowCount));
            }

            for (var i = 0; i < sections.Length; i++)
            {
                var column = compact ? i % 2 : i;
                var row = compact ? i / 2 : 0;
                sections[i].Margin = new Padding(
                    column == 0 ? 0 : UiTheme.PageGap / 2,
                    row == 0 ? 0 : UiTheme.PageGap / 2,
                    column == statusSplit.ColumnCount - 1 ? 0 : UiTheme.PageGap / 2,
                    row == statusSplit.RowCount - 1 ? 0 : UiTheme.PageGap / 2);
                statusSplit.Controls.Add(sections[i], column, row);
            }

            statusSplit.ResumeLayout(true);
        }

        statusSplit.SizeChanged += (_, _) => ApplyLayout();
        statusSplit.HandleCreated += (_, _) => BeginInvoke(ApplyLayout);
        ApplyLayout();
        return statusSplit;
    }

    private TableLayoutPanel BuildSection(string title, Control content, string subtitle)
    {
        var section = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(UiTheme.CardPadding),
            Margin = new Padding(0)
        };
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
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
            UiTheme.ApplyControlRoundedRegion(countLabel, UiTheme.ControlCornerRadius);
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
        content.Margin = new Padding(0, 8, 0, 0);
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
            Padding = new Padding(UiTheme.CardPadding),
            Margin = new Padding(0)
        };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
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
        UiTheme.StyleActionButton(copyButton, 112);
        copyButton.Margin = new Padding(0, 0, 8, 6);
        copyButton.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_logTextBox.Text))
            {
                Clipboard.SetText(_logTextBox.Text);
            }
        };
        var clearButton = UiTheme.CreateButton("清空显示", UiTheme.ButtonKind.Danger);
        UiTheme.StyleActionButton(clearButton, 112);
        clearButton.Margin = new Padding(0, 0, 16, 6);
        clearButton.Click += (_, _) => _logTextBox.Clear();
        var autoScroll = new CheckBox
        {
            Text = "自动滚动",
            Checked = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        UiTheme.StyleCheckBox(autoScroll, UiTheme.SurfaceRaised);
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
            Size = new Size(192, 25),
            ForeColor = UiTheme.Muted,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0),
            Margin = new Padding(0, nav.Controls.Count == 0 ? 0 : 6, 0, 2)
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
            Size = new Size(192, 39),
            Font = new Font(Font.FontFamily, 10F, FontStyle.Regular),
            Margin = new Padding(0, 0, 0, 1),
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

    private Control BuildBossNumbersPage()
    {
        var scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = UiTheme.Surface,
            Margin = new Padding(0)
        };

        var cards = new TableLayoutPanel
        {
            Dock = DockStyle.None,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = UiTheme.Surface,
            ColumnCount = 4,
            RowCount = 0,
            MinimumSize = new Size(BossNumberCardWidth * 4 + UiTheme.PageGap * 3, 0),
            Padding = new Padding(0),
            Margin = new Padding(0)
        };

        var allDungeons = BossNumberGroups
            .SelectMany(group => group.Dungeons)
            .ToDictionary(dungeon => dungeon.Name, StringComparer.Ordinal);
        var currentSeasonNames = CurrentSeasonDungeonNames.ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<BossNumberGroup> seasonGroups =
        [
            new("当前赛季", CurrentSeasonDungeonNames.Select(name => allDungeons[name]).ToArray()),
            new("第一赛季", BossNumberGroups
                .SelectMany(group => group.Dungeons)
                .Where(dungeon => !currentSeasonNames.Contains(dungeon.Name))
                .ToArray())
        ];

        var seasonViews = seasonGroups
            .Select(group =>
            {
                var title = new Label
                {
                    Text = group.Title,
                    AutoSize = true,
                    ForeColor = UiTheme.Text,
                    BackColor = Color.Transparent,
                    Font = new Font(Font.FontFamily, 12F, FontStyle.Bold)
                };
                var groupCards = group.Dungeons.Select(CreateBossNumberCard).ToArray();
                foreach (var card in groupCards)
                {
                    card.Margin = new Padding(0, 0, 0, UiTheme.PageGap);
                }

                return (Title: title, Cards: groupCards);
            })
            .ToArray();

        void ApplyLayout()
        {
            const int columnCount = 4;
            cards.SuspendLayout();
            cards.Controls.Clear();
            cards.ColumnStyles.Clear();
            cards.RowStyles.Clear();
            cards.ColumnCount = columnCount;
            cards.RowCount = 0;
            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                var columnWidth = BossNumberCardWidth
                    + (columnIndex < columnCount - 1 ? UiTheme.PageGap : 0);
                cards.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, columnWidth));
            }

            var row = 0;
            var column = 0;
            foreach (var (title, groupCards) in seasonViews)
            {
                if (column != 0)
                {
                    row++;
                    column = 0;
                }

                cards.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                cards.RowCount = row + 1;
                title.Margin = new Padding(2, row == 0 ? 0 : UiTheme.PageGap, 0, UiTheme.PageGap);
                cards.Controls.Add(title, 0, row);
                cards.SetColumnSpan(title, columnCount);
                row++;

                foreach (var card in groupCards)
                {
                    if (cards.RowStyles.Count <= row)
                    {
                        cards.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                        cards.RowCount = row + 1;
                    }

                    cards.Controls.Add(card, column, row);

                    column++;
                    if (column == columnCount)
                    {
                        column = 0;
                        row++;
                    }
                }
            }

            cards.ResumeLayout(true);
        }

        scrollHost.Controls.Add(cards);
        ApplyLayout();
        return scrollHost;
    }

    private Control CreateBossNumberCard(BossDungeon dungeon)
    {
        var card = new UiCardPanel
        {
            AutoSize = false,
            Size = new Size(
                BossNumberCardWidth,
                UiTheme.CardPadding * 2 + 34 + (dungeon.Bosses.Count + 1) * 28),
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(UiTheme.CardPadding),
            MinimumSize = new Size(0, 0)
        };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.Controls.Add(new Label
        {
            Text = dungeon.Name,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Accent,
            BackColor = Color.Transparent,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        }, 0, 0);

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            ColumnCount = 3,
            RowCount = dungeon.Bosses.Count + 1,
            Margin = new Padding(0)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        AddBossNumberCell(table, "序号", 0, 0, UiTheme.Muted, FontStyle.Bold, ContentAlignment.MiddleCenter);
        AddBossNumberCell(table, "名称", 1, 0, UiTheme.Muted, FontStyle.Bold, ContentAlignment.MiddleLeft);
        AddBossNumberCell(table, "编号", 2, 0, UiTheme.Muted, FontStyle.Bold, ContentAlignment.MiddleCenter);

        for (var index = 0; index < dungeon.Bosses.Count; index++)
        {
            var boss = dungeon.Bosses[index];
            var tableRow = index + 1;
            AddBossNumberCell(table, boss.Sequence.ToString(), 0, tableRow, UiTheme.Muted, FontStyle.Regular, ContentAlignment.MiddleCenter);
            AddBossNumberCell(table, boss.Name, 1, tableRow, UiTheme.Text, FontStyle.Regular, ContentAlignment.MiddleLeft);
            AddBossNumberCell(table, boss.Number.ToString(), 2, tableRow, UiTheme.Accent, FontStyle.Regular, ContentAlignment.MiddleCenter);
        }

        card.Controls.Add(table, 0, 1);
        return card;
    }

    private void AddBossNumberCell(
        TableLayoutPanel table,
        string text,
        int column,
        int row,
        Color color,
        FontStyle style,
        ContentAlignment alignment)
    {
        table.Controls.Add(new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 28,
            AutoEllipsis = true,
            ForeColor = color,
            BackColor = Color.Transparent,
            Font = new Font(Font.FontFamily, 9F, style),
            TextAlign = alignment,
            Margin = new Padding(column == 1 ? 6 : 0, 0, column == 1 ? 6 : 0, 0)
        }, column, row);
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
            RowCount = 1,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var infoCard = new UiCardPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(UiTheme.CardPadding),
            Margin = new Padding(0)
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
            Margin = new Padding(0, 0, 0, UiTheme.PageGap)
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

        scrollHost.Controls.Add(panel);
        return scrollHost;
    }

    private Control BuildCommonFieldsPanel()
    {
        var scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            AutoScroll = true,
            Margin = new Padding(0)
        };

        var fields = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            BackColor = UiTheme.Surface,
            ColumnCount = 2,
            RowCount = 4,
            Margin = new Padding(0)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        fields.Controls.Add(CreateCommonFieldCard(
            "状态",
            [
                "有效性", "战斗时间", "移动", "生命值", "一键辅助", "插入法术",
                "队伍类型", "队伍人数", "首领战", "难度", "英雄天赋", "施法目标",
                "施法技能", "敌人数量", "敌人数-无仇恨", "敌人数-有仇恨",
                "施法(正计时)", "施法(倒计时)", "引导", "蓄力", "蓄力层数",
                "酒池", "符文", "姿态", "神圣军备", "自律", "英勇打击", "吸血鬼打击", "收割者战刃"
            ],
            150), 0, 0);
        fields.Controls.Add(CreateCommonFieldCard(
            "能量",
            [
                "法力值", "怒气值", "集中值", "能量值", "符文", "符文能量",
                "星界能量", "漩涡值", "狂乱值", "恶魔之怒", "痛苦值",
                "连击点", "神圣能量", "精华能量", "灵魂碎片", "真气", "增压层数"
            ],
            150), 1, 0);
        fields.Controls.Add(CreateCommonFieldCard(
            "配置开关",
            ["爆发开关", "AOE开关", "输出模式", "爆发药水开关", "延迟"],
            92), 0, 1);
        fields.Controls.Add(CreateCommonFieldCard(
            "物品",
            ["治疗药水", "魔法药水", "治疗石", "鲁莽药水", "圣光潜力"],
            92), 1, 1);
        fields.Controls.Add(CreateCommonFieldCard(
            "目标",
            ["类型", "生命值", "距离", "施法(倒计时)", "施法(正计时)", "施法可打断", "引导", "引导可打断"],
            104), 0, 2);
        fields.Controls.Add(CreateCommonFieldCard(
            "焦点",
            ["类型", "生命值", "距离", "施法(倒计时)", "施法(正计时)", "施法可打断", "引导", "引导可打断"],
            104), 1, 2);
        fields.Controls.Add(CreateCommonFieldCard(
            "鼠标",
            ["类型", "生命值", "距离", "施法(倒计时)", "施法(正计时)", "施法可打断", "引导", "引导可打断"],
            104), 0, 3);
        fields.Controls.Add(CreateCommonFieldCard(
            "宠物",
            ["存在", "生命值"],
            104), 1, 3);

        scrollHost.Controls.Add(fields);
        return scrollHost;
    }

    private static string GetEmbeddedResourceName(string resourcePath)
        => $"{typeof(StatusForm).Namespace}.{resourcePath}";

    private Control CreateCommonFieldCard(string title, IReadOnlyList<string> items, int minimumHeight)
    {
        var card = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(UiTheme.CardPadding),
            Margin = new Padding(0, 0, UiTheme.PageGap, UiTheme.PageGap),
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
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
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
            Font = new Font(Font.FontFamily, 9F, FontStyle.Regular),
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
            Margin = new Padding(0, 0, 18, 8)
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
            Margin = new Padding(0, 0, 0, 8)
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
                case SettingsNavIcon.BossNumbers:
                    graphics.DrawRectangle(pen, x + w * 0.12f, y + h * 0.12f, w * 0.76f, h * 0.76f);
                    graphics.DrawLine(pen, x + w * 0.38f, y + h * 0.27f, x + w * 0.31f, y + h * 0.73f);
                    graphics.DrawLine(pen, x + w * 0.65f, y + h * 0.27f, x + w * 0.58f, y + h * 0.73f);
                    graphics.DrawLine(pen, x + w * 0.25f, y + h * 0.43f, x + w * 0.72f, y + h * 0.43f);
                    graphics.DrawLine(pen, x + w * 0.22f, y + h * 0.59f, x + w * 0.69f, y + h * 0.59f);
                    break;
                case SettingsNavIcon.CommonFields:
                    graphics.DrawRectangle(pen, x + w * 0.12f, y + h * 0.12f, w * 0.76f, h * 0.76f);
                    graphics.DrawLine(pen, x + w * 0.34f, y + h * 0.12f, x + w * 0.34f, y + h * 0.88f);
                    graphics.DrawLine(pen, x + w * 0.12f, y + h * 0.38f, x + w * 0.88f, y + h * 0.38f);
                    graphics.DrawLine(pen, x + w * 0.12f, y + h * 0.63f, x + w * 0.88f, y + h * 0.63f);
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

    private sealed record BossNumberGroup(string Title, IReadOnlyList<BossDungeon> Dungeons);

    private sealed record BossDungeon(string Name, IReadOnlyList<BossNumberEntry> Bosses);

    private sealed record BossNumberEntry(int Sequence, string Name, int Number);
}
