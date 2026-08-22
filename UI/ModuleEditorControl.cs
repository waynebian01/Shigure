using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Shigure;

public sealed class ModuleEditorControl : UserControl
{
    private const string ModuleWebsiteUrl = "https://www.shigure.club";

    private readonly ModuleStore _moduleStore;
    private readonly Func<Task> _runtimeRestartRequested;
    private readonly Func<ModuleDefinition, string?> _captureDependencies;
    private readonly Func<Task> _modulesReloadRequested;
    private readonly string _baseDirectory;
    private ConditionFieldCatalog _fieldCatalog;
    private KeymapCatalog _keymapCatalog;
    private readonly ListBox _moduleList = new();
    private readonly TextBox _nameBox = new();
    private readonly TextBox _authorBox = new();
    private readonly TextBox _recommendedTalentBox = new();
    private readonly ComboBox _classBox = new();
    private readonly ComboBox _specBox = new();
    private readonly ComboBox _partyTypeBox = new();
    private readonly ComboBox _heroTalentBox = new();
    private readonly DataGridView _rulesGrid = new();
    private readonly DataGridView _adjustmentsGrid = new();
    private readonly DataGridView _formulaAdjustmentsGrid = new();
    private readonly DataGridViewComboBoxColumn _spellColumn = new();
    private readonly DataGridViewComboBoxColumn _unitColumn = new();
    private readonly DataGridViewComboBoxColumn _macroConditionColumn = new();
    private ToolStripDropDown? _rulesComboDropDown;
    private readonly DataGridViewComboBoxColumn _adjustmentFieldColumn = new();
    private readonly DataGridViewComboBoxColumn _adjustmentTypeColumn = new();
    private readonly ListView _unitsList = new();
    private readonly Label _pathLabel = new();
    private readonly Label _versionLabel = new();
    private readonly Label _unitsEmptyHint = new();
    private readonly Label _editorEmptyHint = new();
    private readonly ToolTip _pathToolTip = new();
    private Button _saveButton = null!;
    private Button _deleteButton = null!;
    private Button _addButton = null!;
    private readonly ToolTip _rulesGridToolTip = new()
    {
        InitialDelay = 300,
        ReshowDelay = 100,
        AutoPopDelay = 4000,
        ShowAlways = true
    };
    private List<ModuleDefinition> _modules = new();
    private ModuleDefinition? _selectedModule;
    // 当前编辑中模块的动态单位/数量字段(含未保存的新增), 供目标下拉与条件字段使用。
    private readonly List<ModuleUnit> _units = new();
    private readonly List<ModuleCountField> _counts = new();
    private readonly List<ModuleValueAdjustment> _valueAdjustments = new();
    private HashSet<string>? _availableConditionFields;
    private HashSet<string>? _availableGroupConditionFields;
    // 载入时程序化写入"类型"单元格会触发 CellValueChanged; 置真以跳过"按类型清空数值"的联动。
    private bool _suppressAdjustmentTypeChange;
    private bool _moduleCommandInProgress;
    // 规则行拖拽重排: 拖动起始行, 以及拖动中的插入指示位置(显示一条强调线)。
    private int _dragSourceRow = -1;
    private int _dragIndicatorRow = -1;
    private static readonly PartyTypeOption[] PartyTypeOptions =
    [
        new("任意 (*)", null),
        new("单人 (0)", "0"),
        new("团队 (1-40)", "1-40"),
        new("队伍 (46)", "46")
    ];
    private static readonly MatchOption[] ClassOptions = BuildClassOptions();
    private static readonly HashSet<string> NonAuraGroupFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "生命值",
        "职责",
        "驱散",
        "治疗吸收"
    };
    // 条件动态数值"类型"下拉: 决定"数值"可选项的过滤类别, 顺序与界面一致。
    private static readonly (string Text, ConditionFieldCategory Category)[] AdjustmentTypeOptions =
    [
        ("状态", ConditionFieldCategory.State),
        ("技能", ConditionFieldCategory.Spell),
        ("光环", ConditionFieldCategory.Aura),
        ("动态单位", ConditionFieldCategory.DynamicUnit),
        ("动态数值", ConditionFieldCategory.DynamicValue)
    ];

    public ModuleEditorControl(
        ModuleStore moduleStore,
        Func<Task> runtimeRestartRequested,
        Func<ModuleDefinition, string?> captureDependencies,
        Func<Task> modulesReloadRequested,
        string baseDirectory)
    {
        _moduleStore = moduleStore;
        _runtimeRestartRequested = runtimeRestartRequested;
        _captureDependencies = captureDependencies;
        _modulesReloadRequested = modulesReloadRequested;
        _baseDirectory = baseDirectory;
        _fieldCatalog = ConditionFieldCatalog.Load(baseDirectory);
        _keymapCatalog = KeymapCatalog.Load(baseDirectory);
        InitializeComponent();
        LoadModules();
    }

    public void ReloadCatalogs()
    {
        _fieldCatalog = ConditionFieldCatalog.Load(_baseDirectory);
        _keymapCatalog = KeymapCatalog.Load(_baseDirectory);
        // “更新配置”可能刚重建了 keymap；立即刷新当前规则的技能/目标/宏条件下拉，
        // 避免必须切换职业或重启应用后才能看到新解析出的宏条件。
        RefreshKeymapColumns();
        RefreshAdjustmentFieldColumn();
        _rulesGrid.Invalidate();
    }

    private const int ModuleFooterBarHeight = 56;
    private const int ModuleFooterButtonHeight = 36;

    private void InitializeComponent()
    {
        Dock = DockStyle.Fill;
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.Text;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ModuleFooterBarHeight));
        Controls.Add(root);

        root.Controls.Add(BuildSidebar(), 0, 0);
        root.Controls.Add(BuildEditor(), 1, 0);
        root.Controls.Add(BuildSidebarFooter(), 0, 1);
        root.Controls.Add(BuildActionRow(), 1, 1);
    }

    private Control BuildSidebar()
    {
        var sidebar = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            Margin = new Padding(0, 0, 12, 12),
            ColumnCount = 1,
            RowCount = 1
        };
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _moduleList.Dock = DockStyle.Fill;
        UiTheme.StyleListBox(
            _moduleList,
            Font,
            index => index >= 0 && index < _modules.Count
                ? (_modules[index].Match.ClassId, _modules[index].Match.SpecId)
                : (null, null));
        _moduleList.BackColor = UiTheme.SurfaceRaised;
        _moduleList.SelectedIndexChanged += (_, _) => SelectModule(_moduleList.SelectedIndex);
        sidebar.Controls.Add(_moduleList, 0, 0);
        return sidebar;
    }

    private Control BuildSidebarFooter()
    {
        var footer = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 10, 14, 10),
            Margin = new Padding(0, 0, 12, 0),
            ColumnCount = 3,
            RowCount = 1
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var reloadButton = UiTheme.CreateButton("刷新", UiTheme.ButtonKind.Secondary);
        StyleModuleFooterButton(reloadButton);
        reloadButton.Dock = DockStyle.Fill;
        reloadButton.Click += async (_, _) => await RunModuleCommandAsync(_modulesReloadRequested);

        var getModulesButton = UiTheme.CreateButton(
            "获取模块",
            Color.FromArgb(252, 238, 10),
            Color.Black);
        StyleModuleFooterButton(getModulesButton);
        getModulesButton.Dock = DockStyle.Fill;
        getModulesButton.Padding = new Padding(0, 2, 24, 2);
        getModulesButton.FlatAppearance.BorderColor = Color.FromArgb(252, 238, 10);
        getModulesButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 244, 64);
        getModulesButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(220, 207, 8);
        getModulesButton.Paint += (_, e) => UiTheme.DrawExternalLinkIcon(
            e.Graphics,
            getModulesButton.ClientRectangle,
            getModulesButton.Text,
            getModulesButton.Font,
            getModulesButton.ForeColor,
            getModulesButton.DeviceDpi / 96F);
        getModulesButton.Click += (_, _) => OpenModuleWebsite();

        footer.Controls.Add(reloadButton, 0, 0);
        footer.Controls.Add(getModulesButton, 2, 0);
        return footer;
    }

    private static void OpenModuleWebsite()
    {
        try
        {
            Process.Start(new ProcessStartInfo(ModuleWebsiteUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"无法打开模块网站: {ex.Message}",
                "Shigure",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private Control BuildEditor()
    {
        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding = new Padding(0),
            Margin = new Padding(0, 0, 0, 12),
            ColumnCount = 1,
            RowCount = 3
        };
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        editor.Controls.Add(BuildNameRow(), 0, 0);
        editor.Controls.Add(BuildMatchRow(), 0, 1);
        editor.Controls.Add(BuildEditorTabs(), 0, 2);
        return editor;
    }

    private Control BuildEditorTabs()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 12, 0, 0),
            Padding = new Padding(0),
            BackColor = UiTheme.Surface,
            ColumnCount = 1,
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var tabBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(0)
        };
        tabBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (var i = 0; i < 3; i++)
        {
            tabBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3F));
        }

        var contentCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        contentCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        contentCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0)
        };
        contentCard.Controls.Add(contentHost, 0, 0);

        var pages = new[]
        {
            BuildRulesPanel(),
            BuildUnitsPanel(),
            BuildAdjustmentsPanel(),
        };
        foreach (var page in pages)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            page.BackColor = UiTheme.SurfaceRaised;
            contentHost.Controls.Add(page);
        }

        _editorEmptyHint.Text = "请在左侧选择模块, 或点击「新建」创建";
        _editorEmptyHint.Dock = DockStyle.Fill;
        _editorEmptyHint.TextAlign = ContentAlignment.MiddleCenter;
        _editorEmptyHint.ForeColor = UiTheme.Muted;
        _editorEmptyHint.BackColor = UiTheme.SurfaceRaised;
        _editorEmptyHint.Visible = false;
        contentHost.Controls.Add(_editorEmptyHint);
        _editorEmptyHint.BringToFront();

        var tabs = new UiPillTab[3];
        var selectedIndex = -1;

        void SelectTab(int index)
        {
            if (selectedIndex == index)
            {
                return;
            }

            selectedIndex = index;
            for (var i = 0; i < tabs.Length; i++)
            {
                var selected = i == index;
                tabs[i].Selected = selected;
                pages[i].Visible = selected;
                if (selected)
                {
                    pages[i].BringToFront();
                }
            }
        }

        var titles = new[] { "逻辑编辑", "动态单位", "动态数值" };
        for (var i = 0; i < titles.Length; i++)
        {
            var index = i;
            var tab = new UiPillTab(titles[i]);
            tab.Click += (_, _) => SelectTab(index);
            tabs[i] = tab;
            tabBar.Controls.Add(tab, i, 0);
        }

        root.Controls.Add(tabBar, 0, 0);
        root.Controls.Add(contentCard, 0, 1);
        SelectTab(0);
        return root;
    }

    private Control BuildAdjustmentsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14),
            Margin = new Padding(0)
        };

        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        panel.Controls.Add(CreateSectionLabel("条件动态数值"), 0, 0);
        panel.Controls.Add(BuildAdjustmentsGrid(), 0, 1);
        panel.Controls.Add(CreateSectionLabel("公式动态数值"), 0, 2);
        panel.Controls.Add(BuildFormulaAdjustmentsGrid(), 0, 3);

        return panel;
    }

    private Control BuildRulesPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = 1,
            RowCount = 1,
            Padding = new Padding(14),
            Margin = new Padding(0)
        };

        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(BuildRulesGrid(), 0, 0);
        return panel;
    }

    private Control BuildUnitsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(14),
            Margin = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        UiTheme.ConfigureListViewColumns(
            _unitsList,
            Font,
            "module-units",
            new UiTheme.ListColumn("名称", 210, 420),
            new UiTheme.ListColumn("类型", 80, 240),
            new UiTheme.ListColumn("摘要", 160, 2000, FillRemaining: true));
        _unitsList.MultiSelect = false;
        _unitsList.DoubleClick += (_, _) => EditSelectedUnit();
        _unitsList.KeyDown += OnUnitsListKeyDown;

        _unitsEmptyHint.Text = "暂无动态单位 / 数量\n点击右侧「添加」创建";
        _unitsEmptyHint.Dock = DockStyle.Fill;
        _unitsEmptyHint.TextAlign = ContentAlignment.MiddleCenter;
        _unitsEmptyHint.ForeColor = UiTheme.Muted;
        _unitsEmptyHint.BackColor = UiTheme.Surface;
        _unitsEmptyHint.Visible = false;

        // 列表与空状态提示叠放在同一宿主里, 列表为空时显示提示。
        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Margin = new Padding(0) };
        listHost.Controls.Add(_unitsEmptyHint);
        listHost.Controls.Add(_unitsList);
        _unitsEmptyHint.BringToFront();
        panel.Controls.Add(listHost, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(8, 0, 0, 0),
            Padding = new Padding(0)
        };
        buttons.Resize += (_, _) => LayoutUnitActionButtons(buttons);

        var addButton = CreateUnitActionButton("添加", UiTheme.Field, UiTheme.Text, bottomGap: true);
        addButton.Click += (_, _) => AddUnit();

        var editButton = CreateUnitActionButton("编辑", UiTheme.Field, UiTheme.Text, bottomGap: true);
        editButton.Click += (_, _) => EditSelectedUnit();

        var deleteButton = CreateUnitActionButton("删除", UiTheme.Field, UiTheme.Danger, bottomGap: false);
        deleteButton.Click += (_, _) => DeleteSelectedUnit();

        buttons.Controls.Add(addButton);
        buttons.Controls.Add(editButton);
        buttons.Controls.Add(deleteButton);
        panel.Controls.Add(buttons, 1, 0);

        return panel;
    }

    private Control BuildNameRow()
    {
        var row = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            Padding = new Padding(14, 10, 14, 8),
            Margin = new Padding(0, 0, 0, 12)
        };
        // 名称/作者各占剩余宽度的一半, 两个输入框等宽并铺满窗口。
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        row.Controls.Add(CreateLabel("名称"), 0, 0);
        UiTheme.StyleTextBox(_nameBox);
        _nameBox.Dock = DockStyle.Fill;
        row.Controls.Add(_nameBox, 1, 0);

        var authorLabel = CreateLabel("作者");
        authorLabel.Margin = new Padding(10, 0, 0, 0);
        row.Controls.Add(authorLabel, 2, 0);
        UiTheme.StyleTextBox(_authorBox);
        _authorBox.Dock = DockStyle.Fill;
        row.Controls.Add(_authorBox, 3, 0);

        _pathLabel.Dock = DockStyle.Fill;
        _pathLabel.ForeColor = UiTheme.Muted;
        _pathLabel.BackColor = Color.Transparent;
        _pathLabel.TextAlign = ContentAlignment.MiddleLeft;
        _pathLabel.AutoEllipsis = true;
        _pathLabel.TextChanged += (_, _) => _pathToolTip.SetToolTip(_pathLabel, _pathLabel.Text);
        row.Controls.Add(_pathLabel, 0, 1);
        row.SetColumnSpan(_pathLabel, 3);

        // 版本号紧贴窗口右侧, 右对齐显示在"路径"同一行。
        _versionLabel.Dock = DockStyle.Fill;
        _versionLabel.ForeColor = UiTheme.Muted;
        _versionLabel.BackColor = Color.Transparent;
        _versionLabel.TextAlign = ContentAlignment.MiddleRight;
        _versionLabel.AutoEllipsis = true;
        row.Controls.Add(_versionLabel, 3, 1);

        return row;
    }

    private Control BuildMatchRow()
    {
        var matchLabels = new[] { "职业", "专精", "英雄天赋", "队伍类型" };

        var row = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 12,
            RowCount = 2,
            Padding = new Padding(14),
            Margin = new Padding(0)
        };
        foreach (var label in matchLabels)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, MeasureLabelColumnWidth(label, Font)));
            // 下拉框由原来的 25% 缩短到 20%，余下 5% 作为与下一项标签之间的弹性间隔。
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 5));
        }
        // RowCount 会预置 Percent 样式, 必须 Clear 后再设 Absolute, 否则 Add 只追加到末尾不生效。
        row.RowStyles.Clear();
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        ResetClassOptions(_classBox);
        ResetSpecOptions(_specBox, null);
        ResetHeroTalentOptions(_heroTalentBox, null, null);
        _classBox.SelectedIndexChanged += (_, _) =>
        {
            ResetSpecOptions(_specBox, ReadMatchCombo(_classBox));
            ResetHeroTalentOptions(_heroTalentBox, ReadMatchCombo(_classBox), ReadMatchCombo(_specBox));
            RefreshKeymapColumns();
            RefreshAdjustmentFieldColumn();
        };
        _specBox.SelectedIndexChanged += (_, _) =>
        {
            ResetHeroTalentOptions(_heroTalentBox, ReadMatchCombo(_classBox), ReadMatchCombo(_specBox));
            RefreshAdjustmentFieldColumn();
        };

        AddMatchField(row, "职业:", _classBox, 0);
        AddMatchField(row, "专精:", _specBox, 3);
        AddMatchField(row, "英雄天赋:", _heroTalentBox, 6);
        AddMatchField(row, "队伍类型:", _partyTypeBox, 9);

        var recommendedTalentRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            // 推荐天赋整行相对原位置下移 4px，并与上方匹配项保持清晰间距。
            Margin = new Padding(0, 12, 0, 0)
        };
        recommendedTalentRow.RowStyles.Clear();
        recommendedTalentRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        recommendedTalentRow.ColumnStyles.Add(new ColumnStyle(
            SizeType.Absolute,
            MeasureLabelColumnWidth("推荐天赋", Font)));
        recommendedTalentRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var recommendedTalentLabel = CreateLabel("推荐天赋:");
        recommendedTalentLabel.AutoSize = false;
        recommendedTalentLabel.Margin = Padding.Empty;
        recommendedTalentRow.Controls.Add(recommendedTalentLabel, 0, 0);
        UiTheme.StyleTextBox(_recommendedTalentBox);
        _recommendedTalentBox.Dock = DockStyle.None;
        _recommendedTalentBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _recommendedTalentBox.Margin = Padding.Empty;
        recommendedTalentRow.Controls.Add(_recommendedTalentBox, 1, 0);
        row.Controls.Add(recommendedTalentRow, 0, 1);
        row.SetColumnSpan(recommendedTalentRow, 12);

        return row;
    }

    private Control BuildAdjustmentsGrid()
    {
        UiTheme.StyleDataGridView(_adjustmentsGrid);
        _adjustmentsGrid.AllowUserToAddRows = true;
        _adjustmentsGrid.AllowUserToDeleteRows = false;
        _adjustmentsGrid.AllowUserToResizeColumns = true;
        _adjustmentsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;

        _adjustmentsGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Enabled",
            HeaderText = "启用",
            Width = 68,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None
        });

        _adjustmentFieldColumn.Name = "Field";
        _adjustmentFieldColumn.HeaderText = "数值";
        _adjustmentFieldColumn.Width = 260;
        _adjustmentFieldColumn.MinimumWidth = 200;
        _adjustmentFieldColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _adjustmentFieldColumn.FlatStyle = FlatStyle.Flat;
        _adjustmentsGrid.Columns.Add(_adjustmentFieldColumn);

        _adjustmentsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Delta",
            HeaderText = "调整",
            Width = 70,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None
        });
        _adjustmentsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Condition",
            HeaderText = "条件 (点击编辑)",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            ReadOnly = true
        });
        AddDeleteColumn(_adjustmentsGrid);

        // "类型"列加在集合末尾以保留 Rows.Add 的位置参数(启用/数值/调整/条件), 再用 DisplayIndex 显示到"数值"前。
        _adjustmentTypeColumn.Name = "Type";
        _adjustmentTypeColumn.HeaderText = "类型";
        _adjustmentTypeColumn.Width = 140;
        _adjustmentTypeColumn.MinimumWidth = 100;
        _adjustmentTypeColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _adjustmentTypeColumn.FlatStyle = FlatStyle.Flat;
        foreach (var option in AdjustmentTypeOptions)
        {
            _adjustmentTypeColumn.Items.Add(option.Text);
        }
        _adjustmentsGrid.Columns.Add(_adjustmentTypeColumn);
        _adjustmentTypeColumn.DisplayIndex = 1;

        _adjustmentsGrid.CellClick += OnAdjustmentsGridCellClick;
        _adjustmentsGrid.CellPainting += OnAdjustmentsGridCellPainting;
        _adjustmentsGrid.CellValidating += OnAdjustmentsGridCellValidating;
        _adjustmentsGrid.CellValueChanged += OnAdjustmentsGridCellValueChanged;
        _adjustmentsGrid.DataError += (_, e) => e.ThrowException = false;
        _adjustmentsGrid.EditingControlShowing += OnAdjustmentsGridEditingControlShowing;
        _adjustmentsGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_adjustmentsGrid.IsCurrentCellDirty && _adjustmentsGrid.CurrentCell is DataGridViewComboBoxCell or DataGridViewCheckBoxCell)
            {
                _adjustmentsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        RefreshAdjustmentFieldColumn();
        UiTheme.CacheDataGridViewColumnWidths(_adjustmentsGrid, "module-adjustments");
        return _adjustmentsGrid;
    }

    private Control BuildFormulaAdjustmentsGrid()
    {
        UiTheme.StyleDataGridView(_formulaAdjustmentsGrid);
        _formulaAdjustmentsGrid.AllowUserToAddRows = true;
        _formulaAdjustmentsGrid.AllowUserToDeleteRows = false;
        _formulaAdjustmentsGrid.AllowUserToResizeColumns = true;
        _formulaAdjustmentsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;

        _formulaAdjustmentsGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Enabled",
            HeaderText = "启用",
            Width = 68,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None
        });

        _formulaAdjustmentsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Field",
            HeaderText = "数值名称",
            Width = 180,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None
        });

        _formulaAdjustmentsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Formula",
            HeaderText = "公式 (点击编辑)",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            ReadOnly = true
        });
        AddDeleteColumn(_formulaAdjustmentsGrid);
        _formulaAdjustmentsGrid.CellClick += OnFormulaAdjustmentsGridCellClick;
        _formulaAdjustmentsGrid.CellPainting += OnFormulaAdjustmentsGridCellPainting;
        _formulaAdjustmentsGrid.CellEndEdit += OnFormulaAdjustmentsGridCellEndEdit;
        _formulaAdjustmentsGrid.DataError += (_, e) => e.ThrowException = false;
        _formulaAdjustmentsGrid.UserDeletedRow += (_, _) => RefreshAdjustmentFieldColumn();
        _formulaAdjustmentsGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_formulaAdjustmentsGrid.IsCurrentCellDirty
                && _formulaAdjustmentsGrid.CurrentCell is DataGridViewCheckBoxCell)
            {
                _formulaAdjustmentsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        RefreshAdjustmentFieldColumn();
        UiTheme.CacheDataGridViewColumnWidths(_formulaAdjustmentsGrid, "module-formula-adjustments");
        return _formulaAdjustmentsGrid;
    }

    private Control BuildRulesGrid()
    {
        UiTheme.StyleDataGridView(_rulesGrid);
        _rulesGrid.AllowUserToAddRows = true;
        _rulesGrid.AllowUserToDeleteRows = false;
        _rulesGrid.AllowUserToResizeColumns = true;
        _rulesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _rulesGrid.ShowCellToolTips = false;

        // 启用/技能/目标/宏条件列宽度固定可调并缓存; 条件列用 Fill 自动充满剩余窗口。
        _rulesGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Enabled",
            HeaderText = "启用",
            Width = 68,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None
        });
        _rulesGrid.Columns.Add(CreateSpellIconColumn());
        _spellColumn.Name = "Spell";
        _spellColumn.HeaderText = "技能";
        _spellColumn.Width = 150;
        _spellColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _spellColumn.FlatStyle = FlatStyle.Flat;
        _spellColumn.DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton;
        _spellColumn.ReadOnly = true;
        _rulesGrid.Columns.Add(_spellColumn);
        _unitColumn.Name = "Unit";
        _unitColumn.HeaderText = "目标";
        _unitColumn.Width = 150;
        _unitColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _unitColumn.FlatStyle = FlatStyle.Flat;
        _unitColumn.DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton;
        _unitColumn.ReadOnly = true;
        _rulesGrid.Columns.Add(_unitColumn);
        _macroConditionColumn.Name = "MacroCondition";
        _macroConditionColumn.HeaderText = "宏条件";
        _macroConditionColumn.Width = 150;
        _macroConditionColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _macroConditionColumn.FlatStyle = FlatStyle.Flat;
        _macroConditionColumn.DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton;
        _macroConditionColumn.ReadOnly = true;
        _rulesGrid.Columns.Add(_macroConditionColumn);
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Condition",
            HeaderText = "条件 (点击编辑)",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 220,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        AddRuleIconColumn("MoveUp", "▲", "上移");
        AddRuleIconColumn("MoveDown", "▼", "下移");
        AddRuleIconColumn("Copy", "⧉", "复制到下一行");
        AddRuleIconColumn("InsertBlank", "+", "在下一行添加空白条件");
        AddRuleIconColumn("Delete", "×", "删除", UiTheme.Danger);

        // 拖拽手柄列: 加在集合末尾(保持 Rows.Add 的位置参数仍对应 启用/技能/目标/宏条件/条件),
        // 用 DisplayIndex=0 显示到"启用"前面。自绘六点抓手, 按住拖动可调整该条逻辑顺序。
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Drag",
            HeaderText = string.Empty,
            Width = 30,
            MinimumWidth = 30,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Resizable = DataGridViewTriState.False,
            ReadOnly = true
        });
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "RuleNumber",
            HeaderText = "#",
            Width = 48,
            MinimumWidth = 48,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Resizable = DataGridViewTriState.False,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _rulesGrid.Columns["RuleNumber"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _rulesGrid.Columns["RuleNumber"]!.DefaultCellStyle.ForeColor = UiTheme.Muted;
        _rulesGrid.Columns["Drag"]!.DisplayIndex = 0;
        _rulesGrid.Columns["RuleNumber"]!.DisplayIndex = 1;

        _rulesGrid.AllowDrop = true;
        _rulesGrid.CellClick += OnRulesGridCellClick;
        _rulesGrid.CellFormatting += OnRulesGridCellFormatting;
        _rulesGrid.CellPainting += OnRulesGridCellPainting;
        _rulesGrid.CellMouseEnter += OnRulesGridCellMouseEnter;
        _rulesGrid.CellMouseLeave += OnRulesGridCellMouseLeave;
        _rulesGrid.MouseLeave += (_, _) => _rulesGridToolTip.Hide(_rulesGrid);
        _rulesGrid.MouseDown += OnRulesGridMouseDown;
        _rulesGrid.MouseMove += OnRulesGridMouseMove;
        _rulesGrid.DragOver += OnRulesGridDragOver;
        _rulesGrid.DragDrop += OnRulesGridDragDrop;
        _rulesGrid.DragLeave += (_, _) => ClearDragIndicator();
        _rulesGrid.Paint += OnRulesGridPaint;
        _rulesGrid.DataError += (_, e) => e.ThrowException = false;
        _rulesGrid.CellValueChanged += OnRulesGridCellValueChanged;
        _rulesGrid.HandleCreated += (_, _) => SetCompactRulePrefixColumns();
        SetCompactRulePrefixColumns();
        RefreshKeymapColumns();
        UiTheme.CacheDataGridViewColumnWidths(_rulesGrid, "module-rules");

        return _rulesGrid;
    }

    // 规则表格最左侧的拖拽手柄和编号列只承载结构信息，宽度各缩短为原可读性保护宽度的一半。
    private void SetCompactRulePrefixColumns()
    {
        const int compactWidth = 36;
        foreach (var name in new[] { "Drag", "RuleNumber" })
        {
            var column = _rulesGrid.Columns[name];
            if (column is null)
            {
                continue;
            }

            var width = UiTheme.Scale(_rulesGrid, compactWidth);
            column.MinimumWidth = width;
            column.Width = width;
        }
    }

    private void AddRuleIconColumn(string name, string icon, string tooltip, Color? foreColor = null)
    {
        var column = new DataGridViewButtonColumn
        {
            Name = name,
            HeaderText = string.Empty,
            Text = icon,
            UseColumnTextForButtonValue = true,
            Width = 32,
            MinimumWidth = 32,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Resizable = DataGridViewTriState.False,
            FlatStyle = FlatStyle.Flat
        };
        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        column.DefaultCellStyle.ForeColor = foreColor ?? UiTheme.Muted;
        column.DefaultCellStyle.SelectionForeColor = foreColor ?? UiTheme.Text;
        _rulesGrid.Columns.Add(column);
    }

    private static DataGridViewImageColumn CreateSpellIconColumn()
        => new()
        {
            Name = "SpellIcon",
            HeaderText = "图标",
            Width = 54,
            MinimumWidth = 54,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            ImageLayout = DataGridViewImageCellLayout.Zoom,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                NullValue = null,
                BackColor = UiTheme.Surface
            }
        };

    // 两个动态数值表共用的红色 "×" 删除列。
    private static void AddDeleteColumn(DataGridView grid)
    {
        grid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "Delete",
            HeaderText = string.Empty,
            Text = "×",
            ToolTipText = "删除",
            UseColumnTextForButtonValue = true,
            Width = 32,
            MinimumWidth = 32,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Resizable = DataGridViewTriState.False,
            FlatStyle = FlatStyle.Flat
        });

        grid.Columns["Delete"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        grid.Columns["Delete"]!.DefaultCellStyle.ForeColor = UiTheme.Danger;
    }

    /// <summary>
    /// 按当前选中职业的 keymap 重建“技能/目标/宏条件”下拉选项。
    /// 技能去重(同名技能只出现一次), unit 去重升序; 首项留空表示不填。
    /// 已有行里不在 keymap 中的旧值会补录为额外选项, 避免数据丢失。
    /// </summary>
    private void RefreshKeymapColumns()
    {
        var classId = ReadMatchCombo(_classBox);

        _spellColumn.Items.Clear();
        _spellColumn.Items.Add(string.Empty);
        _spellColumn.Items.Add(ModuleSpecialActions.PauseSpell);
        _spellColumn.Items.Add(ModuleSpecialActions.FailedSpell);
        _spellColumn.Items.Add(ModuleSpecialActions.OneKeySpell);
        foreach (var spell in _keymapCatalog.GetSpells(classId))
        {
            if (!_spellColumn.Items.Contains(spell))
            {
                _spellColumn.Items.Add(spell);
            }
        }

        // 列级 unit 选项作为新行(尚未选技能)的默认全集; 已有行用单元格级选项按技能联动。
        _unitColumn.Items.Clear();
        _unitColumn.Items.Add(string.Empty);
        foreach (var unit in _keymapCatalog.GetUnits(classId))
        {
            _unitColumn.Items.Add(ReservedUnit.ToDisplayText(unit));
        }

        _macroConditionColumn.Items.Clear();
        _macroConditionColumn.Items.Add(string.Empty);

        foreach (DataGridViewRow row in _rulesGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            EnsureComboItem(_spellColumn, row.Cells["Spell"].Value);
            UpdateUnitCellItems(row);
            UpdateMacroConditionCellItems(row);
        }
    }

    /// <summary>
    /// 按该行当前选中的技能, 把"目标"单元格的可选 unit 重建为该技能在 keymap 中实际配置过的值。
    /// 旧值若不在新选项内则补录保留; 若是技能切换导致的非法值则清空。
    /// </summary>
    private void UpdateUnitCellItems(DataGridViewRow row)
    {
        if (row.IsNewRow || row.Cells["Unit"] is not DataGridViewComboBoxCell cell)
        {
            return;
        }

        RebuildUnitCell(row, cell.Value?.ToString());
    }

    /// <summary>
    /// 重建"目标"单元格选项并写入目标值。选项 = 当前技能在 keymap 中的 unit 集合。
    /// desiredValue 合法则保留; 自定义技能(keymap 无该技能)保留旧值; 否则清空。
    /// </summary>
    private void RebuildUnitCell(DataGridViewRow row, string? desiredValue)
    {
        if (row.IsNewRow || row.Cells["Unit"] is not DataGridViewComboBoxCell cell)
        {
            return;
        }

        var spell = row.Cells["Spell"].Value?.ToString();
        cell.Items.Clear();
        cell.Items.Add(string.Empty);

        if (ModuleSpecialActions.IsPauseSpell(spell))
        {
            cell.Value = string.Empty;
            return;
        }

        if (ModuleSpecialActions.IsOneKeySpell(spell))
        {
            var noTarget = ReservedUnit.ToDisplayText(ReservedUnit.None);
            cell.Items.Add(noTarget);
            cell.Value = noTarget;
            return;
        }

        var classId = ReadMatchCombo(_classBox);
        var allowed = ModuleSpecialActions.IsFailedSpell(spell)
            ? _keymapCatalog.GetUnitsForSpells(classId, _keymapCatalog.GetFailedSpellNames(classId))
            : _keymapCatalog.GetUnitsForSpell(classId, spell);

        foreach (var unit in allowed)
        {
            cell.Items.Add(ReservedUnit.ToDisplayText(unit));
        }

        // 动态单位与技能无关, 始终可选; 放在 keymap 编号之后。
        foreach (var unit in _units)
        {
            if (!string.IsNullOrWhiteSpace(unit.Name) && !cell.Items.Contains(unit.Name))
            {
                cell.Items.Add(unit.Name);
            }
        }

        if (string.IsNullOrEmpty(desiredValue))
        {
            cell.Value = string.Empty;
        }
        else if (cell.Items.Contains(desiredValue))
        {
            // keymap 编号或动态单位名(已在上面加入), 直接保留。
            cell.Value = desiredValue;
        }
        else if (allowed.Count == 0)
        {
            // 该技能不在 keymap(自定义技能), 保留旧值不强制清空。
            cell.Items.Add(desiredValue);
            cell.Value = desiredValue;
        }
        else
        {
            // 技能切换导致旧目标非法, 清空。
            cell.Value = string.Empty;
        }
    }

    private void UpdateMacroConditionCellItems(DataGridViewRow row)
    {
        if (row.IsNewRow || row.Cells["MacroCondition"] is not DataGridViewComboBoxCell cell)
        {
            return;
        }

        RebuildMacroConditionCell(row, cell.Value?.ToString());
    }

    /// <summary>
    /// 按当前技能与目标重建“宏条件”选项。只有一个非空条件时自动选中；
    /// 自定义技能或动态单位没有 keymap 条目时保留已有值。
    /// </summary>
    private void RebuildMacroConditionCell(DataGridViewRow row, string? desiredValue)
    {
        if (row.IsNewRow || row.Cells["MacroCondition"] is not DataGridViewComboBoxCell cell)
        {
            return;
        }

        var desired = MacroConditionText.ToDisplayText(desiredValue);
        var spell = row.Cells["Spell"].Value?.ToString();
        var unitText = row.Cells["Unit"].Value?.ToString();
        var unit = ReservedUnit.ParseDisplayText(unitText);
        var allowed = unit is null || string.IsNullOrWhiteSpace(spell)
            ? (IReadOnlyList<string>)[]
            : _keymapCatalog.GetMacroConditions(ReadMatchCombo(_classBox), spell, unit);

        cell.Items.Clear();
        cell.Items.Add(string.Empty);
        foreach (var condition in allowed)
        {
            var displayCondition = MacroConditionText.ToDisplayText(condition);
            if (!cell.Items.Contains(displayCondition))
            {
                cell.Items.Add(displayCondition);
            }
        }

        if (desired.Length > 0 && cell.Items.Contains(desired))
        {
            cell.Value = desired;
        }
        else if (desired.Length > 0 && allowed.Count == 0)
        {
            cell.Items.Add(desired);
            cell.Value = desired;
        }
        else
        {
            var nonEmptyConditions = allowed
                .Select(MacroConditionText.ToDisplayText)
                .Where(condition => !string.IsNullOrWhiteSpace(condition))
                .ToList();
            cell.Value = nonEmptyConditions.Count == 1 && allowed.Count == 1
                ? nonEmptyConditions[0]
                : string.Empty;
        }
    }

    private void OnRulesGridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        var columnName = _rulesGrid.Columns[e.ColumnIndex].Name;
        // 技能改变时联动刷新该行"目标"，技能或目标改变时再刷新"宏条件"。
        if (columnName == "Spell")
        {
            _rulesGrid.Rows[e.RowIndex].Cells["SpellIcon"].Value =
                SpellIconCatalog.Get(CellText(_rulesGrid.Rows[e.RowIndex], "Spell"));
            UpdateUnitCellItems(_rulesGrid.Rows[e.RowIndex]);
            UpdateMacroConditionCellItems(_rulesGrid.Rows[e.RowIndex]);
        }
        else if (columnName == "Unit")
        {
            UpdateMacroConditionCellItems(_rulesGrid.Rows[e.RowIndex]);
        }
    }

    private static void EnsureComboItem(DataGridViewComboBoxColumn column, object? value)
    {
        var text = value?.ToString();
        if (!string.IsNullOrEmpty(text) && !column.Items.Contains(text))
        {
            column.Items.Add(text);
        }
    }

    private void RefreshAdjustmentFieldColumn()
    {
        _adjustmentFieldColumn.Items.Clear();
        _adjustmentFieldColumn.Items.Add(string.Empty);
        foreach (var field in BuildAdjustmentFields())
        {
            if (!_adjustmentFieldColumn.Items.Contains(field.Name))
            {
                _adjustmentFieldColumn.Items.Add(field.Name);
            }
        }

        foreach (DataGridViewRow row in _adjustmentsGrid.Rows)
        {
            if (!row.IsNewRow)
            {
                EnsureComboItem(_adjustmentFieldColumn, row.Cells["Field"].Value);
                // 字段集合可能因职业/专精/动态单位变化, 按该行"类型"重建过滤后的单元格选项。
                RebuildAdjustmentFieldCell(row, row.Cells["Field"].Value?.ToString(), keepCustom: true);
            }
        }

        foreach (DataGridViewRow row in _formulaAdjustmentsGrid.Rows)
        {
            if (!row.IsNewRow)
            {
                EnsureComboItem(_adjustmentFieldColumn, row.Cells["Field"].Value);
            }
        }

        // 动态数值也是可用于条件的字段；新增、改名或删除后立即刷新规则行的缺失字段提示。
        InvalidateConditionFieldValidation();
    }

    // 按该行选中的"类型"重建"数值"单元格的可选项 = 该类别下的字段。
    // desiredValue 为 null 时取单元格现值; 命中过滤后选项则保留, 否则: keepCustom 时补录为自定义项(载入旧数据), 反之清空(用户切换类型)。
    private void RebuildAdjustmentFieldCell(DataGridViewRow row, string? desiredValue, bool keepCustom)
    {
        if (row.IsNewRow || row.Cells["Field"] is not DataGridViewComboBoxCell cell)
        {
            return;
        }

        desiredValue ??= cell.Value?.ToString();
        var category = ReadAdjustmentType(row);

        cell.Items.Clear();
        cell.Items.Add(string.Empty);
        foreach (var field in BuildAdjustmentFields())
        {
            if ((category is null || field.Category == category) && !cell.Items.Contains(field.Name))
            {
                cell.Items.Add(field.Name);
            }
        }

        if (!string.IsNullOrEmpty(desiredValue) && cell.Items.Contains(desiredValue))
        {
            cell.Value = desiredValue;
        }
        else if (!string.IsNullOrEmpty(desiredValue) && keepCustom)
        {
            cell.Items.Add(desiredValue);
            cell.Value = desiredValue;
        }
        else
        {
            cell.Value = string.Empty;
        }
    }

    // 载入旧数据时: 由字段名推断类别, 写入"类型"单元格并重建"数值"选项(保留原值, 含自定义)。
    private void ApplyAdjustmentRowType(DataGridViewRow row, string field)
    {
        _suppressAdjustmentTypeChange = true;
        try
        {
            row.Cells["Type"].Value = AdjustmentTypeText(ResolveAdjustmentCategory(field));
        }
        finally
        {
            _suppressAdjustmentTypeChange = false;
        }

        RebuildAdjustmentFieldCell(row, field, keepCustom: true);
    }

    private static ConditionFieldCategory? ReadAdjustmentType(DataGridViewRow row)
    {
        var text = CellText(row, "Type");
        foreach (var option in AdjustmentTypeOptions)
        {
            if (string.Equals(option.Text, text, StringComparison.Ordinal))
            {
                return option.Category;
            }
        }

        // 未选类型 = 不过滤, 显示全部字段。
        return null;
    }

    private static string AdjustmentTypeText(ConditionFieldCategory category)
    {
        foreach (var option in AdjustmentTypeOptions)
        {
            if (option.Category == category)
            {
                return option.Text;
            }
        }

        return AdjustmentTypeOptions[0].Text;
    }

    // 优先按目录里的字段类别判定; 目录外的自定义字段按 auras./spells. 前缀兜底, 其余归为动态数值。
    private ConditionFieldCategory ResolveAdjustmentCategory(string field)
    {
        var name = field?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return ConditionFieldCategory.State;
        }

        var match = BuildAdjustmentFields()
            .FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        if (match is not null)
        {
            return match.Category;
        }

        if (name.StartsWith("auras.", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("aura.", StringComparison.OrdinalIgnoreCase))
        {
            return ConditionFieldCategory.Aura;
        }

        if (name.StartsWith("spells.", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("spell.", StringComparison.OrdinalIgnoreCase))
        {
            return ConditionFieldCategory.Spell;
        }

        return ConditionFieldCategory.DynamicValue;
    }

    private void OnAdjustmentsGridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_suppressAdjustmentTypeChange || e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        // 用户切换"类型": 重建"数值"选项, 仅保留仍属于该类型的现值, 否则清空让其重选。
        if (_adjustmentsGrid.Columns[e.ColumnIndex].Name == "Type")
        {
            RebuildAdjustmentFieldCell(_adjustmentsGrid.Rows[e.RowIndex], null, keepCustom: false);
        }

        if (_adjustmentsGrid.Columns[e.ColumnIndex].Name == "Field")
        {
            InvalidateConditionFieldValidation();
        }
    }

    private IReadOnlyList<ConditionField> BuildAdjustmentFields()
    {
        var fields = new List<ConditionField>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in _fieldCatalog.GetFields(ReadMatchCombo(_classBox), ReadMatchCombo(_specBox)))
        {
            // 状态仅取可加减的整数字段; 技能/光环原样收录, 供"类型"筛选。
            if (field.Category == ConditionFieldCategory.State && field.Type == ConditionFieldType.Int)
            {
                AddAdjustmentField(fields, seen, field.Name, field.DisplayName, ConditionFieldCategory.State);
            }
            else if (field.Category is ConditionFieldCategory.Spell or ConditionFieldCategory.Aura)
            {
                AddAdjustmentField(fields, seen, field.Name, field.DisplayName, field.Category);
            }
        }

        // 已定义的动态单位生命值和数量拥有明确类别，必须先于动态数值目标加入；
        // 否则同名目标会被 seen 抢先登记成错误类别，载入时无法正确回填“类型”。
        foreach (var unit in _units)
        {
            if (!string.IsNullOrWhiteSpace(unit.HealthName))
            {
                AddAdjustmentField(fields, seen, unit.HealthName, $"{unit.HealthName} (生命值)", ConditionFieldCategory.DynamicUnit);
            }
        }

        foreach (var count in _counts)
        {
            if (!string.IsNullOrWhiteSpace(count.Name))
            {
                AddAdjustmentField(fields, seen, count.Name, $"人数: {count.Name}", ConditionFieldCategory.DynamicValue);
            }
        }

        // 公式结果和其它不属于状态/技能/光环/动态单位的命名目标都是动态数值。
        foreach (var fieldName in GetAdjustmentTargetFields())
        {
            AddAdjustmentField(fields, seen, fieldName, $"{fieldName} (动态数值)", ConditionFieldCategory.DynamicValue);
        }

        return fields;
    }

    private IEnumerable<string> GetAdjustmentTargetFields()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (DataGridViewRow row in _adjustmentsGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            if (TryGetAdjustmentField(CellText(row, "Field"), null, out var field) && seen.Add(field))
            {
                yield return field;
            }
        }

        foreach (DataGridViewRow row in _formulaAdjustmentsGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            if (TryGetAdjustmentField(CellText(row, "Field"), CellText(row, "Formula"), out var field) && seen.Add(field))
            {
                yield return field;
            }
        }
    }

    private static bool TryGetAdjustmentField(string? fieldText, string? formulaText, out string field)
    {
        field = fieldText?.Trim() ?? string.Empty;
        if (field.Length > 0)
        {
            return true;
        }

        return FormulaEvaluator.TrySplitAssignment(formulaText, out field, out _);
    }

    private static void AddAdjustmentField(
        List<ConditionField> fields,
        HashSet<string> seen,
        string name,
        string displayName,
        ConditionFieldCategory category)
    {
        if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
        {
            return;
        }

        fields.Add(new ConditionField(name, displayName, ConditionFieldType.Int, category));
    }

    private void AddUnit()
    {
        using var editor = new UnitEditorForm(GetAuraFields(), GetThresholdFields(), CollectTakenNames(), null, null);
        if (editor.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        if (editor.ResultUnit is { } unit)
        {
            _units.Add(unit);
        }
        else if (editor.ResultCount is { } count)
        {
            _counts.Add(count);
        }

        RefreshUnitsList();
        RefreshUnitDependentUi();
        RefreshAdjustmentFieldColumn();
    }

    private void EditSelectedUnit()
    {
        var (kind, index) = GetSelectedUnitRef();
        if (kind == UnitRowKind.None)
        {
            return;
        }

        var existingUnit = kind == UnitRowKind.Unit ? _units[index] : null;
        var existingCount = kind == UnitRowKind.Count ? _counts[index] : null;
        var ownName = existingUnit?.Name ?? existingCount?.Name;
        var ownHealthName = existingUnit?.HealthName;

        using var editor = new UnitEditorForm(GetAuraFields(), GetThresholdFields(), CollectTakenNames(ownName, ownHealthName), existingUnit, existingCount);
        if (editor.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        // 类别可能在编辑中改变(单位↔数量), 先移除原项再按结果加入。
        if (kind == UnitRowKind.Unit)
        {
            _units.RemoveAt(index);
        }
        else
        {
            _counts.RemoveAt(index);
        }

        if (editor.ResultUnit is { } unit)
        {
            _units.Add(unit);
        }
        else if (editor.ResultCount is { } count)
        {
            _counts.Add(count);
        }

        RefreshUnitsList();
        RefreshUnitDependentUi();
        RefreshAdjustmentFieldColumn();
    }

    private void DeleteSelectedUnit()
    {
        var (kind, index) = GetSelectedUnitRef();
        if (kind == UnitRowKind.None)
        {
            return;
        }

        if (kind == UnitRowKind.Unit)
        {
            _units.RemoveAt(index);
        }
        else
        {
            _counts.RemoveAt(index);
        }

        RefreshUnitsList();
        RefreshUnitDependentUi();
        RefreshAdjustmentFieldColumn();
    }

    // ListView 行顺序: 先全部单位, 再全部数量。把选中行映射回对应列表索引。
    private (UnitRowKind Kind, int Index) GetSelectedUnitRef()
    {
        if (_unitsList.SelectedIndices.Count == 0)
        {
            return (UnitRowKind.None, -1);
        }

        var row = _unitsList.SelectedIndices[0];
        if (row < _units.Count)
        {
            return (UnitRowKind.Unit, row);
        }

        var countIndex = row - _units.Count;
        return countIndex < _counts.Count ? (UnitRowKind.Count, countIndex) : (UnitRowKind.None, -1);
    }

    private void RefreshUnitsList()
    {
        _unitsList.BeginUpdate();
        _unitsList.Items.Clear();
        foreach (var unit in _units)
        {
            var name = string.IsNullOrWhiteSpace(unit.HealthName) ? unit.Name : $"{unit.Name} / {unit.HealthName}";
            var summary = UnitSummary.Describe(unit);
            _unitsList.Items.Add(new ListViewItem([name, "单位", summary]) { ToolTipText = $"{name}\n{summary}" });
        }

        foreach (var count in _counts)
        {
            var summary = UnitSummary.Describe(count);
            _unitsList.Items.Add(new ListViewItem([count.Name, "数量", summary]) { ToolTipText = $"{count.Name}\n{summary}" });
        }

        _unitsList.EndUpdate();
        _unitsEmptyHint.Visible = _unitsList.Items.Count == 0;
    }

    // 单位/数量增删改后, 刷新各规则行"目标"下拉以反映最新的动态单位名。
    private void RefreshUnitDependentUi()
    {
        foreach (DataGridViewRow row in _rulesGrid.Rows)
        {
            if (!row.IsNewRow)
            {
                UpdateUnitCellItems(row);
            }
        }

        // 动态单位名、生命值名和数量名都会参与条件字段校验。
        InvalidateConditionFieldValidation();
    }

    private IReadOnlyList<string> GetAuraFields()
    {
        return _fieldCatalog
            .GetGroupFields(ReadMatchCombo(_classBox), ReadMatchCombo(_specBox))
            .Select(field => field.Name)
            .Where(name => !NonAuraGroupFields.Contains(name))
            .ToList();
    }

    private IReadOnlyList<string> GetThresholdFields()
    {
        // 阈值字段仅取状态/动态单位/动态数值(可加减数值), 排除技能/光环字段。
        return BuildAdjustmentFields()
            .Where(field => field.Category is ConditionFieldCategory.State
                or ConditionFieldCategory.DynamicUnit
                or ConditionFieldCategory.DynamicValue)
            .Select(field => field.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
    }

    // 名称查重集合: 其它单位/数量(含生命值名) + 当前职业/专精的状态字段与 group 字段; 排除正在编辑项自身的名称。
    private IReadOnlyCollection<string> CollectTakenNames(params string?[] ownNames)
    {
        var classId = ReadMatchCombo(_classBox);
        var specId = ReadMatchCombo(_specBox);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var unit in _units)
        {
            taken.Add(unit.Name);
            if (!string.IsNullOrWhiteSpace(unit.HealthName))
            {
                taken.Add(unit.HealthName);
            }
        }

        foreach (var count in _counts)
        {
            taken.Add(count.Name);
        }

        foreach (var field in _fieldCatalog.GetFields(classId, specId))
        {
            taken.Add(field.Name);
        }

        foreach (var field in _fieldCatalog.GetGroupFields(classId, specId))
        {
            taken.Add(field.Name);
        }

        foreach (var ownName in ownNames)
        {
            if (!string.IsNullOrEmpty(ownName))
            {
                taken.Remove(ownName);
            }
        }

        return taken;
    }

    private void OnUnitsListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            EditSelectedUnit();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Delete)
        {
            DeleteSelectedUnit();
            e.Handled = true;
        }
    }

    private enum UnitRowKind
    {
        None,
        Unit,
        Count
    }

    private sealed record RuleRowValues(
        bool Enabled,
        string Spell,
        string UnitText,
        string MacroCondition,
        string Condition,
        IReadOnlyList<string> SubConditions,
        int? DelayMs,
        int? LogicDelayMs);

    private sealed class RuleRowMetadata(
        IEnumerable<string>? subConditions = null,
        int? delayMs = null,
        int? logicDelayMs = null)
    {
        public List<string> SubConditions { get; } = subConditions?.ToList() ?? new List<string>();
        public int? DelayMs { get; set; } = delayMs;
        public int? LogicDelayMs { get; set; } = logicDelayMs;
    }

    private void OnRulesGridCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        var columnName = _rulesGrid.Columns[e.ColumnIndex].Name;
        if (_rulesGrid.Rows[e.RowIndex].Cells[e.ColumnIndex] is DataGridViewComboBoxCell)
        {
            ShowRulesComboDropDown(e.RowIndex, e.ColumnIndex);
            return;
        }

        if (columnName == "MoveUp")
        {
            MoveRule(e.RowIndex, -1);
            return;
        }

        if (columnName == "MoveDown")
        {
            MoveRule(e.RowIndex, 1);
            return;
        }

        if (columnName == "Copy")
        {
            CopyRule(e.RowIndex);
            return;
        }

        if (columnName == "InsertBlank")
        {
            InsertBlankRule(e.RowIndex);
            return;
        }

        if (columnName == "Delete")
        {
            DeleteRule(e.RowIndex);
            return;
        }

        if (columnName == "Condition")
        {
            OpenConditionEditor(e.RowIndex);
        }
    }

    // 不进入 WinForms 原生 ComboBox 编辑态，直接显示受控的深色列表，避免白边、尺寸跳变和按钮错位。
    private void ShowRulesComboDropDown(int rowIndex, int columnIndex)
    {
        CloseRulesComboDropDown();

        var row = _rulesGrid.Rows[rowIndex];
        if (row.IsNewRow)
        {
            rowIndex = _rulesGrid.Rows.Add(true, null!, string.Empty, string.Empty, string.Empty, string.Empty);
            row = _rulesGrid.Rows[rowIndex];
        }

        if (row.Cells[columnIndex] is not DataGridViewComboBoxCell cell)
        {
            return;
        }

        _rulesGrid.CurrentCell = cell;
        var values = cell.Items.Cast<object>()
            .Select(item => item?.ToString() ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (values.Count == 0 && cell.OwningColumn is DataGridViewComboBoxColumn column)
        {
            values.AddRange(column.Items.Cast<object>()
                .Select(item => item?.ToString() ?? string.Empty)
                .Distinct(StringComparer.Ordinal));
        }

        var currentValue = cell.Value?.ToString() ?? string.Empty;
        if (!values.Contains(currentValue, StringComparer.Ordinal))
        {
            values.Insert(0, currentValue);
        }

        var scale = Math.Max(1f, _rulesGrid.DeviceDpi / 96f);
        var itemHeight = Math.Max((int)Math.Round(32 * scale), _rulesGrid.Font.Height + (int)Math.Round(12 * scale));
        var visibleItems = Math.Clamp(values.Count, 1, 9);
        var cellBounds = _rulesGrid.GetCellDisplayRectangle(columnIndex, rowIndex, cutOverflow: true);
        var measuredWidth = values.Count == 0
            ? 0
            : values.Max(value => TextRenderer.MeasureText(DisplayRulesComboValue(value), _rulesGrid.Font).Width);
        var listWidth = Math.Clamp(
            Math.Max(cellBounds.Width, measuredWidth + (int)Math.Round(40 * scale)),
            (int)Math.Round(150 * scale),
            (int)Math.Round(420 * scale));
        var listHeight = visibleItems * itemHeight + 2;

        var listBox = new ListBox
        {
            BackColor = UiTheme.Surface,
            ForeColor = UiTheme.Text,
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            IntegralHeight = false,
            ItemHeight = itemHeight,
            Font = _rulesGrid.Font,
            Size = new Size(listWidth, listHeight)
        };
        listBox.Items.AddRange(values.Cast<object>().ToArray());
        listBox.DrawItem += OnRulesComboListDrawItem;
        listBox.MouseMove += (_, e) =>
        {
            var index = listBox.IndexFromPoint(e.Location);
            if (index >= 0 && index != listBox.SelectedIndex)
            {
                listBox.SelectedIndex = index;
            }
        };

        var selectedIndex = values.FindIndex(value => string.Equals(value, currentValue, StringComparison.Ordinal));
        if (selectedIndex >= 0)
        {
            listBox.SelectedIndex = selectedIndex;
        }

        var host = new ToolStripControlHost(listBox)
        {
            AutoSize = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Size = listBox.Size
        };
        var dropDown = new ToolStripDropDown
        {
            AutoSize = false,
            AutoClose = true,
            BackColor = UiTheme.Border,
            DropShadowEnabled = true,
            Margin = Padding.Empty,
            Padding = new Padding(1),
            Size = new Size(listWidth + 2, listHeight + 2)
        };
        dropDown.Items.Add(host);
        _rulesComboDropDown = dropDown;

        void ApplySelectedValue()
        {
            if (listBox.SelectedIndex < 0 || listBox.SelectedIndex >= values.Count)
            {
                return;
            }

            cell.Value = values[listBox.SelectedIndex];
            _rulesGrid.InvalidateCell(cell);
            dropDown.Close(ToolStripDropDownCloseReason.ItemClicked);
        }

        listBox.Click += (_, _) => ApplySelectedValue();
        listBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                ApplySelectedValue();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                dropDown.Close(ToolStripDropDownCloseReason.Keyboard);
            }
        };
        dropDown.Closed += (_, _) =>
        {
            if (ReferenceEquals(_rulesComboDropDown, dropDown))
            {
                _rulesComboDropDown = null;
            }
        };

        dropDown.Show(_rulesGrid, new Point(cellBounds.Left, cellBounds.Bottom), ToolStripDropDownDirection.BelowRight);
        listBox.Focus();
    }

    private static string DisplayRulesComboValue(string value)
        => string.IsNullOrEmpty(value) ? "（留空）" : value;

    private static void OnRulesComboListDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ListBox listBox || e.Index < 0 || e.Index >= listBox.Items.Count)
        {
            return;
        }

        var selected = (e.State & DrawItemState.Selected) != 0;
        using (var background = new SolidBrush(selected ? UiTheme.AccentSoft : UiTheme.Surface))
        {
            e.Graphics.FillRectangle(background, e.Bounds);
        }

        var text = DisplayRulesComboValue(listBox.Items[e.Index]?.ToString() ?? string.Empty);
        var textBounds = new Rectangle(e.Bounds.Left + 10, e.Bounds.Top, Math.Max(0, e.Bounds.Width - 20), e.Bounds.Height);
        TextRenderer.DrawText(
            e.Graphics,
            text,
            listBox.Font,
            textBounds,
            selected ? UiTheme.Accent : UiTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }

    private void CloseRulesComboDropDown()
    {
        _rulesComboDropDown?.Close(ToolStripDropDownCloseReason.AppClicked);
        _rulesComboDropDown = null;
    }

    private void OnRulesGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        var columnName = _rulesGrid.Columns[e.ColumnIndex].Name;
        var row = _rulesGrid.Rows[e.RowIndex];
        if (columnName == "SpellIcon" && row.IsNewRow)
        {
            e.Value = SpellIconCatalog.GetLastRuleRowIcon();
            e.FormattingApplied = true;
            return;
        }

        if (!row.IsNewRow && GetMissingConditionFields(row).Count > 0)
        {
            // 缺失字段会让条件静默不命中；用整行红色状态在保存前就提醒用户修复配置。
            e.CellStyle.BackColor = UiTheme.DangerSoft;
            e.CellStyle.ForeColor = UiTheme.Danger;
            e.CellStyle.SelectionBackColor = UiTheme.Danger;
            e.CellStyle.SelectionForeColor = UiTheme.Background;
        }

        if (columnName == "RuleNumber")
        {
            e.Value = row.IsNewRow ? string.Empty : (e.RowIndex + 1).ToString();
            e.FormattingApplied = true;
            return;
        }

        // 「条件」列在有子条件时显示成 "主条件 且任一(子1 | 子2)"; 仅改显示, 底层值仍是主条件, 不影响 ReadRules 存盘。
        if (columnName == "Condition" && !row.IsNewRow)
        {
            var metadata = GetRuleMetadata(row);
            e.Value = DecorateCondition(
                e.Value?.ToString() ?? string.Empty,
                metadata.SubConditions,
                metadata.DelayMs,
                metadata.LogicDelayMs);
            e.FormattingApplied = true;
        }
    }

    // 返回主条件和所有子条件中不属于当前职业/专精、动态单位或动态数值目录的字段。
    private IReadOnlyList<string> GetMissingConditionFields(DataGridViewRow row)
    {
        EnsureConditionFieldValidationCatalog();
        var available = _availableConditionFields!;
        var groupFields = _availableGroupConditionFields!;

        var missing = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var metadata = GetRuleMetadata(row);
        foreach (var expression in new[] { CellText(row, "Condition") }.Concat(metadata.SubConditions))
        {
            foreach (var term in ConditionExpression.Parse(expression))
            {
                var original = term.Field.Trim();
                var normalized = NormalizeConditionFieldName(original);
                if (normalized.Length == 0 || available.Contains(normalized))
                {
                    continue;
                }

                // group.<槽位>.<字段> 是运行时支持的直接队伍引用；只需确认末段字段存在。
                var groupParts = normalized.Split('.', 3);
                if (groupParts.Length == 3
                    && string.Equals(groupParts[0], "group", StringComparison.OrdinalIgnoreCase)
                    && groupFields.Contains(groupParts[2]))
                {
                    continue;
                }

                if (seen.Add(original))
                {
                    missing.Add(original);
                }
            }
        }

        return missing;
    }

    // 字段目录的构造会读取职业配置和动态数值表；缓存后避免每个可见单元格重复做同一份工作。
    private void EnsureConditionFieldValidationCatalog()
    {
        if (_availableConditionFields is not null && _availableGroupConditionFields is not null)
        {
            return;
        }

        _availableConditionFields = new HashSet<string>(
            BuildConditionFields(includeRuleSettings: true)
                .Select(field => NormalizeConditionFieldName(field.Name)),
            StringComparer.Ordinal);

        var classId = ReadMatchCombo(_classBox);
        var specId = ReadMatchCombo(_specBox);
        _availableGroupConditionFields = new HashSet<string>(
            _fieldCatalog.GetGroupFields(classId, specId).Select(field => field.Name),
            StringComparer.Ordinal);

        // 运行时也支持“动态单位名.队伍字段”；编辑器目录未展开这些组合，这里仍应判定为有效。
        foreach (var unit in _units.Where(unit => !string.IsNullOrWhiteSpace(unit.Name)))
        {
            foreach (var groupField in _availableGroupConditionFields)
            {
                _availableConditionFields.Add($"{unit.Name}.{groupField}");
            }
        }
    }

    private void InvalidateConditionFieldValidation()
    {
        _availableConditionFields = null;
        _availableGroupConditionFields = null;
        _rulesGrid.Invalidate();
    }

    // 运行时允许 state./spell./aura. 别名且前缀不区分大小写；校验时归一化为目录使用的名称。
    private static string NormalizeConditionFieldName(string? fieldName)
    {
        var name = fieldName?.Trim() ?? string.Empty;
        if (name.StartsWith("state.", StringComparison.OrdinalIgnoreCase))
        {
            return name["state.".Length..];
        }

        if (name.StartsWith("spells.", StringComparison.OrdinalIgnoreCase))
        {
            return $"spells.{name["spells.".Length..]}";
        }

        if (name.StartsWith("spell.", StringComparison.OrdinalIgnoreCase))
        {
            return $"spells.{name["spell.".Length..]}";
        }

        if (name.StartsWith("auras.", StringComparison.OrdinalIgnoreCase))
        {
            return $"auras.{name["auras.".Length..]}";
        }

        if (name.StartsWith("aura.", StringComparison.OrdinalIgnoreCase))
        {
            return $"auras.{name["aura.".Length..]}";
        }

        return name;
    }

    // 把主条件、子条件和规则延迟合成可读文本；仅改显示，不改变底层条件表达式。
    private static string DecorateCondition(
        string main,
        IReadOnlyList<string>? subs,
        int? delayMs,
        int? logicDelayMs)
    {
        var conditionText = main;
        if (subs is { Count: > 0 })
        {
            var any = string.Join(" | ", subs);
            conditionText = main.Length == 0 ? $"任一({any})" : $"{main}  且任一({any})";
        }

        if (delayMs is > 0)
        {
            conditionText = conditionText.Length == 0
                ? $"延迟 {delayMs.Value} ms"
                : $"{conditionText}；延迟 {delayMs.Value} ms";
        }

        if (logicDelayMs is > 0)
        {
            conditionText = conditionText.Length == 0
                ? $"逻辑延迟 {logicDelayMs.Value} ms"
                : $"{conditionText}；逻辑延迟 {logicDelayMs.Value} ms";
        }

        return conditionText;
    }

    private void OnRulesGridCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        var columnName = _rulesGrid.Columns[e.ColumnIndex].Name;
        if (columnName is "Spell" or "Unit" or "MacroCondition")
        {
            PaintRuleComboBoxCell(e);
            return;
        }

        if (columnName == "Drag")
        {
            PaintRuleDragHandle(e);
            return;
        }

        if (columnName is not ("MoveUp" or "MoveDown" or "Copy" or "InsertBlank" or "Delete"))
        {
            return;
        }

        var icon = columnName switch
        {
            "MoveUp" => "▲",
            "MoveDown" => "▼",
            "Copy" => "⧉",
            "InsertBlank" => "+",
            _ => "×"
        };
        var enabled = IsRuleIconEnabled(columnName, e.RowIndex);
        var color = columnName == "Delete" ? UiTheme.Danger : UiTheme.Muted;
        if (!enabled)
        {
            color = Color.FromArgb(70, color);
        }

        PaintGridIconCell(_rulesGrid, e, icon, color);
    }

    private void OnRulesGridCellMouseEnter(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        var columnName = _rulesGrid.Columns[e.ColumnIndex].Name;

        // 条件列(点击编辑) → 手型; 拖拽手柄列 → 移动光标; 其它 → 默认。
        var isExisting = !_rulesGrid.Rows[e.RowIndex].IsNewRow;
        _rulesGrid.Cursor = columnName switch
        {
            "Condition" when isExisting => Cursors.Hand,
            "Drag" when isExisting => Cursors.SizeAll,
            _ => Cursors.Default
        };

        var text = GetRuleCellToolTip(columnName, e.RowIndex, e.ColumnIndex);
        if (string.IsNullOrEmpty(text))
        {
            _rulesGridToolTip.Hide(_rulesGrid);
            return;
        }

        var cellBounds = _rulesGrid.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, cutOverflow: true);
        _rulesGridToolTip.Show(text, _rulesGrid, cellBounds.Left + cellBounds.Width / 2, cellBounds.Bottom + 4);
    }

    private void OnRulesGridCellMouseLeave(object? sender, DataGridViewCellEventArgs e)
    {
        _rulesGrid.Cursor = Cursors.Default;
        _rulesGridToolTip.Hide(_rulesGrid);
    }

    // 图标列沿用原提示; 条件/技能/目标/宏条件列在文本被列宽截断或可点击时给出悬停提示。
    private string GetRuleCellToolTip(string columnName, int rowIndex, int columnIndex)
    {
        if (rowIndex >= _rulesGrid.Rows.Count || _rulesGrid.Rows[rowIndex].IsNewRow)
        {
            return string.Empty;
        }

        var missingFields = GetMissingConditionFields(_rulesGrid.Rows[rowIndex]);
        if (missingFields.Count > 0)
        {
            return $"条件字段不存在：{string.Join("、", missingFields)}\n请先添加对应字段。";
        }

        if (columnName is "MoveUp" or "MoveDown" or "Copy" or "InsertBlank" or "Delete")
        {
            return GetRuleIconToolTip(columnName, rowIndex);
        }

        if (columnName == "Drag")
        {
            return "拖动调整顺序";
        }

        if (columnName is not ("Condition" or "Spell" or "Unit" or "MacroCondition"))
        {
            return string.Empty;
        }

        var row = _rulesGrid.Rows[rowIndex];
        var text = CellText(row, columnName);
        if (columnName == "Condition")
        {
            // 提示与裁剪检测都用合成后的完整文本(含子条件和延迟), 与单元格显示一致。
            var metadata = GetRuleMetadata(row);
            text = DecorateCondition(
                text,
                metadata.SubConditions,
                metadata.DelayMs,
                metadata.LogicDelayMs);
            if (text.Length == 0)
            {
                return "点击编辑条件 (当前: 始终命中)";
            }
        }

        return IsCellTextClipped(text, columnIndex) ? text : string.Empty;
    }

    private bool IsCellTextClipped(string text, int columnIndex)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var available = _rulesGrid.Columns[columnIndex].Width - 12;
        return TextRenderer.MeasureText(text, _rulesGrid.Font).Width > available;
    }

    private string GetRuleIconToolTip(string columnName, int rowIndex)
    {
        if (!IsRuleIconEnabled(columnName, rowIndex))
        {
            return string.Empty;
        }

        return columnName switch
        {
            "MoveUp" => "上移",
            "MoveDown" => "下移",
            "Copy" => "复制到下一行",
            "InsertBlank" => "在下一行添加空白条件",
            "Delete" => "删除",
            _ => string.Empty
        };
    }

    private void OnAdjustmentsGridCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        if (_adjustmentsGrid.Columns[e.ColumnIndex].Name != "Delete")
        {
            return;
        }

        PaintGridIconCell(_adjustmentsGrid, e, "×", UiTheme.Danger);
    }

    private void OnFormulaAdjustmentsGridCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        if (_formulaAdjustmentsGrid.Columns[e.ColumnIndex].Name != "Delete")
        {
            return;
        }

        PaintGridIconCell(_formulaAdjustmentsGrid, e, "×", UiTheme.Danger);
    }

    private static void PaintGridIconCell(DataGridView grid, DataGridViewCellPaintingEventArgs e, string icon, Color color)
    {
        e.Paint(e.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border);
        if (e.Graphics is null)
        {
            e.Handled = true;
            return;
        }

        TextRenderer.DrawText(
            e.Graphics,
            icon,
            grid.Font,
            e.CellBounds,
            color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        e.Handled = true;
    }

    // 避免 WinForms 按系统主题绘制高亮白色方块，统一成深色圆角按钮和青色箭头。
    private void PaintRuleComboBoxCell(DataGridViewCellPaintingEventArgs e)
    {
        e.Paint(e.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border);
        if (e.Graphics is null)
        {
            e.Handled = true;
            return;
        }

        var selected = (_rulesGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].State & DataGridViewElementStates.Selected) != 0;
        var cellStyle = e.CellStyle ?? _rulesGrid.DefaultCellStyle;
        var textColor = selected ? cellStyle.SelectionForeColor : cellStyle.ForeColor;
        var buttonSize = Math.Min(24, Math.Max(18, e.CellBounds.Height - 12));
        var buttonBounds = new Rectangle(
            e.CellBounds.Right - buttonSize - 7,
            e.CellBounds.Top + (e.CellBounds.Height - buttonSize) / 2,
            buttonSize,
            buttonSize);
        var textBounds = new Rectangle(
            e.CellBounds.Left + 10,
            e.CellBounds.Top,
            Math.Max(0, buttonBounds.Left - e.CellBounds.Left - 16),
            e.CellBounds.Height);

        TextRenderer.DrawText(
            e.Graphics,
            e.FormattedValue?.ToString() ?? string.Empty,
            cellStyle.Font ?? _rulesGrid.Font,
            textBounds,
            textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

        var oldSmoothingMode = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = UiTheme.CreateRoundedRectanglePath(buttonBounds, 4))
        using (var background = new SolidBrush(selected ? UiTheme.Pressed : UiTheme.Hover))
        using (var border = new Pen(selected ? UiTheme.Accent : UiTheme.Border))
        {
            e.Graphics.FillPath(background, path);
            e.Graphics.DrawPath(border, path);
        }

        var centerX = buttonBounds.Left + buttonBounds.Width / 2;
        var centerY = buttonBounds.Top + buttonBounds.Height / 2 + 1;
        var arrow = new[]
        {
            new Point(centerX - 4, centerY - 2),
            new Point(centerX + 4, centerY - 2),
            new Point(centerX, centerY + 3)
        };
        using (var arrowBrush = new SolidBrush(selected ? UiTheme.Accent : UiTheme.Muted))
        {
            e.Graphics.FillPolygon(arrowBrush, arrow);
        }

        e.Graphics.SmoothingMode = oldSmoothingMode;
        e.Handled = true;
    }

    // 自绘 2×3 六点抓手, 不依赖字体里是否有 grip 字形; 新行不画。
    private void PaintRuleDragHandle(DataGridViewCellPaintingEventArgs e)
    {
        e.Paint(e.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border);
        if (e.Graphics is null || e.RowIndex < 0 || _rulesGrid.Rows[e.RowIndex].IsNewRow)
        {
            e.Handled = true;
            return;
        }

        var cx = e.CellBounds.Left + e.CellBounds.Width / 2;
        var cy = e.CellBounds.Top + e.CellBounds.Height / 2;
        var color = _rulesGrid.Rows[e.RowIndex].Selected ? UiTheme.Text : UiTheme.Muted;
        using var brush = new SolidBrush(color);
        foreach (var x in new[] { cx - 4, cx })
        {
            foreach (var y in new[] { cy - 7, cy - 1, cy + 5 })
            {
                e.Graphics.FillEllipse(brush, x, y, 2, 2);
            }
        }

        e.Handled = true;
    }

    private bool IsRuleIconEnabled(string columnName, int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _rulesGrid.Rows.Count || _rulesGrid.Rows[rowIndex].IsNewRow)
        {
            return false;
        }

        return columnName switch
        {
            "MoveUp" => rowIndex > 0,
            "MoveDown" => rowIndex < LastRuleRowIndex(),
            "Copy" => true,
            "InsertBlank" => true,
            "Delete" => true,
            _ => false
        };
    }

    private void OnAdjustmentsGridCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        var columnName = _adjustmentsGrid.Columns[e.ColumnIndex].Name;
        if (columnName == "Delete")
        {
            var row = _adjustmentsGrid.Rows[e.RowIndex];
            if (!row.IsNewRow)
            {
                _adjustmentsGrid.Rows.RemoveAt(e.RowIndex);
                RefreshAdjustmentFieldColumn();
            }

            return;
        }

        if (columnName == "Condition")
        {
            OpenAdjustmentConditionEditor(e.RowIndex);
        }
    }

    private void OnFormulaAdjustmentsGridCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        var columnName = _formulaAdjustmentsGrid.Columns[e.ColumnIndex].Name;
        if (columnName == "Delete")
        {
            var row = _formulaAdjustmentsGrid.Rows[e.RowIndex];
            if (!row.IsNewRow)
            {
                _formulaAdjustmentsGrid.Rows.RemoveAt(e.RowIndex);
                RefreshAdjustmentFieldColumn();
            }

            return;
        }

        if (columnName == "Formula")
        {
            OpenFormulaEditor(e.RowIndex);
        }
    }

    private void OnFormulaAdjustmentsGridCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        if (_formulaAdjustmentsGrid.Columns[e.ColumnIndex].Name == "Field")
        {
            RefreshAdjustmentFieldColumn();
        }
    }

    private void OnAdjustmentsGridEditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e)
    {
        var columnName = _adjustmentsGrid.CurrentCell?.OwningColumn?.Name;
        if (e.Control is not ComboBox comboBox)
        {
            return;
        }

        if (columnName == "Field")
        {
            comboBox.DropDownStyle = ComboBoxStyle.DropDown;
            comboBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            comboBox.AutoCompleteSource = AutoCompleteSource.ListItems;
        }
        else if (columnName == "Type")
        {
            // 类型为固定列表, 不允许自由输入。
            comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        }
        else
        {
            return;
        }

        // 可输入下拉默认是系统白底, 与暗色表格冲突; 显式套用暗色。
        comboBox.FlatStyle = FlatStyle.Flat;
        comboBox.BackColor = UiTheme.Field;
        comboBox.ForeColor = UiTheme.Text;
    }

    private void OnAdjustmentsGridCellValidating(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        if (_adjustmentsGrid.Columns[e.ColumnIndex].Name != "Field")
        {
            return;
        }

        var text = e.FormattedValue?.ToString();
        EnsureComboItem(_adjustmentFieldColumn, text);
        // 该行若已按"类型"设了单元格级选项, 自定义输入也要补进单元格, 否则会被组合框拒绝丢失。
        if (!string.IsNullOrEmpty(text)
            && _adjustmentsGrid.Rows[e.RowIndex].Cells["Field"] is DataGridViewComboBoxCell cell
            && cell.Items.Count > 0
            && !cell.Items.Contains(text))
        {
            cell.Items.Add(text);
        }
    }

    private void OpenAdjustmentConditionEditor(int rowIndex)
    {
        var row = _adjustmentsGrid.Rows[rowIndex];
        var current = row.IsNewRow ? string.Empty : CellText(row, "Condition");

        using var editor = new ConditionEditorForm(
            RefreshAndBuildConditionFields(),
            current,
            conditionFieldsProvider: () => RefreshAndBuildConditionFields());
        if (editor.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        if (row.IsNewRow)
        {
            if (!string.IsNullOrWhiteSpace(editor.ConditionText))
            {
                _adjustmentsGrid.Rows.Add(true, string.Empty, 0, editor.ConditionText);
            }

            return;
        }

        row.Cells["Condition"].Value = editor.ConditionText;
    }

    private void OpenFormulaEditor(int rowIndex)
    {
        var row = _formulaAdjustmentsGrid.Rows[rowIndex];
        var current = row.IsNewRow ? string.Empty : CellText(row, "Formula");

        using var editor = new FormulaEditorForm(current);
        if (editor.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        var field = row.IsNewRow ? string.Empty : CellText(row, "Field");
        var formula = editor.FormulaText;
        if (FormulaEvaluator.TrySplitAssignment(formula, out var formulaField, out var normalizedFormula))
        {
            if (string.IsNullOrWhiteSpace(field))
            {
                field = formulaField;
            }

            formula = normalizedFormula;
        }
        else
        {
            formula = FormulaEvaluator.NormalizeExpression(formula);
        }

        if (string.IsNullOrWhiteSpace(field) && !string.IsNullOrWhiteSpace(formula))
        {
            MessageBox.Show(
                "请先填写公式动态数值的“数值名称”，或在公式中写成“名称 = 表达式”。",
                "Shigure",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        if (row.IsNewRow)
        {
            if (!string.IsNullOrWhiteSpace(field) || !string.IsNullOrWhiteSpace(formula))
            {
                _formulaAdjustmentsGrid.Rows.Add(true, field, formula);
                EnsureComboItem(_adjustmentFieldColumn, field);
                RefreshAdjustmentFieldColumn();
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(field))
        {
            row.Cells["Field"].Value = field;
            EnsureComboItem(_adjustmentFieldColumn, field);
        }

        row.Cells["Formula"].Value = formula;
        RefreshAdjustmentFieldColumn();
    }

    private void DeleteRule(int rowIndex)
    {
        _rulesGrid.EndEdit();
        var row = _rulesGrid.Rows[rowIndex];
        // 新行占位符无需删除。
        if (!row.IsNewRow)
        {
            _rulesGrid.Rows.RemoveAt(rowIndex);
        }
    }

    private void CopyRule(int rowIndex)
    {
        _rulesGrid.EndEdit();
        if (!IsExistingRuleRow(rowIndex))
        {
            return;
        }

        InsertRuleAfter(rowIndex, ReadRuleRow(_rulesGrid.Rows[rowIndex]));
    }

    private void InsertBlankRule(int rowIndex)
    {
        _rulesGrid.EndEdit();
        if (!IsExistingRuleRow(rowIndex))
        {
            return;
        }

        InsertRuleAfter(rowIndex, new RuleRowValues(
            true,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            Array.Empty<string>(),
            null,
            null));
    }

    private void InsertRuleAfter(int rowIndex, RuleRowValues values)
    {
        var insertIndex = rowIndex + 1;
        _rulesGrid.Rows.Insert(insertIndex, 1);
        var inserted = _rulesGrid.Rows[insertIndex];
        WriteRuleRow(inserted, values);
        _rulesGrid.CurrentCell = inserted.Cells["Spell"];
        inserted.Selected = true;
        _rulesGrid.Invalidate();
    }

    private void MoveRule(int rowIndex, int direction)
    {
        _rulesGrid.EndEdit();
        if (!IsExistingRuleRow(rowIndex))
        {
            return;
        }

        var targetIndex = rowIndex + direction;
        if (targetIndex < 0 || targetIndex > LastRuleRowIndex())
        {
            return;
        }

        var current = ReadRuleRow(_rulesGrid.Rows[rowIndex]);
        var target = ReadRuleRow(_rulesGrid.Rows[targetIndex]);
        WriteRuleRow(_rulesGrid.Rows[rowIndex], target);
        WriteRuleRow(_rulesGrid.Rows[targetIndex], current);
        _rulesGrid.CurrentCell = _rulesGrid.Rows[targetIndex].Cells["Spell"];
        _rulesGrid.Rows[targetIndex].Selected = true;
        _rulesGrid.Invalidate();
    }

    // 拖拽手柄按下: 记录起始行(仅限抓手列上的已有规则行)。
    private void OnRulesGridMouseDown(object? sender, MouseEventArgs e)
    {
        _dragSourceRow = -1;
        var hit = _rulesGrid.HitTest(e.X, e.Y);
        if (hit.RowIndex >= 0
            && hit.ColumnIndex >= 0
            && _rulesGrid.Columns[hit.ColumnIndex].Name == "Drag"
            && IsExistingRuleRow(hit.RowIndex))
        {
            _dragSourceRow = hit.RowIndex;
        }
    }

    // 在抓手上按住左键移动即开始拖拽(DoDragDrop 自带模态循环, 结束后复位)。
    private void OnRulesGridMouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragSourceRow < 0 || (e.Button & MouseButtons.Left) == 0)
        {
            return;
        }

        var source = _dragSourceRow;
        _rulesGrid.DoDragDrop(source, DragDropEffects.Move);
        _dragSourceRow = -1;
        ClearDragIndicator();
    }

    private void OnRulesGridDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(typeof(int)) != true)
        {
            e.Effect = DragDropEffects.None;
            return;
        }

        e.Effect = DragDropEffects.Move;
        SetDragIndicator(ResolveDropSlot(e));
    }

    private void OnRulesGridDragDrop(object? sender, DragEventArgs e)
    {
        ClearDragIndicator();
        if (e.Data?.GetData(typeof(int)) is int source)
        {
            MoveRuleByDrag(source, ResolveDropSlot(e));
        }
    }

    private void OnRulesGridPaint(object? sender, PaintEventArgs e)
    {
        if (_dragIndicatorRow < 0)
        {
            return;
        }

        var last = LastRuleRowIndex();
        // 指示位置可能等于"末尾"(= last+1): 画在最后一行的下边缘, 否则画在该行上边缘。
        var atEnd = _dragIndicatorRow > last;
        var rect = _rulesGrid.GetRowDisplayRectangle(atEnd ? last : _dragIndicatorRow, false);
        if (rect.Height == 0)
        {
            return;
        }

        var y = atEnd ? rect.Bottom - 1 : rect.Top;
        using var pen = new Pen(UiTheme.Accent, 2);
        e.Graphics.DrawLine(pen, rect.Left, y, rect.Right, y);
    }

    // 把拖放点解析为"插入到第几行之前"的槽位(0..last+1), 行下半区视为插入到其后。
    private int ResolveDropSlot(DragEventArgs e)
    {
        var pt = _rulesGrid.PointToClient(new Point(e.X, e.Y));
        var hit = _rulesGrid.HitTest(pt.X, pt.Y);
        var last = LastRuleRowIndex();
        if (hit.RowIndex < 0 || hit.RowIndex > last)
        {
            return last + 1;
        }

        var rect = _rulesGrid.GetRowDisplayRectangle(hit.RowIndex, false);
        var lowerHalf = pt.Y > rect.Top + rect.Height / 2;
        return lowerHalf ? hit.RowIndex + 1 : hit.RowIndex;
    }

    private void SetDragIndicator(int slot)
    {
        if (_dragIndicatorRow == slot)
        {
            return;
        }

        _dragIndicatorRow = slot;
        _rulesGrid.Invalidate();
    }

    private void ClearDragIndicator()
    {
        if (_dragIndicatorRow < 0)
        {
            return;
        }

        _dragIndicatorRow = -1;
        _rulesGrid.Invalidate();
    }

    // 把第 source 行移动到插入槽位 slot 之前, 通过读出全部规则行 → 重排 → 写回(行数不变)。
    private void MoveRuleByDrag(int source, int slot)
    {
        _rulesGrid.EndEdit();
        if (!IsExistingRuleRow(source))
        {
            return;
        }

        var count = LastRuleRowIndex() + 1;
        if (count <= 1)
        {
            return;
        }

        slot = Math.Clamp(slot, 0, count);
        // 移除 source 后, 其后的插入位置整体前移一位。
        var insertAt = Math.Clamp(source < slot ? slot - 1 : slot, 0, count - 1);
        if (insertAt == source)
        {
            return;
        }

        var rows = new List<RuleRowValues>(count);
        for (var i = 0; i < count; i++)
        {
            rows.Add(ReadRuleRow(_rulesGrid.Rows[i]));
        }

        var moved = rows[source];
        rows.RemoveAt(source);
        rows.Insert(insertAt, moved);
        for (var i = 0; i < count; i++)
        {
            WriteRuleRow(_rulesGrid.Rows[i], rows[i]);
        }

        _rulesGrid.CurrentCell = _rulesGrid.Rows[insertAt].Cells["Spell"];
        _rulesGrid.Rows[insertAt].Selected = true;
        _rulesGrid.Invalidate();
    }

    private int LastRuleRowIndex()
    {
        var last = _rulesGrid.Rows.Count - 1;
        if (_rulesGrid.AllowUserToAddRows)
        {
            last--;
        }

        return last;
    }

    private bool IsExistingRuleRow(int rowIndex)
    {
        return rowIndex >= 0 && rowIndex <= LastRuleRowIndex() && !_rulesGrid.Rows[rowIndex].IsNewRow;
    }

    private RuleRowValues ReadRuleRow(DataGridViewRow row)
    {
        return new RuleRowValues(
            CellBool(row, "Enabled", defaultValue: true),
            CellText(row, "Spell"),
            CellText(row, "Unit"),
            CellText(row, "MacroCondition"),
            CellText(row, "Condition"),
            // 子条件和延迟挂在 row.Tag, 随行一起被移动/拖拽/复制搬运。
            GetRuleMetadata(row).SubConditions,
            GetRuleMetadata(row).DelayMs,
            GetRuleMetadata(row).LogicDelayMs);
    }

    private void WriteRuleRow(DataGridViewRow row, RuleRowValues values)
    {
        row.Cells["Enabled"].Value = values.Enabled;
        EnsureComboItem(_spellColumn, values.Spell);
        row.Cells["Spell"].Value = values.Spell;
        row.Cells["MacroCondition"].Value = string.Empty;
        row.Cells["Condition"].Value = values.Condition;
        row.Tag = new RuleRowMetadata(values.SubConditions, values.DelayMs, values.LogicDelayMs);
        RebuildUnitCell(row, values.UnitText);
        RebuildMacroConditionCell(row, values.MacroCondition);
    }

    private void OpenConditionEditor(int rowIndex)
    {
        var row = _rulesGrid.Rows[rowIndex];
        var current = row.IsNewRow ? string.Empty : CellText(row, "Condition");
        var currentMetadata = row.IsNewRow ? new RuleRowMetadata() : GetRuleMetadata(row);
        var fields = RefreshAndBuildConditionFields(includeRuleSettings: true);

        using var editor = new ConditionEditorForm(
            fields,
            current,
            currentMetadata.SubConditions,
            allowSubConditions: true,
            delayMs: currentMetadata.DelayMs,
            logicDelayMs: currentMetadata.LogicDelayMs,
            allowRuleSettings: true,
            conditionFieldsProvider: () => RefreshAndBuildConditionFields(includeRuleSettings: true));
        if (editor.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        var subs = new List<string>(editor.SubConditions);
        if (row.IsNewRow)
        {
            // 新行占位符不能直接赋值, 改为追加一行(主条件或子条件任一非空即可)。
            if (!string.IsNullOrWhiteSpace(editor.ConditionText)
                || subs.Count > 0
                || editor.DelayMs is > 0
                || editor.LogicDelayMs is > 0)
            {
                var index = _rulesGrid.Rows.Add(true, null!, string.Empty, string.Empty, string.Empty, editor.ConditionText);
                _rulesGrid.Rows[index].Tag = new RuleRowMetadata(
                    subs,
                    editor.DelayMs,
                    editor.LogicDelayMs);
            }

            return;
        }

        row.Cells["Condition"].Value = editor.ConditionText;
        row.Tag = new RuleRowMetadata(subs, editor.DelayMs, editor.LogicDelayMs);
        // 让「条件」列的装饰显示(主条件 且任一(…))立即刷新。
        _rulesGrid.InvalidateRow(rowIndex);
    }

    // 条件字段 = 状态/技能字段 + 每个动态单位的裸名(存在)和值名称 + 动态数值。
    private IReadOnlyList<ConditionField> RefreshAndBuildConditionFields(bool includeRuleSettings = false)
    {
        // 配置可能由“更新配置”或外部文件同步在当前编辑会话中被重建；每次打开条件弹窗都读取最新目录。
        _fieldCatalog = ConditionFieldCatalog.Load(_baseDirectory);
        InvalidateConditionFieldValidation();
        return BuildConditionFields(includeRuleSettings);
    }

    private IReadOnlyList<ConditionField> BuildConditionFields(bool includeRuleSettings = false)
    {
        var classId = ReadMatchCombo(_classBox);
        var specId = ReadMatchCombo(_specBox);
        var fields = new List<ConditionField>(_fieldCatalog.GetFields(classId, specId));
        var seen = new HashSet<string>(fields.Select(field => field.Name), StringComparer.Ordinal);

        if (includeRuleSettings && seen.Add(ShigureConditionFields.Delay))
        {
            fields.Add(new ConditionField(
                ShigureConditionFields.Delay,
                "延迟 (ms)",
                ConditionFieldType.Int,
                ConditionFieldCategory.Shigure));
        }

        if (includeRuleSettings && seen.Add(ShigureConditionFields.LogicDelay))
        {
            fields.Add(new ConditionField(
                ShigureConditionFields.LogicDelay,
                "逻辑延迟 (ms)",
                ConditionFieldType.Int,
                ConditionFieldCategory.Shigure));
        }

        foreach (var unit in _units)
        {
            if (string.IsNullOrWhiteSpace(unit.Name))
            {
                continue;
            }

            // 裸单位名作为存在性布尔。
            if (seen.Add(unit.Name))
            {
                fields.Add(new ConditionField(unit.Name, $"{unit.Name} (存在)", ConditionFieldType.Bool, ConditionFieldCategory.DynamicUnit));
            }

            // 值名称: 该单位 生命值 的直接命名数值字段。
            if (!string.IsNullOrWhiteSpace(unit.HealthName) && seen.Add(unit.HealthName))
            {
                fields.Add(new ConditionField(unit.HealthName, $"{unit.HealthName} (生命值)", ConditionFieldType.Int, ConditionFieldCategory.DynamicUnit));
            }
        }

        foreach (var count in _counts)
        {
            if (!string.IsNullOrWhiteSpace(count.Name) && seen.Add(count.Name))
            {
                fields.Add(new ConditionField(count.Name, $"人数: {count.Name}", ConditionFieldType.Int, ConditionFieldCategory.DynamicValue));
            }
        }

        foreach (var fieldName in GetAdjustmentTargetFields())
        {
            if (seen.Add(fieldName))
            {
                fields.Add(new ConditionField(
                    fieldName,
                    $"{fieldName} (动态数值)",
                    ConditionFieldType.Int,
                    ConditionFieldCategory.DynamicValue));
            }
        }

        return fields;
    }

    private Control BuildActionRow()
    {
        var row = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(14, 10, 14, 10)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var openFolderButton = UiTheme.CreateButton("打开目录", UiTheme.ButtonKind.Secondary);
        StyleModuleFooterButton(openFolderButton);
        openFolderButton.Dock = DockStyle.Fill;
        openFolderButton.Margin = new Padding(0, 0, 8, 0);
        openFolderButton.Click += (_, _) => OpenModuleFolder();
        _pathToolTip.SetToolTip(
            openFolderButton,
            "在资源管理器中打开模块目录；若已选中已保存模块则定位到对应文件");

        var spacer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };

        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _addButton = UiTheme.CreateButton("新建", UiTheme.ButtonKind.Secondary);
        StyleModuleFooterButton(_addButton);
        _addButton.Dock = DockStyle.Fill;
        _addButton.Margin = new Padding(0, 0, 8, 0);
        _addButton.Click += async (_, _) => await RunModuleCommandAsync(AddModuleAsync);

        _deleteButton = UiTheme.CreateButton("删除", UiTheme.ButtonKind.Danger);
        StyleModuleFooterButton(_deleteButton);
        _deleteButton.Dock = DockStyle.Fill;
        _deleteButton.Margin = new Padding(0, 0, 8, 0);
        _deleteButton.Click += async (_, _) => await RunModuleCommandAsync(DeleteSelectedModuleAsync);

        _saveButton = UiTheme.CreateButton("保存", UiTheme.ButtonKind.Primary);
        StyleModuleFooterButton(_saveButton);
        _saveButton.Dock = DockStyle.Fill;
        _saveButton.Margin = new Padding(0);
        _saveButton.Click += async (_, _) => await RunModuleCommandAsync(SaveSelectedModuleAsync);

        buttons.Controls.Add(_addButton, 0, 0);
        buttons.Controls.Add(_deleteButton, 1, 0);
        buttons.Controls.Add(_saveButton, 2, 0);

        row.Controls.Add(openFolderButton, 0, 0);
        row.Controls.Add(spacer, 1, 0);
        row.Controls.Add(buttons, 2, 0);
        return row;
    }

    private void OpenModuleFolder()
    {
        var moduleDirectory = ModuleStore.ResolveModuleDirectory();
        var filePath = _selectedModule?.FilePath;
        try
        {
            if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                    UseShellExecute = true
                });
                return;
            }

            if (!Directory.Exists(moduleDirectory))
            {
                Directory.CreateDirectory(moduleDirectory);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{moduleDirectory}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"无法打开模块文件夹：{ex.Message}",
                "Shigure",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private static void StyleModuleFooterButton(Button button)
    {
        button.AutoSize = false;
        button.Height = ModuleFooterButtonHeight;
        button.Margin = new Padding(0);
        button.Padding = new Padding(10, 0, 10, 0);
        button.TextAlign = ContentAlignment.MiddleCenter;
    }

    private static void StyleModuleActionButton(Button button)
        => StyleModuleFooterButton(button);

    public void ReloadModulesFromStore(bool reloadStore = true) => LoadModules(reloadStore);

    private void LoadModules(bool reloadStore = true)
    {
        if (reloadStore)
        {
            _moduleStore.Reload();
        }
        _modules = _moduleStore.GetModules().ToList();
        _moduleList.Items.Clear();
        foreach (var module in _modules)
        {
            _moduleList.Items.Add(ModuleDisplay.FormatListItem(module));
        }

        if (_modules.Count > 0)
        {
            _moduleList.SelectedIndex = 0;
        }
        else
        {
            ClearEditor();
        }
    }

    private void SelectModule(int index)
    {
        if (index < 0 || index >= _modules.Count)
        {
            ClearEditor();
            return;
        }

        _selectedModule = _modules[index].Clone();
        FillEditor(_selectedModule);
    }

    private void FillEditor(ModuleDefinition module)
    {
        _nameBox.Text = module.Name;
        _authorBox.Text = module.Author;
        _recommendedTalentBox.Text = module.RecommendedTalent;
        SetEditorEnabled(hasModule: true);
        // 先填充动态单位/数量, 后续目标下拉与条件字段都依赖它们。
        _units.Clear();
        _units.AddRange(module.Units.Select(unit => unit.Clone()));
        _counts.Clear();
        _counts.AddRange(module.Counts.Select(count => count.Clone()));
        _valueAdjustments.Clear();
        _valueAdjustments.AddRange(module.ValueAdjustments.Select(adjustment => adjustment.Clone()));
        RefreshUnitsList();
        SelectClass(module.Match.ClassId);
        SelectSpec(module.Match.SpecId);
        SelectPartyType(module.Match.PartyType);
        SelectHeroTalent(module.Match.HeroTalent);
        _pathLabel.Text = module.FilePath ?? "尚未保存";
        _versionLabel.Text = string.IsNullOrWhiteSpace(module.Version) ? "版本 未知" : $"版本 {module.Version}";
        _adjustmentsGrid.Rows.Clear();
        _formulaAdjustmentsGrid.Rows.Clear();
        RefreshAdjustmentFieldColumn();
        foreach (var adjustment in _valueAdjustments)
        {
            if (string.IsNullOrWhiteSpace(adjustment.Formula))
            {
                EnsureComboItem(_adjustmentFieldColumn, adjustment.Field);
                var index = _adjustmentsGrid.Rows.Add(adjustment.Enabled, adjustment.Field, adjustment.Delta, adjustment.Condition);
                // 由字段名回填"类型", 并按类型重建该行"数值"的可选项。
                ApplyAdjustmentRowType(_adjustmentsGrid.Rows[index], adjustment.Field);
            }
            else
            {
                _formulaAdjustmentsGrid.Rows.Add(
                    adjustment.Enabled,
                    adjustment.Field,
                    FormulaEvaluator.NormalizeExpression(adjustment.Formula));
            }
        }

        RefreshAdjustmentFieldColumn();

        _rulesGrid.Rows.Clear();
        RefreshKeymapColumns();

        foreach (var rule in module.Rules)
        {
            // 动态目标优先显示单位名；保留单位显示中文，其余团队槽位显示数字。
            var unitText = !string.IsNullOrWhiteSpace(rule.UnitName)
                ? rule.UnitName!
                : rule.Unit is { } unit ? ReservedUnit.ToDisplayText(unit) : string.Empty;
            EnsureComboItem(_spellColumn, rule.Spell);
            // 先加行(目标先留空), 再按技能重建目标选项并写回目标值, 避免值不在选项内被吞掉。
            var index = _rulesGrid.Rows.Add(
                rule.Enabled,
                SpellIconCatalog.Get(rule.Spell)!,
                rule.Spell,
                string.Empty,
                string.Empty,
                rule.Condition);
            _rulesGrid.Rows[index].Tag = new RuleRowMetadata(
                rule.SubConditions,
                rule.DelayMs,
                rule.LogicDelayMs);
            RebuildUnitCell(_rulesGrid.Rows[index], unitText);
            RebuildMacroConditionCell(_rulesGrid.Rows[index], rule.MacroCondition);
        }
    }

    private void ClearEditor()
    {
        _selectedModule = null;
        _nameBox.Clear();
        _authorBox.Clear();
        _recommendedTalentBox.Clear();
        _units.Clear();
        _counts.Clear();
        _valueAdjustments.Clear();
        RefreshUnitsList();
        SelectClass(null);
        SelectSpec(null);
        SelectPartyType(null);
        SelectHeroTalent(null);
        _pathLabel.Text = "无模块";
        _versionLabel.Text = string.Empty;
        _adjustmentsGrid.Rows.Clear();
        _formulaAdjustmentsGrid.Rows.Clear();
        RefreshAdjustmentFieldColumn();
        _rulesGrid.Rows.Clear();
        SetEditorEnabled(hasModule: false);
    }

    // 无选中模块时禁用保存/删除(否则点了静默无反应), 并在编辑区显示引导提示。
    private void SetEditorEnabled(bool hasModule)
    {
        _saveButton.Enabled = hasModule && !_moduleCommandInProgress;
        _deleteButton.Enabled = hasModule && !_moduleCommandInProgress;
        _addButton.Enabled = !_moduleCommandInProgress;
        _editorEmptyHint.Visible = !hasModule;
        if (!hasModule)
        {
            _editorEmptyHint.BringToFront();
        }
    }

    private async Task RunModuleCommandAsync(Func<Task> command)
    {
        if (_moduleCommandInProgress)
        {
            return;
        }

        _moduleCommandInProgress = true;
        SetEditorEnabled(_selectedModule is not null);
        try
        {
            await command();
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                MessageBox.Show(ex.Message, "模块操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            _moduleCommandInProgress = false;
            if (!IsDisposed)
            {
                SetEditorEnabled(_selectedModule is not null);
            }
        }
    }

    private async Task AddModuleAsync()
    {
        var module = ModuleDefinition.CreateDefault(_moduleStore.CreateNextModuleName());
        try
        {
            _moduleStore.Save(module);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        LoadModules(reloadStore: false);
        var index = _modules.FindIndex(existing => string.Equals(existing.Id, module.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            _moduleList.SelectedIndex = index;
        }

        await _runtimeRestartRequested();
    }

    private async Task SaveSelectedModuleAsync()
    {
        if (_selectedModule is null)
        {
            return;
        }

        if (!TryReadModule(out var module))
        {
            return;
        }

        ModuleDefinition saved;
        string? dependencyWarning;
        try
        {
            dependencyWarning = _captureDependencies(module);
            saved = _moduleStore.Save(module);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        LoadModules(reloadStore: false);
        var index = _modules.FindIndex(existing => string.Equals(existing.Id, saved.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            _moduleList.SelectedIndex = index;
        }

        if (!string.IsNullOrWhiteSpace(dependencyWarning))
        {
            MessageBox.Show(dependencyWarning, "模块已保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        await _runtimeRestartRequested();
    }

    private async Task DeleteSelectedModuleAsync()
    {
        if (_selectedModule is null)
        {
            return;
        }

        var result = MessageBox.Show(
            $"删除模块“{_selectedModule.Name}”？",
            "Shigure",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (result != DialogResult.Yes)
        {
            return;
        }

        _moduleStore.Delete(_selectedModule);
        LoadModules(reloadStore: false);
        await _runtimeRestartRequested();
    }

    private bool TryReadModule(out ModuleDefinition module)
    {
        module = _selectedModule!.Clone();
        module.Name = string.IsNullOrWhiteSpace(_nameBox.Text) ? "新模块" : _nameBox.Text.Trim();
        module.Author = _authorBox.Text.Trim();
        module.RecommendedTalent = _recommendedTalentBox.Text.Trim();
        // 保存时记录当前 Shigure 版本。
        module.Version = AppInfo.Version;
        module.Match = new ModuleMatch
        {
            ClassId = ReadMatchCombo(_classBox),
            SpecId = ReadMatchCombo(_specBox),
            PartyType = ReadPartyTypeCombo(),
            HeroTalent = ReadMatchCombo(_heroTalentBox)
        };

        module.Units = _units.Select(unit => unit.Clone()).ToList();
        module.Counts = _counts.Select(count => count.Clone()).ToList();
        if (!TryReadValueAdjustments(out var valueAdjustments, out var adjustmentError))
        {
            MessageBox.Show(adjustmentError, "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        module.ValueAdjustments = valueAdjustments;
        module.Rules = ReadRules();
        return true;
    }

    private bool TryReadValueAdjustments(out List<ModuleValueAdjustment> adjustments, out string error)
    {
        adjustments = new List<ModuleValueAdjustment>();
        error = string.Empty;

        foreach (DataGridViewRow row in _adjustmentsGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var field = CellText(row, "Field");
            var condition = CellText(row, "Condition");
            var delta = ParseNullableInt(CellText(row, "Delta")) ?? 0;
            if (string.IsNullOrWhiteSpace(field)
                && string.IsNullOrWhiteSpace(condition)
                && delta == 0)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(field))
            {
                continue;
            }

            adjustments.Add(new ModuleValueAdjustment
            {
                Enabled = CellBool(row, "Enabled", defaultValue: true),
                Field = field,
                Delta = delta,
                Formula = string.Empty,
                Condition = condition
            });
        }

        foreach (DataGridViewRow row in _formulaAdjustmentsGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var field = CellText(row, "Field");
            var formula = CellText(row, "Formula");
            if (string.IsNullOrWhiteSpace(field)
                && FormulaEvaluator.TrySplitAssignment(formula, out var formulaField, out var normalizedFormula))
            {
                field = formulaField;
                formula = normalizedFormula;
            }
            else
            {
                formula = FormulaEvaluator.NormalizeExpression(formula);
            }

            if (string.IsNullOrWhiteSpace(field) && string.IsNullOrWhiteSpace(formula))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(formula))
            {
                var rowNumber = row.Index + 1;
                error = string.IsNullOrWhiteSpace(field)
                    ? $"公式动态数值第 {rowNumber} 行缺少数值名称。请在“数值名称”里输入名称，或把公式写成“名称 = 表达式”。"
                    : $"公式动态数值第 {rowNumber} 行缺少公式。";
                return false;
            }

            adjustments.Add(new ModuleValueAdjustment
            {
                Enabled = CellBool(row, "Enabled", defaultValue: true),
                Field = field,
                Delta = 0,
                Formula = formula,
                Condition = string.Empty
            });
        }

        return true;
    }

    private List<ModuleRule> ReadRules()
    {
        var unitNames = new HashSet<string>(
            _units.Where(unit => !string.IsNullOrWhiteSpace(unit.Name)).Select(unit => unit.Name),
            StringComparer.Ordinal);
        var rules = new List<ModuleRule>();
        foreach (DataGridViewRow row in _rulesGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var condition = CellText(row, "Condition");
            var spell = CellText(row, "Spell");
            var unitText = CellText(row, "Unit");
            var macroCondition = CellText(row, "MacroCondition");
            var metadata = GetRuleMetadata(row);
            if (string.IsNullOrWhiteSpace(condition)
                && string.IsNullOrWhiteSpace(spell)
                && string.IsNullOrWhiteSpace(unitText)
                && string.IsNullOrWhiteSpace(macroCondition)
                && metadata.SubConditions.Count == 0
                && metadata.DelayMs is not > 0
                && metadata.LogicDelayMs is not > 0)
            {
                continue;
            }

            // 目标文本命中已定义动态单位名 → UnitName；否则把中文保留单位或数字槽位还原为 Unit。
            var isDynamic = unitNames.Contains(unitText);
            var subs = metadata.SubConditions
                .Select(sub => sub?.Trim() ?? string.Empty)
                .Where(sub => sub.Length > 0)
                .ToList();
            rules.Add(new ModuleRule
            {
                Enabled = CellBool(row, "Enabled", defaultValue: true),
                Condition = condition,
                Unit = isDynamic ? null : ReservedUnit.ParseDisplayText(unitText),
                UnitName = isDynamic ? unitText : null,
                Spell = spell,
                MacroCondition = MacroConditionText.ParseDisplayText(macroCondition),
                Hotkey = string.Empty,
                Step = string.Empty,
                SubConditions = subs is { Count: > 0 } ? subs : null,
                DelayMs = metadata.DelayMs,
                LogicDelayMs = metadata.LogicDelayMs
            });
        }

        return rules;
    }

    private static RuleRowMetadata GetRuleMetadata(DataGridViewRow row)
    {
        return row.Tag switch
        {
            RuleRowMetadata metadata => metadata,
            // 兼容本次升级前已经加载到控件中的旧 Tag 结构。
            List<string> subConditions => new RuleRowMetadata(subConditions),
            _ => new RuleRowMetadata()
        };
    }

    private static void AddMatchField(TableLayoutPanel row, string label, ComboBox box, int column)
    {
        var fieldLabel = CreateLabel(label);
        fieldLabel.Margin = Padding.Empty;
        row.Controls.Add(fieldLabel, column, 0);
        UiTheme.StyleComboBox(box);
        // 仅横向拉伸，让固定高度的 ComboBox 在行内垂直居中，与标签共用一条水平中心线。
        box.Dock = DockStyle.None;
        box.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        box.Margin = Padding.Empty;
        row.Controls.Add(box, column + 1, 0);
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
    }

    private static Label CreateSectionLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Padding = new Padding(0, 2, 0, 0)
        };
    }

    private static int MeasureLabelColumnWidth(string text, Font font)
    {
        return TextRenderer.MeasureText(text, font).Width + 18;
    }

    private static Button CreateUnitActionButton(string text, Color backColor, Color foreColor, bool bottomGap)
    {
        var button = UiTheme.CreateButton(text, backColor, foreColor);
        button.AutoSize = false;
        button.AutoEllipsis = true;
        button.Height = 36;
        button.Margin = new Padding(0, 0, 0, bottomGap ? 8 : 0);
        button.Padding = new Padding(0);
        button.TextAlign = ContentAlignment.MiddleCenter;
        return button;
    }

    private static void LayoutUnitActionButtons(FlowLayoutPanel panel)
    {
        var width = Math.Max(0, panel.ClientSize.Width);
        foreach (Control control in panel.Controls)
        {
            if (control is Button button)
            {
                button.Width = width;
            }
        }
    }

    private void SelectClass(int? value)
    {
        var index = FindMatchOption(_classBox, value);
        if (index < 0 && value is not null)
        {
            _classBox.Items.Add(new MatchOption($"职业{value} ({value})", value));
            index = _classBox.Items.Count - 1;
        }

        _classBox.SelectedIndex = index >= 0 ? index : 0;
        ResetSpecOptions(_specBox, ReadMatchCombo(_classBox));
    }

    private void SelectSpec(int? value)
    {
        var index = FindMatchOption(_specBox, value);
        if (index < 0 && value is not null)
        {
            _specBox.Items.Add(new MatchOption($"专精{value} ({value})", value));
            index = _specBox.Items.Count - 1;
        }

        _specBox.SelectedIndex = index >= 0 ? index : 0;
        ResetHeroTalentOptions(_heroTalentBox, ReadMatchCombo(_classBox), ReadMatchCombo(_specBox));
    }

    private void SelectHeroTalent(int? value)
    {
        var index = FindMatchOption(_heroTalentBox, value);
        if (index < 0 && value is not null)
        {
            _heroTalentBox.Items.Add(new MatchOption($"英雄天赋{value} ({value})", value));
            index = _heroTalentBox.Items.Count - 1;
        }

        _heroTalentBox.SelectedIndex = index >= 0 ? index : 0;
    }

    private static int? ReadMatchCombo(ComboBox comboBox)
    {
        return comboBox.SelectedItem is MatchOption option ? option.Value : null;
    }

    private static void ResetClassOptions(ComboBox comboBox)
    {
        comboBox.Items.Clear();
        comboBox.Items.AddRange(ClassOptions);
        comboBox.SelectedIndex = 0;
    }

    private static void ResetSpecOptions(ComboBox comboBox, int? classId)
    {
        comboBox.Items.Clear();
        comboBox.Items.Add(new MatchOption("任意 (*)", null));
        if (classId is not null)
        {
            foreach (var spec in ClassNames.GetSpecs(classId.Value))
            {
                comboBox.Items.Add(new MatchOption($"{spec.Name} ({spec.Id})", spec.Id));
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private static void ResetHeroTalentOptions(ComboBox comboBox, int? classId, int? specId)
    {
        comboBox.Items.Clear();
        comboBox.Items.Add(new MatchOption("任意 (*)", null));
        if (classId is not null && specId is not null)
        {
            foreach (var heroTalent in ClassNames.GetHeroTalents(classId.Value, specId.Value))
            {
                comboBox.Items.Add(new MatchOption($"{heroTalent.Name} ({heroTalent.Id})", heroTalent.Id));
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private static int FindMatchOption(ComboBox comboBox, int? value)
    {
        for (var i = 0; i < comboBox.Items.Count; i++)
        {
            if (comboBox.Items[i] is MatchOption option && option.Value == value)
            {
                return i;
            }
        }

        return -1;
    }

    private static MatchOption[] BuildClassOptions()
    {
        return ClassNames.GetClasses()
            .Select(item => new MatchOption($"{item.Name} ({item.Id})", item.Id))
            .Prepend(new MatchOption("任意 (*)", null))
            .ToArray();
    }

    private void SelectPartyType(string? value)
    {
        ResetPartyTypeOptions(_partyTypeBox);
        var normalized = ModuleMatch.NormalizePartyTypeValue(value);
        var index = FindPartyTypeOption(normalized);
        if (index < 0 && !string.IsNullOrWhiteSpace(normalized))
        {
            _partyTypeBox.Items.Add(new PartyTypeOption($"自定义 ({normalized})", normalized));
            index = _partyTypeBox.Items.Count - 1;
        }

        _partyTypeBox.SelectedIndex = index >= 0 ? index : 0;
    }

    private string? ReadPartyTypeCombo()
    {
        return _partyTypeBox.SelectedItem is PartyTypeOption option ? option.Value : null;
    }

    private static void ResetPartyTypeOptions(ComboBox comboBox)
    {
        comboBox.Items.Clear();
        comboBox.Items.AddRange(PartyTypeOptions);
        comboBox.SelectedIndex = 0;
    }

    private static int FindPartyTypeOption(string? value)
    {
        for (var i = 0; i < PartyTypeOptions.Length; i++)
        {
            if (string.Equals(PartyTypeOptions[i].Value, value, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static string CellText(DataGridViewRow row, string columnName)
    {
        return row.Cells[columnName].Value?.ToString()?.Trim() ?? string.Empty;
    }

    private static bool CellBool(DataGridViewRow row, string columnName, bool defaultValue)
    {
        var value = row.Cells[columnName].Value;
        return value switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            null => defaultValue,
            _ => defaultValue
        };
    }

    private static int? ParseNullableInt(string text)
    {
        return int.TryParse(text, out var value) ? value : null;
    }

    private sealed record PartyTypeOption(string Text, string? Value)
    {
        public override string ToString()
        {
            return Text;
        }
    }

    private sealed record MatchOption(string Text, int? Value)
    {
        public override string ToString()
        {
            return Text;
        }
    }
}
