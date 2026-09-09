using System.Drawing;
using System.Globalization;

namespace Shigure;

/// <summary>
/// 图形化编辑 Fuyutsui core/classmacros.lua 的 ClassMacros
/// （dynamicSpells / staticSpells / specialSpells）。
/// </summary>
public sealed class ClassMacrosEditorControl : UserControl
{
    private readonly Func<string?> _resolveClassMacrosPath;
    private readonly Func<string, int, Task<ClassConfigPostSaveResult>> _updateConfigAsync;

    private readonly ListBox _classList = new();
    private readonly Label _pathLabel = new();
    private readonly Label _statusLabel = new();
    private readonly ToolTip _toolTip = new();
    private readonly Label _offsetLabel = new();
    private readonly Button _reloadButton;
    private readonly Button _saveButton;

    private readonly DataGridView _dynamicGrid = new();
    private readonly DataGridView _staticGrid = new();
    private readonly DataGridView _specialGrid = new();

    private IReadOnlyDictionary<string, string> _macroBodies =
        new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly Dictionary<string, List<long>> _classSpellIdsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<long>> _classItemIdsByName = new(StringComparer.Ordinal);
    private ClassMacrosStore.MacrosDocument? _document;
    private ClassMacrosStore.ClassMacros? _currentMacros;
    private string? _currentClassFile;
    private int? _currentClassId;
    private bool _suppressUi;
    private bool _updatingDerivedColumns;
    private bool _dirty;

    internal event Action<bool>? DirtyStateChanged;
    internal bool HasUnsavedChanges => _dirty;

    public ClassMacrosEditorControl(
        Func<string?> resolveClassMacrosPath,
        Func<string, int, Task<ClassConfigPostSaveResult>> updateConfigAsync)
    {
        _resolveClassMacrosPath = resolveClassMacrosPath;
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
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);

        root.Controls.Add(BuildSidebar(), 0, 0);
        root.Controls.Add(BuildEditor(), 1, 0);
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
            if (!_suppressUi)
            {
                SelectClassFromList();
            }
        };
        panel.Controls.Add(_classList, 0, 1);
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
            RowCount = 3,
            Margin = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));

        _offsetLabel.Dock = DockStyle.Fill;
        _offsetLabel.AutoSize = false;
        _offsetLabel.ForeColor = UiTheme.Muted;
        _offsetLabel.BackColor = Color.Transparent;
        _offsetLabel.TextAlign = ContentAlignment.MiddleLeft;
        _offsetLabel.Padding = new Padding(14, 0, 14, 0);
        _offsetLabel.Margin = new Padding(0);
        _offsetLabel.AutoEllipsis = true;
        _offsetLabel.Text = "创建顺序：动态宏（每项 30 槽）→ 静态宏 → 特殊宏；空字符串保留槽位";
        root.Controls.Add(_offsetLabel, 0, 0);

        root.Controls.Add(BuildSectionTabs(), 0, 1);

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
        _toolTip.SetToolTip(_reloadButton, "从项目 Fuyutsui 重新加载宏 (F5)");
        _toolTip.SetToolTip(_saveButton, "保存宏并同步游戏 (Ctrl+S)");
        actions.Controls.Add(_reloadButton);
        actions.Controls.Add(_saveButton);
        actionRow.Controls.Add(BuildFooterInfo(), 0, 0);
        actionRow.Controls.Add(actions, 1, 0);
        root.Controls.Add(actionRow, 0, 2);
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
        _statusLabel.Text = "点击刷新以加载项目 Fuyutsui\\core\\classmacros.lua";
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
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(0)
        };
        for (var i = 0; i < 3; i++)
        {
            tabBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
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
            BuildDynamicPage(),
            BuildArrayPage(_staticGrid, "class-macros-static", "完整宏", "注释", showParsedMacro: true),
            BuildArrayPage(
                _specialGrid,
                "class-macros-special",
                "完整宏",
                "注释",
                showParsedMacro: false,
                editSpecialSpell: true)
        };
        foreach (var page in pages)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            page.BackColor = UiTheme.SurfaceRaised;
            page.Padding = new Padding(UiTheme.CardPadding);
            contentHost.Controls.Add(page);
        }

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

        var titles = new[] { "动态宏", "静态宏", "特殊宏" };
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

    private Control BuildDynamicPage()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0)
        };
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        ConfigureGrid(_dynamicGrid, "class-macros-dynamic");
        _dynamicGrid.Columns.Add(CreateMacroIconColumn());
        _dynamicGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name",
            HeaderText = "法术名（每项占 30 个团队点名槽）",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _dynamicGrid.Columns.Add(CreateDeleteColumn());
        WireGrid(_dynamicGrid);
        editor.Controls.Add(_dynamicGrid, 0, 0);
        editor.Controls.Add(BuildMoveButtons(_dynamicGrid), 0, 1);
        root.Controls.Add(editor, 0, 0);
        return root;
    }

    private Control BuildArrayPage(
        DataGridView grid,
        string cacheKey,
        string textHeader,
        string commentHeader,
        bool showParsedMacro,
        bool editSpecialSpell = false)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UiTheme.SurfaceRaised
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        ConfigureGrid(grid, cacheKey);
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Index",
            HeaderText = "顺序",
            Width = 72,
            ReadOnly = true
        });
        grid.Columns.Add(CreateMacroIconColumn());
        if (showParsedMacro)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Unit",
                HeaderText = "单位",
                Width = 72,
                ReadOnly = true
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Condition",
                HeaderText = "条件",
                Width = 180,
                ReadOnly = true
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Spell",
                HeaderText = "技能",
                Width = 180,
                ReadOnly = true
            });
        }
        else if (editSpecialSpell)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Spell",
                HeaderText = "技能",
                Width = 180
            });
        }

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Text",
            HeaderText = textHeader,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        if (!editSpecialSpell)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Comment", HeaderText = commentHeader, Width = 180 });
        }
        grid.Columns.Add(CreateDeleteColumn());
        WireGrid(grid);
        panel.Controls.Add(grid, 0, 0);
        panel.Controls.Add(BuildMoveButtons(grid), 0, 1);
        return panel;
    }

    private void WireGrid(DataGridView grid)
    {
        grid.CellContentClick += HandleDeleteClick;
        grid.CellFormatting += (_, e) => FormatMacroLineBreaks(grid, e);
        grid.CellParsing += (_, e) => ParseMacroLineBreaks(grid, e);
        grid.CellValueChanged += (_, e) =>
        {
            if (_updatingDerivedColumns)
            {
                return;
            }

            if (grid == _staticGrid
                && e.RowIndex >= 0
                && e.ColumnIndex >= 0
                && grid.Columns[e.ColumnIndex].Name is "Text" or "Comment")
            {
                UpdateMacroDisplay(grid, grid.Rows[e.RowIndex]);
            }

            if (e.RowIndex >= 0 && e.RowIndex < grid.Rows.Count)
            {
                UpdateMacroIcon(grid, grid.Rows[e.RowIndex]);
            }

            MarkDirty();
            UpdateOffsetHint();
        };
        grid.UserAddedRow += (_, _) =>
        {
            RenumberArrayRows(grid);
            MarkDirty();
            UpdateOffsetHint();
        };
        grid.RowsRemoved += (_, _) =>
        {
            if (!_suppressUi)
            {
                RenumberArrayRows(grid);
                MarkDirty();
                UpdateOffsetHint();
            }
        };
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

    // 宏文本底层保留真实换行（供解析及保存），表格中以字面量 \n 单行显示。
    private void FormatMacroLineBreaks(DataGridView grid, DataGridViewCellFormattingEventArgs e)
    {
        if ((grid != _staticGrid && grid != _specialGrid)
            || e.RowIndex < 0
            || e.ColumnIndex < 0
            || grid.Columns[e.ColumnIndex].Name != "Text"
            || e.Value is not string text)
        {
            return;
        }

        e.Value = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", "\\n", StringComparison.Ordinal);
        e.FormattingApplied = true;
    }

    private void ParseMacroLineBreaks(DataGridView grid, DataGridViewCellParsingEventArgs e)
    {
        if ((grid != _staticGrid && grid != _specialGrid)
            || e.RowIndex < 0
            || e.ColumnIndex < 0
            || grid.Columns[e.ColumnIndex].Name != "Text"
            || e.Value is not string text)
        {
            return;
        }

        e.Value = text.Replace("\\n", "\n", StringComparison.Ordinal);
        e.ParsingApplied = true;
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

        _document = null;
        _currentMacros = null;
        _currentClassFile = null;
        _currentClassId = null;
        SetDirty(false);

        _suppressUi = true;
        try
        {
            _classList.Items.Clear();
            ClearGrids();

            var path = _resolveClassMacrosPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                _pathLabel.Text = "未找到 core\\classmacros.lua";
                _statusLabel.Text = "请确认程序目录中包含 Fuyutsui\\core\\classmacros.lua 后点击刷新。";
                UpdateOffsetHint();
                return;
            }

            try
            {
                _document = ClassMacrosStore.Load(path);
                _macroBodies = ClassMacrosStore.LoadMacroBodies(path);
            }
            catch (Exception ex)
            {
                _pathLabel.Text = path;
                _statusLabel.Text = $"加载失败: {ex.Message}";
                return;
            }

            _pathLabel.Text = path;
            foreach (var (classId, className) in ClassNames.GetClasses())
            {
                var classFile = ClassMacrosStore.ToClassFileKey(classId);
                var has = _document.Classes.ContainsKey(classFile);
                _classList.Items.Add(new ClassListItem(classId, className, classFile, has));
            }

            // 文件中有但 ClassNames 未覆盖的键
            foreach (var classFile in _document.ClassOrder)
            {
                if (_classList.Items.Cast<ClassListItem>().Any(x =>
                        x.ClassFile.Equals(classFile, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                _classList.Items.Add(new ClassListItem(0, classFile, classFile, true));
            }

            _statusLabel.Text = $"已加载 {_document.Classes.Count} 个职业宏表";
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

    private void SelectClassFromList()
    {
        if (_classList.SelectedItem is not ClassListItem item)
        {
            return;
        }

        if (_dirty && _currentClassFile is not null
            && !_currentClassFile.Equals(item.ClassFile, StringComparison.OrdinalIgnoreCase)
            && !ConfirmDiscard())
        {
            _suppressUi = true;
            try
            {
                SelectClassInList(_currentClassFile);
            }
            finally
            {
                _suppressUi = false;
            }

            return;
        }

        var switching = _currentClassFile is not null
            && !_currentClassFile.Equals(item.ClassFile, StringComparison.OrdinalIgnoreCase);
        if (_dirty && switching)
        {
            // 丢弃：从源文本重新解析当前职业
            if (_document is not null)
            {
                try
                {
                    var reloaded = ClassMacrosStore.Load(_document.FilePath);
                    _document = reloaded;
                }
                catch
                {
                    // ignore
                }
            }

            SetDirty(false);
        }
        else if (switching)
        {
            CommitCurrentFromUi();
        }

        _currentClassFile = item.ClassFile;
        _currentClassId = item.ClassId > 0 ? item.ClassId : null;
        ReloadClassIconAliases();
        if (_document is null)
        {
            _currentMacros = null;
            ClearGrids();
            UpdateOffsetHint();
            return;
        }

        if (!_document.Classes.TryGetValue(item.ClassFile, out var macros))
        {
            macros = new ClassMacrosStore.ClassMacros();
            _document.Classes[item.ClassFile] = macros;
            if (!_document.ClassOrder.Contains(item.ClassFile, StringComparer.OrdinalIgnoreCase))
            {
                _document.ClassOrder.Add(item.ClassFile);
            }
        }

        _currentMacros = macros;
        _statusLabel.Text = _dirty ? "已修改（未保存）" : (item.HasData ? "可编辑" : "新建空表（保存后写入）");
        FillAllEditors();
    }

    private void FillAllEditors()
    {
        _suppressUi = true;
        try
        {
            _dynamicGrid.Rows.Clear();
            _staticGrid.Rows.Clear();
            _specialGrid.Rows.Clear();
            if (_currentMacros is null)
            {
                return;
            }

            AddArrayRows(_staticGrid, _currentMacros.StaticSpells);
            AddArrayRows(_specialGrid, _currentMacros.SpecialSpells);
            FillDynamicEditor();
        }
        finally
        {
            _suppressUi = false;
        }

        UpdateOffsetHint();
    }

    private void FillDynamicEditor()
    {
        var wasSuppressing = _suppressUi;
        _suppressUi = true;
        try
        {
            _dynamicGrid.Rows.Clear();
            if (_currentMacros is null)
            {
                return;
            }

            foreach (var name in _currentMacros.DynamicSpells)
            {
                var rowIndex = _dynamicGrid.Rows.Add(ResolveNamedMacroIcon(name)!, name, "×");
                UpdateMacroIcon(_dynamicGrid, _dynamicGrid.Rows[rowIndex]);
            }
        }
        finally
        {
            _suppressUi = wasSuppressing;
        }
    }

    private void UpdateOffsetHint()
    {
        var dynamicCount = 0;
        var staticCount = 0;
        var specialCount = 0;
        if (_currentMacros is not null)
        {
            // 数组中的空字符串同样占槽，所以按实际行数计算。
            staticCount = _staticGrid.Rows.Cast<DataGridViewRow>().Count(r => !r.IsNewRow);
            specialCount = _specialGrid.Rows.Cast<DataGridViewRow>().Count(r => !r.IsNewRow);
            var visibleDynamicCount = _dynamicGrid.Rows.Cast<DataGridViewRow>().Count(row => !row.IsNewRow);
            dynamicCount = visibleDynamicCount;
        }

        var dynamicSlots = dynamicCount * 30;
        var totalSlots = dynamicSlots + staticCount + specialCount;
        _offsetLabel.Text =
            $"职业动态宏 {dynamicSlots} 个（{dynamicCount} 项 × 30）；静态宏 {staticCount} 个；特殊宏 {specialCount} 个；" +
            $"共 {totalSlots} 个；最多 {FuyutsuiKeymapConverter.MacroSlotCapacity} 个";
    }

    private void CommitCurrentFromUi()
    {
        if (_currentMacros is null)
        {
            return;
        }

        CommitCurrentDynamicFromUi();
        WriteArrayGrid(_staticGrid, _currentMacros.StaticSpells);
        WriteArrayGrid(_specialGrid, _currentMacros.SpecialSpells);
    }

    private void CommitCurrentDynamicFromUi()
    {
        if (_currentMacros is null)
        {
            return;
        }

        var values = new List<string>();
        foreach (DataGridViewRow row in _dynamicGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            values.Add(row.Cells["Name"].Value?.ToString()?.Trim() ?? string.Empty);
        }

        _currentMacros.DynamicSpells.Clear();
        _currentMacros.DynamicSpells.AddRange(values);
    }

    private static void WriteArrayGrid(DataGridView grid, List<ClassMacrosStore.ArrayEntry> target)
    {
        target.Clear();
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var text = row.Cells["Text"].Value?.ToString() ?? "";
            var metadataColumn = grid.Columns.Contains("Comment") ? "Comment" : "Spell";
            var comment = row.Cells[metadataColumn].Value?.ToString()?.Trim();
            target.Add(new ClassMacrosStore.ArrayEntry
            {
                Text = text.Replace("\r\n", "\n", StringComparison.Ordinal),
                Comment = string.IsNullOrWhiteSpace(comment) ? null : comment
            });
        }
    }

    private async Task SaveAndUpdateAsync()
    {
        if (_document is null)
        {
            MessageBox.Show("请先刷新并加载 classmacros.lua。", "宏", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_currentClassId is not { } classId)
        {
            MessageBox.Show("请先选择职业。", "宏", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var localSaved = false;
        try
        {
            CommitCurrentFromUi();
            ClassMacrosStore.Save(_document);
            localSaved = true;
            SetDirty(false);
            _statusLabel.Text = "本地 Lua 已保存，正在更新配置并同步游戏…";
            var updateResult = await _updateConfigAsync(_document.FilePath, classId);
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
                UpdateOffsetHint();
                return;
            }

            _statusLabel.Text = $"已保存宏及该职业的 {updateResult.SavedModuleCount} 个模块";
            UpdateOffsetHint();
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
        => MessageBox.Show("当前修改尚未保存，确定丢弃吗？", "宏", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
           == DialogResult.Yes;

    private void SelectClassInList(string? classFile)
    {
        for (var i = 0; i < _classList.Items.Count; i++)
        {
            if (_classList.Items[i] is ClassListItem item
                && item.ClassFile.Equals(classFile, StringComparison.OrdinalIgnoreCase))
            {
                _classList.SelectedIndex = i;
                return;
            }
        }
    }

    private void ClearGrids()
    {
        _dynamicGrid.Rows.Clear();
        _staticGrid.Rows.Clear();
        _specialGrid.Rows.Clear();
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
        RenumberArrayRows(grid);
        MarkDirty();
        UpdateOffsetHint();
    }

    private void MoveSelectedRow(DataGridView grid, int delta)
    {
        if (grid.CurrentRow is null || grid.CurrentRow.IsNewRow)
        {
            return;
        }

        var index = grid.CurrentRow.Index;
        var target = index + delta;
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
        RenumberArrayRows(grid);
        MarkDirty();
        UpdateOffsetHint();
    }

    private void AddArrayRows(DataGridView grid, List<ClassMacrosStore.ArrayEntry> entries)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var rowIndex = grid.Rows.Add();
            var row = grid.Rows[rowIndex];
            row.Cells["Index"].Value = (i + 1).ToString(CultureInfo.InvariantCulture);
            row.Cells["Text"].Value = entry.Text;
            if (grid == _specialGrid)
            {
                row.Cells["Spell"].Value = entry.Comment ?? "";
            }
            else
            {
                row.Cells["Comment"].Value = entry.Comment ?? "";
            }
            row.Cells["Delete"].Value = "×";
            if (grid == _staticGrid)
            {
                UpdateMacroDisplay(grid, row);
            }

            UpdateMacroIcon(grid, row);
        }
    }

    private void UpdateMacroDisplay(DataGridView grid, DataGridViewRow row)
    {
        if (!grid.Columns.Contains("Unit")
            || !grid.Columns.Contains("Spell")
            || !grid.Columns.Contains("Condition")
            || row.IsNewRow)
        {
            return;
        }

        var text = row.Cells["Text"].Value?.ToString() ?? "";
        var comment = row.Cells["Comment"].Value?.ToString();
        var parsed = FuyutsuiKeymapConverter.ParseStaticMacro(text, comment);

        _updatingDerivedColumns = true;
        try
        {
            row.Cells["Unit"].Value = parsed.Unit.ToString(CultureInfo.InvariantCulture);
            row.Cells["Spell"].Value = parsed.Spell;
            row.Cells["Condition"].Value = MacroConditionText.ToDisplayText(parsed.Condition);
            UpdateMacroIcon(grid, row);
        }
        finally
        {
            _updatingDerivedColumns = false;
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

        ReloadClassIconAliases();
        RefreshAllMacroIcons();
    }

    private void RefreshAllMacroIcons()
    {
        foreach (var grid in new[] { _dynamicGrid, _staticGrid, _specialGrid })
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (!row.IsNewRow)
                {
                    UpdateMacroIcon(grid, row);
                }
            }

            grid.Invalidate();
        }
    }

    private void ReloadClassIconAliases()
    {
        _classSpellIdsByName.Clear();
        _classItemIdsByName.Clear();
        if (_currentClassId is not { } classId || classId <= 0)
        {
            return;
        }

        var macrosPath = _resolveClassMacrosPath();
        if (string.IsNullOrWhiteSpace(macrosPath))
        {
            return;
        }

        var classDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(macrosPath)!, "..", "class"));
        var classPath = Path.Combine(classDirectory, $"{ClassNames.GetConfigFileName(classId)}.lua");
        if (!File.Exists(classPath))
        {
            return;
        }

        try
        {
            var document = ClassBlocksStore.Load(classPath);
            foreach (var spell in document.SpellsList)
            {
                if (spell.SpellId <= 0 || string.IsNullOrWhiteSpace(spell.Name))
                {
                    continue;
                }

                var name = spell.Name.Trim();
                AddNamedId(_classSpellIdsByName, name, spell.SpellId);
                SpellIconCatalog.Register(spell.SpellId, name, overwriteIdName: true);
            }

            foreach (var spec in OrderedSpecsForIcons(document))
            {
                foreach (var item in spec.Items)
                {
                    if (item.ItemId is not { } itemId || itemId <= 0 || string.IsNullOrWhiteSpace(item.Name))
                    {
                        continue;
                    }

                    var name = item.Name.Trim();
                    AddNamedId(_classItemIdsByName, name, itemId);
                    SpellIconCatalog.RegisterItem(itemId, name);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or InvalidDataException or ArgumentException)
        {
            // 当前职业配置缺失时仍可用数据包按名称匹配。
        }
    }

    private IEnumerable<ClassBlocksStore.SpecBlocks> OrderedSpecsForIcons(ClassBlocksStore.ClassFileDocument document)
    {
        foreach (var spec in document.Specs.Values)
        {
            yield return spec;
        }
    }

    private static void AddNamedId(Dictionary<string, List<long>> map, string name, long id)
    {
        if (!map.TryGetValue(name, out var ids))
        {
            ids = [];
            map[name] = ids;
        }

        if (!ids.Contains(id))
        {
            ids.Add(id);
        }
    }

    private void UpdateMacroIcon(DataGridView grid, DataGridViewRow row)
    {
        if (!grid.Columns.Contains("Icon") || row.IsNewRow)
        {
            return;
        }

        row.Cells["Icon"].Value = ResolveMacroItemIcon(grid, row);
    }

    private Image? ResolveMacroItemIcon(DataGridView grid, DataGridViewRow row)
    {
        if (grid.Columns.Contains("Spell"))
        {
            var icon = ResolveNamedMacroIcon(row.Cells["Spell"].Value?.ToString());
            if (icon is not null)
            {
                return icon;
            }
        }

        if (grid.Columns.Contains("Name"))
        {
            var icon = ResolveNamedMacroIcon(row.Cells["Name"].Value?.ToString());
            if (icon is not null)
            {
                return icon;
            }
        }

        if (grid.Columns.Contains("Text"))
        {
            return ResolveItemReferenceIcon(row.Cells["Text"].Value?.ToString());
        }

        return null;
    }

    private Image? ResolveNamedMacroIcon(string? name)
    {
        var normalized = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var itemReferenceIcon = ResolveItemReferenceIcon(normalized);
        if (itemReferenceIcon is not null)
        {
            return itemReferenceIcon;
        }

        if (_classSpellIdsByName.TryGetValue(normalized, out var spellIds))
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

        if (_classItemIdsByName.TryGetValue(normalized, out var itemIds))
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

        return SpellIconCatalog.Get(normalized) ?? SpellIconCatalog.GetItem(normalized);
    }

    private Image? ResolveItemReferenceIcon(string? text)
    {
        var normalized = text?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (SpellIconCatalog.TryParseItemReference(normalized, out var itemId))
        {
            return SpellIconCatalog.GetItem(itemId);
        }

        if (_macroBodies.TryGetValue(normalized, out var body)
            && SpellIconCatalog.TryParseItemReference(body, out itemId))
        {
            return SpellIconCatalog.GetItem(itemId);
        }

        return null;
    }

    private static DataGridViewImageColumn CreateMacroIconColumn()
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

    private static void RenumberArrayRows(DataGridView grid)
    {
        if (!grid.Columns.Contains("Index"))
        {
            return;
        }

        var index = 1;
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (!row.IsNewRow)
            {
                row.Cells["Index"].Value = index++.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    private sealed record ClassListItem(int ClassId, string Name, string ClassFile, bool HasData)
    {
        public override string ToString()
            => HasData ? Name : $"{Name}（无）";
    }
}
