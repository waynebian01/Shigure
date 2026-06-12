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
    private readonly CheckBox _enabledBox = new();
    private readonly ComboBox _classBox = new();
    private readonly ComboBox _specBox = new();
    private readonly ComboBox _partyTypeBox = new();
    private readonly ComboBox _heroTalentBox = new();
    private readonly DataGridView _rulesGrid = new();
    private readonly DataGridViewComboBoxColumn _spellColumn = new();
    private readonly DataGridViewComboBoxColumn _unitColumn = new();
    private readonly Label _pathLabel = new();
    private List<ModuleDefinition> _modules = new();
    private ModuleDefinition? _selectedModule;
    private static readonly PartyTypeOption[] PartyTypeOptions =
    [
        new("任意 (*)", null),
        new("单人 (0)", "0"),
        new("团队 (1-40)", "1-40"),
        new("队伍 (46)", "46")
    ];
    private static readonly MatchOption[] ClassOptions = BuildClassOptions();

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
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
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
        _moduleList.BackColor = UiTheme.Background;
        _moduleList.ForeColor = UiTheme.Text;
        _moduleList.BorderStyle = BorderStyle.None;
        _moduleList.IntegralHeight = false;
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
            Padding = new Padding(8, 0, 0, 0),
            ColumnCount = 1,
            RowCount = 4
        };
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        editor.Controls.Add(BuildNameRow(), 0, 0);
        editor.Controls.Add(BuildMatchRow(), 0, 1);
        editor.Controls.Add(BuildRulesGrid(), 0, 2);
        editor.Controls.Add(BuildActionRow(), 0, 3);
        return editor;
    }

    private Control BuildNameRow()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 4,
            RowCount = 2,
            Margin = new Padding(0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        row.Controls.Add(CreateLabel("名称"), 0, 0);
        StyleTextBox(_nameBox);
        _nameBox.Dock = DockStyle.Fill;
        row.Controls.Add(_nameBox, 1, 0);

        _enabledBox.Text = "启用";
        _enabledBox.Checked = true;
        _enabledBox.Dock = DockStyle.Fill;
        _enabledBox.ForeColor = UiTheme.Text;
        _enabledBox.BackColor = UiTheme.Surface;
        row.Controls.Add(_enabledBox, 2, 0);

        _pathLabel.Dock = DockStyle.Fill;
        _pathLabel.ForeColor = UiTheme.Muted;
        _pathLabel.TextAlign = ContentAlignment.MiddleLeft;
        _pathLabel.AutoEllipsis = true;
        row.Controls.Add(_pathLabel, 0, 1);
        row.SetColumnSpan(_pathLabel, 4);

        return row;
    }

    private Control BuildMatchRow()
    {
        var matchLabels = new[] { "职业", "专精", "英雄天赋", "队伍类型" };

        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 8,
            RowCount = 2,
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
        };
        _specBox.SelectedIndexChanged += (_, _) =>
            ResetHeroTalentOptions(_heroTalentBox, ReadMatchCombo(_classBox), ReadMatchCombo(_specBox));

        AddMatchField(row, "职业:", _classBox, 0);
        AddMatchField(row, "专精:", _specBox, 2);
        AddMatchField(row, "英雄天赋:", _heroTalentBox, 4);
        AddMatchField(row, "队伍类型:", _partyTypeBox, 6);
        return row;
    }

    private Control BuildRulesGrid()
    {
        _rulesGrid.Dock = DockStyle.Fill;
        _rulesGrid.BackgroundColor = UiTheme.Surface;
        _rulesGrid.BorderStyle = BorderStyle.None;
        _rulesGrid.GridColor = UiTheme.Field;
        _rulesGrid.EnableHeadersVisualStyles = false;
        _rulesGrid.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.Field;
        _rulesGrid.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.Muted;
        _rulesGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor = UiTheme.Field;
        _rulesGrid.DefaultCellStyle.BackColor = UiTheme.Surface;
        _rulesGrid.DefaultCellStyle.ForeColor = UiTheme.Text;
        _rulesGrid.DefaultCellStyle.SelectionBackColor = UiTheme.Hover;
        _rulesGrid.DefaultCellStyle.SelectionForeColor = UiTheme.Text;
        _rulesGrid.RowHeadersVisible = false;
        _rulesGrid.AllowUserToAddRows = true;
        _rulesGrid.AllowUserToDeleteRows = true;
        _rulesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        _rulesGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Enabled",
            HeaderText = "启用",
            FillWeight = 38
        });
        _spellColumn.Name = "Spell";
        _spellColumn.HeaderText = "技能";
        _spellColumn.FillWeight = 120;
        _spellColumn.FlatStyle = FlatStyle.Flat;
        _rulesGrid.Columns.Add(_spellColumn);
        _unitColumn.Name = "Unit";
        _unitColumn.HeaderText = "目标";
        _unitColumn.FillWeight = 48;
        _unitColumn.FlatStyle = FlatStyle.Flat;
        _rulesGrid.Columns.Add(_unitColumn);
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Condition",
            HeaderText = "条件 (点击编辑)",
            FillWeight = 180,
            ReadOnly = true
        });
        _rulesGrid.CellClick += OnRulesGridCellClick;
        // 下拉单元格遇到不在选项里的值时不弹异常框, 由 RefreshKeymapColumns 负责补录旧值。
        _rulesGrid.DataError += (_, e) => e.ThrowException = false;
        RefreshKeymapColumns();

        return _rulesGrid;
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
        foreach (var spell in _keymapCatalog.GetSpells(classId))
        {
            _spellColumn.Items.Add(spell);
        }

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
            EnsureComboItem(_unitColumn, row.Cells["Unit"].Value);
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

    private void OnRulesGridCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        if (_rulesGrid.Columns[e.ColumnIndex].Name != "Condition")
        {
            return;
        }

        OpenConditionEditor(e.RowIndex);
    }

    private void OpenConditionEditor(int rowIndex)
    {
        var row = _rulesGrid.Rows[rowIndex];
        var current = row.IsNewRow ? string.Empty : CellText(row, "Condition");
        var fields = _fieldCatalog.GetFields(ReadMatchCombo(_classBox), ReadMatchCombo(_specBox));

        using var editor = new ConditionEditorForm(fields, current);
        if (editor.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        if (row.IsNewRow)
        {
            // 新行占位符不能直接赋值, 改为追加一行。
            if (!string.IsNullOrWhiteSpace(editor.ConditionText))
            {
                _rulesGrid.Rows.Add(true, string.Empty, string.Empty, editor.ConditionText);
            }

            return;
        }

        row.Cells["Condition"].Value = editor.ConditionText;
    }

    private Control BuildActionRow()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 2,
            RowCount = 1
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));

        var hint = new Label
        {
            Text = "技能/目标下拉来自当前职业的 keymap，留空表示默认；点击“条件”列打开可视化编辑器",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        row.Controls.Add(hint, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = UiTheme.Surface
        };

        var saveButton = UiTheme.CreateButton("保存", UiTheme.Accent, Color.Black);
        saveButton.Width = 72;
        saveButton.Height = 30;
        saveButton.Click += (_, _) => SaveSelectedModule();

        var deleteButton = UiTheme.CreateButton("删除", UiTheme.Field, UiTheme.Danger);
        deleteButton.Width = 72;
        deleteButton.Height = 30;
        deleteButton.Click += (_, _) => DeleteSelectedModule();

        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(deleteButton);
        row.Controls.Add(buttons, 1, 0);
        return row;
    }

    private void LoadModules()
    {
        _moduleStore.Reload();
        _modules = _moduleStore.GetModules().ToList();
        _moduleList.Items.Clear();
        foreach (var module in _modules)
        {
            _moduleList.Items.Add(FormatModuleListItem(module));
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
        _enabledBox.Checked = module.Enabled;
        SelectClass(module.Match.ClassId);
        SelectSpec(module.Match.SpecId);
        SelectPartyType(module.Match.PartyType);
        SelectHeroTalent(module.Match.HeroTalent);
        _pathLabel.Text = module.FilePath ?? "尚未保存";
        _rulesGrid.Rows.Clear();
        // SelectClass 只在选项变化时触发刷新, 这里显式刷新一次并补录旧值, 保证行值都在下拉选项里。
        RefreshKeymapColumns();

        foreach (var rule in module.Rules)
        {
            var unitText = rule.Unit?.ToString() ?? string.Empty;
            EnsureComboItem(_spellColumn, rule.Spell);
            EnsureComboItem(_unitColumn, unitText);
            _rulesGrid.Rows.Add(rule.Enabled, rule.Spell, unitText, rule.Condition);
        }
    }

    private void ClearEditor()
    {
        _selectedModule = null;
        _nameBox.Clear();
        _enabledBox.Checked = false;
        SelectClass(null);
        SelectSpec(null);
        SelectPartyType(null);
        SelectHeroTalent(null);
        _pathLabel.Text = "无模块";
        _rulesGrid.Rows.Clear();
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
        module.Enabled = _enabledBox.Checked;
        module.Match = new ModuleMatch
        {
            ClassId = ReadMatchCombo(_classBox),
            SpecId = ReadMatchCombo(_specBox),
            PartyType = ReadPartyTypeCombo(),
            HeroTalent = ReadMatchCombo(_heroTalentBox)
        };

        module.Rules = ReadRules();
        return true;
    }

    private List<ModuleRule> ReadRules()
    {
        var rules = new List<ModuleRule>();
        foreach (DataGridViewRow row in _rulesGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var condition = CellText(row, "Condition");
            var spell = CellText(row, "Spell");
            if (string.IsNullOrWhiteSpace(condition)
                && string.IsNullOrWhiteSpace(spell)
                && string.IsNullOrWhiteSpace(CellText(row, "Unit")))
            {
                continue;
            }

            rules.Add(new ModuleRule
            {
                Enabled = CellBool(row, "Enabled", defaultValue: true),
                Condition = condition,
                Unit = ParseNullableInt(CellText(row, "Unit")),
                Spell = spell,
                Hotkey = string.Empty,
                Step = string.Empty
            });
        }

        return rules;
    }

    private static void AddMatchField(TableLayoutPanel row, string label, TextBox box, int column)
    {
        row.Controls.Add(CreateLabel(label), column, 0);
        StyleTextBox(box);
        box.Dock = DockStyle.Fill;
        row.Controls.Add(box, column + 1, 0);
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

    private static int MeasureLabelColumnWidth(string text, Font font)
    {
        return TextRenderer.MeasureText(text, font).Width + 18;
    }

    private static void StyleTextBox(TextBox textBox)
    {
        textBox.BackColor = UiTheme.Field;
        textBox.ForeColor = UiTheme.Text;
        textBox.BorderStyle = BorderStyle.FixedSingle;
    }

    private static string FormatModuleListItem(ModuleDefinition module)
    {
        var enabled = module.Enabled ? "●" : "○";
        var match = $"{FormatMatchValue(module.Match.ClassId)}/{FormatMatchValue(module.Match.SpecId)}/{FormatPartyTypeValue(module.Match.PartyType)}/{FormatMatchValue(module.Match.HeroTalent)}";
        return $"{enabled} {module.Name}  [{match}]";
    }

    private static string FormatMatchValue(int? value)
    {
        return value?.ToString() ?? "*";
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

    private static string FormatPartyTypeValue(string? value)
    {
        return ModuleMatch.NormalizePartyTypeValue(value) switch
        {
            null => "*",
            "0" => "单人",
            "1-40" => "团队",
            "46" => "队伍",
            var other => other
        };
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
