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
    Nameplates,
    Logic,
    Logs,
    BossNumbers,
    Event,
    BigWigsEvent,
    CommonFields,
    About
}

internal sealed record BossNumberOption(int Number, string Dungeon, string Name);
internal sealed record ExBossEventFilterOption(string Display, string? Value)
{
    public override string ToString() => Display;
}

internal sealed record StateFieldDisplay(string Name, string SpellId, string Type, long IconId = 0, bool IsItem = false);
internal sealed record StatusListIcon(long Id, bool IsItem);

public sealed class StatusForm : Form
{
    private const string AboutLogoResourcePath = "Assets.arasaka-icon-transparent.png";
    private const int SettingsContentWidth = 1200;
    private const int AboutLogoSize = 220;
    private const float AboutLogoOpacity = 0.55F;
    private const int AboutScaleIconSize = 18;
    /// <summary>单行顶栏高度（图标 + 窗口按钮）。</summary>
    private const int TopBarHeight = 44;
    /// <summary>标题栏左侧独立品牌图标边长。</summary>
    private const int NavBrandIconSize = 32;
    /// <summary>Windows 风格标题栏按钮宽。</summary>
    private const int ChromeButtonWidth = 46;
    /// <summary>设置左栏宽度。</summary>
    private const int SettingsSidebarWidth = 260;
    /// <summary>左栏可点页项高度。</summary>
    private const int SettingsNavItemHeight = 30;
    /// <summary>左栏不可点分组标题高度。</summary>
    private const int SettingsNavGroupHeight = 34;

    private const string AboutDisclaimerText =
        """
        1. 合规责任

        Shigure 仅供技术研究、学习交流和个人实验使用。下载、安装、复制、修改、分发或使用本软件前，用户应自行确认相关行为符合所在地法律法规，以及目标软件、游戏平台或服务提供商的用户协议、服务条款和社区规则。

        2. 账号与处罚风险

        本软件可能涉及窗口状态读取、按键发送或自动化辅助流程。此类行为可能被游戏运营商、反作弊系统或相关服务提供商认定为违规，并导致账号限制、角色封禁、数据丢失、收益回收或其他处罚。用户应充分了解并自行承担全部风险，开发者不对由此产生的任何后果负责。

        3. 无担保声明

        本项目基于 MIT License 开源发布。软件按“原样”（AS IS）提供，不对稳定性、准确性、完整性、安全性、兼容性、持续可用性或特定用途适用性作出任何明示或暗示保证。因使用或无法使用本软件导致的直接、间接、偶然、特殊或后续损失，均由用户自行承担。

        4. 商业使用与衍生版本

        MIT License 允许在遵守许可证条件的前提下复制、修改、分发和商业使用本软件。任何第三方对本软件或其衍生版本的运营、销售、推广、技术支持或其他商业利用，均由该第三方独立负责。开发者不代表、不授权或保证任何第三方产品、服务或运营活动，也不对第三方的违法行为、违反平台规则的行为或由此产生的后果负责。
        但本声明不限制适用法律所规定的责任，也不构成对任何具体行为合法性的保证。

        5. 使用即表示接受

        使用者应在使用前阅读并理解本免责声明及 MIT License。开始使用本软件，表示使用者已获得必要授权，并将自行承担使用本软件产生的风险；但仅通过使用行为是否构成法律上的合同接受，应以适用法律及实际使用场景为准。
        """;

    private const string AboutMitLicenseText =
        """
        MIT License

        Copyright (c) 2026 waynebian01

        Permission is hereby granted, free of charge, to any person obtaining a copy
        of this software and associated documentation files (the "Software"), to deal
        in the Software without restriction, including without limitation the rights
        to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
        copies of the Software, and to permit persons to whom the Software is
        furnished to do so, subject to the following conditions:

        The above copyright notice and this permission notice shall be included in all
        copies or substantial portions of the Software.

        THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
        IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
        FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
        AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
        LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
        OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
        SOFTWARE.
        """;

    private const string AboutBootstrapIconsAttributionText =
        """
        Bootstrap Icons

        https://github.com/twbs/icons

        Copyright (c) 2019-2024 The Bootstrap Authors

        本软件的部分界面图标来自该项目，采用 MIT License。
        """;

    private const string AboutCleanIconsAttributionText =
        """
        Clean Icons - Mechagnome Edition

        https://github.com/AcidWeb/Clean-Icons-Mechagnome-Edition

        Upscaled icon pack for World of Warcraft.

        本软件的技能与物品图标来自该项目。
        """;

    private const string AboutWowListfileAttributionText =
        """
        wow-listfile

        https://github.com/wowdev/wow-listfile

        A listfile for WoW archived files.

        本软件的部分文件名与资源索引数据来自该项目。
        """;

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
    private readonly List<SettingsNavGroup> _navGroups = [];
    private FlowLayoutPanel _navList = null!;
    private TextBox _navSearchBox = null!;
    private Label _navEmptyLabel = null!;
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
    private ListView _nameplateList = null!;
    private ListView _unitInfoList = null!;
    private TextBox _logTextBox = null!;
    private Panel _contentHost = null!;
    private Panel _settingsHost = null!;
    private Panel _configHost = null!;
    private Panel _macrosHost = null!;
    private Panel _moduleHost = null!;
    private Panel _aboutHost = null!;
    private Button _maximizeButton = null!;
    private bool _usesDwmRoundedCorners;
    private readonly System.Windows.Forms.Timer _roundedCornerResizeTimer;
    private BorderlessFormChrome.EdgeHitTransparentScope? _edgeHitScope;

    internal string SelectedPageKey => _selectedPage.ToString();

    public StatusForm()
    {
        _roundedCornerResizeTimer = new System.Windows.Forms.Timer
        {
            Interval = 50
        };
        _roundedCornerResizeTimer.Tick += (_, _) =>
        {
            _roundedCornerResizeTimer.Stop();
            if (IsHandleCreated && !_usesDwmRoundedCorners)
            {
                UiTheme.ApplyFallbackRoundedCorners(this);
            }
        };
        InitializeComponent();
        BorderlessFormChrome.ApplyResizePadding(this);
        _edgeHitScope = BorderlessFormChrome.InstallEdgeHitTransparent(this);
        UiTheme.SetListViewSubItemIconResolver(_stateList, ResolveStatusListIcon);
        UiTheme.SetListViewRowAccentResolver(_stateList, ResolveStateListAccent);
        UiTheme.SetListViewSubItemIconResolver(_auraList, ResolveStatusListIcon);
        UiTheme.SetListViewSubItemIconResolver(_spellList, ResolveStatusListIcon);
        UiTheme.SetListViewRowAccentResolver(_spellList, ResolveSpellListAccent);
        SpellIconCatalog.CatalogChanged += OnSpellIconCatalogChanged;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SpellIconCatalog.CatalogChanged -= OnSpellIconCatalogChanged;
            _roundedCornerResizeTimer.Dispose();
            _toolTip.Dispose();
            _edgeHitScope?.Dispose();
            _edgeHitScope = null;
        }

        base.Dispose(disposing);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UiTheme.ApplyDarkTitleBar(this);
        _usesDwmRoundedCorners = UiTheme.ApplyRoundedCorners(this);
        BorderlessFormChrome.ApplyResizePadding(this);
        SyncMaximizedBounds();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        BorderlessFormChrome.ApplyResizePadding(this);
        SyncMaximizedBounds();
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        SyncMaximizedBounds();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateMaximizeButton();
        SyncMaximizedBounds();
        if (!_usesDwmRoundedCorners && IsHandleCreated && WindowState == FormWindowState.Normal)
        {
            _roundedCornerResizeTimer.Stop();
            _roundedCornerResizeTimer.Start();
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == BorderlessFormChrome.WmGetMinMaxInfo)
        {
            base.WndProc(ref m);
            BorderlessFormChrome.TryHandleGetMinMaxInfo(this, ref m);
            return;
        }

        if (m.Msg == BorderlessFormChrome.WmNcHitTest)
        {
            base.WndProc(ref m);
            // 最大化时不启用边缘缩放命中。
            if (WindowState == FormWindowState.Normal)
            {
                BorderlessFormChrome.TryHandleNcHitTest(this, ref m);
            }

            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        BorderlessFormChrome.ApplyResizePadding(this);
        SyncMaximizedBounds();
        if (_hasKnownBounds)
        {
            return;
        }

        var workingArea = Screen.FromControl(this).WorkingArea;
        var targetWidth = Math.Min(1520, Math.Max(MinimumSize.Width, workingArea.Width - 80));
        var targetHeight = Math.Min(860, Math.Max(MinimumSize.Height, workingArea.Height - 80));
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

    private static Image? ResolveStatusListIcon(ListViewItem item, int columnIndex)
    {
        if (item.ListView is not { } list
            || columnIndex < 0
            || columnIndex >= list.Columns.Count
            || list.Columns[columnIndex].Text != "名称")
        {
            return null;
        }

        return item.Tag switch
        {
            StatusListIcon { IsItem: true, Id: var itemId } when itemId > 0 => SpellIconCatalog.GetItem(itemId),
            StatusListIcon { Id: var spellId } when spellId > 0 => SpellIconCatalog.Get(spellId),
            long itemId when itemId > 0 => SpellIconCatalog.GetItem(itemId),
            _ => null
        };
    }

    private static Color? ResolveStateListAccent(ListViewItem item)
        => item.Tag is string category && category.Length > 0
            ? UiTheme.GetStateCategoryAccent(category)
            : null;

    private static Color? ResolveSpellListAccent(ListViewItem item)
        => item.Tag switch
        {
            StatusListIcon { IsItem: true } => UiTheme.GetStateCategoryAccent(ClassStateCatalog.CategoryItem),
            StatusListIcon => UiTheme.GetStateCategoryAccent(ClassStateCatalog.CategoryState),
            _ => null
        };

    private void OnSpellIconCatalogChanged()
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(OnSpellIconCatalogChanged);
            return;
        }

        _stateList.Invalidate();
        _auraList.Invalidate();
        _spellList.Invalidate();
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        Text = "设置";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        MinimumSize = new Size(1180, 680);
        Size = new Size(1520, 860);
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
            RowCount = 2,
            ColumnCount = 1,
            Margin = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, TopBarHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        _settingsHost = CreatePageHost();
        _configHost = CreatePageHost();
        _macrosHost = CreatePageHost();
        _moduleHost = CreatePageHost();
        _aboutHost = CreatePageHost();

        // 「值」列与光环卡一致：Absolute min44 / width100；名称吸收剩余宽度。
        _stateList = UiTheme.CreateListView(Font, "status-state-v4",
            new UiTheme.ListColumn("#", 28, 28, FixedWidth: true),
            new UiTheme.ListColumn("分类", 56, 88),
            new UiTheme.ListColumn("名称", 40, 200, FillRemaining: true),
            new UiTheme.ListColumn("值", 44, 100));
        _auraList = UiTheme.CreateListView(Font, "status-aura-v3",
            new UiTheme.ListColumn("#", 28, 28, FixedWidth: true),
            new UiTheme.ListColumn("名称", 70, 240, FillRemaining: true),
            new UiTheme.ListColumn("spellId", 64, 100),
            new UiTheme.ListColumn("类型", 48, 80),
            new UiTheme.ListColumn("值", 44, 100));
        _dynamicUnitList = UiTheme.CreateListView(Font, "status-dynamic-unit-v3",
            new UiTheme.ListColumn("类型", 40, 140),
            new UiTheme.ListColumn("名称", 40, 240, FillRemaining: true),
            new UiTheme.ListColumn("值", 44, 100));
        _spellList = UiTheme.CreateListView(Font, "status-spell-v3",
            new UiTheme.ListColumn("#", 28, 28, FixedWidth: true),
            new UiTheme.ListColumn("名称", 70, 240, FillRemaining: true),
            new UiTheme.ListColumn("spellId", 64, 100),
            new UiTheme.ListColumn("类型", 58, 100),
            new UiTheme.ListColumn("值", 44, 100));

        _partyList = UiTheme.CreateListView(Font, "status-party",
            new UiTheme.ListColumn("单位", 120, 180, FixedWidth: true),
            new UiTheme.ListColumn("摘要", 320, 1600, FillRemaining: true));
        _nameplateList = UiTheme.CreateListView(Font, "status-nameplates",
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

        var titleBar = BuildTitleBar();
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SettingsEditor,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SettingsSidebarWidth));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var sidebar = BuildSettingsSidebar();
        _contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SettingsEditor,
            Padding = new Padding(28, 12, 24, 16),
            Margin = new Padding(0)
        };

        _navList.Controls.Add(_navEmptyLabel);
        AddNavGroup("常用");
        AddNavItem(SettingsPage.General, "通用", CreatePageShell("通用", "运行控制、配置同步、数据包与模块选择", _settingsHost));
        AddNavGroup("编辑");
        AddNavItem(SettingsPage.Config, "配置", CreatePageShell("配置", "编辑职业、专精和扫描字段", _configHost));
        AddNavItem(SettingsPage.Macros, "宏", CreatePageShell("宏", "维护职业动态宏、静态宏与特殊宏", _macrosHost));
        AddNavItem(SettingsPage.Modules, "模块", CreatePageShell("模块", "创建、匹配并维护运行模块", _moduleHost));
        AddNavGroup("监控");
        AddNavItem(SettingsPage.Status, "状态", CreatePageShell("状态", string.Empty, BuildStatusPage()));
        AddNavItem(SettingsPage.Party, "队伍", CreatePageShell("队伍", "当前队伍单位与扫描字段摘要", BuildFixedWidthSectionPage("队伍成员", _partyList, "实时队伍数据")));
        AddNavItem(SettingsPage.Nameplates, "姓名板", CreatePageShell("姓名板", $"{NameplateStateLayout.SlotCount} 个敌对姓名板与配置字段", BuildFixedWidthSectionPage("姓名板", _nameplateList, "实时姓名板数据")));
        AddNavItem(SettingsPage.Logic, "逻辑", CreatePageShell("逻辑", "运行时推荐目标与调试值", BuildFixedWidthSectionPage("逻辑信息", _unitInfoList, "当前模块的决策输出")));
        AddNavItem(SettingsPage.Logs, "日志", CreatePageShell("日志", "运行、模块匹配与施放记录", BuildLogPage()));
        AddNavGroup("说明");
        AddNavItem(SettingsPage.BossNumbers, "首领", CreatePageShell("首领编号", "副本首领的序号、名称与扫描编号", CreateLazyBossNumbersPage()));
        AddNavItem(SettingsPage.Event, "EX事件", CreatePageShell("EX 事件", "247 个首领技能事件及其像素编码", BuildExBossEventsPage()));
        AddNavItem(SettingsPage.BigWigsEvent, "BW事件", CreatePageShell("BigWigs 团本事件", $"{BigWigsEventCatalog.Events.Count} 个团队首领技能事件及其像素编码", BuildBigWigsEventsPage()));
        AddNavItem(SettingsPage.CommonFields, "字段", CreatePageShell("常用字段", "模块条件可用的状态字段参考", CreateLazyCommonFieldsPage()));
        AddNavGroup("系统");
        AddNavItem(SettingsPage.About, "关于", CreatePageShell("关于", "应用信息、免责声明、许可证与来源", _aboutHost));
        _aboutHost.Controls.Add(BuildAboutPanel());

        body.Controls.Add(sidebar, 0, 0);
        body.Controls.Add(_contentHost, 1, 0);
        root.Controls.Add(titleBar, 0, 0);
        root.Controls.Add(body, 0, 1);

        InitializeEmptyLists();
        ResumeLayout(false);
        SelectView(SettingsPage.General);
        SyncNavItemWidths();
    }

    private void InitializeEmptyLists()
    {
        ReplaceItems(_stateList, [new ListViewItem(["-", "-", "状态", "等待游戏状态"])]);
        ReplaceItems(_auraList, [new ListViewItem(["-", "光环", "-", "-", "无数据"])]);
        ReplaceItems(_spellList, [new ListViewItem(["-", "技能", "-", "-", "无数据"])]);
        ReplaceItems(_dynamicUnitList, [new ListViewItem(["-", "动态单位", "等待游戏状态"])]);
        ReplaceItems(_partyList, [new ListViewItem(["队伍", "无队伍数据"])]);
        ReplaceItems(_nameplateList, [new ListViewItem(["姓名板", "无姓名板数据"])]);
        ReplaceItems(_unitInfoList, [new ListViewItem(["逻辑信息", "无推荐目标"])]);
    }

    private Control BuildTitleBar()
    {
        // 无边框标题栏：品牌图标 | 拖拽区 | 最小化/最大化/关闭。页导航在左栏。
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Background,
            ColumnCount = 3,
            RowCount = 1,
            // 右侧贴边，便于 Windows 风格矩形标题按钮顶满高度。
            Padding = new Padding(12, 0, 0, 0),
            Margin = new Padding(0)
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.Paint += (_, e) =>
        {
            using var divider = new Pen(UiTheme.Border);
            var y = shell.ClientSize.Height - 1;
            e.Graphics.DrawLine(divider, 0, y, shell.ClientSize.Width, y);
        };

        var brandIcon = new PictureBox
        {
            Size = new Size(NavBrandIconSize, NavBrandIconSize),
            MinimumSize = new Size(NavBrandIconSize, NavBrandIconSize),
            MaximumSize = new Size(NavBrandIconSize, NavBrandIconSize),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Margin = new Padding(2, 0, 14, 0),
            Anchor = AnchorStyles.None,
            TabStop = false,
            AccessibleName = "Shigure"
        };
        brandIcon.Image = LoadNavBrandIcon();
        EnableDrag(brandIcon);

        var dragArea = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Background,
            Margin = new Padding(0)
        };
        EnableDrag(dragArea);

        var chromeActions = new FlowLayoutPanel
        {
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        var minimizeButton = CreateChromeButton("─", "最小化");
        minimizeButton.Click += (_, _) => WindowState = FormWindowState.Minimized;
        _maximizeButton = CreateChromeButton("□", "最大化");
        _maximizeButton.Click += (_, _) => ToggleMaximize();
        var closeButton = CreateChromeButton("✕", "关闭", isClose: true);
        closeButton.Click += (_, _) => Close();
        chromeActions.Controls.Add(minimizeButton);
        chromeActions.Controls.Add(_maximizeButton);
        chromeActions.Controls.Add(closeButton);

        shell.Controls.Add(brandIcon, 0, 0);
        shell.Controls.Add(dragArea, 1, 0);
        shell.Controls.Add(chromeActions, 2, 0);
        EnableDrag(shell);
        UpdateMaximizeButton();
        return shell;
    }

    private Control BuildSettingsSidebar()
    {
        var sidebar = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SettingsSidebar,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        sidebar.Paint += (_, e) =>
        {
            using var pen = new Pen(UiTheme.Border);
            var x = Math.Max(0, sidebar.ClientSize.Width - 1);
            e.Graphics.DrawLine(pen, x, 0, x, sidebar.ClientSize.Height);
        };

        var searchHost = new Panel
        {
            Dock = DockStyle.Top,
            Height = 48,
            BackColor = UiTheme.SettingsSidebar,
            Padding = new Padding(12, 12, 12, 8),
            Margin = new Padding(0)
        };
        _navSearchBox = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "搜索设置",
            AccessibleName = "搜索设置",
            Font = Font
        };
        UiTheme.StyleTextBox(_navSearchBox);
        _navSearchBox.BackColor = UiTheme.SettingsInput;
        _navSearchBox.TextChanged += (_, _) => ApplyNavFilter();
        searchHost.Controls.Add(_navSearchBox);

        _navList = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = UiTheme.SettingsSidebar,
            Margin = new Padding(0),
            Padding = new Padding(0, 4, 0, 8)
        };
        _navList.Resize += (_, _) => SyncNavItemWidths();

        _navEmptyLabel = new Label
        {
            Text = "没有匹配的设置",
            AutoSize = false,
            Height = SettingsNavGroupHeight,
            ForeColor = UiTheme.Muted,
            BackColor = UiTheme.SettingsSidebar,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(16, 4, 8, 0),
            Margin = new Padding(0),
            Visible = false
        };

        // 先加入 Fill，再加入 Top，使搜索框停在左栏顶部。
        sidebar.Controls.Add(_navList);
        sidebar.Controls.Add(searchHost);
        return sidebar;
    }

    private void SyncMaximizedBounds()
    {
        var working = BorderlessFormChrome.GetWorkingArea(this);
        if (MaximizedBounds != working)
        {
            MaximizedBounds = working;
        }
    }

    private void ToggleMaximize()
    {
        SyncMaximizedBounds();
        if (WindowState == FormWindowState.Maximized)
        {
            WindowState = FormWindowState.Normal;
        }
        else
        {
            // 先同步工作区，再最大化，避免盖住任务栏；还原由系统记回 Maximize 前的 Normal bounds。
            WindowState = FormWindowState.Maximized;
        }

        UpdateMaximizeButton();
    }

    private void UpdateMaximizeButton()
    {
        if (_maximizeButton is null || _maximizeButton.IsDisposed)
        {
            return;
        }

        var maximized = WindowState == FormWindowState.Maximized;
        _maximizeButton.Text = maximized ? "❐" : "□";
        _toolTip.SetToolTip(_maximizeButton, maximized ? "还原" : "最大化");
    }

    private static Image? LoadNavBrandIcon()
    {
        // 优先 32px 资源，高度贴近导航文字行。
        const string resourceName = "Shigure.Assets.arasaka-icon-32.png";
        using var stream = typeof(StatusForm).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    private Button CreateChromeButton(string text, string tooltip, bool isClose = false)
    {
        // Windows 风格：矩形命中区、无圆角胶囊底，默认与顶栏同色。
        var button = new Button
        {
            Text = text,
            AutoSize = false,
            Size = new Size(ChromeButtonWidth, TopBarHeight),
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            FlatStyle = FlatStyle.Flat,
            BackColor = UiTheme.Background,
            ForeColor = UiTheme.Muted,
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand,
            TabStop = false,
            Font = new Font("Segoe UI Symbol", 10F, FontStyle.Regular)
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.BorderColor = UiTheme.Background;
        button.FlatAppearance.MouseOverBackColor = isClose
            ? Color.FromArgb(196, 43, 28)
            : UiTheme.Hover;
        button.FlatAppearance.MouseDownBackColor = isClose
            ? Color.FromArgb(153, 27, 21)
            : UiTheme.Pressed;
        if (isClose)
        {
            button.MouseEnter += (_, _) => button.ForeColor = Color.White;
            button.MouseLeave += (_, _) => button.ForeColor = UiTheme.Muted;
        }

        _toolTip.SetToolTip(button, tooltip);
        return button;
    }

    private void EnableDrag(Control control)
    {
        control.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessageW(Handle, NativeMethods.WmNcLButtonDown, NativeMethods.HtCaption, 0);
            }
        };
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
        // 页标题是普通文字，不再套卡片。编辑器和列表仍放在标题下方。
        var hasSubtitle = !string.IsNullOrWhiteSpace(subtitle);
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SettingsEditor,
            ColumnCount = 1,
            RowCount = hasSubtitle ? 3 : 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        if (hasSubtitle)
        {
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        }

        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Text,
            BackColor = Color.Transparent,
            Font = new Font(Font.FontFamily, 18F, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        }, 0, 0);

        var contentRow = 1;
        if (hasSubtitle)
        {
            shell.Controls.Add(new Label
            {
                Text = subtitle,
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.Muted,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Margin = new Padding(0)
            }, 0, 1);
            contentRow = 2;
        }

        content.Dock = DockStyle.Fill;
        content.Margin = new Padding(0, 8, 0, 0);
        shell.Controls.Add(content, 0, contentRow);
        return shell;
    }

    private const int StatusCardWidth = 600;

    private Control BuildStatusPage()
    {
        var contentWidth = StatusCardWidth * 4 + UiTheme.PageGap * 3;
        var scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = UiTheme.Surface,
            Margin = new Padding(0)
        };

        var statusSplit = new TableLayoutPanel
        {
            Dock = DockStyle.None,
            Location = Point.Empty,
            BackColor = UiTheme.Surface,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            Width = contentWidth
        };
        statusSplit.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var sections = new[]
        {
            BuildSection("状态", _stateList, "基础字段与当前模块"),
            BuildSection("光环", _auraList, "时间与层数"),
            BuildSection("技能", _spellList, "冷却、充能与次数"),
            BuildSection("动态单位", _dynamicUnitList, "模块运行时计算值")
        };

        for (var i = 0; i < sections.Length; i++)
        {
            var hasGap = i < sections.Length - 1;
            statusSplit.ColumnStyles.Add(new ColumnStyle(
                SizeType.Absolute,
                StatusCardWidth + (hasGap ? UiTheme.PageGap : 0)));
            sections[i].Dock = DockStyle.Fill;
            sections[i].MinimumSize = new Size(StatusCardWidth, 0);
            sections[i].MaximumSize = new Size(StatusCardWidth, 0);
            sections[i].Margin = new Padding(0, 0, hasGap ? UiTheme.PageGap : 0, 0);
            statusSplit.Controls.Add(sections[i], i, 0);
        }

        void SyncScrollLayout()
        {
            // 内容比视口宽时预留底栏横向滚动条高度，避免再挤出纵向滚动条。
            var viewHeight = scrollHost.ClientSize.Height;
            var needsHorizontalScroll = contentWidth > scrollHost.ClientSize.Width;
            if (needsHorizontalScroll && !scrollHost.HorizontalScroll.Visible)
            {
                viewHeight = Math.Max(1, viewHeight - SystemInformation.HorizontalScrollBarHeight);
            }

            var height = Math.Max(200, viewHeight);
            var nextSize = new Size(contentWidth, height);
            if (statusSplit.Size != nextSize)
            {
                statusSplit.Size = nextSize;
            }

            // 只声明最小内容宽度，强制出现底部横向滚动条。
            var minSize = new Size(contentWidth, 0);
            if (scrollHost.AutoScrollMinSize != minSize)
            {
                scrollHost.AutoScrollMinSize = minSize;
            }
        }

        scrollHost.Controls.Add(statusSplit);
        scrollHost.Resize += (_, _) => SyncScrollLayout();
        scrollHost.HandleCreated += (_, _) => BeginInvoke(SyncScrollLayout);
        SyncScrollLayout();
        return scrollHost;
    }

    private TableLayoutPanel BuildSection(
        string title,
        Control content,
        string subtitle,
        ListView? countListView = null)
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
        countListView ??= content as ListView;
        if (countListView is { } listView)
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

    private static void SyncCenteredContentLayout(
        Panel scrollHost,
        Control content,
        int contentWidth,
        int? contentHeight = null)
    {
        if (content.Width != contentWidth)
        {
            content.Width = contentWidth;
        }

        if (contentHeight is { } height && content.Height != height)
        {
            content.Height = height;
        }

        var viewWidth = scrollHost.ClientSize.Width;
        var left = viewWidth > contentWidth
            ? (viewWidth - contentWidth) / 2
            : 0;
        if (content.Left != left)
        {
            content.Left = left;
        }

        if (content.Top != 0)
        {
            content.Top = 0;
        }

        var minSize = new Size(contentWidth, 0);
        if (scrollHost.AutoScrollMinSize != minSize)
        {
            scrollHost.AutoScrollMinSize = minSize;
        }
    }

    private Control BuildFixedWidthSectionPage(
        string title,
        Control content,
        string subtitle,
        ListView? countListView = null,
        int? contentWidth = null)
    {
        var pageWidth = contentWidth ?? SettingsContentWidth;
        var scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = UiTheme.Surface,
            Margin = new Padding(0)
        };

        var section = BuildSection(title, content, subtitle, countListView);
        section.Dock = DockStyle.None;
        section.Location = Point.Empty;
        section.Width = pageWidth;
        section.MinimumSize = new Size(pageWidth, 0);
        section.MaximumSize = new Size(pageWidth, 0);

        void SyncScrollLayout()
        {
            var viewHeight = scrollHost.ClientSize.Height;
            if (pageWidth > scrollHost.ClientSize.Width && !scrollHost.HorizontalScroll.Visible)
            {
                viewHeight = Math.Max(1, viewHeight - SystemInformation.HorizontalScrollBarHeight);
            }

            var height = Math.Max(200, viewHeight);
            SyncCenteredContentLayout(scrollHost, section, pageWidth, height);
        }

        scrollHost.Controls.Add(section);
        scrollHost.Resize += (_, _) => SyncScrollLayout();
        scrollHost.HandleCreated += (_, _) => BeginInvoke(SyncScrollLayout);
        SyncScrollLayout();
        return scrollHost;
    }

    private Control BuildLogPage()
    {
        var scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = UiTheme.Surface,
            Margin = new Padding(0)
        };

        var card = new UiCardPanel
        {
            Dock = DockStyle.None,
            Location = Point.Empty,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(UiTheme.CardPadding),
            Margin = new Padding(0),
            Width = SettingsContentWidth,
            MinimumSize = new Size(SettingsContentWidth, 0),
            MaximumSize = new Size(SettingsContentWidth, 0)
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
        _logTextBox.Dock = DockStyle.Fill;
        _logTextBox.Margin = new Padding(0);
        card.Controls.Add(_logTextBox, 0, 1);

        void SyncScrollLayout()
        {
            var viewHeight = scrollHost.ClientSize.Height;
            if (SettingsContentWidth > scrollHost.ClientSize.Width && !scrollHost.HorizontalScroll.Visible)
            {
                viewHeight = Math.Max(1, viewHeight - SystemInformation.HorizontalScrollBarHeight);
            }

            var height = Math.Max(200, viewHeight);
            SyncCenteredContentLayout(scrollHost, card, SettingsContentWidth, height);
        }

        scrollHost.Controls.Add(card);
        scrollHost.Resize += (_, _) => SyncScrollLayout();
        scrollHost.HandleCreated += (_, _) => BeginInvoke(SyncScrollLayout);
        SyncScrollLayout();
        return scrollHost;
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

    private void AddNavGroup(string title)
    {
        var header = new Label
        {
            Text = title,
            AutoSize = false,
            Size = new Size(SettingsSidebarWidth, SettingsNavGroupHeight),
            ForeColor = UiTheme.Muted,
            BackColor = UiTheme.SettingsSidebar,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Regular),
            TextAlign = ContentAlignment.BottomLeft,
            Padding = new Padding(16, 10, 8, 4),
            Margin = new Padding(0)
        };
        _navGroups.Add(new SettingsNavGroup { Header = header });
        _navList.Controls.Add(header);
    }

    private void AddNavItem(SettingsPage page, string text, Control view)
    {
        view.Dock = DockStyle.Fill;
        view.Visible = false;
        _contentHost.Controls.Add(view);

        var button = new SettingsNavButton
        {
            Text = text,
            AutoSize = false,
            Size = new Size(SettingsSidebarWidth, SettingsNavItemHeight),
            Font = Font,
            Margin = new Padding(0),
            Cursor = Cursors.Hand,
            TabStop = true,
            AccessibleName = text
        };

        button.Click += (_, _) => SelectView(page);
        _navItems.Add((button, view, page));
        _navGroups[^1].Items.Add(button);
        _navList.Controls.Add(button);
    }

    /// <summary>
    /// 按页名过滤左栏。有关键字时只保留命中的页，分组标题一并隐藏；无命中显示空状态。
    /// </summary>
    private void ApplyNavFilter()
    {
        var query = _navSearchBox.Text.Trim();
        var any = false;
        foreach (var group in _navGroups)
        {
            var groupHit = false;
            foreach (var item in group.Items)
            {
                var hit = query.Length == 0
                    || item.Text.Contains(query, StringComparison.OrdinalIgnoreCase);
                item.Visible = hit;
                if (hit)
                {
                    groupHit = true;
                    any = true;
                }
            }

            group.Header.Visible = query.Length == 0 && groupHit;
        }

        _navEmptyLabel.Visible = !any;
        _navList.PerformLayout();
        SyncNavItemWidths();
    }

    private void SyncNavItemWidths()
    {
        if (_navList is null || _navList.IsDisposed)
        {
            return;
        }

        var width = Math.Max(1, _navList.ClientSize.Width - _navList.Padding.Horizontal);
        foreach (Control child in _navList.Controls)
        {
            if (child.Width != width)
            {
                child.Width = width;
            }
        }
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

    /// <summary>
    /// 首领页首次点开时再构建（避免启动/进设置就创建大量控件）。
    /// </summary>
    private Control CreateLazyBossNumbersPage()
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Margin = new Padding(0)
        };
        var built = false;
        host.VisibleChanged += (_, _) =>
        {
            if (!host.Visible || built || host.IsDisposed)
            {
                return;
            }

            built = true;
            host.SuspendLayout();
            try
            {
                var page = BuildBossNumbersPage();
                page.Dock = DockStyle.Fill;
                host.Controls.Add(page);
            }
            finally
            {
                host.ResumeLayout(true);
            }
        };
        return host;
    }

    private Control BuildBossNumbersPage()
    {
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

        // 两张赛季大卡各挂一个 ListView（分组=副本），避免每副本一套 TableLayout+Label。
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        root.SuspendLayout();
        try
        {
            for (var i = 0; i < seasonGroups.Count; i++)
            {
                var card = CreateBossSeasonCard(seasonGroups[i]);
                card.Margin = new Padding(0, 0, 0, i == 0 ? UiTheme.PageGap : 0);
                root.Controls.Add(card, 0, i);
            }
        }
        finally
        {
            root.ResumeLayout(true);
        }

        return root;
    }

    private Control CreateBossSeasonCard(BossNumberGroup group)
    {
        var card = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(UiTheme.CardPadding),
            Margin = new Padding(0)
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        card.Controls.Add(new Label
        {
            Text = group.Title,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Text,
            BackColor = Color.Transparent,
            Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        }, 0, 0);

        // 名称列 FillRemaining 上限须够大，否则宽窗口右侧会留出空白深灰条。
        // 不用 ShowGroups：.NET ListView 组头无法 OwnerDraw，系统默认呈链接色。
        var cacheKey = group.Title == "当前赛季" ? "boss-numbers-current-v2" : "boss-numbers-season1-v2";
        var list = UiTheme.CreateListView(
            Font,
            cacheKey,
            new UiTheme.ListColumn("副本", 96, 220),
            new UiTheme.ListColumn("序号", 48, 56, FixedWidth: true),
            new UiTheme.ListColumn("名称", 120, 2000, FillRemaining: true),
            new UiTheme.ListColumn("编号", 56, 72));
        list.BackColor = UiTheme.SurfaceRaised;
        list.ShowGroups = false;
        UiTheme.EmphasizeListViewPrimaryColumn(list, Font);

        list.BeginUpdate();
        try
        {
            foreach (var dungeon in group.Dungeons)
            {
                var firstInDungeon = true;
                foreach (var boss in dungeon.Bosses)
                {
                    // 同副本仅首行显示副本名，避免整列重复；字重/颜色由 Emphasize 统一。
                    var item = new ListViewItem(
                    [
                        firstInDungeon ? dungeon.Name : string.Empty,
                        boss.Sequence.ToString(),
                        boss.Name,
                        boss.Number.ToString()
                    ]);
                    list.Items.Add(item);
                    firstInDungeon = false;
                }
            }
        }
        finally
        {
            list.EndUpdate();
        }

        card.Controls.Add(list, 0, 1);
        return card;
    }

    private Control BuildExBossEventsPage()
    {
        var allEvents = ExBossEventCatalog.Events;
        var eventList = UiTheme.CreateListView(Font, "ex-boss-events",
            new UiTheme.ListColumn("键", 56, 72, FixedWidth: true),
            new UiTheme.ListColumn("像素编码", 88, 110, FixedWidth: true),
            new UiTheme.ListColumn("事件 ID", 82, 100, FixedWidth: true),
            new UiTheme.ListColumn("技能 ID", 92, 112, FixedWidth: true),
            new UiTheme.ListColumn("类型", 72, 92, FixedWidth: true),
            new UiTheme.ListColumn("事件", 150, 260),
            new UiTheme.ListColumn("副本", 190, 320),
            new UiTheme.ListColumn("首领", 190, 520, FillRemaining: true));

        var dungeonFilter = CreateExBossEventFilter(250);
        var bossFilter = CreateExBossEventFilter(250);
        var typeFilter = CreateExBossEventFilter(190);
        dungeonFilter.Items.Add(new ExBossEventFilterOption("副本：全部", null));
        foreach (var dungeon in allEvents
                     .Select(item => item.MapName)
                     .Where(name => name.Length > 0)
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            dungeonFilter.Items.Add(new ExBossEventFilterOption($"副本：{dungeon}", dungeon));
        }

        typeFilter.Items.Add(new ExBossEventFilterOption("类型：全部", null));
        foreach (var type in ExBossEventCatalog.MechanicTypes.Where(item => item.Value > 0))
        {
            typeFilter.Items.Add(new ExBossEventFilterOption($"类型：{type.Name}", type.Name));
        }

        var filterRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        filterRow.Controls.Add(new Label
        {
            Text = "筛选",
            AutoSize = false,
            Size = new Size(52, 36),
            ForeColor = UiTheme.Muted,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 4, 8, 4)
        });
        filterRow.Controls.Add(dungeonFilter);
        filterRow.Controls.Add(bossFilter);
        filterRow.Controls.Add(typeFilter);

        var eventContent = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0)
        };
        eventContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        eventContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        eventContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        eventContent.Controls.Add(filterRow, 0, 0);
        eventList.Dock = DockStyle.Fill;
        eventList.Margin = new Padding(0);
        eventContent.Controls.Add(eventList, 0, 1);

        var page = BuildFixedWidthSectionPage(
            "EX 首领技能事件",
            eventContent,
            "事件键按 eventID 升序生成；像素写入键 / 255，模块条件使用整数键 1–247",
            eventList,
            UiTheme.EventPageWidth);

        var updatingFilters = false;

        void ResetBossFilter()
        {
            updatingFilters = true;
            try
            {
                var selectedDungeon = (dungeonFilter.SelectedItem as ExBossEventFilterOption)?.Value;
                bossFilter.BeginUpdate();
                bossFilter.Items.Clear();
                bossFilter.Items.Add(new ExBossEventFilterOption("首领：全部", null));
                foreach (var boss in allEvents
                             .Where(item => selectedDungeon is null
                                 || string.Equals(item.MapName, selectedDungeon, StringComparison.Ordinal))
                             .Select(item => item.BossName)
                             .Where(name => name.Length > 0)
                             .Distinct(StringComparer.Ordinal)
                             .OrderBy(name => name, StringComparer.Ordinal))
                {
                    bossFilter.Items.Add(new ExBossEventFilterOption($"首领：{boss}", boss));
                }

                bossFilter.SelectedIndex = 0;
                bossFilter.EndUpdate();
            }
            finally
            {
                updatingFilters = false;
            }
        }

        void ApplyFilters()
        {
            if (updatingFilters)
            {
                return;
            }

            var selectedDungeon = (dungeonFilter.SelectedItem as ExBossEventFilterOption)?.Value;
            var selectedBoss = (bossFilter.SelectedItem as ExBossEventFilterOption)?.Value;
            var selectedType = (typeFilter.SelectedItem as ExBossEventFilterOption)?.Value;
            var filteredEvents = allEvents.Where(item =>
                (selectedDungeon is null || string.Equals(item.MapName, selectedDungeon, StringComparison.Ordinal))
                && (selectedBoss is null || string.Equals(item.BossName, selectedBoss, StringComparison.Ordinal))
                && (selectedType is null || string.Equals(item.MechanicType, selectedType, StringComparison.Ordinal)));
            ReplaceItems(eventList, CreateExBossEventItems(filteredEvents));
        }

        dungeonFilter.SelectedIndexChanged += (_, _) =>
        {
            ResetBossFilter();
            ApplyFilters();
        };
        bossFilter.SelectedIndexChanged += (_, _) => ApplyFilters();
        typeFilter.SelectedIndexChanged += (_, _) => ApplyFilters();
        dungeonFilter.SelectedIndex = 0;
        typeFilter.SelectedIndex = 0;
        ResetBossFilter();
        ApplyFilters();
        return page;
    }

    private static UiDropDown CreateExBossEventFilter(int width)
    {
        var filter = new UiDropDown
        {
            AutoSize = false,
            Size = new Size(width, 36),
            DropDownWidth = Math.Max(width, 280),
            Margin = new Padding(0, 4, 10, 4)
        };
        UiTheme.StyleComboBox(filter);
        return filter;
    }

    private static IReadOnlyList<ListViewItem> CreateExBossEventItems(IEnumerable<ExBossEventInfo> events)
        => events
            .Select(item => new ListViewItem(
            [
                item.Key.ToString(),
                $"{item.Key} / 255",
                item.EventId.ToString(),
                item.SpellId.ToString(),
                item.MechanicType,
                item.Name,
                item.MapName,
                item.BossName
            ])
            {
                Tag = item,
                ToolTipText = $"键 {item.Key} · eventID {item.EventId} · spellID {item.SpellId} · {item.MapName} / {item.BossName} / {item.Name}"
            })
            .ToArray();

    private Control BuildBigWigsEventsPage()
    {
        var allEvents = BigWigsEventCatalog.Events;
        var eventList = UiTheme.CreateListView(Font, "bigwigs-boss-events",
            new UiTheme.ListColumn("键", 56, 72, FixedWidth: true),
            new UiTheme.ListColumn("像素编码", 88, 110, FixedWidth: true),
            new UiTheme.ListColumn("技能 ID", 100, 120, FixedWidth: true),
            new UiTheme.ListColumn("类型", 170, 280),
            new UiTheme.ListColumn("事件", 170, 300),
            new UiTheme.ListColumn("副本", 180, 300),
            new UiTheme.ListColumn("首领", 190, 460, FillRemaining: true));

        var dungeonFilter = CreateExBossEventFilter(250);
        var bossFilter = CreateExBossEventFilter(250);
        var typeFilter = CreateExBossEventFilter(190);
        dungeonFilter.Items.Add(new ExBossEventFilterOption("副本：全部", null));
        foreach (var dungeon in allEvents
                     .Select(item => item.MapName)
                     .Where(name => name.Length > 0)
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            dungeonFilter.Items.Add(new ExBossEventFilterOption($"副本：{dungeon}", dungeon));
        }

        typeFilter.Items.Add(new ExBossEventFilterOption("类型：全部", null));
        foreach (var type in BigWigsEventCatalog.EventTypes.Where(type =>
                     type.Value > 0 && allEvents.Any(item => BigWigsEventHasType(item, type.Name))))
        {
            typeFilter.Items.Add(new ExBossEventFilterOption($"类型：{type.Name}", type.Name));
        }

        var filterRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        filterRow.Controls.Add(new Label
        {
            Text = "筛选",
            AutoSize = false,
            Size = new Size(52, 36),
            ForeColor = UiTheme.Muted,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 4, 8, 4)
        });
        filterRow.Controls.Add(dungeonFilter);
        filterRow.Controls.Add(bossFilter);
        filterRow.Controls.Add(typeFilter);

        var eventContent = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0)
        };
        eventContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        eventContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        eventContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        eventContent.Controls.Add(filterRow, 0, 0);
        eventList.Dock = DockStyle.Fill;
        eventList.Margin = new Padding(0);
        eventContent.Controls.Add(eventList, 0, 1);

        var page = BuildFixedWidthSectionPage(
            "BigWigs 团队首领技能事件",
            eventContent,
            $"按 spellID 升序生成 {allEvents.Count} 个键；像素写入键 / 255，模块条件使用整数键",
            eventList,
            UiTheme.EventPageWidth);

        var updatingFilters = false;

        void ResetBossFilter()
        {
            updatingFilters = true;
            try
            {
                var selectedDungeon = (dungeonFilter.SelectedItem as ExBossEventFilterOption)?.Value;
                bossFilter.BeginUpdate();
                bossFilter.Items.Clear();
                bossFilter.Items.Add(new ExBossEventFilterOption("首领：全部", null));
                foreach (var boss in allEvents
                             .Where(item => selectedDungeon is null
                                 || string.Equals(item.MapName, selectedDungeon, StringComparison.Ordinal))
                             .Select(item => item.BossName)
                             .Where(name => name.Length > 0)
                             .Distinct(StringComparer.Ordinal)
                             .OrderBy(name => name, StringComparer.Ordinal))
                {
                    bossFilter.Items.Add(new ExBossEventFilterOption($"首领：{boss}", boss));
                }

                bossFilter.SelectedIndex = 0;
                bossFilter.EndUpdate();
            }
            finally
            {
                updatingFilters = false;
            }
        }

        void ApplyFilters()
        {
            if (updatingFilters)
            {
                return;
            }

            var selectedDungeon = (dungeonFilter.SelectedItem as ExBossEventFilterOption)?.Value;
            var selectedBoss = (bossFilter.SelectedItem as ExBossEventFilterOption)?.Value;
            var selectedType = (typeFilter.SelectedItem as ExBossEventFilterOption)?.Value;
            var filteredEvents = allEvents.Where(item =>
                (selectedDungeon is null || string.Equals(item.MapName, selectedDungeon, StringComparison.Ordinal))
                && (selectedBoss is null || string.Equals(item.BossName, selectedBoss, StringComparison.Ordinal))
                && (selectedType is null || BigWigsEventHasType(item, selectedType)));
            ReplaceItems(eventList, CreateBigWigsEventItems(filteredEvents));
        }

        dungeonFilter.SelectedIndexChanged += (_, _) =>
        {
            ResetBossFilter();
            ApplyFilters();
        };
        bossFilter.SelectedIndexChanged += (_, _) => ApplyFilters();
        typeFilter.SelectedIndexChanged += (_, _) => ApplyFilters();
        dungeonFilter.SelectedIndex = 0;
        typeFilter.SelectedIndex = 0;
        ResetBossFilter();
        ApplyFilters();
        return page;
    }

    private static bool BigWigsEventHasType(BigWigsEventInfo item, string selectedType)
        => item.Methods.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains(selectedType, StringComparer.Ordinal);

    private static IReadOnlyList<ListViewItem> CreateBigWigsEventItems(IEnumerable<BigWigsEventInfo> events)
        => events
            .Select(item => new ListViewItem(
            [
                item.Key.ToString(),
                $"{item.Key} / 255",
                item.SpellId.ToString(),
                DescribeBigWigsEventTypes(item),
                item.Name,
                item.MapName,
                item.BossName
            ])
            {
                Tag = item,
                ToolTipText = $"键 {item.Key} · spellID {item.SpellId} · {DescribeBigWigsEventTypes(item)} · {item.MapName} / {item.BossName} / {item.Name}"
            })
            .ToArray();

    private static string DescribeBigWigsEventTypes(BigWigsEventInfo item)
    {
        var types = BigWigsEventCatalog.EventTypes
            .Where(type => type.Value > 0 && BigWigsEventHasType(item, type.Name))
            .Select(type => type.Name)
            .ToArray();
        return types.Length > 0 ? string.Join(" / ", types) : "未分类";
    }

    private Control BuildAboutPanel()
    {
        var scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            AutoScroll = true,
            Margin = new Padding(0)
        };

        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 6,
            Location = Point.Empty,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        ApplySettingsCardWidth(panel);
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SettingsContentWidth));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var infoCard = new UiCardPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(UiTheme.CardPadding),
            Margin = new Padding(0, 0, 0, UiTheme.PageGap)
        };
        ApplySettingsCardWidth(infoCard);
        infoCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        infoCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, AboutLogoSize + UiTheme.PageGap));
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
            MaximumSize = new Size(GetAboutInfoTextWidth(), 0),
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
        var logo = new AboutLogoBox(GetEmbeddedResourceName(AboutLogoResourcePath), AboutLogoSize, AboutLogoOpacity)
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(AboutLogoSize, AboutLogoSize),
            Margin = new Padding(UiTheme.PageGap, 0, 0, 0)
        };
        infoCard.Controls.Add(logo, 1, 0);
        infoCard.SetRowSpan(logo, 2);
        panel.Controls.Add(infoCard, 0, 0);
        panel.Controls.Add(CreateAboutArticleCard("免责声明", AboutDisclaimerText), 0, 1);
        panel.Controls.Add(CreateAboutArticleCard("许可证", AboutMitLicenseText), 0, 2);
        panel.Controls.Add(CreateAboutArticleCard("界面图标来源", AboutBootstrapIconsAttributionText), 0, 3);
        panel.Controls.Add(CreateAboutArticleCard("技能与物品图标来源", AboutCleanIconsAttributionText), 0, 4);
        panel.Controls.Add(CreateAboutArticleCard("数据来源", AboutWowListfileAttributionText), 0, 5);

        void SyncAboutLayout()
            => SyncCenteredContentLayout(scrollHost, panel, SettingsContentWidth);

        scrollHost.Controls.Add(panel);
        scrollHost.Resize += (_, _) => SyncAboutLayout();
        scrollHost.HandleCreated += (_, _) => BeginInvoke(SyncAboutLayout);
        panel.SizeChanged += (_, _) => SyncAboutLayout();
        SyncAboutLayout();
        return scrollHost;
    }

    private static void ApplySettingsCardWidth(Control card)
    {
        card.Dock = DockStyle.None;
        card.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        card.Width = SettingsContentWidth;
        card.MinimumSize = new Size(SettingsContentWidth, 0);
        card.MaximumSize = new Size(SettingsContentWidth, 0);
    }

    private static int GetAboutCardInnerWidth()
        => Math.Max(80, SettingsContentWidth - UiTheme.CardPadding * 2);

    private static int GetAboutInfoTextWidth()
        => Math.Max(80, GetAboutCardInnerWidth() - AboutLogoSize - UiTheme.PageGap);

    private Control CreateAboutArticleCard(string title, string body)
    {
        var card = new UiCardPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(UiTheme.CardPadding),
            Margin = new Padding(0, 0, 0, UiTheme.PageGap)
        };
        ApplySettingsCardWidth(card);
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var heading = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 8)
        };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, AboutScaleIconSize + 6));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.Controls.Add(new AboutScaleIcon
        {
            Width = AboutScaleIconSize,
            Height = AboutScaleIconSize,
            Margin = new Padding(0, 1, 6, 0)
        }, 0, 0);
        heading.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Accent,
            BackColor = Color.Transparent,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        }, 1, 0);
        card.Controls.Add(heading, 0, 0);

        var bodyLabel = new Label
        {
            Text = body,
            AutoSize = true,
            Dock = DockStyle.Top,
            ForeColor = UiTheme.Text,
            BackColor = Color.Transparent,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Regular),
            MaximumSize = new Size(GetAboutCardInnerWidth(), 0),
            Margin = new Padding(0)
        };
        card.Controls.Add(bodyLabel, 0, 1);
        return card;
    }

    /// <summary>
    /// 字段页分组：与 ClassStateCatalog.TopCategories 对齐，拆成两张全宽卡（对齐首领页两赛季）。
    /// </summary>
    private static readonly string[] PlayerCommonFieldCategories =
    [
        ClassStateCatalog.CategoryState,
        ClassStateCatalog.CategorySpecial,
        ClassStateCatalog.CategoryResource,
        ClassStateCatalog.CategoryConfig
    ];

    private static readonly string[] UnitCommonFieldCategories =
    [
        ClassStateCatalog.CategoryTarget,
        ClassStateCatalog.CategoryFocus,
        ClassStateCatalog.CategoryMouseover,
        ClassStateCatalog.CategoryPet,
        ClassStateCatalog.CategoryBoss1,
        ClassStateCatalog.CategoryBoss2,
        ClassStateCatalog.CategoryBoss3,
        ClassStateCatalog.CategoryBoss4,
        ClassStateCatalog.CategoryBoss5
    ];

    /// <summary>
    /// 字段页首次点开时再构建（与首领页一致，避免启动即创建大量 ListView 行）。
    /// </summary>
    private Control CreateLazyCommonFieldsPage()
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Margin = new Padding(0)
        };
        var built = false;
        host.VisibleChanged += (_, _) =>
        {
            if (!host.Visible || built || host.IsDisposed)
            {
                return;
            }

            built = true;
            host.SuspendLayout();
            try
            {
                var page = BuildCommonFieldsPanel();
                page.Dock = DockStyle.Fill;
                host.Controls.Add(page);
            }
            finally
            {
                host.ResumeLayout(true);
            }
        };
        return host;
    }

    private Control BuildCommonFieldsPanel()
    {
        // 两张全宽大卡各挂一个 ListView（分组=分类），视觉对齐首领页两赛季卡片。
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        root.SuspendLayout();
        try
        {
            var playerCard = CreateCommonFieldCard(
                "玩家与资源",
                "common-fields-player-v1",
                EnumerateCatalogFieldRows(PlayerCommonFieldCategories));
            playerCard.Margin = new Padding(0, 0, 0, UiTheme.PageGap);
            root.Controls.Add(playerCard, 0, 0);

            var unitRows = EnumerateCatalogFieldRows(UnitCommonFieldCategories)
                .Concat(EnumerateNameplateFieldRows())
                .ToArray();
            var unitCard = CreateCommonFieldCard("单位字段", "common-fields-unit-v1", unitRows);
            unitCard.Margin = new Padding(0);
            root.Controls.Add(unitCard, 0, 1);
        }
        finally
        {
            root.ResumeLayout(true);
        }

        return root;
    }

    /// <summary>
    /// 从 ClassStateCatalog 按分类顺序枚举字段行；分类显示名走 GetCategoryDisplayName（状态→玩家）。
    /// </summary>
    private static IReadOnlyList<(string Category, string Field)> EnumerateCatalogFieldRows(
        IReadOnlyList<string> categories)
    {
        var rows = new List<(string Category, string Field)>();
        foreach (var category in categories)
        {
            var displayCategory = ClassStateCatalog.GetCategoryDisplayName(category);
            foreach (var option in ClassStateCatalog.GetOptions(category))
            {
                rows.Add((displayCategory, option.Name));
            }
        }

        return rows;
    }

    /// <summary>
    /// 姓名板条件字段来自 NameplateStateLayout（映射格、TTD 别名与槽位模式），不在 ClassStateCatalog 内。
    /// </summary>
    private static IReadOnlyList<(string Category, string Field)> EnumerateNameplateFieldRows()
    {
        const string category = NameplateStateLayout.MappingClassification;
        var rows = new List<(string Category, string Field)>();
        foreach (var name in NameplateStateLayout.MappingFieldNames)
        {
            rows.Add((category, name));
        }

        foreach (var alias in NameplateStateLayout.UnitTtdAliases)
        {
            rows.Add((category, alias.TtdField));
        }

        rows.Add((category, "nameplates.N.存在"));
        rows.Add((category, "nameplates.N.生命值"));
        rows.Add((category, "nameplates.N.距离"));
        rows.Add((category, "nameplates.N.战斗"));
        rows.Add((category, "nameplates.N.TTD"));
        rows.Add((category, "nameplates.N.光环N"));
        return rows;
    }

    private Control CreateCommonFieldCard(
        string title,
        string cacheKey,
        IReadOnlyList<(string Category, string Field)> rows)
    {
        var card = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(UiTheme.CardPadding),
            Margin = new Padding(0)
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        card.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Text,
            BackColor = Color.Transparent,
            Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        }, 0, 0);

        // 名称列 FillRemaining 上限须够大，否则宽窗口右侧会留出空白深灰条。
        // 不用 ShowGroups：.NET ListView 组头无法 OwnerDraw，系统默认呈链接色。
        var list = UiTheme.CreateListView(
            Font,
            cacheKey,
            new UiTheme.ListColumn("分类", 96, 220),
            new UiTheme.ListColumn("字段", 160, 2000, FillRemaining: true));
        list.BackColor = UiTheme.SurfaceRaised;
        list.ShowGroups = false;
        UiTheme.EmphasizeListViewPrimaryColumn(list, Font);

        list.BeginUpdate();
        try
        {
            string? lastCategory = null;
            foreach (var (category, field) in rows)
            {
                // 同分类仅首行显示分类名，避免整列重复；字重/颜色由 Emphasize 统一。
                var showCategory = !string.Equals(category, lastCategory, StringComparison.Ordinal);
                list.Items.Add(new ListViewItem(
                [
                    showCategory ? category : string.Empty,
                    field
                ]));
                lastCategory = category;
            }
        }
        finally
        {
            list.EndUpdate();
        }

        card.Controls.Add(list, 0, 1);
        return card;
    }

    private static string GetEmbeddedResourceName(string resourcePath)
        => $"{typeof(StatusForm).Namespace}.{resourcePath}";

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

    private sealed class AboutScaleIcon : Control
    {
        public AboutScaleIcon()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor
                | ControlStyles.UserPaint,
                true);
            BackColor = Color.Transparent;
            Size = new Size(AboutScaleIconSize, AboutScaleIconSize);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var w = Math.Max(1, Width - 1);
            var h = Math.Max(1, Height - 1);
            using var pen = new Pen(UiTheme.Accent, 1.4F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };

            var fulcrum = new PointF(w * 0.50F, h * 0.18F);
            var leftHang = new PointF(w * 0.20F, h * 0.22F);
            var rightHang = new PointF(w * 0.80F, h * 0.22F);
            graphics.DrawLine(pen, leftHang, fulcrum);
            graphics.DrawLine(pen, fulcrum, rightHang);
            graphics.DrawLine(pen, fulcrum, new PointF(w * 0.50F, h * 0.78F));
            graphics.DrawLine(pen, new PointF(w * 0.32F, h * 0.88F), new PointF(w * 0.68F, h * 0.88F));

            DrawPan(graphics, pen, leftHang, w * 0.18F, h * 0.42F);
            DrawPan(graphics, pen, rightHang, w * 0.18F, h * 0.42F);
        }

        private static void DrawPan(Graphics graphics, Pen pen, PointF hang, float panHalfWidth, float panHeight)
        {
            var panTop = hang.Y + panHeight * 0.28F;
            var panBottom = hang.Y + panHeight;
            graphics.DrawLine(pen, hang, new PointF(hang.X, panTop));
            graphics.DrawLines(pen, [
                new PointF(hang.X - panHalfWidth, panTop),
                new PointF(hang.X, panBottom),
                new PointF(hang.X + panHalfWidth, panTop)]);
        }
    }

    private sealed class AboutLogoBox : Control
    {
        private readonly Bitmap? _logo;
        private readonly int _logoSize;
        private readonly float _opacity;

        public AboutLogoBox(string resourceName, int logoSize, float opacity)
        {
            _logoSize = logoSize;
            _opacity = Math.Clamp(opacity, 0F, 1F);
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor
                | ControlStyles.UserPaint,
                true);
            BackColor = UiTheme.SurfaceRaised;
            ResizeRedraw = true;

            using var stream = typeof(StatusForm).Assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                return;
            }

            using var image = Image.FromStream(stream);
            _logo = new Bitmap(image);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_logo is null || ClientSize.Width <= 1 || ClientSize.Height <= 1)
            {
                return;
            }

            var size = Math.Min(_logoSize, Math.Min(ClientSize.Width, ClientSize.Height));
            var bounds = new Rectangle(
                Math.Max(0, (ClientSize.Width - size) / 2),
                Math.Max(0, (ClientSize.Height - size) / 2),
                size,
                size);

            using var attributes = new ImageAttributes();
            attributes.SetColorKey(Color.Black, Color.FromArgb(24, 24, 24));
            var colorMatrix = new ColorMatrix
            {
                Matrix33 = _opacity
            };
            attributes.SetColorMatrix(
                colorMatrix,
                ColorMatrixFlag.Default,
                ColorAdjustType.Bitmap);

            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.DrawImage(
                _logo,
                bounds,
                0,
                0,
                _logo.Width,
                _logo.Height,
                GraphicsUnit.Pixel,
                attributes);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _logo?.Dispose();
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
        UpdateNameplateList(snapshot);
        UpdateUnitInfoList(snapshot);
    }

    private void UpdateStateList(RenderSnapshot snapshot)
    {
        var items = new List<ListViewItem>();
        if (snapshot.State is null)
        {
            items.Add(new ListViewItem(new[] { "-", "-", "状态", "等待游戏状态" }));
        }
        else
        {
            var index = 0;
            if (!string.IsNullOrWhiteSpace(snapshot.ModuleName))
            {
                index++;
                items.Add(CreateStateRow(index, "匹配模块", snapshot.ModuleName));
            }

            foreach (var (key, value) in snapshot.State.Values)
            {
                if (key is "spells" or "auras" or "group" or "nameplates"
                    || key.StartsWith('$')
                    || snapshot.State.ItemIds.ContainsKey(key))
                {
                    continue;
                }

                index++;
                items.Add(CreateStateRow(index, key, UiTheme.FormatValue(value)));
            }
        }

        ReplaceItems(_stateList, items);
    }

    private static ListViewItem CreateStateRow(int index, string name, string value)
    {
        var category = string.Equals(name, "匹配模块", StringComparison.Ordinal)
            ? "模块"
            : NameplateStateLayout.IsMappingField(name)
                ? NameplateStateLayout.MappingClassification
                : NameplateStateLayout.UnitTtdAliases.Any(alias =>
                      string.Equals(alias.TtdField, name, StringComparison.Ordinal))
                    ? NameplateStateLayout.UnitTtdAliases
                        .First(alias => string.Equals(alias.TtdField, name, StringComparison.Ordinal))
                        .Classification
                    : ClassStateCatalog.GetCategoryDisplayName(ClassStateCatalog.ClassifyField(name));
        return new ListViewItem(new[] { index.ToString(), category, name, value })
        {
            Tag = category
        };
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
                var display = DescribeAuraState(key);
                items.Add(CreateNamedStateRow(index, display, value));
            }
        }

        if (items.Count == 0)
        {
            items.Add(new ListViewItem(new[] { "-", "光环", "-", "-", "无数据" }));
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
        var index = 0;
        if (snapshot.State is not null)
        {
            foreach (var (key, value) in snapshot.State.Spells)
            {
                index++;
                items.Add(CreateNamedStateRow(index, DescribeSpellState(snapshot.State, key), value));
            }

            foreach (var (name, itemId) in snapshot.State.ItemIds)
            {
                if (!snapshot.State.Values.TryGetValue(name, out var value))
                {
                    continue;
                }

                index++;
                items.Add(CreateNamedStateRow(
                    index,
                    new StateFieldDisplay(name, itemId.ToString(), "冷却", itemId, IsItem: true),
                    value));
            }
        }

        if (items.Count == 0)
        {
            items.Add(new ListViewItem(new[] { "-", "技能", "-", "-", "无数据" }));
        }

        ReplaceItems(_spellList, items);
    }

    internal static IReadOnlyList<BossNumberOption> GetBossNumberOptions()
        => BossNumberGroups
            .SelectMany(group => group.Dungeons)
            .SelectMany(dungeon => dungeon.Bosses.Select(boss => new BossNumberOption(
                boss.Number,
                dungeon.Name,
                boss.Name)))
            .DistinctBy(option => option.Number)
            .OrderBy(option => option.Number)
            .ToArray();

    private static StateFieldDisplay DescribeAuraState(string key)
    {
        if (!SpellFieldKey.TryParseAura($"auras.{key}", out _, out var spellId, out var metric))
        {
            return new StateFieldDisplay(key, "-", "-");
        }

        var type = metric switch
        {
            SpellFieldKey.AuraValue => "时间",
            SpellFieldKey.AuraApplications => "层数",
            _ => metric
        };
        return CreateStateFieldDisplay(spellId, type);
    }

    private static StateFieldDisplay DescribeSpellState(GameState state, string key)
    {
        if (!SpellFieldKey.TryParseSpell($"spells.{key}", out var spellId, out var metric))
        {
            return new StateFieldDisplay(key, "-", "-");
        }

        string type;
        if (!state.SpellDisplayTypes.TryGetValue(key, out var configuredType)
            || string.IsNullOrWhiteSpace(configuredType))
        {
            type = metric switch
            {
                SpellFieldKey.SpellCooldown => "冷却",
                SpellFieldKey.SpellChargeCooldown => "充能",
                SpellFieldKey.SpellCount when state.Spells.ContainsKey($"{spellId}.{SpellFieldKey.SpellChargeCooldown}") => "充能层数",
                SpellFieldKey.SpellCount => "施法次数",
                _ => metric
            };
        }
        else
        {
            type = configuredType;
        }

        return CreateStateFieldDisplay(spellId, type);
    }

    private static StateFieldDisplay CreateStateFieldDisplay(long spellId, string type)
    {
        var name = SpellIconCatalog.ResolveSuggestionName(spellId, null) ?? "未知法术";
        return new StateFieldDisplay(name, spellId.ToString(), type, spellId);
    }

    private static ListViewItem CreateNamedStateRow(int index, StateFieldDisplay display, object? value)
    {
        var row = new ListViewItem(new[]
        {
            index.ToString(),
            display.Name,
            display.SpellId,
            display.Type,
            UiTheme.FormatValue(value)
        });
        row.Tag = new StatusListIcon(display.IconId, display.IsItem);
        return row;
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

                var summary = string.Join("  ", unitData.Select(kv =>
                    $"{DisplayPartyFieldName(kv.Key)}: {UiTheme.FormatValue(kv.Value)}"));
                items.Add(new ListViewItem(new[] { $"Unit {unitKey}", summary }));
            }
        }

        ReplaceItems(_partyList, items);
    }

    private void UpdateNameplateList(RenderSnapshot snapshot)
    {
        var items = new List<ListViewItem>();
        for (var slot = 1; slot <= NameplateStateLayout.SlotCount; slot++)
        {
            var key = slot.ToString();
            if (snapshot.State?.Nameplates.TryGetValue(key, out var data) != true || data is null)
            {
                items.Add(new ListViewItem([$"nameplate{slot}", "-"]));
                continue;
            }

            var present = data.TryGetValue("存在", out var presentValue) && presentValue is bool flag && flag;
            if (!present)
            {
                items.Add(new ListViewItem([$"nameplate{slot}", "-"]));
                continue;
            }

            // 同一光环会以 光环N / 名称 / auras.{id}.value 三种键暴露给条件求值, 页面只展示名称那份。
            var summary = string.Join("  ", data
                .Where(pair => pair.Key is not "存在"
                    && !pair.Key.StartsWith("光环", StringComparison.Ordinal)
                    && !SpellFieldKey.TryParseAuraMember(pair.Key, out _, out _))
                .Select(pair => $"{pair.Key}: {UiTheme.FormatValue(pair.Value)}"));

            items.Add(new ListViewItem([$"nameplate{slot}", summary]));
        }

        ReplaceItems(_nameplateList, items);
    }

    private static string DisplayPartyFieldName(string key)
    {
        if (!SpellFieldKey.TryParseAuraMember(key, out var spellId, out _))
        {
            return key;
        }

        return SpellIconCatalog.ResolveSuggestionName(spellId, null) ?? key;
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
            if (current.ToolTipText != next.ToolTipText
                || !Equals(current.Tag, next.Tag))
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
            current.Tag = next.Tag;
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
        private bool _hovered;
        private bool _pressed;
        private bool _isSelected;
        private bool _isDirty;

        public SettingsNavButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = UiTheme.SettingsSidebar;
            ForeColor = UiTheme.SettingsNavText;
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
            graphics.Clear(UiTheme.SettingsSidebar);
            var scale = Math.Max(1f, DeviceDpi / 96f);
            var backgroundColor = _isSelected
                ? UiTheme.SettingsNavSelected
                : _pressed
                    ? UiTheme.SettingsNavPressed
                    : _hovered
                        ? UiTheme.SettingsNavHover
                        : UiTheme.SettingsSidebar;
            using (var background = new SolidBrush(backgroundColor))
            {
                graphics.FillRectangle(background, ClientRectangle);
            }

            if (_isSelected)
            {
                var barWidth = Math.Max(2, (int)Math.Round(2 * scale));
                using var bar = new SolidBrush(UiTheme.SettingsModified);
                graphics.FillRectangle(bar, 0, 0, barWidth, Height);
            }

            var left = (int)Math.Round(20 * scale);
            var rightReserve = (int)Math.Round(22 * scale);
            var textBounds = new Rectangle(left, 0, Math.Max(0, Width - left - rightReserve), Height);
            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                textBounds,
                _isSelected || _hovered ? UiTheme.Text : UiTheme.SettingsNavText,
                TextFormatFlags.Left
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix);

            if (_isDirty)
            {
                var dotSize = Math.Max(5, (int)Math.Round(6 * scale));
                var oldSmoothingMode = graphics.SmoothingMode;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var warning = new SolidBrush(UiTheme.Warning);
                graphics.FillEllipse(
                    warning,
                    Math.Max(0, Width - rightReserve + (rightReserve - dotSize) / 2),
                    (Height - dotSize) / 2,
                    dotSize,
                    dotSize);
                graphics.SmoothingMode = oldSmoothingMode;
            }

            if (Focused && ShowFocusCues)
            {
                var focusBounds = Rectangle.Inflate(ClientRectangle, -2, -2);
                ControlPaint.DrawFocusRectangle(graphics, focusBounds, UiTheme.Text, backgroundColor);
            }
        }
    }

    private sealed class SettingsNavGroup
    {
        public required Label Header { get; init; }

        public List<SettingsNavButton> Items { get; } = [];
    }

    private sealed record BossNumberGroup(string Title, IReadOnlyList<BossDungeon> Dungeons);

    private sealed record BossDungeon(string Name, IReadOnlyList<BossNumberEntry> Bosses);

    private sealed record BossNumberEntry(int Sequence, string Name, int Number);
}
