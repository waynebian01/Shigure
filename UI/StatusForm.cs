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
    Nameplates,
    Logic,
    Logs,
    BossNumbers,
    CommonFields,
    About
}

internal sealed record BossNumberOption(int Number, string Dungeon, string Name);

internal sealed record StateFieldDisplay(string Name, string SpellId, string Type, long IconId = 0, bool IsItem = false);
internal sealed record StatusListIcon(long Id, bool IsItem);

public sealed class StatusForm : Form
{
    private const string AboutLogoResourcePath = "Assets.arasaka-icon-transparent.png";
    private const int AboutCardWidth = 1600;
    private const int SectionCardWidth = 1600;
    private const int AboutLogoSize = 220;
    private const float AboutLogoOpacity = 0.55F;
    private const int BossNumberCardWidth = 400;
    private const int AboutScaleIconSize = 18;

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

    internal string SelectedPageKey => _selectedPage.ToString();

    public StatusForm()
    {
        InitializeComponent();
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
        }

        base.Dispose(disposing);
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

        _stateList = UiTheme.CreateListView(Font, "status-state-v3",
            new UiTheme.ListColumn("#", 28, 28, FixedWidth: true),
            new UiTheme.ListColumn("分类", 56, 88),
            new UiTheme.ListColumn("名称", 40, 200),
            new UiTheme.ListColumn("值", 40, 900, FillRemaining: true));
        _auraList = UiTheme.CreateListView(Font, "status-aura-v3",
            new UiTheme.ListColumn("#", 28, 28, FixedWidth: true),
            new UiTheme.ListColumn("名称", 70, 240, FillRemaining: true),
            new UiTheme.ListColumn("spellId", 64, 100),
            new UiTheme.ListColumn("类型", 48, 80),
            new UiTheme.ListColumn("值", 44, 100));
        _dynamicUnitList = UiTheme.CreateListView(Font, "status-dynamic-unit-v2",
            new UiTheme.ListColumn("类型", 40, 140),
            new UiTheme.ListColumn("名称", 40, 240),
            new UiTheme.ListColumn("值", 40, 900, FillRemaining: true));
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
        AddNavItem(nav, SettingsPage.Party, SettingsNavIcon.Party, "队伍", CreatePageShell("队伍", "当前队伍单位与扫描字段摘要", BuildFixedWidthSectionPage("队伍成员", _partyList, "实时队伍数据")));
        AddNavItem(nav, SettingsPage.Nameplates, SettingsNavIcon.Party, "姓名板", CreatePageShell("姓名板", "20 个敌对姓名板与配置字段", BuildFixedWidthSectionPage("姓名板", _nameplateList, "实时姓名板数据")));
        AddNavItem(nav, SettingsPage.Logic, SettingsNavIcon.Logic, "逻辑", CreatePageShell("逻辑", "运行时推荐目标与调试值", BuildFixedWidthSectionPage("逻辑信息", _unitInfoList, "当前模块的决策输出")));
        AddNavItem(nav, SettingsPage.Logs, SettingsNavIcon.Logs, "日志", CreatePageShell("日志", "运行、模块匹配与施放记录", BuildLogPage()));
        AddNavGroup(nav, "说明");
        AddNavItem(nav, SettingsPage.BossNumbers, SettingsNavIcon.BossNumbers, "首领", CreatePageShell("首领编号", "副本首领的序号、名称与扫描编号", BuildBossNumbersPage()));
        AddNavItem(nav, SettingsPage.CommonFields, SettingsNavIcon.CommonFields, "字段", CreatePageShell("常用字段", "模块条件可用的状态字段参考", BuildCommonFieldsPanel()));
        AddNavGroup(nav, "系统");
        AddNavItem(nav, SettingsPage.About, SettingsNavIcon.About, "关于", CreatePageShell("关于", "应用信息、免责声明、许可证与来源", _aboutHost));
        _aboutHost.Controls.Add(BuildAboutPanel());

        root.Controls.Add(navShell, 0, 0);
        root.Controls.Add(_contentHost, 1, 0);

        InitializeEmptyLists();
        ResumeLayout(false);
        SelectView(SettingsPage.General);
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

    private Control BuildFixedWidthSectionPage(string title, Control content, string subtitle)
    {
        var scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = UiTheme.Surface,
            Margin = new Padding(0)
        };

        var section = BuildSection(title, content, subtitle);
        section.Dock = DockStyle.None;
        section.Location = Point.Empty;
        section.Width = SectionCardWidth;
        section.MinimumSize = new Size(SectionCardWidth, 0);
        section.MaximumSize = new Size(SectionCardWidth, 0);

        void SyncScrollLayout()
        {
            var viewHeight = scrollHost.ClientSize.Height;
            if (SectionCardWidth > scrollHost.ClientSize.Width && !scrollHost.HorizontalScroll.Visible)
            {
                viewHeight = Math.Max(1, viewHeight - SystemInformation.HorizontalScrollBarHeight);
            }

            var height = Math.Max(200, viewHeight);
            var nextSize = new Size(SectionCardWidth, height);
            if (section.Size != nextSize)
            {
                section.Size = nextSize;
            }

            var minSize = new Size(SectionCardWidth, 0);
            if (scrollHost.AutoScrollMinSize != minSize)
            {
                scrollHost.AutoScrollMinSize = minSize;
            }
        }

        scrollHost.Controls.Add(section);
        scrollHost.Resize += (_, _) => SyncScrollLayout();
        scrollHost.HandleCreated += (_, _) => BeginInvoke(SyncScrollLayout);
        SyncScrollLayout();
        return scrollHost;
    }

    private Control BuildLogPage()
    {
        const int logCardWidth = 1600;
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
            Width = logCardWidth,
            MinimumSize = new Size(logCardWidth, 0),
            MaximumSize = new Size(logCardWidth, 0)
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
            if (logCardWidth > scrollHost.ClientSize.Width && !scrollHost.HorizontalScroll.Visible)
            {
                viewHeight = Math.Max(1, viewHeight - SystemInformation.HorizontalScrollBarHeight);
            }

            var height = Math.Max(200, viewHeight);
            var nextSize = new Size(logCardWidth, height);
            if (card.Size != nextSize)
            {
                card.Size = nextSize;
            }

            var minSize = new Size(logCardWidth, 0);
            if (scrollHost.AutoScrollMinSize != minSize)
            {
                scrollHost.AutoScrollMinSize = minSize;
            }
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
            Margin = new Padding(0, 0, 0, 8),
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
        ApplyAboutCardWidth(panel);
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, AboutCardWidth));
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
        ApplyAboutCardWidth(infoCard);
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
        scrollHost.Controls.Add(panel);
        return scrollHost;
    }

    private static void ApplyAboutCardWidth(Control card)
    {
        card.Dock = DockStyle.None;
        card.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        card.Width = AboutCardWidth;
        card.MinimumSize = new Size(AboutCardWidth, 0);
        card.MaximumSize = new Size(AboutCardWidth, 0);
    }

    private static int GetAboutCardInnerWidth()
        => Math.Max(80, AboutCardWidth - UiTheme.CardPadding * 2);

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
        ApplyAboutCardWidth(card);
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

    private const int CommonFieldCardWidth = 800;

    private Control BuildCommonFieldsPanel()
    {
        var contentWidth = CommonFieldCardWidth * 2 + UiTheme.PageGap;
        var scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            AutoScroll = true,
            Margin = new Padding(0),
            AutoScrollMinSize = new Size(contentWidth, 0)
        };

        var fields = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.None,
            Location = Point.Empty,
            BackColor = UiTheme.Surface,
            ColumnCount = 2,
            RowCount = 5,
            Width = contentWidth,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, CommonFieldCardWidth + UiTheme.PageGap));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, CommonFieldCardWidth));
        for (var i = 0; i < 5; i++)
        {
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        fields.Controls.Add(CreateCommonFieldCard(
            "状态",
            [
                "有效性", "战斗时间", "移动", "生命值", "一键辅助", "插入法术", "插入物品",
                "队伍类型", "队伍人数", "首领战", "难度", "英雄天赋", "施法目标",
                "施法技能", "敌人数量", "敌人数-无仇恨", "敌人数-有仇恨",
                "施法(正计时)", "施法(倒计时)", "引导", "蓄力", "蓄力层数",
                "上个技能", "公共冷却"
            ],
            150), 0, 0);
        fields.Controls.Add(CreateCommonFieldCard(
            "特殊",
            [
                "计时器", "循环计时器", "战斗计时(秒)", "战斗计时(分)",
                "酒池", "符文", "姿态", "神圣军备", "自律", "天启骑士数量",
                "英勇打击", "吸血鬼打击", "收割者战刃", "沸点",
                "风暴涌流图腾", "风暴涌流图腾数量", "治疗之泉图腾", "治疗之泉图腾数量"
            ],
            150), 1, 0);
        fields.Controls.Add(CreateCommonFieldCard(
            "能量",
            [
                "法力值", "怒气值", "集中值", "能量值", "符文", "符文能量",
                "星界能量", "漩涡值", "狂乱值", "恶魔之怒", "痛苦值",
                "连击点", "神圣能量", "精华能量", "灵魂碎片", "真气", "增压层数"
            ],
            150), 0, 1);
        fields.Controls.Add(CreateCommonFieldCard(
            "配置开关",
            ["爆发开关", "AOE开关", "输出模式", "爆发药水开关", "延迟"],
            92), 1, 1);
        fields.Controls.Add(CreateCommonFieldCard(
            "物品",
            ["治疗药水", "魔法药水", "治疗石", "鲁莽药水", "圣光潜力"],
            92), 0, 2);
        fields.Controls.Add(CreateCommonFieldCard(
            "目标",
            ["类型", "生命值", "距离", "施法(倒计时)", "施法(正计时)", "施法可打断", "引导", "引导可打断"],
            104), 1, 2);
        fields.Controls.Add(CreateCommonFieldCard(
            "焦点",
            ["类型", "生命值", "距离", "施法(倒计时)", "施法(正计时)", "施法可打断", "引导", "引导可打断"],
            104), 0, 3);
        fields.Controls.Add(CreateCommonFieldCard(
            "鼠标",
            ["类型", "生命值", "距离", "施法(倒计时)", "施法(正计时)", "施法可打断", "引导", "引导可打断"],
            104), 1, 3);
        fields.Controls.Add(CreateCommonFieldCard(
            "宠物",
            ["存在", "生命值"],
            104), 0, 4);
        fields.Controls.Add(CreateCommonFieldCard(
            "姓名板",
            ["nameplates.N.存在", "nameplates.N.生命值", "nameplates.N.距离", "nameplates.N.光环N"],
            104), 1, 4);

        void SyncScrollLayout()
        {
            if (fields.Width != contentWidth)
            {
                fields.Width = contentWidth;
            }

            var minSize = new Size(contentWidth, 0);
            if (scrollHost.AutoScrollMinSize != minSize)
            {
                scrollHost.AutoScrollMinSize = minSize;
            }
        }

        scrollHost.Controls.Add(fields);
        scrollHost.Resize += (_, _) => SyncScrollLayout();
        scrollHost.HandleCreated += (_, _) => BeginInvoke(SyncScrollLayout);
        SyncScrollLayout();
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
            Margin = new Padding(0, 0, 0, UiTheme.PageGap),
            MinimumSize = new Size(CommonFieldCardWidth, minimumHeight),
            MaximumSize = new Size(CommonFieldCardWidth, 0)
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
        for (var slot = 1; slot <= 20; slot++)
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

            var summary = string.Join("  ", data
                .Where(pair => pair.Key is not "存在" && !pair.Key.StartsWith("光环", StringComparison.Ordinal))
                .Select(pair => $"{pair.Key}: {UiTheme.FormatValue(pair.Value)}"));
            var auraValues = data.Where(pair => pair.Key.StartsWith("光环", StringComparison.Ordinal));
            var auraSummary = string.Join("  ", auraValues.Select(pair => $"{pair.Key}: {UiTheme.FormatValue(pair.Value)}"));
            if (!string.IsNullOrWhiteSpace(auraSummary))
            {
                summary = string.IsNullOrWhiteSpace(summary) ? auraSummary : summary + "  " + auraSummary;
            }

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
            UiIconCatalog.Draw(graphics, _icon, iconBounds, iconColor);
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

    }

    private sealed record BossNumberGroup(string Title, IReadOnlyList<BossDungeon> Dungeons);

    private sealed record BossDungeon(string Name, IReadOnlyList<BossNumberEntry> Bosses);

    private sealed record BossNumberEntry(int Sequence, string Name, int Number);
}
