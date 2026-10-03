using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Shigure;

public sealed class ModuleEditorControl : UserControl
{
    private const string ModuleWebsiteUrl = "https://www.shigure.club";
    // 使用独立列名避开旧版“注释”文字列缓存的较大宽度；新图标列从紧凑宽度重新开始缓存。
    private const string RuleCommentColumnName = "RuleComment";

    private ModuleStore _moduleStore;
    private readonly Func<Task> _runtimeRestartRequested;
    private readonly Func<ModuleDefinition, string?> _captureDependencies;
    private readonly Func<Task> _modulesReloadRequested;
    private readonly Func<GameProfile> _resolveProfile;
    private ConditionFieldCatalog _fieldCatalog;
    private KeymapCatalog _keymapCatalog;
    private readonly UiThemedListBox _moduleList = new();
    private readonly TextBox _nameBox = new UiThemedTextBox();
    private readonly TextBox _authorBox = new UiThemedTextBox();
    private readonly TextBox _recommendedTalentBox = new UiThemedTextBox();
    private readonly UiDropDown _classBox = new();
    private readonly UiDropDown _specBox = new();
    private readonly UiDropDown _partyTypeBox = new();
    private readonly UiDropDown _heroTalentBox = new();
    private TableLayoutPanel? _matchRow;
    private Label? _specLabel;
    private Label? _heroTalentLabel;
    private readonly DataGridView _rulesGrid = new UiThemedDataGridView();
    private readonly DataGridView _adjustmentsGrid = new UiThemedDataGridView();
    private readonly DataGridView _formulaAdjustmentsGrid = new UiThemedDataGridView();
    private readonly DataGridViewComboBoxColumn _spellColumn = new();
    private readonly DataGridViewComboBoxColumn _unitColumn = new();
    private readonly DataGridViewComboBoxColumn _macroConditionColumn = new();
    private ToolStripDropDown? _rulesComboDropDown;
    private ToolStripDropDown? _adjustmentComboDropDown;
    private readonly DataGridViewTextBoxColumn _adjustmentFieldColumn = new();
    private readonly DataGridViewComboBoxColumn _adjustmentTypeColumn = new();
    private readonly ListView _unitsList = new UiThemedListView();
    private readonly ListView _numberArraysList = new UiThemedListView();
    private readonly Label _pathLabel = new();
    private readonly Label _unitsEmptyHint = new();
    private readonly Label _editorEmptyHint = new();
    private readonly ToolTip _pathToolTip = new();
    private Button _saveButton = null!;
    private Button _saveAllButton = null!;
    private Button _deleteButton = null!;
    private Button _addButton = null!;
    private Button _reloadButton = null!;
    private Button _ruleViewButton = null!;
    private bool _relaxedRuleView;
    private bool _suppressNextRulesCellClick;
    private bool _relaxedCommentSyncing;
    private bool _relaxedCommentSyncQueued;
    private readonly List<TextBox> _relaxedCommentBoxes = new();
    private readonly ToolTip _rulesGridToolTip = new()
    {
        InitialDelay = 300,
        ReshowDelay = 100,
        AutoPopDelay = 4000,
        ShowAlways = true
    };
    private readonly ClassIconStrip _classFilterStrip = new();
    private System.Windows.Forms.Timer? _topAreaResizeTimer;
    private List<ModuleDefinition> _allModules = new();
    private List<ModuleDefinition> _modules = new();
    private int? _filterClassId;
    private ModuleDefinition? _selectedModule;
    private string? _editorBaseline;
    // 当前编辑中模块的动态单位/数量字段(含未保存的新增), 供目标下拉与条件字段使用。
    private readonly List<ModuleUnit> _units = new();
    private readonly List<ModuleCountField> _counts = new();
    private readonly List<ModuleEnemyCountField> _enemyCounts = new();
    private readonly List<ModuleAverageHealthField> _averageHealthFields = new();
    private readonly List<ModuleValueAdjustment> _valueAdjustments = new();
    private readonly List<ModuleNumberArray> _numberArrays = new();
    private readonly Dictionary<string, List<long>> _currentClassSpellIdsByName =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<long>> _currentSpecItemIdsByName =
        new(StringComparer.Ordinal);
    private readonly List<ConditionSpell> _currentClassConditionSpells = new();
    private readonly List<ConditionItem> _currentClassConditionItems = new();
    private HashSet<string>? _availableConditionFields;
    private HashSet<string>? _availableGroupConditionFields;
    private Dictionary<string, string>? _conditionFieldDisplayNames;
    // 载入时程序化写入"类型"单元格会触发 CellValueChanged; 置真以跳过"按类型清空字段"的联动。
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
    private static readonly HashSet<string> NonAuraGroupFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "生命值",
        "职责",
        "驱散",
        "职业",
        "治疗吸收"
    };
    // 条件动态数值"类型"下拉: 决定"字段"可选项的过滤类别, 顺序与界面一致。
    private static readonly (string Text, ConditionFieldCategory Category)[] AdjustmentTypeOptions =
    [
        ("状态", ConditionFieldCategory.State),
        ("技能", ConditionFieldCategory.Spell),
        ("光环", ConditionFieldCategory.Aura),
        ("动态单位", ConditionFieldCategory.DynamicUnit),
        ("动态数值", ConditionFieldCategory.DynamicValue),
        ("自建", ConditionFieldCategory.Shigure)
    ];

    internal ModuleEditorControl(
        ModuleStore moduleStore,
        Func<Task> runtimeRestartRequested,
        Func<ModuleDefinition, string?> captureDependencies,
        Func<Task> modulesReloadRequested,
        Func<GameProfile> resolveProfile)
    {
        _moduleStore = moduleStore;
        _runtimeRestartRequested = runtimeRestartRequested;
        _captureDependencies = captureDependencies;
        _modulesReloadRequested = modulesReloadRequested;
        _resolveProfile = resolveProfile;
        _fieldCatalog = ConditionFieldCatalog.Load(resolveProfile().RuntimeDirectory, resolveProfile().AddonRoot);
        _keymapCatalog = KeymapCatalog.Load(resolveProfile().RuntimeDirectory, resolveProfile().AddonRoot);
        InitializeComponent();
        SpellIconCatalog.CatalogChanged += OnSpellIconCatalogChanged;
        LoadModules(reloadStore: false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CloseRulesComboDropDown();
            CloseAdjustmentComboDropDown();
            SpellIconCatalog.CatalogChanged -= OnSpellIconCatalogChanged;
        }

        base.Dispose(disposing);
    }

    public void ReloadCatalogs()
    {
        var wasDirty = HasUnsavedChanges;
        _fieldCatalog = ConditionFieldCatalog.Load(_resolveProfile().RuntimeDirectory, _resolveProfile().AddonRoot);
        _keymapCatalog = KeymapCatalog.Load(_resolveProfile().RuntimeDirectory, _resolveProfile().AddonRoot);
        UpdateMatchRowProfile();
        var selectedClassId = ReadMatchCombo(_classBox);
        ResetClassOptions(_classBox);
        if (selectedClassId is not null)
        {
            var index = FindMatchOption(_classBox, selectedClassId);
            if (index >= 0) _classBox.SelectedIndex = index;
        }
        var filterItems = new List<(int? ClassId, string Tooltip)> { (null, "全部") };
        filterItems.AddRange(GetAvailableClasses().Select(item => ((int?)item.Id, item.Name)));
        _classFilterStrip.SetItems(filterItems);
        _classFilterStrip.SelectClassId(null);
        ReloadCurrentClassSpellIds();
        // “更新配置”可能刚重建了 keymap；立即刷新当前规则的技能/目标/宏条件下拉，
        // 避免必须切换职业或重启应用后才能看到新解析出的宏条件。
        RefreshKeymapColumns();
        RefreshAdjustmentFieldColumn();
        RefreshRuleSpellIcons();
        _rulesGrid.Invalidate();
        if (!wasDirty && _selectedModule is not null)
            _editorBaseline = CaptureEditorFingerprint();
    }

    internal bool HasUnsavedChanges => _selectedModule is not null
        && !string.Equals(_editorBaseline, CaptureEditorFingerprint(), StringComparison.Ordinal);

    internal void UseModuleStore(ModuleStore store)
    {
        if (ReferenceEquals(_moduleStore, store)) return;
        _moduleStore = store;
        ClearEditor();
        ReloadCatalogs();
        LoadModules(reloadStore: false);
    }

    private string CaptureEditorFingerprint()
    {
        static object[] GridRows(DataGridView grid) => grid.Rows.Cast<DataGridViewRow>()
            .Where(row => !row.IsNewRow)
            .Select(row => (object)new
            {
                Cells = row.Cells.Cast<DataGridViewCell>()
                    .Select(cell => ReferenceEquals(grid.CurrentCell, cell) && grid.IsCurrentCellInEditMode
                        ? cell.EditedFormattedValue?.ToString()
                        : cell.Value?.ToString()).ToArray(),
                Metadata = row.Tag is RuleRowMetadata metadata
                    ? (object)new
                    {
                        metadata.SubConditions,
                        metadata.DelayMs,
                        metadata.LogicDelayMs,
                        metadata.ContinueLogic
                    }
                    : row.Tag
            }).ToArray();

        return System.Text.Json.JsonSerializer.Serialize(new
        {
            Name = _nameBox.Text,
            Author = _authorBox.Text,
            RecommendedTalent = _recommendedTalentBox.Text,
            Class = _classBox.SelectedIndex,
            Spec = _specBox.SelectedIndex,
            Party = _partyTypeBox.SelectedIndex,
            Hero = _heroTalentBox.SelectedIndex,
            Units = _units,
            Counts = _counts,
            EnemyCounts = _enemyCounts,
            AverageHealthFields = _averageHealthFields,
            ValueAdjustments = _valueAdjustments,
            NumberArrays = _numberArrays,
            Rules = GridRows(_rulesGrid),
            Adjustments = GridRows(_adjustmentsGrid),
            FormulaAdjustments = GridRows(_formulaAdjustmentsGrid)
        });
    }

    private const int ModuleFooterBarHeight = 64;
    private const int ModuleFooterButtonHeight = 40;

    private void InitializeComponent()
    {
        Dock = DockStyle.Fill;
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.Text;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var filterItems = new List<(int? ClassId, string Tooltip)> { (null, "全部") };
        filterItems.AddRange(GetAvailableClasses().Select(item => ((int?)item.Id, item.Name)));
        _classFilterStrip.SetItems(filterItems);
        _classFilterStrip.SelectClassId(null);
        _classFilterStrip.SelectionChanged += (_, _) =>
        {
            _filterClassId = _classFilterStrip.SelectedClassId;
            ApplyModuleClassFilter(preserveSelection: true);
        };

        var iconStack = UiTheme.CreateIconStripStack(_classFilterStrip);
        iconStack.Dock = DockStyle.None;
        iconStack.Margin = Padding.Empty;
        var iconViewport = new UiThemedPanel
        {
            AutoScroll = true,
            Margin = Padding.Empty
        };
        iconViewport.Controls.Add(iconStack);
        var iconCard = UiTheme.CreateIconStripCard(iconViewport);
        var identityCard = BuildNameRow();
        identityCard.Dock = DockStyle.None;
        var topArea = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Margin = Padding.Empty,
            // 子控件的位置由 SyncTopArea 按可用宽度计算。
        };
        topArea.Controls.Add(iconCard);
        topArea.Controls.Add(identityCard);
        identityCard.BringToFront();
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiTheme.ModuleSidebarWidth));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, ModuleFooterBarHeight));
        body.Controls.Add(BuildSidebar(), 0, 0);
        body.Controls.Add(BuildEditor(), 1, 0);
        var footer = BuildActionRow();
        body.Controls.Add(footer, 0, 1);
        body.SetColumnSpan(footer, 2);
        body.Resize += (_, _) =>
        {
            var desired = body.ClientSize.Width < UiTheme.Scale(this, 1000)
                ? 220
                : UiTheme.ModuleSidebarWidth;
            if (body.ColumnStyles[0].Width != desired)
            {
                body.ColumnStyles[0].Width = desired;
            }
        };

        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
        };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute,
            ClassIconStrip.StripHeight + UiTheme.IconStripCardPadding * 2 + UiTheme.PageGap));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(topArea, 0, 0);
        page.Controls.Add(body, 0, 1);

        Controls.Add(UiTheme.CreateFixedWidthPageHost(page, UiTheme.EditorPageWidth, throttleResize: true));

        var iconWidthLogical = ClassIconStrip.StripPadding * 2
            + filterItems.Count * ClassIconStrip.CellSize
            + Math.Max(0, filterItems.Count - 1) * ClassIconStrip.CellGap;

        void SyncTopArea()
        {
            if (topArea.IsDisposed || !topArea.IsHandleCreated)
            {
                return;
            }

            var stripHeight = _classFilterStrip.ScaledHeight;
            var identityHeight = Math.Max(stripHeight, UiTheme.Scale(this, 48));
            var gap = UiTheme.Scale(this, UiTheme.PageGap);
            var iconWidth = UiTheme.Scale(this, iconWidthLogical);
            var preferredIdentity = UiTheme.Scale(this, UiTheme.ModuleIdentityCardWidth);
            var minimumIdentity = UiTheme.Scale(this, UiTheme.ModuleIdentityCardMinWidth);
            var availableWidth = topArea.ClientSize.Width;
            if (availableWidth <= 0)
            {
                return;
            }

            var preferredIconCardWidth = iconWidth + iconCard.Padding.Horizontal;
            // 先收窄名称/作者卡，再让职业图标卡在自身内部滚动；两张卡始终同排。
            var identityWidth = Math.Clamp(
                availableWidth - preferredIconCardWidth - gap,
                Math.Min(minimumIdentity, Math.Max(0, availableWidth - gap)),
                preferredIdentity);
            var iconCardWidth = Math.Max(0,
                Math.Min(preferredIconCardWidth, availableWidth - identityWidth - gap));
            var needsScroll = iconWidth > Math.Max(0, iconCardWidth - iconCard.Padding.Horizontal);
            var cardHeight = Math.Max(identityHeight,
                stripHeight + iconCard.Padding.Vertical
                    + (needsScroll ? SystemInformation.HorizontalScrollBarHeight : 0));
            var topHeight = cardHeight + gap;
            if (Math.Abs(page.RowStyles[0].Height - topHeight) > 0.5f)
            {
                page.RowStyles[0].Height = topHeight;
            }

            var identityLeft = availableWidth - identityWidth;
            var iconCardBounds = new Rectangle(0, 0, iconCardWidth, cardHeight);
            var iconBounds = new Rectangle(0, 0, iconWidth, stripHeight);
            var identityBounds = new Rectangle(identityLeft, 0, identityWidth, cardHeight);

            if (iconCard.Bounds == iconCardBounds
                && iconStack.Bounds == iconBounds
                && identityCard.Bounds == identityBounds)
            {
                return;
            }

            topArea.SuspendLayout();
            try
            {
                if (iconCard.Bounds != iconCardBounds)
                {
                    iconCard.Bounds = iconCardBounds;
                }

                if (iconStack.Bounds != iconBounds)
                {
                    iconStack.Bounds = iconBounds;
                }

                if (identityCard.Bounds != identityBounds)
                {
                    identityCard.Bounds = identityBounds;
                }

                if (iconStack.RowStyles.Count >= 1
                    && Math.Abs(iconStack.RowStyles[0].Height - stripHeight) > 0.5f)
                {
                    iconStack.RowStyles[0] = new RowStyle(SizeType.Absolute, stripHeight);
                }

                identityCard.BringToFront();
            }
            finally
            {
                topArea.ResumeLayout(false);
            }
        }

        _topAreaResizeTimer = new System.Windows.Forms.Timer
        {
            Interval = UiTheme.LayoutResizeDebounceMs
        };
        _topAreaResizeTimer.Tick += (_, _) =>
        {
            _topAreaResizeTimer.Stop();
            SyncTopArea();
        };

        topArea.Resize += (_, _) =>
        {
            _topAreaResizeTimer.Stop();
            _topAreaResizeTimer.Start();
        };
        HandleCreated += (_, _) => BeginInvoke(SyncTopArea);
        _classFilterStrip.HandleCreated += (_, _) => SyncTopArea();
        Disposed += (_, _) =>
        {
            _topAreaResizeTimer?.Stop();
            _topAreaResizeTimer?.Dispose();
            _topAreaResizeTimer = null;
        };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.S))
        {
            if (_saveButton.Enabled)
            {
                _saveButton.PerformClick();
            }

            return true;
        }

        if (keyData == Keys.F5)
        {
            if (_reloadButton.Enabled)
            {
                _reloadButton.PerformClick();
            }

            return true;
        }

        if (keyData == (Keys.Control | Keys.N))
        {
            if (_addButton.Enabled)
            {
                _addButton.PerformClick();
            }

            return true;
        }

        if (_rulesGrid.ContainsFocus && TryHandleRulesGridShortcut(keyData))
        {
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private bool TryHandleRulesGridShortcut(Keys keyData)
    {
        var rowIndex = _rulesGrid.CurrentCell?.RowIndex ?? -1;
        if (keyData == (Keys.Control | Keys.D))
        {
            CopyRule(rowIndex);
            return true;
        }

        if (keyData == (Keys.Alt | Keys.Up))
        {
            MoveRule(rowIndex, -1);
            return true;
        }

        if (keyData == (Keys.Alt | Keys.Down))
        {
            MoveRule(rowIndex, 1);
            return true;
        }

        return false;
    }

    private Control BuildSidebar()
    {
        var sidebar = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding = new Padding(UiTheme.CardPadding),
            Margin = new Padding(0, 0, UiTheme.PageGap, UiTheme.PageGap),
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
                : (null, null),
            itemForeColorSelector: index => index >= 0
                                               && index < _modules.Count
                                               && _moduleStore.HasImportIssue(_modules[index].Id)
                ? UiTheme.Danger
                : null);
        _moduleList.BackColor = UiTheme.SurfaceRaised;
        _moduleList.SelectedIndexChanged += (_, _) => SelectModule(_moduleList.SelectedIndex);
        var hoveredModuleIndex = -1;
        _moduleList.MouseMove += (_, e) =>
        {
            var index = _moduleList.IndexFromPoint(e.Location);
            if (index == hoveredModuleIndex)
            {
                return;
            }

            hoveredModuleIndex = index;
            _pathToolTip.SetToolTip(
                _moduleList,
                index >= 0 && index < _moduleList.Items.Count
                    ? _moduleList.Items[index]?.ToString()
                    : null);
        };
        _moduleList.MouseLeave += (_, _) => hoveredModuleIndex = -1;
        sidebar.Controls.Add(_moduleList, 0, 0);
        return sidebar;
    }

    private Control BuildSidebarFooter(Color backgroundColor)
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = backgroundColor,
            Padding = Padding.Empty,
            Margin = new Padding(0, 0, 8, 0),
            ColumnCount = 3,
            RowCount = 1
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _reloadButton = UiTheme.CreateButton("刷新", UiTheme.ButtonKind.Secondary);
        StyleModuleFooterButton(_reloadButton);
        _reloadButton.Dock = DockStyle.Fill;
        _reloadButton.Click += async (_, _) => await RunModuleCommandAsync(_modulesReloadRequested);
        _pathToolTip.SetToolTip(_reloadButton, "重新加载模块列表 (F5)");

        var getModulesButton = UiTheme.CreateExternalLinkButton(
            "获取模块",
            Color.FromArgb(252, 238, 10),
            Color.Black);
        StyleModuleFooterButton(getModulesButton);
        getModulesButton.Dock = DockStyle.Fill;
        getModulesButton.FlatAppearance.BorderColor = Color.FromArgb(252, 238, 10);
        getModulesButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 244, 64);
        getModulesButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(220, 207, 8);
        getModulesButton.Click += (_, _) => OpenModuleWebsite();

        footer.Controls.Add(_reloadButton, 0, 0);
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
            Margin = new Padding(0, 0, 0, UiTheme.PageGap),
            ColumnCount = 1,
            RowCount = 2
        };
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        editor.Controls.Add(BuildMatchRow(), 0, 0);
        editor.Controls.Add(BuildEditorTabs(), 0, 1);
        return editor;
    }

    private Control BuildEditorTabs()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, UiTheme.PageGap, 0, 0),
            Padding = new Padding(0),
            BackColor = UiTheme.Surface,
            ColumnCount = 1,
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, UiTheme.TabBarHeight));
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
            BackColor = UiTheme.Surface,
            FillColor = UiTheme.Surface,
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
            BackColor = UiTheme.Surface,
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
            contentHost.Controls.Add(page);
        }

        _editorEmptyHint.Text = "请在左侧选择模块, 或点击「新建」创建";
        _editorEmptyHint.Dock = DockStyle.Fill;
        _editorEmptyHint.TextAlign = ContentAlignment.MiddleCenter;
        _editorEmptyHint.ForeColor = UiTheme.Muted;
        _editorEmptyHint.BackColor = UiTheme.Surface;
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
        var gap = UiTheme.PageGap;
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 3,
            RowCount = 3,
            Padding = new Padding(gap),
            Margin = new Padding(0)
        };

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, gap));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, gap));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 45));

        var conditionCard = CreateEditorSectionCard("条件动态数值", BuildAdjustmentsGrid());
        panel.Controls.Add(conditionCard, 0, 0);
        panel.SetColumnSpan(conditionCard, 3);
        panel.Controls.Add(CreateEditorSectionCard("公式动态数值", BuildFormulaAdjustmentsGrid()), 0, 2);
        panel.Controls.Add(CreateEditorSectionCard("数组", BuildNumberArraysPanel()), 2, 2);
        return panel;
    }

    private Control CreateEditorCardPage(string title, Control body)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 1,
            RowCount = 1,
            Padding = new Padding(UiTheme.PageGap),
            Margin = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(CreateEditorSectionCard(title, body), 0, 0);
        return panel;
    }

    private Control CreateEditorSectionCard(string title, Control body)
    {
        var card = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            FillColor = UiTheme.SurfaceRaised,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(UiTheme.CardPadding, 10, UiTheme.CardPadding, UiTheme.CardPadding),
            Margin = new Padding(0)
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(UiTheme.CreateSectionTitle(Font, title), 0, 0);
        body.Margin = new Padding(0, 4, 0, 0);
        card.Controls.Add(body, 0, 1);
        return card;
    }

    private Control BuildNumberArraysPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        UiTheme.ConfigureListViewColumns(
            _numberArraysList,
            Font,
            "module-number-arrays",
            new UiTheme.ListColumn("名称", 140, 420),
            new UiTheme.ListColumn("数组", 240, 2000, FillRemaining: true));
        _numberArraysList.MultiSelect = false;
        _numberArraysList.DoubleClick += (_, _) => EditSelectedNumberArray();
        panel.Controls.Add(_numberArraysList, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(8, 0, 0, 0)
        };
        buttons.Resize += (_, _) => LayoutUnitActionButtons(buttons);
        var add = CreateUnitActionButton("添加", UiTheme.Field, UiTheme.Text, bottomGap: true);
        add.Click += (_, _) => AddNumberArray();
        var edit = CreateUnitActionButton("编辑", UiTheme.Field, UiTheme.Text, bottomGap: true);
        edit.Click += (_, _) => EditSelectedNumberArray();
        var delete = CreateUnitActionButton("删除", UiTheme.Field, UiTheme.Danger, bottomGap: false);
        delete.Click += (_, _) => DeleteSelectedNumberArray();
        buttons.Controls.Add(add);
        buttons.Controls.Add(edit);
        buttons.Controls.Add(delete);
        panel.Controls.Add(buttons, 1, 0);
        return panel;
    }

    private void RefreshNumberArraysList(int selectIndex = -1)
    {
        _numberArraysList.BeginUpdate();
        _numberArraysList.Items.Clear();
        foreach (var array in _numberArrays)
        {
            var values = string.Join(", ", array.Numbers);
            _numberArraysList.Items.Add(new ListViewItem([array.Name, values])
            {
                ToolTipText = values
            });
        }
        _numberArraysList.EndUpdate();
        if (selectIndex >= 0 && selectIndex < _numberArraysList.Items.Count)
        {
            _numberArraysList.Items[selectIndex].Selected = true;
        }
    }

    private void AddNumberArray()
    {
        if (_selectedModule is null) return;
        using var editor = new ModuleNumberArrayEditorForm(null, _numberArrays.Select(array => array.Name));
        if (editor.ShowDialog(FindForm()) != DialogResult.OK || editor.Result is null) return;
        _numberArrays.Add(editor.Result);
        RefreshNumberArraysList(_numberArrays.Count - 1);
    }

    private void EditSelectedNumberArray()
    {
        if (_selectedModule is null || _numberArraysList.SelectedIndices.Count == 0) return;
        var index = _numberArraysList.SelectedIndices[0];
        using var editor = new ModuleNumberArrayEditorForm(
            _numberArrays[index],
            _numberArrays.Where((_, i) => i != index).Select(array => array.Name));
        if (editor.ShowDialog(FindForm()) != DialogResult.OK || editor.Result is null) return;
        _numberArrays[index] = editor.Result;
        RefreshNumberArraysList(index);
    }

    private void DeleteSelectedNumberArray()
    {
        if (_selectedModule is null || _numberArraysList.SelectedIndices.Count == 0) return;
        _numberArrays.RemoveAt(_numberArraysList.SelectedIndices[0]);
        RefreshNumberArraysList();
    }

    private Control BuildRulesPanel()
    {
        var page = CreateEditorCardPage("逻辑规则", BuildRulesGrid());
        _rulesGrid.ScrollBars = ScrollBars.Both;
        return page;
    }

    private Control BuildUnitsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
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

        return CreateEditorCardPage("动态单位", panel);
    }

    private Control BuildNameRow()
    {
        var row = new UiCardPanel
        {
            BackColor = UiTheme.Surface,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(UiTheme.CardPadding, 4, UiTheme.CardPadding, 4),
            Margin = Padding.Empty
        };
        // 卡片外壳贴合内容：标签 + 等宽输入，无弹性留白列；靠右由顶栏定位负责。
        row.ColumnStyles.Clear();
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.RowStyles.Clear();
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        row.Controls.Add(CreateLabel("名称"), 0, 0);
        UiTheme.StyleTextBox(_nameBox);
        _nameBox.Dock = DockStyle.Fill;
        _nameBox.Margin = new Padding(0, 6, 0, 0);
        row.Controls.Add(_nameBox, 1, 0);

        row.Controls.Add(CreateLabel("作者"), 0, 1);
        UiTheme.StyleTextBox(_authorBox);
        _authorBox.Dock = DockStyle.Fill;
        _authorBox.Margin = new Padding(0, 6, 0, 0);
        row.Controls.Add(_authorBox, 1, 1);

        return row;
    }

    private Control BuildMatchRow()
    {
        var matchLabels = new[] { "职业:", "专精:", "英雄天赋:", "队伍类型:" };
        var matchBoxes = new[] { _classBox, _specBox, _heroTalentBox, _partyTypeBox };
        // 职业贴左、队伍类型贴右；组间等宽弹性间隙，四下拉等宽。
        var columnCount = matchLabels.Length * 2 + (matchLabels.Length - 1);
        var row = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = columnCount,
            RowCount = 2,
            Padding = new Padding(UiTheme.CardPadding),
            Margin = new Padding(0)
        };

        row.ColumnStyles.Clear();
        for (var i = 0; i < matchLabels.Length; i++)
        {
            row.ColumnStyles.Add(new ColumnStyle(
                SizeType.Absolute,
                MeasureLabelColumnWidth(matchLabels[i], Font)));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiTheme.ModuleMatchFieldWidth));
            if (i < matchLabels.Length - 1)
            {
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            }
        }

        row.RowStyles.Clear();
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        // 与匹配行同高，避免 Percent 撑高导致输入框比标签高出一截。
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        ResetClassOptions(_classBox);
        ResetSpecOptions(_specBox, null);
        ResetHeroTalentOptions(_heroTalentBox, null, null);
        _classBox.SelectedIndexChanged += (_, _) =>
        {
            ResetSpecOptions(_specBox, ReadMatchCombo(_classBox));
            ResetHeroTalentOptions(_heroTalentBox, ReadMatchCombo(_classBox), ReadMatchCombo(_specBox));
            ReloadCurrentClassSpellIds();
            RefreshKeymapColumns();
            RefreshAdjustmentFieldColumn();
            RefreshRuleSpellIcons();
            InvalidateConditionFieldValidation();
            _rulesGrid.Invalidate();
        };
        _specBox.SelectedIndexChanged += (_, _) =>
        {
            ResetHeroTalentOptions(_heroTalentBox, ReadMatchCombo(_classBox), ReadMatchCombo(_specBox));
            ReloadCurrentClassSpellIds();
            RefreshRuleSpellIcons();
            RefreshAdjustmentFieldColumn();
            InvalidateConditionFieldValidation();
            _rulesGrid.Invalidate();
        };

        // 列：标签0 / 框1 / 隙2 / 标签3 / 框4 / …
        for (var i = 0; i < matchLabels.Length; i++)
        {
            var label = AddMatchField(row, matchLabels[i], matchBoxes[i], i * 3);
            if (i == 1) _specLabel = label;
            if (i == 2) _heroTalentLabel = label;
        }
        _matchRow = row;
        UpdateMatchRowProfile();

        var recommendedTalentRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 4, 0, 0),
            Padding = Padding.Empty
        };
        recommendedTalentRow.RowStyles.Clear();
        recommendedTalentRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        recommendedTalentRow.ColumnStyles.Clear();
        recommendedTalentRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        recommendedTalentRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var recommendedTalentLabel = CreateLabel("推荐天赋:");
        recommendedTalentLabel.AutoSize = false;
        recommendedTalentLabel.Dock = DockStyle.Fill;
        recommendedTalentLabel.TextAlign = ContentAlignment.MiddleLeft;
        recommendedTalentLabel.Margin = Padding.Empty;
        recommendedTalentRow.Controls.Add(recommendedTalentLabel, 0, 0);
        UiTheme.StyleTextBox(_recommendedTalentBox);
        // 与标签同处固定行高内 Dock.Fill；仅输入框下移 4px，标签位置不变。
        _recommendedTalentBox.Dock = DockStyle.Fill;
        _recommendedTalentBox.Margin = new Padding(0, 10, 0, 0);
        recommendedTalentRow.Controls.Add(_recommendedTalentBox, 1, 0);
        row.Controls.Add(recommendedTalentRow, 0, 1);
        row.SetColumnSpan(recommendedTalentRow, columnCount);

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
        _adjustmentFieldColumn.HeaderText = "字段";
        _adjustmentFieldColumn.ReadOnly = true;
        _adjustmentFieldColumn.Width = 260;
        _adjustmentFieldColumn.MinimumWidth = 200;
        _adjustmentFieldColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _adjustmentsGrid.Columns.Add(_adjustmentFieldColumn);

        _adjustmentsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Delta",
            HeaderText = "调整",
            CellTemplate = new AdjustmentValueCell(),
            Width = 110,
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

        // 新列加在集合末尾以保留 Rows.Add 的位置参数(启用/字段/调整/条件), 再用 DisplayIndex 排序。
        _adjustmentTypeColumn.Name = "Type";
        _adjustmentTypeColumn.HeaderText = "类型";
        _adjustmentTypeColumn.Width = 140;
        _adjustmentTypeColumn.MinimumWidth = 100;
        _adjustmentTypeColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _adjustmentTypeColumn.FlatStyle = FlatStyle.Flat;
        _adjustmentTypeColumn.DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton;
        _adjustmentTypeColumn.ReadOnly = true;
        foreach (var option in AdjustmentTypeOptions)
        {
            _adjustmentTypeColumn.Items.Add(option.Text);
        }
        _adjustmentsGrid.Columns.Add(_adjustmentTypeColumn);
        _adjustmentTypeColumn.DisplayIndex = 1;
        _adjustmentsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Operation",
            HeaderText = "调整方式",
            Width = 110,
            ReadOnly = true,
            DefaultCellStyle = new DataGridViewCellStyle { NullValue = "+" },
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None
        });
        _adjustmentsGrid.Columns["Operation"]!.DisplayIndex = 3;

        _adjustmentsGrid.CellClick += OnAdjustmentsGridCellClick;
        _adjustmentsGrid.CellFormatting += OnAdjustmentsGridCellFormatting;
        _adjustmentsGrid.CellPainting += OnAdjustmentsGridCellPainting;
        _adjustmentsGrid.CellValueChanged += OnAdjustmentsGridCellValueChanged;
        _adjustmentsGrid.CellEndEdit += (_, _) => RefreshAdjustmentFieldColumn();
        _adjustmentsGrid.DefaultValuesNeeded += (_, e) =>
        {
            e.Row.Cells["Enabled"].Value = true;
            e.Row.Cells["Operation"].Value = "+";
            e.Row.Cells["Delta"].Value = "0";
        };
        _adjustmentsGrid.DataError += (_, e) => e.ThrowException = false;
        _adjustmentsGrid.EditingControlShowing += OnAdjustmentsGridEditingControlShowing;
        _adjustmentsGrid.KeyDown += OnAdjustmentsGridKeyDown;
        _adjustmentsGrid.CellBeginEdit += (_, _) => CloseAdjustmentComboDropDown();
        _adjustmentsGrid.Disposed += (_, _) => CloseAdjustmentComboDropDown();
        _adjustmentsGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_adjustmentsGrid.IsCurrentCellDirty && _adjustmentsGrid.CurrentCell is DataGridViewCheckBoxCell)
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
        // 注释存在单元格 Value 里。必须用文本列（或按钮列且 UseColumnTextForButtonValue=false）：
        // DataGridViewButtonCell.GetValue 在 UseColumnTextForButtonValue=true 时会返回列 Text（空串），
        // 导致编辑器写入后读回仍是空的，保存模块时注释也会丢失。
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = RuleCommentColumnName,
            HeaderText = string.Empty,
            Width = 32,
            MinimumWidth = 32,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Resizable = DataGridViewTriState.False,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _rulesGrid.Columns[RuleCommentColumnName]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        AddRuleIconColumn("MoveUp", "▲", "上移 (Alt+↑)");
        AddRuleIconColumn("MoveDown", "▼", "下移 (Alt+↓)");
        AddRuleIconColumn("Copy", "⧉", "复制到下一行 (Ctrl+D)");
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
        _rulesGrid.RowPrePaint += OnRulesGridRowPrePaint;
        _rulesGrid.RowsAdded += OnRulesGridRowsAdded;
        _rulesGrid.Scroll += (_, _) => QueueRelaxedCommentSync();
        _rulesGrid.CellMouseEnter += OnRulesGridCellMouseEnter;
        _rulesGrid.CellMouseLeave += OnRulesGridCellMouseLeave;
        _rulesGrid.MouseLeave += (_, _) =>
        {
            _rulesGridToolTip.Hide(_rulesGrid);
            if (_relaxedRuleView)
            {
                _rulesGrid.Cursor = Cursors.Default;
            }
        };
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
                BackColor = UiTheme.Surface,
                Padding = new Padding(13, 6, 13, 6)
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
        _spellColumn.Items.Add(ModuleSpecialActions.FailedItem);
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
            : ModuleSpecialActions.IsFailedItem(spell)
                ? _keymapCatalog.GetUnitsForSpells(classId, _keymapCatalog.GetFailedItemNames(classId))
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
                GetRuleSpellIcon(CellText(_rulesGrid.Rows[e.RowIndex], "Spell"));
            UpdateUnitCellItems(_rulesGrid.Rows[e.RowIndex]);
            UpdateMacroConditionCellItems(_rulesGrid.Rows[e.RowIndex]);
        }
        else if (columnName == "Unit")
        {
            UpdateMacroConditionCellItems(_rulesGrid.Rows[e.RowIndex]);
        }
    }

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

        RefreshRuleSpellIcons();
        _rulesGrid.Invalidate();
    }

    private void ReloadCurrentClassSpellIds()
    {
        _currentClassSpellIdsByName.Clear();
        _currentSpecItemIdsByName.Clear();
        _currentClassConditionSpells.Clear();
        _currentClassConditionItems.Clear();
        var classId = ReadMatchCombo(_classBox);
        if (classId is null)
        {
            return;
        }

        var classPath = Path.Combine(_resolveProfile().AddonRoot, "class", $"{ClassNames.GetConfigFileName(classId.Value)}.lua");
        try
        {
            var document = ClassBlocksStore.Load(classPath);
            foreach (var spell in document.SpellsList
                         .Where(spell => spell.SpellId > 0 && !string.IsNullOrWhiteSpace(spell.Name))
                         .OrderBy(spell => spell.Index)
                         .ThenBy(spell => spell.SpellId))
            {
                var name = spell.Name.Trim();
                if (_currentClassConditionSpells.All(item => item.SpellId != spell.SpellId))
                {
                    _currentClassConditionSpells.Add(new ConditionSpell(spell.SpellId, spell.Index, name));
                }

                if (!_currentClassSpellIdsByName.TryGetValue(name, out var spellIds))
                {
                    spellIds = [];
                    _currentClassSpellIdsByName[name] = spellIds;
                }

                if (!spellIds.Contains(spell.SpellId))
                {
                    spellIds.Add(spell.SpellId);
                }
            }

            foreach (var item in document.ItemsList
                         .Where(item => item.ItemId > 0 && !string.IsNullOrWhiteSpace(item.Name))
                         .OrderBy(item => item.Index)
                         .ThenBy(item => item.ItemId))
            {
                var name = item.Name.Trim();
                SpellIconCatalog.RegisterItem(item.ItemId, name);
                if (_currentClassConditionItems.All(entry => entry.ItemId != item.ItemId))
                {
                    _currentClassConditionItems.Add(new ConditionItem(item.ItemId, item.Index, name));
                }

                if (!_currentSpecItemIdsByName.TryGetValue(name, out var classItemIds))
                {
                    classItemIds = [];
                    _currentSpecItemIdsByName[name] = classItemIds;
                }

                if (!classItemIds.Contains(item.ItemId))
                {
                    classItemIds.Add(item.ItemId);
                }
            }

            var specId = ReadMatchCombo(_specBox);
            if (specId is not null && document.Specs.TryGetValue(specId.Value, out var spec))
            {
                foreach (var item in spec.Items)
                {
                    if (item.ItemId is not { } itemId || itemId <= 0 || string.IsNullOrWhiteSpace(item.Name))
                    {
                        continue;
                    }

                    var name = item.Name.Trim();
                    SpellIconCatalog.RegisterItem(itemId, name);
                    if (!_currentSpecItemIdsByName.TryGetValue(name, out var itemIds))
                    {
                        itemIds = [];
                        _currentSpecItemIdsByName[name] = itemIds;
                    }

                    if (!itemIds.Contains(itemId))
                    {
                        itemIds.Add(itemId);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or InvalidDataException or ArgumentException)
        {
            // 当前职业文件缺失或暂时不可读时，继续使用全局名称匹配。
        }
    }

    private Image? GetRuleSpellIcon(string? spellName)
    {
        var normalized = spellName?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        // 模块规则中的技能名来自当前职业 keymap；若与物品同名，优先使用职业技能列表的 spellId。
        if (_currentClassSpellIdsByName.TryGetValue(normalized, out var spellIds))
        {
            foreach (var spellId in spellIds)
            {
                var icon = SpellIconCatalog.Get(spellId);
                if (icon is not null)
                {
                    return icon;
                }
            }
        }

        if (_currentSpecItemIdsByName.TryGetValue(normalized, out var itemIds))
        {
            foreach (var itemId in itemIds)
            {
                var icon = SpellIconCatalog.GetItem(itemId);
                if (icon is not null)
                {
                    return icon;
                }
            }
        }

        if (SpellIconCatalog.TryParseItemReference(normalized, out var explicitItemId))
        {
            var icon = SpellIconCatalog.GetItem(explicitItemId);
            if (icon is not null)
            {
                return icon;
            }
        }

        var officialItemIcon = SpellIconCatalog.GetItem(normalized);
        if (officialItemIcon is not null)
        {
            return officialItemIcon;
        }

        return SpellIconCatalog.Get(normalized);
    }

    private void RefreshRuleSpellIcons()
    {
        foreach (DataGridViewRow row in _rulesGrid.Rows)
        {
            if (!row.IsNewRow)
            {
                row.Cells["SpellIcon"].Value = GetRuleSpellIcon(CellText(row, "Spell"));
            }
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
        foreach (DataGridViewRow row in _adjustmentsGrid.Rows)
        {
            if (!row.IsNewRow)
            {
                // 字段集合可能因职业/专精/动态单位变化, 按该行"类型"重新校验现值。
                RebuildAdjustmentFieldCell(row, row.Cells["Field"].Value?.ToString(), keepCustom: true);
            }
        }

        // 动态数值也是可用于条件的字段；新增、改名或删除后立即刷新规则行的缺失字段提示。
        InvalidateConditionFieldValidation();
        RefreshAdjustmentValidation();
    }

    private void RefreshAdjustmentValidation()
    {
        var structuredFields = BuildStructuredFieldSet();
        foreach (DataGridViewRow row in _adjustmentsGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var messages = new List<string>();
            AddMissingStructuredField(CellText(row, "Field"), "调整目标", structuredFields, messages);
            AddMissingConditionReferences(CellText(row, "Condition"), structuredFields, messages);
            ApplyAdjustmentValidationStyle(row, messages);
        }

        foreach (DataGridViewRow row in _formulaAdjustmentsGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var messages = new List<string>();
            foreach (Match match in Regex.Matches(
                         FormulaEvaluator.NormalizeExpression(CellText(row, "Formula")),
                         @"\b(?:auras|aura|spells|spell)\.[_$\p{L}\p{N}.]+",
                         RegexOptions.IgnoreCase))
            {
                AddMissingStructuredField(match.Value, "公式字段", structuredFields, messages);
            }
            ApplyAdjustmentValidationStyle(row, messages);
        }
    }

    private HashSet<string> BuildStructuredFieldSet()
    {
        var classId = ReadMatchCombo(_classBox);
        var specId = ReadMatchCombo(_specBox);
        var fields = new HashSet<string>(
            _fieldCatalog.GetFields(classId, specId)
                .Where(field => field.Category is ConditionFieldCategory.Aura or ConditionFieldCategory.Spell)
                .Select(field => NormalizeConditionFieldName(field.Name)),
            StringComparer.Ordinal);
        fields.UnionWith(_fieldCatalog.GetAuraAliasFieldNames(classId, specId, groupOnly: false));
        var groupFields = _fieldCatalog.GetGroupFields(classId, specId)
            .Where(field => SpellFieldKey.TryParseAuraMember(field.Name, out _, out _))
            .Select(field => field.Name)
            .ToList();
        groupFields.AddRange(_fieldCatalog.GetAuraAliasFieldNames(classId, specId, groupOnly: true));
        fields.UnionWith(groupFields);
        foreach (var unit in _units.Where(unit => !string.IsNullOrWhiteSpace(unit.Name)))
        {
            fields.UnionWith(groupFields.Select(field => $"{unit.Name}.{field}"));
        }
        return fields;
    }

    private void AddMissingConditionReferences(
        string expression,
        IReadOnlySet<string> structuredFields,
        ICollection<string> messages)
    {
        var availableSpellIds = _currentClassConditionSpells.Select(spell => spell.SpellId).ToHashSet();
        foreach (var term in ConditionExpression.Parse(expression))
        {
            if (term.NamedArray && !_numberArrays.Any(array => string.Equals(array.Name, term.Value, StringComparison.Ordinal)))
            {
                AddUnique(messages, $"数组“{term.Value}”不存在");
                continue;
            }
            if (SpellIdConditionFields.Contains(term.Field))
            {
                if (ConditionExpression.IsInOperator(term.Op)) continue;
                if (!long.TryParse(term.Value.Trim(), out var spellId)
                    || spellId <= 0
                    || !availableSpellIds.Contains(spellId))
                {
                    AddUnique(messages, $"{term.Field}不存在 spellId 为 {term.Value.Trim()} 的法术");
                }
                continue;
            }

            if (ItemIdConditionFields.Contains(term.Field))
            {
                if (!long.TryParse(term.Value.Trim(), out var itemId)
                    || itemId <= 0
                    || !_currentClassConditionItems.Any(item => item.ItemId == itemId))
                {
                    AddUnique(messages, $"{term.Field}不存在 itemId 为 {term.Value.Trim()} 的物品");
                }
                continue;
            }

            AddMissingStructuredField(term.Field, "条件字段", structuredFields, messages);
        }
    }

    private static void AddMissingStructuredField(
        string field,
        string source,
        IReadOnlySet<string> available,
        ICollection<string> messages)
    {
        var normalized = NormalizeConditionFieldName(field);
        var isStructured = normalized.StartsWith("auras.", StringComparison.Ordinal)
            || normalized.StartsWith("spells.", StringComparison.Ordinal)
            || normalized.Contains(".auras.", StringComparison.Ordinal);
        if (!isStructured || available.Contains(normalized))
        {
            return;
        }

        var spellId = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(part => long.TryParse(part, out var id) && id > 0);
        AddUnique(messages, spellId is null
            ? $"{source}不存在：{field}"
            : $"{source}不存在 spellId 为 {spellId} 的字段：{field}");
    }

    private static void AddUnique(ICollection<string> values, string value)
    {
        if (!values.Contains(value))
        {
            values.Add(value);
        }
    }

    private static void ApplyAdjustmentValidationStyle(DataGridViewRow row, IReadOnlyCollection<string> messages)
    {
        var text = string.Join('\n', messages);
        row.ErrorText = text;
        row.DefaultCellStyle.BackColor = messages.Count == 0 ? Color.Empty : UiTheme.DangerSoft;
        row.DefaultCellStyle.ForeColor = messages.Count == 0 ? Color.Empty : UiTheme.Danger;
        row.DefaultCellStyle.SelectionBackColor = messages.Count == 0 ? Color.Empty : UiTheme.Danger;
        row.DefaultCellStyle.SelectionForeColor = messages.Count == 0 ? Color.Empty : UiTheme.Background;
        foreach (DataGridViewCell cell in row.Cells)
        {
            cell.ToolTipText = text;
        }
    }

    // 按该行选中的"类型"重建字段选项；自建行使用可编辑文本单元格。
    // desiredValue 为 null 时取单元格现值; 命中过滤后选项则保留, 否则: keepCustom 时补录为自定义项(载入旧数据), 反之清空(用户切换类型)。
    private void RebuildAdjustmentFieldCell(DataGridViewRow row, string? desiredValue, bool keepCustom)
    {
        if (row.IsNewRow)
        {
            return;
        }

        var cell = row.Cells["Field"];
        cell.ReadOnly = !IsCustomAdjustmentRow(row);
        if (IsCustomAdjustmentRow(row))
        {
            row.Cells["Operation"].Value = "=";
        }
        desiredValue ??= cell.Value?.ToString();
        var category = ReadAdjustmentType(row);
        var isAvailable = !string.IsNullOrEmpty(desiredValue)
            && BuildAdjustmentFields().Any(field =>
                (category is null || field.Category == category)
                && string.Equals(field.Name, desiredValue, StringComparison.Ordinal));
        if (string.IsNullOrEmpty(desiredValue) || isAvailable || keepCustom)
        {
            cell.Value = desiredValue ?? string.Empty;
        }
        else
        {
            cell.Value = string.Empty;
        }
    }

    private static bool IsCustomAdjustmentRow(DataGridViewRow? row)
        => row is not null && CellText(row, "Type") == "自建";

    private static string ReadAdjustmentOperation(DataGridViewRow row)
        => IsCustomAdjustmentRow(row) ? "="
            : string.IsNullOrWhiteSpace(CellText(row, "Operation")) ? "+" : CellText(row, "Operation");

    private static bool CanSelectAdjustmentBoolean(DataGridViewRow? row)
        => row is not null && ReadAdjustmentOperation(row) == "=";

    private IEnumerable<ConditionField> GetCustomAdjustmentFields()
    {
        foreach (DataGridViewRow row in _adjustmentsGrid.Rows)
        {
            if (!row.IsNewRow && IsCustomAdjustmentRow(row)
                && !string.IsNullOrWhiteSpace(CellText(row, "Field")))
            {
                var name = CellText(row, "Field");
                yield return new ConditionField(name, name,
                    CellText(row, "Delta") == ModuleValueAdjustment.ConditionBooleanValue
                        || bool.TryParse(CellText(row, "Delta"), out _) ? ConditionFieldType.Bool : ConditionFieldType.Int,
                    ConditionFieldCategory.Shigure);
            }
        }
    }

    // 载入旧数据时: 由字段名推断类别, 写入"类型"单元格并重建字段选项(保留原值)。
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

        // 用户切换"类型": 重建字段选项, 仅保留仍属于该类型的现值, 否则清空让其重选。
        if (_adjustmentsGrid.Columns[e.ColumnIndex].Name == "Type")
        {
            RebuildAdjustmentFieldCell(_adjustmentsGrid.Rows[e.RowIndex], null, keepCustom: false);
        }

        if (_adjustmentsGrid.Columns[e.ColumnIndex].Name == "Operation")
        {
            var row = _adjustmentsGrid.Rows[e.RowIndex];
            var value = CellText(row, "Delta");
            if (!CanSelectAdjustmentBoolean(row)
                && (value == ModuleValueAdjustment.ConditionBooleanValue || bool.TryParse(value, out _)))
            {
                row.Cells["Delta"].Value = "0";
            }
            _adjustmentsGrid.InvalidateCell(row.Cells["Delta"]);
        }

        if (_adjustmentsGrid.Columns[e.ColumnIndex].Name is "Field" or "Delta" or "Type")
        {
            InvalidateConditionFieldValidation();
        }

        RefreshAdjustmentValidation();
    }

    private void OnAdjustmentsGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0
            || e.ColumnIndex < 0
            || _adjustmentsGrid.Rows[e.RowIndex].IsNewRow)
        {
            return;
        }

        var columnName = _adjustmentsGrid.Columns[e.ColumnIndex].Name;
        if (columnName == "Field")
        {
            // 调整目标只显示字段名称；单元格底层继续保存结构化 spellId 键。
            e.Value = FormatAdjustmentFieldForDisplay(e.Value?.ToString());
            e.FormattingApplied = true;
        }
        else if (columnName == "Condition")
        {
            // 动态数值条件沿用规则表的名称化显示；单元格底层仍保留 spellId 表达式供保存和运行。
            e.Value = FormatConditionExpressionForDisplay(e.Value?.ToString());
            e.FormattingApplied = true;
        }
    }

    private string FormatAdjustmentFieldForDisplay(string? field)
    {
        var source = field?.Trim() ?? string.Empty;
        var normalized = NormalizeConditionFieldName(source);
        var isSpellReference = SpellFieldKey.TryParseSpell(normalized, out var spellId, out _);
        var isAuraReference = !isSpellReference
            && (SpellFieldKey.TryParseAura(normalized, out _, out spellId, out _)
                || SpellFieldKey.TryParseAuraMember(normalized, out spellId, out _));
        if (!isSpellReference && !isAuraReference)
        {
            return source;
        }

        return ResolveConditionFieldDisplayName(normalized, spellId)
               ?? ResolveConditionSpellName(spellId, normalized)
               ?? source;
    }

    private IReadOnlyList<ConditionField> BuildAdjustmentFields()
    {
        var fields = new List<ConditionField>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in _fieldCatalog.GetFields(ReadMatchCombo(_classBox), ReadMatchCombo(_specBox)))
        {
            // 状态支持整数运算与布尔赋值；技能/光环保留其原有类型。
            if (field.Category == ConditionFieldCategory.State && field.Type is ConditionFieldType.Int or ConditionFieldType.Bool)
            {
                AddAdjustmentField(fields, seen, field.Name, field.DisplayName, ConditionFieldCategory.State, field.Type);
            }
            else if (field.Category == ConditionFieldCategory.Spell
                     && string.Equals(
                         field.Classification,
                         CooldownConditionClassifications.Item,
                         StringComparison.Ordinal)
                     && field.Type == ConditionFieldType.Int)
            {
                AddAdjustmentField(fields, seen, field.Name, field.DisplayName, ConditionFieldCategory.State);
            }
            else if (field.Category is ConditionFieldCategory.Spell or ConditionFieldCategory.Aura)
            {
                AddAdjustmentField(fields, seen, field.Name, field.DisplayName, field.Category, field.Type);
            }
        }

        // 已定义的动态单位生命值和数量拥有明确类别，必须先于动态数值目标加入；
        // 否则同名目标会被 seen 抢先登记成错误类别，载入时无法正确回填“类型”。
        foreach (var unit in _units)
        {
            if (!string.IsNullOrWhiteSpace(unit.ValueName))
            {
                var fieldLabel = UnitSummary.DescribeTargetField(unit.TargetField);
                AddAdjustmentField(
                    fields,
                    seen,
                    unit.ValueName,
                    $"{unit.ValueName} ({fieldLabel})",
                    ConditionFieldCategory.DynamicUnit);
            }
        }

        foreach (var count in _counts)
        {
            if (!string.IsNullOrWhiteSpace(count.Name))
            {
                AddAdjustmentField(fields, seen, count.Name, $"人数: {count.Name}", ConditionFieldCategory.DynamicValue);
            }
        }

        foreach (var count in _enemyCounts)
        {
            if (!string.IsNullOrWhiteSpace(count.Name))
            {
                AddAdjustmentField(fields, seen, count.Name, $"敌人数: {count.Name}", ConditionFieldCategory.DynamicValue);
            }
        }

        foreach (var field in _averageHealthFields)
        {
            if (!string.IsNullOrWhiteSpace(field.Name))
            {
                AddAdjustmentField(fields, seen, field.Name, $"平均血量: {field.Name}", ConditionFieldCategory.DynamicValue);
            }
        }

        foreach (var custom in GetCustomAdjustmentFields())
        {
            AddAdjustmentField(fields, seen, custom.Name, custom.DisplayName, custom.Category, custom.Type);
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
        ConditionFieldCategory category,
        ConditionFieldType type = ConditionFieldType.Int)
    {
        if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
        {
            return;
        }

        fields.Add(new ConditionField(name, displayName, type, category));
    }

    private void AddUnit()
    {
        using var editor = new UnitEditorForm(
            GetAuraFields(),
            GetNameplateAuraFields(),
            GetThresholdFields(),
            GetFormulaValueNames(),
            CollectTakenNames(),
            null,
            null,
            null,
            hasNameplateImprovedGarrote: _fieldCatalog.HasNameplateImprovedGarrote(
                ReadMatchCombo(_classBox), ReadMatchCombo(_specBox)),
            hasNameplateThreat: _fieldCatalog.HasNameplateThreat(
                ReadMatchCombo(_classBox), ReadMatchCombo(_specBox)),
            hasNameplateCastSpell: _fieldCatalog.HasNameplateCastSpell(
                ReadMatchCombo(_classBox), ReadMatchCombo(_specBox)),
            numberArrayNames: _numberArrays.Select(array => array.Name).ToArray());
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
        else if (editor.ResultEnemyCount is { } enemyCount)
        {
            _enemyCounts.Add(enemyCount);
        }
        else if (editor.ResultAverageHealth is { } averageHealth)
        {
            _averageHealthFields.Add(averageHealth);
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
        var existingEnemyCount = kind == UnitRowKind.EnemyCount ? _enemyCounts[index] : null;
        var existingAverageHealth = kind == UnitRowKind.AverageHealth ? _averageHealthFields[index] : null;
        var ownName = existingUnit?.Name ?? existingCount?.Name ?? existingEnemyCount?.Name ?? existingAverageHealth?.Name;
        var ownValueName = existingUnit?.ValueName;

        using var editor = new UnitEditorForm(
            GetAuraFields(),
            GetNameplateAuraFields(),
            GetThresholdFields(),
            GetFormulaValueNames(),
            CollectTakenNames(ownName, ownValueName),
            existingUnit,
            existingCount,
            existingEnemyCount,
            existingAverageHealth,
            _fieldCatalog.HasNameplateImprovedGarrote(ReadMatchCombo(_classBox), ReadMatchCombo(_specBox)),
            _fieldCatalog.HasNameplateThreat(ReadMatchCombo(_classBox), ReadMatchCombo(_specBox)),
            _fieldCatalog.HasNameplateCastSpell(ReadMatchCombo(_classBox), ReadMatchCombo(_specBox)),
            _numberArrays.Select(array => array.Name).ToArray());
        if (editor.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        // 类别可能在编辑中改变(单位↔数量↔敌人数量), 先移除原项再按结果加入。
        RemoveUnitRow(kind, index);

        if (editor.ResultUnit is { } unit)
        {
            _units.Add(unit);
        }
        else if (editor.ResultCount is { } count)
        {
            _counts.Add(count);
        }
        else if (editor.ResultEnemyCount is { } enemyCount)
        {
            _enemyCounts.Add(enemyCount);
        }
        else if (editor.ResultAverageHealth is { } averageHealth)
        {
            _averageHealthFields.Add(averageHealth);
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

        RemoveUnitRow(kind, index);

        RefreshUnitsList();
        RefreshUnitDependentUi();
        RefreshAdjustmentFieldColumn();
    }

    private void RemoveUnitRow(UnitRowKind kind, int index)
    {
        switch (kind)
        {
            case UnitRowKind.Unit:
                _units.RemoveAt(index);
                break;
            case UnitRowKind.Count:
                _counts.RemoveAt(index);
                break;
            case UnitRowKind.EnemyCount:
                _enemyCounts.RemoveAt(index);
                break;
            case UnitRowKind.AverageHealth:
                _averageHealthFields.RemoveAt(index);
                break;
        }
    }

    // ListView 行顺序: 队友单位、队友数量、敌人数量、平均血量。把选中行映射回对应列表索引。
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
        if (countIndex < _counts.Count)
        {
            return (UnitRowKind.Count, countIndex);
        }

        var enemyIndex = countIndex - _counts.Count;
        if (enemyIndex < _enemyCounts.Count)
        {
            return (UnitRowKind.EnemyCount, enemyIndex);
        }

        var averageIndex = enemyIndex - _enemyCounts.Count;
        return averageIndex < _averageHealthFields.Count
            ? (UnitRowKind.AverageHealth, averageIndex)
            : (UnitRowKind.None, -1);
    }

    private void RefreshUnitsList()
    {
        var availableAuraIds = GetAuraFields()
            .SelectMany(field => field.Name.Split('.', StringSplitOptions.RemoveEmptyEntries))
            .Where(part => long.TryParse(part, out _))
            .Select(long.Parse)
            .ToHashSet();
        _unitsList.BeginUpdate();
        _unitsList.Items.Clear();
        foreach (var unit in _units)
        {
            var name = string.IsNullOrWhiteSpace(unit.ValueName) ? unit.Name : $"{unit.Name} / {unit.ValueName}";
            var summary = UnitSummary.Describe(unit, ResolveGroupAuraName);
            var item = new ListViewItem([name, "队友单位", summary]) { ToolTipText = $"{name}\n{summary}" };
            var referencedAuraIds = (unit.FilterGroups ?? [])
                .SelectMany(group => group.Conditions ?? [])
                .Where(condition => condition.Field == CountConditionFieldKind.Aura)
                .Select(condition => condition.AuraSpellId.GetValueOrDefault())
                .Concat(unit.TargetAuraSpellId is { } targetAuraId ? [targetAuraId] : [])
                .Where(id => id > 0)
                .Distinct();
            var missing = referencedAuraIds.Where(id => !availableAuraIds.Contains(id)).ToArray();
            if (missing.Length > 0)
            {
                item.BackColor = UiTheme.DangerSoft;
                item.ForeColor = UiTheme.Danger;
                item.ToolTipText += $"\n队伍不存在 spellId 为 {string.Join("、", missing)} 的光环";
            }
            _unitsList.Items.Add(item);
        }

        foreach (var count in _counts)
        {
            var summary = UnitSummary.Describe(count, ResolveGroupAuraName);
            var item = new ListViewItem([count.Name, "队友数量", summary]) { ToolTipText = $"{count.Name}\n{summary}" };
            var referencedAuraIds = count.FilterGroups
                .SelectMany(group => group.Conditions)
                .Where(condition => condition.Field == CountConditionFieldKind.Aura)
                .Select(condition => condition.AuraSpellId.GetValueOrDefault())
                .Where(id => id > 0)
                .Distinct();
            var missing = referencedAuraIds.Where(id => !availableAuraIds.Contains(id)).ToArray();
            if (missing.Length > 0)
            {
                item.BackColor = UiTheme.DangerSoft;
                item.ForeColor = UiTheme.Danger;
                item.ToolTipText += $"\n队伍不存在 spellId 为 {string.Join("、", missing)} 的光环";
            }
            _unitsList.Items.Add(item);
        }

        var availableNameplateAuraIds = GetNameplateAuraFields()
            .SelectMany(field => field.Name.Split('.', StringSplitOptions.RemoveEmptyEntries))
            .Where(part => long.TryParse(part, out _))
            .Select(long.Parse)
            .ToHashSet();
        foreach (var count in _enemyCounts)
        {
            var summary = UnitSummary.Describe(count, ResolveNameplateAuraName);
            var item = new ListViewItem([count.Name, "敌人数量", summary]) { ToolTipText = $"{count.Name}\n{summary}" };
            var referencedAuraIds = count.FilterGroups
                .SelectMany(group => group.Conditions)
                .Where(condition => condition.Field == CountConditionFieldKind.Aura)
                .Select(condition => condition.AuraSpellId.GetValueOrDefault())
                .Where(id => id > 0)
                .Distinct();
            var missing = referencedAuraIds.Where(id => !availableNameplateAuraIds.Contains(id)).ToArray();
            if (missing.Length > 0)
            {
                item.BackColor = UiTheme.DangerSoft;
                item.ForeColor = UiTheme.Danger;
                item.ToolTipText += $"\n姓名板不存在 spellId 为 {string.Join("、", missing)} 的光环";
            }

            _unitsList.Items.Add(item);
        }

        foreach (var field in _averageHealthFields)
        {
            var enemyTarget = field.Target == AverageHealthTargetKind.Enemies;
            var summary = UnitSummary.Describe(
                field,
                enemyTarget ? ResolveNameplateAuraName : ResolveGroupAuraName);
            var item = new ListViewItem([field.Name, "平均血量", summary])
            {
                ToolTipText = $"{field.Name}\n{summary}"
            };
            var availableIds = enemyTarget ? availableNameplateAuraIds : availableAuraIds;
            var referencedAuraIds = (field.FilterGroups ?? [])
                .SelectMany(group => group.Conditions ?? [])
                .Where(condition => condition.Field == CountConditionFieldKind.Aura)
                .Select(condition => condition.AuraSpellId.GetValueOrDefault())
                .Where(id => id > 0)
                .Distinct();
            var missing = referencedAuraIds.Where(id => !availableIds.Contains(id)).ToArray();
            if (missing.Length > 0)
            {
                item.BackColor = UiTheme.DangerSoft;
                item.ForeColor = UiTheme.Danger;
                item.ToolTipText += $"\n{(enemyTarget ? "姓名板" : "队伍")}不存在 spellId 为 {string.Join("、", missing)} 的光环";
            }

            _unitsList.Items.Add(item);
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

    private IReadOnlyList<ConditionField> GetAuraFields()
    {
        return _fieldCatalog
            .GetGroupFields(ReadMatchCombo(_classBox), ReadMatchCombo(_specBox))
            .Where(field => !NonAuraGroupFields.Contains(field.Name))
            .ToList();
    }

    private IReadOnlyList<ConditionField> GetNameplateAuraFields()
        => _fieldCatalog.GetNameplateAuraFields(ReadMatchCombo(_classBox), ReadMatchCombo(_specBox));

    private string? ResolveNameplateAuraName(long spellId)
    {
        foreach (var field in GetNameplateAuraFields())
        {
            if (field.Name.Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Any(part => long.TryParse(part, out var id) && id == spellId))
            {
                return field.DisplayName.Split(" / ", 2, StringSplitOptions.TrimEntries)[0];
            }
        }

        return null;
    }

    private string? ResolveGroupAuraName(long spellId)
    {
        foreach (var field in GetAuraFields())
        {
            if (field.Name.Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Any(part => long.TryParse(part, out var id) && id == spellId))
            {
                return field.DisplayName.Split(" / ", 2, StringSplitOptions.TrimEntries)[0];
            }
        }

        return null;
    }

    private IReadOnlyList<string> GetThresholdFields()
    {
        // 阈值字段取状态/动态单位/动态数值/自建中的整数，排除技能/光环字段。
        return BuildAdjustmentFields()
            .Where(field => field.Type == ConditionFieldType.Int && field.Category is ConditionFieldCategory.State
                or ConditionFieldCategory.DynamicUnit
                or ConditionFieldCategory.DynamicValue
                or ConditionFieldCategory.Shigure)
            .Select(field => field.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
    }

    private IReadOnlyList<string> GetFormulaValueNames()
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (DataGridViewRow row in _formulaAdjustmentsGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            if (TryGetAdjustmentField(CellText(row, "Field"), CellText(row, "Formula"), out var field)
                && seen.Add(field))
            {
                names.Add(field);
            }
        }

        return names;
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
            if (!string.IsNullOrWhiteSpace(unit.ValueName))
            {
                taken.Add(unit.ValueName);
            }
        }

        foreach (var count in _counts)
        {
            taken.Add(count.Name);
        }

        foreach (var count in _enemyCounts)
        {
            taken.Add(count.Name);
        }

        foreach (var field in _averageHealthFields)
        {
            taken.Add(field.Name);
        }

        foreach (var field in _fieldCatalog.GetFields(classId, specId))
        {
            taken.Add(field.Name);
        }

        foreach (var field in _fieldCatalog.GetGroupFields(classId, specId))
        {
            taken.Add(field.Name);
        }

        taken.UnionWith(GetAdjustmentTargetFields());

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
        Count,
        EnemyCount,
        AverageHealth
    }

    private sealed record RuleRowValues(
        bool Enabled,
        string Spell,
        string UnitText,
        string MacroCondition,
        string Condition,
        string Comment,
        IReadOnlyList<string> SubConditions,
        int? DelayMs,
        int? LogicDelayMs,
        bool? ContinueLogic);

    private sealed class RuleRowMetadata(
        IEnumerable<string>? subConditions = null,
        int? delayMs = null,
        int? logicDelayMs = null,
        bool? continueLogic = null)
    {
        public List<string> SubConditions { get; } = subConditions?.ToList() ?? new List<string>();
        public int? DelayMs { get; set; } = delayMs;
        public int? LogicDelayMs { get; set; } = logicDelayMs;
        public bool? ContinueLogic { get; set; } = continueLogic;
    }

    private void OnRulesGridCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (_suppressNextRulesCellClick)
        {
            _suppressNextRulesCellClick = false;
            return;
        }

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
            return;
        }

        if (columnName == RuleCommentColumnName && !_rulesGrid.Rows[e.RowIndex].IsNewRow)
        {
            OpenRuleTextEditor(e.RowIndex);
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
        if (_relaxedRuleView)
        {
            _rulesGrid.HorizontalScrollingOffset = 0;
        }

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

        var cellBounds = GetRuleDropDownAnchor(rowIndex, _rulesGrid.Columns[columnIndex].Name);
        var options = values
            .Select(value => new UiDropDownOption(value, value,
                cell.OwningColumn?.Name == "Spell" ? GetRuleSpellIcon(value) : null))
            .ToList();
        ToolStripDropDown? dropDown = null;
        dropDown = UiDropDownPopup.Show(
            _rulesGrid,
            cellBounds,
            options,
            currentValue,
            selected =>
            {
                cell.Value = selected.Value?.ToString() ?? string.Empty;
                _rulesGrid.InvalidateCell(cell);
            },
            minimumWidth: 150,
            closed: () =>
            {
                if (ReferenceEquals(_rulesComboDropDown, dropDown))
                {
                    _rulesComboDropDown = null;
                }
            });
        _rulesComboDropDown = dropDown;
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

        if (!row.IsNewRow
            && (GetMissingConditionFields(row).Count > 0
                || GetMissingConditionSpells(row).Count > 0
                || GetMissingConditionItems(row).Count > 0))
        {
            // 缺失字段或 spellId 会让条件不命中；用整行红色状态提醒用户修复配置。
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
                metadata.LogicDelayMs,
                metadata.ContinueLogic);
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

    private IReadOnlyList<string> GetMissingConditionSpells(DataGridViewRow row)
    {
        var availableSpellIds = _currentClassConditionSpells
            .Select(spell => spell.SpellId)
            .ToHashSet();
        var missing = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var metadata = GetRuleMetadata(row);
        foreach (var expression in new[] { CellText(row, "Condition") }.Concat(metadata.SubConditions))
        {
            foreach (var term in ConditionExpression.Parse(expression))
            {
                if (!SpellIdConditionFields.Contains(term.Field))
                {
                    continue;
                }

                if (ConditionExpression.IsInOperator(term.Op)) continue;

                var value = term.Value.Trim();
                if (long.TryParse(value, out var spellId)
                    && spellId > 0
                    && availableSpellIds.Contains(spellId))
                {
                    continue;
                }

                var message = $"{term.Field}不存在 spellId 为 {value} 的法术";
                if (seen.Add(message))
                {
                    missing.Add(message);
                }
            }
        }

        return missing;
    }

    private IReadOnlyList<string> GetMissingConditionItems(DataGridViewRow row)
    {
        var availableItemIds = _currentClassConditionItems
            .Select(item => item.ItemId)
            .ToHashSet();
        var missing = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var metadata = GetRuleMetadata(row);
        foreach (var expression in new[] { CellText(row, "Condition") }.Concat(metadata.SubConditions))
        {
            foreach (var term in ConditionExpression.Parse(expression))
            {
                if (!ItemIdConditionFields.Contains(term.Field))
                {
                    continue;
                }

                var value = term.Value.Trim();
                if (long.TryParse(value, out var itemId)
                    && itemId > 0
                    && availableItemIds.Contains(itemId))
                {
                    continue;
                }

                var message = $"{term.Field}不存在 itemId 为 {value} 的物品";
                if (seen.Add(message))
                {
                    missing.Add(message);
                }
            }
        }

        return missing;
    }

    // 字段目录的构造会读取职业配置和动态数值表；缓存后避免每个可见单元格重复做同一份工作。
    private void EnsureConditionFieldValidationCatalog()
    {
        if (_availableConditionFields is not null
            && _availableGroupConditionFields is not null
            && _conditionFieldDisplayNames is not null)
        {
            return;
        }

        var conditionFields = BuildConditionFields(includeRuleSettings: true);
        _availableConditionFields = new HashSet<string>(
            conditionFields
                .Select(field => NormalizeConditionFieldName(field.Name)),
            StringComparer.Ordinal);
        _conditionFieldDisplayNames = conditionFields
            .GroupBy(field => NormalizeConditionFieldName(field.Name), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().DisplayName, StringComparer.Ordinal);

        var classId = ReadMatchCombo(_classBox);
        var specId = ReadMatchCombo(_specBox);
        _availableGroupConditionFields = new HashSet<string>(
            _fieldCatalog.GetGroupFields(classId, specId).Select(field => field.Name),
            StringComparer.Ordinal);
        _availableConditionFields.UnionWith(
            _fieldCatalog.GetAuraAliasFieldNames(classId, specId, groupOnly: false));
        _availableGroupConditionFields.UnionWith(
            _fieldCatalog.GetAuraAliasFieldNames(classId, specId, groupOnly: true));

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
        _conditionFieldDisplayNames = null;
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
    private string DecorateCondition(
        string main,
        IReadOnlyList<string>? subs,
        int? delayMs,
        int? logicDelayMs,
        bool? continueLogic)
    {
        var conditionText = FormatConditionExpressionForDisplay(main);
        if (subs is { Count: > 0 })
        {
            var any = string.Join(" | ", subs.Select(FormatConditionExpressionForDisplay));
            conditionText = conditionText.Length == 0
                ? $"任一({any})"
                : $"{conditionText}  且任一({any})";
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

        if (continueLogic is true)
        {
            conditionText = conditionText.Length == 0
                ? "继续逻辑"
                : $"{conditionText}；继续逻辑";
        }

        return conditionText;
    }

    private string FormatConditionExpressionForDisplay(string? expression)
    {
        var source = expression?.Trim() ?? string.Empty;
        if (source.Length == 0)
        {
            return string.Empty;
        }

        var terms = ConditionExpression.Parse(source);
        if (terms.Count == 0)
        {
            return source;
        }

        return ConditionExpression.Build(terms.Select(term =>
        {
            var field = FormatConditionFieldForDisplay(term.Field);
            var value = SpellIdConditionFields.Contains(term.Field)
                && !ConditionExpression.IsInOperator(term.Op)
                ? FormatConditionSpellValueForDisplay(term.Value)
                : ItemIdConditionFields.Contains(term.Field)
                    ? FormatConditionItemValueForDisplay(term.Value)
                : string.Equals(NormalizeConditionFieldName(term.Field), "首领战", StringComparison.Ordinal)
                    ? FormatBossValueForDisplay(term.Value)
                : term.Value;
            return term with { Field = field, Value = value };
        }));
    }

    private string FormatConditionFieldForDisplay(string field)
    {
        var normalized = NormalizeConditionFieldName(field);
        var isSpellReference = SpellFieldKey.TryParseSpell(normalized, out var spellId, out _);
        var isAuraReference = !isSpellReference
            && (SpellFieldKey.TryParseAura(normalized, out _, out spellId, out _)
                || SpellFieldKey.TryParseAuraMember(normalized, out spellId, out _));
        if (!isSpellReference && !isAuraReference)
        {
            return field;
        }

        var fieldDisplayName = ResolveConditionFieldDisplayName(normalized, spellId);
        if (!string.IsNullOrWhiteSpace(fieldDisplayName))
        {
            return $"{(isSpellReference ? "cd:" : "aura:")}{fieldDisplayName}";
        }

        var name = ResolveConditionSpellName(spellId, normalized);
        if (string.IsNullOrWhiteSpace(name))
        {
            return field;
        }

        return $"{(isSpellReference ? "cd:" : "aura:")}{name}";
    }

    private string FormatConditionSpellValueForDisplay(string value)
    {
        var normalized = value.Trim();
        if (!long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var spellId)
            || spellId <= 0)
        {
            return value;
        }

        var name = ResolveConditionSpellName(spellId, null);
        return string.IsNullOrWhiteSpace(name) ? value : name;
    }

    private string FormatConditionItemValueForDisplay(string value)
    {
        var normalized = value.Trim();
        if (!long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId)
            || itemId <= 0)
        {
            return value;
        }

        var name = _currentClassConditionItems.FirstOrDefault(item => item.ItemId == itemId)?.Name;
        return string.IsNullOrWhiteSpace(name) ? value : name;
    }

    private static string FormatBossValueForDisplay(string value)
    {
        var normalized = value.Trim();
        if (!int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bossNumber))
        {
            return value;
        }

        if (bossNumber == 0)
        {
            return "非首领战";
        }

        return StatusForm.GetBossNumberOptions()
                   .FirstOrDefault(option => option.Number == bossNumber)?.Name
               ?? value;
    }

    private string? ResolveConditionSpellName(long spellId, string? normalizedField)
    {
        var localName = _currentClassConditionSpells
            .FirstOrDefault(spell => spell.SpellId == spellId)?.Name;
        if (!string.IsNullOrWhiteSpace(localName))
        {
            return localName;
        }

        var catalogName = SpellIconCatalog.ResolveSuggestionName(spellId, null);
        if (!string.IsNullOrWhiteSpace(catalogName))
        {
            return catalogName;
        }

        if (!string.IsNullOrWhiteSpace(normalizedField))
        {
            return ResolveConditionFieldDisplayName(normalizedField, spellId);
        }

        return null;
    }

    private string? ResolveConditionFieldDisplayName(string normalizedField, long spellId)
    {
        EnsureConditionFieldValidationCatalog();
        if (!_conditionFieldDisplayNames!.TryGetValue(normalizedField, out var displayName))
        {
            return null;
        }

        var value = displayName;
        if (value.StartsWith("技能: ", StringComparison.Ordinal))
        {
            value = value["技能: ".Length..];
        }

        var idSuffix = $" / {spellId.ToString(CultureInfo.InvariantCulture)}";
        if (value.EndsWith(idSuffix, StringComparison.Ordinal))
        {
            value = value[..^idSuffix.Length];
        }

        return value.Trim();
    }

    private enum RelaxedRulePart
    {
        None,
        Enabled,
        Spell,
        Unit,
        Macro,
        Condition,
        MoveUp,
        MoveDown,
        Copy,
        InsertBlank,
        Delete
    }

    private readonly record struct RelaxedRuleRegions(
        Rectangle Enabled,
        Rectangle Icon,
        Rectangle Spell,
        Rectangle Unit,
        Rectangle Macro,
        Rectangle Comment,
        Rectangle Condition,
        Rectangle MoveUp,
        Rectangle MoveDown,
        Rectangle Copy,
        Rectangle InsertBlank,
        Rectangle Delete);

    private void ToggleRuleView()
    {
        HideRelaxedCommentEditors();
        CloseRulesComboDropDown();
        _rulesGrid.EndEdit();
        _relaxedRuleView = !_relaxedRuleView;
        ApplyRuleViewMode();
    }

    private void ApplyRuleViewMode()
    {
        var relaxed = _relaxedRuleView;
        _ruleViewButton.Text = relaxed ? "紧缩视图" : "宽松视图";
        _pathToolTip.SetToolTip(
            _ruleViewButton,
            relaxed ? "当前为宽松视图，点击切换为紧缩视图" : "当前为紧缩视图，点击切换为宽松视图");

        var height = relaxed ? RelaxedRuleRowHeight() : CompactRuleRowHeight();
        _rulesGrid.SuspendLayout();
        try
        {
            _rulesGrid.ColumnHeadersVisible = !relaxed;
            if (_rulesGrid.Columns["Enabled"] is DataGridViewColumn enabledColumn)
            {
                enabledColumn.ReadOnly = relaxed;
            }

            _rulesGrid.HorizontalScrollingOffset = 0;
            _rulesGrid.ScrollBars = relaxed ? ScrollBars.Vertical : ScrollBars.Both;
            _rulesGrid.RowTemplate.Height = height;
            foreach (DataGridViewRow row in _rulesGrid.Rows)
            {
                if (!row.IsNewRow)
                {
                    row.Height = height;
                }
            }
        }
        finally
        {
            _rulesGrid.ResumeLayout();
        }

        _rulesGrid.Invalidate();
        SyncRelaxedCommentBoxes();
    }

    private int CompactRuleRowHeight()
    {
        var verticalPadding = UiTheme.Scale(_rulesGrid, 12);
        return Math.Max(UiTheme.Scale(_rulesGrid, UiTheme.GridRowHeight), _rulesGrid.Font.Height + verticalPadding);
    }

    private int RelaxedRuleRowHeight()
    {
        var metrics = RelaxedRuleMetrics();
        return metrics.CardGap
            + metrics.Pad
            + metrics.Control
            + metrics.LineGap
            + metrics.Control
            + metrics.LineGap
            + metrics.Control
            + metrics.Pad;
    }

    private (int CardGap, int Side, int Pad, int LineGap, int Control, int DropDown) RelaxedRuleMetrics()
    {
        var control = Math.Max(1, (int)Math.Round(UiTheme.Scale(_rulesGrid, 36) * 0.8));
        return (
            UiTheme.Scale(_rulesGrid, 8),
            UiTheme.Scale(_rulesGrid, 8),
            UiTheme.Scale(_rulesGrid, 12),
            UiTheme.Scale(_rulesGrid, 8),
            control,
            UiTheme.Scale(_rulesGrid, 168));
    }

    private void OnRulesGridRowsAdded(object? sender, DataGridViewRowsAddedEventArgs e)
    {
        if (!_relaxedRuleView)
        {
            return;
        }

        var height = RelaxedRuleRowHeight();
        for (var i = 0; i < e.RowCount; i++)
        {
            var index = e.RowIndex + i;
            if (index < _rulesGrid.Rows.Count && !_rulesGrid.Rows[index].IsNewRow)
            {
                _rulesGrid.Rows[index].Height = height;
            }
        }
    }

    private void OnRulesGridRowPrePaint(object? sender, DataGridViewRowPrePaintEventArgs e)
    {
        if (!_relaxedRuleView || e.RowIndex < 0 || e.Graphics is null)
        {
            return;
        }

        var display = _rulesGrid.DisplayRectangle;
        var bounds = new Rectangle(display.Left, e.RowBounds.Top, display.Width, e.RowBounds.Height);
        PaintRelaxedRuleRow(e.Graphics, e.RowIndex, e.RowBounds, bounds);
        e.Handled = true;
    }

    private void PaintRelaxedRuleRow(Graphics graphics, int rowIndex, Rectangle fullBounds, Rectangle bounds)
    {
        var row = _rulesGrid.Rows[rowIndex];
        var missing = !row.IsNewRow && RuleRowHasMissingReferences(row);
        var selected = row.Selected;
        using (var brush = new SolidBrush(_rulesGrid.BackgroundColor))
        {
            graphics.FillRectangle(brush, fullBounds);
        }

        var card = GetRelaxedCardBounds(bounds);
        var previousMode = graphics.SmoothingMode;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var path = UiTheme.CreateRoundedRectanglePath(card, UiTheme.Scale(_rulesGrid, UiTheme.CardCornerRadius)))
        using (var fill = new SolidBrush(missing ? UiTheme.DangerSoft : UiTheme.SurfaceRaised))
        {
            graphics.FillPath(fill, path);
            if (selected)
            {
                using var border = new Pen(UiTheme.Accent);
                graphics.DrawPath(border, path);
            }
        }

        graphics.SmoothingMode = previousMode;

        var regions = BuildRelaxedRuleRegions(card);
        var enabled = !row.IsNewRow && CellBool(row, "Enabled", defaultValue: true);
        PaintRelaxedCheckBox(graphics, regions.Enabled, enabled);

        var icon = row.IsNewRow
            ? SpellIconCatalog.GetLastRuleRowIcon()
            : GetRuleSpellIcon(CellText(row, "Spell"));
        if (icon is not null && regions.Icon.Width > 4 && regions.Icon.Height > 4)
        {
            var size = Math.Min(regions.Icon.Width, regions.Icon.Height) - UiTheme.Scale(_rulesGrid, 2);
            var dest = new Rectangle(
                regions.Icon.X + (regions.Icon.Width - size) / 2,
                regions.Icon.Y + (regions.Icon.Height - size) / 2,
                size,
                size);
            graphics.DrawImage(icon, dest);
        }

        var fore = missing ? UiTheme.Danger : UiTheme.Text;
        PaintRelaxedValueField(graphics, regions.Spell, CellText(row, "Spell"), "技能", fore, withArrow: true);
        PaintRelaxedValueField(graphics, regions.Unit, CellText(row, "Unit"), "目标", fore, withArrow: true);
        PaintRelaxedValueField(graphics, regions.Macro, CellText(row, "MacroCondition"), "宏条件", fore, withArrow: true);
        PaintRelaxedChrome(graphics, regions.Comment, UiTheme.Field, UiTheme.Border);

        var metadata = GetRuleMetadata(row);
        var condition = row.IsNewRow
            ? string.Empty
            : DecorateCondition(
                CellText(row, "Condition"),
                metadata.SubConditions,
                metadata.DelayMs,
                metadata.LogicDelayMs,
                metadata.ContinueLogic);
        PaintRelaxedValueField(
            graphics,
            regions.Condition,
            condition,
            "点击编辑条件",
            missing ? fore : UiTheme.Text,
            withArrow: false);

        PaintRelaxedActionButton(graphics, regions.MoveUp, "上移一行", IsRuleIconEnabled("MoveUp", rowIndex), danger: false);
        PaintRelaxedActionButton(graphics, regions.MoveDown, "下移一行", IsRuleIconEnabled("MoveDown", rowIndex), danger: false);
        PaintRelaxedActionButton(graphics, regions.Copy, "复制到下一行", IsRuleIconEnabled("Copy", rowIndex), danger: false);
        PaintRelaxedActionButton(graphics, regions.InsertBlank, "在下一行添加空白行", IsRuleIconEnabled("InsertBlank", rowIndex), danger: false);
        PaintRelaxedActionButton(graphics, regions.Delete, "删除", IsRuleIconEnabled("Delete", rowIndex), danger: true);
    }

    private bool RuleRowHasMissingReferences(DataGridViewRow row)
        => GetMissingConditionFields(row).Count > 0
           || GetMissingConditionSpells(row).Count > 0
           || GetMissingConditionItems(row).Count > 0;

    private int RelaxedControlRadius()
        => UiTheme.Scale(_rulesGrid, UiTheme.ControlCornerRadius);

    private void PaintRelaxedChrome(Graphics graphics, Rectangle bounds, Color fill, Color border)
    {
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        var rect = new Rectangle(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        var previous = graphics.SmoothingMode;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var path = UiTheme.CreateRoundedRectanglePath(rect, RelaxedControlRadius());
        using var brush = new SolidBrush(fill);
        using var pen = new Pen(border);
        graphics.FillPath(brush, path);
        graphics.DrawPath(pen, path);
        graphics.SmoothingMode = previous;
    }

    private void PaintRelaxedCheckBox(Graphics graphics, Rectangle bounds, bool isChecked)
    {
        var boxSize = Math.Min(UiTheme.Scale(_rulesGrid, 16), Math.Max(1, bounds.Height - 2));
        var box = new Rectangle(
            bounds.X,
            bounds.Y + Math.Max(0, (bounds.Height - boxSize) / 2),
            boxSize,
            boxSize);
        var rect = new Rectangle(box.X, box.Y, Math.Max(1, box.Width - 1), Math.Max(1, box.Height - 1));
        var previous = graphics.SmoothingMode;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var path = UiTheme.CreateRoundedRectanglePath(rect, UiTheme.Scale(_rulesGrid, 4)))
        using (var fill = new SolidBrush(isChecked ? UiTheme.AccentSoft : UiTheme.Field))
        using (var border = new Pen(isChecked ? UiTheme.Accent : UiTheme.Border))
        {
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);
        }

        if (isChecked)
        {
            var inset = UiTheme.Scale(_rulesGrid, 3);
            using var pen = new Pen(UiTheme.Accent, 2);
            graphics.DrawLines(
                pen,
                new[]
                {
                    new Point(box.Left + inset, box.Top + box.Height / 2),
                    new Point(box.Left + box.Width / 2 - 1, box.Bottom - inset),
                    new Point(box.Right - inset, box.Top + inset)
                });
        }

        graphics.SmoothingMode = previous;
    }

    private void PaintRelaxedValueField(
        Graphics graphics,
        Rectangle bounds,
        string value,
        string placeholder,
        Color valueColor,
        bool withArrow)
    {
        PaintRelaxedChrome(graphics, bounds, UiTheme.Field, UiTheme.Border);
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        var arrowWidth = withArrow ? UiTheme.Scale(_rulesGrid, 18) : UiTheme.Scale(_rulesGrid, 8);
        var text = string.IsNullOrWhiteSpace(value) ? placeholder : value;
        var color = string.IsNullOrWhiteSpace(value) ? UiTheme.Muted : valueColor;
        var textBounds = new Rectangle(
            bounds.X + UiTheme.Scale(_rulesGrid, 8),
            bounds.Y,
            Math.Max(0, bounds.Width - arrowWidth - UiTheme.Scale(_rulesGrid, 8)),
            bounds.Height);
        TextRenderer.DrawText(
            graphics,
            text,
            _rulesGrid.Font,
            textBounds,
            color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        if (!withArrow)
        {
            return;
        }

        var previous = graphics.SmoothingMode;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var arrow = UiTheme.Scale(_rulesGrid, 8);
        var centerX = bounds.Right - UiTheme.Scale(_rulesGrid, 12);
        var centerY = bounds.Top + bounds.Height / 2;
        using var arrowBrush = new SolidBrush(UiTheme.Muted);
        graphics.FillPolygon(
            arrowBrush,
            new[]
            {
                new Point(centerX - arrow / 2, centerY - arrow / 4),
                new Point(centerX + arrow / 2, centerY - arrow / 4),
                new Point(centerX, centerY + arrow / 3)
            });
        graphics.SmoothingMode = previous;
    }

    private void PaintRelaxedActionButton(Graphics graphics, Rectangle bounds, string text, bool enabled, bool danger)
    {
        PaintRelaxedChrome(graphics, bounds, UiTheme.Field, UiTheme.Border);
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        var color = !enabled ? UiTheme.Muted : danger ? UiTheme.Danger : UiTheme.Text;
        TextRenderer.DrawText(
            graphics,
            text,
            _rulesGrid.Font,
            bounds,
            color,
            TextFormatFlags.HorizontalCenter
            | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix);
    }

    private Rectangle GetRelaxedCommentEditorBounds(Rectangle comment)
    {
        if (comment.Width <= 1 || comment.Height <= 1)
        {
            return Rectangle.Empty;
        }

        var padX = UiTheme.Scale(_rulesGrid, 8);
        var textHeight = TextRenderer.MeasureText(
            "注释",
            _rulesGrid.Font,
            Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height;
        textHeight = Math.Min(Math.Max(1, comment.Height - 4), textHeight + UiTheme.Scale(_rulesGrid, 2));
        return new Rectangle(
            comment.X + padX,
            comment.Y + Math.Max(0, (comment.Height - textHeight) / 2),
            Math.Max(1, comment.Width - padX * 2),
            textHeight);
    }

    private Rectangle GetRelaxedCardBounds(Rectangle rowBounds)
    {
        var metrics = RelaxedRuleMetrics();
        return new Rectangle(
            rowBounds.Left + metrics.Side,
            rowBounds.Top + metrics.CardGap / 2,
            Math.Max(0, rowBounds.Width - metrics.Side * 2),
            Math.Max(0, rowBounds.Height - metrics.CardGap));
    }

    private RelaxedRuleRegions BuildRelaxedRuleRegions(Rectangle bounds)
    {
        var metrics = RelaxedRuleMetrics();
        var x = bounds.Left + metrics.Pad;
        var y = bounds.Top + metrics.Pad;
        var contentWidth = Math.Max(0, bounds.Width - metrics.Pad * 2);
        var check = UiTheme.Scale(_rulesGrid, 16);
        var enabled = new Rectangle(x, y, check + metrics.LineGap, metrics.Control);
        x += enabled.Width;

        var icon = new Rectangle(x, y, metrics.Control, metrics.Control);
        x += icon.Width + metrics.LineGap;

        var commentMin = UiTheme.Scale(_rulesGrid, 96);
        var dropDown = metrics.DropDown;
        var available = Math.Max(0, bounds.Right - metrics.Pad - x);
        var fieldGaps = metrics.LineGap * 3;
        if (dropDown * 3 + fieldGaps + commentMin > available)
        {
            dropDown = Math.Max(UiTheme.Scale(_rulesGrid, 80), (available - fieldGaps - commentMin) / 3);
        }

        var spell = new Rectangle(x, y, dropDown, metrics.Control);
        x += dropDown + metrics.LineGap;
        var unit = new Rectangle(x, y, dropDown, metrics.Control);
        x += dropDown + metrics.LineGap;
        var macro = new Rectangle(x, y, dropDown, metrics.Control);
        x += dropDown + metrics.LineGap;
        var comment = new Rectangle(x, y, Math.Max(0, bounds.Right - metrics.Pad - x), metrics.Control);

        var condition = new Rectangle(
            bounds.Left + metrics.Pad,
            enabled.Bottom + metrics.LineGap,
            contentWidth,
            metrics.Control);
        var buttonY = condition.Bottom + metrics.LineGap;
        var labels = new[] { "上移一行", "下移一行", "复制到下一行", "在下一行添加空白行", "删除" };
        var textPad = UiTheme.Scale(_rulesGrid, 16);
        var widths = labels
            .Select(label => TextRenderer.MeasureText(label, _rulesGrid.Font).Width + textPad)
            .ToArray();
        var total = widths.Sum() + metrics.LineGap * (widths.Length - 1);
        if (total > contentWidth && total > 0)
        {
            var scale = contentWidth / (float)total;
            var minimum = UiTheme.Scale(_rulesGrid, 28);
            for (var i = 0; i < widths.Length; i++)
            {
                widths[i] = Math.Max(minimum, (int)(widths[i] * scale));
            }

            total = widths.Sum() + metrics.LineGap * (widths.Length - 1);
        }

        var buttons = new Rectangle[widths.Length];
        var buttonX = bounds.Right - metrics.Pad - total;
        for (var i = 0; i < widths.Length; i++)
        {
            buttons[i] = new Rectangle(buttonX, buttonY, widths[i], metrics.Control);
            buttonX += widths[i] + metrics.LineGap;
        }

        return new RelaxedRuleRegions(
            enabled,
            icon,
            spell,
            unit,
            macro,
            comment,
            condition,
            buttons[0],
            buttons[1],
            buttons[2],
            buttons[3],
            buttons[4]);
    }

    private bool TryGetRelaxedRowBounds(int rowIndex, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (rowIndex < 0 || rowIndex >= _rulesGrid.Rows.Count)
        {
            return false;
        }

        var rowRect = _rulesGrid.GetRowDisplayRectangle(rowIndex, cutOverflow: false);
        if (rowRect.Height <= 0)
        {
            return false;
        }

        var display = _rulesGrid.DisplayRectangle;
        bounds = new Rectangle(display.Left, rowRect.Top, display.Width, rowRect.Height);
        return true;
    }

    private RelaxedRulePart HitRelaxedRulePart(int rowIndex, Point location)
    {
        if (!TryGetRelaxedRowBounds(rowIndex, out var bounds))
        {
            return RelaxedRulePart.None;
        }

        var regions = BuildRelaxedRuleRegions(GetRelaxedCardBounds(bounds));
        if (regions.Enabled.Contains(location))
        {
            return RelaxedRulePart.Enabled;
        }

        if (regions.Icon.Contains(location) || regions.Spell.Contains(location))
        {
            return RelaxedRulePart.Spell;
        }

        if (regions.Unit.Contains(location))
        {
            return RelaxedRulePart.Unit;
        }

        if (regions.Macro.Contains(location))
        {
            return RelaxedRulePart.Macro;
        }

        if (regions.Condition.Contains(location))
        {
            return RelaxedRulePart.Condition;
        }

        if (regions.MoveUp.Contains(location))
        {
            return RelaxedRulePart.MoveUp;
        }

        if (regions.MoveDown.Contains(location))
        {
            return RelaxedRulePart.MoveDown;
        }

        if (regions.Copy.Contains(location))
        {
            return RelaxedRulePart.Copy;
        }

        if (regions.InsertBlank.Contains(location))
        {
            return RelaxedRulePart.InsertBlank;
        }

        if (regions.Delete.Contains(location))
        {
            return RelaxedRulePart.Delete;
        }

        return RelaxedRulePart.None;
    }

    private void DispatchRelaxedRuleClick(int rowIndex, Point location)
    {
        var part = HitRelaxedRulePart(rowIndex, location);
        SelectRelaxedRuleRow(rowIndex);
        switch (part)
        {
            case RelaxedRulePart.Enabled:
                ToggleRelaxedRuleEnabled(rowIndex);
                break;
            case RelaxedRulePart.Spell:
                ShowRulesComboDropDown(rowIndex, _rulesGrid.Columns["Spell"]!.Index);
                break;
            case RelaxedRulePart.Unit:
                ShowRulesComboDropDown(rowIndex, _rulesGrid.Columns["Unit"]!.Index);
                break;
            case RelaxedRulePart.Macro:
                ShowRulesComboDropDown(rowIndex, _rulesGrid.Columns["MacroCondition"]!.Index);
                break;
            case RelaxedRulePart.Condition:
                OpenConditionEditor(rowIndex);
                break;
            case RelaxedRulePart.MoveUp when IsRuleIconEnabled("MoveUp", rowIndex):
                MoveRule(rowIndex, -1);
                break;
            case RelaxedRulePart.MoveDown when IsRuleIconEnabled("MoveDown", rowIndex):
                MoveRule(rowIndex, 1);
                break;
            case RelaxedRulePart.Copy when IsRuleIconEnabled("Copy", rowIndex):
                CopyRule(rowIndex);
                break;
            case RelaxedRulePart.InsertBlank when IsRuleIconEnabled("InsertBlank", rowIndex):
                InsertBlankRule(rowIndex);
                break;
            case RelaxedRulePart.Delete when IsRuleIconEnabled("Delete", rowIndex):
                DeleteRule(rowIndex);
                break;
        }
    }

    private void UpdateRelaxedRuleCursor(Point location)
    {
        var hit = _rulesGrid.HitTest(location.X, location.Y);
        if (hit.RowIndex < 0)
        {
            _rulesGrid.Cursor = Cursors.Default;
            return;
        }

        var part = HitRelaxedRulePart(hit.RowIndex, location);
        var interactive = part is RelaxedRulePart.Enabled
            or RelaxedRulePart.Spell
            or RelaxedRulePart.Unit
            or RelaxedRulePart.Macro
            or RelaxedRulePart.Condition
            || part switch
            {
                RelaxedRulePart.MoveUp => IsRuleIconEnabled("MoveUp", hit.RowIndex),
                RelaxedRulePart.MoveDown => IsRuleIconEnabled("MoveDown", hit.RowIndex),
                RelaxedRulePart.Copy => IsRuleIconEnabled("Copy", hit.RowIndex),
                RelaxedRulePart.InsertBlank => IsRuleIconEnabled("InsertBlank", hit.RowIndex),
                RelaxedRulePart.Delete => IsRuleIconEnabled("Delete", hit.RowIndex),
                _ => false
            };
        _rulesGrid.Cursor = interactive ? Cursors.Hand : Cursors.Default;
    }

    private void SelectRelaxedRuleRow(int rowIndex)
    {
        if (!IsExistingRuleRow(rowIndex))
        {
            return;
        }

        _rulesGrid.ClearSelection();
        _rulesGrid.Rows[rowIndex].Selected = true;
        var cell = _rulesGrid.Rows[rowIndex].Cells["Spell"];
        if (!ReferenceEquals(_rulesGrid.CurrentCell, cell))
        {
            _rulesGrid.CurrentCell = cell;
        }

        _rulesGrid.HorizontalScrollingOffset = 0;
    }

    private void ToggleRelaxedRuleEnabled(int rowIndex)
    {
        var row = _rulesGrid.Rows[rowIndex];
        if (row.IsNewRow)
        {
            var index = _rulesGrid.Rows.Add(true, null!, string.Empty, string.Empty, string.Empty, string.Empty);
            SelectRelaxedRuleRow(index);
            return;
        }

        row.Cells["Enabled"].Value = !CellBool(row, "Enabled", defaultValue: true);
        _rulesGrid.InvalidateRow(rowIndex);
    }

    private Rectangle GetRuleDropDownAnchor(int rowIndex, string columnName)
    {
        if (_relaxedRuleView && TryGetRelaxedRowBounds(rowIndex, out var rowBounds))
        {
            var regions = BuildRelaxedRuleRegions(GetRelaxedCardBounds(rowBounds));
            return columnName switch
            {
                "Unit" => regions.Unit,
                "MacroCondition" => regions.Macro,
                _ => regions.Spell
            };
        }

        var columnIndex = _rulesGrid.Columns[columnName]?.Index ?? 0;
        return _rulesGrid.GetCellDisplayRectangle(columnIndex, rowIndex, cutOverflow: true);
    }

    private TextBox CreateRelaxedCommentBox()
    {
        var editor = new TextBox
        {
            Visible = false,
            PlaceholderText = "注释",
            TabStop = true,
            BorderStyle = BorderStyle.None,
            BackColor = UiTheme.Field,
            ForeColor = UiTheme.Text
        };
        UiTheme.StyleTextBox(editor);
        editor.BorderStyle = BorderStyle.None;
        editor.TextChanged += OnRelaxedCommentBoxTextChanged;
        editor.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.SuppressKeyPress = true;
            e.Handled = true;
            _rulesGrid.Focus();
        };
        _rulesGrid.Controls.Add(editor);
        return editor;
    }

    private void OnRelaxedCommentBoxTextChanged(object? sender, EventArgs e)
    {
        if (_relaxedCommentSyncing || sender is not TextBox box || box.Tag is not int rowIndex)
        {
            return;
        }

        if (rowIndex < 0 || rowIndex >= _rulesGrid.Rows.Count)
        {
            return;
        }

        var row = _rulesGrid.Rows[rowIndex];
        if (row.IsNewRow)
        {
            if (box.Text.Length == 0)
            {
                return;
            }

            var text = box.Text;
            var caret = box.SelectionStart;
            _relaxedCommentSyncing = true;
            int index;
            try
            {
                index = _rulesGrid.Rows.Add(
                    true,
                    null!,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    text);
                box.Tag = index;
            }
            finally
            {
                _relaxedCommentSyncing = false;
            }

            SyncRelaxedCommentBoxes();
            if (_relaxedCommentBoxes.FirstOrDefault(item => item.Tag is int itemRow && itemRow == index) is TextBox current)
            {
                current.Focus();
                current.SelectionStart = Math.Min(caret, current.Text.Length);
            }

            return;
        }

        row.Cells[RuleCommentColumnName].Value = box.Text;
    }

    private void QueueRelaxedCommentSync()
    {
        if (_relaxedCommentSyncQueued || !IsHandleCreated || IsDisposed)
        {
            return;
        }

        _relaxedCommentSyncQueued = true;
        BeginInvoke(() =>
        {
            _relaxedCommentSyncQueued = false;
            if (!IsDisposed)
            {
                SyncRelaxedCommentBoxes();
            }
        });
    }

    private void SyncRelaxedCommentBoxes()
    {
        if (_relaxedCommentSyncing || IsDisposed)
        {
            return;
        }

        if (!_relaxedRuleView)
        {
            HideRelaxedCommentEditors();
            return;
        }

        var display = _rulesGrid.DisplayRectangle;
        var visible = new List<int>();
        for (var i = 0; i < _rulesGrid.Rows.Count; i++)
        {
            if (!TryGetRelaxedRowBounds(i, out var rowBounds))
            {
                continue;
            }

            if (rowBounds.Bottom <= display.Top || rowBounds.Top >= display.Bottom)
            {
                continue;
            }

            visible.Add(i);
        }

        while (_relaxedCommentBoxes.Count < visible.Count)
        {
            _relaxedCommentBoxes.Add(CreateRelaxedCommentBox());
        }

        TextBox? focused = null;
        var focusedRow = -1;
        foreach (var box in _relaxedCommentBoxes)
        {
            if (box.Focused && box.Tag is int row)
            {
                focused = box;
                focusedRow = row;
                break;
            }
        }

        var pool = new List<TextBox>(_relaxedCommentBoxes);
        var assignment = new List<(TextBox Box, int Row)>();
        if (focused is not null && visible.Contains(focusedRow))
        {
            assignment.Add((focused, focusedRow));
            pool.Remove(focused);
            visible.Remove(focusedRow);
        }

        foreach (var rowIndex in visible)
        {
            var box = pool[0];
            pool.RemoveAt(0);
            assignment.Add((box, rowIndex));
        }

        _relaxedCommentSyncing = true;
        try
        {
            var shown = new HashSet<TextBox>();
            foreach (var (box, rowIndex) in assignment)
            {
                if (!TryGetRelaxedRowBounds(rowIndex, out var rowBounds))
                {
                    continue;
                }

                var comment = GetRelaxedCommentEditorBounds(
                    BuildRelaxedRuleRegions(GetRelaxedCardBounds(rowBounds)).Comment);
                if (comment.Width <= 1 || comment.Height <= 1)
                {
                    continue;
                }

                if (!ReferenceEquals(box.Font, _rulesGrid.Font))
                {
                    box.Font = _rulesGrid.Font;
                }
                if (box.Bounds != comment)
                {
                    box.Bounds = comment;
                }

                if (!box.Focused)
                {
                    var text = _rulesGrid.Rows[rowIndex].IsNewRow
                        ? string.Empty
                        : CellText(_rulesGrid.Rows[rowIndex], RuleCommentColumnName);
                    if (!string.Equals(box.Text, text, StringComparison.Ordinal))
                    {
                        box.Text = text;
                    }
                }

                box.Tag = rowIndex;
                if (!box.Visible)
                {
                    box.Visible = true;
                    box.BringToFront();
                }

                shown.Add(box);
            }

            foreach (var box in _relaxedCommentBoxes)
            {
                if (shown.Contains(box))
                {
                    continue;
                }

                box.Visible = false;
                box.Tag = -1;
            }
        }
        finally
        {
            _relaxedCommentSyncing = false;
        }
    }

    private void HideRelaxedCommentEditors()
    {
        _relaxedCommentSyncing = true;
        try
        {
            foreach (var box in _relaxedCommentBoxes)
            {
                box.Visible = false;
                box.Tag = -1;
            }
        }
        finally
        {
            _relaxedCommentSyncing = false;
        }
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
            UiTheme.PaintDataGridViewComboBoxCell(_rulesGrid, e);
            return;
        }

        if (columnName == "Drag")
        {
            PaintRuleDragHandle(e);
            return;
        }

        if (columnName == RuleCommentColumnName)
        {
            PaintRuleCommentCell(e);
            return;
        }

        if (columnName is not ("MoveUp" or "MoveDown" or "Copy" or "InsertBlank" or "Delete"))
        {
            return;
        }

        var enabled = IsRuleIconEnabled(columnName, e.RowIndex);
        var color = columnName == "Delete" ? UiTheme.Danger : UiTheme.Muted;
        if (!enabled)
        {
            color = Color.FromArgb(70, color);
        }

        PaintGridIconCell(e, columnName, color);
    }

    private void PaintRuleCommentCell(DataGridViewCellPaintingEventArgs e)
    {
        e.Paint(e.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border);
        if (e.Graphics is null || e.RowIndex < 0 || _rulesGrid.Rows[e.RowIndex].IsNewRow)
        {
            e.Handled = true;
            return;
        }

        var hasComment = !string.IsNullOrWhiteSpace(CellText(_rulesGrid.Rows[e.RowIndex], RuleCommentColumnName));
        var color = hasComment ? UiTheme.Accent : UiTheme.Muted;
        PaintGridNamedIcon(e, "Comment", color);
    }

    private void OnRulesGridCellMouseEnter(object? sender, DataGridViewCellEventArgs e)
    {
        if (_relaxedRuleView || e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        var columnName = _rulesGrid.Columns[e.ColumnIndex].Name;

        // 条件/注释列 → 手型; 拖拽手柄列 → 移动光标; 其它 → 默认。
        var isExisting = !_rulesGrid.Rows[e.RowIndex].IsNewRow;
        _rulesGrid.Cursor = columnName switch
        {
            "Condition" or RuleCommentColumnName when isExisting => Cursors.Hand,
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
        if (_relaxedRuleView)
        {
            return;
        }

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

        if (columnName == RuleCommentColumnName)
        {
            var comment = CellText(_rulesGrid.Rows[rowIndex], RuleCommentColumnName);
            return comment.Length == 0 ? "点击编辑注释" : comment;
        }

        var missingFields = GetMissingConditionFields(_rulesGrid.Rows[rowIndex]);
        var missingSpells = GetMissingConditionSpells(_rulesGrid.Rows[rowIndex]);
        var missingItems = GetMissingConditionItems(_rulesGrid.Rows[rowIndex]);
        if (missingFields.Count > 0 || missingSpells.Count > 0 || missingItems.Count > 0)
        {
            var messages = new List<string>();
            if (missingFields.Count > 0)
            {
                messages.Add($"条件字段不存在：{string.Join("、", missingFields)}");
                messages.Add("请先添加对应字段。");
            }

            messages.AddRange(missingSpells);
            messages.AddRange(missingItems);
            return string.Join('\n', messages);
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
                metadata.LogicDelayMs,
                metadata.ContinueLogic);
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
            "MoveUp" => "上移 (Alt+↑)",
            "MoveDown" => "下移 (Alt+↓)",
            "Copy" => "复制到下一行 (Ctrl+D)",
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

        var columnName = _adjustmentsGrid.Columns[e.ColumnIndex].Name;
        if (columnName == "Type"
            || (columnName == "Delta" && CanSelectAdjustmentBoolean(_adjustmentsGrid.Rows[e.RowIndex]))
            || (columnName == "Operation" && !IsCustomAdjustmentRow(_adjustmentsGrid.Rows[e.RowIndex]))
            || (columnName == "Field" && !IsCustomAdjustmentRow(_adjustmentsGrid.Rows[e.RowIndex])))
        {
            UiTheme.PaintDataGridViewComboBoxCell(_adjustmentsGrid, e);
            return;
        }

        if (columnName != "Delete")
        {
            return;
        }

        PaintGridIconCell(e, "Delete", UiTheme.Danger);
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

        PaintGridIconCell(e, "Delete", UiTheme.Danger);
    }

    private static void PaintGridIconCell(DataGridViewCellPaintingEventArgs e, string iconName, Color color)
    {
        e.Paint(e.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border);
        PaintGridNamedIcon(e, iconName, color);
    }

    private static void PaintGridNamedIcon(DataGridViewCellPaintingEventArgs e, string iconName, Color color)
    {
        if (e.Graphics is null)
        {
            e.Handled = true;
            return;
        }

        var size = (int)Math.Round(Math.Max(16, Math.Min(e.CellBounds.Width, e.CellBounds.Height) - 10) * 0.7f);
        UiIconCatalog.Draw(e.Graphics, iconName, new Rectangle(
            e.CellBounds.Left + (e.CellBounds.Width - size) / 2,
            e.CellBounds.Top + (e.CellBounds.Height - size) / 2,
            size,
            size), color);
        e.Handled = true;
    }

    // 新行不画拖拽抓手。
    private void PaintRuleDragHandle(DataGridViewCellPaintingEventArgs e)
    {
        e.Paint(e.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border);
        if (e.Graphics is null || e.RowIndex < 0 || _rulesGrid.Rows[e.RowIndex].IsNewRow)
        {
            e.Handled = true;
            return;
        }

        var color = _rulesGrid.Rows[e.RowIndex].Selected ? UiTheme.Text : UiTheme.Muted;
        PaintGridNamedIcon(e, "Drag", color);
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
        var row = _adjustmentsGrid.Rows[e.RowIndex];
        if (columnName == "Type"
            || (columnName == "Operation" && !IsCustomAdjustmentRow(row))
            || (columnName == "Field" && !IsCustomAdjustmentRow(row)))
        {
            ShowAdjustmentComboDropDown(e.RowIndex, e.ColumnIndex);
            return;
        }

        if (columnName == "Delta" && CanSelectAdjustmentBoolean(row))
        {
            var bounds = _adjustmentsGrid.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, true);
            var button = UiTheme.GetDropDownButtonBounds(_adjustmentsGrid, bounds);
            if (button.Contains(_adjustmentsGrid.PointToClient(Cursor.Position)))
            {
                ShowAdjustmentComboDropDown(e.RowIndex, e.ColumnIndex);
                return;
            }
        }

        CloseAdjustmentComboDropDown();
        if (IsCustomAdjustmentRow(row) && columnName is "Field" or "Delta")
        {
            _adjustmentsGrid.CurrentCell = row.Cells[e.ColumnIndex];
            _adjustmentsGrid.BeginEdit(selectAll: false);
            return;
        }
        if (columnName == "Delete")
        {
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

    private void OnAdjustmentsGridKeyDown(object? sender, KeyEventArgs e)
    {
        var cell = _adjustmentsGrid.CurrentCell;
        if (cell is null
            || cell.OwningColumn?.Name is not ("Type" or "Field" or "Operation" or "Delta")
            || (cell.OwningColumn.Name == "Field" && IsCustomAdjustmentRow(cell.OwningRow))
            || (cell.OwningColumn.Name == "Operation" && IsCustomAdjustmentRow(cell.OwningRow))
            || (cell.OwningColumn.Name == "Delta" && !CanSelectAdjustmentBoolean(cell.OwningRow))
            || (cell.OwningColumn.Name == "Delta" && !(e.KeyCode == Keys.F4 || (e.KeyCode == Keys.Down && e.Alt)))
            || e.KeyCode is not (Keys.Enter or Keys.Space or Keys.F4 or Keys.Down))
        {
            return;
        }

        if (e.KeyCode == Keys.Down && !e.Alt)
        {
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;
        ShowAdjustmentComboDropDown(cell.RowIndex, cell.ColumnIndex);
    }

    private void ShowAdjustmentComboDropDown(int rowIndex, int columnIndex)
    {
        CloseAdjustmentComboDropDown();
        _adjustmentsGrid.EndEdit();
        if (rowIndex < 0 || rowIndex >= _adjustmentsGrid.Rows.Count)
        {
            return;
        }

        var row = _adjustmentsGrid.Rows[rowIndex];
        if (row.IsNewRow)
        {
            rowIndex = _adjustmentsGrid.Rows.Add(true, string.Empty, 0, string.Empty);
            row = _adjustmentsGrid.Rows[rowIndex];
        }

        var cell = row.Cells[columnIndex];
        var columnName = cell.OwningColumn?.Name;
        if (columnName is not ("Type" or "Field" or "Operation" or "Delta")
            || (columnName == "Delta" && !CanSelectAdjustmentBoolean(row))
            || (columnName == "Operation" && IsCustomAdjustmentRow(row))
            || (columnName == "Field" && IsCustomAdjustmentRow(row)))
        {
            return;
        }

        _adjustmentsGrid.CurrentCell = cell;
        var currentValue = cell.Value?.ToString() ?? string.Empty;
        List<UiDropDownOption> options;
        if (columnName == "Type")
        {
            options = AdjustmentTypeOptions
                .Select(option => new UiDropDownOption(option.Text, option.Text))
                .ToList();
        }
        else if (columnName == "Operation")
        {
            options = new[] { "+", "-", "=" }.Select(value => new UiDropDownOption(value, value)).ToList();
        }
        else if (columnName == "Delta")
        {
            options = [new UiDropDownOption(ModuleValueAdjustment.ConditionBooleanValue,
                ModuleValueAdjustment.ConditionBooleanValue)];
        }
        else
        {
            var category = ReadAdjustmentType(row);
            var fields = BuildAdjustmentFields()
                .Where(field => category is null || field.Category == category)
                .GroupBy(field => field.Name, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
            options = fields
                .Select(field => new UiDropDownOption(
                    field.Name,
                    FormatAdjustmentFieldForDisplay(field.Name)))
                .Prepend(new UiDropDownOption(string.Empty, string.Empty))
                .ToList();
            if (!string.IsNullOrEmpty(currentValue)
                && !fields.Any(field => string.Equals(field.Name, currentValue, StringComparison.Ordinal)))
            {
                options.Insert(0, new UiDropDownOption(
                    currentValue,
                    FormatAdjustmentFieldForDisplay(currentValue)));
            }
        }

        var cellBounds = _adjustmentsGrid.GetCellDisplayRectangle(columnIndex, rowIndex, cutOverflow: true);
        ToolStripDropDown? dropDown = null;
        dropDown = UiDropDownPopup.Show(
            _adjustmentsGrid,
            cellBounds,
            options,
            currentValue,
            selected =>
            {
                cell.Value = selected.Value?.ToString() ?? string.Empty;
                _adjustmentsGrid.InvalidateCell(cell);
            },
            minimumWidth: columnName == "Type" ? 120 : 180,
            closed: () =>
            {
                if (ReferenceEquals(_adjustmentComboDropDown, dropDown))
                {
                    _adjustmentComboDropDown = null;
                }
            });
        _adjustmentComboDropDown = dropDown;
    }

    private void CloseAdjustmentComboDropDown()
    {
        var dropDown = _adjustmentComboDropDown;
        _adjustmentComboDropDown = null;
        dropDown?.Close(ToolStripDropDownCloseReason.AppClicked);
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
        if (_adjustmentsGrid.CurrentCell?.OwningColumn?.Name is not ("Field" or "Delta")
            || e.Control is not TextBox textBox)
        {
            return;
        }

        textBox.BackColor = UiTheme.Field;
        textBox.ForeColor = UiTheme.Text;
        textBox.BorderStyle = BorderStyle.None;
    }

    private void OpenAdjustmentConditionEditor(int rowIndex)
    {
        var row = _adjustmentsGrid.Rows[rowIndex];
        var current = row.IsNewRow ? string.Empty : CellText(row, "Condition");

        using var editor = new ConditionEditorForm(
            RefreshAndBuildConditionFields(),
            current,
            conditionFieldsProvider: () => RefreshAndBuildConditionFields(),
            spells: RefreshAndBuildConditionSpells(),
            conditionSpellsProvider: () => RefreshAndBuildConditionSpells(),
            items: RefreshAndBuildConditionItems(),
            conditionItemsProvider: () => RefreshAndBuildConditionItems(),
            numberArrayNames: _numberArrays.Select(array => array.Name).ToArray());
        if (editor.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        if (row.IsNewRow)
        {
            if (!string.IsNullOrWhiteSpace(editor.ConditionText))
            {
                _adjustmentsGrid.Rows.Add(true, string.Empty, 0, editor.ConditionText);
                RefreshAdjustmentFieldColumn();
            }

            return;
        }

        row.Cells["Condition"].Value = editor.ConditionText;
        RefreshAdjustmentValidation();
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
                RefreshAdjustmentFieldColumn();
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(field))
        {
            row.Cells["Field"].Value = field;
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
            string.Empty,
            Array.Empty<string>(),
            null,
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
        if (_relaxedRuleView)
        {
            _dragSourceRow = -1;
            if (e.Button == MouseButtons.Left)
            {
                var relaxedHit = _rulesGrid.HitTest(e.X, e.Y);
                if (relaxedHit.RowIndex >= 0)
                {
                    _suppressNextRulesCellClick = true;
                    DispatchRelaxedRuleClick(relaxedHit.RowIndex, e.Location);
                }
            }

            return;
        }

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
        if (_relaxedRuleView)
        {
            _dragSourceRow = -1;
            UpdateRelaxedRuleCursor(e.Location);
            return;
        }

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
        if (_relaxedRuleView)
        {
            QueueRelaxedCommentSync();
        }

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
            CellText(row, RuleCommentColumnName),
            // 子条件和延迟挂在 row.Tag, 随行一起被移动/拖拽/复制搬运。
            GetRuleMetadata(row).SubConditions,
            GetRuleMetadata(row).DelayMs,
            GetRuleMetadata(row).LogicDelayMs,
            GetRuleMetadata(row).ContinueLogic);
    }

    private void WriteRuleRow(DataGridViewRow row, RuleRowValues values)
    {
        row.Cells["Enabled"].Value = values.Enabled;
        EnsureComboItem(_spellColumn, values.Spell);
        row.Cells["Spell"].Value = values.Spell;
        row.Cells["MacroCondition"].Value = string.Empty;
        row.Cells["Condition"].Value = values.Condition;
        row.Cells[RuleCommentColumnName].Value = values.Comment;
        row.Tag = new RuleRowMetadata(
            values.SubConditions,
            values.DelayMs,
            values.LogicDelayMs,
            values.ContinueLogic);
        RebuildUnitCell(row, values.UnitText);
        RebuildMacroConditionCell(row, values.MacroCondition);
    }

    private void OpenConditionEditor(int rowIndex)
    {
        var row = _rulesGrid.Rows[rowIndex];
        var current = row.IsNewRow ? string.Empty : CellText(row, "Condition");
        var currentMetadata = row.IsNewRow ? new RuleRowMetadata() : GetRuleMetadata(row);
        var fields = RefreshAndBuildConditionFields(includeRuleSettings: true);
        var spells = RefreshAndBuildConditionSpells();
        var items = RefreshAndBuildConditionItems();

        using var editor = new ConditionEditorForm(
            fields,
            current,
            currentMetadata.SubConditions,
            allowSubConditions: true,
            delayMs: currentMetadata.DelayMs,
            logicDelayMs: currentMetadata.LogicDelayMs,
            continueLogic: currentMetadata.ContinueLogic,
            allowRuleSettings: true,
            conditionFieldsProvider: () => RefreshAndBuildConditionFields(includeRuleSettings: true),
            spells: spells,
            conditionSpellsProvider: () => RefreshAndBuildConditionSpells(),
            items: items,
            conditionItemsProvider: () => RefreshAndBuildConditionItems(),
            numberArrayNames: _numberArrays.Select(array => array.Name).ToArray());
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
                || editor.LogicDelayMs is > 0
                || editor.ContinueLogic is true)
            {
                var index = _rulesGrid.Rows.Add(true, null!, string.Empty, string.Empty, string.Empty, editor.ConditionText);
                _rulesGrid.Rows[index].Tag = new RuleRowMetadata(
                    subs,
                    editor.DelayMs,
                    editor.LogicDelayMs,
                    editor.ContinueLogic);
            }

            return;
        }

        row.Cells["Condition"].Value = editor.ConditionText;
        row.Tag = new RuleRowMetadata(subs, editor.DelayMs, editor.LogicDelayMs, editor.ContinueLogic);
        // 让「条件」列的装饰显示(主条件 且任一(…))立即刷新。
        _rulesGrid.InvalidateRow(rowIndex);
    }

    private void OpenRuleTextEditor(int rowIndex)
    {
        if (!IsExistingRuleRow(rowIndex))
        {
            return;
        }

        var row = _rulesGrid.Rows[rowIndex];
        using var editor = new RuleTextEditorForm(CellText(row, RuleCommentColumnName));
        if (editor.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        row.Cells[RuleCommentColumnName].Value = editor.CommentText;
        _rulesGrid.InvalidateRow(rowIndex);
    }

    // 条件字段 = 状态/技能字段 + 每个动态单位的裸名(存在)和值名称 + 动态数值。
    private IReadOnlyList<ConditionField> RefreshAndBuildConditionFields(bool includeRuleSettings = false)
    {
        // 配置可能由“更新配置”或外部文件同步在当前编辑会话中被重建；每次打开条件弹窗都读取最新目录。
        _fieldCatalog = ConditionFieldCatalog.Load(_resolveProfile().RuntimeDirectory, _resolveProfile().AddonRoot);
        InvalidateConditionFieldValidation();
        return BuildConditionFields(includeRuleSettings);
    }

    private IReadOnlyList<ConditionSpell> RefreshAndBuildConditionSpells()
    {
        ReloadCurrentClassSpellIds();
        return _currentClassConditionSpells.ToArray();
    }

    private IReadOnlyList<ConditionItem> RefreshAndBuildConditionItems()
    {
        ReloadCurrentClassSpellIds();
        return _currentClassConditionItems.ToArray();
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

        if (includeRuleSettings && seen.Add(ShigureConditionFields.ContinueLogic))
        {
            fields.Add(new ConditionField(
                ShigureConditionFields.ContinueLogic,
                "继续逻辑",
                ConditionFieldType.Bool,
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

            // 值名称: 导出所选单位目标字段的命名数值。
            if (!string.IsNullOrWhiteSpace(unit.ValueName) && seen.Add(unit.ValueName))
            {
                var fieldLabel = UnitSummary.DescribeTargetField(unit.TargetField);
                fields.Add(new ConditionField(
                    unit.ValueName,
                    $"{unit.ValueName} ({fieldLabel})",
                    ConditionFieldType.Int,
                    ConditionFieldCategory.DynamicUnit));
            }
        }

        foreach (var count in _counts)
        {
            if (!string.IsNullOrWhiteSpace(count.Name) && seen.Add(count.Name))
            {
                fields.Add(new ConditionField(count.Name, $"人数: {count.Name}", ConditionFieldType.Int, ConditionFieldCategory.DynamicValue));
            }
        }

        foreach (var count in _enemyCounts)
        {
            if (!string.IsNullOrWhiteSpace(count.Name) && seen.Add(count.Name))
            {
                fields.Add(new ConditionField(count.Name, $"敌人数: {count.Name}", ConditionFieldType.Int, ConditionFieldCategory.DynamicValue));
            }
        }

        foreach (var field in _averageHealthFields)
        {
            if (!string.IsNullOrWhiteSpace(field.Name) && seen.Add(field.Name))
            {
                fields.Add(new ConditionField(field.Name, $"平均血量: {field.Name}", ConditionFieldType.Int, ConditionFieldCategory.DynamicValue));
            }
        }

        foreach (var fieldName in GetAdjustmentTargetFields())
        {
            if (seen.Add(fieldName))
            {
                var custom = GetCustomAdjustmentFields().FirstOrDefault(field => field.Name == fieldName);
                fields.Add(new ConditionField(
                    fieldName,
                    custom?.DisplayName ?? $"{fieldName} (动态数值)",
                    custom?.Type ?? ConditionFieldType.Int,
                    custom?.Category ?? ConditionFieldCategory.DynamicValue));
            }
        }

        return fields;
    }

    private Control BuildActionRow()
    {
        var row = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 5,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(UiTheme.CardPadding, 12, UiTheme.CardPadding, 12)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 472));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var openFolderButton = UiTheme.CreateButton("打开目录", UiTheme.ButtonKind.Secondary);
        StyleModuleFooterButton(openFolderButton);
        openFolderButton.Dock = DockStyle.Fill;
        openFolderButton.Margin = new Padding(0, 0, 8, 0);
        openFolderButton.Click += (_, _) => OpenModuleFolder();
        _pathToolTip.SetToolTip(
            openFolderButton,
            "在资源管理器中打开模块目录；若已选中已保存模块则定位到对应文件");

        _ruleViewButton = UiTheme.CreateButton("宽松视图", UiTheme.ButtonKind.Secondary);
        StyleModuleFooterButton(_ruleViewButton);
        _ruleViewButton.Dock = DockStyle.Fill;
        _ruleViewButton.Margin = new Padding(0, 0, 8, 0);
        _ruleViewButton.Click += (_, _) => ToggleRuleView();
        _pathToolTip.SetToolTip(_ruleViewButton, "当前为紧缩视图，点击切换为宽松视图");

        _pathLabel.Dock = DockStyle.None;
        _pathLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _pathLabel.Height = UiTheme.Scale(this, 24);
        _pathLabel.Margin = new Padding(0, 0, 16, 0);
        _pathLabel.ForeColor = UiTheme.Muted;
        _pathLabel.BackColor = row.FillColor;
        _pathLabel.TextAlign = ContentAlignment.MiddleLeft;
        _pathLabel.AutoEllipsis = true;
        _pathLabel.TextChanged += (_, _) => _pathToolTip.SetToolTip(_pathLabel, _pathLabel.Text);

        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = row.FillColor,
            ColumnCount = 7,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _addButton = UiTheme.CreateButton("新建", UiTheme.ButtonKind.Secondary);
        StyleModuleFooterButton(_addButton);
        _addButton.Dock = DockStyle.Fill;
        _addButton.Margin = Padding.Empty;
        _addButton.Click += async (_, _) => await RunModuleCommandAsync(AddModuleAsync);
        _pathToolTip.SetToolTip(_addButton, "新建模块 (Ctrl+N)");

        _deleteButton = UiTheme.CreateButton("删除", UiTheme.ButtonKind.Danger);
        StyleModuleFooterButton(_deleteButton);
        _deleteButton.Dock = DockStyle.Fill;
        _deleteButton.Margin = Padding.Empty;
        _deleteButton.Click += async (_, _) => await RunModuleCommandAsync(DeleteSelectedModuleAsync);

        _saveButton = UiTheme.CreateButton("保存", UiTheme.ButtonKind.Primary);
        StyleModuleFooterButton(_saveButton);
        _saveButton.Dock = DockStyle.Fill;
        _saveButton.Margin = new Padding(0);
        _saveButton.Click += async (_, _) => await RunModuleCommandAsync(SaveSelectedModuleAsync);
        _pathToolTip.SetToolTip(_saveButton, "保存当前模块 (Ctrl+S)");

        _saveAllButton = UiTheme.CreateButton("保存所有", UiTheme.ButtonKind.Secondary);
        StyleModuleFooterButton(_saveAllButton);
        _saveAllButton.Dock = DockStyle.Fill;
        _saveAllButton.Click += async (_, _) => await RunModuleCommandAsync(SaveAllModulesAsync);
        _pathToolTip.SetToolTip(_saveAllButton, "依次保存所有职业的模块，包含当前模块的未保存编辑");

        buttons.Controls.Add(_addButton, 0, 0);
        buttons.Controls.Add(_deleteButton, 2, 0);
        buttons.Controls.Add(_saveButton, 4, 0);
        buttons.Controls.Add(_saveAllButton, 6, 0);

        row.Controls.Add(_pathLabel, 0, 0);
        row.Controls.Add(BuildSidebarFooter(row.FillColor), 1, 0);
        row.Controls.Add(openFolderButton, 2, 0);
        row.Controls.Add(_ruleViewButton, 3, 0);
        row.Controls.Add(buttons, 4, 0);
        row.Resize += (_, _) =>
        {
            // 按钮集中靠右，左侧为路径预留空间；小窗口缩短按钮并省略长路径。
            var gap = UiTheme.Scale(row, 8);
            var width = Math.Clamp(
                (row.ClientSize.Width - row.Padding.Horizontal - gap * 7 - UiTheme.Scale(row, 160)) / 8,
                UiTheme.Scale(row, 72), UiTheme.Scale(row, 112));
            row.ColumnStyles[1].Width = width * 2 + gap * 3;
            row.ColumnStyles[2].Width = width + gap;
            row.ColumnStyles[3].Width = width + gap;
            row.ColumnStyles[4].Width = width * 4 + gap * 3;
        };
        return row;
    }

    private void OpenModuleFolder()
    {
        var moduleDirectory = _moduleStore.ModuleDirectory;
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

        _allModules = _moduleStore.GetModulesForDisplay().ToList();
        ApplyModuleClassFilter(preserveSelection: false);
    }

    private void ApplyModuleClassFilter(bool preserveSelection)
    {
        var selectedId = preserveSelection ? _selectedModule?.Id : null;
        _modules = _filterClassId is { } classId
            ? _allModules.Where(module => module.Match.ClassId == classId).ToList()
            : _allModules.ToList();

        _moduleList.BeginUpdate();
        try
        {
            _moduleList.Items.Clear();
            foreach (var module in _modules)
            {
                _moduleList.Items.Add(ModuleDisplay.FormatListItem(module));
            }
        }
        finally
        {
            _moduleList.EndUpdate();
        }

        if (_modules.Count == 0)
        {
            ClearEditor();
            return;
        }

        var index = 0;
        if (selectedId is not null)
        {
            var matched = _modules.FindIndex(module => module.Id == selectedId);
            if (matched >= 0)
            {
                index = matched;
            }
        }

        if (_moduleList.SelectedIndex == index)
        {
            SelectModule(index);
        }
        else
        {
            _moduleList.SelectedIndex = index;
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
        CloseRulesComboDropDown();
        CloseAdjustmentComboDropDown();
        _nameBox.Text = module.Name;
        _authorBox.Text = module.Author;
        _recommendedTalentBox.Text = module.RecommendedTalent;
        SetEditorEnabled(hasModule: true);
        // 先填充动态单位/数量, 后续目标下拉与条件字段都依赖它们。
        _units.Clear();
        _units.AddRange(module.Units.Select(unit => unit.Clone()));
        _counts.Clear();
        _counts.AddRange(module.Counts.Select(count => count.Clone()));
        _enemyCounts.Clear();
        _enemyCounts.AddRange(module.EnemyCounts.Select(count => count.Clone()));
        _averageHealthFields.Clear();
        _averageHealthFields.AddRange(module.AverageHealthFields.Select(field => field.Clone()));
        _valueAdjustments.Clear();
        _valueAdjustments.AddRange(module.ValueAdjustments.Select(adjustment => adjustment.Clone()));
        _numberArrays.Clear();
        _numberArrays.AddRange(module.NumberArrays.Select(array => array.Clone()));
        RefreshNumberArraysList();
        SelectClass(module.Match.ClassId);
        SelectSpec(module.Match.SpecId);
        SelectPartyType(module.Match.PartyType);
        SelectHeroTalent(module.Match.HeroTalent);
        RefreshUnitsList();
        _pathLabel.Text = module.FilePath ?? "尚未保存";
        _adjustmentsGrid.Rows.Clear();
        _formulaAdjustmentsGrid.Rows.Clear();
        RefreshAdjustmentFieldColumn();
        foreach (var adjustment in _valueAdjustments)
        {
            if (string.IsNullOrWhiteSpace(adjustment.Formula))
            {
                var index = _adjustmentsGrid.Rows.Add(adjustment.Enabled, adjustment.Field,
                    adjustment.Value ?? adjustment.Delta.ToString(System.Globalization.CultureInfo.InvariantCulture), adjustment.Condition);
                _adjustmentsGrid.Rows[index].Cells["Operation"].Value = adjustment.Operation;
                // 新数据恢复自建标记，旧数据由字段名回填类型。
                if (adjustment.IsCustom)
                {
                    _suppressAdjustmentTypeChange = true;
                    try
                    {
                        _adjustmentsGrid.Rows[index].Cells["Type"].Value = "自建";
                    }
                    finally
                    {
                        _suppressAdjustmentTypeChange = false;
                    }
                    RebuildAdjustmentFieldCell(_adjustmentsGrid.Rows[index], adjustment.Field, keepCustom: true);
                }
                else
                {
                    ApplyAdjustmentRowType(_adjustmentsGrid.Rows[index], adjustment.Field);
                }
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

        HideRelaxedCommentEditors();
        _rulesGrid.Rows.Clear();
        RefreshKeymapColumns();

        foreach (var rule in module.Rules)
        {
            var ruleSpellDisplay = DisplayRuleSpell(rule.Spell);
            // 动态目标优先显示单位名；保留单位显示中文，其余团队槽位显示数字。
            var unitText = !string.IsNullOrWhiteSpace(rule.UnitName)
                ? rule.UnitName!
                : rule.Unit is { } unit ? ReservedUnit.ToDisplayText(unit) : string.Empty;
            EnsureComboItem(_spellColumn, ruleSpellDisplay);
            // 先加行(目标先留空), 再按技能重建目标选项并写回目标值, 避免值不在选项内被吞掉。
            var index = _rulesGrid.Rows.Add(
                rule.Enabled,
                GetRuleSpellIcon(ruleSpellDisplay)!,
                ruleSpellDisplay,
                string.Empty,
                string.Empty,
                rule.Condition,
                rule.Comment);
            _rulesGrid.Rows[index].Tag = new RuleRowMetadata(
                rule.SubConditions,
                rule.DelayMs,
                rule.LogicDelayMs,
                rule.ContinueLogic);
            RebuildUnitCell(_rulesGrid.Rows[index], unitText);
            RebuildMacroConditionCell(_rulesGrid.Rows[index], rule.MacroCondition);
        }
        _editorBaseline = CaptureEditorFingerprint();
    }

    private void ClearEditor()
    {
        CloseRulesComboDropDown();
        CloseAdjustmentComboDropDown();
        _selectedModule = null;
        _nameBox.Clear();
        _authorBox.Clear();
        _recommendedTalentBox.Clear();
        _units.Clear();
        _counts.Clear();
        _enemyCounts.Clear();
        _averageHealthFields.Clear();
        _valueAdjustments.Clear();
        _numberArrays.Clear();
        RefreshNumberArraysList();
        RefreshUnitsList();
        SelectClass(null);
        SelectSpec(null);
        SelectPartyType(null);
        SelectHeroTalent(null);
        _pathLabel.Text = "无模块";
        _adjustmentsGrid.Rows.Clear();
        _formulaAdjustmentsGrid.Rows.Clear();
        RefreshAdjustmentFieldColumn();
        HideRelaxedCommentEditors();
        _rulesGrid.Rows.Clear();
        SetEditorEnabled(hasModule: false);
        _editorBaseline = null;
    }

    // 无选中模块时禁用保存/删除(否则点了静默无反应), 并在编辑区显示引导提示。
    private void SetEditorEnabled(bool hasModule)
    {
        _saveButton.Enabled = hasModule && !_moduleCommandInProgress;
        _saveAllButton.Enabled = _allModules.Count > 0 && !_moduleCommandInProgress;
        _reloadButton.Enabled = !_moduleCommandInProgress;
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

    private async Task SaveAllModulesAsync()
    {
        // 从完整列表取快照，职业筛选只影响显示，不影响批量保存范围。
        var modules = _moduleStore.GetModulesForDisplay().ToList();
        if (modules.Count == 0)
        {
            return;
        }

        var selectedId = _selectedModule?.Id;
        var editorBaseline = _editorBaseline;
        ModuleDefinition? editedModule = null;
        if (_selectedModule is not null)
        {
            // 先提交当前单元格，再读取尚未保存的编辑，避免批量刷新时丢失。
            _rulesGrid.EndEdit();
            _adjustmentsGrid.EndEdit();
            _formulaAdjustmentsGrid.EndEdit();
            if (!TryReadModule(out editedModule))
            {
                return;
            }

            var selectedIndex = modules.FindIndex(module =>
                string.Equals(module.Id, selectedId, StringComparison.OrdinalIgnoreCase));
            if (selectedIndex >= 0)
            {
                modules[selectedIndex] = editedModule;
            }
        }

        var warnings = new List<string>();
        var errors = new List<string>();
        var savedCount = 0;
        var selectedSaved = false;
        foreach (var module in modules)
        {
            try
            {
                if (!TryValidateNumberArrayReferences(module, out var arrayError))
                {
                    throw new InvalidOperationException(arrayError);
                }
                if (!TryUpgradeLegacySpellReferences(module, out var upgradeError))
                {
                    throw new InvalidOperationException(upgradeError);
                }

                module.Version = AppInfo.Version;
                var warning = _captureDependencies(module);
                _moduleStore.Save(module);
                savedCount++;
                selectedSaved |= string.Equals(module.Id, selectedId, StringComparison.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(warning))
                {
                    warnings.Add($"{module.Name}：{warning}");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{module.Name}：{ex.Message}");
            }
        }

        LoadModules(reloadStore: false);
        var index = _modules.FindIndex(module =>
            string.Equals(module.Id, selectedId, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            _moduleList.SelectedIndex = index;
            if (!selectedSaved && editedModule is not null)
            {
                // 当前模块保存失败时保留其编辑内容和未保存标记。
                _selectedModule = editedModule;
                FillEditor(editedModule);
                _editorBaseline = editorBaseline;
            }
        }

        // 全部写盘完成后只重启一次运行时。
        if (savedCount > 0)
        {
            await _runtimeRestartRequested();
        }

        var message = $"已保存 {savedCount}/{modules.Count} 个模块。";
        if (errors.Count > 0)
        {
            message += "\n\n保存失败：\n" + string.Join("\n", errors);
        }
        if (warnings.Count > 0)
        {
            message += "\n\n依赖提示：\n" + string.Join("\n", warnings);
        }
        MessageBox.Show(message, "保存所有模块", MessageBoxButtons.OK,
            errors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
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
            SpecId = _resolveProfile().AddonName.Equals("Shingen", StringComparison.OrdinalIgnoreCase)
                ? null : ReadMatchCombo(_specBox),
            PartyType = ReadPartyTypeCombo(),
            HeroTalent = _resolveProfile().AddonName.Equals("Shingen", StringComparison.OrdinalIgnoreCase)
                ? null : ReadMatchCombo(_heroTalentBox)
        };

        module.Units = _units.Select(unit => unit.Clone()).ToList();
        module.Counts = _counts.Select(count => count.Clone()).ToList();
        module.EnemyCounts = _enemyCounts.Select(count => count.Clone()).ToList();
        module.AverageHealthFields = _averageHealthFields.Select(field => field.Clone()).ToList();
        if (!TryReadValueAdjustments(out var valueAdjustments, out var adjustmentError))
        {
            MessageBox.Show(adjustmentError, "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        module.ValueAdjustments = valueAdjustments;
        module.NumberArrays = _numberArrays.Select(array => array.Clone()).ToList();
        if (!TryReadRules(out var rules, out var rulesError))
        {
            MessageBox.Show(rulesError, "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        module.Rules = rules;
        if (!TryValidateNumberArrayReferences(module, out var arrayError))
        {
            MessageBox.Show(arrayError, "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (!TryUpgradeLegacySpellReferences(module, out var upgradeError))
        {
            MessageBox.Show(upgradeError, "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        return true;
    }

    private static bool TryValidateNumberArrayReferences(ModuleDefinition module, out string error)
    {
        error = string.Empty;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var array in module.NumberArrays)
        {
            if (!ConditionExpression.IsArrayName(array.Name) || !names.Add(array.Name))
            {
                error = $"数组名称“{array.Name}”无效或重复。";
                return false;
            }
        }

        static string? MissingName(string? expression, HashSet<string> names)
        {
            foreach (var term in ConditionExpression.Parse(expression))
            {
                if (term.NamedArray && !names.Contains(term.Value))
                    return term.Value;
            }
            return null;
        }

        for (var i = 0; i < module.Rules.Count; i++)
        {
            if (MissingName(module.Rules[i].Condition, names) is { } missing)
            {
                error = $"规则第 {i + 1} 行引用的数组“{missing}”不存在。";
                return false;
            }
            foreach (var sub in module.Rules[i].SubConditions ?? [])
            {
                if (MissingName(sub, names) is not { } subMissing) continue;
                error = $"规则第 {i + 1} 行子条件引用的数组“{subMissing}”不存在。";
                return false;
            }
        }
        for (var i = 0; i < module.ValueAdjustments.Count; i++)
        {
            if (MissingName(module.ValueAdjustments[i].Condition, names) is not { } missing) continue;
            error = $"动态数值第 {i + 1} 行引用的数组“{missing}”不存在。";
            return false;
        }
        var filters = module.Units.Select(unit => (unit.Name, unit.FilterGroups))
            .Concat(module.Counts.Select(count => (count.Name, count.FilterGroups)))
            .Concat(module.EnemyCounts.Select(count => (count.Name, count.FilterGroups)))
            .Concat(module.AverageHealthFields.Select(field => (field.Name, field.FilterGroups)));
        foreach (var (fieldName, groups) in filters)
        {
            foreach (var condition in groups.SelectMany(group => group.Conditions))
            {
                if (condition.ValueKind != CountConditionValueKind.NumberArray) continue;
                if (condition.ValueField is { } arrayName && names.Contains(arrayName)) continue;
                error = $"动态字段“{fieldName}”引用的数组“{condition.ValueField}”不存在。";
                return false;
            }
        }
        return true;
    }

    private bool TryUpgradeLegacySpellReferences(ModuleDefinition module, out string error)
    {
        error = string.Empty;
        var failure = string.Empty;
        var candidates = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Add(string legacy, string current)
        {
            if (string.IsNullOrWhiteSpace(legacy) || string.IsNullOrWhiteSpace(current))
            {
                return;
            }

            if (!candidates.TryGetValue(legacy, out var values))
            {
                values = new HashSet<string>(StringComparer.Ordinal);
                candidates[legacy] = values;
            }
            values.Add(current);
        }

        var spec = module.Dependencies?.Config?.Spec;
        if (spec is not null)
        {
            AddAuraEntries(spec.PlayerAuras, "player", string.Empty);
            AddAuraEntries(spec.TargetHarmfulAuras, "target.harmful", "目标");
            AddAuraEntries(spec.TargetHelpfulAuras, "target.helpful", "目标");
            AddAuraEntries(spec.FocusHarmfulAuras, "focus.harmful", "焦点");
            AddAuraEntries(spec.FocusHelpfulAuras, "focus.helpful", "焦点");
            foreach (var spell in spec.Spells ?? [])
            {
                if (spell.SpellId <= 0 || string.IsNullOrWhiteSpace(spell.Name))
                {
                    continue;
                }
                Add($"spells.{spell.Name}", SpellFieldKey.Spell(spell.SpellId));
                if (spell.Charge)
                {
                    Add($"spells.{spell.Name}充能", SpellFieldKey.Spell(spell.SpellId, SpellFieldKey.SpellChargeCooldown));
                }
                if (spell.Charge || spell.CastCount is > 0)
                {
                    Add($"spells.{spell.Name}层数", SpellFieldKey.Spell(spell.SpellId, SpellFieldKey.SpellCount));
                }
            }
        }

        foreach (var field in _fieldCatalog.GetGroupFields(module.Match.ClassId, module.Match.SpecId)
                     .Where(field => field.Name.StartsWith("auras.", StringComparison.Ordinal)))
        {
            var display = field.DisplayName.Split(" / ", 2, StringSplitOptions.TrimEntries)[0];
            Add(display, field.Name);
        }

        var ambiguous = candidates.Where(pair => pair.Value.Count != 1).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        var replacements = candidates
            .Where(pair => pair.Value.Count == 1)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Single(), StringComparer.Ordinal);
        var dynamicFieldNames = module.Units
            .SelectMany(unit => new[] { unit.Name, unit.ValueName })
            .Concat(module.Counts.Select(count => count.Name))
            .Concat(module.EnemyCounts.Select(count => count.Name))
            .Concat(module.AverageHealthFields.Select(field => field.Name))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        for (var ruleIndex = 0; ruleIndex < module.Rules.Count; ruleIndex++)
        {
            var rule = module.Rules[ruleIndex];
            var rowLabel = $"规则第 {ruleIndex + 1} 行";
            if (!TryReplace(rule.Condition, $"{rowLabel}主条件", out var condition)
                || rule.SubConditions is not null
                && !TryReplaceList(rule.SubConditions, index => $"{rowLabel}子条件第 {index + 1} 行"))
            {
                error = failure;
                return false;
            }
            rule.Condition = condition;
        }
        for (var adjustmentIndex = 0; adjustmentIndex < module.ValueAdjustments.Count; adjustmentIndex++)
        {
            var adjustment = module.ValueAdjustments[adjustmentIndex];
            var rowLabel = $"动态数值第 {adjustmentIndex + 1} 行";
            if (!TryReplace(adjustment.Condition, $"{rowLabel}条件", out var condition)
                || !TryReplace(adjustment.Field, $"{rowLabel}调整目标", out var field)
                || !TryReplace(adjustment.Formula, $"{rowLabel}公式", out var formula))
            {
                error = failure;
                return false;
            }
            adjustment.Condition = condition;
            adjustment.Field = field;
            adjustment.Formula = formula;
        }

        return true;

        void AddAuraEntries(IEnumerable<ModuleAuraSnapshot>? entries, string scope, string prefix)
        {
            foreach (var aura in entries ?? [])
            {
                var id = SpellFieldKey.CanonicalAuraId(aura.SpellId, aura.SpellIds);
                if (id is null || string.IsNullOrWhiteSpace(aura.Name))
                {
                    continue;
                }
                Add($"auras.{prefix}{aura.Name}", SpellFieldKey.Aura(scope, id.Value));
                if (aura.MaxApps is not null)
                {
                    Add($"auras.{prefix}{aura.Name}层数", SpellFieldKey.Aura(scope, id.Value, SpellFieldKey.AuraApplications));
                }
            }
        }

        bool TryReplace(string source, string location, out string result)
        {
            result = source ?? string.Empty;

            // 兼容修复旧迁移逻辑曾经造成的子串污染，例如：
            // “无救赎最低”被错误写成“无auras.194384.value最低”。
            foreach (var dynamicName in dynamicFieldNames)
            {
                foreach (var pair in replacements)
                {
                    if (!dynamicName.Contains(pair.Key, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var corrupted = dynamicName.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
                    if (!string.Equals(corrupted, dynamicName, StringComparison.Ordinal))
                    {
                        result = ReplaceFieldReference(result, corrupted, dynamicName);
                    }
                }
            }

            // 动态单位/生命值/数量名称优先于旧光环显示名。先暂存完整引用，
            // 防止名称恰好等于或包含旧光环名时被迁移逻辑改写。
            var protectedReferences = new List<(string Placeholder, string Name)>();
            for (var index = 0; index < dynamicFieldNames.Length; index++)
            {
                var name = dynamicFieldNames[index];
                if (!ContainsFieldReference(result, name))
                {
                    continue;
                }

                var placeholder = $"__SHIGURE_DYNAMIC_FIELD_{index}__";
                result = ReplaceFieldReference(result, name, placeholder);
                protectedReferences.Add((placeholder, name));
            }

            foreach (var key in ambiguous)
            {
                if (ContainsFieldReference(result, key))
                {
                    failure = $"{location}：旧模块字段“{key}”对应多个 spellId，请重新选择后再保存。";
                    return false;
                }
            }
            foreach (var pair in replacements.OrderByDescending(pair => pair.Key.Length))
            {
                result = ReplaceFieldReference(result, pair.Key, pair.Value);
            }
            if (Regex.IsMatch(result, @"\b(?:auras|spells)\.[^\s&|<>=!+\-*/()]+"))
            {
                var unresolved = Regex.Match(result, @"\b(?:auras|spells)\.[^\s&|<>=!+\-*/()]+").Value;
                if (!SpellFieldKey.TryParseAura(unresolved, out _, out _, out _)
                    && !SpellFieldKey.TryParseAuraMember(unresolved, out _, out _)
                    && !SpellFieldKey.TryParseSpell(unresolved, out _, out _))
                {
                    failure = $"{location}：旧模块字段“{unresolved}”无法转换为 spellId，请重新选择后再保存。";
                    return false;
                }
            }
            foreach (var (placeholder, name) in protectedReferences)
            {
                result = result.Replace(placeholder, name, StringComparison.Ordinal);
            }
            return true;
        }

        static bool ContainsFieldReference(string source, string field)
            => Regex.IsMatch(source, FieldReferencePattern(field));

        static string ReplaceFieldReference(string source, string field, string replacement)
            => Regex.Replace(source, FieldReferencePattern(field), _ => replacement);

        static string FieldReferencePattern(string field)
            => $@"(?<![_$\p{{L}}\p{{N}}]){Regex.Escape(field)}(?![_$\p{{L}}\p{{N}}])";

        bool TryReplaceList(List<string> values, Func<int, string> location)
        {
            for (var i = 0; i < values.Count; i++)
            {
                if (!TryReplace(values[i], location(i), out var replaced))
                {
                    return false;
                }
                values[i] = replaced;
            }
            return true;
        }
    }

    private bool TryReadValueAdjustments(out List<ModuleValueAdjustment> adjustments, out string error)
    {
        adjustments = new List<ModuleValueAdjustment>();
        var adjustmentRowNumbers = new Dictionary<ModuleValueAdjustment, int>();
        error = string.Empty;

        foreach (DataGridViewRow row in _adjustmentsGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var field = CellText(row, "Field");
            var condition = CellText(row, "Condition");
            var value = CellText(row, "Delta");
            if (string.IsNullOrWhiteSpace(field)
                && string.IsNullOrWhiteSpace(condition)
                && (string.IsNullOrWhiteSpace(value) || value == "0")
                && !IsCustomAdjustmentRow(row))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(field))
            {
                error = $"条件动态数值第 {row.Index + 1} 行缺少字段。";
                return false;
            }

            var adjustment = new ModuleValueAdjustment
            {
                Enabled = CellBool(row, "Enabled", defaultValue: true),
                Field = field,
                Operation = ReadAdjustmentOperation(row),
                Value = value,
                IsCustom = IsCustomAdjustmentRow(row),
                Formula = string.Empty,
                Condition = condition
            };
            if (!adjustment.TryGetOperand(out var operand))
            {
                error = $"条件动态数值第 {row.Index + 1} 行：调整必须是整数或布尔值，布尔值只能使用“=”。";
                return false;
            }
            if (operand is bool && (_counts.Any(count => count.Name == field)
                                    || _enemyCounts.Any(count => count.Name == field)
                                    || _averageHealthFields.Any(item => item.Name == field)))
            {
                error = $"条件动态数值第 {row.Index + 1} 行：数量和平均血量字段只能赋整数。";
                return false;
            }
            adjustment.Delta = operand is int number ? number : 0;
            adjustment.Value = adjustment.IsConditionBoolean ? ModuleValueAdjustment.ConditionBooleanValue
                : operand is bool boolean ? (boolean ? "true" : "false")
                : ((int)operand!).ToString(System.Globalization.CultureInfo.InvariantCulture);
            adjustments.Add(adjustment);
            adjustmentRowNumbers[adjustment] = row.Index + 1;
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

        var reserved = new HashSet<string>(
            _fieldCatalog.GetFields(ReadMatchCombo(_classBox), ReadMatchCombo(_specBox)).Select(field => field.Name),
            StringComparer.OrdinalIgnoreCase);
        reserved.UnionWith(_units.SelectMany(unit => new[] { unit.Name, unit.ValueName ?? string.Empty }));
        reserved.UnionWith(_counts.Select(count => count.Name));
        reserved.UnionWith(_enemyCounts.Select(count => count.Name));
        reserved.UnionWith(_averageHealthFields.Select(field => field.Name));
        reserved.UnionWith(_numberArrays.Select(array => array.Name));
        reserved.UnionWith(adjustments.Where(adjustment => !string.IsNullOrWhiteSpace(adjustment.Formula))
            .Select(adjustment => adjustment.Field));
        reserved.UnionWith(new[] { ShigureConditionFields.Delay, ShigureConditionFields.LogicDelay,
            ShigureConditionFields.ContinueLogic, ShigureConditionFields.TotalCombatTimeSeconds,
            "延迟 (ms)", "逻辑延迟 (ms)", "继续逻辑", "true", "false", "null", "nil" });
        var customTypes = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var adjustment in adjustments.Where(adjustment => adjustment.IsCustom))
        {
            var rowNumber = adjustmentRowNumbers[adjustment];
            var name = adjustment.Field;
            if (name.Contains('.') || name.Contains('$') || double.TryParse(name, out _) || reserved.Contains(name))
            {
                error = $"条件动态数值第 {rowNumber} 行：自建字段名称“{name}”无效或已被其它字段占用。";
                return false;
            }
            adjustment.TryGetOperand(out var operand);
            var isBoolean = operand is bool;
            if (customTypes.TryGetValue(name, out var existingType) && existingType != isBoolean)
            {
                error = $"条件动态数值第 {rowNumber} 行：自建字段“{name}”不能混用整数与布尔值。";
                return false;
            }
            customTypes[name] = isBoolean;
        }
        foreach (var adjustment in adjustments.Where(adjustment => !adjustment.IsCustom
                     && string.IsNullOrWhiteSpace(adjustment.Formula) && customTypes.ContainsKey(adjustment.Field)))
        {
            error = $"自建字段“{adjustment.Field}”的调整行请选择“自建”类型。";
            return false;
        }
        return true;
    }

    // 赋值时给布尔值下拉预留空间，加减时使用完整的整数输入区域。
    private sealed class AdjustmentValueCell : DataGridViewTextBoxCell
    {
        public override void PositionEditingControl(bool setLocation, bool setSize, Rectangle cellBounds,
            Rectangle cellClip, DataGridViewCellStyle cellStyle, bool singleVerticalBorderAdded,
            bool singleHorizontalBorderAdded, bool isFirstDisplayedColumn, bool isFirstDisplayedRow)
        {
            if (DataGridView is { } grid && cellBounds.Width > 0 && CanSelectAdjustmentBoolean(OwningRow))
            {
                var button = UiTheme.GetDropDownButtonBounds(grid, new Rectangle(Point.Empty, cellBounds.Size));
                cellBounds.Width = Math.Max(1, button.Left - UiTheme.Scale(grid, 4));
                cellClip = Rectangle.Intersect(cellClip, cellBounds);
            }
            base.PositionEditingControl(setLocation, setSize, cellBounds, cellClip, cellStyle,
                singleVerticalBorderAdded, singleHorizontalBorderAdded, isFirstDisplayedColumn, isFirstDisplayedRow);
        }
    }

    private bool TryReadRules(out List<ModuleRule> rules, out string error)
    {
        error = string.Empty;
        var unitNames = new HashSet<string>(
            _units.Where(unit => !string.IsNullOrWhiteSpace(unit.Name)).Select(unit => unit.Name),
            StringComparer.Ordinal);
        rules = new List<ModuleRule>();
        foreach (DataGridViewRow row in _rulesGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var condition = CellText(row, "Condition");
            var comment = CellText(row, RuleCommentColumnName);
            var spell = CellText(row, "Spell");
            var unitText = CellText(row, "Unit");
            var macroCondition = CellText(row, "MacroCondition");
            var metadata = GetRuleMetadata(row);
            if (string.IsNullOrWhiteSpace(condition)
                && string.IsNullOrWhiteSpace(comment)
                && string.IsNullOrWhiteSpace(spell)
                && string.IsNullOrWhiteSpace(unitText)
                && string.IsNullOrWhiteSpace(macroCondition)
                && metadata.SubConditions.Count == 0
                && metadata.DelayMs is not > 0
                && metadata.LogicDelayMs is not > 0
                && metadata.ContinueLogic is not true)
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
                Comment = comment,
                Unit = isDynamic ? null : ReservedUnit.ParseDisplayText(unitText),
                UnitName = isDynamic ? unitText : null,
                Spell = spell,
                MacroCondition = MacroConditionText.ParseDisplayText(macroCondition),
                Hotkey = string.Empty,
                Step = string.Empty,
                SubConditions = subs is { Count: > 0 } ? subs : null,
                DelayMs = metadata.DelayMs,
                LogicDelayMs = metadata.LogicDelayMs,
                ContinueLogic = metadata.ContinueLogic
            });
        }

        return true;
    }

    private string DisplayRuleSpell(string? persisted)
    {
        var value = persisted?.Trim() ?? string.Empty;
        if (value.Length == 0 || ModuleSpecialActions.IsPauseSpell(value)
            || ModuleSpecialActions.IsFailedSpell(value)
            || ModuleSpecialActions.IsFailedItem(value)
            || ModuleSpecialActions.IsOneKeySpell(value))
        {
            return value;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var spellId))
        {
            return _currentClassConditionSpells.FirstOrDefault(spell => spell.SpellId == spellId)?.Name ?? value;
        }

        return value;
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

    private static Label AddMatchField(
        TableLayoutPanel row,
        string label,
        UiDropDown box,
        int column)
    {
        var fieldLabel = CreateLabel(label);
        fieldLabel.AutoSize = false;
        fieldLabel.Margin = Padding.Empty;
        row.Controls.Add(fieldLabel, column, 0);
        UiTheme.StyleComboBox(box);
        // 标签与下拉同处一行；固定列宽下 Dock.Fill 填满单元格。
        box.Dock = DockStyle.Fill;
        box.Margin = Padding.Empty;
        row.Controls.Add(box, column + 1, 0);
        return fieldLabel;
    }

    private void UpdateMatchRowProfile()
    {
        if (_matchRow is null) return;
        var forever = _resolveProfile().AddonName.Equals("Shingen", StringComparison.OrdinalIgnoreCase);
        if (forever)
        {
            _specBox.SelectedIndex = 0;
            _heroTalentBox.SelectedIndex = 0;
        }
        _specBox.Visible = !forever;
        _heroTalentBox.Visible = !forever;
        if (_specLabel is not null) _specLabel.Visible = !forever;
        if (_heroTalentLabel is not null) _heroTalentLabel.Visible = !forever;
        for (var i = 0; i < 11; i++)
        {
            if (i is 2 or 5 or 8)
            {
                _matchRow.ColumnStyles[i].SizeType = forever && i != 2
                    ? SizeType.Absolute : SizeType.Percent;
                _matchRow.ColumnStyles[i].Width = forever && i != 2 ? 0 : 100;
            }
            else if (forever && i is >= 3 and <= 7)
            {
                _matchRow.ColumnStyles[i].SizeType = SizeType.Absolute;
                _matchRow.ColumnStyles[i].Width = 0;
            }
            else if (!forever && (i is 3 or 6))
            {
                _matchRow.ColumnStyles[i].SizeType = SizeType.Absolute;
                _matchRow.ColumnStyles[i].Width = MeasureLabelColumnWidth(i == 3 ? "专精:" : "英雄天赋:", Font);
            }
            else if (!forever && (i is 4 or 7))
            {
                _matchRow.ColumnStyles[i].SizeType = SizeType.Absolute;
                _matchRow.ColumnStyles[i].Width = UiTheme.ModuleMatchFieldWidth;
            }
        }
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
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
        button.Height = ModuleFooterButtonHeight;
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

    private static int? ReadMatchCombo(UiDropDown comboBox)
    {
        return comboBox.SelectedItem is MatchOption option ? option.Value : null;
    }

    private void ResetClassOptions(UiDropDown comboBox)
    {
        comboBox.Items.Clear();
        comboBox.Items.AddRange(GetAvailableClasses()
            .Select(item => new MatchOption($"{item.Name} ({item.Id})", item.Id))
            .Prepend(new MatchOption("任意 (*)", null))
            .ToArray());
        comboBox.SelectedIndex = 0;
    }

    private static void ResetSpecOptions(UiDropDown comboBox, int? classId)
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

    private static void ResetHeroTalentOptions(UiDropDown comboBox, int? classId, int? specId)
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

    private static int FindMatchOption(UiDropDown comboBox, int? value)
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

    private IReadOnlyList<(int Id, string Name)> GetAvailableClasses()
    {
        var classDirectory = Path.Combine(_resolveProfile().AddonRoot, "class");
        return ClassNames.GetClasses()
            .Where(item => File.Exists(Path.Combine(classDirectory,
                ClassNames.GetConfigFileName(item.Id) + ".lua")))
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

    private static void ResetPartyTypeOptions(UiDropDown comboBox)
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
