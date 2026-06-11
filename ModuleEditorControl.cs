using System.Drawing;

namespace Shigure;

public sealed class ModuleEditorControl : UserControl
{
    private readonly ModuleStore _moduleStore;
    private readonly Action _runtimeRestartRequested;
    private readonly ListBox _moduleList = new();
    private readonly TextBox _nameBox = new();
    private readonly CheckBox _enabledBox = new();
    private readonly TextBox _classBox = new();
    private readonly TextBox _specBox = new();
    private readonly TextBox _partyTypeBox = new();
    private readonly TextBox _heroTalentBox = new();
    private readonly DataGridView _rulesGrid = new();
    private readonly Label _pathLabel = new();
    private List<ModuleDefinition> _modules = new();
    private ModuleDefinition? _selectedModule;

    public ModuleEditorControl(ModuleStore moduleStore, Action runtimeRestartRequested)
    {
        _moduleStore = moduleStore;
        _runtimeRestartRequested = runtimeRestartRequested;
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
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 8,
            RowCount = 2,
            Margin = new Padding(0)
        };
        for (var i = 0; i < 4; i++)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }

        AddMatchField(row, "职业", _classBox, 0);
        AddMatchField(row, "专精", _specBox, 2);
        AddMatchField(row, "队伍类型", _partyTypeBox, 4);
        AddMatchField(row, "英雄天赋", _heroTalentBox, 6);
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
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Condition",
            HeaderText = "判断条件",
            FillWeight = 180
        });
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Unit",
            HeaderText = "单位",
            FillWeight = 48
        });
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Spell",
            HeaderText = "技能",
            FillWeight = 90
        });
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Hotkey",
            HeaderText = "按键",
            FillWeight = 70
        });
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Step",
            HeaderText = "步骤文本",
            FillWeight = 120
        });

        return _rulesGrid;
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
            Text = "条件示例: 生命值 < 50 && spells.圣疗术 == 0",
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
        _classBox.Text = FormatMatchValue(module.Match.ClassId);
        _specBox.Text = FormatMatchValue(module.Match.SpecId);
        _partyTypeBox.Text = FormatMatchValue(module.Match.PartyType);
        _heroTalentBox.Text = FormatMatchValue(module.Match.HeroTalent);
        _pathLabel.Text = module.FilePath ?? "尚未保存";
        _rulesGrid.Rows.Clear();

        foreach (var rule in module.Rules)
        {
            _rulesGrid.Rows.Add(rule.Enabled, rule.Condition, rule.Unit?.ToString() ?? string.Empty, rule.Spell, rule.Hotkey, rule.Step);
        }
    }

    private void ClearEditor()
    {
        _selectedModule = null;
        _nameBox.Clear();
        _enabledBox.Checked = false;
        _classBox.Clear();
        _specBox.Clear();
        _partyTypeBox.Clear();
        _heroTalentBox.Clear();
        _pathLabel.Text = "无模块";
        _rulesGrid.Rows.Clear();
    }

    private void AddModule()
    {
        var module = ModuleDefinition.CreateDefault();
        _moduleStore.Save(module);
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

        var saved = _moduleStore.Save(module);
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
            ClassId = ParseMatchBox(_classBox, "职业"),
            SpecId = ParseMatchBox(_specBox, "专精"),
            PartyType = ParseMatchBox(_partyTypeBox, "队伍类型"),
            HeroTalent = ParseMatchBox(_heroTalentBox, "英雄天赋")
        };

        if (_lastParseFailed)
        {
            _lastParseFailed = false;
            return false;
        }

        module.Rules = ReadRules();
        return true;
    }

    private bool _lastParseFailed;

    private int? ParseMatchBox(TextBox box, string label)
    {
        var text = box.Text.Trim();
        if (string.IsNullOrWhiteSpace(text) || text == "*")
        {
            return null;
        }

        if (int.TryParse(text, out var value))
        {
            return value;
        }

        MessageBox.Show($"{label} 必须是数字，留空或 * 表示任意。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        _lastParseFailed = true;
        return null;
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
            var hotkey = CellText(row, "Hotkey");
            var step = CellText(row, "Step");
            if (string.IsNullOrWhiteSpace(condition)
                && string.IsNullOrWhiteSpace(spell)
                && string.IsNullOrWhiteSpace(hotkey)
                && string.IsNullOrWhiteSpace(step))
            {
                continue;
            }

            rules.Add(new ModuleRule
            {
                Enabled = CellBool(row, "Enabled", defaultValue: true),
                Condition = condition,
                Unit = ParseNullableInt(CellText(row, "Unit")),
                Spell = spell,
                Hotkey = hotkey,
                Step = step
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

    private static void StyleTextBox(TextBox textBox)
    {
        textBox.BackColor = UiTheme.Field;
        textBox.ForeColor = UiTheme.Text;
        textBox.BorderStyle = BorderStyle.FixedSingle;
    }

    private static string FormatModuleListItem(ModuleDefinition module)
    {
        var enabled = module.Enabled ? "●" : "○";
        var match = $"{FormatMatchValue(module.Match.ClassId)}/{FormatMatchValue(module.Match.SpecId)}/{FormatMatchValue(module.Match.PartyType)}/{FormatMatchValue(module.Match.HeroTalent)}";
        return $"{enabled} {module.Name}  [{match}]";
    }

    private static string FormatMatchValue(int? value)
    {
        return value?.ToString() ?? "*";
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
}
