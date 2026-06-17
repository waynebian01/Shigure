using System.Drawing;

namespace Shigure;

public sealed class ModuleEditorControl : UserControl
{
    private readonly ModuleStore _moduleStore;
    private readonly Action _runtimeRestartRequested;
    private readonly ConditionFieldCatalog _fieldCatalog;
    private readonly KeymapCatalog _keymapCatalog;
    private readonly ListBox _moduleList = new();
    private readonly TextBox _nameBox = new();
    private readonly ComboBox _classBox = new();
    private readonly ComboBox _specBox = new();
    private readonly ComboBox _partyTypeBox = new();
    private readonly ComboBox _heroTalentBox = new();
    private readonly DataGridView _rulesGrid = new();
    private readonly DataGridView _adjustmentsGrid = new();
    private readonly DataGridView _formulaAdjustmentsGrid = new();
    private readonly DataGridViewComboBoxColumn _spellColumn = new();
    private readonly DataGridViewComboBoxColumn _unitColumn = new();
    private readonly DataGridViewComboBoxColumn _adjustmentFieldColumn = new();
    private readonly DataGridViewComboBoxColumn _adjustmentTypeColumn = new();
    private readonly ListView _unitsList = new();
    private readonly Label _pathLabel = new();
    private readonly Label _unitsEmptyHint = new();
    private readonly Label _editorEmptyHint = new();
    private Button _saveButton = null!;
    private Button _deleteButton = null!;
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
    // 程序化恢复列宽时置真, 避免 ColumnWidthChanged 把默认值回写覆盖用户保存的宽度。
    private bool _suppressColumnSave;
    private bool _suppressUnitsColumnResize;
    // 载入时程序化写入"类型"单元格会触发 CellValueChanged; 置真以跳过"按类型清空数值"的联动。
    private bool _suppressAdjustmentTypeChange;
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
    // 这三列固定宽度并缓存; "条件"列为 Fill, 图标列固定且不缓存。
    private static readonly string[] FixedWidthColumns = ["Enabled", "Spell", "Unit"];
    private static readonly HashSet<string> NonAuraGroupFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "生命值",
        "职责",
        "驱散"
    };
    // 条件动态数值"类型"下拉: 决定"数值"可选项的过滤类别, 顺序与界面一致。
    private static readonly (string Text, ConditionFieldCategory Category)[] AdjustmentTypeOptions =
    [
        ("状态", ConditionFieldCategory.State),
        ("技能", ConditionFieldCategory.Spell),
        ("光环", ConditionFieldCategory.Aura),
        ("动态单位", ConditionFieldCategory.DynamicUnit)
    ];

    public ModuleEditorControl(ModuleStore moduleStore, Action runtimeRestartRequested, string baseDirectory)
    {
        _moduleStore = moduleStore;
        _runtimeRestartRequested = runtimeRestartRequested;
        _fieldCatalog = ConditionFieldCatalog.Load(baseDirectory);
        _keymapCatalog = KeymapCatalog.Load(baseDirectory);
        InitializeComponent();
        LoadModules();
    }

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
            RowCount = 1,
            Margin = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);

        root.Controls.Add(BuildSidebar(), 0, 0);
        root.Controls.Add(BuildEditor(), 1, 0);
    }

    private Control BuildSidebar()
    {
        var sidebar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Background,
            Padding = new Padding(0, 0, 10, 0),
            ColumnCount = 1,
            RowCount = 2
        };
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        _moduleList.Dock = DockStyle.Fill;
        UiTheme.StyleListBox(_moduleList, Font);
        _moduleList.BackColor = UiTheme.Background;
        _moduleList.SelectedIndexChanged += (_, _) => SelectModule(_moduleList.SelectedIndex);
        sidebar.Controls.Add(_moduleList, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = UiTheme.Background,
            Margin = new Padding(0)
        };

        var addButton = UiTheme.CreateButton("新建", UiTheme.Field, UiTheme.Text);
        addButton.Width = 72;
        addButton.Height = 30;
        addButton.Click += (_, _) => AddModule();

        var reloadButton = UiTheme.CreateButton("刷新", UiTheme.Field, UiTheme.Text);
        reloadButton.Width = 72;
        reloadButton.Height = 30;
        reloadButton.Click += (_, _) => LoadModules();

        buttons.Controls.Add(addButton);
        buttons.Controls.Add(reloadButton);
        sidebar.Controls.Add(buttons, 0, 1);

        return sidebar;
    }

    private Control BuildEditor()
    {
        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding = new Padding(8, 0, 0, 4),
            ColumnCount = 1,
            RowCount = 4
        };
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        editor.Controls.Add(BuildNameRow(), 0, 0);
        editor.Controls.Add(BuildMatchRow(), 0, 1);
        editor.Controls.Add(BuildEditorTabs(), 0, 2);
        editor.Controls.Add(BuildActionRow(), 0, 3);
        return editor;
    }

    private Control BuildEditorTabs()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 6),
            Padding = new Padding(0),
            BackColor = UiTheme.Surface,
            ColumnCount = 1,
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var tabBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        tabBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (var i = 0; i < 3; i++)
        {
            tabBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3F));
        }

        var contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0)
        };

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

        var labels = new Label[3];
        var selectedIndex = -1;

        void SelectTab(int index)
        {
            if (selectedIndex == index)
            {
                return;
            }

            selectedIndex = index;
            for (var i = 0; i < labels.Length; i++)
            {
                var selected = i == index;
                labels[i].BackColor = selected ? UiTheme.Field : UiTheme.Surface;
                labels[i].ForeColor = selected ? UiTheme.Text : UiTheme.Muted;
                labels[i].Invalidate();
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
            var label = new Label
            {
                Text = titles[i],
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false,
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.Muted,
                Cursor = Cursors.Hand,
                Margin = new Padding(i == 0 ? 0 : 1, 0, 0, 0)
            };
            label.Click += (_, _) => SelectTab(index);
            label.MouseEnter += (_, _) =>
            {
                if (selectedIndex != index)
                {
                    label.BackColor = UiTheme.Hover;
                }
            };
            label.MouseLeave += (_, _) =>
            {
                if (selectedIndex != index)
                {
                    label.BackColor = UiTheme.Surface;
                }
            };
            label.Paint += (_, e) =>
            {
                if (selectedIndex != index)
                {
                    return;
                }

                using var accent = new SolidBrush(UiTheme.Accent);
                e.Graphics.FillRectangle(accent, 8, label.Height - 3, Math.Max(0, label.Width - 16), 2);
            };
            // 标签随窗口/侧栏宽度变化时重绘, 否则选中下划线会停留在旧宽度。
            label.SizeChanged += (_, _) => label.Invalidate();

            labels[i] = label;
            tabBar.Controls.Add(label, i, 0);
        }

        root.Controls.Add(tabBar, 0, 0);
        root.Controls.Add(contentHost, 0, 1);
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
            Padding = new Padding(10),
            Margin = new Padding(0)
        };

        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
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
            Padding = new Padding(10),
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
            Padding = new Padding(10),
            Margin = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        foreach (var column in new[] { ("名称", 210), ("类型", 80), ("摘要", 160) })
        {
            _unitsList.Columns.Add(column.Item1, column.Item2);
        }

        _unitsList.MultiSelect = false;
        UiTheme.StyleListView(_unitsList, Font);
        _unitsList.DoubleClick += (_, _) => EditSelectedUnit();
        _unitsList.KeyDown += OnUnitsListKeyDown;
        _unitsList.Resize += (_, _) => StretchUnitsSummaryColumn();
        _unitsList.HandleCreated += (_, _) => StretchUnitsSummaryColumn();
        _unitsList.ColumnWidthChanged += (_, _) =>
        {
            if (!_suppressUnitsColumnResize)
            {
                StretchUnitsSummaryColumn();
            }
        };

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
            Margin = new Padding(6, 0, 0, 0),
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
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(10, 8, 10, 2),
            Margin = new Padding(0, 0, 0, 8)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        row.Controls.Add(CreateLabel("名称"), 0, 0);
        UiTheme.StyleTextBox(_nameBox);
        _nameBox.Dock = DockStyle.Fill;
        row.Controls.Add(_nameBox, 1, 0);

        _pathLabel.Dock = DockStyle.Fill;
        _pathLabel.ForeColor = UiTheme.Muted;
        _pathLabel.TextAlign = ContentAlignment.MiddleLeft;
        _pathLabel.AutoEllipsis = true;
        row.Controls.Add(_pathLabel, 0, 1);
        row.SetColumnSpan(_pathLabel, 2);

        return row;
    }

    private Control BuildMatchRow()
    {
        var matchLabels = new[] { "职业", "专精", "英雄天赋", "队伍类型" };

        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = 8,
            RowCount = 2,
            Padding = new Padding(10, 8, 10, 8),
            Margin = new Padding(0)
        };
        foreach (var label in matchLabels)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, MeasureLabelColumnWidth(label, Font)));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }

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
        AddMatchField(row, "专精:", _specBox, 2);
        AddMatchField(row, "英雄天赋:", _heroTalentBox, 4);
        AddMatchField(row, "队伍类型:", _partyTypeBox, 6);
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
            Width = 100,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None
        });

        _adjustmentFieldColumn.Name = "Field";
        _adjustmentFieldColumn.HeaderText = "数值";
        _adjustmentFieldColumn.Width = 160;
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
        _adjustmentTypeColumn.Width = 84;
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
            Width = 86,
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

        // 启用/技能/目标 三列宽度固定可调并缓存; 条件列用 Fill 自动充满剩余窗口。
        _rulesGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Enabled",
            HeaderText = "启用",
            Width = 120,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None
        });
        _spellColumn.Name = "Spell";
        _spellColumn.HeaderText = "技能";
        _spellColumn.Width = 150;
        _spellColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _spellColumn.FlatStyle = FlatStyle.Flat;
        _rulesGrid.Columns.Add(_spellColumn);
        _unitColumn.Name = "Unit";
        _unitColumn.HeaderText = "目标";
        _unitColumn.Width = 150;
        _unitColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _unitColumn.FlatStyle = FlatStyle.Flat;
        _rulesGrid.Columns.Add(_unitColumn);
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Condition",
            HeaderText = "条件 (点击编辑)",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 220,
            ReadOnly = true
        });
        AddRuleIconColumn("MoveUp", "▲", "上移");
        AddRuleIconColumn("MoveDown", "▼", "下移");
        AddRuleIconColumn("Copy", "⧉", "复制到下一行");
        AddRuleIconColumn("InsertBlank", "+", "在下一行添加空白条件");
        AddRuleIconColumn("Delete", "×", "删除", UiTheme.Danger);

        // 拖拽手柄列: 加在集合末尾(保持 Rows.Add 的位置参数仍对应 启用/技能/目标/条件),
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
            HeaderText = "编号",
            Width = 48,
            MinimumWidth = 48,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Resizable = DataGridViewTriState.False,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _rulesGrid.Columns["RuleNumber"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _rulesGrid.Columns["RuleNumber"]!.DefaultCellStyle.ForeColor = UiTheme.Muted;
        _rulesGrid.Columns["RuleNumber"]!.DisplayIndex = 0;
        _rulesGrid.Columns["Drag"]!.DisplayIndex = 1;

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
        _rulesGrid.ColumnWidthChanged += OnColumnWidthChanged;
        _rulesGrid.CellValueChanged += OnRulesGridCellValueChanged;
        // 组合框改值默认要等失焦才提交; 立即提交以便"目标"随"技能"实时联动。
        _rulesGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_rulesGrid.IsCurrentCellDirty && _rulesGrid.CurrentCell is DataGridViewComboBoxCell)
            {
                _rulesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        RefreshKeymapColumns();
        ApplyColumnWidths(UiCacheStore.Load().ModuleRulesGridColumns);

        return _rulesGrid;
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
    /// 按当前选中职业的 keymap 重建“技能/目标”下拉选项。
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
            _unitColumn.Items.Add(unit.ToString());
        }

        foreach (DataGridViewRow row in _rulesGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            EnsureComboItem(_spellColumn, row.Cells["Spell"].Value);
            UpdateUnitCellItems(row);
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
            cell.Items.Add("0");
            cell.Value = "0";
            return;
        }

        var classId = ReadMatchCombo(_classBox);
        var allowed = ModuleSpecialActions.IsFailedSpell(spell)
            ? _keymapCatalog.GetUnitsForSpells(classId, _keymapCatalog.GetFailedSpellNames(classId))
            : _keymapCatalog.GetUnitsForSpell(classId, spell);

        foreach (var unit in allowed)
        {
            cell.Items.Add(unit.ToString());
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
            // 技能切换导致旧的数字目标非法, 清空。
            cell.Value = string.Empty;
        }
    }

    private void OnRulesGridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        // 技能改变时联动刷新该行"目标"的可选值。
        if (_rulesGrid.Columns[e.ColumnIndex].Name == "Spell")
        {
            UpdateUnitCellItems(_rulesGrid.Rows[e.RowIndex]);
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

    // 优先按目录里的字段类别判定; 目录外的自定义字段按 auras./spells. 前缀兜底, 其余归为状态。
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

        return ConditionFieldCategory.State;
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

        foreach (var fieldName in GetAdjustmentTargetFields())
        {
            AddAdjustmentField(fields, seen, fieldName, $"{fieldName} (动态数值)", ConditionFieldCategory.State);
        }

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
                AddAdjustmentField(fields, seen, count.Name, $"人数: {count.Name}", ConditionFieldCategory.DynamicUnit);
            }
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
        // 阈值字段仅取状态/动态单位(可加减数值), 排除新加入"数值"选项的技能/光环字段。
        return BuildAdjustmentFields()
            .Where(field => field.Category is ConditionFieldCategory.State or ConditionFieldCategory.DynamicUnit)
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

    private sealed record RuleRowValues(bool Enabled, string Spell, string UnitText, string Condition, IReadOnlyList<string> SubConditions);

    private void ApplyColumnWidths(Dictionary<string, int>? widths)
    {
        if (widths is null || widths.Count == 0)
        {
            return;
        }

        _suppressColumnSave = true;
        try
        {
            foreach (var name in FixedWidthColumns)
            {
                if (widths.TryGetValue(name, out var width) && width > 0)
                {
                    _rulesGrid.Columns[name]!.Width = width;
                }
            }
        }
        finally
        {
            _suppressColumnSave = false;
        }
    }

    private void OnColumnWidthChanged(object? sender, DataGridViewColumnEventArgs e)
    {
        // Fill 列(条件)宽度随窗口/其它列变化, 不参与保存; 程序化恢复期间也跳过。
        if (_suppressColumnSave || e.Column.Name == "Condition")
        {
            return;
        }

        SaveColumnWidths();
    }

    private void SaveColumnWidths()
    {
        var cache = UiCacheStore.Load();
        cache.ModuleRulesGridColumns ??= new();

        foreach (var name in FixedWidthColumns)
        {
            cache.ModuleRulesGridColumns[name] = _rulesGrid.Columns[name]!.Width;
        }

        UiCacheStore.Save(cache);
    }

    private void OnRulesGridCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        var columnName = _rulesGrid.Columns[e.ColumnIndex].Name;
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

    private void OnRulesGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        var columnName = _rulesGrid.Columns[e.ColumnIndex].Name;
        var row = _rulesGrid.Rows[e.RowIndex];
        if (columnName == "RuleNumber")
        {
            e.Value = row.IsNewRow ? string.Empty : (e.RowIndex + 1).ToString();
            e.FormattingApplied = true;
            return;
        }

        // 「条件」列在有子条件时显示成 "主条件 且任一(子1 | 子2)"; 仅改显示, 底层值仍是主条件, 不影响 ReadRules 存盘。
        if (columnName == "Condition" && !row.IsNewRow)
        {
            e.Value = DecorateCondition(e.Value?.ToString() ?? string.Empty, row.Tag as List<string>);
            e.FormattingApplied = true;
        }
    }

    // 把主条件与子条件合成可读文本(与 ModuleRule.DescribeCondition / 弹窗预览同形)。无子条件时原样返回。
    private static string DecorateCondition(string main, List<string>? subs)
    {
        if (subs is not { Count: > 0 })
        {
            return main;
        }

        var any = string.Join(" | ", subs);
        return main.Length == 0 ? $"任一({any})" : $"{main}  且任一({any})";
    }

    private void OnRulesGridCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        var columnName = _rulesGrid.Columns[e.ColumnIndex].Name;
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

    // 图标列沿用原提示; 条件/技能/目标三列在文本被列宽截断或可点击时给出悬停提示。
    private string GetRuleCellToolTip(string columnName, int rowIndex, int columnIndex)
    {
        if (columnName is "MoveUp" or "MoveDown" or "Copy" or "InsertBlank" or "Delete")
        {
            return GetRuleIconToolTip(columnName, rowIndex);
        }

        if (rowIndex >= _rulesGrid.Rows.Count || _rulesGrid.Rows[rowIndex].IsNewRow)
        {
            return string.Empty;
        }

        if (columnName == "Drag")
        {
            return "拖动调整顺序";
        }

        if (columnName is not ("Condition" or "Spell" or "Unit"))
        {
            return string.Empty;
        }

        var row = _rulesGrid.Rows[rowIndex];
        var text = CellText(row, columnName);
        if (columnName == "Condition")
        {
            // 提示与裁剪检测都用合成后的完整文本(含子条件), 与单元格显示一致。
            text = DecorateCondition(text, row.Tag as List<string>);
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

        using var editor = new ConditionEditorForm(BuildConditionFields(), current);
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

        InsertRuleAfter(rowIndex, new RuleRowValues(true, string.Empty, string.Empty, string.Empty, Array.Empty<string>()));
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
            CellText(row, "Condition"),
            // 子条件挂在 row.Tag, 随行一起被移动/拖拽/复制搬运。
            row.Tag as List<string> ?? new List<string>());
    }

    private void WriteRuleRow(DataGridViewRow row, RuleRowValues values)
    {
        row.Cells["Enabled"].Value = values.Enabled;
        EnsureComboItem(_spellColumn, values.Spell);
        row.Cells["Spell"].Value = values.Spell;
        row.Cells["Condition"].Value = values.Condition;
        row.Tag = new List<string>(values.SubConditions);
        RebuildUnitCell(row, values.UnitText);
    }

    private void OpenConditionEditor(int rowIndex)
    {
        var row = _rulesGrid.Rows[rowIndex];
        var current = row.IsNewRow ? string.Empty : CellText(row, "Condition");
        var currentSubs = row.IsNewRow ? null : row.Tag as List<string>;
        var fields = BuildConditionFields();

        using var editor = new ConditionEditorForm(fields, current, currentSubs, allowSubConditions: true);
        if (editor.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        var subs = new List<string>(editor.SubConditions);
        if (row.IsNewRow)
        {
            // 新行占位符不能直接赋值, 改为追加一行(主条件或子条件任一非空即可)。
            if (!string.IsNullOrWhiteSpace(editor.ConditionText) || subs.Count > 0)
            {
                var index = _rulesGrid.Rows.Add(true, string.Empty, string.Empty, editor.ConditionText);
                _rulesGrid.Rows[index].Tag = subs;
            }

            return;
        }

        row.Cells["Condition"].Value = editor.ConditionText;
        row.Tag = subs;
        // 让「条件」列的装饰显示(主条件 且任一(…))立即刷新。
        _rulesGrid.InvalidateRow(rowIndex);
    }

    // 条件字段 = 状态/技能字段 + 每个动态单位的裸名(存在)和值名称 + 每个数量名。
    private IReadOnlyList<ConditionField> BuildConditionFields()
    {
        var classId = ReadMatchCombo(_classBox);
        var specId = ReadMatchCombo(_specBox);
        var fields = new List<ConditionField>(_fieldCatalog.GetFields(classId, specId));
        var seen = new HashSet<string>(fields.Select(field => field.Name), StringComparer.Ordinal);

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
                fields.Add(new ConditionField(count.Name, $"人数: {count.Name}", ConditionFieldType.Int, ConditionFieldCategory.DynamicUnit));
            }
        }

        return fields;
    }

    private Control BuildActionRow()
    {
        var row = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0),
            Padding = new Padding(10, 0, 10, 0)
        };

        var hint = new Label
        {
            Text = "目标可选 keymap 编号或上方定义的动态单位；点击“条件”列打开可视化编辑器",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        };

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0),
            Padding = new Padding(0, 6, 0, 6)
        };

        _saveButton = UiTheme.CreateButton("保存", UiTheme.Accent, Color.Black);
        _saveButton.Margin = new Padding(8, 0, 0, 0);
        _saveButton.Click += (_, _) => SaveSelectedModule();

        _deleteButton = UiTheme.CreateButton("删除", UiTheme.Field, UiTheme.Danger);
        _deleteButton.Margin = new Padding(8, 0, 0, 0);
        _deleteButton.Click += (_, _) => DeleteSelectedModule();

        buttons.Controls.Add(_saveButton);
        buttons.Controls.Add(_deleteButton);
        row.Controls.Add(hint);
        row.Controls.Add(buttons);
        return row;
    }

    private void LoadModules()
    {
        _moduleStore.Reload();
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
        ApplyColumnWidths(UiCacheStore.Load().ModuleRulesGridColumns);

        foreach (var rule in module.Rules)
        {
            // 动态目标优先显示单位名, 否则显示数字单位。
            var unitText = !string.IsNullOrWhiteSpace(rule.UnitName)
                ? rule.UnitName!
                : rule.Unit?.ToString() ?? string.Empty;
            EnsureComboItem(_spellColumn, rule.Spell);
            // 先加行(目标先留空), 再按技能重建目标选项并写回目标值, 避免值不在选项内被吞掉。
            var index = _rulesGrid.Rows.Add(rule.Enabled, rule.Spell, string.Empty, rule.Condition);
            _rulesGrid.Rows[index].Tag = rule.SubConditions is null
                ? new List<string>()
                : new List<string>(rule.SubConditions);
            RebuildUnitCell(_rulesGrid.Rows[index], unitText);
        }
    }

    private void ClearEditor()
    {
        _selectedModule = null;
        _nameBox.Clear();
        _units.Clear();
        _counts.Clear();
        _valueAdjustments.Clear();
        RefreshUnitsList();
        SelectClass(null);
        SelectSpec(null);
        SelectPartyType(null);
        SelectHeroTalent(null);
        _pathLabel.Text = "无模块";
        _adjustmentsGrid.Rows.Clear();
        _formulaAdjustmentsGrid.Rows.Clear();
        RefreshAdjustmentFieldColumn();
        _rulesGrid.Rows.Clear();
        SetEditorEnabled(hasModule: false);
    }

    // 无选中模块时禁用保存/删除(否则点了静默无反应), 并在编辑区显示引导提示。
    private void SetEditorEnabled(bool hasModule)
    {
        _saveButton.Enabled = hasModule;
        _deleteButton.Enabled = hasModule;
        _editorEmptyHint.Visible = !hasModule;
        if (!hasModule)
        {
            _editorEmptyHint.BringToFront();
        }
    }

    private void AddModule()
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

        LoadModules();
        var index = _modules.FindIndex(existing => string.Equals(existing.Id, module.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            _moduleList.SelectedIndex = index;
        }

        _runtimeRestartRequested();
    }

    private void SaveSelectedModule()
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
        try
        {
            saved = _moduleStore.Save(module);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        LoadModules();
        var index = _modules.FindIndex(existing => string.Equals(existing.Id, saved.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            _moduleList.SelectedIndex = index;
        }

        _runtimeRestartRequested();
    }

    private void DeleteSelectedModule()
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
        LoadModules();
        _runtimeRestartRequested();
    }

    private bool TryReadModule(out ModuleDefinition module)
    {
        module = _selectedModule!.Clone();
        module.Name = string.IsNullOrWhiteSpace(_nameBox.Text) ? "新模块" : _nameBox.Text.Trim();
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
            if (string.IsNullOrWhiteSpace(condition)
                && string.IsNullOrWhiteSpace(spell)
                && string.IsNullOrWhiteSpace(unitText))
            {
                continue;
            }

            // 目标文本命中已定义动态单位名 → UnitName; 否则按数字 → Unit; 都不是则留空。
            var isDynamic = unitNames.Contains(unitText);
            var subs = (row.Tag as List<string>)?
                .Select(sub => sub?.Trim() ?? string.Empty)
                .Where(sub => sub.Length > 0)
                .ToList();
            rules.Add(new ModuleRule
            {
                Enabled = CellBool(row, "Enabled", defaultValue: true),
                Condition = condition,
                Unit = isDynamic ? null : ParseNullableInt(unitText),
                UnitName = isDynamic ? unitText : null,
                Spell = spell,
                Hotkey = string.Empty,
                Step = string.Empty,
                SubConditions = subs is { Count: > 0 } ? subs : null
            });
        }

        return rules;
    }

    private static void AddMatchField(TableLayoutPanel row, string label, ComboBox box, int column)
    {
        row.Controls.Add(CreateLabel(label), column, 0);
        UiTheme.StyleComboBox(box);
        box.Dock = DockStyle.Fill;
        row.Controls.Add(box, column + 1, 0);
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
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
        button.Height = 32;
        button.Margin = new Padding(0, 0, 0, bottomGap ? 6 : 0);
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

    private void StretchUnitsSummaryColumn()
    {
        if (_unitsList.Columns.Count < 3 || _suppressUnitsColumnResize)
        {
            return;
        }

        _suppressUnitsColumnResize = true;
        try
        {
            var summaryWidth = _unitsList.ClientSize.Width
                - _unitsList.Columns[0].Width
                - _unitsList.Columns[1].Width;
            _unitsList.Columns[2].Width = Math.Max(120, summaryWidth);
        }
        finally
        {
            _suppressUnitsColumnResize = false;
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
