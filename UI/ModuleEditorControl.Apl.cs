namespace Shigure;

public sealed partial class ModuleEditorControl
{
    private const string AplCallPrefix = "调用：";
    private const string AplRunPrefix = "运行：";
    private const int AplCardHeight = 48;
    private readonly List<ModuleSubLogic> _subLogics = [];
    private readonly Dictionary<string, List<RuleRowValues>> _aplDrafts = new(StringComparer.Ordinal);
    private readonly UiThemedFlowLayoutPanel _aplSidebar = new();
    private string _activeAplId = string.Empty;
    private bool _loadingAplRows;
    private bool _aplNameDialogOpen;
    private ModuleDefinition? _aplValidationGraph;

    private Control BuildAplRulesPanel()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, BackColor = UiTheme.SurfaceRaised
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.SizeChanged += (_, _) => layout.ColumnStyles[0].Width = UiTheme.Scale(layout, 160);
        layout.DpiChangedAfterParent += (_, _) => layout.ColumnStyles[0].Width = UiTheme.Scale(layout, 160);
        _aplSidebar.Dock = DockStyle.Fill;
        _aplSidebar.AutoScroll = true;
        _aplSidebar.WrapContents = false;
        _aplSidebar.FlowDirection = FlowDirection.TopDown;
        _aplSidebar.BackColor = UiTheme.SurfaceRaised;
        _aplSidebar.Margin = new Padding(0, 0, 8, 0);
        _aplSidebar.Resize += (_, _) => LayoutAplCards();
        layout.Controls.Add(_aplSidebar, 0, 0);
        layout.Controls.Add(BuildRulesGrid(), 1, 0);
        return layout;
    }

    private void LayoutAplCards()
    {
        var width = Math.Max(1, _aplSidebar.ClientSize.Width - UiTheme.Scale(_aplSidebar, 4)
            - (_aplSidebar.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0));
        foreach (Control card in _aplSidebar.Controls) card.Width = width;
    }

    private void RefreshAplCards()
    {
        _aplSidebar.SuspendLayout();
        foreach (var control in _aplSidebar.Controls.Cast<Control>().ToArray()) control.Dispose();
        AddAplCard("", "主逻辑");
        foreach (var logic in _subLogics) AddAplCard(logic.Id, logic.Name);
        var add = UiTheme.CreateButton("＋", UiTheme.ButtonKind.Secondary);
        add.AccessibleName = "新建次逻辑";
        add.AutoSize = false;
        add.Height = UiTheme.Scale(_aplSidebar, AplCardHeight);
        add.Margin = new Padding(0, 0, 0, UiTheme.Scale(_aplSidebar, 8));
        add.Enabled = _selectedModule is not null;
        add.Click += (_, _) => AddApl();
        _aplSidebar.Controls.Add(add);
        _aplSidebar.ResumeLayout();
        LayoutAplCards();
    }

    private void AddAplCard(string id, string name)
    {
        var selected = id == _activeAplId;
        var card = new UiCardPanel
        {
            Height = UiTheme.Scale(_aplSidebar, AplCardHeight), ColumnCount = 3, RowCount = 1,
            FillColor = selected ? UiTheme.AccentSoft : UiTheme.Field,
            Margin = new Padding(0, 0, 0, UiTheme.Scale(_aplSidebar, 8)),
            Padding = new Padding(UiTheme.Scale(_aplSidebar, 8), 0, UiTheme.Scale(_aplSidebar, 8), 0),
            Cursor = Cursors.Hand, Tag = id
        };
        var actionWidth = id.Length == 0 ? 0 : UiTheme.Scale(card, 32);
        // 左右保留相同宽度，使名称相对整张卡片居中。
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, actionWidth));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, actionWidth));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var label = new AplCardLabel
        {
            Text = name, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true, BackColor = Color.Transparent,
            ForeColor = selected ? UiTheme.Accent : UiTheme.Text, Margin = Padding.Empty,
            AccessibleName = name, TabStop = true
        };
        _pathToolTip.SetToolTip(label, id.Length == 0 ? "主逻辑：每轮判断的入口" : $"{name}；双击或按 F2 改名");
        label.Click += (_, _) => SelectApl(id);
        label.DoubleClick += (_, _) => { if (id.Length > 0) RenameApl(id); };
        label.PreviewKeyDown += (_, e) => e.IsInputKey = e.KeyCode is Keys.Enter or Keys.Space;
        label.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Enter or Keys.Space) { SelectApl(id); e.Handled = true; }
            if (e.KeyCode == Keys.F2 && id.Length > 0) { RenameApl(id); e.Handled = true; }
        };
        card.Controls.Add(label, 1, 0);
        card.Click += (_, _) => SelectApl(id);
        if (id.Length > 0)
        {
            var delete = UiTheme.CreateButton("×", UiTheme.Field, UiTheme.Danger);
            delete.Dock = DockStyle.Fill;
            delete.Margin = new Padding(0, UiTheme.Scale(card, 8), UiTheme.Scale(card, 4), UiTheme.Scale(card, 8));
            delete.Padding = Padding.Empty;
            delete.AccessibleName = $"删除次逻辑：{name}";
            delete.Click += (_, _) => DeleteApl(id);
            card.Controls.Add(delete, 2, 0);
        }
        _aplSidebar.Controls.Add(card);
    }

    private List<RuleRowValues> CurrentAplRows() => _rulesGrid.Rows.Cast<DataGridViewRow>()
        .Where(row => !row.IsNewRow).Select(ReadRuleRow).ToList();

    private void CaptureCurrentApl()
    {
        _rulesGrid.EndEdit();
        _aplDrafts[_activeAplId] = CurrentAplRows();
    }

    private object AplFingerprint() => new
    {
        Logics = _subLogics.Select(logic => new { logic.Id, logic.Name }).ToArray(),
        Rules = new[] { "" }.Concat(_subLogics.Select(logic => logic.Id)).Select(id => new
        {
            Id = id,
            Rows = id == _activeAplId ? CurrentAplRows() : _aplDrafts.GetValueOrDefault(id, [])
        }).ToArray()
    };

    private void SelectApl(string id)
    {
        if (_selectedModule is null || id == _activeAplId) return;
        CaptureCurrentApl();
        _activeAplId = id;
        ShowAplRows();
        RefreshAplSelection();
    }

    private void RefreshAplSelection()
    {
        foreach (var card in _aplSidebar.Controls.OfType<UiCardPanel>())
        {
            var selected = card.Tag as string == _activeAplId;
            card.FillColor = selected ? UiTheme.AccentSoft : UiTheme.Field;
            foreach (var label in card.Controls.OfType<AplCardLabel>())
                label.ForeColor = selected ? UiTheme.Accent : UiTheme.Text;
        }
    }

    private void ShowAplRows()
    {
        CloseRulesComboDropDown();
        HideRelaxedCommentEditors();
        _aplValidationGraph = null;
        _loadingAplRows = true;
        _rulesGrid.SuspendLayout();
        try
        {
            _rulesGrid.Rows.Clear();
            RefreshKeymapColumns();
            foreach (var values in _aplDrafts.GetValueOrDefault(_activeAplId, []))
            {
                var index = _rulesGrid.Rows.Add();
                WriteRuleRow(_rulesGrid.Rows[index], values);
                _rulesGrid.Rows[index].Cells["SpellIcon"].Value = GetRuleSpellIcon(values.Spell);
            }
        }
        finally
        {
            _loadingAplRows = false;
            _aplValidationGraph = null;
            _rulesGrid.ResumeLayout();
        }
        _rulesGrid.Invalidate();
        QueueRelaxedCommentSync();
    }

    private void LoadAplEditor(ModuleDefinition module)
    {
        _activeAplId = "";
        _subLogics.Clear();
        _subLogics.AddRange(module.SubLogics.Select(logic => logic.Clone()));
        _aplDrafts.Clear();
        _aplDrafts[""] = module.Rules.Select(ToAplRow).ToList();
        foreach (var logic in _subLogics) _aplDrafts[logic.Id] = logic.Rules.Select(ToAplRow).ToList();
        ShowAplRows();
        RefreshAplCards();
    }

    private RuleRowValues ToAplRow(ModuleRule rule)
    {
        var unit = ModuleSpecialActions.IsKeyPress(rule.Spell) ? rule.Hotkey
            : rule.UnitName ?? (rule.Unit is { } slot ? ReservedUnit.ToDisplayText(slot) : "");
        var target = _subLogics.FirstOrDefault(logic => logic.Id == rule.CallTargetId);
        var spell = ModuleApl.IsInvocation(rule)
            ? (ModuleApl.IsRun(rule) ? AplRunPrefix : AplCallPrefix)
                + (target?.Name ?? rule.CallTargetName ?? "未知次逻辑") + (target is null ? "（目标已删除）" : "")
            : DisplayRuleSpell(rule.Spell);
        return new(rule.Enabled, spell, unit, MacroConditionText.ToDisplayText(rule.MacroCondition),
            rule.Condition, rule.Comment, rule.SubConditions?.ToArray() ?? [], rule.DelayMs, rule.LogicDelayMs,
            rule.ContinueLogic, rule.MatchDelayMs, rule.HitDelayMs, rule.CallTargetId, rule.CallTargetName);
    }

    private ModuleDefinition AplGraph()
    {
        List<ModuleRule> Calls(string id) => (id == _activeAplId ? CurrentAplRows() : _aplDrafts.GetValueOrDefault(id, []))
            .Where(row => IsAplAction(row.Spell)).Select(row => new ModuleRule
            {
                Spell = AplStoredAction(row.Spell), CallTargetId = row.CallTargetId
            }).ToList();
        return new ModuleDefinition
        {
            Rules = Calls(""),
            SubLogics = _subLogics.Select(logic => new ModuleSubLogic
                { Id = logic.Id, Name = logic.Name, Rules = Calls(logic.Id) }).ToList()
        };
    }

    private static bool IsAplRun(string? spell) => spell?.StartsWith(AplRunPrefix, StringComparison.Ordinal) == true;
    private static bool IsAplAction(string? spell) => IsAplRun(spell)
        || spell?.StartsWith(AplCallPrefix, StringComparison.Ordinal) == true;
    private static string AplActionPrefix(string spell) => IsAplRun(spell) ? AplRunPrefix : AplCallPrefix;
    private static string AplStoredAction(string spell) => IsAplRun(spell) ? ModuleApl.RunAction : ModuleApl.CallAction;

    private string? AplRowError(DataGridViewRow row) => IsAplAction(CellText(row, "Spell"))
        ? ModuleApl.CallError(_aplValidationGraph ??= AplGraph(), _activeAplId, GetRuleMetadata(row).CallTargetId) : null;

    private void UpdateAplAction(DataGridViewRow row)
    {
        if (_loadingAplRows) return;
        var metadata = GetRuleMetadata(row);
        var spell = CellText(row, "Spell");
        if (IsAplAction(spell))
        {
            var target = _subLogics.FirstOrDefault(logic => AplActionPrefix(spell) + logic.Name == spell);
            metadata.CallTargetId = target?.Id ?? metadata.CallTargetId;
            metadata.CallTargetName = target?.Name ?? metadata.CallTargetName;
            metadata.DelayMs = metadata.LogicDelayMs = metadata.HitDelayMs = metadata.MatchDelayMs = null;
            metadata.ContinueLogic = null;
        }
        else metadata.CallTargetId = metadata.CallTargetName = null;
        row.Tag = metadata;
    }

    private void AddApl()
    {
        if (_selectedModule is null) return;
        var n = 1;
        while (_subLogics.Any(logic => logic.Name == $"次逻辑 {n}")) n++;
        var name = PromptAplName($"次逻辑 {n}", null);
        if (name is null) return;
        CaptureCurrentApl();
        var logic = new ModuleSubLogic { Name = name };
        _subLogics.Add(logic);
        _aplDrafts[logic.Id] = [];
        _activeAplId = logic.Id;
        ShowAplRows();
        RefreshAplCards();
    }

    private void RenameApl(string id)
    {
        var logic = _subLogics.First(item => item.Id == id);
        var name = PromptAplName(logic.Name, id);
        if (name is null || name == logic.Name) return;
        RenameAplReferences(id, name);
    }

    private void RenameAplReferences(string id, string name)
    {
        CaptureCurrentApl();
        var logic = _subLogics.First(item => item.Id == id);
        logic.Name = name;
        foreach (var rows in _aplDrafts.Values)
            for (var i = 0; i < rows.Count; i++)
                if (IsAplAction(rows[i].Spell) && rows[i].CallTargetId == id)
                    rows[i] = rows[i] with { Spell = AplActionPrefix(rows[i].Spell) + name, CallTargetName = name };
        ShowAplRows();
        RefreshAplCards();
    }

    private void DeleteApl(string id)
    {
        CaptureCurrentApl();
        var logic = _subLogics.First(item => item.Id == id);
        var count = _aplDrafts.Where(pair => pair.Key != id).SelectMany(pair => pair.Value)
            .Count(row => IsAplAction(row.Spell) && row.CallTargetId == id);
        if (MessageBox.Show(FindForm(), $"删除次逻辑“{logic.Name}”？\n共有 {count} 条引用规则，将保留条件并禁用。",
                "删除次逻辑", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
            != DialogResult.Yes) return;
        RemoveApl(id);
    }

    private void RemoveApl(string id)
    {
        CaptureCurrentApl();
        var logic = _subLogics.First(item => item.Id == id);
        _subLogics.Remove(logic);
        _aplDrafts.Remove(id);
        foreach (var rows in _aplDrafts.Values)
            for (var i = 0; i < rows.Count; i++)
                if (IsAplAction(rows[i].Spell) && rows[i].CallTargetId == id)
                    rows[i] = rows[i] with { Enabled = false, Spell = AplActionPrefix(rows[i].Spell) + logic.Name + "（目标已删除）" };
        if (_activeAplId == id) _activeAplId = "";
        ShowAplRows();
        RefreshAplCards();
    }

    private string? PromptAplName(string current, string? ownId)
    {
        if (_aplNameDialogOpen) return null;
        _aplNameDialogOpen = true;
        try
        {
            using var dialog = new AplNameEditorForm(current, name =>
                name == "主逻辑" || _subLogics.Any(logic => logic.Id != ownId && logic.Name == name));
            return dialog.ShowDialog(FindForm()) == DialogResult.OK ? dialog.LogicName : null;
        }
        finally { _aplNameDialogOpen = false; }
    }

    private sealed class AplCardLabel : Label
    {
        public AplCardLabel() => SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick
            | ControlStyles.StandardDoubleClick, true);

        protected override void OnPaint(PaintEventArgs e)
        {
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, ForeColor, BackColor);
        }
    }

    private bool TryReadAplRules(ModuleDefinition module, out string error)
    {
        CaptureCurrentApl();
        module.SubLogics = _subLogics.Select(logic => logic.Clone()).ToList();
        foreach (var id in new[] { "" }.Concat(_subLogics.Select(logic => logic.Id)))
        {
            var name = id.Length == 0 ? "主逻辑" : _subLogics.First(logic => logic.Id == id).Name;
            if (!TryReadRules(_aplDrafts.GetValueOrDefault(id, []), out var rules, out error))
            {
                error = $"{name}：{error}";
                return false;
            }
            if (id.Length == 0) module.Rules = rules;
            else module.SubLogics.First(logic => logic.Id == id).Rules = rules;
        }
        error = ModuleApl.ValidationError(module) ?? string.Empty;
        return error.Length == 0;
    }
}

internal sealed class AplNameEditorForm : Form
{
    public string LogicName { get; private set; } = string.Empty;

    public AplNameEditorForm(string current, Func<string, bool> isTaken)
    {
        Text = "次逻辑名称";
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(360, 144);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.Text;
        var box = new UiThemedTextBox { Text = current, Dock = DockStyle.Fill };
        UiTheme.StyleTextBox(box);
        var ok = UiTheme.CreateButton("确定", UiTheme.ButtonKind.Primary);
        var cancel = UiTheme.CreateButton("取消", UiTheme.ButtonKind.Secondary);
        cancel.DialogResult = DialogResult.Cancel;
        ok.Click += (_, _) =>
        {
            var name = box.Text.Trim();
            if (name.Length == 0 || isTaken(name))
            {
                MessageBox.Show(this, "名称不能为空、重复或使用“主逻辑”。", "次逻辑名称",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                box.Focus();
                return;
            }
            LogicName = name;
            DialogResult = DialogResult.OK;
        };
        AcceptButton = ok;
        CancelButton = cancel;
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label { Text = "名称", ForeColor = UiTheme.Muted, Dock = DockStyle.Fill }, 0, 0);
        root.Controls.Add(box, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        Shown += (_, _) => { box.Focus(); box.SelectAll(); };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UiTheme.ApplyDarkTitleBar(this);
    }
}
