using System.Drawing;

namespace Shigure;

/// <summary>
/// 队友单位、数量与平均血量字段的编辑弹窗：按类别动态显隐筛选控件。
/// 队友单位、队友/敌人数量与平均血量均共用 <see cref="CountFilterEditorControl"/>。
/// </summary>
public sealed class UnitEditorForm : Form
{
    private const int RowWidth = 800;
    private const int LegacyClientWidth = RowWidth + 36;
    private const int DefaultClientWidth = 960;
    private const int DefaultClientHeight = 760;
    private const int LabelWidth = 136;
    // 页头双栏（类别/统计对象、名称/值名称）：等宽标签列 + 百分比输入列，随窗体拉宽。
    private const int FieldHeight = 36;
    private const int ParamRowHeight = FieldHeight + 16;

    private static readonly TargetFieldItem[] TargetFieldOptions =
    [
        new("生命值", UnitTargetFieldKind.Health),
        new("治疗吸收", UnitTargetFieldKind.HealingAbsorb),
        new("职责", UnitTargetFieldKind.Role),
        new("驱散", UnitTargetFieldKind.Dispel),
        new("光环", UnitTargetFieldKind.Aura)
    ];

    private static readonly SelectionModeItem[] HealthSelectionModes =
    [
        new("最低", UnitSelectionMode.Lowest),
        new("最高", UnitSelectionMode.Highest)
    ];

    private static readonly SelectionModeItem[] OrderSelectionModes =
    [
        new("正序", UnitSelectionMode.Ascending),
        new("倒序", UnitSelectionMode.Descending)
    ];

    private static readonly SelectionModeItem[] AuraSelectionModes =
    [
        new("最长", UnitSelectionMode.Longest),
        new("最短", UnitSelectionMode.Shortest),
        new("正序", UnitSelectionMode.Ascending),
        new("倒序", UnitSelectionMode.Descending)
    ];

    private static readonly AverageTargetItem[] AverageTargetOptions =
    [
        new("队友", AverageHealthTargetKind.Allies),
        new("敌人", AverageHealthTargetKind.Enemies)
    ];

    private readonly IReadOnlyList<ConditionField> _auraFields;
    private readonly IReadOnlyList<ConditionField> _nameplateAuraFields;
    private readonly HashSet<string> _takenNames;
    private readonly CountFilterEditorControl _countFilterEditor;

    private readonly Label _valueNameLabel = new();
    private readonly TextBox _nameBox = new UiThemedTextBox();
    private readonly TextBox _valueNameBox = new UiThemedTextBox();
    private readonly UiDropDown _categoryBox = new();
    private readonly UiDropDown _selectorBox = new();
    private readonly UiDropDown _targetFieldBox = new();
    private readonly UiDropDown _selectionModeBox = new();
    private readonly UiDropDown _targetAuraBox = new();
    private readonly TableLayoutPanel _paramPanel = new();
    private readonly Label _previewLabel = new();
    private readonly ToolTip _toolTip = new();
    private Button? _okButton;
    private TableLayoutPanel _rootPanel = null!;

    private Label _selectorLabel = null!;
    private TableLayoutPanel _categoryRow = null!;
    private TableLayoutPanel _nameRow = null!;
    private TableLayoutPanel _unitTargetRow = null!;
    private TableLayoutPanel _targetAuraRow = null!;
    private UiCardPanel _countFilterSection = null!;
    private int _labelColumnWidth = LabelWidth;

    public ModuleUnit? ResultUnit { get; private set; }
    public ModuleCountField? ResultCount { get; private set; }
    public ModuleEnemyCountField? ResultEnemyCount { get; private set; }
    public ModuleAverageHealthField? ResultAverageHealth { get; private set; }

    public UnitEditorForm(
        IReadOnlyList<ConditionField> auraFields,
        IReadOnlyList<ConditionField> nameplateAuraFields,
        IReadOnlyList<string> thresholdFields,
        IReadOnlyList<string> formulaValueNames,
        IReadOnlyCollection<string> takenNames,
        ModuleUnit? existingUnit,
        ModuleCountField? existingCount,
        ModuleEnemyCountField? existingEnemyCount,
        ModuleAverageHealthField? existingAverageHealth = null,
        bool hasNameplateImprovedGarrote = false,
        bool hasNameplateThreat = false,
        bool hasNameplateCastSpell = false,
        IReadOnlyList<string>? numberArrayNames = null)
    {
        _auraFields = auraFields;
        _nameplateAuraFields = nameplateAuraFields;
        _takenNames = new HashSet<string>(takenNames, StringComparer.OrdinalIgnoreCase);
        _countFilterEditor = new CountFilterEditorControl(
            auraFields,
            nameplateAuraFields,
            thresholdFields,
            formulaValueNames,
            hasNameplateImprovedGarrote,
            hasNameplateThreat,
            hasNameplateCastSpell,
            numberArrayNames);
        _countFilterEditor.Changed += (_, _) => UpdatePreview();
        InitializeComponent();
        Seed(existingUnit, existingCount, existingEnemyCount, existingAverageHealth);
        UpdateParamVisibility();
        UpdateValueNameState();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UiTheme.ApplyDarkTitleBar(this);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var defaultWidth = Width;
        var cache = UiCacheStore.Load();
        var cached = cache.UnitEditorWindowSize;
        // 旧版默认高度为 1200：只迁移与旧默认值相近的缓存，保留用户手动调整的尺寸。
        if (cache.UnitEditorLayoutVersion == 0)
        {
            var chromeHeight = Math.Max(0, Height - ClientSize.Height);
            if (cached is { Height: > 0 }
                && Math.Abs(cached.Height - (1200 + chromeHeight)) <= 24)
            {
                cached = new WindowSize
                {
                    Width = cached.Width,
                    Height = DefaultClientHeight + chromeHeight
                };
                cache.UnitEditorWindowSize = cached;
            }

            cache.UnitEditorLayoutVersion = 1;
            UiCacheStore.Save(cache);
        }
        // 旧版本把宽度锁在最小宽度上。那种缓存改用加宽后的默认宽度，之后的拖动结果仍会记住。
        if (cached is { Width: > 0 } && cached.Width <= MinimumSize.Width + 2)
        {
            cached = new WindowSize
            {
                Width = Math.Max(cached.Width, defaultWidth),
                Height = cached.Height
            };
        }

        UiTheme.RestoreCachedDialogPlacement(
            this,
            cached,
            cache.UnitEditorWindowLocation);
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        _countFilterEditor.FinishResize();
        UiTheme.SaveCachedDialogPlacement(
            this,
            (c, size) => c.UnitEditorWindowSize = size,
            (c, location) => c.UnitEditorWindowLocation = location);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        UiTheme.SaveCachedDialogPlacement(
            this,
            (c, size) => c.UnitEditorWindowSize = size,
            (c, location) => c.UnitEditorWindowLocation = location);
        base.OnFormClosed(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UpdateLabelColumnWidths();
        UpdatePreviewRowHeight();
        ApplyParamRowStyles();
        _nameBox.Focus();
        _nameBox.SelectAll();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        UpdateLabelColumnWidths();
        UpdatePreviewRowHeight();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        UpdateLabelColumnWidths();
        UpdatePreviewRowHeight();
    }

    private void UpdatePreviewRowHeight()
    {
        if (_rootPanel is not null)
        {
            _rootPanel.RowStyles[2].Height = Math.Max(
                _rootPanel.RowStyles[2].Height,
                Font.Height + 30);
        }
    }

    private void UpdateLabelColumnWidths()
    {
        if (_unitTargetRow is null)
        {
            return;
        }

        // 按当前字体和 DPI 留足中文标签的宽度；只在显示或 DPI 改变时重新布局。
        var measured = TextRenderer.MeasureText(
            "查找的单位",
            Font,
            Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        var width = Math.Max(LabelWidth, measured + UiTheme.Scale(this, 24));
        if (_labelColumnWidth == width)
        {
            return;
        }

        _labelColumnWidth = width;
        _unitTargetRow.ColumnStyles[0].Width = width;
        _unitTargetRow.ColumnStyles[2].Width = width;
        _targetAuraRow.ColumnStyles[0].Width = width;
        ApplyHeaderRowLayout(_categoryRow, split: IsAverageHealthCategory);
        ApplyHeaderRowLayout(_nameRow, split: IsUnitCategory);
    }

    private void InitializeComponent()
    {
        Text = "编辑单位与统计";
        Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.Text;
        // 默认展示完整编辑流程，避免筛选区在初次打开时留下大块空白。
        UiTheme.ConfigureResizableDialog(this, DefaultClientWidth, DefaultClientHeight, LegacyClientWidth, 600);
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding = new Padding(UiTheme.CardPadding, 12, UiTheme.CardPadding, 12),
            ColumnCount = 1,
            RowCount = 4
        };
        _rootPanel = root;
        // 页头两行固定行高，避免 Percent 50/50 随窗体拉伸。
        const int headerRowHeight = 58;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,
            headerRowHeight * 2 + 16 + UiTheme.PageGap));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        Controls.Add(root);

        UiTheme.StyleComboBox(_categoryBox);
        _categoryBox.DropDownWidth = 180;
        _categoryBox.Items.AddRange(["队友单位", "队友数量", "敌人数量", "平均血量"]);
        _categoryBox.SelectedIndex = 0;
        _categoryBox.SelectedIndexChanged += (_, _) =>
        {
            PopulateSelectors();
            UpdateParamVisibility();
            UpdateValueNameState();
        };

        UiTheme.StyleComboBox(_selectorBox);
        _selectorBox.DropDownWidth = 360;
        // 平均血量切换「统计对象」时刷新敌人/队友条件字段与预览。
        _selectorBox.SelectedIndexChanged += (_, _) => UpdateParamVisibility();

        var headerCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding = new Padding(12, 8, 12, 8),
            Margin = new Padding(0, 0, 0, UiTheme.PageGap),
            ColumnCount = 1,
            RowCount = 2
        };
        headerCard.RowStyles.Add(new RowStyle(SizeType.Absolute, headerRowHeight));
        headerCard.RowStyles.Add(new RowStyle(SizeType.Absolute, headerRowHeight));
        // 类别单独一行；平均血量时右侧显示「统计对象」。
        headerCard.Controls.Add(BuildSplitRow("类别", _categoryBox, "选择器", _selectorBox), 0, 0);
        headerCard.Controls.Add(BuildNameRow(), 0, 1);
        root.Controls.Add(headerCard, 0, 0);

        _paramPanel.Dock = DockStyle.Fill;
        _paramPanel.BackColor = UiTheme.SurfaceRaised;
        _paramPanel.ColumnCount = 1;
        _paramPanel.RowCount = 3;
        _paramPanel.Margin = new Padding(0);
        _paramPanel.Padding = new Padding(8, 10, 8, 8);
        _paramPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        BuildParamRows();
        var paramsCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding = new Padding(4),
            Margin = new Padding(0, 0, 0, UiTheme.PageGap),
            ColumnCount = 1,
            RowCount = 1
        };
        paramsCard.Controls.Add(_paramPanel, 0, 0);
        root.Controls.Add(paramsCard, 0, 1);

        _previewLabel.Dock = DockStyle.Fill;
        _previewLabel.ForeColor = UiTheme.Muted;
        _previewLabel.TextAlign = ContentAlignment.MiddleLeft;
        _previewLabel.AutoEllipsis = true;
        _previewLabel.Margin = new Padding(0);
        var previewCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding = new Padding(UiTheme.CardPadding, 6, UiTheme.CardPadding, 6),
            Margin = new Padding(0, 0, 0, UiTheme.PageGap),
            ColumnCount = 1,
            RowCount = 1
        };
        previewCard.Controls.Add(_previewLabel, 0, 0);
        root.Controls.Add(previewCard, 0, 2);

        root.Controls.Add(BuildActionRow(), 0, 3);

        PopulateSelectors();
        PopulateTargetFieldOptions();
        PopulateTargetAuras();
    }

    private void BuildParamRows()
    {
        UiTheme.StyleComboBox(_targetFieldBox);
        _targetFieldBox.DropDownWidth = 220;
        _targetFieldBox.SelectedIndexChanged += (_, _) =>
        {
            PopulateSelectionModes(preserveSelection: false);
            UpdateParamVisibility();
        };

        UiTheme.StyleComboBox(_selectionModeBox);
        _selectionModeBox.DropDownWidth = 180;
        _selectionModeBox.SelectedIndexChanged += (_, _) => UpdatePreview();

        UiTheme.StyleComboBox(_targetAuraBox);
        _targetAuraBox.DropDownWidth = 360;
        _targetAuraBox.SelectedIndexChanged += (_, _) => UpdatePreview();

        _unitTargetRow = BuildParamSplitRow("查找的单位", _targetFieldBox, "目标选择", _selectionModeBox);
        _targetAuraRow = BuildLabeledRow("目标光环", _targetAuraBox);
        _countFilterSection = BuildFilterSection("列表筛选", _countFilterEditor);

        _paramPanel.Controls.Add(_unitTargetRow, 0, 0);
        _paramPanel.Controls.Add(_targetAuraRow, 0, 1);
        _paramPanel.Controls.Add(_countFilterSection, 0, 2);
        ApplyParamRowStyles();
    }

    private void ApplyParamRowStyles()
    {
        // 隐藏行压成 0 高，可见的目标行固定高度，筛选区吃掉剩余高度。
        _paramPanel.RowStyles.Clear();
        _paramPanel.RowStyles.Add(new RowStyle(
            SizeType.Absolute,
            _unitTargetRow.Visible ? ParamRowHeight + 8 : 0));
        _paramPanel.RowStyles.Add(new RowStyle(
            SizeType.Absolute,
            _targetAuraRow.Visible ? ParamRowHeight + 8 : 0));
        _paramPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    }

    private void PopulateTargetAuras()
    {
        var selected = TryReadAuraSpellId(_targetAuraBox.SelectedItem, out var selectedId)
            ? selectedId
            : (long?)null;
        _targetAuraBox.BeginUpdate();
        try
        {
            _targetAuraBox.Items.Clear();
            foreach (var aura in _auraFields)
            {
                _targetAuraBox.Items.Add(aura);
            }

            if (selected is { } id)
            {
                SelectAura(_targetAuraBox, id);
            }
            else if (_targetAuraBox.Items.Count > 0)
            {
                _targetAuraBox.SelectedIndex = 0;
            }
        }
        finally
        {
            _targetAuraBox.EndUpdate();
        }
    }

    private static UiCardPanel BuildFilterSection(string title, Control editor)
    {
        var section = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            FillColor = UiTheme.Surface,
            Padding = new Padding(12, 10, 12, 12),
            Margin = new Padding(0, 4, 0, 0),
            ColumnCount = 1,
            RowCount = 2
        };
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        section.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var titleLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            ForeColor = UiTheme.Text,
            Text = title,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0),
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(0)
        };

        editor.Dock = DockStyle.Fill;
        editor.Margin = new Padding(0);

        section.Controls.Add(titleLabel, 0, 0);
        section.Controls.Add(editor, 0, 1);
        return section;
    }

    private Control BuildActionRow()
    {
        var row = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding = new Padding(UiTheme.CardPadding, 10, UiTheme.CardPadding, 10),
            Margin = new Padding(0),
            ColumnCount = 2,
            RowCount = 1
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 184));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };

        _okButton = UiTheme.CreateButton("确定", UiTheme.ButtonKind.Primary);
        UiTheme.StyleActionButton(_okButton, 84);
        _okButton.Margin = new Padding(8, 0, 0, 0);
        _okButton.Click += (_, _) => OnConfirm();

        var cancelButton = UiTheme.CreateButton("取消", UiTheme.ButtonKind.Secondary);
        UiTheme.StyleActionButton(cancelButton, 84);
        cancelButton.Margin = new Padding(8, 0, 0, 0);
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;

        actions.Controls.Add(_okButton);
        actions.Controls.Add(cancelButton);
        row.Controls.Add(actions, 1, 0);

        void FitActionRow()
        {
            foreach (var button in new[] { _okButton, cancelButton })
            {
                var textSize = TextRenderer.MeasureText(
                    button.Text,
                    button.Font,
                    Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                button.Size = new Size(
                    Math.Max(84, textSize.Width + button.Padding.Horizontal + Math.Max(40, button.Font.Height + 8)),
                    Math.Max(40, textSize.Height + button.Padding.Vertical + UiTheme.Scale(button, 8)));
            }

            row.ColumnStyles[1].Width = Math.Max(184, _okButton.Width + cancelButton.Width + 16);
            if (row.Parent is TableLayoutPanel root)
            {
                root.RowStyles[3].Height = Math.Max(56, Math.Max(_okButton.Height, cancelButton.Height) + 20);
            }
        }

        _okButton.HandleCreated += (_, _) => FitActionRow();
        _okButton.FontChanged += (_, _) => FitActionRow();
        cancelButton.HandleCreated += (_, _) => FitActionRow();
        cancelButton.FontChanged += (_, _) => FitActionRow();
        FitActionRow();
        AcceptButton = _okButton;
        CancelButton = cancelButton;
        return row;
    }

    private void PopulateSelectors()
    {
        _selectorBox.Items.Clear();
        // 只有平均血量需要在页头选择统计对象。
        var hasSelector = IsAverageHealthCategory;
        _selectorBox.Visible = hasSelector;
        if (_selectorLabel is not null)
        {
            _selectorLabel.Visible = hasSelector;
            _selectorLabel.Text = "统计对象";
        }

        ApplyHeaderRowLayout(_categoryRow, split: hasSelector);

        if (!hasSelector)
        {
            return;
        }

        _selectorBox.Items.AddRange(AverageTargetOptions.Cast<object>().ToArray());
        if (_selectorBox.Items.Count > 0)
        {
            _selectorBox.SelectedIndex = 0;
        }
    }

    private void PopulateTargetFieldOptions()
    {
        _targetFieldBox.Items.Clear();
        _targetFieldBox.Items.AddRange(TargetFieldOptions.Cast<object>().ToArray());
        _targetFieldBox.SelectedIndex = 0;
        PopulateSelectionModes(preserveSelection: false);
    }

    private void PopulateSelectionModes(bool preserveSelection)
    {
        var previous = SelectedSelectionMode();
        var options = SelectionModesFor(SelectedTargetField());
        _selectionModeBox.BeginUpdate();
        try
        {
            _selectionModeBox.Items.Clear();
            _selectionModeBox.Items.AddRange(options.Cast<object>().ToArray());
            if (preserveSelection)
            {
                SelectSelectionMode(previous);
            }
            else if (_selectionModeBox.Items.Count > 0)
            {
                _selectionModeBox.SelectedIndex = 0;
            }
        }
        finally
        {
            _selectionModeBox.EndUpdate();
        }
    }

    private static SelectionModeItem[] SelectionModesFor(UnitTargetFieldKind field)
        => field switch
        {
            UnitTargetFieldKind.Health or UnitTargetFieldKind.HealingAbsorb => HealthSelectionModes,
            UnitTargetFieldKind.Role or UnitTargetFieldKind.Dispel => OrderSelectionModes,
            UnitTargetFieldKind.Aura => AuraSelectionModes,
            _ => HealthSelectionModes
        };

    // 值名称始终对队友单位可用：导出所选单位的目标字段值。
    private void UpdateValueNameState()
    {
        var visible = IsUnitCategory;
        _valueNameLabel.Visible = visible;
        _valueNameBox.Visible = visible;
        _valueNameBox.Enabled = visible;
        // 仅队友单位显示双栏；其它类别名称独占整行，与类别下拉的单栏布局一致。
        ApplyHeaderRowLayout(_nameRow, split: visible);
        if (!visible)
        {
            _valueNameBox.Text = string.Empty;
        }
    }

    private void UpdateParamVisibility()
    {
        var enemyTarget = IsEnemyCountCategory
            || (IsAverageHealthCategory && SelectedAverageTarget() == AverageHealthTargetKind.Enemies);
        _countFilterEditor.SetEnemyTarget(enemyTarget);
        _countFilterSection.Visible = true;
        _unitTargetRow.Visible = IsUnitCategory;
        _targetAuraRow.Visible = IsUnitCategory && SelectedTargetField() == UnitTargetFieldKind.Aura;
        ApplyParamRowStyles();

        var hasSelector = IsAverageHealthCategory;
        _selectorBox.Visible = hasSelector;
        if (_selectorLabel is not null)
        {
            _selectorLabel.Visible = hasSelector;
        }

        ApplyHeaderRowLayout(_categoryRow, split: hasSelector);
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if ((IsUnitCategory || IsCountCategory || IsEnemyCountCategory || IsAverageHealthCategory)
            && !_countFilterEditor.TryValidate(out var filterMessage))
        {
            _previewLabel.ForeColor = UiTheme.Danger;
            _previewLabel.Text = $"预览: {filterMessage}";
            if (_okButton is not null)
            {
                _okButton.Enabled = false;
            }

            return;
        }

        _previewLabel.ForeColor = UiTheme.Muted;
        if (_okButton is not null)
        {
            _okButton.Enabled = true;
        }

        var text = BuildPreviewText();
        _previewLabel.Text = string.IsNullOrEmpty(text) ? "预览: -" : $"预览: {text}";
    }

    // 用当前控件状态构造一个宽容的(不校验、不弹框)单位/数量, 复用 UnitSummary 渲染预览。
    private string BuildPreviewText()
    {
        if (IsAverageHealthCategory)
        {
            var field = BuildAverageHealth(_nameBox.Text.Trim());
            Func<long, string?> resolver = field.Target == AverageHealthTargetKind.Enemies
                ? ResolveNameplateAuraName
                : ResolveAuraName;
            return UnitSummary.Describe(field, resolver);
        }

        if (IsEnemyCountCategory)
        {
            return UnitSummary.Describe(BuildEnemyCount(_nameBox.Text.Trim()), ResolveNameplateAuraName);
        }

        if (IsCountCategory)
        {
            return UnitSummary.Describe(BuildAllyCount(_nameBox.Text.Trim()), ResolveAuraName);
        }

        return UnitSummary.Describe(BuildFilteredUnit(_nameBox.Text.Trim()), ResolveAuraName);
    }

    private ModuleEnemyCountField BuildEnemyCount(string name)
    {
        return new ModuleEnemyCountField
        {
            Name = name,
            FilterGroups = _countFilterEditor.ReadGroups()
        };
    }

    private ModuleCountField BuildAllyCount(string name)
    {
        return new ModuleCountField
        {
            Name = name,
            FilterGroups = _countFilterEditor.ReadGroups()
        };
    }

    private ModuleUnit BuildFilteredUnit(string name)
    {
        var targetField = SelectedTargetField();
        return new ModuleUnit
        {
            Name = name,
            ValueName = IsUnitCategory
                ? (string.IsNullOrWhiteSpace(_valueNameBox.Text) ? null : _valueNameBox.Text.Trim())
                : null,
            FilterGroups = _countFilterEditor.ReadGroups(),
            TargetField = targetField,
            SelectionMode = SelectedSelectionMode(),
            TargetAuraSpellId = targetField == UnitTargetFieldKind.Aura
                ? (TryReadAuraSpellId(_targetAuraBox.SelectedItem, out var auraId) ? auraId : null)
                : null
        };
    }

    private ModuleAverageHealthField BuildAverageHealth(string name)
    {
        return new ModuleAverageHealthField
        {
            Name = name,
            Target = SelectedAverageTarget(),
            FilterGroups = _countFilterEditor.ReadGroups()
        };
    }

    private void Seed(
        ModuleUnit? unit,
        ModuleCountField? count,
        ModuleEnemyCountField? enemyCount,
        ModuleAverageHealthField? averageHealth)
    {
        if (averageHealth is not null)
        {
            _nameBox.Text = averageHealth.Name;
            _categoryBox.SelectedIndex = 3;
            PopulateSelectors();
            SelectAverageTarget(averageHealth.Target);
            _countFilterEditor.SetEnemyTarget(averageHealth.Target == AverageHealthTargetKind.Enemies);
            _countFilterEditor.LoadGroups(averageHealth.FilterGroups);
            return;
        }

        if (enemyCount is not null)
        {
            _nameBox.Text = enemyCount.Name;
            _categoryBox.SelectedIndex = 2;
            PopulateSelectors();
            _countFilterEditor.SetEnemyTarget(true);
            _countFilterEditor.LoadGroups(enemyCount.FilterGroups);
            return;
        }

        if (count is not null)
        {
            _nameBox.Text = count.Name;
            _categoryBox.SelectedIndex = 1;
            PopulateSelectors();
            _countFilterEditor.SetEnemyTarget(false);
            _countFilterEditor.LoadGroups(count.FilterGroups);
            return;
        }

        if (unit is not null)
        {
            _nameBox.Text = unit.Name;
            _valueNameBox.Text = unit.ValueName ?? string.Empty;
            _categoryBox.SelectedIndex = 0;
            PopulateSelectors();
            SelectTargetField(unit.TargetField);
            PopulateSelectionModes(preserveSelection: false);
            SelectSelectionMode(unit.SelectionMode);
            PopulateTargetAuras();
            if (unit.TargetAuraSpellId is { } auraId)
            {
                SelectAura(_targetAuraBox, auraId);
            }

            _countFilterEditor.SetEnemyTarget(false);
            _countFilterEditor.LoadGroups(unit.FilterGroups);
        }
    }

    private void OnConfirm()
    {
        var name = _nameBox.Text.Trim();
        if (!ValidateName(name, out var message))
        {
            MessageBox.Show(message, "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (IsAverageHealthCategory)
        {
            if (!_countFilterEditor.TryValidate(out var averageFilterMessage))
            {
                MessageBox.Show(averageFilterMessage, "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ResultAverageHealth = BuildAverageHealth(name);
            DialogResult = DialogResult.OK;
            return;
        }

        if (IsEnemyCountCategory)
        {
            if (!_countFilterEditor.TryValidate(out var filterMessage))
            {
                MessageBox.Show(filterMessage, "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ResultEnemyCount = BuildEnemyCount(name);
            DialogResult = DialogResult.OK;
            return;
        }

        if (IsCountCategory)
        {
            if (!_countFilterEditor.TryValidate(out var filterMessage))
            {
                MessageBox.Show(filterMessage, "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ResultCount = BuildAllyCount(name);
            DialogResult = DialogResult.OK;
            return;
        }

        if (!_countFilterEditor.TryValidate(out var unitFilterMessage))
        {
            MessageBox.Show(unitFilterMessage, "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var valueName = _valueNameBox.Text.Trim();
        if (valueName.Length > 0)
        {
            if (string.Equals(valueName, name, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("值名称不能与名称相同。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!ValidateName(valueName, out var valueMessage))
            {
                MessageBox.Show($"值名称: {valueMessage}", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        var unit = BuildFilteredUnit(name);
        unit.ValueName = valueName.Length == 0 ? null : valueName;
        if (!ValidateUnit(unit))
        {
            return;
        }

        ResultUnit = unit;
        DialogResult = DialogResult.OK;
    }

    private bool ValidateUnit(ModuleUnit unit)
    {
        if (!IsSelectionModeValid(unit.TargetField, unit.SelectionMode))
        {
            MessageBox.Show("目标选择与查找的单位不匹配。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (unit.TargetField == UnitTargetFieldKind.Aura && unit.TargetAuraSpellId is not > 0)
        {
            MessageBox.Show("请选择目标光环。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        return true;
    }

    private static bool IsSelectionModeValid(UnitTargetFieldKind field, UnitSelectionMode mode)
        => field switch
        {
            UnitTargetFieldKind.Health or UnitTargetFieldKind.HealingAbsorb
                => mode is UnitSelectionMode.Lowest or UnitSelectionMode.Highest,
            UnitTargetFieldKind.Role or UnitTargetFieldKind.Dispel
                => mode is UnitSelectionMode.Ascending or UnitSelectionMode.Descending,
            UnitTargetFieldKind.Aura
                => mode is UnitSelectionMode.Longest or UnitSelectionMode.Shortest
                    or UnitSelectionMode.Ascending or UnitSelectionMode.Descending,
            _ => false
        };

    private bool ValidateName(string name, out string message)
    {
        message = string.Empty;
        if (name.Length == 0)
        {
            message = "名称不能为空。";
            return false;
        }

        if (name.Contains('.') || name.Contains('$'))
        {
            message = "名称不能包含 '.' 或 '$'。";
            return false;
        }

        if (int.TryParse(name, out _))
        {
            message = "名称不能是纯数字(会与单位编号混淆)。";
            return false;
        }

        if (_takenNames.Contains(name))
        {
            message = $"名称“{name}”已被其它单位/字段或状态字段占用。";
            return false;
        }

        return true;
    }

    private bool IsUnitCategory => _categoryBox.SelectedIndex == 0;

    private bool IsCountCategory => _categoryBox.SelectedIndex == 1;

    private bool IsEnemyCountCategory => _categoryBox.SelectedIndex == 2;

    private bool IsAverageHealthCategory => _categoryBox.SelectedIndex == 3;

    private UnitTargetFieldKind SelectedTargetField()
        => (_targetFieldBox.SelectedItem as TargetFieldItem)?.Kind ?? UnitTargetFieldKind.Health;

    private UnitSelectionMode SelectedSelectionMode()
        => (_selectionModeBox.SelectedItem as SelectionModeItem)?.Mode ?? UnitSelectionMode.Lowest;

    private AverageHealthTargetKind SelectedAverageTarget()
        => (_selectorBox.SelectedItem as AverageTargetItem)?.Kind ?? AverageHealthTargetKind.Allies;

    private void SelectAverageTarget(AverageHealthTargetKind kind)
    {
        for (var i = 0; i < _selectorBox.Items.Count; i++)
        {
            if (_selectorBox.Items[i] is AverageTargetItem item && item.Kind == kind)
            {
                _selectorBox.SelectedIndex = i;
                return;
            }
        }

        _selectorBox.SelectedIndex = 0;
    }

    private void SelectTargetField(UnitTargetFieldKind kind)
    {
        for (var i = 0; i < _targetFieldBox.Items.Count; i++)
        {
            if (_targetFieldBox.Items[i] is TargetFieldItem item && item.Kind == kind)
            {
                _targetFieldBox.SelectedIndex = i;
                return;
            }
        }

        _targetFieldBox.SelectedIndex = 0;
    }

    private void SelectSelectionMode(UnitSelectionMode mode)
    {
        for (var i = 0; i < _selectionModeBox.Items.Count; i++)
        {
            if (_selectionModeBox.Items[i] is SelectionModeItem item && item.Mode == mode)
            {
                _selectionModeBox.SelectedIndex = i;
                return;
            }
        }

        if (_selectionModeBox.Items.Count > 0)
        {
            _selectionModeBox.SelectedIndex = 0;
        }
    }

    private static void SelectAura(UiDropDown box, long? auraSpellId)
    {
        if (auraSpellId is null)
        {
            return;
        }

        var index = -1;
        for (var i = 0; i < box.Items.Count; i++)
        {
            if (TryReadAuraSpellId(box.Items[i], out var existing) && existing == auraSpellId)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            box.Items.Add(UnknownAura(auraSpellId.Value));
            index = box.Items.Count - 1;
        }

        box.SelectedIndex = index;
    }

    private static ConditionField UnknownAura(long spellId)
        => new(
            SpellFieldKey.AuraMember(spellId),
            $"未知光环 / {spellId}",
            ConditionFieldType.Int,
            ConditionFieldCategory.Aura);

    private static bool TryReadAuraSpellId(object? item, out long spellId)
    {
        var value = item is ConditionField field ? field.Name : item?.ToString();
        foreach (var part in value?.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [])
        {
            if (long.TryParse(part, out spellId) && spellId > 0)
            {
                return true;
            }
        }

        spellId = 0;
        return false;
    }

    private string? ResolveAuraName(long spellId)
        => _auraFields.FirstOrDefault(field => TryReadAuraSpellId(field, out var id) && id == spellId)
            ?.DisplayName.Split(" / ", 2, StringSplitOptions.TrimEntries)[0];

    private string? ResolveNameplateAuraName(long spellId)
        => _nameplateAuraFields.FirstOrDefault(field => TryReadAuraSpellId(field, out var id) && id == spellId)
            ?.DisplayName.Split(" / ", 2, StringSplitOptions.TrimEntries)[0];

    private static TableLayoutPanel BuildLabeledRow(string label, Control control, int height = ParamRowHeight)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Height = height,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(0, 8, 0, 8),
            ColumnCount = 2,
            RowCount = 1
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelWidth));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var labelControl = new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        };

        control.Dock = DockStyle.Fill;
        control.Margin = Padding.Empty;
        panel.Controls.Add(labelControl, 0, 0);
        panel.Controls.Add(control, 1, 0);
        return panel;
    }

    private static TableLayoutPanel BuildParamSplitRow(string labelA, Control controlA, string labelB, Control controlB)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Height = ParamRowHeight,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(0, 8, 0, 8),
            ColumnCount = 4,
            RowCount = 1
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelWidth));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelWidth));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var labelAControl = new Label
        {
            Text = labelA,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        };
        controlA.Dock = DockStyle.Fill;
        controlA.Margin = new Padding(0, 0, 12, 0);

        var labelBControl = new Label
        {
            Text = labelB,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        };
        controlB.Dock = DockStyle.Fill;
        controlB.Margin = new Padding(0);

        panel.Controls.Add(labelAControl, 0, 0);
        panel.Controls.Add(controlA, 1, 0);
        panel.Controls.Add(labelBControl, 2, 0);
        panel.Controls.Add(controlB, 3, 0);
        return panel;
    }

    private Control BuildSplitRow(string labelA, Control controlA, string labelB, Control controlB)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0),
            Padding = new Padding(0, 11, 0, 11),
            ColumnCount = 4,
            RowCount = 1
        };
        _categoryRow = panel;
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var labelAControl = new Label
        {
            Text = labelA,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        };
        controlA.Dock = DockStyle.Fill;
        controlA.Margin = new Padding(0, 0, 12, 0);

        _selectorLabel = new Label
        {
            Text = labelB,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        };
        controlB.Dock = DockStyle.Fill;
        controlB.Margin = new Padding(0);

        panel.Controls.Add(labelAControl, 0, 0);
        panel.Controls.Add(controlA, 1, 0);
        panel.Controls.Add(_selectorLabel, 2, 0);
        panel.Controls.Add(controlB, 3, 0);
        ApplyHeaderRowLayout(panel, split: false);
        return panel;
    }

    private Control BuildNameRow()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0),
            Padding = new Padding(0, 11, 0, 11),
            ColumnCount = 4,
            RowCount = 1
        };
        _nameRow = panel;
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var nameLabel = new Label
        {
            Text = "名称",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        };
        UiTheme.StyleTextBox(_nameBox);
        _nameBox.AutoSize = false;
        _nameBox.Dock = DockStyle.Fill;
        _nameBox.Margin = new Padding(0, 0, 12, 0);
        _nameBox.TextChanged += (_, _) => UpdatePreview();

        _valueNameLabel.Text = "值名称";
        _valueNameLabel.Dock = DockStyle.Fill;
        _valueNameLabel.ForeColor = UiTheme.Muted;
        _valueNameLabel.TextAlign = ContentAlignment.MiddleLeft;
        _valueNameLabel.AutoEllipsis = true;
        _valueNameLabel.Margin = new Padding(0);
        UiTheme.StyleTextBox(_valueNameBox);
        _valueNameBox.AutoSize = false;
        _valueNameBox.Dock = DockStyle.Fill;
        _valueNameBox.Margin = new Padding(0);
        _valueNameBox.TextChanged += (_, _) => UpdatePreview();
        const string valueTip = "可选：把所选单位的目标字段值暴露为同名数值条件字段";
        _toolTip.SetToolTip(_valueNameBox, valueTip);
        _toolTip.SetToolTip(_valueNameLabel, valueTip);

        panel.Controls.Add(nameLabel, 0, 0);
        panel.Controls.Add(_nameBox, 1, 0);
        panel.Controls.Add(_valueNameLabel, 2, 0);
        panel.Controls.Add(_valueNameBox, 3, 0);
        ApplyHeaderRowLayout(panel, split: true);
        return panel;
    }

    private void ApplyHeaderRowLayout(TableLayoutPanel? panel, bool split)
    {
        if (panel is null)
        {
            return;
        }

        panel.SuspendLayout();
        try
        {
            panel.ColumnStyles.Clear();
            if (split)
            {
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, _labelColumnWidth));
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, _labelColumnWidth));
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            }
            else
            {
                // 单栏：标签固定宽，输入占满剩余；后两列折叠为 0。
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, _labelColumnWidth));
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));
            }
        }
        finally
        {
            panel.ResumeLayout(true);
        }
    }

    private sealed record TargetFieldItem(string Text, UnitTargetFieldKind Kind)
    {
        public override string ToString() => Text;
    }

    private sealed record SelectionModeItem(string Text, UnitSelectionMode Mode)
    {
        public override string ToString() => Text;
    }

    private sealed record AverageTargetItem(string Text, AverageHealthTargetKind Kind)
    {
        public override string ToString() => Text;
    }
}
