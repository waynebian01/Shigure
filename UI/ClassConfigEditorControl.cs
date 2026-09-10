using System.Drawing;
using System.Globalization;

namespace Shigure;

public sealed record ClassConfigPostSaveResult(
    string? AddonSyncIssue,
    int SavedModuleCount,
    IReadOnlyList<string> ModuleWarnings);

/// <summary>
/// 图形化编辑 Fuyutsui class/*.lua 的 ClassBlocks（states / auras / spells / items / group），
/// 并编辑同文件中的 spellsList 与 itemsList。
/// </summary>
public sealed class ClassConfigEditorControl : UserControl
{
    private readonly Func<string?> _resolveClassDirectory;
    private readonly Func<string, int, Task<ClassConfigPostSaveResult>> _updateConfigAsync;

    private readonly ListBox _classList = new();
    private readonly ListBox _specList = new();
    private readonly Label _pathLabel = new();
    private readonly Label _statusLabel = new();
    private readonly ToolTip _toolTip = new();
    private readonly Button _reloadButton = null!;
    private readonly Button _saveButton = null!;

    private readonly DataGridView _statesGrid = new();
    private readonly DataGridViewComboBoxColumn _stateNameColumn = new();
    private ToolStripDropDown? _stateComboDropDown;
    private readonly DataGridView _aurasGrid = new();
    private readonly DataGridView _spellsGrid = new();
    private readonly TextBox _spellsSearchBox = new();
    private readonly DataGridView _itemsGrid = new();
    private readonly TextBox _itemsSearchBox = new();
    private readonly DataGridView _itemsListGrid = new();
    private readonly TextBox _itemsListSearchBox = new();
    private readonly DataGridView _itemDatabaseGrid = new();
    private readonly TextBox _itemDatabaseFilterBox = new();
    private readonly Label _itemDatabaseStatusLabel = new();
    private readonly System.Windows.Forms.Timer _itemDatabaseFilterTimer = new() { Interval = 150 };
    private const int ItemDatabasePageSize = 20;
    private ItemDatabaseResultSet _itemDatabaseResults = ItemDatabaseResultSet.Empty;
    private int _itemDatabaseVisibleCount;
    private bool _expandingItemDatabaseRows;
    private CancellationTokenSource? _itemDatabaseFilterCancellation;
    private int _itemDatabaseFilterVersion;
    private readonly DataGridView _spellsListGrid = new();
    private readonly TextBox _spellsListSearchBox = new();
    private readonly DataGridView _spellDatabaseGrid = new();
    private readonly TextBox _spellDatabaseFilterBox = new();
    private readonly Label _spellDatabaseStatusLabel = new();
    private readonly System.Windows.Forms.Timer _spellDatabaseFilterTimer = new() { Interval = 150 };
    private const int SpellDatabasePageSize = 20;
    private SpellDatabaseResultSet _spellDatabaseResults = SpellDatabaseResultSet.Empty;
    private int _spellDatabaseVisibleCount;
    private bool _expandingSpellDatabaseRows;
    private CancellationTokenSource? _spellDatabaseFilterCancellation;
    private int _spellDatabaseFilterVersion;
    private readonly Label _groupPixelSummary = new() { AutoSize = true };
    private readonly CheckBox _groupEnabledBox = new();
    private readonly CheckBox _groupHasHealthBox = new();
    private readonly CheckBox _groupHasRoleBox = new();
    private readonly CheckBox _groupHasDispelBox = new();
    private readonly DataGridView _groupAurasGrid = new();
    private readonly CheckBox _nameplateHealthBox = new();
    private readonly CheckBox _nameplateRangeBox = new();
    private readonly Label _nameplatePixelSummary = new() { AutoSize = true };
    private readonly CheckBox _nameplateEnabledBox = new();
    private readonly DataGridView _nameplateAurasGrid = new();

    private string? _classDirectory;
    private readonly Dictionary<int, ClassBlocksStore.ClassFileDocument> _documents = new();
    private ClassBlocksStore.ClassFileDocument? _currentDocument;
    private ClassBlocksStore.SpecBlocks? _currentSpec;
    private int? _currentClassId;
    private int? _currentSpecId;
    private bool _suppressUi;
    private bool _dirty;
    private int _editorTabIndex = -1;

    internal event Action<bool>? DirtyStateChanged;
    internal bool HasUnsavedChanges => _dirty;
    private string _selectedStateCategory = ClassStateCatalog.CategoryState;
    private string _lastStateCategory = ClassStateCatalog.CategoryState;
    private string _lastAuraBucket = "player";

    private static readonly string[] FixedStateNames = ["锚点", "职业", "专精"];

    private static readonly (string Key, string Text)[] AuraBuckets =
    [
        ("player", "玩家"),
        ("target.harmful", "目标·敌对"),
        ("target.helpful", "目标·友善"),
        ("focus.harmful", "焦点·敌对"),
        ("focus.helpful", "焦点·友善")
    ];

    public ClassConfigEditorControl(
        Func<string?> resolveClassDirectory,
        Func<string, int, Task<ClassConfigPostSaveResult>> updateConfigAsync)
    {
        _resolveClassDirectory = resolveClassDirectory;
        _updateConfigAsync = updateConfigAsync;
        _reloadButton = UiTheme.CreateButton("刷新", UiTheme.ButtonKind.Secondary);
        _saveButton = UiTheme.CreateButton("保存", UiTheme.ButtonKind.Primary);
        InitializeComponent();
        SpellIconCatalog.CatalogChanged += OnSpellIconCatalogChanged;
        ReloadFromAddon();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CloseStateComboDropDown();
            _spellDatabaseFilterTimer.Stop();
            _spellDatabaseFilterTimer.Dispose();
            _spellDatabaseFilterCancellation?.Cancel();
            _itemDatabaseFilterTimer.Stop();
            _itemDatabaseFilterTimer.Dispose();
            _itemDatabaseFilterCancellation?.Cancel();
            SpellIconCatalog.CatalogChanged -= OnSpellIconCatalogChanged;
        }

        base.Dispose(disposing);
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
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 184));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);

        root.Controls.Add(BuildSidebar(), 0, 0);
        root.Controls.Add(BuildSpecSidebar(), 1, 0);
        root.Controls.Add(BuildEditor(), 2, 0);
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

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private Control BuildSidebar()
    {
        var panel = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(UiTheme.CardPadding),
            Margin = new Padding(0, 0, UiTheme.PageGap, 0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        panel.Controls.Add(new Label
        {
            Text = "职业",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Text,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        }, 0, 0);

        _classList.Dock = DockStyle.Fill;
        UiTheme.StyleClassIconListBox(
            _classList,
            item => (item as ClassListItem)?.ClassId,
            iconSize: 40);
        _classList.BackColor = UiTheme.SurfaceRaised;
        _classList.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressUi)
            {
                return;
            }

            SelectClassFromList();
        };
        panel.Controls.Add(_classList, 0, 1);
        return panel;
    }

    private Control BuildSpecSidebar()
    {
        var panel = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(UiTheme.CardPadding),
            Margin = new Padding(0, 0, UiTheme.PageGap, 0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        panel.Controls.Add(new Label
        {
            Text = "专精",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Text,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        }, 0, 0);

        _specList.Dock = DockStyle.Fill;
        UiTheme.StyleSpecIconListBox(
            _specList,
            item => item is SpecOption spec ? (spec.ClassId, spec.Id) : null,
            iconSize: 40);
        _specList.BackColor = UiTheme.SurfaceRaised;
        _specList.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressUi)
            {
                return;
            }

            SelectSpec(_specList.SelectedItem as SpecOption);
        };
        panel.Controls.Add(_specList, 0, 1);
        return panel;
    }

    private static void StyleActionButton(Button button)
    {
        UiTheme.StyleActionButton(button);
        button.Margin = new Padding(0, 0, 0, 8);
    }

    private Control BuildEditor()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));

        root.Controls.Add(BuildSectionTabs(), 0, 0);

        var actionRow = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, UiTheme.PageGap, 0, 0),
            Padding = new Padding(UiTheme.CardPadding, 10, UiTheme.CardPadding, 10)
        };
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 228));

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        StyleActionButton(_reloadButton);
        StyleActionButton(_saveButton);
        _reloadButton.Margin = new Padding(0, 0, 8, 0);
        _saveButton.Margin = new Padding(0);
        _reloadButton.Click += (_, _) => ReloadFromAddon();
        _saveButton.Click += async (_, _) => await SaveAndUpdateAsync();
        _toolTip.SetToolTip(_reloadButton, "从项目 Fuyutsui 重新加载配置 (F5)");
        _toolTip.SetToolTip(_saveButton, "保存配置并同步游戏 (Ctrl+S)");
        actions.Controls.Add(_reloadButton);
        actions.Controls.Add(_saveButton);
        actionRow.Controls.Add(BuildFooterInfo(), 0, 0);
        actionRow.Controls.Add(actions, 1, 0);
        root.Controls.Add(actionRow, 0, 1);
        return root;
    }

    private Control BuildFooterInfo()
    {
        var info = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0)
        };
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        info.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        info.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        info.Controls.Add(CreateFieldCaption("状态"), 0, 0);
        ConfigureInfoLabel(_statusLabel, UiTheme.Muted);
        _statusLabel.Text = "点击刷新以加载项目 Fuyutsui\\class";
        _statusLabel.TextChanged += (_, _) => _toolTip.SetToolTip(_statusLabel, _statusLabel.Text);
        _toolTip.SetToolTip(_statusLabel, _statusLabel.Text);
        info.Controls.Add(_statusLabel, 1, 0);

        info.Controls.Add(CreateFieldCaption("路径"), 0, 1);
        ConfigureInfoLabel(_pathLabel, UiTheme.Text);
        _pathLabel.Text = "未加载";
        _pathLabel.TextChanged += (_, _) => _toolTip.SetToolTip(_pathLabel, _pathLabel.Text);
        _toolTip.SetToolTip(_pathLabel, _pathLabel.Text);
        info.Controls.Add(_pathLabel, 1, 1);
        return info;
    }

    private Control BuildSectionTabs()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, UiTheme.TabBarHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var tabBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 7,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(0)
        };
        for (var i = 0; i < 7; i++)
        {
            tabBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 7));
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
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        contentCard.Controls.Add(contentHost, 0, 0);

        var pages = new Control[]
        {
            BuildStatesPage(),
            BuildAurasPage(),
            BuildSpellsPage(),
            BuildGroupPage(),
            BuildNameplatesPage(),
            BuildSpellsListPage(),
            BuildItemsPage()
        };
        foreach (var page in pages)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            page.BackColor = UiTheme.SurfaceRaised;
            page.Padding = new Padding(UiTheme.CardPadding);
            contentHost.Controls.Add(page);
        }

        var tabs = new UiPillTab[7];
        void SelectTab(int index)
        {
            if (_editorTabIndex == index)
            {
                return;
            }

            if (!_suppressUi && _editorTabIndex == 2)
            {
                _spellsGrid.EndEdit();
                _itemsGrid.EndEdit();
                WriteBackSpells();
                WriteBackItems();
            }

            if (!_suppressUi && _editorTabIndex == 4)
            {
                _nameplateAurasGrid.EndEdit();
                WriteBackNameplates();
            }

            if (!_suppressUi && _editorTabIndex == 6)
            {
                _itemsListGrid.EndEdit();
                WriteBackItemsList();
            }

            _editorTabIndex = index;
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

        var titles = new[] { "状态", "光环", "冷却", "队伍", "姓名板", "技能列表", "物品列表" };
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

    private Control BuildStatesPage()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UiTheme.SurfaceRaised
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        panel.Controls.Add(BuildStateCategoryTabs(), 0, 0);

        ConfigureGrid(_statesGrid, "class-config-states");
        _statesGrid.EditMode = DataGridViewEditMode.EditProgrammatically;
        _stateNameColumn.Name = "Name";
        _stateNameColumn.HeaderText = "状态名";
        _stateNameColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _stateNameColumn.DisplayMember = nameof(ClassStateCatalog.StateOption.Display);
        _stateNameColumn.ValueMember = nameof(ClassStateCatalog.StateOption.Name);
        _stateNameColumn.FlatStyle = FlatStyle.Flat;
        _stateNameColumn.DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton;
        _statesGrid.Columns.Add(_stateNameColumn);
        _statesGrid.Columns.Add(CreateDeleteColumn());
        _statesGrid.CellPainting += (_, e) =>
        {
            if (e.RowIndex >= 0
                && e.ColumnIndex >= 0
                && _statesGrid.Columns[e.ColumnIndex].Name == "Name")
            {
                UiTheme.PaintDataGridViewComboBoxCell(_statesGrid, e);
            }
        };
        _statesGrid.CellClick += OnStatesGridCellClick;
        _statesGrid.KeyDown += OnStatesGridKeyDown;
        _statesGrid.CellContentClick += HandleDeleteClick;
        _statesGrid.CellValueChanged += (_, _) => MarkDirty();
        _statesGrid.UserAddedRow += (_, _) => MarkDirty();
        _statesGrid.DataError += (_, e) => e.ThrowException = false;
        _statesGrid.Disposed += (_, _) => CloseStateComboDropDown();
        panel.Controls.Add(_statesGrid, 0, 1);
        panel.Controls.Add(BuildMoveButtons(_statesGrid), 0, 2);
        return panel;
    }

    private Control BuildItemsPage()
    {
        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var leftColumn = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0, 0, 5, 0),
            Padding = new Padding(0)
        };
        leftColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        leftColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var searchCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(12, 8, 12, 8)
        };
        searchCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        searchCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        searchCard.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "搜索",
            ForeColor = UiTheme.Text,
            BackColor = Color.Transparent,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
            Padding = new Padding(8, 0, 0, 0)
        }, 0, 0);

        UiTheme.StyleTextBox(_itemsListSearchBox);
        _itemsListSearchBox.Dock = DockStyle.None;
        _itemsListSearchBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _itemsListSearchBox.Margin = new Padding(0);
        _itemsListSearchBox.Height = 30;
        _itemsListSearchBox.PlaceholderText = "itemId、索引或名称";
        _itemsListSearchBox.TextChanged += (_, _) => ApplyItemsListFilter();
        searchCard.Controls.Add(_itemsListSearchBox, 1, 0);
        leftColumn.Controls.Add(searchCard, 0, 0);

        var currentListCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        currentListCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        currentListCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        currentListCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var currentListHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(12, 0, 12, 0)
        };
        currentListHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        currentListHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var currentListTitle = CreateCardTitle("物品列表");
        currentListTitle.AutoSize = true;
        currentListTitle.Dock = DockStyle.None;
        currentListTitle.Anchor = AnchorStyles.Left;
        currentListHeader.Controls.Add(currentListTitle, 0, 0);
        var hint = CreateFieldCaption("来自当前职业 Lua 的 Fuyutsui.itemsList。");
        hint.TextAlign = ContentAlignment.MiddleRight;
        currentListHeader.Controls.Add(hint, 1, 0);
        currentListCard.Controls.Add(currentListHeader, 0, 0);

        ConfigureGrid(_itemsListGrid, "class-config-items-list");
        _itemsListGrid.AllowUserToAddRows = false;
        _itemsListGrid.CellContentClick += HandleItemsListDeleteClick;
        _itemsListGrid.CellValueChanged += (_, e) =>
        {
            MarkDirty();
            if (e.RowIndex >= 0 && e.RowIndex < _itemsListGrid.Rows.Count)
            {
                UpdateItemGridIcon(_itemsListGrid.Rows[e.RowIndex]);
            }
        };
        _itemsListGrid.DataError += (_, e) => e.ThrowException = false;
        _itemsListGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ItemId",
            HeaderText = "itemId",
            Width = 125,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _itemsListGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Index",
            HeaderText = "索引",
            Width = 72,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _itemsListGrid.Columns.Add(CreateSpellIconColumn());
        _itemsListGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name",
            HeaderText = "名称",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _itemsListGrid.Columns.Add(CreateDeleteColumn());
        currentListCard.Controls.Add(_itemsListGrid, 0, 1);
        leftColumn.Controls.Add(currentListCard, 0, 1);
        split.Controls.Add(leftColumn, 0, 0);

        var rightColumn = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(5, 0, 0, 0),
            Padding = new Padding(0)
        };
        rightColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        rightColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var filterCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(12, 8, 12, 8)
        };
        filterCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        filterCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filterCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        filterCard.Controls.Add(CreateCardTitle("筛选", 8), 0, 0);
        UiTheme.StyleTextBox(_itemDatabaseFilterBox);
        _itemDatabaseFilterBox.Dock = DockStyle.None;
        _itemDatabaseFilterBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _itemDatabaseFilterBox.Margin = new Padding(0);
        _itemDatabaseFilterBox.Height = 30;
        _itemDatabaseFilterBox.PlaceholderText = "itemId 或名称";
        _itemDatabaseFilterBox.TextChanged += (_, _) => ScheduleItemDatabaseFilter();
        filterCard.Controls.Add(_itemDatabaseFilterBox, 1, 0);
        rightColumn.Controls.Add(filterCard, 0, 0);

        var databaseListCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        databaseListCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        databaseListCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        databaseListCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var databaseHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(12, 0, 12, 0)
        };
        databaseHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        databaseHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        databaseHeader.Controls.Add(CreateCardTitle("物品数据库"), 0, 0);
        _itemDatabaseStatusLabel.Dock = DockStyle.Fill;
        _itemDatabaseStatusLabel.ForeColor = UiTheme.Muted;
        _itemDatabaseStatusLabel.BackColor = Color.Transparent;
        _itemDatabaseStatusLabel.TextAlign = ContentAlignment.MiddleRight;
        _itemDatabaseStatusLabel.Margin = new Padding(0);
        databaseHeader.Controls.Add(_itemDatabaseStatusLabel, 1, 0);
        databaseListCard.Controls.Add(databaseHeader, 0, 0);

        ConfigureGrid(_itemDatabaseGrid, "class-config-item-database");
        _itemDatabaseGrid.AllowUserToAddRows = false;
        _itemDatabaseGrid.ReadOnly = true;
        _itemDatabaseGrid.VirtualMode = true;
        _itemDatabaseGrid.EditMode = DataGridViewEditMode.EditProgrammatically;
        _itemDatabaseGrid.RowCount = 0;
        _itemDatabaseGrid.CellValueNeeded += OnItemDatabaseCellValueNeeded;
        _itemDatabaseGrid.CellContentClick += OnItemDatabaseCellContentClick;
        _itemDatabaseGrid.Scroll += OnItemDatabaseScroll;
        _itemDatabaseGrid.Columns.Add(CreateSpellIconColumn());
        _itemDatabaseGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ItemId",
            HeaderText = "itemId",
            Width = 125,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _itemDatabaseGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name",
            HeaderText = "名称",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _itemDatabaseGrid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "Add",
            HeaderText = "添加",
            Text = "添加",
            UseColumnTextForButtonValue = true,
            Width = 72,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _itemDatabaseGrid.HandleCreated += (_, _) => RefreshItemDatabase();
        databaseListCard.Controls.Add(_itemDatabaseGrid, 0, 1);
        rightColumn.Controls.Add(databaseListCard, 0, 1);

        _itemDatabaseFilterTimer.Tick += async (_, _) =>
        {
            _itemDatabaseFilterTimer.Stop();
            await ApplyItemDatabaseFilterAsync();
        };

        split.Controls.Add(rightColumn, 1, 0);
        return split;
    }

    private Control BuildStateCategoryTabs()
    {
        var categories = ClassStateCatalog.TopCategories;
        var tabBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = categories.Length,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        foreach (var _ in categories)
        {
            tabBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / categories.Length));
        }

        var tabs = new UiPillTab[categories.Length];
        void ApplySelection()
        {
            for (var i = 0; i < tabs.Length; i++)
            {
                tabs[i].Selected = string.Equals(categories[i], _selectedStateCategory, StringComparison.Ordinal);
            }
        }

        void SelectCategory(string category)
        {
            if (_suppressUi
                || string.Equals(category, _selectedStateCategory, StringComparison.Ordinal))
            {
                return;
            }

            _statesGrid.EndEdit();
            WriteBackStatesCategory(_lastStateCategory);
            _selectedStateCategory = category;
            _lastStateCategory = category;
            ApplySelection();
            ReloadStatesGrid();
        }

        for (var i = 0; i < categories.Length; i++)
        {
            var category = categories[i];
            var tab = new UiPillTab(ClassStateCatalog.GetCategoryDisplayName(category));
            tab.Click += (_, _) => SelectCategory(category);
            tabs[i] = tab;
            tabBar.Controls.Add(tab, i, 0);
        }

        ApplySelection();
        return tabBar;
    }

    private Control BuildAurasPage()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UiTheme.SurfaceRaised
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        panel.Controls.Add(BuildAuraBucketTabs(), 0, 0);

        ConfigureGrid(_aurasGrid, "class-config-auras");
        _aurasGrid.Columns.Add(CreateSpellIconColumn());
        _aurasGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "名称", Width = 160 });
        _aurasGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SpellId", HeaderText = "spellId", Width = 110 });
        _aurasGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "SpellIds",
            HeaderText = "spellIds（逗号分隔）",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _aurasGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "MaxApps", HeaderText = "maxApps", Width = 135 });
        _aurasGrid.Columns.Add(CreateDeleteColumn());
        _aurasGrid.CellContentClick += HandleDeleteClick;
        _aurasGrid.CellValueChanged += (_, e) =>
        {
            MarkDirty();
            if (e.RowIndex >= 0 && e.RowIndex < _aurasGrid.Rows.Count
                && e.ColumnIndex >= 0
                && _aurasGrid.Columns[e.ColumnIndex].Name is "Name" or "SpellId" or "SpellIds")
            {
                UpdateAuraGridIcon(_aurasGrid.Rows[e.RowIndex]);
            }
        };
        _aurasGrid.UserAddedRow += (_, _) => MarkDirty();
        panel.Controls.Add(_aurasGrid, 0, 1);
        panel.Controls.Add(BuildMoveButtons(_aurasGrid), 0, 2);
        return panel;
    }

    private Control BuildAuraBucketTabs()
    {
        var tabBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = AuraBuckets.Length,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        foreach (var _ in AuraBuckets)
        {
            tabBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / AuraBuckets.Length));
        }

        var tabs = new UiPillTab[AuraBuckets.Length];
        void ApplySelection()
        {
            for (var i = 0; i < tabs.Length; i++)
            {
                tabs[i].Selected = string.Equals(AuraBuckets[i].Key, _lastAuraBucket, StringComparison.Ordinal);
            }
        }

        void SelectBucket(string key)
        {
            if (_suppressUi || string.Equals(key, _lastAuraBucket, StringComparison.Ordinal))
            {
                return;
            }

            _aurasGrid.EndEdit();
            WriteBackAuras(_lastAuraBucket);
            _lastAuraBucket = key;
            ApplySelection();
            _suppressUi = true;
            try
            {
                FillAurasGrid();
            }
            finally
            {
                _suppressUi = false;
            }
        }

        for (var i = 0; i < AuraBuckets.Length; i++)
        {
            var bucket = AuraBuckets[i];
            var tab = new UiPillTab(bucket.Text);
            tab.Click += (_, _) => SelectBucket(bucket.Key);
            tabs[i] = tab;
            tabBar.Controls.Add(tab, i, 0);
        }

        ApplySelection();
        return tabBar;
    }

    private Control BuildSpellsPage()
    {
        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var leftColumn = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0, 0, 5, 0),
            Padding = new Padding(0)
        };
        leftColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        leftColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        leftColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var spellSearchCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(12, 8, 12, 8)
        };
        spellSearchCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        spellSearchCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        spellSearchCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        spellSearchCard.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "搜索",
            ForeColor = UiTheme.Text,
            BackColor = Color.Transparent,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
            Padding = new Padding(8, 0, 0, 0)
        }, 0, 0);

        UiTheme.StyleTextBox(_spellsSearchBox);
        _spellsSearchBox.Dock = DockStyle.None;
        _spellsSearchBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _spellsSearchBox.Margin = new Padding(0);
        _spellsSearchBox.Height = 30;
        _spellsSearchBox.PlaceholderText = "spellId 或名称";
        _spellsSearchBox.TextChanged += (_, _) => ApplySpellsFilter();
        spellSearchCard.Controls.Add(_spellsSearchBox, 1, 0);
        leftColumn.Controls.Add(spellSearchCard, 0, 0);

        var spellCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        spellCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        spellCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var spellHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(12, 0, 12, 0)
        };
        spellHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        spellHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var spellTitle = CreateCardTitle("技能冷却");
        spellTitle.AutoSize = true;
        spellTitle.Dock = DockStyle.None;
        spellTitle.Anchor = AnchorStyles.Left;
        spellHeader.Controls.Add(spellTitle, 0, 0);
        var textureOrderHint = CreateFieldCaption(
            "充能法术连续占 2 格：冷却 → 充能冷却。");
        textureOrderHint.TextAlign = ContentAlignment.MiddleRight;
        spellHeader.Controls.Add(textureOrderHint, 1, 0);
        spellCard.Controls.Add(spellHeader, 0, 0);

        ConfigureGrid(_spellsGrid, "class-config-spells");
        _spellsGrid.Columns.Add(CreateSpellIconColumn());
        _spellsGrid.Columns.Add(CreateSpellTextColumn("Name", "名称", 24, 160));
        _spellsGrid.Columns.Add(CreateSpellTextColumn("SpellId", "法术 ID", 14, 120));
        _spellsGrid.Columns.Add(CreateSpellCheckColumn("Charge", "充能", 10, 80));
        _spellsGrid.Columns.Add(CreateSpellTextColumn("MaxCharge", "最大充能", 14, 110));
        _spellsGrid.Columns.Add(CreateSpellTextColumn("CastCount", "施法次数", 14, 110));
        _spellsGrid.Columns.Add(CreateSpellCheckColumn("ForcedKnown", "强制已学", 14, 110));
        _spellsGrid.Columns.Add(CreateSpellCheckColumn("InSpellBook", "法术书中", 14, 110));
        _spellsGrid.Columns.Add(CreateDeleteColumn());
        _spellsGrid.CellContentClick += HandleDeleteClick;
        _spellsGrid.CellValueChanged += (_, e) =>
        {
            MarkDirty();
            if (e.RowIndex >= 0 && e.RowIndex < _spellsGrid.Rows.Count)
            {
                UpdateSpellGridIcon(_spellsGrid.Rows[e.RowIndex]);
            }
        };
        _spellsGrid.UserAddedRow += (_, _) => MarkDirty();
        _spellsGrid.DataError += (_, e) => e.ThrowException = false;
        spellCard.Controls.Add(_spellsGrid, 0, 1);
        leftColumn.Controls.Add(spellCard, 0, 1);
        leftColumn.Controls.Add(BuildMoveButtons(_spellsGrid), 0, 2);
        split.Controls.Add(leftColumn, 0, 0);

        var rightColumn = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(5, 0, 0, 0),
            Padding = new Padding(0)
        };
        rightColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        rightColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var searchCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(12, 8, 12, 8)
        };
        searchCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        searchCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        searchCard.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "搜索",
            ForeColor = UiTheme.Text,
            BackColor = Color.Transparent,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
            Padding = new Padding(8, 0, 0, 0)
        }, 0, 0);

        UiTheme.StyleTextBox(_itemsSearchBox);
        _itemsSearchBox.Dock = DockStyle.None;
        _itemsSearchBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _itemsSearchBox.Margin = new Padding(0);
        _itemsSearchBox.Height = 30;
        _itemsSearchBox.PlaceholderText = "itemId 或名称";
        _itemsSearchBox.TextChanged += (_, _) => ApplyItemsFilter();
        searchCard.Controls.Add(_itemsSearchBox, 1, 0);
        rightColumn.Controls.Add(searchCard, 0, 0);

        var itemCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        itemCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        itemCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        itemCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var itemHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(12, 0, 12, 0)
        };
        itemHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        itemHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var itemTitle = CreateCardTitle("物品冷却");
        itemTitle.AutoSize = true;
        itemTitle.Dock = DockStyle.None;
        itemTitle.Anchor = AnchorStyles.Left;
        itemHeader.Controls.Add(itemTitle, 0, 0);
        var itemHint = CreateFieldCaption("名称可改为业务别名；图标始终按 itemId 匹配。");
        itemHint.TextAlign = ContentAlignment.MiddleRight;
        itemHeader.Controls.Add(itemHint, 1, 0);
        itemCard.Controls.Add(itemHeader, 0, 0);

        ConfigureGrid(_itemsGrid, "class-config-items");
        _itemsGrid.CellContentClick += HandleDeleteClick;
        _itemsGrid.CellValueChanged += (_, e) =>
        {
            MarkDirty();
            if (e.RowIndex >= 0 && e.RowIndex < _itemsGrid.Rows.Count)
            {
                UpdateItemGridIcon(_itemsGrid.Rows[e.RowIndex]);
            }
        };
        _itemsGrid.UserAddedRow += (_, _) => MarkDirty();
        _itemsGrid.DataError += (_, e) => e.ThrowException = false;
        _itemsGrid.Columns.Add(CreateSpellIconColumn());
        _itemsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ItemId",
            HeaderText = "itemId",
            Width = 125,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _itemsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name",
            HeaderText = "名称",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _itemsGrid.Columns.Add(CreateSpellCheckColumn("IsEquipped", "是否装备中", 12, 110));
        _itemsGrid.Columns.Add(CreateDeleteColumn());
        itemCard.Controls.Add(_itemsGrid, 0, 1);
        rightColumn.Controls.Add(itemCard, 0, 1);
        split.Controls.Add(rightColumn, 1, 0);
        return split;
    }

    private Control BuildSpellsListPage()
    {
        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var leftColumn = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0, 0, 5, 0),
            Padding = new Padding(0)
        };
        leftColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        leftColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var searchCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(12, 8, 12, 8)
        };
        searchCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        searchCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        searchCard.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "搜索",
            ForeColor = UiTheme.Text,
            BackColor = Color.Transparent,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
            Padding = new Padding(8, 0, 0, 0)
        }, 0, 0);

        UiTheme.StyleTextBox(_spellsListSearchBox);
        _spellsListSearchBox.Dock = DockStyle.None;
        _spellsListSearchBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _spellsListSearchBox.Margin = new Padding(0);
        _spellsListSearchBox.Height = 30;
        _spellsListSearchBox.PlaceholderText = "法术 ID、索引或名称";
        _spellsListSearchBox.TextChanged += (_, _) => ApplySpellsListFilter();
        searchCard.Controls.Add(_spellsListSearchBox, 1, 0);
        leftColumn.Controls.Add(searchCard, 0, 0);

        var currentListCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        currentListCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        currentListCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        currentListCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var currentListHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(12, 0, 12, 0)
        };
        currentListHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        currentListHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var currentListTitle = CreateCardTitle("技能列表");
        currentListTitle.AutoSize = true;
        currentListTitle.Dock = DockStyle.None;
        currentListTitle.Anchor = AnchorStyles.Left;
        currentListHeader.Controls.Add(currentListTitle, 0, 0);
        var hint = CreateFieldCaption("来自当前职业 Lua，仅编辑索引 1–100。");
        hint.TextAlign = ContentAlignment.MiddleRight;
        currentListHeader.Controls.Add(hint, 1, 0);
        currentListCard.Controls.Add(currentListHeader, 0, 0);

        ConfigureGrid(_spellsListGrid, "class-config-spells-list");
        _spellsListGrid.AllowUserToAddRows = false;
        _spellsListGrid.CellContentClick += HandleSpellsListDeleteClick;
        _spellsListGrid.CellValueChanged += (_, e) =>
        {
            MarkDirty();
            if (e.RowIndex >= 0 && e.RowIndex < _spellsListGrid.Rows.Count)
            {
                UpdateSpellGridIcon(_spellsListGrid.Rows[e.RowIndex]);
            }
        };
        _spellsListGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "SpellId",
            HeaderText = "法术 ID",
            Width = 125,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _spellsListGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Index",
            HeaderText = "索引",
            Width = 76,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _spellsListGrid.Columns.Add(CreateSpellIconColumn());
        _spellsListGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name",
            HeaderText = "名称",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _spellsListGrid.Columns.Add(CreateDeleteColumn());
        currentListCard.Controls.Add(_spellsListGrid, 0, 1);
        leftColumn.Controls.Add(currentListCard, 0, 1);
        split.Controls.Add(leftColumn, 0, 0);

        var rightColumn = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(5, 0, 0, 0),
            Padding = new Padding(0)
        };
        rightColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        rightColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var filterCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(12, 8, 12, 8)
        };
        filterCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        filterCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filterCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        filterCard.Controls.Add(CreateCardTitle("筛选", 8), 0, 0);
        UiTheme.StyleTextBox(_spellDatabaseFilterBox);
        _spellDatabaseFilterBox.Dock = DockStyle.None;
        _spellDatabaseFilterBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _spellDatabaseFilterBox.Margin = new Padding(0);
        _spellDatabaseFilterBox.Height = 30;
        _spellDatabaseFilterBox.PlaceholderText = "spellId 或名称";
        _spellDatabaseFilterBox.TextChanged += (_, _) => ScheduleSpellDatabaseFilter();
        filterCard.Controls.Add(_spellDatabaseFilterBox, 1, 0);
        rightColumn.Controls.Add(filterCard, 0, 0);

        var databaseListCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        databaseListCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        databaseListCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        databaseListCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var databaseHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(12, 0, 12, 0)
        };
        databaseHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        databaseHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        databaseHeader.Controls.Add(CreateCardTitle("技能列表"), 0, 0);
        _spellDatabaseStatusLabel.Dock = DockStyle.Fill;
        _spellDatabaseStatusLabel.ForeColor = UiTheme.Muted;
        _spellDatabaseStatusLabel.BackColor = Color.Transparent;
        _spellDatabaseStatusLabel.TextAlign = ContentAlignment.MiddleRight;
        _spellDatabaseStatusLabel.Margin = new Padding(0);
        databaseHeader.Controls.Add(_spellDatabaseStatusLabel, 1, 0);
        databaseListCard.Controls.Add(databaseHeader, 0, 0);

        ConfigureGrid(_spellDatabaseGrid, "class-config-spell-database");
        _spellDatabaseGrid.AllowUserToAddRows = false;
        _spellDatabaseGrid.ReadOnly = true;
        _spellDatabaseGrid.VirtualMode = true;
        _spellDatabaseGrid.EditMode = DataGridViewEditMode.EditProgrammatically;
        _spellDatabaseGrid.RowCount = 0;
        _spellDatabaseGrid.CellValueNeeded += OnSpellDatabaseCellValueNeeded;
        _spellDatabaseGrid.CellContentClick += OnSpellDatabaseCellContentClick;
        _spellDatabaseGrid.Scroll += OnSpellDatabaseScroll;
        _spellDatabaseGrid.Columns.Add(CreateSpellIconColumn());
        _spellDatabaseGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "SpellId",
            HeaderText = "spellId",
            Width = 125,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _spellDatabaseGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name",
            HeaderText = "名称",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _spellDatabaseGrid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "Add",
            HeaderText = "添加",
            Text = "添加",
            UseColumnTextForButtonValue = true,
            Width = 72,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _spellDatabaseGrid.HandleCreated += (_, _) => RefreshSpellDatabase();
        databaseListCard.Controls.Add(_spellDatabaseGrid, 0, 1);
        rightColumn.Controls.Add(databaseListCard, 0, 1);

        _spellDatabaseFilterTimer.Tick += async (_, _) =>
        {
            _spellDatabaseFilterTimer.Stop();
            await ApplySpellDatabaseFilterAsync();
        };

        split.Controls.Add(rightColumn, 1, 0);
        return split;
    }

    private Label CreateCardTitle(string text, int leftPadding = 0)
        => new()
        {
            Dock = DockStyle.Fill,
            Text = text,
            ForeColor = UiTheme.Text,
            BackColor = Color.Transparent,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
            Padding = new Padding(leftPadding, 0, 0, 0)
        };

    private Control BuildGroupPage()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UiTheme.SurfaceRaised
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var fields = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            BackColor = UiTheme.SurfaceRaised,
            Padding = new Padding(4, 6, 4, 6),
            Margin = new Padding(0)
        };
        _groupEnabledBox.Text = "启用";
        _groupEnabledBox.ForeColor = UiTheme.Text;
        _groupEnabledBox.AutoSize = true;
        _groupEnabledBox.CheckedChanged += (_, _) =>
        {
            if (!_suppressUi)
            {
                MarkDirty();
                UpdateGroupEditorsEnabled();
            }
        };
        foreach (var box in new[] { _groupHasHealthBox, _groupHasRoleBox, _groupHasDispelBox })
        {
            box.Text = "启用";
            box.AutoSize = true;
            box.ForeColor = UiTheme.Text;
            box.CheckedChanged += (_, _) => { MarkDirty(); UpdateGroupPixelSummary(); };
        }
        _groupPixelSummary.ForeColor = UiTheme.Text;
        var groupCards = new Control[]
        {
            CreateGroupCard("GROUP", _groupEnabledBox),
            CreateGroupCard("生命值", _groupHasHealthBox),
            CreateGroupCard("职责", _groupHasRoleBox),
            CreateGroupCard("驱散", _groupHasDispelBox),
            CreateGroupCard("自动分配", _groupPixelSummary)
        };
        foreach (var card in groupCards)
        {
            fields.Controls.Add(card);
        }

        void FitGroupCards()
        {
            const int minimumCardWidth = 176;
            var totalMargins = groupCards.Sum(card => card.Margin.Horizontal);
            var availableWidth = Math.Max(0, fields.ClientSize.Width - fields.Padding.Horizontal - totalMargins);
            var cardWidth = Math.Max(minimumCardWidth, availableWidth / groupCards.Length);
            foreach (var card in groupCards)
            {
                card.Width = cardWidth;
            }
        }

        fields.SizeChanged += (_, _) => FitGroupCards();
        fields.HandleCreated += (_, _) => FitGroupCards();
        panel.Controls.Add(fields, 0, 0);

        ConfigureGrid(_groupAurasGrid, "class-config-group-auras");
        _groupAurasGrid.Columns.Add(CreateSpellIconColumn());
        _groupAurasGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "名称", Width = 160 });
        _groupAurasGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SpellId", HeaderText = "spellId", Width = 110 });
        _groupAurasGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "SpellIds",
            HeaderText = "spellIds（逗号分隔）",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _groupAurasGrid.Columns.Add(CreateDeleteColumn());
        _groupAurasGrid.CellContentClick += HandleDeleteClick;
        _groupAurasGrid.CellValueChanged += (_, e) =>
        {
            MarkDirty();
            UpdateGroupPixelSummary();
            if (e.RowIndex >= 0 && e.RowIndex < _groupAurasGrid.Rows.Count
                && e.ColumnIndex >= 0
                && _groupAurasGrid.Columns[e.ColumnIndex].Name is "Name" or "SpellId" or "SpellIds")
            {
                UpdateAuraGridIcon(_groupAurasGrid.Rows[e.RowIndex]);
            }
        };
        _groupAurasGrid.UserAddedRow += (_, _) => { MarkDirty(); UpdateGroupPixelSummary(); };
        _groupAurasGrid.RowsRemoved += (_, _) => UpdateGroupPixelSummary();
        panel.Controls.Add(_groupAurasGrid, 0, 1);
        panel.Controls.Add(BuildMoveButtons(_groupAurasGrid), 0, 2);
        return panel;
    }

    private Control BuildNameplatesPage()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UiTheme.SurfaceRaised
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var fields = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            BackColor = UiTheme.SurfaceRaised,
            Padding = new Padding(4, 6, 4, 6),
            Margin = new Padding(0)
        };

        _nameplateEnabledBox.Text = "启用";
        _nameplateEnabledBox.ForeColor = UiTheme.Text;
        _nameplateEnabledBox.AutoSize = true;
        _nameplateEnabledBox.CheckedChanged += (_, _) =>
        {
            if (!_suppressUi)
            {
                MarkDirty();
                UpdateNameplateEditorsEnabled();
            }
        };
        _nameplateHealthBox.Text = "生命值";
        _nameplateRangeBox.Text = "距离";
        foreach (var box in new[] { _nameplateHealthBox, _nameplateRangeBox })
        {
            box.AutoSize = true;
            box.ForeColor = UiTheme.Text;
            box.CheckedChanged += (_, _) =>
            {
                MarkDirty();
                UpdateNameplatePixelSummary();
            };
        }
        _nameplatePixelSummary.ForeColor = UiTheme.Text;

        fields.Controls.Add(CreateGroupCard("NAMEPLATES", _nameplateEnabledBox));
        fields.Controls.Add(CreateGroupCard("生命值", _nameplateHealthBox));
        fields.Controls.Add(CreateGroupCard("距离", _nameplateRangeBox));
        fields.Controls.Add(CreateGroupCard("主像素（队伍后）", _nameplatePixelSummary));
        panel.Controls.Add(fields, 0, 0);

        ConfigureGrid(_nameplateAurasGrid, "class-config-nameplates");
        _nameplateAurasGrid.Columns.Add(CreateSpellIconColumn());
        _nameplateAurasGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "名称", Width = 220 });
        _nameplateAurasGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SpellId", HeaderText = "spellId", Width = 120 });
        _nameplateAurasGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "SpellIds",
            HeaderText = "spellIds（逗号分隔）",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _nameplateAurasGrid.Columns.Add(CreateDeleteColumn());
        _nameplateAurasGrid.CellContentClick += HandleDeleteClick;
        _nameplateAurasGrid.CellValueChanged += (_, e) =>
        {
            MarkDirty();
            UpdateNameplatePixelSummary();
            if (e.RowIndex >= 0 && e.RowIndex < _nameplateAurasGrid.Rows.Count
                && e.ColumnIndex >= 0
                && _nameplateAurasGrid.Columns[e.ColumnIndex].Name is "Name" or "SpellId" or "SpellIds")
            {
                UpdateAuraGridIcon(_nameplateAurasGrid.Rows[e.RowIndex]);
            }
        };
        _nameplateAurasGrid.UserAddedRow += (_, _) => { MarkDirty(); UpdateNameplatePixelSummary(); };
        _nameplateAurasGrid.RowsRemoved += (_, _) => UpdateNameplatePixelSummary();
        panel.Controls.Add(_nameplateAurasGrid, 0, 1);
        panel.Controls.Add(BuildMoveButtons(_nameplateAurasGrid), 0, 2);
        return panel;
    }

    private void FillNameplateEditors()
    {
        _nameplateAurasGrid.Rows.Clear();
        if (_currentSpec?.Nameplates is { } nameplates)
        {
            _nameplateEnabledBox.Checked = true;
            _nameplateHealthBox.Checked = nameplates.HealthPercent is > 0;
            _nameplateRangeBox.Checked = nameplates.Range is > 0;
            foreach (var aura in nameplates.Auras)
            {
                var icon = GetAuraIcon(aura.SpellId, aura.SpellIds, aura.Name);
                _nameplateAurasGrid.Rows.Add(
                    icon!,
                    aura.Name,
                    aura.SpellId?.ToString(CultureInfo.InvariantCulture) ?? "",
                    string.Join(", ", aura.SpellIds),
                    "×");
            }
        }
        else
        {
            _nameplateEnabledBox.Checked = false;
            _nameplateHealthBox.Checked = true;
            _nameplateRangeBox.Checked = true;
        }

        UpdateNameplateEditorsEnabled();
    }

    private void UpdateNameplateEditorsEnabled()
    {
        var enabled = _nameplateEnabledBox.Checked;
        _nameplateHealthBox.Enabled = enabled;
        _nameplateRangeBox.Enabled = enabled;
        _nameplateAurasGrid.Enabled = enabled;
        _nameplateAurasGrid.ReadOnly = !enabled;
        UpdateNameplatePixelSummary();
    }

    private void UpdateNameplatePixelSummary()
    {
        var fields = (_nameplateHealthBox.Checked ? 1 : 0) + (_nameplateRangeBox.Checked ? 1 : 0);
        foreach (DataGridViewRow row in _nameplateAurasGrid.Rows)
        {
            if (!row.IsNewRow
                && ((long.TryParse(row.Cells["SpellId"].Value?.ToString(), out var id) && id > 0)
                    || ParseIdList(row.Cells["SpellIds"].Value?.ToString() ?? string.Empty).Any()))
            {
                fields++;
            }
        }
        _nameplatePixelSummary.Text = _nameplateEnabledBox.Checked
            ? $"20 × {fields} = {20 * fields} 格"
            : "未启用";
    }


    private Control CreateGroupCard(string title, Control content)
    {
        var card = new UiCardPanel
        {
            AutoSize = false,
            Size = new Size(160, 88),
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12, 10, 12, 10),
            Margin = new Padding(4, 0, 4, 0)
        };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            AutoSize = false,
            ForeColor = UiTheme.Muted,
            BackColor = Color.Transparent,
            Font = new Font(Font.FontFamily, 8F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = Padding.Empty
        }, 0, 0);

        if (content is CheckBox checkBox)
        {
            checkBox.Anchor = AnchorStyles.Left;
            checkBox.Margin = Padding.Empty;
        }
        else if (content is NumericUpDown numeric)
        {
            numeric.Dock = DockStyle.None;
            numeric.Anchor = AnchorStyles.Left;
            numeric.Margin = Padding.Empty;
        }
        else
        {
            content.Dock = DockStyle.Fill;
            content.Margin = Padding.Empty;
        }

        card.Controls.Add(content, 0, 1);
        return card;
    }

    private Control BuildMoveButtons(DataGridView grid)
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = UiTheme.SurfaceRaised
        };
        var up = UiTheme.CreateButton("▲", UiTheme.Field, UiTheme.Text);
        var down = UiTheme.CreateButton("▼", UiTheme.Field, UiTheme.Text);
        UiTheme.StyleActionButton(up, 48);
        UiTheme.StyleActionButton(down, 48);
        up.Margin = new Padding(0, 4, 8, 4);
        down.Margin = new Padding(0, 4, 0, 4);
        up.Click += (_, _) => MoveSelectedRow(grid, -1);
        down.Click += (_, _) => MoveSelectedRow(grid, 1);
        bar.Controls.Add(up);
        bar.Controls.Add(down);
        return bar;
    }

    private static void ConfigureGrid(DataGridView grid, string cacheKey)
    {
        UiTheme.StyleDataGridView(grid);
        UiTheme.CacheDataGridViewColumnWidths(grid, cacheKey);
        grid.AllowUserToAddRows = true;
        grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.EditMode = DataGridViewEditMode.EditOnEnter;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
    }

    private static DataGridViewButtonColumn CreateDeleteColumn()
        => new()
        {
            Name = "Delete",
            HeaderText = "",
            Text = "×",
            UseColumnTextForButtonValue = true,
            Width = 44
        };

    private static DataGridViewTextBoxColumn CreateSpellTextColumn(
        string name,
        string headerText,
        float fillWeight,
        int minimumWidth)
        => new()
        {
            Name = name,
            HeaderText = headerText,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = fillWeight,
            MinimumWidth = minimumWidth,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };

    private static DataGridViewImageColumn CreateSpellIconColumn()
        => new()
        {
            Name = "Icon",
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
                BackColor = UiTheme.SurfaceRaised
            }
        };

    private static DataGridViewCheckBoxColumn CreateSpellCheckColumn(
        string name,
        string headerText,
        float fillWeight,
        int minimumWidth)
        => new()
        {
            Name = name,
            HeaderText = headerText,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = fillWeight,
            MinimumWidth = minimumWidth,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            TrueValue = true,
            FalseValue = false,
            IndeterminateValue = false,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                NullValue = false
            }
        };

    private Label CreateMutedLabel(string text)
        => new()
        {
            Text = text,
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Margin = new Padding(0, 8, 8, 0),
            TextAlign = ContentAlignment.MiddleLeft
        };

    private static Label CreateFieldCaption(string text)
        => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoSize = false,
            ForeColor = UiTheme.Muted,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
            AutoEllipsis = true
        };

    private static void ConfigureInfoLabel(Label label, Color foreColor)
    {
        label.Dock = DockStyle.Fill;
        label.AutoSize = false;
        label.ForeColor = foreColor;
        label.BackColor = Color.Transparent;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.AutoEllipsis = true;
        label.Margin = new Padding(0);
    }

    public void ReloadFromAddon()
    {
        if (_dirty && !ConfirmDiscard())
        {
            return;
        }

        _classDirectory = _resolveClassDirectory();
        _documents.Clear();
        _currentDocument = null;
        _currentSpec = null;
        _currentClassId = null;
        _currentSpecId = null;
        SetDirty(false);

        _suppressUi = true;
        try
        {
            _classList.Items.Clear();
            ClearSpecList();
            ClearGrids();

            if (string.IsNullOrWhiteSpace(_classDirectory) || !Directory.Exists(_classDirectory))
            {
                _pathLabel.Text = "未找到 Fuyutsui\\class";
                _statusLabel.Text = "请确认程序目录中包含 Fuyutsui\\class 后点击刷新。";
                return;
            }

            _pathLabel.Text = _classDirectory;
            foreach (var (classId, className) in ClassNames.GetClasses())
            {
                var fileName = ClassNames.GetConfigFileName(classId);
                var path = Path.Combine(_classDirectory, $"{fileName}.lua");
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    var doc = ClassBlocksStore.Load(path);
                    _documents[classId] = doc;
                    RegisterDocumentSpellNames(doc);
                    _classList.Items.Add(new ClassListItem(classId, className, fileName, doc.IsModernFormat));
                }
                catch (Exception ex)
                {
                    _classList.Items.Add(new ClassListItem(classId, className, fileName, false, ex.Message));
                }
            }

            _statusLabel.Text = $"已加载 {_documents.Count} 个职业文件";
            if (_classList.Items.Count > 0)
            {
                _classList.SelectedIndex = 0;
            }
        }
        finally
        {
            _suppressUi = false;
        }

        SelectClassFromList();
    }

    private static void RegisterDocumentSpellNames(ClassBlocksStore.ClassFileDocument document)
    {
        foreach (var spec in document.Specs.Values)
        {
            foreach (var spell in spec.Spells)
            {
                SpellIconCatalog.Register(spell.SpellId, spell.Name);
            }

            IEnumerable<ClassBlocksStore.AuraEntry>[] auraLists =
            {
                spec.PlayerAuras,
                spec.TargetHarmfulAuras,
                spec.TargetHelpfulAuras,
                spec.FocusHarmfulAuras,
                spec.FocusHelpfulAuras
            };
            foreach (var auras in auraLists)
            {
                foreach (var aura in auras)
                {
                    if (aura.SpellId is { } spellId)
                    {
                        SpellIconCatalog.Register(spellId, aura.Name);
                    }

                    foreach (var candidate in aura.SpellIds)
                    {
                        SpellIconCatalog.Register(candidate, aura.Name);
                    }
                }
            }

            foreach (var item in spec.Items)
            {
                if (item.ItemId is { } itemId)
                {
                    SpellIconCatalog.RegisterItem(itemId, item.Name);
                }
            }

            if (spec.Group is not { } group)
            {
                continue;
            }

            foreach (var aura in group.Auras)
            {
                if (aura.SpellId is { } spellId)
                {
                    SpellIconCatalog.Register(spellId, aura.Name);
                }

                foreach (var candidate in aura.SpellIds)
                {
                    SpellIconCatalog.Register(candidate, aura.Name);
                }
            }
        }

        // spellsList / itemsList 是用户实际添加/保存时使用的名称，优先级高于专精规则中的同 ID 别名。
        foreach (var spell in document.SpellsList)
        {
            SpellIconCatalog.Register(spell.SpellId, spell.Name, overwriteIdName: true);
        }

        foreach (var item in document.ItemsList)
        {
            SpellIconCatalog.RegisterItem(item.ItemId, item.Name);
        }
    }

    private void SelectClassFromList()
    {
        if (_classList.SelectedItem is not ClassListItem item)
        {
            return;
        }

        if (_dirty && _currentClassId != item.ClassId && !ConfirmDiscard())
        {
            _suppressUi = true;
            try
            {
                SelectClassInList(_currentClassId);
            }
            finally
            {
                _suppressUi = false;
            }

            return;
        }

        var discarding = _dirty && _currentClassId != item.ClassId;
        if (_dirty && _currentClassId == item.ClassId)
        {
            return;
        }

        if (discarding && _currentClassId is { } previousClassId && _documents.ContainsKey(previousClassId))
        {
            try
            {
                _documents[previousClassId] = ClassBlocksStore.Load(_documents[previousClassId].FilePath);
            }
            catch
            {
                // 丢弃失败时保留内存副本，避免阻断切换。
            }
        }

        SetDirty(false);
        _currentClassId = item.ClassId;
        _documents.TryGetValue(item.ClassId, out _currentDocument);
        _pathLabel.Text = _currentDocument?.FilePath ?? Path.Combine(_classDirectory ?? "", $"{item.FileName}.lua");

        if (_currentDocument is null)
        {
            _statusLabel.Text = string.IsNullOrWhiteSpace(item.Error)
                ? "无法加载该职业文件"
                : item.Error!;
            _currentSpec = null;
            _currentSpecId = null;
            _suppressUi = true;
            try
            {
                ClearSpecList();
                ClearGrids();
            }
            finally
            {
                _suppressUi = false;
            }

            return;
        }

        if (!_currentDocument.IsModernFormat)
        {
            _statusLabel.Text = "此文件仍是旧版稀疏索引格式，请先迁移到 states/auras/spells/items/group 后再编辑。";
        }
        else
        {
            _statusLabel.Text = _dirty ? "已修改（未保存）" : "可编辑";
        }

        _suppressUi = true;
        try
        {
            var options = new List<SpecOption>();
            foreach (var spec in ClassNames.GetSpecs(item.ClassId))
            {
                if (_currentDocument.Specs.ContainsKey(spec.Id))
                {
                    options.Add(new SpecOption(item.ClassId, spec.Id, spec.Name));
                }
            }

            // 也显示文件中有但 ClassNames 未登记的专精。
            foreach (var specId in _currentDocument.Specs.Keys.OrderBy(x => x))
            {
                if (options.Any(x => x.Id == specId))
                {
                    continue;
                }

                options.Add(new SpecOption(item.ClassId, specId, $"专精{specId}"));
            }

            RebuildSpecList(options);
            _currentSpecId = null;
            _currentSpec = null;
        }
        finally
        {
            _suppressUi = false;
        }

        SelectSpec(_specList.SelectedItem as SpecOption);
        _suppressUi = true;
        try
        {
            FillSpellsListGrid();
            FillItemsListGrid();
        }
        finally
        {
            _suppressUi = false;
        }
    }

    private void RebuildSpecList(IReadOnlyList<SpecOption> options)
    {
        _specList.Items.Clear();
        foreach (var option in options)
        {
            _specList.Items.Add(option);
        }

        if (_specList.Items.Count > 0)
        {
            _specList.SelectedIndex = 0;
        }
    }

    private void ClearSpecList()
    {
        _specList.Items.Clear();
    }

    private void SelectSpec(SpecOption? spec)
    {
        if (_currentDocument is null || spec is null)
        {
            _currentSpec = null;
            _currentSpecId = null;
            _specList.Invalidate();
            ClearGrids();
            return;
        }

        if (_dirty && _currentSpecId is not null && _currentSpecId != spec.Id)
        {
            CommitCurrentSpecFromUi();
        }

        _currentSpecId = spec.Id;
        if (!_currentDocument.Specs.TryGetValue(spec.Id, out var blocks))
        {
            blocks = new ClassBlocksStore.SpecBlocks();
            _currentDocument.Specs[spec.Id] = blocks;
        }

        _currentSpec = blocks;
        _specList.Invalidate();
        FillAllEditors();
    }

    private void FillAllEditors()
    {
        _suppressUi = true;
        try
        {
            _lastStateCategory = _selectedStateCategory;
            FillStatesGrid();
            FillAurasGrid();
            FillSpellsGrid();
            FillItemsGrid();
            FillGroupEditors();
            FillNameplateEditors();
        }
        finally
        {
            _suppressUi = false;
        }
    }

    private void FillStatesGrid()
    {
        CloseStateComboDropDown();
        _statesGrid.Rows.Clear();
        if (_currentSpec is null)
        {
            return;
        }

        var category = _selectedStateCategory;
        BindStateNameColumn(ClassStateCatalog.GetAllOptions(category));
        var storageCategory = ClassStateCatalog.GetStorageCategory(category);
        IEnumerable<string> names = _currentSpec.NestedStates
            ? _currentSpec.CategorizedStates.GetValueOrDefault(storageCategory) ?? []
            : _currentSpec.FlatStates;
        names = names.Where(name =>
            ClassStateCatalog.IsInCategory(name, category)
            && !IsHiddenStateName(name));

        foreach (var name in names)
        {
            EnsureStateOptionAvailable(category, name);
            _statesGrid.Rows.Add(name, "×");
        }
    }

    private void FillItemsGrid()
    {
        _itemsGrid.Rows.Clear();
        _itemsSearchBox.Clear();
        if (_currentSpec is null)
        {
            return;
        }

        foreach (var item in _currentSpec.Items.OrderBy(item => item.ItemId ?? long.MaxValue))
        {
            if (item.ItemId is { } itemId)
            {
                SpellIconCatalog.RegisterItem(itemId, item.Name);
            }

            var rowIndex = _itemsGrid.Rows.Add(
                (item.ItemId is { } id ? SpellIconCatalog.GetItem(id) : null)!,
                item.ItemId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                item.Name,
                item.IsEquipped,
                "×");
            _itemsGrid.Rows[rowIndex].Tag = item;
        }

        ApplyItemsFilter();
    }

    private static void UpdateItemGridIcon(DataGridViewRow row)
    {
        if (row.IsNewRow)
        {
            row.Cells["Icon"].Value = null;
            return;
        }

        var name = row.Cells["Name"].Value?.ToString();
        Image? icon = null;
        if (long.TryParse(
                row.Cells["ItemId"].Value?.ToString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var itemId)
            && itemId > 0)
        {
            SpellIconCatalog.RegisterItem(itemId, name);
            icon = SpellIconCatalog.GetItem(itemId);
        }

        row.Cells["Icon"].Value = icon;
    }

    private void ApplySpellsFilter()
    {
        var query = _spellsSearchBox.Text.Trim();
        _spellsGrid.ClearSelection();
        _spellsGrid.CurrentCell = null;

        foreach (DataGridViewRow row in _spellsGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            row.Visible = string.IsNullOrEmpty(query) || SpellsRowMatches(row, query);
        }
    }

    private static bool SpellsRowMatches(DataGridViewRow row, string query)
        => new[] { "SpellId", "Name" }
            .Select(columnName => row.Cells[columnName].Value?.ToString() ?? string.Empty)
            .Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase));

    private void ApplyItemsFilter()
    {
        var query = _itemsSearchBox.Text.Trim();
        _itemsGrid.ClearSelection();
        _itemsGrid.CurrentCell = null;

        foreach (DataGridViewRow row in _itemsGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            row.Visible = string.IsNullOrEmpty(query) || ItemsRowMatches(row, query);
        }
    }

    private static bool ItemsRowMatches(DataGridViewRow row, string query)
        => new[] { "ItemId", "Name" }
            .Select(columnName => row.Cells[columnName].Value?.ToString() ?? string.Empty)
            .Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase));

    private void ScheduleItemDatabaseFilter()
    {
        _itemDatabaseFilterTimer.Stop();
        _itemDatabaseFilterVersion++;
        CancelItemDatabaseFilter();
        if (!_itemDatabaseGrid.IsHandleCreated || IsDisposed || Disposing)
        {
            return;
        }

        if (!SpellIconCatalog.IsItemDatabaseAvailable)
        {
            ApplyItemDatabaseResults(ItemDatabaseResultSet.Empty, "未安装物品数据库");
            return;
        }

        _itemDatabaseStatusLabel.Text = "正在筛选…";
        _itemDatabaseFilterTimer.Start();
    }

    private void RefreshItemDatabase()
    {
        if (IsDisposed || Disposing || !_itemDatabaseGrid.IsHandleCreated)
        {
            return;
        }

        _itemDatabaseFilterTimer.Stop();
        _itemDatabaseFilterVersion++;
        CancelItemDatabaseFilter();
        var packageAvailable = SpellIconCatalog.IsItemDatabaseAvailable;
        _itemDatabaseFilterBox.Enabled = packageAvailable;
        if (!packageAvailable)
        {
            ApplyItemDatabaseResults(ItemDatabaseResultSet.Empty, "未安装物品数据库");
            return;
        }

        if (string.IsNullOrWhiteSpace(_itemDatabaseFilterBox.Text))
        {
            var snapshot = SpellIconCatalog.GetItemSuggestionsSnapshot();
            ApplyItemDatabaseResults(ItemDatabaseResultSet.FromAll(snapshot));
            return;
        }

        _itemDatabaseStatusLabel.Text = "正在筛选…";
        _ = ApplyItemDatabaseFilterAsync();
    }

    private async Task ApplyItemDatabaseFilterAsync()
    {
        if (IsDisposed || Disposing || !_itemDatabaseGrid.IsHandleCreated)
        {
            return;
        }

        var version = _itemDatabaseFilterVersion;
        var query = _itemDatabaseFilterBox.Text.Trim();
        var snapshot = SpellIconCatalog.GetItemSuggestionsSnapshot();
        var registeredNames = SpellIconCatalog.GetRegisteredItemNamesSnapshot();
        if (string.IsNullOrEmpty(query))
        {
            ApplyItemDatabaseResults(ItemDatabaseResultSet.FromAll(snapshot));
            return;
        }

        var cancellation = new CancellationTokenSource();
        _itemDatabaseFilterCancellation = cancellation;
        try
        {
            var results = await Task.Run(
                () => FilterItemDatabase(snapshot, registeredNames, query, cancellation.Token),
                cancellation.Token);
            if (cancellation.IsCancellationRequested
                || version != _itemDatabaseFilterVersion
                || IsDisposed
                || Disposing)
            {
                return;
            }

            ApplyItemDatabaseResults(results);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_itemDatabaseFilterCancellation, cancellation))
            {
                _itemDatabaseFilterCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private static ItemDatabaseResultSet FilterItemDatabase(
        IReadOnlyList<ItemSuggestion> source,
        IReadOnlyDictionary<long, string> registeredNames,
        string query,
        CancellationToken cancellationToken)
    {
        var numeric = query.All(character => character is >= '0' and <= '9');
        if (numeric)
        {
            return FilterItemDatabaseByIdPrefix(source, query);
        }

        var indices = new List<int>();
        for (var index = 0; index < source.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var suggestion = source[index];
            var name = string.IsNullOrWhiteSpace(suggestion.Name)
                ? registeredNames.GetValueOrDefault(suggestion.ItemId) ?? string.Empty
                : suggestion.Name;
            if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                indices.Add(index);
            }
        }

        return ItemDatabaseResultSet.FromIndices(source, indices.ToArray());
    }

    private static ItemDatabaseResultSet FilterItemDatabaseByIdPrefix(
        IReadOnlyList<ItemSuggestion> source,
        string query)
    {
        if (source.Count == 0
            || query.Length > 19
            || query[0] == '0'
            || !long.TryParse(query, NumberStyles.None, CultureInfo.InvariantCulture, out var prefix)
            || prefix <= 0)
        {
            return ItemDatabaseResultSet.Empty;
        }

        var ranges = new List<ItemDatabaseRange>(19);
        long scale = 1;
        while (prefix <= long.MaxValue / scale)
        {
            var startItemId = prefix * scale;
            var intervalLength = scale - 1;
            var endItemId = intervalLength > long.MaxValue - startItemId
                ? long.MaxValue
                : startItemId + intervalLength;
            var startIndex = LowerBoundItemSuggestion(source, startItemId);
            var endIndex = UpperBoundItemSuggestion(source, endItemId);
            if (endIndex > startIndex)
            {
                ranges.Add(new ItemDatabaseRange(startIndex, endIndex - startIndex));
            }

            if (scale > long.MaxValue / 10)
            {
                break;
            }

            scale *= 10;
        }

        return ItemDatabaseResultSet.FromRanges(source, ranges.ToArray());
    }

    private static int LowerBoundItemSuggestion(IReadOnlyList<ItemSuggestion> source, long itemId)
    {
        var low = 0;
        var high = source.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (source[middle].ItemId < itemId)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static int UpperBoundItemSuggestion(IReadOnlyList<ItemSuggestion> source, long itemId)
    {
        var low = 0;
        var high = source.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (source[middle].ItemId <= itemId)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private void ApplyItemDatabaseResults(ItemDatabaseResultSet results, string? status = null)
    {
        _itemDatabaseGrid.ClearSelection();
        _itemDatabaseGrid.CurrentCell = null;
        _itemDatabaseResults = results;
        _itemDatabaseVisibleCount = Math.Min(ItemDatabasePageSize, results.Count);
        _itemDatabaseGrid.RowCount = _itemDatabaseVisibleCount;
        _itemDatabaseStatusLabel.Text = status ?? FormatItemDatabaseStatus();
        _itemDatabaseGrid.Invalidate();
    }

    private string FormatItemDatabaseStatus()
        => _itemDatabaseResults.Count == 0
            ? "匹配 0 个物品"
            : $"已显示 {_itemDatabaseVisibleCount:N0} / 共 {_itemDatabaseResults.Count:N0} 个物品";

    private void OnItemDatabaseScroll(object? sender, ScrollEventArgs e)
    {
        if (e.ScrollOrientation != ScrollOrientation.VerticalScroll
            || _expandingItemDatabaseRows
            || _itemDatabaseVisibleCount >= _itemDatabaseResults.Count
            || _itemDatabaseGrid.FirstDisplayedScrollingRowIndex < 0)
        {
            return;
        }

        var lastDisplayedRow = _itemDatabaseGrid.FirstDisplayedScrollingRowIndex
                               + _itemDatabaseGrid.DisplayedRowCount(includePartialRow: true);
        if (lastDisplayedRow < _itemDatabaseVisibleCount - 2)
        {
            return;
        }

        _expandingItemDatabaseRows = true;
        try
        {
            _itemDatabaseVisibleCount = Math.Min(
                _itemDatabaseVisibleCount + ItemDatabasePageSize,
                _itemDatabaseResults.Count);
            _itemDatabaseGrid.RowCount = _itemDatabaseVisibleCount;
            _itemDatabaseStatusLabel.Text = FormatItemDatabaseStatus();
        }
        finally
        {
            _expandingItemDatabaseRows = false;
        }
    }

    private void CancelItemDatabaseFilter()
    {
        var cancellation = _itemDatabaseFilterCancellation;
        _itemDatabaseFilterCancellation = null;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
    }

    private void OnItemDatabaseCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _itemDatabaseVisibleCount || e.ColumnIndex < 0)
        {
            return;
        }

        var suggestion = _itemDatabaseResults[e.RowIndex];
        var displayName = SpellIconCatalog.ResolveItemSuggestionName(suggestion.ItemId, suggestion.Name)
                          ?? string.Empty;
        e.Value = _itemDatabaseGrid.Columns[e.ColumnIndex].Name switch
        {
            "Icon" => SpellIconCatalog.GetItem(suggestion.ItemId),
            "ItemId" => suggestion.ItemId.ToString(CultureInfo.InvariantCulture),
            "Name" => displayName,
            "Add" => "添加",
            _ => null
        };
    }

    private void OnItemDatabaseCellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0
            || e.RowIndex >= _itemDatabaseVisibleCount
            || e.ColumnIndex < 0
            || _itemDatabaseGrid.Columns[e.ColumnIndex].Name != "Add")
        {
            return;
        }

        var suggestion = _itemDatabaseResults[e.RowIndex];
        var name = SpellIconCatalog.ResolveItemSuggestionName(suggestion.ItemId, suggestion.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(
                $"当前物品数据库缺少 itemId {suggestion.ItemId} 的名称，请更新技能/物品数据包后再添加。",
                "物品列表",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        AddItemsListFromDatabase(new ItemSuggestion(suggestion.ItemId, name));
    }

    private void AddItemsListFromDatabase(ItemSuggestion suggestion)
    {
        if (_currentDocument is null)
        {
            MessageBox.Show("请先选择一个职业文件。", "物品列表", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!_currentDocument.IsModernFormat)
        {
            MessageBox.Show("旧版稀疏索引格式暂不支持添加物品。", "物品列表", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _itemsListGrid.EndEdit();
        if (!TryValidateItemsList(out var validationError))
        {
            MessageBox.Show(validationError, "物品列表", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        WriteBackItemsList();
        if (_currentDocument.ItemsList.Any(item => item.ItemId == suggestion.ItemId))
        {
            MessageBox.Show(
                $"已有此物品：{suggestion.Name}（{suggestion.ItemId}）",
                "物品列表",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var usedIndices = _currentDocument.ItemsList
            .Select(item => item.Index)
            .Where(index => index >= 1)
            .ToHashSet();
        var nextIndex = 1;
        while (usedIndices.Contains(nextIndex))
        {
            nextIndex++;
        }

        var entry = new ClassBlocksStore.ItemsListEntry
        {
            ItemId = suggestion.ItemId,
            Index = nextIndex,
            Name = suggestion.Name
        };
        _currentDocument.ItemsList.Add(entry);
        SpellIconCatalog.RegisterItem(suggestion.ItemId, suggestion.Name);

        var rowIndex = _itemsListGrid.Rows.Add(
            suggestion.ItemId.ToString(CultureInfo.InvariantCulture),
            nextIndex.ToString(CultureInfo.InvariantCulture),
            SpellIconCatalog.GetItem(suggestion.ItemId)!,
            suggestion.Name);
        var row = _itemsListGrid.Rows[rowIndex];
        row.Tag = entry;
        _itemsListSearchBox.Clear();
        ApplyItemsListFilter();
        row.Selected = true;
        _itemsListGrid.CurrentCell = row.Cells["ItemId"];
        _itemsListGrid.FirstDisplayedScrollingRowIndex = rowIndex;
        MarkDirty();
    }

    private void ReloadStatesGrid()
    {
        CloseStateComboDropDown();
        _suppressUi = true;
        try
        {
            FillStatesGrid();
        }
        finally
        {
            _suppressUi = false;
        }
    }

    private void BindStateNameColumn(IReadOnlyList<ClassStateCatalog.StateOption> options)
    {
        _stateNameColumn.DataSource = null;
        _stateNameColumn.DataSource = options.ToList();
        _stateNameColumn.DisplayMember = nameof(ClassStateCatalog.StateOption.Display);
        _stateNameColumn.ValueMember = nameof(ClassStateCatalog.StateOption.Name);
    }

    private void EnsureStateOptionAvailable(string category, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (_stateNameColumn.DataSource is IEnumerable<ClassStateCatalog.StateOption> current
            && current.Any(o => string.Equals(o.Name, name, StringComparison.Ordinal)))
        {
            return;
        }

        var options = ClassStateCatalog.GetAllOptions(category).ToList();
        if (!options.Any(o => string.Equals(o.Name, name, StringComparison.Ordinal)))
        {
            var optionCategory = ClassStateCatalog.FindCategory(name) ?? "未识别";
            options.Add(new ClassStateCatalog.StateOption(optionCategory, name));
        }

        BindStateNameColumn(options);
    }

    private void OnStatesGridCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0
            || e.ColumnIndex < 0
            || _statesGrid.Columns[e.ColumnIndex].Name != "Name")
        {
            return;
        }

        ShowStateNameDropDown(e.RowIndex, e.ColumnIndex);
    }

    private void OnStatesGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (_statesGrid.CurrentCell is not DataGridViewComboBoxCell cell
            || cell.OwningColumn?.Name != "Name"
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
        ShowStateNameDropDown(cell.RowIndex, cell.ColumnIndex);
    }

    private void ShowStateNameDropDown(int rowIndex, int columnIndex)
    {
        CloseStateComboDropDown();
        if (rowIndex < 0
            || rowIndex >= _statesGrid.Rows.Count)
        {
            return;
        }

        var row = _statesGrid.Rows[rowIndex];
        if (row.Cells[columnIndex] is not DataGridViewComboBoxCell cell)
        {
            return;
        }

        _statesGrid.CurrentCell = cell;
        var category = _selectedStateCategory;
        var current = cell.Value?.ToString()?.Trim();
        var usedNames = GetUsedStateNames(category, rowIndex);
        var options = ClassStateCatalog.GetOptions(category)
            .Where(option => !usedNames.Contains(option.Name) && !IsHiddenStateName(option.Name))
            .ToList();
        if (!string.IsNullOrWhiteSpace(current)
            && !options.Any(o => string.Equals(o.Name, current, StringComparison.Ordinal)))
        {
            var optionCategory = ClassStateCatalog.FindCategory(current) ?? "未识别";
            options.Insert(0, new ClassStateCatalog.StateOption(optionCategory, current));
        }

        var popupOptions = options
            .Select(option => new UiDropDownOption(option.Name, option.Display))
            .ToList();
        var cellBounds = _statesGrid.GetCellDisplayRectangle(columnIndex, rowIndex, cutOverflow: true);
        ToolStripDropDown? dropDown = null;
        dropDown = UiDropDownPopup.Show(
            _statesGrid,
            cellBounds,
            popupOptions,
            current,
            selected =>
            {
                var selectedValue = selected.Value?.ToString() ?? string.Empty;
                if (row.IsNewRow)
                {
                    var addedRowIndex = _statesGrid.Rows.Add(selectedValue, "×");
                    var addedCell = _statesGrid.Rows[addedRowIndex].Cells[columnIndex];
                    _statesGrid.CurrentCell = addedCell;
                    _statesGrid.InvalidateCell(addedCell);
                }
                else
                {
                    cell.Value = selectedValue;
                    _statesGrid.InvalidateCell(cell);
                }

                MarkDirty();
            },
            closed: () =>
            {
                if (ReferenceEquals(_stateComboDropDown, dropDown))
                {
                    _stateComboDropDown = null;
                }
            });
        _stateComboDropDown = dropDown;
    }

    private void CloseStateComboDropDown()
    {
        var dropDown = _stateComboDropDown;
        _stateComboDropDown = null;
        dropDown?.Close(ToolStripDropDownCloseReason.AppClicked);
    }

    private HashSet<string> GetUsedStateNames(string category, int excludedRowIndex)
    {
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        if (_currentSpec is not null)
        {
            IEnumerable<string> storedNames;
            if (_currentSpec.NestedStates)
            {
                var storageCategory = ClassStateCatalog.GetStorageCategory(category);
                storedNames = _currentSpec.CategorizedStates.GetValueOrDefault(storageCategory) ?? [];
            }
            else
            {
                storedNames = _currentSpec.FlatStates;
            }

            foreach (var name in storedNames)
            {
                // 当前分类以表格中的未保存内容为准，其它分类仍以专精数据为准。
                if (!ClassStateCatalog.IsInCategory(name, category))
                {
                    usedNames.Add(name);
                }
            }
        }

        foreach (DataGridViewRow row in _statesGrid.Rows)
        {
            if (row.IsNewRow || row.Index == excludedRowIndex)
            {
                continue;
            }

            var name = row.Cells["Name"].Value?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                usedNames.Add(name);
            }
        }

        return usedNames;
    }

    private void FillAurasGrid()
    {
        _aurasGrid.Rows.Clear();
        if (_currentSpec is null)
        {
            return;
        }

        foreach (var aura in GetCurrentAuraList())
        {
            var icon = GetAuraIcon(aura.SpellId, aura.SpellIds, aura.Name);

            _aurasGrid.Rows.Add(
                icon!,
                aura.Name,
                aura.SpellId?.ToString(CultureInfo.InvariantCulture) ?? "",
                string.Join(", ", aura.SpellIds),
                aura.MaxApps?.ToString(CultureInfo.InvariantCulture) ?? "",
                "×");
        }
    }

    private void FillSpellsGrid()
    {
        _spellsGrid.Rows.Clear();
        _spellsSearchBox.Clear();
        if (_currentSpec is null)
        {
            return;
        }

        foreach (var spell in _currentSpec.Spells)
        {
            SpellIconCatalog.Register(spell.SpellId, spell.Name);
            _spellsGrid.Rows.Add(
                SpellIconCatalog.Get(spell.SpellId)!,
                spell.Name,
                spell.SpellId.ToString(CultureInfo.InvariantCulture),
                spell.Charge,
                spell.MaxCharge?.ToString(CultureInfo.InvariantCulture) ?? "",
                spell.CastCount?.ToString(CultureInfo.InvariantCulture) ?? "",
                spell.ForcedKnown,
                spell.InSpellBook,
                "×");
        }
    }

    private static void UpdateAuraGridIcon(DataGridViewRow row)
    {
        if (row.IsNewRow)
        {
            row.Cells["Icon"].Value = null;
            return;
        }

        var name = row.Cells["Name"].Value?.ToString();
        var spellId = long.TryParse(
            row.Cells["SpellId"].Value?.ToString(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsedSpellId)
            ? parsedSpellId
            : (long?)null;
        row.Cells["Icon"].Value = GetAuraIcon(
            spellId,
            ParseIdList(row.Cells["SpellIds"].Value?.ToString() ?? ""),
            name);
    }

    private static Image? GetAuraIcon(long? spellId, IEnumerable<long> spellIds, string? name)
    {
        if (spellId is > 0)
        {
            SpellIconCatalog.Register(spellId.Value, name);
            var icon = SpellIconCatalog.Get(spellId.Value);
            if (icon is not null)
            {
                return icon;
            }
        }

        foreach (var candidate in spellIds)
        {
            if (candidate <= 0 || candidate == spellId)
            {
                continue;
            }

            SpellIconCatalog.Register(candidate, name);
            var icon = SpellIconCatalog.Get(candidate);
            if (icon is not null)
            {
                return icon;
            }
        }

        return SpellIconCatalog.Get(name);
    }

    private static void UpdateSpellGridIcon(DataGridViewRow row)
    {
        if (row.IsNewRow)
        {
            row.Cells["Icon"].Value = null;
            return;
        }

        var name = row.Cells["Name"].Value?.ToString();
        var icon = long.TryParse(
            row.Cells["SpellId"].Value?.ToString(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var spellId)
            ? SpellIconCatalog.Get(spellId)
            : null;
        if (spellId > 0)
        {
            SpellIconCatalog.Register(spellId, name);
        }

        row.Cells["Icon"].Value = icon ?? SpellIconCatalog.Get(name);
    }

    private void FillSpellsListGrid()
    {
        _spellsListGrid.Rows.Clear();
        _spellsListSearchBox.Clear();
        if (_currentDocument is null)
        {
            return;
        }

        foreach (var spell in _currentDocument.SpellsList.Where(spell => spell.Index is >= 1 and <= 100))
        {
            SpellIconCatalog.Register(spell.SpellId, spell.Name);
            var rowIndex = _spellsListGrid.Rows.Add(
                spell.SpellId.ToString(CultureInfo.InvariantCulture),
                spell.Index.ToString(CultureInfo.InvariantCulture),
                (SpellIconCatalog.Get(spell.SpellId) ?? SpellIconCatalog.Get(spell.Name))!,
                spell.Name);
            _spellsListGrid.Rows[rowIndex].Tag = spell;
        }
    }

    private void FillItemsListGrid()
    {
        _itemsListGrid.Rows.Clear();
        _itemsListSearchBox.Clear();
        if (_currentDocument is null)
        {
            return;
        }

        foreach (var item in _currentDocument.ItemsList.OrderBy(entry => entry.Index).ThenBy(entry => entry.ItemId))
        {
            SpellIconCatalog.RegisterItem(item.ItemId, item.Name);
            var rowIndex = _itemsListGrid.Rows.Add(
                item.ItemId.ToString(CultureInfo.InvariantCulture),
                item.Index.ToString(CultureInfo.InvariantCulture),
                SpellIconCatalog.GetItem(item.ItemId)!,
                item.Name);
            _itemsListGrid.Rows[rowIndex].Tag = item;
        }
    }

    private void ApplyItemsListFilter()
    {
        var query = _itemsListSearchBox.Text.Trim();
        _itemsListGrid.ClearSelection();
        _itemsListGrid.CurrentCell = null;

        foreach (DataGridViewRow row in _itemsListGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            row.Visible = string.IsNullOrEmpty(query) || ItemsListRowMatches(row, query);
        }
    }

    private static bool ItemsListRowMatches(DataGridViewRow row, string query)
        => new[] { "ItemId", "Index", "Name" }
            .Select(columnName => row.Cells[columnName].Value?.ToString() ?? string.Empty)
            .Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase));

    private void ApplySpellsListFilter()
    {
        var query = _spellsListSearchBox.Text.Trim();
        _spellsListGrid.ClearSelection();
        _spellsListGrid.CurrentCell = null;

        foreach (DataGridViewRow row in _spellsListGrid.Rows)
        {
            row.Visible = string.IsNullOrEmpty(query)
                          || SpellsListRowMatches(row, query);
        }
    }

    private static bool SpellsListRowMatches(DataGridViewRow row, string query)
        => new[] { "SpellId", "Index", "Name" }
            .Select(columnName => row.Cells[columnName].Value?.ToString() ?? string.Empty)
            .Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase));

    private void ScheduleSpellDatabaseFilter()
    {
        _spellDatabaseFilterTimer.Stop();
        _spellDatabaseFilterVersion++;
        CancelSpellDatabaseFilter();
        if (!_spellDatabaseGrid.IsHandleCreated || IsDisposed || Disposing)
        {
            return;
        }

        if (!SpellIconCatalog.IsPackageAvailable)
        {
            ApplySpellDatabaseResults(SpellDatabaseResultSet.Empty, "未安装技能数据库");
            return;
        }

        _spellDatabaseStatusLabel.Text = "正在筛选…";
        _spellDatabaseFilterTimer.Start();
    }

    private void RefreshSpellDatabase()
    {
        if (IsDisposed || Disposing || !_spellDatabaseGrid.IsHandleCreated)
        {
            return;
        }

        _spellDatabaseFilterTimer.Stop();
        _spellDatabaseFilterVersion++;
        CancelSpellDatabaseFilter();
        var packageAvailable = SpellIconCatalog.IsPackageAvailable;
        _spellDatabaseFilterBox.Enabled = packageAvailable;
        if (!packageAvailable)
        {
            ApplySpellDatabaseResults(SpellDatabaseResultSet.Empty, "未安装技能数据库");
            return;
        }

        if (string.IsNullOrWhiteSpace(_spellDatabaseFilterBox.Text))
        {
            var snapshot = SpellIconCatalog.GetSuggestionsSnapshot();
            ApplySpellDatabaseResults(SpellDatabaseResultSet.FromAll(snapshot));
            return;
        }

        _spellDatabaseStatusLabel.Text = "正在筛选…";
        _ = ApplySpellDatabaseFilterAsync();
    }

    private async Task ApplySpellDatabaseFilterAsync()
    {
        if (IsDisposed || Disposing || !_spellDatabaseGrid.IsHandleCreated)
        {
            return;
        }

        var version = _spellDatabaseFilterVersion;
        var query = _spellDatabaseFilterBox.Text.Trim();
        var snapshot = SpellIconCatalog.GetSuggestionsSnapshot();
        var registeredNames = SpellIconCatalog.GetRegisteredSpellNamesSnapshot();
        if (string.IsNullOrEmpty(query))
        {
            ApplySpellDatabaseResults(SpellDatabaseResultSet.FromAll(snapshot));
            return;
        }

        var cancellation = new CancellationTokenSource();
        _spellDatabaseFilterCancellation = cancellation;
        try
        {
            var results = await Task.Run(
                () => FilterSpellDatabase(snapshot, registeredNames, query, cancellation.Token),
                cancellation.Token);
            if (cancellation.IsCancellationRequested
                || version != _spellDatabaseFilterVersion
                || IsDisposed
                || Disposing)
            {
                return;
            }

            ApplySpellDatabaseResults(results);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_spellDatabaseFilterCancellation, cancellation))
            {
                _spellDatabaseFilterCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private static SpellDatabaseResultSet FilterSpellDatabase(
        IReadOnlyList<SpellSuggestion> source,
        IReadOnlyDictionary<long, string> registeredNames,
        string query,
        CancellationToken cancellationToken)
    {
        var numeric = query.All(character => character is >= '0' and <= '9');
        if (numeric)
        {
            return FilterSpellDatabaseByIdPrefix(source, query);
        }

        var indices = new List<int>();
        for (var index = 0; index < source.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var suggestion = source[index];
            var name = string.IsNullOrWhiteSpace(suggestion.Name)
                ? registeredNames.GetValueOrDefault(suggestion.SpellId) ?? string.Empty
                : suggestion.Name;
            if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                indices.Add(index);
            }
        }

        return SpellDatabaseResultSet.FromIndices(source, indices.ToArray());
    }

    private static SpellDatabaseResultSet FilterSpellDatabaseByIdPrefix(
        IReadOnlyList<SpellSuggestion> source,
        string query)
    {
        if (source.Count == 0
            || query.Length > 19
            || query[0] == '0'
            || !long.TryParse(query, NumberStyles.None, CultureInfo.InvariantCulture, out var prefix)
            || prefix <= 0)
        {
            return SpellDatabaseResultSet.Empty;
        }

        var ranges = new List<SpellDatabaseRange>(19);
        long scale = 1;
        while (prefix <= long.MaxValue / scale)
        {
            var startSpellId = prefix * scale;
            var intervalLength = scale - 1;
            var endSpellId = intervalLength > long.MaxValue - startSpellId
                ? long.MaxValue
                : startSpellId + intervalLength;
            var startIndex = LowerBoundSpellSuggestion(source, startSpellId);
            var endIndex = UpperBoundSpellSuggestion(source, endSpellId);
            if (endIndex > startIndex)
            {
                ranges.Add(new SpellDatabaseRange(startIndex, endIndex - startIndex));
            }

            if (scale > long.MaxValue / 10)
            {
                break;
            }

            scale *= 10;
        }

        return SpellDatabaseResultSet.FromRanges(source, ranges.ToArray());
    }

    private static int LowerBoundSpellSuggestion(IReadOnlyList<SpellSuggestion> source, long spellId)
    {
        var low = 0;
        var high = source.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (source[middle].SpellId < spellId)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static int UpperBoundSpellSuggestion(IReadOnlyList<SpellSuggestion> source, long spellId)
    {
        var low = 0;
        var high = source.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (source[middle].SpellId <= spellId)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private void ApplySpellDatabaseResults(SpellDatabaseResultSet results, string? status = null)
    {
        _spellDatabaseGrid.ClearSelection();
        _spellDatabaseGrid.CurrentCell = null;
        _spellDatabaseResults = results;
        _spellDatabaseVisibleCount = Math.Min(SpellDatabasePageSize, results.Count);
        _spellDatabaseGrid.RowCount = _spellDatabaseVisibleCount;
        _spellDatabaseStatusLabel.Text = status ?? FormatSpellDatabaseStatus();
        _spellDatabaseGrid.Invalidate();
    }

    private string FormatSpellDatabaseStatus()
        => _spellDatabaseResults.Count == 0
            ? "匹配 0 个技能"
            : $"已显示 {_spellDatabaseVisibleCount:N0} / 共 {_spellDatabaseResults.Count:N0} 个技能";

    private void OnSpellDatabaseScroll(object? sender, ScrollEventArgs e)
    {
        if (e.ScrollOrientation != ScrollOrientation.VerticalScroll
            || _expandingSpellDatabaseRows
            || _spellDatabaseVisibleCount >= _spellDatabaseResults.Count
            || _spellDatabaseGrid.FirstDisplayedScrollingRowIndex < 0)
        {
            return;
        }

        var lastDisplayedRow = _spellDatabaseGrid.FirstDisplayedScrollingRowIndex
                               + _spellDatabaseGrid.DisplayedRowCount(includePartialRow: true);
        if (lastDisplayedRow < _spellDatabaseVisibleCount - 2)
        {
            return;
        }

        _expandingSpellDatabaseRows = true;
        try
        {
            _spellDatabaseVisibleCount = Math.Min(
                _spellDatabaseVisibleCount + SpellDatabasePageSize,
                _spellDatabaseResults.Count);
            _spellDatabaseGrid.RowCount = _spellDatabaseVisibleCount;
            _spellDatabaseStatusLabel.Text = FormatSpellDatabaseStatus();
        }
        finally
        {
            _expandingSpellDatabaseRows = false;
        }
    }

    private void CancelSpellDatabaseFilter()
    {
        var cancellation = _spellDatabaseFilterCancellation;
        _spellDatabaseFilterCancellation = null;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
    }

    private void OnSpellDatabaseCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _spellDatabaseVisibleCount || e.ColumnIndex < 0)
        {
            return;
        }

        var suggestion = _spellDatabaseResults[e.RowIndex];
        var displayName = SpellIconCatalog.ResolveSuggestionName(suggestion.SpellId, suggestion.Name) ?? string.Empty;
        e.Value = _spellDatabaseGrid.Columns[e.ColumnIndex].Name switch
        {
            "Icon" => SpellIconCatalog.Get(suggestion.SpellId),
            "SpellId" => suggestion.SpellId.ToString(CultureInfo.InvariantCulture),
            "Name" => displayName,
            "Add" => "添加",
            _ => null
        };
    }

    private void OnSpellDatabaseCellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0
            || e.RowIndex >= _spellDatabaseVisibleCount
            || e.ColumnIndex < 0
            || _spellDatabaseGrid.Columns[e.ColumnIndex].Name != "Add")
        {
            return;
        }

        var suggestion = _spellDatabaseResults[e.RowIndex];
        var name = SpellIconCatalog.ResolveSuggestionName(suggestion.SpellId, suggestion.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(
                $"当前技能数据库缺少 spellId {suggestion.SpellId} 的名称，请更新技能数据包后再添加。",
                "技能列表",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        AddSpellFromDatabase(new SpellSuggestion(suggestion.SpellId, name));
    }

    private void FillGroupEditors()
    {
        _groupAurasGrid.Rows.Clear();
        if (_currentSpec?.Group is { } group)
        {
            _groupEnabledBox.Checked = true;
            _groupHasHealthBox.Checked = group.State.Contains("healthPercent");
            _groupHasRoleBox.Checked = group.State.Contains("role");
            _groupHasDispelBox.Checked = group.State.Contains("dispel");
            foreach (var aura in group.Auras)
            {
                var icon = GetAuraIcon(aura.SpellId, aura.SpellIds, aura.Name);
                _groupAurasGrid.Rows.Add(
                    icon!,
                    aura.Name,
                    aura.SpellId?.ToString(CultureInfo.InvariantCulture) ?? "",
                    string.Join(", ", aura.SpellIds),
                    "×");
            }
        }
        else
        {
            _groupEnabledBox.Checked = false;
            _groupHasHealthBox.Checked = false;
            _groupHasRoleBox.Checked = false;
            _groupHasDispelBox.Checked = false;
        }

        UpdateGroupEditorsEnabled();
    }

    private void UpdateGroupEditorsEnabled()
    {
        var enabled = _groupEnabledBox.Checked;
        _groupHasHealthBox.Enabled = enabled;
        _groupHasRoleBox.Enabled = enabled;
        _groupHasDispelBox.Enabled = enabled;
        _groupAurasGrid.Enabled = enabled;
        _groupAurasGrid.ReadOnly = !enabled;
        UpdateGroupPixelSummary();
    }

    private void UpdateGroupPixelSummary()
    {
        var fields = (_groupHasHealthBox.Checked ? 1 : 0) + (_groupHasRoleBox.Checked ? 1 : 0)
            + (_groupHasDispelBox.Checked ? 1 : 0);
        foreach (DataGridViewRow row in _groupAurasGrid.Rows)
        {
            if (!row.IsNewRow
                && ((long.TryParse(row.Cells["SpellId"].Value?.ToString(), out var id) && id > 0)
                    || ParseIdList(row.Cells["SpellIds"].Value?.ToString() ?? string.Empty).Any()))
            {
                fields++;
            }
        }
        _groupPixelSummary.Text = _groupEnabledBox.Checked ? $"每人 {fields} 格，30 人共 {30 * fields} 格" : "未启用";
    }

    private List<ClassBlocksStore.AuraEntry> GetCurrentAuraList()
        => ResolveAuraList(_lastAuraBucket);

    private List<ClassBlocksStore.AuraEntry> ResolveAuraList(string key)
    {
        if (_currentSpec is null)
        {
            return [];
        }

        return key switch
        {
            "target.harmful" => _currentSpec.TargetHarmfulAuras,
            "target.helpful" => _currentSpec.TargetHelpfulAuras,
            "focus.harmful" => _currentSpec.FocusHarmfulAuras,
            "focus.helpful" => _currentSpec.FocusHelpfulAuras,
            _ => _currentSpec.PlayerAuras
        };
    }

    private void CommitCurrentSpecFromUi()
    {
        if (_currentSpec is null || _currentDocument is null || !_currentDocument.IsModernFormat)
        {
            return;
        }

        _itemsGrid.EndEdit();
        _spellsGrid.EndEdit();
        NormalizeFixedStateNames(_currentSpec);
        WriteBackStatesCategory(_lastStateCategory);

        WriteBackAuras(_lastAuraBucket);
        WriteBackSpells();
        WriteBackItems();
        WriteBackGroup();
        WriteBackNameplates();
    }

    private void WriteBackSpellsList()
    {
        if (_currentDocument is null)
        {
            return;
        }

        foreach (DataGridViewRow row in _spellsListGrid.Rows)
        {
            if (row.Tag is not ClassBlocksStore.SpellsListEntry entry)
            {
                continue;
            }

            entry.SpellId = long.Parse(
                row.Cells["SpellId"].Value?.ToString()?.Trim() ?? "",
                NumberStyles.None,
                CultureInfo.InvariantCulture);
            entry.Index = int.Parse(
                row.Cells["Index"].Value?.ToString()?.Trim() ?? "",
                NumberStyles.None,
                CultureInfo.InvariantCulture);
            entry.Name = row.Cells["Name"].Value?.ToString()?.Trim() ?? "";
        }
    }

    private void WriteBackItemsList()
    {
        if (_currentDocument is null)
        {
            return;
        }

        foreach (DataGridViewRow row in _itemsListGrid.Rows)
        {
            if (row.IsNewRow || row.Tag is not ClassBlocksStore.ItemsListEntry entry)
            {
                continue;
            }

            if (!long.TryParse(
                    row.Cells["ItemId"].Value?.ToString()?.Trim(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var itemId))
            {
                continue;
            }

            entry.ItemId = itemId;
            if (int.TryParse(
                    row.Cells["Index"].Value?.ToString()?.Trim(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var index))
            {
                entry.Index = index;
            }

            entry.Name = row.Cells["Name"].Value?.ToString()?.Trim() ?? "";
        }
    }

    private void AddSpellFromDatabase(SpellSuggestion suggestion)
    {
        if (_currentDocument is null)
        {
            MessageBox.Show("请先选择一个职业文件。", "技能列表", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!_currentDocument.IsModernFormat)
        {
            MessageBox.Show("旧版稀疏索引格式暂不支持添加技能。", "技能列表", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _spellsListGrid.EndEdit();
        if (!TryValidateSpellsList(out var validationError))
        {
            MessageBox.Show(validationError, "技能列表", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        WriteBackSpellsList();
        if (_currentDocument.SpellsList.Any(entry => entry.SpellId == suggestion.SpellId))
        {
            MessageBox.Show(
                $"已有此技能：{suggestion.Name}（{suggestion.SpellId}）",
                "技能列表",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var usedIndices = _currentDocument.SpellsList
            .Select(entry => entry.Index)
            .Where(index => index is >= 1 and <= 100)
            .ToHashSet();
        var nextIndex = Enumerable.Range(1, 100).FirstOrDefault(index => !usedIndices.Contains(index));
        if (nextIndex == 0)
        {
            MessageBox.Show("索引 1–100 已全部使用，无法继续添加技能。", "技能列表", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var entry = new ClassBlocksStore.SpellsListEntry
        {
            SpellId = suggestion.SpellId,
            Index = nextIndex,
            Name = suggestion.Name
        };
        _currentDocument.SpellsList.Add(entry);
        SpellIconCatalog.Register(suggestion.SpellId, suggestion.Name);

        var rowIndex = _spellsListGrid.Rows.Add(
            suggestion.SpellId.ToString(CultureInfo.InvariantCulture),
            nextIndex.ToString(CultureInfo.InvariantCulture),
            (SpellIconCatalog.Get(suggestion.SpellId) ?? SpellIconCatalog.Get(suggestion.Name))!,
            suggestion.Name);
        var row = _spellsListGrid.Rows[rowIndex];
        row.Tag = entry;
        _spellsListSearchBox.Clear();
        ApplySpellsListFilter();
        row.Selected = true;
        _spellsListGrid.CurrentCell = row.Cells["SpellId"];
        _spellsListGrid.FirstDisplayedScrollingRowIndex = rowIndex;

        MarkDirty();
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

        RefreshSpellDatabase();
        RefreshItemDatabase();
        foreach (var grid in new[] { _spellsGrid, _spellsListGrid })
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (!row.IsNewRow)
                {
                    UpdateSpellGridIcon(row);
                }
            }

            grid.Invalidate();
        }

        foreach (var grid in new[] { _itemsGrid, _itemsListGrid })
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (!row.IsNewRow)
                {
                    UpdateItemGridIcon(row);
                }
            }

            grid.Invalidate();
        }

        _itemDatabaseGrid.Invalidate();

        foreach (var grid in new[] { _aurasGrid, _groupAurasGrid })
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                UpdateAuraGridIcon(row);
            }

            grid.Invalidate();
        }
    }

    private bool TryValidateSpellsList(out string error)
    {
        error = string.Empty;
        if (_currentDocument is null)
        {
            return true;
        }

        var editedIds = new HashSet<long>();
        foreach (DataGridViewRow row in _spellsListGrid.Rows)
        {
            var rowNumber = row.Index + 1;
            if (!long.TryParse(row.Cells["SpellId"].Value?.ToString()?.Trim(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var spellId)
                || spellId <= 0)
            {
                error = $"技能列表第 {rowNumber} 行的法术 ID 必须是正整数。";
                return false;
            }

            if (!int.TryParse(row.Cells["Index"].Value?.ToString()?.Trim(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var index)
                || index is < 1 or > 100)
            {
                error = $"技能列表第 {rowNumber} 行的索引必须是 1–100 的整数。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(row.Cells["Name"].Value?.ToString()))
            {
                error = $"技能列表第 {rowNumber} 行的名称不能为空。";
                return false;
            }

            if (!editedIds.Add(spellId))
            {
                error = $"技能列表中的法术 ID {spellId} 重复。";
                return false;
            }
        }

        var hiddenIds = _currentDocument.SpellsList
            .Where(entry => entry.Index is < 1 or > 100)
            .Select(entry => entry.SpellId)
            .ToHashSet();
        var conflictId = editedIds.FirstOrDefault(hiddenIds.Contains);
        if (conflictId != 0)
        {
            error = $"法术 ID {conflictId} 已被技能列表中索引 101+ 的条目使用。";
            return false;
        }

        return true;
    }

    private bool TryValidateItemsList(out string error)
    {
        error = string.Empty;
        if (_currentDocument is null)
        {
            return true;
        }

        var itemIds = new HashSet<long>();
        var indices = new HashSet<int>();
        foreach (DataGridViewRow row in _itemsListGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var rowNumber = row.Index + 1;
            if (!long.TryParse(row.Cells["ItemId"].Value?.ToString()?.Trim(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var itemId)
                || itemId <= 0)
            {
                error = $"物品列表第 {rowNumber} 行 itemId 必须是正整数。";
                return false;
            }

            if (!int.TryParse(row.Cells["Index"].Value?.ToString()?.Trim(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var index)
                || index <= 0)
            {
                error = $"物品列表第 {rowNumber} 行的索引必须是正整数。";
                return false;
            }

            var name = row.Cells["Name"].Value?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                error = $"物品列表第 {rowNumber} 行名称不能为空。";
                return false;
            }

            if (!itemIds.Add(itemId))
            {
                error = $"物品列表 itemId {itemId} 重复。";
                return false;
            }

            if (!indices.Add(index))
            {
                error = $"物品列表索引 {index} 重复。";
                return false;
            }
        }

        return true;
    }

    private bool TryValidateNameplates(out string error)
    {
        error = string.Empty;
        if (_currentDocument is null)
        {
            return true;
        }

        foreach (var (specId, spec) in _currentDocument.Specs.OrderBy(pair => pair.Key))
        {
            if (spec.Nameplates is not { } nameplates)
            {
                continue;
            }

            if (nameplates.HealthPercent is not > 0 && nameplates.Range is not > 0 && nameplates.Auras.Count == 0)
            {
                error = $"专精 {specId} 的姓名板至少需要选择一个字段或光环。";
                return false;
            }

            for (var index = 0; index < nameplates.Auras.Count; index++)
            {
                var aura = nameplates.Auras[index];
                if (aura.SpellId is not > 0 && aura.SpellIds.Count == 0)
                {
                    error = $"专精 {specId} 的姓名板光环第 {index + 1} 行缺少有效 spellId。";
                    return false;
                }
            }
        }

        return true;
    }

    private bool TryValidateItems(out string error)
    {
        error = string.Empty;
        if (_currentDocument is null)
        {
            return true;
        }

        foreach (var (specId, spec) in _currentDocument.Specs.OrderBy(pair => pair.Key))
        {
            if (spec.Items.Count == 0)
            {
                continue;
            }

            var itemIds = new HashSet<long>();
            var itemNames = new HashSet<string>(StringComparer.Ordinal);
            var bareNames = new HashSet<string>(StringComparer.Ordinal);
            if (spec.NestedStates)
            {
                foreach (var category in new[]
                         {
                             ClassStateCatalog.CategoryState,
                             ClassStateCatalog.CategorySpecial,
                             ClassStateCatalog.CategoryResource,
                             ClassStateCatalog.CategoryConfig
                         })
                {
                    foreach (var name in spec.CategorizedStates.GetValueOrDefault(category) ?? [])
                    {
                        bareNames.Add(name);
                    }
                }
            }
            else
            {
                bareNames.UnionWith(spec.FlatStates);
            }

            for (var index = 0; index < spec.Items.Count; index++)
            {
                var item = spec.Items[index];
                var rowNumber = index + 1;
                if (item.ItemId is not { } itemId || itemId <= 0)
                {
                    error = $"专精 {specId} 的物品第 {rowNumber} 行 itemId 必须是正整数。";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(item.Name))
                {
                    error = $"专精 {specId} 的物品第 {rowNumber} 行名称不能为空。";
                    return false;
                }

                if (!itemIds.Add(itemId))
                {
                    error = $"专精 {specId} 的物品 itemId {itemId} 重复。";
                    return false;
                }

                if (!itemNames.Add(item.Name))
                {
                    error = $"专精 {specId} 的物品名称“{item.Name}”重复。";
                    return false;
                }

                if (bareNames.Contains(item.Name))
                {
                    error = $"专精 {specId} 的物品名称“{item.Name}”与状态、特殊、能量或配置开关字段重名。";
                    return false;
                }
            }
        }

        return true;
    }

    private void WriteBackStatesCategory(string category)
    {
        if (_currentSpec is null)
        {
            return;
        }

        var storageCategory = ClassStateCatalog.GetStorageCategory(category);
        List<string> list;
        if (_currentSpec.NestedStates)
        {
            if (!_currentSpec.CategorizedStates.TryGetValue(storageCategory, out list!))
            {
                list = new List<string>();
                _currentSpec.CategorizedStates[storageCategory] = list;
            }
        }
        else
        {
            list = _currentSpec.FlatStates;
        }

        var editedNames = new List<string>();
        foreach (DataGridViewRow row in _statesGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var name = row.Cells["Name"].Value?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                editedNames.Add(name);
            }
        }

        var insertIndex = list.FindIndex(name =>
            ClassStateCatalog.IsInCategory(name, category)
            && !IsHiddenStateName(name));
        if (insertIndex < 0)
        {
            var anchorIndex = string.Equals(category, ClassStateCatalog.CategoryState, StringComparison.Ordinal)
                ? list.FindLastIndex(IsHiddenStateName)
                : -1;
            insertIndex = anchorIndex >= 0 ? anchorIndex + 1 : list.Count;
        }

        list.RemoveAll(name =>
            ClassStateCatalog.IsInCategory(name, category)
            && !IsHiddenStateName(name));
        list.InsertRange(Math.Min(insertIndex, list.Count), editedNames);
    }

    private void WriteBackAuras(string bucketKey)
    {
        if (_currentSpec is null)
        {
            return;
        }

        var list = ResolveAuraList(bucketKey);
        list.Clear();
        foreach (DataGridViewRow row in _aurasGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var name = row.Cells["Name"].Value?.ToString()?.Trim() ?? "";
            var spellIdsText = row.Cells["SpellIds"].Value?.ToString()?.Trim() ?? "";
            var spellIdText = row.Cells["SpellId"].Value?.ToString()?.Trim() ?? "";
            var maxAppsText = row.Cells["MaxApps"].Value?.ToString()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(spellIdText) && string.IsNullOrWhiteSpace(spellIdsText))
            {
                continue;
            }

            var entry = new ClassBlocksStore.AuraEntry { Name = name };
            foreach (var id in ParseIdList(spellIdsText))
            {
                entry.SpellIds.Add(id);
            }

            if (long.TryParse(spellIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sid))
            {
                entry.SpellId = sid;
            }

            if (int.TryParse(maxAppsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxApps))
            {
                entry.MaxApps = maxApps;
            }

            list.Add(entry);
        }
    }

    private void WriteBackItems()
    {
        if (_currentSpec is null)
        {
            return;
        }

        _currentSpec.Items.Clear();
        foreach (DataGridViewRow row in _itemsGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var idText = row.Cells["ItemId"].Value?.ToString()?.Trim();
            var name = row.Cells["Name"].Value?.ToString()?.Trim() ?? string.Empty;
            long? itemId = long.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedId)
                ? parsedId
                : null;
            if (itemId is not null || name.Length > 0)
            {
                _currentSpec.Items.Add(new ClassBlocksStore.ItemEntry
                {
                    ItemId = itemId,
                    Name = name,
                    IsEquipped = row.Cells["IsEquipped"].Value is true
                });
            }
        }
    }

    private void WriteBackSpells()
    {
        if (_currentSpec is null)
        {
            return;
        }

        _currentSpec.Spells.Clear();
        foreach (DataGridViewRow row in _spellsGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var spellIdText = row.Cells["SpellId"].Value?.ToString()?.Trim() ?? "";
            if (!long.TryParse(spellIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var spellId))
            {
                continue;
            }

            var entry = new ClassBlocksStore.SpellEntry
            {
                SpellId = spellId,
                Name = row.Cells["Name"].Value?.ToString()?.Trim() ?? "",
                Charge = row.Cells["Charge"].Value is true,
                ForcedKnown = row.Cells["ForcedKnown"].Value is true,
                InSpellBook = row.Cells["InSpellBook"].Value is true
            };
            if (int.TryParse(row.Cells["MaxCharge"].Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxCharge))
            {
                entry.MaxCharge = maxCharge;
            }

            if (int.TryParse(row.Cells["CastCount"].Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var castCount))
            {
                entry.CastCount = castCount;
            }

            _currentSpec.Spells.Add(entry);
        }
    }

    private void WriteBackGroup()
    {
        if (_currentSpec is null)
        {
            return;
        }

        if (!_groupEnabledBox.Checked)
        {
            _currentSpec.Group = null;
            return;
        }

        var group = new ClassBlocksStore.GroupBlocks();
        var enabledFields = new Dictionary<string, bool>
        {
            ["healthPercent"] = _groupHasHealthBox.Checked,
            ["role"] = _groupHasRoleBox.Checked,
            ["dispel"] = _groupHasDispelBox.Checked
        };
        // 保留配置中的 state 顺序；新启用的字段追加到末尾。
        foreach (var field in (_currentSpec.Group?.State ?? []).Concat(GroupStateLayout.SupportedFields).Distinct())
        {
            if (enabledFields.TryGetValue(field, out var enabled) && enabled) group.State.Add(field);
        }

        foreach (DataGridViewRow row in _groupAurasGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var entry = new ClassBlocksStore.GroupAuraEntry
            {
                Name = row.Cells["Name"].Value?.ToString()?.Trim() ?? ""
            };
            var spellIdsText = row.Cells["SpellIds"].Value?.ToString()?.Trim() ?? "";
            foreach (var id in ParseIdList(spellIdsText))
            {
                entry.SpellIds.Add(id);
            }

            if (long.TryParse(row.Cells["SpellId"].Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sid))
            {
                entry.SpellId = sid;
            }

            if (entry.SpellId is > 0 || entry.SpellIds.Count > 0)
            {
                group.Auras.Add(entry);
            }
        }

        _currentSpec.Group = group;
    }

    private void WriteBackNameplates()
    {
        if (_currentSpec is null)
        {
            return;
        }

        if (!_nameplateEnabledBox.Checked)
        {
            _currentSpec.Nameplates = null;
            return;
        }

        var offset = 0;
        var nameplates = new ClassBlocksStore.NameplateBlocks
        {
            HealthPercent = _nameplateHealthBox.Checked ? ++offset : null,
            Range = _nameplateRangeBox.Checked ? ++offset : null
        };

        foreach (DataGridViewRow row in _nameplateAurasGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var name = row.Cells["Name"].Value?.ToString()?.Trim() ?? string.Empty;
            var spellIdsText = row.Cells["SpellIds"].Value?.ToString()?.Trim() ?? string.Empty;
            var entry = new ClassBlocksStore.AuraEntry { Name = name };
            foreach (var id in ParseIdList(spellIdsText))
            {
                entry.SpellIds.Add(id);
            }

            if (long.TryParse(row.Cells["SpellId"].Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var spellId)
                && spellId > 0)
            {
                entry.SpellId = spellId;
            }

            if (entry.SpellId is not > 0 && entry.SpellIds.Count == 0)
            {
                continue;
            }

            nameplates.Auras.Add(entry);
        }

        _currentSpec.Nameplates = nameplates;
    }

    private async Task SaveAndUpdateAsync()
    {
        if (_currentDocument is null || _currentClassId is null)
        {
            MessageBox.Show("请先选择一个职业文件。", "配置", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!_currentDocument.IsModernFormat)
        {
            MessageBox.Show("旧版稀疏索引格式暂不支持保存。", "配置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var localSaved = false;
        try
        {
            _spellsListGrid.EndEdit();
            _itemsListGrid.EndEdit();
            _statesGrid.EndEdit();
            _spellsGrid.EndEdit();
            _itemsGrid.EndEdit();
            if (!TryValidateSpellsList(out var validationError))
            {
                MessageBox.Show(validationError, "技能列表", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _statusLabel.Text = validationError;
                return;
            }

            if (!TryValidateItemsList(out validationError))
            {
                MessageBox.Show(validationError, "物品列表", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _statusLabel.Text = validationError;
                return;
            }

            // 切换分类前把当前状态表写回。
            CommitCurrentSpecFromUi();
            if (!TryValidateNameplates(out validationError))
            {
                MessageBox.Show(validationError, "姓名板", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _statusLabel.Text = validationError;
                return;
            }
            if (!TryValidateItems(out validationError))
            {
                MessageBox.Show(validationError, "物品冷却", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _statusLabel.Text = validationError;
                return;
            }

            WriteBackSpellsList();
            WriteBackItemsList();
            ClassBlocksStore.Save(_currentDocument);
            localSaved = true;
            SetDirty(false);
            _statusLabel.Text = "本地 Lua 已保存，正在更新配置并同步游戏…";
            var updateResult = await _updateConfigAsync(_currentDocument.FilePath, _currentClassId.Value);
            if (IsDisposed)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(updateResult.AddonSyncIssue))
            {
                _statusLabel.Text = "本地已保存并更新配置，但游戏同步失败";
                MessageBox.Show(
                    $"本地 Lua、该职业的 {updateResult.SavedModuleCount} 个模块及 config/keymap 已保存，"
                    + $"但游戏插件同步未完成：\n{updateResult.AddonSyncIssue}",
                    "游戏同步失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            _statusLabel.Text = $"已保存配置及该职业的 {updateResult.SavedModuleCount} 个模块";
            var warningText = updateResult.ModuleWarnings.Count == 0
                ? string.Empty
                : $"\n\n有 {updateResult.ModuleWarnings.Count} 个模块未携带依赖，详情见日志。";
            MessageBox.Show(
                $"已一并保存该职业的 {updateResult.SavedModuleCount} 个模块。{warningText}\n\n请在游戏内重载界面,  /reload",
                "保存成功",
                MessageBoxButtons.OK,
                updateResult.ModuleWarnings.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                var title = localSaved ? "保存后的更新失败" : "保存失败";
                var message = localSaved
                    ? $"本地 Lua 已保存，但后续更新失败：\n{ex.Message}"
                    : ex.Message;
                MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                _statusLabel.Text = localSaved
                    ? $"本地已保存，后续更新失败: {ex.Message}"
                    : $"保存失败: {ex.Message}";
            }
        }
    }

    private void MarkDirty()
    {
        if (_suppressUi)
        {
            return;
        }

        SetDirty(true);
        if (_statusLabel.Text != "已修改（未保存）")
        {
            _statusLabel.Text = "已修改（未保存）";
        }
    }

    private void SetDirty(bool dirty)
    {
        if (_dirty == dirty)
        {
            _saveButton.Enabled = dirty;
            return;
        }

        _dirty = dirty;
        _saveButton.Enabled = dirty;
        DirtyStateChanged?.Invoke(dirty);
    }

    private bool ConfirmDiscard()
        => MessageBox.Show("当前修改尚未保存，确定丢弃吗？", "配置", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
           == DialogResult.Yes;

    private void SelectClassInList(int? classId)
    {
        for (var i = 0; i < _classList.Items.Count; i++)
        {
            if (_classList.Items[i] is ClassListItem item && item.ClassId == classId)
            {
                _classList.SelectedIndex = i;
                return;
            }
        }
    }

    private void ClearGrids()
    {
        _statesGrid.Rows.Clear();
        _aurasGrid.Rows.Clear();
        _spellsGrid.Rows.Clear();
        _spellsSearchBox.Clear();
        _itemsGrid.Rows.Clear();
        _itemsSearchBox.Clear();
        _itemsListGrid.Rows.Clear();
        _itemsListSearchBox.Clear();
        _spellsListGrid.Rows.Clear();
        _spellsListSearchBox.Clear();
        _groupAurasGrid.Rows.Clear();
        _nameplateAurasGrid.Rows.Clear();
    }

    private void HandleDeleteClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        if (grid.Columns[e.ColumnIndex].Name != "Delete")
        {
            return;
        }

        if (grid.Rows[e.RowIndex].IsNewRow)
        {
            return;
        }

        grid.Rows.RemoveAt(e.RowIndex);
        MarkDirty();
    }

    private void HandleSpellsListDeleteClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (sender is not DataGridView grid
            || e.RowIndex < 0
            || e.ColumnIndex < 0
            || grid.Columns[e.ColumnIndex].Name != "Delete")
        {
            return;
        }

        var row = grid.Rows[e.RowIndex];
        if (row.IsNewRow || row.Tag is not ClassBlocksStore.SpellsListEntry entry)
        {
            return;
        }

        if (_currentDocument is not null)
        {
            if (entry.OriginalSpellId > 0)
            {
                _currentDocument.DeletedSpellsListOriginalIds.Add(entry.OriginalSpellId);
            }

            _currentDocument.SpellsList.Remove(entry);
        }

        grid.Rows.RemoveAt(e.RowIndex);
        MarkDirty();
    }

    private void HandleItemsListDeleteClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (sender is not DataGridView grid
            || e.RowIndex < 0
            || e.ColumnIndex < 0
            || grid.Columns[e.ColumnIndex].Name != "Delete")
        {
            return;
        }

        var row = grid.Rows[e.RowIndex];
        if (row.IsNewRow || row.Tag is not ClassBlocksStore.ItemsListEntry entry)
        {
            return;
        }

        if (_currentDocument is not null)
        {
            if (entry.OriginalItemId > 0)
            {
                _currentDocument.DeletedItemsListOriginalIds.Add(entry.OriginalItemId);
            }

            _currentDocument.ItemsList.Remove(entry);
        }

        grid.Rows.RemoveAt(e.RowIndex);
        MarkDirty();
    }

    private void MoveSelectedRow(DataGridView grid, int delta)
    {
        if (grid.CurrentRow is null || grid.CurrentRow.IsNewRow)
        {
            return;
        }

        var index = grid.CurrentRow.Index;
        var target = index + delta;
        while (target >= 0 && target < grid.Rows.Count && !grid.Rows[target].IsNewRow && !grid.Rows[target].Visible)
        {
            target += delta;
        }

        if (target < 0 || target >= grid.Rows.Count || grid.Rows[target].IsNewRow)
        {
            return;
        }

        var values = new object[grid.Columns.Count];
        for (var i = 0; i < grid.Columns.Count; i++)
        {
            values[i] = grid.Rows[index].Cells[i].Value ?? DBNull.Value;
        }

        grid.Rows.RemoveAt(index);
        grid.Rows.Insert(target, values);
        grid.ClearSelection();
        grid.Rows[target].Selected = true;
        grid.CurrentCell = grid.Rows[target].Cells[0];
        MarkDirty();
    }

    private static IEnumerable<long> ParseIdList(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (var part in text.Split([',', ' ', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                yield return id;
            }
        }
    }

    private static decimal Clamp(NumericUpDown box, int value)
        => Math.Min(box.Maximum, Math.Max(box.Minimum, value));

    private static void NormalizeFixedStateNames(ClassBlocksStore.SpecBlocks spec)
    {
        var states = spec.NestedStates
            ? spec.CategorizedStates[ClassStateCatalog.CategoryState]
            : spec.FlatStates;
        states.RemoveAll(IsHiddenStateName);
        states.InsertRange(0, FixedStateNames);
    }

    private static bool IsHiddenStateName(string? name)
        => name is not null && FixedStateNames.Contains(name, StringComparer.Ordinal);

    private readonly record struct ItemDatabaseRange(int SourceIndex, int Count);

    private sealed class ItemDatabaseResultSet
    {
        private readonly IReadOnlyList<ItemSuggestion> _source;
        private readonly ItemDatabaseRange[]? _ranges;
        private readonly int[]? _indices;

        private ItemDatabaseResultSet(
            IReadOnlyList<ItemSuggestion> source,
            ItemDatabaseRange[]? ranges,
            int[]? indices,
            int count)
        {
            _source = source;
            _ranges = ranges;
            _indices = indices;
            Count = count;
        }

        public static ItemDatabaseResultSet Empty { get; } = new(
            Array.Empty<ItemSuggestion>(),
            Array.Empty<ItemDatabaseRange>(),
            null,
            0);

        public int Count { get; }

        public ItemSuggestion this[int index]
        {
            get
            {
                if (index < 0 || index >= Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                if (_indices is not null)
                {
                    return _source[_indices[index]];
                }

                var remaining = index;
                foreach (var range in _ranges!)
                {
                    if (remaining < range.Count)
                    {
                        return _source[range.SourceIndex + remaining];
                    }

                    remaining -= range.Count;
                }

                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public static ItemDatabaseResultSet FromAll(IReadOnlyList<ItemSuggestion> source)
            => source.Count == 0
                ? Empty
                : new ItemDatabaseResultSet(
                    source,
                    [new ItemDatabaseRange(0, source.Count)],
                    null,
                    source.Count);

        public static ItemDatabaseResultSet FromRanges(
            IReadOnlyList<ItemSuggestion> source,
            ItemDatabaseRange[] ranges)
        {
            var count = ranges.Sum(range => range.Count);
            return count == 0
                ? Empty
                : new ItemDatabaseResultSet(source, ranges, null, count);
        }

        public static ItemDatabaseResultSet FromIndices(
            IReadOnlyList<ItemSuggestion> source,
            int[] indices)
            => indices.Length == 0
                ? Empty
                : new ItemDatabaseResultSet(source, null, indices, indices.Length);
    }

    private readonly record struct SpellDatabaseRange(int SourceIndex, int Count);

    private sealed class SpellDatabaseResultSet
    {
        private readonly IReadOnlyList<SpellSuggestion> _source;
        private readonly SpellDatabaseRange[]? _ranges;
        private readonly int[]? _indices;

        private SpellDatabaseResultSet(
            IReadOnlyList<SpellSuggestion> source,
            SpellDatabaseRange[]? ranges,
            int[]? indices,
            int count)
        {
            _source = source;
            _ranges = ranges;
            _indices = indices;
            Count = count;
        }

        public static SpellDatabaseResultSet Empty { get; } = new(
            Array.Empty<SpellSuggestion>(),
            Array.Empty<SpellDatabaseRange>(),
            null,
            0);

        public int Count { get; }

        public SpellSuggestion this[int index]
        {
            get
            {
                if (index < 0 || index >= Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                if (_indices is not null)
                {
                    return _source[_indices[index]];
                }

                var remaining = index;
                foreach (var range in _ranges!)
                {
                    if (remaining < range.Count)
                    {
                        return _source[range.SourceIndex + remaining];
                    }

                    remaining -= range.Count;
                }

                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public static SpellDatabaseResultSet FromAll(IReadOnlyList<SpellSuggestion> source)
            => source.Count == 0
                ? Empty
                : new SpellDatabaseResultSet(
                    source,
                    [new SpellDatabaseRange(0, source.Count)],
                    null,
                    source.Count);

        public static SpellDatabaseResultSet FromRanges(
            IReadOnlyList<SpellSuggestion> source,
            SpellDatabaseRange[] ranges)
        {
            var count = ranges.Sum(range => range.Count);
            return count == 0
                ? Empty
                : new SpellDatabaseResultSet(source, ranges, null, count);
        }

        public static SpellDatabaseResultSet FromIndices(
            IReadOnlyList<SpellSuggestion> source,
            int[] indices)
            => indices.Length == 0
                ? Empty
                : new SpellDatabaseResultSet(source, null, indices, indices.Length);
    }

    private sealed record ClassListItem(int ClassId, string Name, string FileName, bool IsModern, string? Error = null)
    {
        public override string ToString()
            => Error is not null ? $"{Name}（错误）" : IsModern ? Name : $"{Name}（旧格式）";
    }

    private sealed record SpecOption(int ClassId, int Id, string Name)
    {
        public override string ToString() => Name;
    }
}
