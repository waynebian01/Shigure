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
    Auras,
    Cooldowns,
    DynamicUnits,
    Party,
    Nameplates,
    Logic,
    Logs,
    BossNumbers,
    Event,
    DungeonSpells,
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

public sealed partial class StatusForm : Form
{
    private readonly Func<GameProfile> _resolveProfile;
    private const string AboutLogoResourcePath = "Assets.arasaka-icon-transparent.png";
    private const int SettingsContentWidth = 1200;
    private const int AboutLogoSize = 220;
    private const float AboutLogoOpacity = 0.55F;
    private const int AboutScaleIconSize = 18;
    /// <summary>独立标题栏高度；页面导航位于左侧栏。</summary>
    private const int TopBarHeight = 52;
    private const int SidebarWidth = 244;
    private const int CompactSidebarWidth = 84;
    private const int MinExpandedSidebarWidth = 208;
    private const int MaxSidebarWidth = 360;
    private const int SidebarCompactThreshold = 136;
    private const int SidebarSplitterWidth = 5;
    private const int SidebarItemHeight = 40;
    /// <summary>导航行左侧独立品牌图标边长（与导航文字垂直对齐）。</summary>
    private const int NavBrandIconSize = 32;
    /// <summary>Windows 风格标题栏按钮宽。</summary>
    private const int ChromeButtonWidth = 46;
    private const int NavItemDirtyReserve = 22;

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
    private readonly List<Label> _sidebarGroups = new();
    private readonly Dictionary<ListView, Label> _listCounts = new();
    private readonly HashSet<SettingsPage> _dirtyPages = new();
    private readonly ToolTip _toolTip = new();
    private RenderSnapshot? _lastSnapshot;
    private bool _hasKnownBounds;
    private bool _autoScrollLog = true;
    private SettingsPage _selectedPage = SettingsPage.General;

    private ListView _stateList = null!;
    private readonly UiDropDown _stateCategoryFilter = new();
    private ListView _auraList = null!;
    private ListView _dynamicUnitList = null!;
    private ListView _spellList = null!;
    private ListView _partyList = null!;
    private ListView _nameplateList = null!;
    private ListView _unitInfoList = null!;
    private TextBox _logTextBox = null!;
    private Panel _contentHost = null!;
    private TableLayoutPanel _bodyLayout = null!;
    private FlowLayoutPanel _sidebarNav = null!;
    private Button _sidebarToggle = null!;
    private int _sidebarExpandedWidth = SidebarWidth;
    private bool _sidebarCollapsed;
    private bool _sidebarDragging;
    private int _sidebarDragStartX;
    private int _sidebarDragStartWidth;
    private Panel _settingsHost = null!;
    private Panel _configHost = null!;
    private Panel _macrosHost = null!;
    private Panel _moduleHost = null!;
    private Panel _aboutHost = null!;
    private Label _aboutModulePathLabel = null!;
    private Label _aboutConfigPathLabel = null!;
    private Button _maximizeButton = null!;
    private bool _usesDwmRoundedCorners;
    private readonly System.Windows.Forms.Timer _roundedCornerResizeTimer;
    private BorderlessFormChrome.EdgeHitTransparentScope? _edgeHitScope;

    internal string SelectedPageKey => _selectedPage.ToString();
    internal int SidebarExpandedWidth => _sidebarExpandedWidth;
    internal bool SidebarCollapsed => _sidebarCollapsed;
    internal event EventHandler? SidebarLayoutChanged;

    internal StatusForm(Func<GameProfile> resolveProfile)
    {
        _resolveProfile = resolveProfile;
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
        _dungeonSpellList?.Invalidate();
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        Text = "设置";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        MinimumSize = new Size(1040, 640);
        Size = new Size(1280, 800);
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.Text;
        ShowInTaskbar = true;
        TopMost = false;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
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

        // 序号列保持定宽；名称列保留原剩余宽度的一半，另一半由其余列均分。
        _stateList = UiTheme.CreateListView(Font, "status-state-v7",
            new UiTheme.ListColumn("#", 28, 28, FixedWidth: true),
            new UiTheme.ListColumn("分类", 56, 2000, FixedWidth: true, RemainingWidthWeight: 1),
            new UiTheme.ListColumn("名称", 40, 2000, FillRemaining: true, RemainingWidthWeight: 2),
            new UiTheme.ListColumn("值", 64, 2000, FixedWidth: true, RemainingWidthWeight: 1));
        _auraList = UiTheme.CreateListView(Font, "status-aura-v6",
            new UiTheme.ListColumn("#", 28, 28, FixedWidth: true),
            new UiTheme.ListColumn("名称", 70, 2000, FillRemaining: true, RemainingWidthWeight: 3),
            new UiTheme.ListColumn("spellId", 64, 2000, FixedWidth: true, RemainingWidthWeight: 1),
            new UiTheme.ListColumn("类型", 48, 2000, FixedWidth: true, RemainingWidthWeight: 1),
            new UiTheme.ListColumn("值", 64, 2000, FixedWidth: true, RemainingWidthWeight: 1));
        // 名称原有的弹性宽度按 60% / 40% 分配，值列保留基础宽度；不套用旧固定列宽缓存。
        _dynamicUnitList = UiTheme.CreateListView(Font,
            new UiTheme.ListColumn("类型", 72, 72, FixedWidth: true),
            new UiTheme.ListColumn("名称", 40, 2000, FillRemaining: true, RemainingWidthWeight: 3),
            new UiTheme.ListColumn("值", 64, 2000, FixedWidth: true, RemainingWidthWeight: 2));
        _spellList = UiTheme.CreateListView(Font, "status-spell-v6",
            new UiTheme.ListColumn("#", 28, 28, FixedWidth: true),
            new UiTheme.ListColumn("名称", 70, 2000, FillRemaining: true, RemainingWidthWeight: 3),
            new UiTheme.ListColumn("spellId", 64, 2000, FixedWidth: true, RemainingWidthWeight: 1),
            new UiTheme.ListColumn("类型", 58, 2000, FixedWidth: true, RemainingWidthWeight: 1),
            new UiTheme.ListColumn("值", 64, 2000, FixedWidth: true, RemainingWidthWeight: 1));

        _partyList = UiTheme.CreateListView(Font, "status-party-v3",
            new UiTheme.ListColumn("单位", 88, 120, FixedWidth: true),
            new UiTheme.ListColumn("生命值", 72, 96),
            new UiTheme.ListColumn("治疗吸收", 88, 112),
            new UiTheme.ListColumn("职责", 64, 88),
            new UiTheme.ListColumn("职业", 96, 160),
            new UiTheme.ListColumn("驱散魔法", 80, 96),
            new UiTheme.ListColumn("驱散诅咒", 80, 96),
            new UiTheme.ListColumn("驱散疾病", 80, 96),
            new UiTheme.ListColumn("驱散中毒", 80, 96),
            new UiTheme.ListColumn("驱散流血", 80, 96),
            new UiTheme.ListColumn("光环 / 其他", 160, 1600, FillRemaining: true));
        _nameplateList = UiTheme.CreateListView(Font, "status-nameplates-v2",
            new UiTheme.ListColumn("单位", 112, 140, FixedWidth: true),
            new UiTheme.ListColumn("生命值", 72, 96),
            new UiTheme.ListColumn("距离", 64, 88),
            new UiTheme.ListColumn("战斗", 64, 88),
            new UiTheme.ListColumn("TTD", 64, 88),
            new UiTheme.ListColumn("仇恨值", 72, 96),
            new UiTheme.ListColumn("施法技能", 88, 120),
            new UiTheme.ListColumn(NameplateStateLayout.CastCountdownField, 120, 160),
            new UiTheme.ListColumn("强化锁喉", 88, 112),
            new UiTheme.ListColumn("光环 / 其他", 160, 1600, FillRemaining: true));
        UiTheme.SetListViewColumnVisible(_nameplateList, NameplateStateLayout.ImprovedGarroteField, false);
        _unitInfoList = UiTheme.CreateListView(Font, "status-unit-info",
            new UiTheme.ListColumn("名称", 180, 320),
            new UiTheme.ListColumn("值", 320, 1400, FillRemaining: true));
        _logTextBox = new UiThemedTextBox
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

        var navShell = BuildNavigationShell();
        var sidebar = BuildSidebar(out var nav);

        _contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding = new Padding(20, 0, 20, 16),
            Margin = new Padding(0)
        };

        AddSidebarGroup(nav, "设置");
        AddNavItem(nav, SettingsPage.General, "通用", "General", _settingsHost);
        AddNavItem(nav, SettingsPage.Config, "配置", "Config", _configHost);
        AddNavItem(nav, SettingsPage.Macros, "宏", "Macros", _macrosHost);
        AddNavItem(nav, SettingsPage.Modules, "模块", "Modules", _moduleHost);
        AddSidebarGroup(nav, "实时数据");
        AddNavItem(nav, SettingsPage.Status, "状态", "Status", BuildStatePage());
        AddNavItem(nav, SettingsPage.Auras, "光环", "capslock", BuildFixedWidthSectionPage("光环", _auraList, "时间与层数"));
        AddNavItem(nav, SettingsPage.Cooldowns, "冷却", "book", BuildFixedWidthSectionPage("冷却", _spellList, "冷却、充能与次数"));
        AddNavItem(nav, SettingsPage.DynamicUnits, "动态单位", "person-gear", BuildFixedWidthSectionPage("动态单位", _dynamicUnitList, "模块运行时计算值"));
        AddNavItem(nav, SettingsPage.Party, "队伍", "Party", BuildFixedWidthSectionPage("队伍成员", _partyList, "实时队伍数据"));
        AddNavItem(nav, SettingsPage.Nameplates, "姓名板", "Nameplates", BuildFixedWidthSectionPage("姓名板", _nameplateList, "实时姓名板数据"));
        AddNavItem(nav, SettingsPage.Logic, "逻辑", "Logic", BuildFixedWidthSectionPage("逻辑信息", _unitInfoList, "当前模块的决策输出"));
        AddNavItem(nav, SettingsPage.Logs, "日志", "Logs", BuildLogPage());
        AddSidebarGroup(nav, "参考");
        AddNavItem(nav, SettingsPage.BossNumbers, "首领", "BossNumbers", CreateLazyBossNumbersPage());
        AddNavItem(nav, SettingsPage.DungeonSpells, "副本技能", "book", CreateLazyDungeonSpellsPage());
        AddNavItem(nav, SettingsPage.Event, "EX 事件", "Event", BuildExBossEventsPage());
        AddNavItem(nav, SettingsPage.BigWigsEvent, "BW 事件", "BigWigsEvent", BuildBigWigsEventsPage());
        AddNavItem(nav, SettingsPage.CommonFields, "字段", "CommonFields", CreateLazyCommonFieldsPage());
        AddSidebarGroup(nav, "其他");
        AddNavItem(nav, SettingsPage.About, "关于", "About", _aboutHost);
        _aboutHost.Controls.Add(BuildAboutPanel());

        _bodyLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        _bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SidebarWidth));
        _bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SidebarSplitterWidth));
        _bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _bodyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _bodyLayout.Controls.Add(sidebar, 0, 0);
        _bodyLayout.Controls.Add(BuildSidebarSplitter(), 1, 0);
        _bodyLayout.Controls.Add(_contentHost, 2, 0);
        root.Controls.Add(navShell, 0, 0);
        root.Controls.Add(_bodyLayout, 0, 1);

        InitializeEmptyLists();
        ResumeLayout(false);
        SelectView(SettingsPage.General);
    }

    private void InitializeEmptyLists()
    {
        ReplaceItems(_stateList, [new ListViewItem(["-", "-", "状态", "等待游戏状态"])]);
        ReplaceItems(_auraList, [new ListViewItem(["-", "光环", "-", "-", "无数据"])]);
        ReplaceItems(_spellList, [new ListViewItem(["-", "冷却", "-", "-", "无数据"])]);
        ReplaceItems(_dynamicUnitList, [new ListViewItem(["-", "动态单位", "等待游戏状态"])]);
        ReplaceItems(_partyList, [CreateUnitStatusRow(_partyList, "队伍", null, false, "无队伍数据")]);
        ReplaceItems(_nameplateList, Enumerable.Range(1, NameplateStateLayout.SlotCount)
            .Select(slot => CreateUnitStatusRow(_nameplateList, $"nameplate{slot}", null, true)).ToArray());
        ReplaceItems(_unitInfoList, [new ListViewItem(["逻辑信息", "无推荐目标"])]);
    }

    private Control BuildNavigationShell()
    {
        // 标题栏只承载品牌和窗口操作，页面入口放在独立侧栏。
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(18, 0, 0, 0),
            Margin = new Padding(0)
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var brandIcon = new PictureBox
        {
            Size = new Size(NavBrandIconSize, NavBrandIconSize),
            MinimumSize = new Size(NavBrandIconSize, NavBrandIconSize),
            MaximumSize = new Size(NavBrandIconSize, NavBrandIconSize),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 10, 0),
            Anchor = AnchorStyles.None,
            TabStop = false,
            AccessibleName = "Shigure"
        };
        brandIcon.Image = LoadNavBrandIcon();
        EnableDrag(brandIcon);

        var brand = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = false,
            BackColor = UiTheme.Surface,
            Margin = new Padding(0),
            Padding = new Padding(0, 10, 0, 10)
        };
        var brandTitle = new Label
        {
            Text = "Shigure",
            AutoSize = true,
            MinimumSize = new Size(0, NavBrandIconSize),
            Margin = new Padding(0, 0, 12, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UiTheme.Text,
            Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
            BackColor = Color.Transparent
        };
        brand.Controls.Add(brandIcon);
        brand.Controls.Add(brandTitle);
        EnableDrag(brand);
        EnableDrag(brandTitle);

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

        shell.Controls.Add(brand, 0, 0);
        shell.Controls.Add(chromeActions, 1, 0);
        EnableDrag(shell);
        UpdateMaximizeButton();
        return shell;
    }

    private Control BuildSidebar(out FlowLayoutPanel nav)
    {
        var cardHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding = new Padding(8),
            Margin = Padding.Empty
        };
        var sidebar = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SettingsNavigation,
            Padding = new Padding(1),
            Margin = Padding.Empty
        };
        cardHost.Controls.Add(sidebar);
        sidebar.Resize += (_, _) =>
        {
            if (sidebar.ClientSize.Width <= 0 || sidebar.ClientSize.Height <= 0)
            {
                return;
            }

            using var shape = UiTheme.CreateRoundedRectanglePath(
                sidebar.ClientRectangle, UiTheme.Scale(sidebar, UiTheme.CardCornerRadius));
            var previous = sidebar.Region;
            sidebar.Region = new Region(shape);
            previous?.Dispose();
        };
        sidebar.Paint += (_, e) =>
        {
            var bounds = new Rectangle(0, 0,
                Math.Max(1, sidebar.ClientSize.Width - 1),
                Math.Max(1, sidebar.ClientSize.Height - 1));
            using var shape = UiTheme.CreateRoundedRectanglePath(
                bounds, UiTheme.Scale(sidebar, UiTheme.CardCornerRadius));
            using var border = new Pen(UiTheme.SettingsNavigationBorder);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawPath(border, shape);
        };

        var viewport = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SettingsNavigation,
            Margin = Padding.Empty
        };
        nav = new UiThemedFlowLayoutPanel
        {
            Dock = DockStyle.None,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = UiTheme.SettingsNavigation,
            Margin = Padding.Empty,
            Padding = new Padding(12, 10, 12, 14)
        };
        _sidebarNav = nav;
        var navPanel = nav;
        void FitItems()
        {
            var width = Math.Max(1, navPanel.ClientSize.Width - navPanel.Padding.Horizontal);
            foreach (Control item in navPanel.Controls)
            {
                if (item.Width != width)
                {
                    item.Width = width;
                }
            }
        }
        nav.Resize += (_, _) => FitItems();
        nav.Layout += (_, _) => FitItems();
        viewport.Controls.Add(nav);
        viewport.Resize += (_, _) => navPanel.SetBounds(
            0, 0,
            viewport.ClientSize.Width,
            viewport.ClientSize.Height);
        sidebar.Controls.Add(viewport);

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 44,
            BackColor = UiTheme.SettingsNavigation,
            Margin = Padding.Empty
        };
        _sidebarToggle = new UiButton
        {
            Size = new Size(40, 40),
            Text = string.Empty,
            AccessibleName = "收起侧栏",
            Cursor = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
            BackColor = UiTheme.SettingsNavigation,
            ForeColor = UiTheme.Muted,
            Margin = Padding.Empty
        };
        _sidebarToggle.FlatAppearance.BorderSize = 0;
        _sidebarToggle.FlatAppearance.MouseOverBackColor = UiTheme.Hover;
        _sidebarToggle.FlatAppearance.MouseDownBackColor = UiTheme.Pressed;
        var toggleHovered = false;
        var togglePressed = false;
        _sidebarToggle.MouseEnter += (_, _) => { toggleHovered = true; _sidebarToggle.Invalidate(); };
        _sidebarToggle.MouseLeave += (_, _) => { toggleHovered = false; togglePressed = false; _sidebarToggle.Invalidate(); };
        _sidebarToggle.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                togglePressed = true;
                _sidebarToggle.Invalidate();
            }
        };
        _sidebarToggle.MouseUp += (_, _) => { togglePressed = false; _sidebarToggle.Invalidate(); };
        _sidebarToggle.Paint += (_, e) =>
        {
            e.Graphics.Clear(togglePressed ? UiTheme.Pressed
                : toggleHovered ? UiTheme.Hover : UiTheme.SettingsNavigation);
            var iconSize = Math.Max(18, (int)Math.Round(18 * DeviceDpi / 96.0));
            UiIconCatalog.Draw(e.Graphics, "window-sidebar",
                new Rectangle((_sidebarToggle.Width - iconSize) / 2,
                    (_sidebarToggle.Height - iconSize) / 2, iconSize, iconSize),
                UiTheme.Muted);
        };
        _sidebarToggle.Click += (_, _) =>
        {
            SetSidebarWidth(_sidebarCollapsed ? _sidebarExpandedWidth : CompactSidebarWidth);
            SidebarLayoutChanged?.Invoke(this, EventArgs.Empty);
        };
        _toolTip.SetToolTip(_sidebarToggle, "收起侧栏");
        header.Controls.Add(_sidebarToggle);
        header.Resize += (_, _) => PositionSidebarToggle();
        navPanel.Resize += (_, _) => PositionSidebarToggle();
        sidebar.Controls.Add(header);
        PositionSidebarToggle();

        return cardHost;
    }

    private Control BuildSidebarSplitter()
    {
        var splitter = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Cursor = Cursors.VSplit,
            Margin = Padding.Empty
        };
        var splitterHovered = false;
        splitter.Paint += (_, e) =>
        {
            if (!splitterHovered && !_sidebarDragging)
            {
                return;
            }

            using var divider = new Pen(UiTheme.Border);
            var x = splitter.ClientSize.Width / 2;
            e.Graphics.DrawLine(divider, x, 0, x, splitter.ClientSize.Height);
        };
        splitter.MouseEnter += (_, _) => { splitterHovered = true; splitter.Invalidate(); };
        splitter.MouseLeave += (_, _) => { splitterHovered = false; splitter.Invalidate(); };
        splitter.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            _sidebarDragging = true;
            splitter.Invalidate();
            _sidebarDragStartX = Cursor.Position.X;
            _sidebarDragStartWidth = (int)_bodyLayout.ColumnStyles[0].Width;
            splitter.Capture = true;
        };
        splitter.MouseMove += (_, _) =>
        {
            if (_sidebarDragging)
            {
                SetSidebarWidth(_sidebarDragStartWidth + Cursor.Position.X - _sidebarDragStartX);
            }
        };
        splitter.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && _sidebarDragging)
            {
                FinishSidebarDrag();
                splitter.Invalidate();
            }
        };
        splitter.MouseCaptureChanged += (_, _) =>
        {
            if (_sidebarDragging)
            {
                FinishSidebarDrag();
                splitter.Invalidate();
            }
        };
        return splitter;
    }

    private void FinishSidebarDrag()
    {
        _sidebarDragging = false;
        var width = (int)_bodyLayout.ColumnStyles[0].Width;
        SetSidebarWidth(width < SidebarCompactThreshold
            ? CompactSidebarWidth
            : Math.Max(MinExpandedSidebarWidth, width));
        SidebarLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetSidebarWidth(int requestedWidth)
    {
        var width = Math.Clamp(requestedWidth, CompactSidebarWidth, MaxSidebarWidth);
        var collapsed = width < SidebarCompactThreshold;
        _bodyLayout.ColumnStyles[0].Width = width;
        _sidebarCollapsed = collapsed;
        if (!collapsed && width >= MinExpandedSidebarWidth)
        {
            _sidebarExpandedWidth = width;
        }

        _sidebarNav.Padding = collapsed
            ? new Padding(8, 10, 8, 14)
            : new Padding(12, 10, 12, 14);
        foreach (var group in _sidebarGroups)
        {
            group.Visible = !collapsed;
        }

        foreach (var (button, _, _) in _navItems)
        {
            button.IsCompact = collapsed;
        }

        _sidebarToggle.AccessibleName = collapsed ? "展开侧栏" : "收起侧栏";
        _toolTip.SetToolTip(_sidebarToggle, _sidebarToggle.AccessibleName);
        _sidebarNav.PerformLayout();
        PositionSidebarToggle();
    }

    private void PositionSidebarToggle()
    {
        if (_sidebarToggle is null || _sidebarNav is null || _sidebarToggle.Parent is not { } header)
        {
            return;
        }

        var scale = Math.Max(1f, _sidebarToggle.DeviceDpi / 96f);
        var iconSize = Math.Max(16, (int)Math.Round(18 * scale));
        var iconCenter = _sidebarCollapsed
            ? _sidebarNav.Padding.Left
                + Math.Max(1, _sidebarNav.ClientSize.Width - _sidebarNav.Padding.Horizontal) / 2
            : _sidebarNav.Padding.Left + (int)Math.Round(14 * scale) + iconSize / 2;
        _sidebarToggle.Left = Math.Clamp(iconCenter - _sidebarToggle.Width / 2,
            0, Math.Max(0, header.ClientSize.Width - _sidebarToggle.Width));
        _sidebarToggle.Top = (header.ClientSize.Height - _sidebarToggle.Height) / 2;
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
        var button = new UiButton
        {
            Text = text,
            AutoSize = false,
            Size = new Size(ChromeButtonWidth, TopBarHeight),
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            FlatStyle = FlatStyle.Flat,
            BackColor = UiTheme.Surface,
            ForeColor = UiTheme.Muted,
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand,
            TabStop = false,
            Font = new Font("Segoe UI Symbol", 10F, FontStyle.Regular)
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.BorderColor = UiTheme.Surface;
        button.FlatAppearance.MouseOverBackColor = isClose
            ? Color.FromArgb(196, 43, 28)
            : UiTheme.Hover;
        button.FlatAppearance.MouseDownBackColor = isClose
            ? Color.FromArgb(153, 27, 21)
            : UiTheme.Pressed;
        var hovered = false;
        var pressed = false;
        button.MouseEnter += (_, _) => { hovered = true; button.Invalidate(); };
        button.MouseLeave += (_, _) => { hovered = false; pressed = false; button.Invalidate(); };
        button.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                pressed = true;
                button.Invalidate();
            }
        };
        button.MouseUp += (_, _) => { pressed = false; button.Invalidate(); };
        button.Paint += (_, e) =>
        {
            e.Graphics.Clear(pressed ? button.FlatAppearance.MouseDownBackColor
                : hovered ? button.FlatAppearance.MouseOverBackColor : UiTheme.Surface);
            TextRenderer.DrawText(e.Graphics, button.Text, button.Font, button.ClientRectangle,
                button.ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        };
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

    private TableLayoutPanel BuildSection(
        string title,
        Control content,
        string subtitle,
        ListView? countListView = null,
        bool scaleHeader = false)
    {
        var section = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(UiTheme.CardPadding),
            Margin = new Padding(0)
        };
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, scaleHeader ? UiTheme.Scale(this, 28) : 28));
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, scaleHeader ? UiTheme.Scale(this, 22) : 22));
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
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, scaleHeader ? UiTheme.Scale(this, 72) : 52));
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
        var viewWidth = Math.Max(1, scrollHost.ClientSize.Width);
        var targetWidth = Math.Min(contentWidth, viewWidth);
        if (content.Width != targetWidth)
        {
            content.Width = targetWidth;
        }

        if (contentHeight is { } height && content.Height != height)
        {
            content.Height = height;
        }

        var left = viewWidth > targetWidth
            ? (viewWidth - targetWidth) / 2
            : 0;
        if (content.Left != left)
        {
            content.Left = left;
        }

        if (content.Top != 0)
        {
            content.Top = 0;
        }

        if (scrollHost.AutoScrollMinSize != Size.Empty)
        {
            scrollHost.AutoScrollMinSize = Size.Empty;
        }
    }

    private Control BuildFixedWidthSectionPage(
        string title,
        Control content,
        string subtitle,
        ListView? countListView = null,
        int? contentWidth = null,
        bool scaleHeader = false)
    {
        var pageWidth = contentWidth ?? SettingsContentWidth;
        var scrollHost = new UiThemedPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = false,
            BackColor = UiTheme.Surface,
            Margin = new Padding(0)
        };

        var section = BuildSection(title, content, subtitle, countListView, scaleHeader);
        section.Dock = DockStyle.None;
        section.Location = Point.Empty;
        section.Width = pageWidth;

        void SyncScrollLayout()
        {
            var viewHeight = scrollHost.ClientSize.Height;
            var height = Math.Max(200, viewHeight);
            SyncCenteredContentLayout(scrollHost, section, pageWidth, height);
        }

        scrollHost.Controls.Add(section);
        scrollHost.Resize += (_, _) => SyncScrollLayout();
        scrollHost.HandleCreated += (_, _) => BeginInvoke(SyncScrollLayout);
        SyncScrollLayout();
        return scrollHost;
    }

    private Control BuildStatePage()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0)
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0)
        };
        toolbar.Controls.Add(new Label
        {
            Text = "分类",
            Size = new Size(52, 36),
            ForeColor = UiTheme.Muted,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 4, 8, 4)
        });
        _stateCategoryFilter.Size = new Size(220, 36);
        _stateCategoryFilter.DropDownWidth = 220;
        _stateCategoryFilter.Margin = new Padding(0, 4, 0, 4);
        UiTheme.StyleComboBox(_stateCategoryFilter);
        _stateCategoryFilter.Items.Add("全部");
        foreach (var category in ClassStateCatalog.TopCategories
                     .Select(ClassStateCatalog.GetCategoryDisplayName)
                     .Concat(new[] { "模块", NameplateStateLayout.MappingClassification })
                     .Distinct(StringComparer.Ordinal))
        {
            _stateCategoryFilter.Items.Add(category);
        }
        _stateCategoryFilter.SelectedIndex = 0;
        _stateCategoryFilter.SelectedIndexChanged += (_, _) =>
        {
            if (_lastSnapshot is { } snapshot)
            {
                UpdateStateList(snapshot);
            }
        };
        toolbar.Controls.Add(_stateCategoryFilter);
        content.Controls.Add(toolbar, 0, 0);
        content.Controls.Add(_stateList, 0, 1);
        return BuildFixedWidthSectionPage("状态", content, "基础字段与当前模块", _stateList);
    }

    private Control BuildLogPage()
    {
        var scrollHost = new UiThemedPanel
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
            Width = SettingsContentWidth
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

    internal void ApplyCachedSidebar(int? expandedWidth, bool? collapsed)
    {
        if (expandedWidth is { } width)
        {
            _sidebarExpandedWidth = Math.Clamp(width, MinExpandedSidebarWidth, MaxSidebarWidth);
        }

        SetSidebarWidth(collapsed == true ? CompactSidebarWidth : _sidebarExpandedWidth);
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

    private void AddSidebarGroup(FlowLayoutPanel nav, string title)
    {
        var group = new Label
        {
            Text = title,
            AutoSize = false,
            Size = new Size(SidebarWidth - 24, 32),
            Margin = new Padding(10, nav.Controls.Count == 0 ? 6 : 16, 0, 2),
            Padding = Padding.Empty,
            ForeColor = UiTheme.Muted,
            BackColor = Color.Transparent,
            Font = new Font(Font.FontFamily, 8.5F, FontStyle.Bold),
            TextAlign = ContentAlignment.BottomLeft
        };
        _sidebarGroups.Add(group);
        nav.Controls.Add(group);
    }

    private void AddNavItem(FlowLayoutPanel nav, SettingsPage page, string text, string iconName, Control view)
    {
        view.Dock = DockStyle.Fill;
        view.Visible = false;
        _contentHost.Controls.Add(view);

        var button = new SettingsNavButton
        {
            Text = text,
            IconName = iconName,
            AutoSize = false,
            Size = new Size(SidebarWidth - 24, SidebarItemHeight),
            Font = new Font(Font.FontFamily, 9.5F, FontStyle.Regular),
            Margin = new Padding(0, 2, 0, 2),
            Cursor = Cursors.Hand,
            TabStop = true,
            AccessibleRole = AccessibleRole.PageTab,
            AccessibleName = text
        };

        button.Click += (_, _) => SelectView(page);
        _toolTip.SetToolTip(button, text);
        _navItems.Add((button, view, page));
        nav.Controls.Add(button);
    }

    private void SelectView(SettingsPage page)
    {
        _selectedPage = page;
        if (page == SettingsPage.About)
        {
            var profile = _resolveProfile();
            UpdateAboutPath(_aboutModulePathLabel, profile.ModuleDirectory);
            UpdateAboutPath(_aboutConfigPathLabel,
                ConfigService.ResolveConfigPath(profile.RuntimeDirectory));
        }
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

    private static Control CreateCenteredCardStack(Control content)
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = false,
            BackColor = UiTheme.Surface,
            Margin = Padding.Empty
        };
        content.Dock = DockStyle.None;
        content.Margin = Padding.Empty;
        host.Controls.Add(content);

        void SyncLayout()
        {
            if (host.ClientSize.Width <= 0 || host.ClientSize.Height <= 0)
            {
                return;
            }

            SyncCenteredContentLayout(host, content, SettingsContentWidth, host.ClientSize.Height);
        }

        host.Resize += (_, _) => SyncLayout();
        host.HandleCreated += (_, _) => SyncLayout();
        SyncLayout();
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

        return CreateCenteredCardStack(root);
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
        var scrollHost = new UiThemedPanel
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
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
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
            MaximumSize = new Size(500, 0),
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
        var profile = _resolveProfile();
        var modulePath = profile.ModuleDirectory;
        var configPath = ConfigService.ResolveConfigPath(profile.RuntimeDirectory);
        _aboutModulePathLabel = AddAboutRow(details, "模块目录", FormatAboutPath(modulePath), modulePath);
        _aboutConfigPathLabel = AddAboutRow(details, "配置目录", FormatAboutPath(configPath), configPath);
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

        var syncingAboutLayout = false;
        void SyncAboutLayout()
        {
            if (syncingAboutLayout || scrollHost.IsDisposed)
            {
                return;
            }

            syncingAboutLayout = true;
            try
            {
                var width = Math.Min(SettingsContentWidth, Math.Max(1, scrollHost.ClientSize.Width));
                if (panel.MinimumSize.Width != width || panel.MaximumSize.Width != width)
                {
                    panel.MaximumSize = Size.Empty;
                    panel.MinimumSize = new Size(width, 0);
                    panel.MaximumSize = new Size(width, 0);
                }
                SyncCenteredContentLayout(scrollHost, panel, SettingsContentWidth);
                foreach (Control card in panel.Controls)
                {
                    if (card.MinimumSize.Width != width || card.MaximumSize.Width != width)
                    {
                        card.MaximumSize = Size.Empty;
                        card.MinimumSize = new Size(width, 0);
                        card.MaximumSize = new Size(width, 0);
                    }
                    if (card.Width != width)
                    {
                        card.Width = width;
                    }
                }
            }
            finally
            {
                syncingAboutLayout = false;
            }
        }

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
    }

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
            MaximumSize = new Size(680, 0),
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

        return CreateCenteredCardStack(root);
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
        rows.Add((category, "nameplates.N.施法技能"));
        rows.Add((category, "nameplates.N.施法(倒计时)"));
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

    private void UpdateAboutPath(Label label, string path)
    {
        label.Text = FormatAboutPath(path);
        _toolTip.SetToolTip(label, path);
    }

    private Label AddAboutRow(TableLayoutPanel panel, string name, string value, string? tooltip = null)
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
        return valueLabel;
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

        var selectedCategory = _stateCategoryFilter.SelectedIndex > 0
            ? _stateCategoryFilter.SelectedItem?.ToString() : null;
        if (snapshot.State is not null && selectedCategory is not null)
        {
            items = items.Where(item => string.Equals(item.Tag as string, selectedCategory, StringComparison.Ordinal))
                .ToList();
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
            items.Add(new ListViewItem(new[] { "-", "冷却", "-", "-", "无数据" }));
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
            items.Add(CreateUnitStatusRow(_partyList, "队伍", null, false, "无队伍数据"));
        }
        else
        {
            for (var i = 1; i <= partyCount; i++)
            {
                var unitKey = i.ToString();
                snapshot.State.Group.TryGetValue(unitKey, out var unitData);
                items.Add(CreateUnitStatusRow(_partyList, $"Unit {unitKey}", unitData, false));
            }
        }

        ReplaceItems(_partyList, items);
    }

    private void UpdateNameplateList(RenderSnapshot snapshot)
    {
        // 字段由当前运行配置的像素布局决定；即使没有可见敌人，空槽位也保留配置字段。
        // 正式服奇袭且配置启用强化锁喉时才有该字段，不受编辑界面版本选择影响。
        var showImprovedGarrote = snapshot.State?.Nameplates.Values.Any(
            plate => plate.ContainsKey(NameplateStateLayout.ImprovedGarroteField)) == true;
        UiTheme.SetListViewColumnVisible(_nameplateList, NameplateStateLayout.ImprovedGarroteField, showImprovedGarrote);
        var items = new List<ListViewItem>();
        for (var slot = 1; slot <= NameplateStateLayout.SlotCount; slot++)
        {
            var key = slot.ToString();
            IReadOnlyDictionary<string, object?>? data = null;
            if (snapshot.State?.Nameplates.TryGetValue(key, out var plate) == true
                && plate.TryGetValue("存在", out var presentValue) && presentValue is true)
            {
                data = plate;
            }
            items.Add(CreateUnitStatusRow(_nameplateList, $"nameplate{slot}", data, true));
        }

        ReplaceItems(_nameplateList, items);
    }

    private static ListViewItem CreateUnitStatusRow(
        ListView listView,
        string unit,
        IReadOnlyDictionary<string, object?>? data,
        bool isNameplate,
        string emptyText = "-")
    {
        var fields = listView.Columns.Cast<ColumnHeader>().Skip(1).SkipLast(1)
            .Select(column => column.Text).ToArray();
        var cells = new List<string> { unit };
        foreach (var field in fields)
        {
            var value = data is not null && data.TryGetValue(field, out var found) ? found : null;
            cells.Add(field == "TTD"
                ? value is int seconds ? seconds.ToString() : "null"
                : DisplayPartyFieldValue(field, value));
        }

        // 固定状态逐列显示；同一姓名板光环的别名只保留名称那份。
        var details = data?.Where(pair => !fields.Contains(pair.Key, StringComparer.Ordinal)
            && pair.Key != "存在"
            && (isNameplate || pair.Key != "驱散")
            && (!isNameplate || (!pair.Key.StartsWith("光环", StringComparison.Ordinal)
                && !SpellFieldKey.TryParseAuraMember(pair.Key, out _, out _))))
            .Select(pair => $"{DisplayPartyFieldName(pair.Key)}: {DisplayPartyFieldValue(pair.Key, pair.Value)}")
            .ToArray();
        cells.Add(details is { Length: > 0 } ? string.Join("  ", details) : emptyText);
        var row = new ListViewItem(cells.ToArray());
        row.ToolTipText = string.Join("  ", listView.Columns.Cast<ColumnHeader>()
            .Where(column => column.Width > 0)
            .Select(column => column.Text == NameplateStateLayout.CastCountdownField
                ? $"{column.Text}: {cells[column.Index]}（单位 0.1 秒，10 ≈ 1 秒）"
                : $"{column.Text}: {cells[column.Index]}"));
        return row;
    }

    private static string DisplayPartyFieldName(string key)
    {
        if (!SpellFieldKey.TryParseAuraMember(key, out var spellId, out var metric))
        {
            return key;
        }

        var name = SpellIconCatalog.ResolveSuggestionName(spellId, null) ?? key;
        return metric == SpellFieldKey.AuraApplications ? name + "层数" : name;
    }

    private static string DisplayPartyFieldValue(string key, object? value)
    {
        if (key == "职业" && value is int classId && classId > 0)
        {
            var name = ClassNames.GetClassAndSpecName(classId, null).ClassName;
            return name is null ? classId.ToString() : $"{name} ({classId})";
        }

        return UiTheme.FormatValue(value);
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

    private sealed class SettingsNavButton : UiButton
    {
        private bool _hovered;
        private bool _pressed;
        private bool _isSelected;
        private bool _isDirty;
        private bool _isCompact;

        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string IconName { get; set; } = string.Empty;

        public SettingsNavButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = UiTheme.SettingsNavigation;
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

        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool IsCompact
        {
            get => _isCompact;
            set
            {
                if (_isCompact == value)
                {
                    return;
                }

                _isCompact = value;
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
            graphics.Clear(UiTheme.SettingsNavigation);
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
                        : UiTheme.SettingsNavigation;
            using (var path = UiTheme.CreateRoundedRectanglePath(bounds, Math.Max(6, (int)Math.Round(8 * scale))))
            using (var background = new SolidBrush(backgroundColor))
            {
                graphics.FillPath(background, path);
                if (_isSelected)
                {
                    using var indicator = new SolidBrush(UiTheme.Accent);
                    graphics.FillRectangle(indicator, 0, Math.Max(5, Height / 4),
                        Math.Max(3, (int)Math.Round(3 * scale)), Math.Max(1, Height / 2));
                }
            }

            graphics.SmoothingMode = oldSmoothingMode;

            var iconSize = Math.Max(16, (int)Math.Round(18 * scale));
            var iconLeft = _isCompact
                ? (Width - iconSize) / 2
                : (int)Math.Round(14 * scale);
            var dirtyReserve = _isDirty
                ? (int)Math.Round(NavItemDirtyReserve * scale)
                : (int)Math.Round(6 * scale);
            var itemColor = _isSelected ? UiTheme.Accent : _hovered ? UiTheme.Text : UiTheme.Muted;
            UiIconCatalog.Draw(graphics, IconName,
                new Rectangle(iconLeft, (Height - iconSize) / 2, iconSize, iconSize), itemColor);
            if (!_isCompact)
            {
                var textLeft = iconLeft + iconSize + (int)Math.Round(12 * scale);
                var textBounds = new Rectangle(
                    textLeft,
                    0,
                    Math.Max(0, Width - textLeft - dirtyReserve),
                    Height);
                TextRenderer.DrawText(
                    graphics,
                    Text,
                    Font,
                    textBounds,
                    _isSelected || _hovered ? UiTheme.Text : UiTheme.Muted,
                    TextFormatFlags.Left
                    | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine
                    | TextFormatFlags.NoPrefix
                    | TextFormatFlags.EndEllipsis);
            }

            if (_isDirty)
            {
                var dotSize = Math.Max(5, (int)Math.Round(6 * scale));
                var reserveLeft = Width - dirtyReserve;
                var dotLeft = _isCompact
                    ? Width - dotSize - Math.Max(6, (int)Math.Round(7 * scale))
                    : reserveLeft + (dirtyReserve - dotSize) / 2;
                using var warning = new SolidBrush(UiTheme.Warning);
                graphics.FillEllipse(
                    warning,
                    Math.Max(0, Math.Min(dotLeft, Width - dotSize)),
                    _isCompact ? Math.Max(5, (int)Math.Round(5 * scale)) : (Height - dotSize) / 2,
                    dotSize,
                    dotSize);
            }

            if (Focused && ShowFocusCues)
            {
                var focusBounds = Rectangle.Inflate(bounds, -(int)Math.Round(3 * scale), -(int)Math.Round(3 * scale));
                ControlPaint.DrawFocusRectangle(graphics, focusBounds, UiTheme.Text, backgroundColor);
            }
        }
    }

    private sealed record BossNumberGroup(string Title, IReadOnlyList<BossDungeon> Dungeons);

    private sealed record BossDungeon(string Name, IReadOnlyList<BossNumberEntry> Bosses);

    private sealed record BossNumberEntry(int Sequence, string Name, int Number);
}
