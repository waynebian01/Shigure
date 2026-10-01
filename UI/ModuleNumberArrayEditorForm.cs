using System.Drawing;
using System.Globalization;

namespace Shigure;

/// <summary>模块数字数组编辑器；图标和名称始终从法术图标包按数字查询。</summary>
public sealed class ModuleNumberArrayEditorForm : Form
{
    private const int LookupPageSize = 20;

    private readonly UiDropDown _kindBox = new();
    private readonly TextBox _nameBox = new UiThemedTextBox();
    private readonly DataGridView _grid = new UiThemedDataGridView();
    private Label _lookupTitle = null!;
    private readonly DataGridView _lookupGrid = new UiThemedDataGridView();
    private readonly TextBox _lookupFilterBox = new UiThemedTextBox();
    private readonly Label _lookupStatusLabel = new();
    private readonly System.Windows.Forms.Timer _lookupFilterTimer = new() { Interval = 150 };
    private readonly HashSet<string> _takenNames;
    private readonly NumberCatalog _bossCatalog = NumberCatalog.LoadBosses();
    private readonly NumberCatalog _mapCatalog = NumberCatalog.LoadMaps();
    private SpellDatabaseResultSet _lookupResults = SpellDatabaseResultSet.Empty;
    private List<NumberCatalogEntry> _catalogMatches = [];
    private int _lookupVisibleCount;
    private CancellationTokenSource? _lookupFilterCancellation;
    private int _lookupFilterVersion;
    private bool _expandingLookupRows;

    public ModuleNumberArray? Result { get; private set; }

    public ModuleNumberArrayEditorForm(ModuleNumberArray? source, IEnumerable<string> takenNames)
    {
        _takenNames = new HashSet<string>(takenNames, StringComparer.Ordinal);
        InitializeComponent();
        _nameBox.Text = source?.Name ?? string.Empty;
        foreach (var number in source?.Numbers ?? [])
        {
            var index = _grid.Rows.Add(null!, string.Empty, number.ToString(CultureInfo.InvariantCulture));
            UpdateRowInfo(_grid.Rows[index]);
        }

        _kindBox.SelectedIndexChanged += (_, _) => ApplyKind();
        if (source?.Kind is ModuleNumberArrayKind kind && kind != ModuleNumberArrayKind.Spell)
        {
            _kindBox.SelectedIndex = (int)kind;
        }

        SpellIconCatalog.CatalogChanged += OnSpellIconCatalogChanged;
        _lookupFilterTimer.Tick += async (_, _) =>
        {
            _lookupFilterTimer.Stop();
            await ApplyLookupFilterAsync();
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UiTheme.ApplyDarkTitleBar(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lookupFilterTimer.Stop();
            _lookupFilterTimer.Dispose();
            CancelLookupFilter();
            SpellIconCatalog.CatalogChanged -= OnSpellIconCatalogChanged;
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        Text = "编辑数组";
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.Text;
        ClientSize = new Size(1180, 620);
        MinimumSize = new Size(960, 480);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(UiTheme.CardPadding),
            BackColor = UiTheme.Surface
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        Controls.Add(root);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
            BackColor = UiTheme.Surface
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiTheme.PageGap));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(BuildArrayColumn(), 0, 0);
        body.Controls.Add(BuildLookupColumn(), 2, 0);
        root.Controls.Add(body, 0, 0);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = UiTheme.Surface,
            Margin = new Padding(0, UiTheme.PageGap, 0, 0)
        };
        var save = UiTheme.CreateButton("保存", UiTheme.ButtonKind.Primary);
        save.Width = 108;
        save.Height = 38;
        save.Click += (_, _) => SaveArray();
        var cancel = UiTheme.CreateButton("取消", UiTheme.ButtonKind.Secondary);
        cancel.Width = 108;
        cancel.Height = 38;
        cancel.DialogResult = DialogResult.Cancel;
        var remove = UiTheme.CreateButton("删除选中行", UiTheme.ButtonKind.Secondary);
        remove.Width = 120;
        remove.Height = 38;
        remove.Click += (_, _) =>
        {
            foreach (var row in _grid.SelectedCells.Cast<DataGridViewCell>()
                         .Select(cell => cell.OwningRow).OfType<DataGridViewRow>()
                         .Distinct().Where(row => !row.IsNewRow).ToArray())
                _grid.Rows.Remove(row);
        };
        footer.Controls.Add(save);
        footer.Controls.Add(cancel);
        footer.Controls.Add(remove);
        root.Controls.Add(footer, 0, 1);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private Control BuildArrayColumn()
    {
        var column = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            BackColor = UiTheme.SurfaceRaised
        };
        column.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        column.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var nameRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
            BackColor = UiTheme.SurfaceRaised
        };
        nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168));
        nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
        nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        UiTheme.StyleComboBox(_kindBox);
        _kindBox.Dock = DockStyle.Fill;
        _kindBox.Margin = new Padding(0, 6, 8, 6);
        _kindBox.Items.Add(new UiDropDownOption(ModuleNumberArrayKind.Spell, "技能列表"));
        _kindBox.Items.Add(new UiDropDownOption(ModuleNumberArrayKind.Boss, "首领列表"));
        _kindBox.Items.Add(new UiDropDownOption(ModuleNumberArrayKind.Map, "地图"));
        _kindBox.Items.Add(new UiDropDownOption(ModuleNumberArrayKind.Other, "其他"));
        _kindBox.SelectedIndex = 0;
        nameRow.Controls.Add(_kindBox, 0, 0);
        nameRow.Controls.Add(new Label
        {
            Text = "名称",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UiTheme.Text,
            BackColor = UiTheme.SurfaceRaised
        }, 1, 0);
        UiTheme.StyleTextBox(_nameBox);
        _nameBox.Dock = DockStyle.Fill;
        _nameBox.Margin = new Padding(0, 6, 0, 6);
        nameRow.Controls.Add(_nameBox, 2, 0);
        column.Controls.Add(nameRow, 0, 0);

        UiTheme.StyleDataGridView(_grid);
        _grid.AllowUserToAddRows = true;
        _grid.AllowUserToDeleteRows = true;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.Columns.Add(new DataGridViewImageColumn
        {
            Name = "Icon",
            HeaderText = "图标",
            Width = 64,
            ReadOnly = true,
            ImageLayout = DataGridViewImageCellLayout.Zoom
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name",
            HeaderText = "名称",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            ReadOnly = true
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Number",
            HeaderText = "数字",
            Width = 160
        });
        _grid.CellEndEdit += (_, e) =>
        {
            if (e.RowIndex >= 0 && !_grid.Rows[e.RowIndex].IsNewRow)
                UpdateRowInfo(_grid.Rows[e.RowIndex]);
        };
        _grid.DataError += (_, e) => e.ThrowException = false;
        column.Controls.Add(_grid, 0, 1);
        return CreateSectionCard("数组", column);
    }

    private Control BuildLookupColumn()
    {
        var column = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            BackColor = UiTheme.SurfaceRaised
        };
        column.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        column.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        UiTheme.StyleTextBox(_lookupFilterBox);
        _lookupFilterBox.Dock = DockStyle.Fill;
        _lookupFilterBox.Margin = new Padding(0, 4, 0, 4);
        _lookupFilterBox.PlaceholderText = "spellId 或名称";
        _lookupFilterBox.TextChanged += (_, _) => ScheduleLookupFilter();
        column.Controls.Add(_lookupFilterBox, 0, 0);

        _lookupStatusLabel.Dock = DockStyle.Fill;
        _lookupStatusLabel.ForeColor = UiTheme.Muted;
        _lookupStatusLabel.BackColor = Color.Transparent;
        _lookupStatusLabel.TextAlign = ContentAlignment.MiddleRight;
        _lookupStatusLabel.Margin = new Padding(0);

        UiTheme.StyleDataGridView(_lookupGrid);
        _lookupGrid.AllowUserToAddRows = false;
        _lookupGrid.ReadOnly = true;
        _lookupGrid.VirtualMode = true;
        _lookupGrid.EditMode = DataGridViewEditMode.EditProgrammatically;
        _lookupGrid.RowCount = 0;
        _lookupGrid.CellValueNeeded += OnLookupCellValueNeeded;
        _lookupGrid.CellContentClick += OnLookupCellContentClick;
        _lookupGrid.Scroll += OnLookupScroll;
        _lookupGrid.DataError += (_, e) => e.ThrowException = false;
        _lookupGrid.Columns.Add(new DataGridViewImageColumn
        {
            Name = "Icon",
            HeaderText = "图标",
            Width = 54,
            ReadOnly = true,
            ImageLayout = DataGridViewImageCellLayout.Zoom,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _lookupGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "SpellId",
            HeaderText = "spellId",
            Width = 110,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _lookupGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name",
            HeaderText = "名称",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _lookupGrid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "Add",
            HeaderText = "添加至数组",
            Text = "添加至数组",
            UseColumnTextForButtonValue = true,
            Width = 120,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _lookupGrid.HandleCreated += (_, _) => RefreshLookup();
        column.Controls.Add(_lookupGrid, 0, 1);
        return CreateSectionCard("技能查找", column, _lookupStatusLabel);
    }

    private Control CreateSectionCard(string title, Control body, Control? headerTrailing = null)
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

        var titleLabel = UiTheme.CreateSectionTitle(Font, title);
        if (headerTrailing is not null)
        {
            _lookupTitle = titleLabel;
        }

        Control header = titleLabel;
        if (headerTrailing is not null)
        {
            var headerRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            headerRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            headerRow.Controls.Add(header, 0, 0);
            headerTrailing.Margin = new Padding(8, 0, 0, 0);
            headerRow.Controls.Add(headerTrailing, 1, 0);
            header = headerRow;
        }

        card.Controls.Add(header, 0, 0);
        body.Margin = new Padding(0, 4, 0, 0);
        card.Controls.Add(body, 0, 1);
        return card;
    }

    private void ScheduleLookupFilter()
    {
        if (UsesCatalogLookup)
        {
            ApplyCatalogFilter();
            return;
        }

        _lookupFilterTimer.Stop();
        _lookupFilterVersion++;
        CancelLookupFilter();
        if (!_lookupGrid.IsHandleCreated || IsDisposed || Disposing)
        {
            return;
        }

        if (!SpellIconCatalog.IsPackageAvailable)
        {
            ApplyLookupResults(SpellDatabaseResultSet.Empty, "未安装技能数据库");
            return;
        }

        _lookupStatusLabel.Text = "正在筛选…";
        _lookupFilterTimer.Start();
    }

    private void RefreshLookup()
    {
        if (IsDisposed || Disposing || !_lookupGrid.IsHandleCreated)
        {
            return;
        }

        if (UsesCatalogLookup)
        {
            _lookupFilterTimer.Stop();
            CancelLookupFilter();
            ApplyCatalogFilter();
            return;
        }

        _lookupFilterTimer.Stop();
        _lookupFilterVersion++;
        CancelLookupFilter();
        var packageAvailable = SpellIconCatalog.IsPackageAvailable;
        _lookupFilterBox.Enabled = packageAvailable;
        if (!packageAvailable)
        {
            ApplyLookupResults(SpellDatabaseResultSet.Empty, "未安装技能数据库");
            return;
        }

        if (string.IsNullOrWhiteSpace(_lookupFilterBox.Text))
        {
            ApplyLookupResults(SpellDatabaseResultSet.FromAll(SpellIconCatalog.GetSuggestionsSnapshot()));
            return;
        }

        _lookupStatusLabel.Text = "正在筛选…";
        _ = ApplyLookupFilterAsync();
    }

    private async Task ApplyLookupFilterAsync()
    {
        if (IsDisposed || Disposing || !_lookupGrid.IsHandleCreated)
        {
            return;
        }

        var version = _lookupFilterVersion;
        var query = _lookupFilterBox.Text.Trim();
        var snapshot = SpellIconCatalog.GetSuggestionsSnapshot();
        var registeredNames = SpellIconCatalog.GetRegisteredSpellNamesSnapshot();
        if (string.IsNullOrEmpty(query))
        {
            ApplyLookupResults(SpellDatabaseResultSet.FromAll(snapshot));
            return;
        }

        var cancellation = new CancellationTokenSource();
        _lookupFilterCancellation = cancellation;
        try
        {
            var results = await Task.Run(
                () => SpellDatabaseQuery.Filter(snapshot, registeredNames, query, cancellation.Token),
                cancellation.Token);
            if (cancellation.IsCancellationRequested
                || version != _lookupFilterVersion
                || IsDisposed
                || Disposing)
            {
                return;
            }

            ApplyLookupResults(results);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_lookupFilterCancellation, cancellation))
            {
                _lookupFilterCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void ApplyLookupResults(SpellDatabaseResultSet results, string? status = null)
    {
        _lookupResults = results;
        _lookupVisibleCount = Math.Min(LookupPageSize, results.Count);
        SetLookupRowCount(_lookupVisibleCount);
        _lookupStatusLabel.Text = status ?? FormatLookupStatus();
        _lookupGrid.Invalidate();
    }

    // 虚拟表在滚动位置超过新行数时直接改 RowCount 会抛出 index 越界。
    private void SetLookupRowCount(int count)
    {
        if (_lookupGrid.RowCount > 0)
        {
            _lookupGrid.ClearSelection();
            _lookupGrid.CurrentCell = null;
            if (_lookupGrid.FirstDisplayedScrollingRowIndex > 0)
            {
                _lookupGrid.FirstDisplayedScrollingRowIndex = 0;
            }
        }

        _lookupGrid.RowCount = count;
    }

    private string FormatLookupStatus()
        => _lookupResults.Count == 0
            ? $"匹配 0 个{LookupNoun}"
            : $"已显示 {_lookupVisibleCount:N0} / 共 {_lookupResults.Count:N0} 个{LookupNoun}";

    private void OnLookupScroll(object? sender, ScrollEventArgs e)
    {
        if (UsesCatalogLookup
            || e.ScrollOrientation != ScrollOrientation.VerticalScroll
            || _expandingLookupRows
            || _lookupVisibleCount >= _lookupResults.Count
            || _lookupGrid.FirstDisplayedScrollingRowIndex < 0)
        {
            return;
        }

        var lastDisplayedRow = _lookupGrid.FirstDisplayedScrollingRowIndex
                               + _lookupGrid.DisplayedRowCount(includePartialRow: true);
        if (lastDisplayedRow < _lookupVisibleCount - 2)
        {
            return;
        }

        _expandingLookupRows = true;
        try
        {
            _lookupVisibleCount = Math.Min(
                _lookupVisibleCount + LookupPageSize,
                _lookupResults.Count);
            _lookupGrid.RowCount = _lookupVisibleCount;
            _lookupStatusLabel.Text = FormatLookupStatus();
        }
        finally
        {
            _expandingLookupRows = false;
        }
    }

    private void CancelLookupFilter()
    {
        var cancellation = _lookupFilterCancellation;
        _lookupFilterCancellation = null;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
    }

    private void OnLookupCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _lookupVisibleCount || e.ColumnIndex < 0)
        {
            return;
        }

        if (UsesCatalogLookup)
        {
            var entry = _catalogMatches[e.RowIndex];
            e.Value = _lookupGrid.Columns[e.ColumnIndex].Name switch
            {
                "SpellId" => entry.Id.ToString(CultureInfo.InvariantCulture),
                "Name" => entry.Name,
                "Add" => "添加",
                _ => null
            };
            return;
        }

        var suggestion = _lookupResults[e.RowIndex];
        var displayName = SpellIconCatalog.ResolveSuggestionName(suggestion.SpellId, suggestion.Name) ?? string.Empty;
        e.Value = _lookupGrid.Columns[e.ColumnIndex].Name switch
        {
            "Icon" => SpellIconCatalog.Get(suggestion.SpellId),
            "SpellId" => suggestion.SpellId.ToString(CultureInfo.InvariantCulture),
            "Name" => displayName,
            "Add" => "添加至数组",
            _ => null
        };
    }

    private void OnLookupCellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0
            || e.RowIndex >= _lookupVisibleCount
            || e.ColumnIndex < 0
            || _lookupGrid.Columns[e.ColumnIndex].Name != "Add")
        {
            return;
        }

        var id = UsesCatalogLookup
            ? _catalogMatches[e.RowIndex].Id
            : _lookupResults[e.RowIndex].SpellId;
        AddNumber(id);
    }

    private void AddNumber(long spellId)
    {
        _grid.EndEdit();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var text = row.Cells["Number"].Value?.ToString()?.Trim();
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                && number == spellId)
            {
                _grid.ClearSelection();
                _grid.CurrentCell = row.Cells["Number"];
                _grid.FirstDisplayedScrollingRowIndex = row.Index;
                return;
            }
        }

        var index = _grid.Rows.Add(null!, string.Empty, spellId.ToString(CultureInfo.InvariantCulture));
        UpdateRowInfo(_grid.Rows[index]);
        _grid.FirstDisplayedScrollingRowIndex = index;
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

        RefreshLookup();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (!row.IsNewRow)
            {
                UpdateRowInfo(row);
            }
        }
    }

    private void UpdateRowInfo(DataGridViewRow row)
    {
        var text = row.Cells["Number"].Value?.ToString()?.Trim();
        row.Cells["Icon"].Value = null;
        row.Cells["Name"].Value = string.Empty;
        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return;
        }

        switch (CurrentKind)
        {
            case ModuleNumberArrayKind.Spell:
                row.Cells["Icon"].Value = SpellIconCatalog.Get(number);
                row.Cells["Name"].Value = SpellIconCatalog.GetSpellName(number) ?? string.Empty;
                break;
            case ModuleNumberArrayKind.Boss:
                row.Cells["Name"].Value = _bossCatalog.FindName(number) ?? string.Empty;
                break;
            case ModuleNumberArrayKind.Map:
                row.Cells["Name"].Value = _mapCatalog.FindName(number) ?? string.Empty;
                break;
        }
    }

    private void ApplyKind()
    {
        var kind = CurrentKind;
        var spellLike = kind is ModuleNumberArrayKind.Spell or ModuleNumberArrayKind.Other;
        _grid.Columns["Icon"]!.Visible = kind == ModuleNumberArrayKind.Spell;
        var nameColumn = _grid.Columns["Name"]!;
        nameColumn.Visible = kind != ModuleNumberArrayKind.Other;
        nameColumn.AutoSizeMode = kind == ModuleNumberArrayKind.Other
            ? DataGridViewAutoSizeColumnMode.None
            : DataGridViewAutoSizeColumnMode.Fill;
        nameColumn.HeaderText = kind switch
        {
            ModuleNumberArrayKind.Boss => "首领名称",
            ModuleNumberArrayKind.Map => "地图名称",
            _ => "名称"
        };
        var numberColumn = _grid.Columns["Number"]!;
        numberColumn.HeaderText = kind switch
        {
            ModuleNumberArrayKind.Boss => "索引ID",
            ModuleNumberArrayKind.Map => "地图ID",
            _ => "数字"
        };
        if (kind == ModuleNumberArrayKind.Other)
        {
            numberColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        }
        else
        {
            numberColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            numberColumn.Width = 160;
        }

        _lookupGrid.Columns["Icon"]!.Visible = spellLike;
        _lookupGrid.Columns["SpellId"]!.HeaderText = kind switch
        {
            ModuleNumberArrayKind.Boss => "索引ID",
            ModuleNumberArrayKind.Map => "地图ID",
            _ => "spellId"
        };
        _lookupGrid.Columns["Name"]!.HeaderText = kind switch
        {
            ModuleNumberArrayKind.Boss => "首领名称",
            ModuleNumberArrayKind.Map => "地图名称",
            _ => "名称"
        };
        var addText = spellLike ? "添加至数组" : "添加";
        var addColumn = (DataGridViewButtonColumn)_lookupGrid.Columns["Add"]!;
        addColumn.Text = addText;
        addColumn.HeaderText = addText;
        _lookupFilterBox.PlaceholderText = kind switch
        {
            ModuleNumberArrayKind.Boss => "名称或索引ID",
            ModuleNumberArrayKind.Map => "名称或地图ID",
            _ => "spellId 或名称"
        };
        if (_lookupTitle is not null)
        {
            _lookupTitle.Text = kind switch
            {
                ModuleNumberArrayKind.Boss => "首领列表",
                ModuleNumberArrayKind.Map => "地图列表",
                _ => "技能查找"
            };
        }

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (!row.IsNewRow)
            {
                UpdateRowInfo(row);
            }
        }

        _grid.Invalidate();
        RefreshLookup();
    }

    private void ApplyCatalogFilter()
    {
        var catalog = CurrentKind == ModuleNumberArrayKind.Boss ? _bossCatalog : _mapCatalog;
        if (catalog.Error is not null)
        {
            ShowCatalogMatches([], catalog.Error);
            return;
        }

        var query = _lookupFilterBox.Text.Trim();
        IEnumerable<NumberCatalogEntry> matches = catalog.Entries;
        if (query.Length > 0)
        {
            var numeric = query.All(character => character is >= '0' and <= '9');
            matches = numeric
                ? catalog.Entries.Where(entry =>
                    entry.Id.ToString(CultureInfo.InvariantCulture).StartsWith(query, StringComparison.Ordinal))
                : catalog.Entries.Where(entry =>
                    entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var matched = matches.ToArray();
        var status = matched.Length == 0
            ? $"匹配 0 个{LookupNoun}"
            : $"已显示 {matched.Length:N0} / 共 {catalog.Entries.Count:N0} 个{LookupNoun}";
        ShowCatalogMatches(matched, status);
    }

    private void ShowCatalogMatches(IReadOnlyList<NumberCatalogEntry> matches, string status)
    {
        if (!_lookupGrid.IsHandleCreated)
        {
            _catalogMatches = matches.ToList();
            return;
        }

        _lookupResults = SpellDatabaseResultSet.Empty;
        _catalogMatches = matches.ToList();
        _lookupVisibleCount = _catalogMatches.Count;
        SetLookupRowCount(_lookupVisibleCount);
        _lookupStatusLabel.Text = status;
        _lookupGrid.Invalidate();
    }

    private ModuleNumberArrayKind CurrentKind
        => _kindBox.SelectedItem is UiDropDownOption { Value: ModuleNumberArrayKind kind }
            ? kind
            : ModuleNumberArrayKind.Spell;

    private bool UsesCatalogLookup
        => CurrentKind is ModuleNumberArrayKind.Boss or ModuleNumberArrayKind.Map;

    private string LookupNoun
        => CurrentKind switch
        {
            ModuleNumberArrayKind.Boss => "首领",
            ModuleNumberArrayKind.Map => "地图",
            _ => "技能"
        };

    private void SaveArray()
    {
        _grid.EndEdit();
        var name = _nameBox.Text.Trim();
        if (!ConditionExpression.IsArrayName(name))
        {
            MessageBox.Show("数组名称不能为空，也不能包含空格、括号、逗号、& 或 |，且不能是纯数字。", "Shigure",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_takenNames.Contains(name))
        {
            MessageBox.Show("数组名称已存在。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var numbers = new List<long>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            var text = row.Cells["Number"].Value?.ToString()?.Trim();
            if (string.IsNullOrEmpty(text)) continue;
            if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                MessageBox.Show($"第 {row.Index + 1} 行请输入整数。", "Shigure",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            numbers.Add(number);
        }

        Result = new ModuleNumberArray
        {
            Name = name,
            Numbers = numbers.Distinct().ToList(),
            Kind = CurrentKind
        };
        DialogResult = DialogResult.OK;
        Close();
    }
}
