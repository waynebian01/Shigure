using System.Drawing;

namespace Shigure;

/// <summary>队友/敌人数量共用的分组列表筛选编辑器。</summary>
internal sealed class CountFilterEditorControl : UserControl
{
    private const string NumberColumn = "Number";
    private const string EnabledColumn = "Enabled";
    private const string FieldColumn = "Field";
    private const string ComparisonColumn = "Comparison";
    private const string ValueColumn = "Value";
    private const string DeleteColumn = "Delete";

    private static readonly ComparisonOption[] AllComparisons =
    [
        new("==", CountConditionComparisonKind.Equal),
        new("!=", CountConditionComparisonKind.NotEqual),
        new(">", CountConditionComparisonKind.GreaterThan),
        new("<", CountConditionComparisonKind.LessThan),
        new(">=", CountConditionComparisonKind.GreaterThanOrEqual),
        new("<=", CountConditionComparisonKind.LessThanOrEqual)
    ];

    private static readonly ComparisonOption[] EqualityComparisons = AllComparisons[..2];
    private static readonly ComparisonOption[] CastSpellComparisons =
    [
        .. EqualityComparisons,
        new("in", CountConditionComparisonKind.In),
        new("not in", CountConditionComparisonKind.NotIn)
    ];
    private static readonly ValueOption[] RoleValues =
    [
        new("坦克 (1)", 1),
        new("治疗 (2)", 2),
        new("输出 (3)", 3)
    ];
    private static readonly ValueOption[] ClassValues = ClassNames.GetClasses()
        .Select(item => new ValueOption($"{item.Name} ({item.Id})", item.Id))
        .ToArray();
    private static readonly ValueOption[] DispelValues =
    [
        new("魔法 (1)", 1),
        new("诅咒 (2)", 2),
        new("疾病 (3)", 3),
        new("中毒 (4)", 4),
        new("激怒 (9)", 9),
        new("流血 (11)", 11)
    ];
    private static readonly ValueOption[] CombatValues =
    [
        new("战斗中", 1),
        new("不在战斗中", 0)
    ];
    private static readonly ValueOption[] ImprovedGarroteValues =
    [
        new("无锁喉 (0)", 0),
        new("强化锁喉 (1)", 1),
        new("普通锁喉 (2)", 2)
    ];

    private readonly IReadOnlyList<ConditionField> _allyAuras;
    private readonly IReadOnlyList<ConditionField> _enemyAuras;
    private readonly HashSet<string> _thresholdFields;
    private readonly IReadOnlyList<string> _formulaValueNames;
    private readonly HashSet<string> _formulaValueNameSet;
    private readonly FlowLayoutPanel _groupsPanel = new UiThemedFlowLayoutPanel();
    private readonly Label _emptyHint = new();
    private readonly List<GroupEditor> _groups = new();
    private bool _enemy;
    private bool _loading;
    private readonly bool _hasNameplateImprovedGarrote;
    private bool _retainedImprovedGarrote;
    private readonly bool _hasNameplateThreat;
    private bool _retainedThreat;
    private readonly bool _hasNameplateCastSpell;
    private bool _retainedCastSpell;
    private readonly IReadOnlyList<string> _numberArrayNames;

    public event EventHandler? Changed;

    public CountFilterEditorControl(
        IReadOnlyList<ConditionField> allyAuras,
        IReadOnlyList<ConditionField> enemyAuras,
        IReadOnlyList<string> thresholdFields,
        IReadOnlyList<string> formulaValueNames,
        bool hasNameplateImprovedGarrote = false,
        bool hasNameplateThreat = false,
        bool hasNameplateCastSpell = false,
        IReadOnlyList<string>? numberArrayNames = null)
    {
        _allyAuras = allyAuras;
        _enemyAuras = enemyAuras;
        _hasNameplateImprovedGarrote = hasNameplateImprovedGarrote;
        _hasNameplateThreat = hasNameplateThreat;
        _hasNameplateCastSpell = hasNameplateCastSpell;
        _numberArrayNames = (numberArrayNames ?? []).ToArray();
        _thresholdFields = new HashSet<string>(thresholdFields, StringComparer.Ordinal);
        _formulaValueNames = formulaValueNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        _formulaValueNameSet = new HashSet<string>(_formulaValueNames, StringComparer.Ordinal);
        Dock = DockStyle.Fill;
        Margin = new Padding(0);
        BackColor = UiTheme.Surface;

        _groupsPanel.Dock = DockStyle.Fill;
        _groupsPanel.FlowDirection = FlowDirection.TopDown;
        _groupsPanel.WrapContents = false;
        _groupsPanel.AutoScroll = true;
        _groupsPanel.BackColor = UiTheme.Surface;
        _groupsPanel.Padding = new Padding(0, 0, 4, 0);
        Controls.Add(_groupsPanel);

        var addGroupButton = UiTheme.CreateButton("添加条件组", UiTheme.ButtonKind.Secondary);
        UiTheme.StyleActionButton(addGroupButton, 160);
        KeepButtonTextVisible(addGroupButton, 160);
        addGroupButton.Margin = new Padding(0, 8, 0, 8);
        addGroupButton.Click += (_, _) => AddGroup(CountConditionGroupMode.All, []);
        _groupsPanel.Controls.Add(addGroupButton);

        _emptyHint.Text = "尚未添加筛选条件\n无需筛选时可直接保存；需要限定单位时请添加条件组。";
        _emptyHint.ForeColor = UiTheme.Muted;
        _emptyHint.TextAlign = ContentAlignment.MiddleCenter;
        _emptyHint.AutoSize = false;
        _emptyHint.Height = 132;
        _emptyHint.Margin = new Padding(0, 8, 0, 0);
        _groupsPanel.Controls.Add(_emptyHint);
        _groupsPanel.Resize += (_, _) => FitGroupWidths();
    }

    private static void KeepButtonTextVisible(Button button, int minimumWidth)
    {
        void Fit()
        {
            var textSize = TextRenderer.MeasureText(
                button.Text,
                button.Font,
                Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            button.Size = new Size(
                Math.Max(minimumWidth,
                    textSize.Width + button.Padding.Horizontal + Math.Max(40, button.Font.Height + 8)),
                Math.Max(40, textSize.Height + button.Padding.Vertical + UiTheme.Scale(button, 8)));
        }

        button.HandleCreated += (_, _) => Fit();
        button.FontChanged += (_, _) => Fit();
        Fit();
    }

    private void FitGroupWidths(bool force = false)
    {
        // FlowLayoutPanel 子项不会自动跟宿主拉宽，按内容区宽度同步条件组卡片。
        // 预留系统滚动条宽度，避免宽度同步本身触发水平滚动条。
        var width = _groupsPanel.ClientSize.Width
            - _groupsPanel.Padding.Horizontal
            - SystemInformation.VerticalScrollBarWidth
            - 2;
        if (width < 200)
        {
            return;
        }

        // 留出的滚动条空间允许拖动时按 12 像素步进布局，减少表格列重复计算。
        if (!force
            && _emptyHint.Width > 0
            && Math.Abs(_emptyHint.Width - width) < 12
            && _groups.All(group => Math.Abs(group.Root.Width - width) < 12))
        {
            return;
        }

        _groupsPanel.SuspendLayout();
        try
        {
            if (_emptyHint.Width != width)
            {
                _emptyHint.Width = width;
            }

            foreach (var group in _groups)
            {
                if (group.Root.Width != width)
                {
                    group.Root.Width = width;
                }
            }
        }
        finally
        {
            _groupsPanel.ResumeLayout(true);
        }
    }

    internal void FinishResize() => FitGroupWidths(force: true);

    public void SetEnemyTarget(bool enemy)
    {
        if (_enemy == enemy)
        {
            return;
        }

        _enemy = enemy;
        foreach (var group in _groups)
        {
            group.RefreshFieldOptions();
        }

        OnChanged();
    }

    public void LoadGroups(IReadOnlyList<ModuleCountConditionGroup>? groups)
    {
        _loading = true;
        try
        {
            // 功能关闭后仍保留已保存的筛选，避免编辑时静默改成生命值条件。
            _retainedImprovedGarrote = (groups ?? []).SelectMany(group => group.Conditions ?? [])
                .Any(condition => condition.Field == CountConditionFieldKind.ImprovedGarrote);
            _retainedThreat = (groups ?? []).SelectMany(group => group.Conditions ?? [])
                .Any(condition => condition.Field == CountConditionFieldKind.Threat);
            _retainedCastSpell = (groups ?? []).SelectMany(group => group.Conditions ?? [])
                .Any(condition => condition.Field == CountConditionFieldKind.CastSpell);
            foreach (var editor in _groups)
            {
                _groupsPanel.Controls.Remove(editor.Root);
                editor.Root.Dispose();
            }
            _groups.Clear();

            foreach (var group in groups ?? [])
            {
                AddGroup(group.Mode, group.Conditions ?? []);
            }

            if (_groups.Count == 0)
            {
                AddGroup(CountConditionGroupMode.All, []);
            }
        }
        finally
        {
            _loading = false;
            RenumberGroups();
            FitGroupWidths(force: true);
            foreach (var editor in _groups)
            {
                editor.ApplyCurrentFontLayout();
            }
            OnChanged();
        }
    }

    public List<ModuleCountConditionGroup> ReadGroups()
        => _groups.Select(group => group.Read()).ToList();

    public bool TryValidate(out string message)
    {
        foreach (var group in _groups)
        {
            if (!group.TryValidate(out message))
            {
                return false;
            }
        }

        message = string.Empty;
        return true;
    }

    private void AddGroup(
        CountConditionGroupMode mode,
        IReadOnlyList<ModuleCountCondition> conditions)
    {
        var editor = new GroupEditor(this, mode);
        _groups.Add(editor);
        _emptyHint.Visible = false;
        _groupsPanel.Controls.Add(editor.Root);
        _groupsPanel.Controls.SetChildIndex(editor.Root, Math.Max(0, _groupsPanel.Controls.Count - 2));
        foreach (var condition in conditions)
        {
            editor.AddCondition(condition);
        }

        RenumberGroups();
        if (!_loading)
        {
            FitGroupWidths(force: true);
            editor.ApplyCurrentFontLayout();
        }
        OnChanged();
    }

    private void DeleteGroup(GroupEditor editor)
    {
        _groups.Remove(editor);
        _groupsPanel.Controls.Remove(editor.Root);
        editor.Root.Dispose();
        if (_groups.Count == 0)
        {
            AddGroup(CountConditionGroupMode.All, []);
        }

        RenumberGroups();
        OnChanged();
    }

    private void RenumberGroups()
    {
        for (var index = 0; index < _groups.Count; index++)
        {
            _groups[index].SetNumber(index + 1);
        }
    }

    private List<FieldOption> CreateFieldOptions(IEnumerable<long>? additionalAuraIds = null)
    {
        var options = _enemy
            ? new List<FieldOption>
            {
                new("生命值", CountConditionFieldKind.Health),
                new("距离", CountConditionFieldKind.Range),
                new("战斗", CountConditionFieldKind.Combat)
            }
            : new List<FieldOption>
            {
                new("生命值", CountConditionFieldKind.Health),
                new("治疗吸收", CountConditionFieldKind.HealingAbsorb),
                new("职责", CountConditionFieldKind.Role),
                new("驱散", CountConditionFieldKind.Dispel),
                new("职业", CountConditionFieldKind.Class)
            };
        if (_enemy && (_hasNameplateImprovedGarrote || _retainedImprovedGarrote))
        {
            options.Add(new FieldOption("强化锁喉", CountConditionFieldKind.ImprovedGarrote));
        }
        if (_enemy && (_hasNameplateThreat || _retainedThreat))
        {
            options.Add(new FieldOption("仇恨值", CountConditionFieldKind.Threat));
        }
        if (_enemy && (_hasNameplateCastSpell || _retainedCastSpell))
        {
            options.Add(new FieldOption("施法技能", CountConditionFieldKind.CastSpell));
        }
        foreach (var aura in (_enemy ? _enemyAuras : _allyAuras))
        {
            if (TryReadAuraSpellId(aura, out var spellId)
                && options.All(option => option.AuraSpellId != spellId))
            {
                options.Add(new FieldOption(aura.DisplayName, CountConditionFieldKind.Aura, spellId));
            }
        }

        foreach (var spellId in additionalAuraIds ?? [])
        {
            if (spellId > 0 && options.All(option => option.AuraSpellId != spellId))
            {
                options.Add(new FieldOption($"未知光环 / {spellId}", CountConditionFieldKind.Aura, spellId));
            }
        }

        return options;
    }

    private static bool TryReadAuraSpellId(ConditionField field, out long spellId)
    {
        foreach (var part in field.Name.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (long.TryParse(part, out spellId) && spellId > 0)
            {
                return true;
            }
        }

        spellId = 0;
        return false;
    }

    private void OnChanged()
    {
        if (!_loading)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool IsFormulaValueName(string name)
        => name.Length > 0 && _formulaValueNameSet.Contains(name);

    private sealed class GroupEditor
    {
        private readonly CountFilterEditorControl _owner;
        private readonly Label _title = new();
        private readonly UiDropDown _modeBox = new();
        private readonly DataGridView _grid = new UiThemedDataGridView();
        private ToolStripDropDown? _valueDropDown;
        private ToolStripDropDown? _comboDropDown;
        private bool _openFormulaDropDown;
        private bool _updating;

        public TableLayoutPanel Root { get; }

        public GroupEditor(CountFilterEditorControl owner, CountConditionGroupMode mode)
        {
            _owner = owner;
            Root = new TableLayoutPanel
            {
                Width = 770,
                Height = 280,
                BackColor = UiTheme.SurfaceRaised,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(8),
                ColumnCount = 1,
                RowCount = 3
            };
            Root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            Root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                ColumnCount = 3,
                RowCount = 1
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _title.Dock = DockStyle.Fill;
            _title.ForeColor = UiTheme.Text;
            _title.TextAlign = ContentAlignment.MiddleLeft;
            _title.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            _title.Margin = new Padding(0);
            header.Controls.Add(_title, 0, 0);

            UiTheme.StyleComboBox(_modeBox);
            _modeBox.Dock = DockStyle.Fill;
            _modeBox.Margin = new Padding(8, 4, 8, 4);
            _modeBox.Items.AddRange([
                new GroupModeOption("全部满足", CountConditionGroupMode.All),
                new GroupModeOption("任一满足", CountConditionGroupMode.Any)
            ]);
            _modeBox.SelectedIndex = mode == CountConditionGroupMode.Any ? 1 : 0;
            _modeBox.SelectedIndexChanged += (_, _) => _owner.OnChanged();
            header.Controls.Add(_modeBox, 1, 0);

            var deleteGroupButton = UiTheme.CreateButton("删除组", UiTheme.ButtonKind.Danger);
            UiTheme.StyleActionButton(deleteGroupButton, 108);
            KeepButtonTextVisible(deleteGroupButton, 108);
            deleteGroupButton.Margin = new Padding(0);
            deleteGroupButton.Click += (_, _) => _owner.DeleteGroup(this);
            var deleteHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            deleteGroupButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            deleteHost.Controls.Add(deleteGroupButton);
            deleteGroupButton.SizeChanged += (_, _) =>
            {
                Root.RowStyles[0].Height = Math.Max(
                    Root.RowStyles[0].Height,
                    deleteGroupButton.Height + 8);
                FitThreeConditionRows();
            };
            deleteHost.Layout += (_, _) =>
            {
                deleteGroupButton.Location = new Point(
                    Math.Max(0, deleteHost.ClientSize.Width - deleteGroupButton.Width),
                    Math.Max(0, (deleteHost.ClientSize.Height - deleteGroupButton.Height) / 2));
            };
            header.Controls.Add(deleteHost, 2, 0);
            Root.Controls.Add(header, 0, 0);

            ConfigureGrid();
            // StyleDataGridView 会 Dock=Fill；放在百分比行里随条件组卡片拉宽。
            _grid.Dock = DockStyle.Fill;
            _grid.Margin = new Padding(0, 4, 0, 4);
            Root.Controls.Add(_grid, 0, 1);
            UiTheme.EnableDebouncedFillColumns(_grid);

            var addButton = UiTheme.CreateButton("添加条件", UiTheme.ButtonKind.Secondary);
            UiTheme.StyleActionButton(addButton, 144);
            KeepButtonTextVisible(addButton, 144);
            addButton.Dock = DockStyle.Left;
            addButton.Margin = new Padding(0, 4, 0, 4);
            addButton.SizeChanged += (_, _) =>
            {
                Root.RowStyles[2].Height = Math.Max(
                    Root.RowStyles[2].Height,
                    addButton.Height + addButton.Margin.Vertical);
                FitThreeConditionRows();
            };
            addButton.Click += (_, _) => AddCondition(null);
            Root.Controls.Add(addButton, 0, 2);
            FitThreeConditionRows();
        }

        public void SetNumber(int number) => _title.Text = $"条件组 {number}";

        public void ApplyCurrentFontLayout() => ApplyColumnLayout();

        public void AddCondition(ModuleCountCondition? condition)
        {
            var extraAuraIds = condition?.AuraSpellId is { } spellId ? new[] { spellId } : [];
            RefreshFieldOptions(extraAuraIds);
            var fieldKey = condition is null
                ? FieldOption.KeyFor(CountConditionFieldKind.Health, null)
                : FieldOption.KeyFor(condition.Field, condition.AuraSpellId);
            var rowIndex = _grid.Rows.Add(
                _grid.Rows.Count + 1,
                condition?.Enabled ?? true,
                fieldKey,
                condition?.Comparison ?? CountConditionComparisonKind.GreaterThan,
                string.Empty,
                string.Empty);
            var row = _grid.Rows[rowIndex];
            ConfigureRow(row, condition);
            RenumberRows();
            _owner.OnChanged();
        }

        public void RefreshFieldOptions(IEnumerable<long>? additionalAuraIds = null)
        {
            if (_grid.Columns[FieldColumn] is not DataGridViewComboBoxColumn column)
            {
                return;
            }

            var existingAuraIds = _grid.Rows.Cast<DataGridViewRow>()
                .Select(row => ParseFieldKey(row.Cells[FieldColumn].Value?.ToString()).AuraSpellId)
                .Where(id => id is > 0)
                .Select(id => id!.Value)
                .Concat(additionalAuraIds ?? [])
                .Distinct()
                .ToArray();
            column.DataSource = _owner.CreateFieldOptions(existingAuraIds);

            foreach (DataGridViewRow row in _grid.Rows)
            {
                var option = ParseFieldKey(row.Cells[FieldColumn].Value?.ToString());
                var allowed = _owner.CreateFieldOptions(existingAuraIds)
                    .Any(candidate => candidate.Key == option.Key);
                if (!allowed)
                {
                    row.Cells[FieldColumn].Value = FieldOption.KeyFor(CountConditionFieldKind.Health, null);
                    ConfigureRow(row, null);
                }
            }
        }

        public ModuleCountConditionGroup Read()
        {
            _grid.EndEdit();
            return new ModuleCountConditionGroup
            {
                Mode = (_modeBox.SelectedItem as GroupModeOption)?.Mode ?? CountConditionGroupMode.All,
                Conditions = _grid.Rows.Cast<DataGridViewRow>()
                    .Select(TryReadCondition)
                    .Where(condition => condition is not null)
                    .Cast<ModuleCountCondition>()
                    .ToList()
            };
        }

        public bool TryValidate(out string message)
        {
            _grid.EndEdit();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var enabled = row.Cells[EnabledColumn].Value is bool value && value;
                if (!enabled)
                {
                    continue;
                }

                if (TryReadCondition(row) is null)
                {
                    message = $"{_title.Text}存在未填写完整的启用条件。";
                    return false;
                }
            }

            message = string.Empty;
            return true;
        }

        private void ConfigureGrid()
        {
            UiTheme.StyleDataGridView(_grid);
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _grid.MultiSelect = false;
            _grid.EditMode = DataGridViewEditMode.EditOnEnter;
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = NumberColumn,
                HeaderText = "#",
                Width = 42,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            _grid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = EnabledColumn,
                HeaderText = "启用",
                Width = 62,
                ValueType = typeof(bool),
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            _grid.Columns.Add(new DataGridViewComboBoxColumn
            {
                Name = FieldColumn,
                HeaderText = "字段",
                Width = 340,
                MinimumWidth = 220,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 60,
                DataSource = _owner.CreateFieldOptions(),
                DisplayMember = nameof(FieldOption.Text),
                ValueMember = nameof(FieldOption.Key),
                ValueType = typeof(string),
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                FlatStyle = FlatStyle.Flat,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            _grid.Columns.Add(new DataGridViewComboBoxColumn
            {
                Name = ComparisonColumn,
                HeaderText = "判断",
                Width = 80,
                DataSource = AllComparisons.ToList(),
                DisplayMember = nameof(ComparisonOption.Text),
                ValueMember = nameof(ComparisonOption.Kind),
                ValueType = typeof(CountConditionComparisonKind),
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                FlatStyle = FlatStyle.Flat,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = ValueColumn,
                HeaderText = "值",
                Width = 190,
                MinimumWidth = 120,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 40,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            _grid.Columns.Add(new DataGridViewButtonColumn
            {
                Name = DeleteColumn,
                HeaderText = "删除",
                Text = "×",
                UseColumnTextForButtonValue = true,
                Width = 54,
                FlatStyle = FlatStyle.Flat,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });

            ApplyColumnLayout();
            // 通用主题会按 DPI 扩大最小列宽；主题处理后重新分配，值列吸收窗口拖宽后的剩余空间。
            _grid.HandleCreated += (_, _) => ApplyColumnLayout();
            _grid.FontChanged += (_, _) => ApplyColumnLayout();

            _grid.CurrentCellDirtyStateChanged += (_, _) =>
            {
                if (_grid.IsCurrentCellDirty)
                {
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            _grid.CellValueChanged += (_, e) =>
            {
                if (_updating || e.RowIndex < 0)
                {
                    return;
                }

                if (_grid.Columns[e.ColumnIndex].Name is FieldColumn or ComparisonColumn)
                {
                    ConfigureRow(_grid.Rows[e.RowIndex], null);
                }
                _owner.OnChanged();
            };
            _grid.CellEndEdit += (_, _) => _owner.OnChanged();
            _grid.CellContentClick += (_, e) =>
            {
                if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != DeleteColumn)
                {
                    return;
                }

                _grid.Rows.RemoveAt(e.RowIndex);
                RenumberRows();
                _owner.OnChanged();
            };
            // 下拉列保持只读，单击直接弹出深色列表，避免先进入原生白底编辑框。
            _grid.CellClick += (_, e) =>
            {
                if (e.RowIndex < 0
                    || e.ColumnIndex < 0
                    || _grid.Rows[e.RowIndex].Cells[e.ColumnIndex] is not DataGridViewComboBoxCell)
                {
                    return;
                }

                var rowIndex = e.RowIndex;
                var columnIndex = e.ColumnIndex;
                _grid.BeginInvoke(() =>
                {
                    if (!_grid.IsDisposed)
                    {
                        ShowComboDropDown(rowIndex, columnIndex);
                    }
                });
            };
            _grid.EditingControlShowing += (_, e) =>
            {
                if (e.Control is TextBox textBox
                    && _grid.CurrentCell?.OwningColumn?.Name == ValueColumn
                    && _grid.CurrentCell is FormulaValueCell)
                {
                    textBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    textBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
                    var source = new AutoCompleteStringCollection();
                    var arrayComparison = ReadComparison(_grid.Rows[_grid.CurrentCell.RowIndex]
                        .Cells[ComparisonColumn].Value) is CountConditionComparisonKind.In
                            or CountConditionComparisonKind.NotIn;
                    source.AddRange((arrayComparison
                            ? _owner._numberArrayNames
                            : _owner._thresholdFields.Concat(_owner._formulaValueNames))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray());
                    textBox.AutoCompleteCustomSource = source;
                    var rowIndex = _grid.CurrentCell.RowIndex;
                    var columnIndex = _grid.CurrentCell.ColumnIndex;
                    _grid.BeginInvoke(() =>
                    {
                        if (!_grid.IsDisposed)
                        {
                            _grid.InvalidateCell(columnIndex, rowIndex);
                        }
                    });
                }
            };
            _grid.CellMouseDown += (_, e) =>
            {
                _openFormulaDropDown = false;
                if (e.RowIndex < 0
                    || e.ColumnIndex < 0
                    || _grid.Rows[e.RowIndex].Cells[e.ColumnIndex] is not FormulaValueCell cell)
                {
                    return;
                }

                var buttonBounds = UiTheme.GetDropDownButtonBounds(
                    _grid,
                    new Rectangle(0, 0, cell.Size.Width, cell.Size.Height));
                _openFormulaDropDown = buttonBounds.Contains(e.X, e.Y);
            };
            _grid.CellBeginEdit += (_, e) =>
            {
                if (e.RowIndex >= 0
                    && e.ColumnIndex >= 0
                    && _grid.Rows[e.RowIndex].Cells[e.ColumnIndex] is DataGridViewComboBoxCell)
                {
                    e.Cancel = true;
                    return;
                }

                if (_openFormulaDropDown
                    && e.RowIndex >= 0
                    && _grid.Columns[e.ColumnIndex].Name == ValueColumn
                    && _grid.Rows[e.RowIndex].Cells[e.ColumnIndex] is FormulaValueCell)
                {
                    e.Cancel = true;
                }
            };
            _grid.CellMouseUp += (_, e) =>
            {
                if (!_openFormulaDropDown || e.RowIndex < 0 || e.ColumnIndex < 0)
                {
                    return;
                }

                _openFormulaDropDown = false;
                var rowIndex = e.RowIndex;
                var columnIndex = e.ColumnIndex;
                _grid.BeginInvoke(() =>
                {
                    if (_grid.IsDisposed)
                    {
                        return;
                    }

                    if (_grid.IsCurrentCellInEditMode)
                    {
                        _grid.EndEdit();
                    }

                    ShowFormulaValueDropDown(rowIndex, columnIndex);
                });
            };
            _grid.KeyDown += (_, e) =>
            {
                var openDropDown = e.KeyCode is Keys.F4 or Keys.Enter or Keys.Space
                    || (e.KeyCode == Keys.Down && e.Alt);
                if (_grid.CurrentCell is DataGridViewComboBoxCell comboCell && openDropDown)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    ShowComboDropDown(comboCell.RowIndex, comboCell.ColumnIndex);
                    return;
                }

                if (_grid.CurrentCell is not FormulaValueCell cell
                    || e.KeyCode is not (Keys.F4 or Keys.Down)
                    || (e.KeyCode == Keys.Down && !e.Alt))
                {
                    return;
                }

                e.Handled = true;
                e.SuppressKeyPress = true;
                if (_grid.IsCurrentCellInEditMode)
                {
                    _grid.EndEdit();
                }

                ShowFormulaValueDropDown(cell.RowIndex, cell.ColumnIndex);
            };
            _grid.CellPainting += (_, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0)
                {
                    return;
                }

                var cell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
                if (cell is FormulaValueCell)
                {
                    UiTheme.PaintDataGridViewComboBoxCell(_grid, e);
                    return;
                }

                if (cell is DataGridViewComboBoxCell)
                {
                    UiTheme.PaintDataGridViewComboBoxCell(_grid, e);
                }
            };
            _grid.CellFormatting += (_, e) =>
            {
                if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != ValueColumn)
                {
                    return;
                }

                var cell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
                if (cell is not FormulaValueCell)
                {
                    return;
                }

                var text = cell.Value?.ToString()?.Trim() ?? string.Empty;
                cell.ToolTipText = ReadComparison(_grid.Rows[e.RowIndex].Cells[ComparisonColumn].Value)
                    is CountConditionComparisonKind.In or CountConditionComparisonKind.NotIn
                    ? $"数组: {text}"
                    : _owner.IsFormulaValueName(text) ? $"公式动态数值: {text}" : string.Empty;
            };
            _grid.Disposed += (_, _) =>
            {
                CloseValueDropDown();
                CloseComboDropDown();
            };
            _grid.DataError += (_, _) => { };
        }

        private void ConfigureRow(DataGridViewRow row, ModuleCountCondition? seed)
        {
            _updating = true;
            try
            {
                var option = ParseFieldKey(row.Cells[FieldColumn].Value?.ToString());
                var restricted = option.Kind is CountConditionFieldKind.Role
                    or CountConditionFieldKind.Dispel
                    or CountConditionFieldKind.Class
                    or CountConditionFieldKind.Combat
                    or CountConditionFieldKind.ImprovedGarrote;
                var previousComparison = seed?.Comparison
                    ?? ReadComparison(row.Cells[ComparisonColumn].Value)
                    ?? CountConditionComparisonKind.Equal;
                var castSpell = option.Kind == CountConditionFieldKind.CastSpell;
                var comparisons = castSpell ? CastSpellComparisons
                    : restricted ? EqualityComparisons : AllComparisons;
                var comparisonCell = new DataGridViewComboBoxCell
                {
                    DataSource = comparisons.ToList(),
                    DisplayMember = nameof(ComparisonOption.Text),
                    ValueMember = nameof(ComparisonOption.Kind),
                    ValueType = typeof(CountConditionComparisonKind),
                    DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                    FlatStyle = FlatStyle.Flat
                };
                row.Cells[ComparisonColumn] = comparisonCell;
                comparisonCell.Value = !comparisons.Any(candidate => candidate.Kind == previousComparison)
                        ? CountConditionComparisonKind.Equal
                        : previousComparison;

                var selectedComparison = (CountConditionComparisonKind)comparisonCell.Value;
                var selectingArray = castSpell && selectedComparison is (CountConditionComparisonKind.In
                    or CountConditionComparisonKind.NotIn);

                object? previousValue = seed is null
                    ? null
                    : restricted
                        ? seed.Value
                        : FormatValue(seed);
                DataGridViewCell valueCell = option.Kind switch
                {
                    CountConditionFieldKind.Role => CreateValueComboCell(RoleValues),
                    CountConditionFieldKind.Dispel => CreateValueComboCell(DispelValues),
                    CountConditionFieldKind.Class => CreateValueComboCell(ClassValues),
                    CountConditionFieldKind.Combat => CreateValueComboCell(CombatValues),
                    CountConditionFieldKind.ImprovedGarrote => CreateValueComboCell(ImprovedGarroteValues),
                    _ => new FormulaValueCell()
                };
                row.Cells[ValueColumn] = valueCell;
                if (valueCell is DataGridViewComboBoxCell)
                {
                    valueCell.ReadOnly = true;
                }

                valueCell.Value = previousValue
                    ?? (selectingArray ? _owner._numberArrayNames.FirstOrDefault() ?? string.Empty
                        : DefaultValue(option.Kind));
                if (option.Kind == CountConditionFieldKind.Threat)
                {
                    valueCell.ToolTipText = "0 未坦克 / 1 仇恨高但未坦克 / 2 坦克但仇恨不稳 / 3 稳定坦克；无仇恨记录按 0 处理。";
                }
            }
            finally
            {
                _updating = false;
            }
        }

        private ModuleCountCondition? TryReadCondition(DataGridViewRow row)
        {
            var option = ParseFieldKey(row.Cells[FieldColumn].Value?.ToString());
            if (option.Kind == CountConditionFieldKind.Aura && option.AuraSpellId is not > 0
                || ReadComparison(row.Cells[ComparisonColumn].Value) is not { } comparison)
            {
                return null;
            }

            var rawValue = row.Cells[ValueColumn].Value?.ToString()?.Trim() ?? string.Empty;
            if (comparison is CountConditionComparisonKind.In or CountConditionComparisonKind.NotIn)
            {
                if (option.Kind != CountConditionFieldKind.CastSpell
                    || !_owner._numberArrayNames.Contains(rawValue, StringComparer.Ordinal))
                    return null;
                return new ModuleCountCondition
                {
                    Enabled = row.Cells[EnabledColumn].Value is bool arrayEnabled && arrayEnabled,
                    Field = option.Kind,
                    Comparison = comparison,
                    ValueKind = CountConditionValueKind.NumberArray,
                    ValueField = rawValue
                };
            }
            var isFormulaValue = _owner.IsFormulaValueName(rawValue);
            var constant = 0;
            var typedValue = !isFormulaValue && TryReadInt(row.Cells[ValueColumn].Value, out constant);
            var isStateField = isFormulaValue
                || (!typedValue && _owner._thresholdFields.Contains(rawValue));
            if (!typedValue && !isStateField)
            {
                return null;
            }

            return new ModuleCountCondition
            {
                Enabled = row.Cells[EnabledColumn].Value is bool enabled && enabled,
                Field = option.Kind,
                AuraSpellId = option.AuraSpellId,
                Comparison = comparison,
                ValueKind = isStateField ? CountConditionValueKind.StateField : CountConditionValueKind.Constant,
                Value = typedValue ? constant : 0,
                ValueField = isStateField ? rawValue : null
            };
        }

        private void ShowFormulaValueDropDown(int rowIndex, int columnIndex)
        {
            if (_grid.IsDisposed
                || rowIndex < 0
                || columnIndex < 0
                || rowIndex >= _grid.Rows.Count
                || _grid.Rows[rowIndex].Cells[columnIndex] is not FormulaValueCell cell)
            {
                return;
            }

            CloseComboDropDown();
            CloseValueDropDown();
            _grid.CurrentCell = cell;
            var currentValue = cell.Value?.ToString()?.Trim() ?? string.Empty;
            var arrayComparison = ReadComparison(_grid.Rows[rowIndex].Cells[ComparisonColumn].Value)
                is CountConditionComparisonKind.In or CountConditionComparisonKind.NotIn;
            var manualValue = currentValue.Length > 0 && int.TryParse(currentValue, out var number)
                ? number.ToString()
                : "0";
            var options = arrayComparison
                ? _owner._numberArrayNames.Select(name => new UiDropDownOption(name, name)).ToList()
                : new List<UiDropDownOption>
                {
                    new(manualValue, "手动输入数字", LeadingText: manualValue)
                };
            if (!arrayComparison)
                options.AddRange(_owner._formulaValueNames.Select(name => new UiDropDownOption(name, name)));
            if (options.Count == 0) return;

            var cellBounds = _grid.GetCellDisplayRectangle(columnIndex, rowIndex, cutOverflow: true);
            ToolStripDropDown? dropDown = null;
            dropDown = UiDropDownPopup.Show(
                _grid,
                cellBounds,
                options,
                arrayComparison || _owner.IsFormulaValueName(currentValue) ? currentValue : manualValue,
                selected =>
                {
                    var value = selected.Value?.ToString() ?? string.Empty;
                    cell.Value = value;
                    _grid.InvalidateCell(cell);
                    _owner.OnChanged();
                    if (!arrayComparison && string.Equals(selected.Display, "手动输入数字", StringComparison.Ordinal))
                    {
                        _grid.BeginInvoke(() =>
                        {
                            if (_grid.IsDisposed || cell.DataGridView is null || cell.ReadOnly)
                            {
                                return;
                            }

                            _grid.CurrentCell = cell;
                            _grid.BeginEdit(selectAll: true);
                        });
                    }
                },
                preferredWidth: 240,
                closed: () =>
                {
                    if (ReferenceEquals(_valueDropDown, dropDown))
                    {
                        _valueDropDown = null;
                    }
                });
            _valueDropDown = dropDown;
        }

        private void ShowComboDropDown(int rowIndex, int columnIndex)
        {
            if (_grid.IsDisposed
                || rowIndex < 0
                || columnIndex < 0
                || rowIndex >= _grid.Rows.Count
                || _grid.Rows[rowIndex].Cells[columnIndex] is not DataGridViewComboBoxCell cell)
            {
                return;
            }

            CloseValueDropDown();
            CloseComboDropDown();
            if (_grid.IsCurrentCellInEditMode)
            {
                _grid.EndEdit();
            }

            var options = CreateComboOptions(cell);
            if (options.Count == 0)
            {
                return;
            }

            _grid.CurrentCell = cell;
            var cellBounds = _grid.GetCellDisplayRectangle(columnIndex, rowIndex, cutOverflow: true);
            ToolStripDropDown? dropDown = null;
            dropDown = UiDropDownPopup.Show(
                _grid,
                cellBounds,
                options,
                cell.Value,
                selected =>
                {
                    cell.Value = selected.Value;
                    _grid.InvalidateCell(cell);
                },
                closed: () =>
                {
                    if (ReferenceEquals(_comboDropDown, dropDown))
                    {
                        _comboDropDown = null;
                    }
                });
            _comboDropDown = dropDown;
        }

        private static List<UiDropDownOption> CreateComboOptions(DataGridViewComboBoxCell cell)
        {
            var dataSource = cell.DataSource
                ?? (cell.OwningColumn as DataGridViewComboBoxColumn)?.DataSource;
            if (dataSource is not System.Collections.IEnumerable source)
            {
                return [];
            }

            return source
                .Cast<object>()
                .Select(item => item switch
                {
                    FieldOption field => new UiDropDownOption(field.Key, field.Text),
                    ComparisonOption comparison => new UiDropDownOption(comparison.Kind, comparison.Text),
                    ValueOption value => new UiDropDownOption(value.Value, value.Text),
                    _ => new UiDropDownOption(item, item.ToString() ?? string.Empty)
                })
                .ToList();
        }

        private void CloseComboDropDown()
        {
            _comboDropDown?.Close(ToolStripDropDownCloseReason.AppClicked);
            _comboDropDown = null;
        }

        private void CloseValueDropDown()
        {
            _valueDropDown?.Close(ToolStripDropDownCloseReason.AppClicked);
            _valueDropDown = null;
        }

        private void ApplyColumnLayout()
        {
            ConfigureFixedColumn(NumberColumn, 42);
            ConfigureFixedColumn(EnabledColumn, 84);
            // 字段与值两列按剩余宽度均分，拉宽窗口时表格不再右侧留白。
            ConfigureFillColumn(FieldColumn, 220);
            ConfigureFixedColumn(ComparisonColumn, 144);
            ConfigureFillColumn(ValueColumn, 120);
            ConfigureFixedColumn(DeleteColumn, 84);

            _grid.ColumnHeadersHeight = Math.Max(38, _grid.Font.Height + 12);
            _grid.RowTemplate.Height = Math.Max(40, _grid.Font.Height + 14);
            foreach (DataGridViewRow row in _grid.Rows)
            {
                row.Height = _grid.RowTemplate.Height;
            }

            FitThreeConditionRows();
        }

        private void FitThreeConditionRows()
        {
            if (_grid.Columns.Count == 0)
            {
                return;
            }

            var height = Root.Padding.Vertical
                + (int)Math.Ceiling(Root.RowStyles[0].Height)
                + (int)Math.Ceiling(Root.RowStyles[2].Height)
                + _grid.Margin.Vertical
                + _grid.ColumnHeadersHeight
                + 3 * _grid.RowTemplate.Height
                + 2; // DataGridView 上下边框
            if (Root.Height != height)
            {
                Root.Height = height;
            }
        }

        private void ConfigureFixedColumn(string name, int width)
        {
            var column = _grid.Columns[name]
                ?? throw new InvalidOperationException($"找不到数量筛选列：{name}");
            var headerFont = _grid.ColumnHeadersDefaultCellStyle.Font ?? _grid.Font;
            var headerWidth = TextRenderer.MeasureText(
                column.HeaderText ?? string.Empty,
                headerFont,
                Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            width = Math.Max(
                width,
                headerWidth + _grid.ColumnHeadersDefaultCellStyle.Padding.Horizontal
                    + Math.Max(24, headerFont.Height));
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            column.MinimumWidth = width;
            column.Width = width;
        }

        private void ConfigureFillColumn(string name, int minimumWidth)
        {
            var column = _grid.Columns[name]
                ?? throw new InvalidOperationException($"找不到数量筛选列：{name}");
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            column.MinimumWidth = minimumWidth;
            column.FillWeight = name == FieldColumn ? 60 : 40;
        }

        private void RenumberRows()
        {
            for (var index = 0; index < _grid.Rows.Count; index++)
            {
                _grid.Rows[index].Cells[NumberColumn].Value = index + 1;
            }
        }

        private static DataGridViewComboBoxCell CreateValueComboCell(ValueOption[] options)
            => new()
            {
                DataSource = options.ToList(),
                DisplayMember = nameof(ValueOption.Text),
                ValueMember = nameof(ValueOption.Value),
                ValueType = typeof(int),
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                FlatStyle = FlatStyle.Flat
            };

        private static string FormatValue(ModuleCountCondition condition)
            => condition.ValueKind is CountConditionValueKind.StateField or CountConditionValueKind.NumberArray
                ? condition.ValueField ?? string.Empty
                : condition.Value.ToString();

        private static object DefaultValue(CountConditionFieldKind kind)
            => kind switch
            {
                CountConditionFieldKind.Role => 1,
                CountConditionFieldKind.Dispel => 1,
                CountConditionFieldKind.Class => 1,
                CountConditionFieldKind.Combat => 1,
                CountConditionFieldKind.ImprovedGarrote => 1,
                _ => "0"
            };

        private static bool TryReadInt(object? value, out int result)
        {
            if (value is int typed)
            {
                result = typed;
                return true;
            }

            return int.TryParse(value?.ToString(), out result);
        }

        private static CountConditionComparisonKind? ReadComparison(object? value)
        {
            if (value is CountConditionComparisonKind typed)
            {
                return typed;
            }

            return Enum.TryParse<CountConditionComparisonKind>(value?.ToString(), out var parsed)
                ? parsed
                : null;
        }

        private static FieldOption ParseFieldKey(string? key)
        {
            if (key?.StartsWith("aura:", StringComparison.Ordinal) == true
                && long.TryParse(key["aura:".Length..], out var spellId))
            {
                return new FieldOption(key, CountConditionFieldKind.Aura, spellId);
            }

            return Enum.TryParse<CountConditionFieldKind>(key, out var kind)
                ? new FieldOption(key ?? string.Empty, kind)
                : new FieldOption(string.Empty, CountConditionFieldKind.Health);
        }
    }

    private sealed record FieldOption(string Text, CountConditionFieldKind Kind, long? AuraSpellId = null)
    {
        public string Key => KeyFor(Kind, AuraSpellId);
        public static string KeyFor(CountConditionFieldKind kind, long? auraSpellId)
            => kind == CountConditionFieldKind.Aura ? $"aura:{auraSpellId.GetValueOrDefault()}" : kind.ToString();
        public override string ToString() => Text;
    }

    private sealed record ComparisonOption(string Text, CountConditionComparisonKind Kind)
    {
        public override string ToString() => Text;
    }

    private sealed record GroupModeOption(string Text, CountConditionGroupMode Mode)
    {
        public override string ToString() => Text;
    }

    private sealed record ValueOption(string Text, int Value)
    {
        public override string ToString() => Text;
    }

    // 数值条件的值可手填数字，也可从下拉中选择公式动态数值名称。
    // 编辑时只覆盖文字区域，右侧下拉按钮保持可见、可点。
    private sealed class FormulaValueCell : DataGridViewTextBoxCell
    {
        public override void PositionEditingControl(
            bool setLocation,
            bool setSize,
            Rectangle cellBounds,
            Rectangle cellClip,
            DataGridViewCellStyle cellStyle,
            bool singleVerticalBorderAdded,
            bool singleHorizontalBorderAdded,
            bool isFirstDisplayedColumn,
            bool isFirstDisplayedRow)
        {
            if (DataGridView is { } grid && cellBounds.Width > 0 && cellBounds.Height > 0)
            {
                var buttonBounds = UiTheme.GetDropDownButtonBounds(
                    grid,
                    new Rectangle(Point.Empty, cellBounds.Size));
                var editorWidth = Math.Max(1, buttonBounds.Left - UiTheme.Scale(grid, 4));
                cellBounds.Width = editorWidth;
                cellClip = Rectangle.Intersect(
                    cellClip,
                    new Rectangle(cellBounds.X, cellBounds.Y, editorWidth, cellBounds.Height));
            }

            base.PositionEditingControl(
                setLocation,
                setSize,
                cellBounds,
                cellClip,
                cellStyle,
                singleVerticalBorderAdded,
                singleHorizontalBorderAdded,
                isFirstDisplayedColumn,
                isFirstDisplayedRow);
        }
    }
}
