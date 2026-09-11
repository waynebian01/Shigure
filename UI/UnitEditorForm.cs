using System.Drawing;

namespace Shigure;

/// <summary>
/// 动态单位 / 数量字段的编辑弹窗: 选择类别(单位/数量)与选择器, 按所选类型动态显隐参数控件。
/// 光环候选来自当前职业/专精的 group 字段。校验名称非空、唯一、非纯数字、不含 '.'/'$'。
/// </summary>
public sealed class UnitEditorForm : Form
{
    private const int RowWidth = 800;
    private const int LabelWidth = 132;
    private const int ControlLeft = LabelWidth + 10;

    private static readonly RoleOption[] RoleOptions =
    [
        new("坦克 (1)", 1),
        new("治疗 (2)", 2),
        new("输出 (3)", 3)
    ];

    private static readonly DispelTypeOption[] DispelTypeOptions =
    [
        new("1: 魔法", 1),
        new("2: 诅咒", 2),
        new("3: 疾病", 3),
        new("4: 中毒", 4)
    ];

    private static readonly LowestHealthAuraFilterItem[] LowestHealthAuraFilterOptions =
    [
        new("不筛选光环", LowestHealthAuraFilterKind.None),
        new("带任一光环", LowestHealthAuraFilterKind.WithAnyAura),
        new("不带任一光环", LowestHealthAuraFilterKind.WithoutAnyAura),
        new("不带某光环", LowestHealthAuraFilterKind.WithoutAura),
        new("带某光环", LowestHealthAuraFilterKind.WithAura),
        new("某光环值等于", LowestHealthAuraFilterKind.WithAuraCount)
    ];

    private static readonly LowestHealthRoleFilterItem[] LowestHealthRoleFilterOptions =
    [
        new("不筛选职责", null),
        new("包含某职责", UnitRoleFilterKind.Include),
        new("不含某职责", UnitRoleFilterKind.Exclude)
    ];

    private static readonly SelectorItem[] UnitSelectors =
    [
        new("生命值最低", UnitSelectorKind.LowestHealth),
        new("治疗吸收最高", UnitSelectorKind.HighestHealingAbsorb),
        new("按职责", UnitSelectorKind.UnitWithRole),
        new("按职责且不带某光环", UnitSelectorKind.UnitWithRoleWithoutAura),
        new("带某光环(持续最久)", UnitSelectorKind.UnitWithAura),
        new("带某光环(持续最短)", UnitSelectorKind.UnitWithAuraShortest),
        new("带某驱散类型", UnitSelectorKind.UnitWithDispelType)
    ];

    private static readonly CountItem[] CountSelectors =
    [
        new("血量 - 低于阈值", CountKind.UnitsBelowHealth),
        new("血量 - 低于阈值不带某光环", CountKind.UnitsWithoutAuraBelowHealth),
        new("血量 - 低于阈值带某光环", CountKind.UnitsWithAuraBelowHealth),
        new("治疗吸收 - 大于阈值", CountKind.UnitsAboveHealingAbsorb),
        new("治疗吸收 - 大于阈值不带某光环", CountKind.UnitsWithoutAuraAboveHealingAbsorb),
        new("治疗吸收 - 大于阈值带某光环", CountKind.UnitsWithAuraAboveHealingAbsorb),
        new("光环 - 带某光环", CountKind.UnitsWithAura)
    ];

    private static readonly ThresholdModeItem[] ThresholdModeOptions =
    [
        new("固定阈值", false),
        new("动态阈值", true)
    ];

    private static readonly EnemyThresholdFilterItem[] EnemyHealthFilterOptions =
    [
        new("不筛选生命值", EnemyThresholdFilterKind.None),
        new("生命值大于", EnemyThresholdFilterKind.Above),
        new("生命值小于", EnemyThresholdFilterKind.Below)
    ];

    private static readonly EnemyThresholdFilterItem[] EnemyRangeFilterOptions =
    [
        new("不筛选距离", EnemyThresholdFilterKind.None),
        new("距离大于", EnemyThresholdFilterKind.Above),
        new("距离小于", EnemyThresholdFilterKind.Below)
    ];

    private static readonly EnemyCombatFilterItem[] EnemyCombatFilterOptions =
    [
        new("不筛选战斗", EnemyCombatFilterKind.None),
        new("战斗中", EnemyCombatFilterKind.InCombat),
        new("不在战斗中", EnemyCombatFilterKind.NotInCombat)
    ];

    private static readonly EnemyAuraFilterItem[] EnemyAuraFilterOptions =
    [
        new("不筛选光环", EnemyAuraFilterKind.None),
        new("有某一个光环", EnemyAuraFilterKind.WithAura),
        new("没有某一个光环", EnemyAuraFilterKind.WithoutAura),
        new("有任一光环 (多选)", EnemyAuraFilterKind.WithAnyAura),
        new("无任一光环 (多选)", EnemyAuraFilterKind.WithoutAnyAura)
    ];

    private readonly IReadOnlyList<ConditionField> _auraFields;
    private readonly IReadOnlyList<ConditionField> _nameplateAuraFields;
    private readonly IReadOnlyList<string> _thresholdFields;
    private readonly HashSet<string> _takenNames;

    private readonly Label _healthNameLabel = new();
    private readonly TextBox _nameBox = new();
    private readonly TextBox _healthNameBox = new();
    private readonly UiDropDown _categoryBox = new();
    private readonly UiDropDown _selectorBox = new();
    private readonly UiDropDown _lowestHealthAuraFilterBox = new();
    private readonly UiDropDown _lowestHealthRoleFilterBox = new();
    private readonly FlowLayoutPanel _paramPanel = new();
    private readonly Label _previewLabel = new();
    private readonly ToolTip _toolTip = new();

    private readonly NumericUpDown _thresholdBox = new();
    private readonly UiDropDown _thresholdModeBox = new();
    private readonly UiDropDown _thresholdFieldBox = new();
    private readonly UiDropDown _roleBox = new();
    private readonly CheckBox _reverseBox = new();
    private readonly UiDropDown _auraBox = new();
    private readonly CheckedListBox _aurasBox = new();
    private readonly NumericUpDown _auraCountBox = new();
    private readonly UiDropDown _dispelTypeBox = new();

    private readonly UiDropDown _enemyHealthFilterBox = new();
    private readonly UiDropDown _enemyAuraFilterBox = new();
    private readonly UiDropDown _enemyRangeFilterBox = new();
    private readonly UiDropDown _enemyCombatFilterBox = new();
    private readonly UiDropDown _enemyAuraBox = new();
    private readonly CheckedListBox _enemyAurasBox = new();
    private readonly ThresholdGroup _enemyHealthThreshold = new();
    private readonly ThresholdGroup _enemyRangeThreshold = new();

    private Label _selectorLabel = null!;
    private Panel _enemyHealthFilterRow = null!;
    private Panel _enemyAuraFilterRow = null!;
    private Panel _enemyRangeFilterRow = null!;
    private Panel _enemyCombatFilterRow = null!;
    private Panel _enemyAuraRow = null!;
    private Panel _enemyAurasRow = null!;
    private Panel _thresholdModeRow = null!;
    private Panel _thresholdRow = null!;
    private Label _thresholdLabel = null!;
    private Panel _thresholdFieldRow = null!;
    private Panel _lowestHealthAuraFilterRow = null!;
    private Panel _lowestHealthRoleFilterRow = null!;
    private Panel _roleRow = null!;
    private Panel _reverseRow = null!;
    private Panel _auraRow = null!;
    private Panel _aurasRow = null!;
    private Panel _auraCountRow = null!;
    private Panel _dispelRow = null!;
    private bool _usesHealingAbsorbThreshold;

    public ModuleUnit? ResultUnit { get; private set; }
    public ModuleCountField? ResultCount { get; private set; }
    public ModuleEnemyCountField? ResultEnemyCount { get; private set; }

    public UnitEditorForm(
        IReadOnlyList<ConditionField> auraFields,
        IReadOnlyList<ConditionField> nameplateAuraFields,
        IReadOnlyList<string> thresholdFields,
        IReadOnlyCollection<string> takenNames,
        ModuleUnit? existingUnit,
        ModuleCountField? existingCount,
        ModuleEnemyCountField? existingEnemyCount)
    {
        _auraFields = auraFields;
        _nameplateAuraFields = nameplateAuraFields;
        _thresholdFields = thresholdFields;
        _takenNames = new HashSet<string>(takenNames, StringComparer.OrdinalIgnoreCase);
        InitializeComponent();
        Seed(existingUnit, existingCount, existingEnemyCount);
        UpdateParamVisibility();
        UpdateHealthNameState();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UiTheme.ApplyDarkTitleBar(this);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        RestoreCachedWindowSize();
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        SaveWindowSize();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SaveWindowSize();
        base.OnFormClosed(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _nameBox.Focus();
        _nameBox.SelectAll();
    }

    private void RestoreCachedWindowSize()
    {
        var cached = UiCacheStore.Load().UnitEditorWindowSize;
        if (cached is null || cached.Width <= 0 || cached.Height <= 0)
        {
            return;
        }

        var workingArea = Owner is not null
            ? Screen.FromControl(Owner).WorkingArea
            : Screen.FromControl(this).WorkingArea;
        var maximumWidth = Math.Max(MinimumSize.Width, workingArea.Width - 40);
        var maximumHeight = Math.Max(MinimumSize.Height, workingArea.Height - 40);
        Size = new Size(
            Math.Clamp(cached.Width, MinimumSize.Width, maximumWidth),
            Math.Clamp(cached.Height, MinimumSize.Height, maximumHeight));

        if (Owner is not null)
        {
            CenterToParent();
        }
        else
        {
            CenterToScreen();
        }
    }

    private void SaveWindowSize()
    {
        if (WindowState != FormWindowState.Normal || Width <= 0 || Height <= 0)
        {
            return;
        }

        var cache = UiCacheStore.Load();
        cache.UnitEditorWindowSize = new WindowSize
        {
            Width = Width,
            Height = Height
        };
        UiCacheStore.Save(cache);
    }

    private void InitializeComponent()
    {
        Text = "编辑单位";
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.Text;
        ClientSize = new Size(RowWidth + 36, 600);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(RowWidth + 52, 420);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding = new Padding(UiTheme.CardPadding, 12, UiTheme.CardPadding, 12),
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        Controls.Add(root);

        UiTheme.StyleComboBox(_categoryBox);
        _categoryBox.DropDownWidth = 180;
        _categoryBox.Items.AddRange(["单位 (可作目标)", "数量 (仅条件)", "敌人数量 (仅条件)"]);
        _categoryBox.SelectedIndex = 0;
        _categoryBox.SelectedIndexChanged += (_, _) =>
        {
            PopulateSelectors();
            UpdateParamVisibility();
            UpdateHealthNameState();
        };

        UiTheme.StyleComboBox(_selectorBox);
        _selectorBox.DropDownWidth = 360;
        _selectorBox.SelectedIndexChanged += (_, _) =>
        {
            UpdateParamVisibility();
            UpdateHealthNameState();
        };

        var headerCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(4),
            Margin = new Padding(0, 0, 0, UiTheme.PageGap),
            ColumnCount = 1,
            RowCount = 2
        };
        headerCard.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        headerCard.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        headerCard.Controls.Add(BuildSplitRow("类别", _categoryBox, "选择器", _selectorBox), 0, 0);
        headerCard.Controls.Add(BuildNameRow(), 0, 1);
        root.Controls.Add(headerCard, 0, 0);

        _paramPanel.Dock = DockStyle.Fill;
        _paramPanel.BackColor = Color.Transparent;
        _paramPanel.FlowDirection = FlowDirection.TopDown;
        _paramPanel.WrapContents = false;
        _paramPanel.AutoScroll = true;
        _paramPanel.Margin = new Padding(0);
        _paramPanel.Padding = new Padding(4, 6, 4, 6);
        BuildParamRows();
        var paramsCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
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
            Padding = new Padding(UiTheme.CardPadding, 6, UiTheme.CardPadding, 6),
            Margin = new Padding(0, 0, 0, UiTheme.PageGap),
            ColumnCount = 1,
            RowCount = 1
        };
        previewCard.Controls.Add(_previewLabel, 0, 0);
        root.Controls.Add(previewCard, 0, 2);

        root.Controls.Add(BuildActionRow(), 0, 3);

        PopulateSelectors();
    }

    private void BuildParamRows()
    {
        _thresholdBox.Minimum = 1;
        _thresholdBox.Maximum = int.MaxValue;
        _thresholdBox.Value = 100;
        UiTheme.StyleNumericUpDown(_thresholdBox);

        UiTheme.StyleComboBox(_thresholdModeBox);
        _thresholdModeBox.DropDownWidth = 160;
        _thresholdModeBox.Items.AddRange(ThresholdModeOptions.Cast<object>().ToArray());
        _thresholdModeBox.SelectedIndex = 0;
        _thresholdModeBox.SelectedIndexChanged += (_, _) => UpdateParamVisibility();

        UiTheme.StyleComboBox(_thresholdFieldBox);
        _thresholdFieldBox.DropDownWidth = 360;
        foreach (var field in _thresholdFields)
        {
            if (!_thresholdFieldBox.Items.Contains(field))
            {
                _thresholdFieldBox.Items.Add(field);
            }
        }

        if (_thresholdFieldBox.Items.Count > 0)
        {
            _thresholdFieldBox.SelectedIndex = 0;
        }

        UiTheme.StyleComboBox(_lowestHealthAuraFilterBox);
        _lowestHealthAuraFilterBox.DropDownWidth = 220;
        _lowestHealthAuraFilterBox.Items.AddRange(LowestHealthAuraFilterOptions.Cast<object>().ToArray());
        _lowestHealthAuraFilterBox.SelectedIndex = 0;
        _lowestHealthAuraFilterBox.SelectedIndexChanged += (_, _) => UpdateParamVisibility();

        UiTheme.StyleComboBox(_lowestHealthRoleFilterBox);
        _lowestHealthRoleFilterBox.DropDownWidth = 220;
        _lowestHealthRoleFilterBox.Items.AddRange(LowestHealthRoleFilterOptions.Cast<object>().ToArray());
        _lowestHealthRoleFilterBox.SelectedIndex = 0;
        _lowestHealthRoleFilterBox.SelectedIndexChanged += (_, _) => UpdateParamVisibility();

        UiTheme.StyleComboBox(_roleBox);
        _roleBox.DropDownWidth = 160;
        _roleBox.Items.AddRange(RoleOptions.Cast<object>().ToArray());
        _roleBox.SelectedIndex = 0;

        _reverseBox.Text = "取逆序最后一个匹配单位";
        UiTheme.StyleCheckBox(_reverseBox, UiTheme.SurfaceRaised);
        _reverseBox.AutoSize = false;
        _reverseBox.TextAlign = ContentAlignment.MiddleLeft;

        UiTheme.StyleComboBox(_auraBox);
        _auraBox.DropDownWidth = 360;
        foreach (var aura in _auraFields)
        {
            _auraBox.Items.Add(aura);
        }

        if (_auraBox.Items.Count > 0)
        {
            _auraBox.SelectedIndex = 0;
        }

        UiTheme.StyleCheckedListBox(_aurasBox);
        foreach (var aura in _auraFields)
        {
            _aurasBox.Items.Add(aura);
        }

        _auraCountBox.Minimum = 0;
        _auraCountBox.Maximum = 100;
        _auraCountBox.Value = 1;
        UiTheme.StyleNumericUpDown(_auraCountBox);

        UiTheme.StyleComboBox(_dispelTypeBox);
        _dispelTypeBox.DropDownWidth = 180;
        _dispelTypeBox.Items.AddRange(DispelTypeOptions.Cast<object>().ToArray());
        _dispelTypeBox.SelectedIndex = 0;

        // 参数值变化刷新底部实时预览(类别/选择器/阈值类型/光环筛选经 UpdateParamVisibility 间接刷新)。
        _thresholdBox.ValueChanged += (_, _) => UpdatePreview();
        _thresholdFieldBox.SelectedIndexChanged += (_, _) => UpdatePreview();
        _roleBox.SelectedIndexChanged += (_, _) => UpdatePreview();
        _reverseBox.CheckedChanged += (_, _) => UpdatePreview();
        _auraBox.SelectedIndexChanged += (_, _) => UpdatePreview();
        _auraCountBox.ValueChanged += (_, _) => UpdatePreview();
        _dispelTypeBox.SelectedIndexChanged += (_, _) => UpdatePreview();
        // ItemCheck 在勾选状态提交前触发, 延后到提交后再读 CheckedItems。
        // Seed 期间 CheckAuras 也会触发本事件, 此时窗口句柄尚未创建, 跳过(构造末尾会统一刷新)。
        _aurasBox.ItemCheck += (_, _) =>
        {
            if (IsHandleCreated)
            {
                BeginInvoke(new Action(UpdatePreview));
            }
        };

        _thresholdModeRow = BuildLabeledRow("阈值类型", _thresholdModeBox);
        _thresholdRow = BuildLabeledRow("血量阈值 (<)", _thresholdBox);
        _thresholdLabel = _thresholdRow.Controls.OfType<Label>().Single();
        _thresholdFieldRow = BuildLabeledRow("动态阈值", _thresholdFieldBox);
        _lowestHealthAuraFilterRow = BuildLabeledRow("光环筛选", _lowestHealthAuraFilterBox);
        _lowestHealthRoleFilterRow = BuildLabeledRow("职责筛选", _lowestHealthRoleFilterBox);
        _roleRow = BuildLabeledRow("职责", _roleBox);
        _reverseRow = BuildLabeledRow("顺序", _reverseBox);
        _auraRow = BuildLabeledRow("光环", _auraBox);
        _aurasRow = BuildLabeledRow("光环 (可多选)", _aurasBox, 116);
        _auraCountRow = BuildLabeledRow("光环值", _auraCountBox);
        _dispelRow = BuildLabeledRow("驱散类型", _dispelTypeBox);

        BuildEnemyParamRows();

        _paramPanel.Controls.AddRange([_thresholdModeRow, _thresholdRow, _thresholdFieldRow, _lowestHealthAuraFilterRow, _lowestHealthRoleFilterRow, _roleRow, _reverseRow, _auraRow, _aurasRow, _auraCountRow, _dispelRow]);
        _paramPanel.Controls.AddRange([
            _enemyHealthFilterRow,
            _enemyHealthThreshold.ModeRow,
            _enemyHealthThreshold.ValueRow,
            _enemyHealthThreshold.FieldRow,
            _enemyAuraFilterRow,
            _enemyAuraRow,
            _enemyAurasRow,
            _enemyRangeFilterRow,
            _enemyRangeThreshold.ModeRow,
            _enemyRangeThreshold.ValueRow,
            _enemyRangeThreshold.FieldRow,
            _enemyCombatFilterRow
        ]);
    }

    // 敌人数量: 生命值 / 光环 / 距离 / 战斗 四组筛选彼此独立, 前三组各自带一套阈值控件。
    private void BuildEnemyParamRows()
    {
        UiTheme.StyleComboBox(_enemyHealthFilterBox);
        _enemyHealthFilterBox.DropDownWidth = 220;
        _enemyHealthFilterBox.Items.AddRange(EnemyHealthFilterOptions.Cast<object>().ToArray());
        _enemyHealthFilterBox.SelectedIndex = 0;
        _enemyHealthFilterBox.SelectedIndexChanged += (_, _) => UpdateParamVisibility();

        UiTheme.StyleComboBox(_enemyAuraFilterBox);
        _enemyAuraFilterBox.DropDownWidth = 220;
        _enemyAuraFilterBox.Items.AddRange(EnemyAuraFilterOptions.Cast<object>().ToArray());
        _enemyAuraFilterBox.SelectedIndex = 0;
        _enemyAuraFilterBox.SelectedIndexChanged += (_, _) => UpdateParamVisibility();

        UiTheme.StyleComboBox(_enemyRangeFilterBox);
        _enemyRangeFilterBox.DropDownWidth = 220;
        _enemyRangeFilterBox.Items.AddRange(EnemyRangeFilterOptions.Cast<object>().ToArray());
        _enemyRangeFilterBox.SelectedIndex = 0;
        _enemyRangeFilterBox.SelectedIndexChanged += (_, _) => UpdateParamVisibility();

        UiTheme.StyleComboBox(_enemyCombatFilterBox);
        _enemyCombatFilterBox.DropDownWidth = 220;
        _enemyCombatFilterBox.Items.AddRange(EnemyCombatFilterOptions.Cast<object>().ToArray());
        _enemyCombatFilterBox.SelectedIndex = 0;
        _enemyCombatFilterBox.SelectedIndexChanged += (_, _) => UpdateParamVisibility();

        UiTheme.StyleComboBox(_enemyAuraBox);
        _enemyAuraBox.DropDownWidth = 360;
        UiTheme.StyleCheckedListBox(_enemyAurasBox);
        foreach (var aura in _nameplateAuraFields)
        {
            _enemyAuraBox.Items.Add(aura);
            _enemyAurasBox.Items.Add(aura);
        }

        if (_enemyAuraBox.Items.Count > 0)
        {
            _enemyAuraBox.SelectedIndex = 0;
        }

        _enemyAuraBox.SelectedIndexChanged += (_, _) => UpdatePreview();
        _enemyAurasBox.ItemCheck += (_, _) =>
        {
            if (IsHandleCreated)
            {
                BeginInvoke(new Action(UpdatePreview));
            }
        };

        InitializeThresholdGroup(_enemyHealthThreshold, "生命值阈值", 0);
        InitializeThresholdGroup(_enemyRangeThreshold, "距离阈值", 0);

        _enemyHealthFilterRow = BuildLabeledRow("生命值筛选", _enemyHealthFilterBox);
        _enemyAuraFilterRow = BuildLabeledRow("光环筛选", _enemyAuraFilterBox);
        _enemyRangeFilterRow = BuildLabeledRow("距离筛选", _enemyRangeFilterBox);
        _enemyCombatFilterRow = BuildLabeledRow("战斗筛选", _enemyCombatFilterBox);
        _enemyAuraRow = BuildLabeledRow("光环", _enemyAuraBox);
        _enemyAurasRow = BuildLabeledRow("光环 (可多选)", _enemyAurasBox, 116);
    }

    private void InitializeThresholdGroup(ThresholdGroup group, string valueLabel, int defaultValue)
    {
        group.ValueBox.Minimum = 0;
        group.ValueBox.Maximum = int.MaxValue;
        group.ValueBox.Value = defaultValue;
        UiTheme.StyleNumericUpDown(group.ValueBox);
        group.ValueBox.ValueChanged += (_, _) => UpdatePreview();

        UiTheme.StyleComboBox(group.ModeBox);
        group.ModeBox.DropDownWidth = 160;
        group.ModeBox.Items.AddRange(ThresholdModeOptions.Cast<object>().ToArray());
        group.ModeBox.SelectedIndex = 0;
        group.ModeBox.SelectedIndexChanged += (_, _) => UpdateParamVisibility();

        UiTheme.StyleComboBox(group.FieldBox);
        group.FieldBox.DropDownWidth = 360;
        foreach (var field in _thresholdFields)
        {
            if (!group.FieldBox.Items.Contains(field))
            {
                group.FieldBox.Items.Add(field);
            }
        }

        if (group.FieldBox.Items.Count > 0)
        {
            group.FieldBox.SelectedIndex = 0;
        }

        group.FieldBox.SelectedIndexChanged += (_, _) => UpdatePreview();

        group.ModeRow = BuildLabeledRow($"{valueLabel}类型", group.ModeBox);
        group.ValueRow = BuildLabeledRow(valueLabel, group.ValueBox);
        group.FieldRow = BuildLabeledRow($"动态{valueLabel}", group.FieldBox);
    }

    private Control BuildActionRow()
    {
        var row = new UiCardPanel
        {
            Dock = DockStyle.Fill,
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

        var okButton = UiTheme.CreateButton("确定", UiTheme.ButtonKind.Primary);
        UiTheme.StyleActionButton(okButton, 84);
        okButton.Margin = new Padding(8, 0, 0, 0);
        okButton.Click += (_, _) => OnConfirm();

        var cancelButton = UiTheme.CreateButton("取消", UiTheme.ButtonKind.Secondary);
        UiTheme.StyleActionButton(cancelButton, 84);
        cancelButton.Margin = new Padding(8, 0, 0, 0);
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;

        actions.Controls.Add(okButton);
        actions.Controls.Add(cancelButton);
        row.Controls.Add(actions, 1, 0);
        AcceptButton = okButton;
        CancelButton = cancelButton;
        return row;
    }

    private void PopulateSelectors()
    {
        _selectorBox.Items.Clear();
        // 敌人数量没有互斥的选择器, 三组筛选同时生效, 因此整行隐藏。
        var hasSelector = !IsEnemyCountCategory;
        _selectorBox.Visible = hasSelector;
        if (_selectorLabel is not null)
        {
            _selectorLabel.Visible = hasSelector;
        }

        if (!hasSelector)
        {
            return;
        }

        if (IsCountCategory)
        {
            _selectorBox.Items.AddRange(CountSelectors.Cast<object>().ToArray());
        }
        else
        {
            _selectorBox.Items.AddRange(UnitSelectors.Cast<object>().ToArray());
        }

        if (_selectorBox.Items.Count > 0)
        {
            _selectorBox.SelectedIndex = 0;
        }
    }

    // 值名称只对"生命值最低"单位有意义(把该单位的 生命值 暴露成数值条件字段)。
    private void UpdateHealthNameState()
    {
        var visible = SupportsHealthName();
        _healthNameLabel.Visible = visible;
        _healthNameBox.Visible = visible;
        _healthNameBox.Enabled = visible;
        if (!visible)
        {
            _healthNameBox.Text = string.Empty;
        }
    }

    private void UpdateParamVisibility()
    {
        bool threshold = false, lowestHealthAuraFilter = false, lowestHealthRoleFilter = false, role = false, reverse = false, auraSingle = false, auraMulti = false, auraCount = false, dispel = false;

        if (IsEnemyCountCategory)
        {
            UpdateEnemyParamVisibility();
            _thresholdModeRow.Visible = false;
            _thresholdRow.Visible = false;
            _thresholdFieldRow.Visible = false;
            _lowestHealthAuraFilterRow.Visible = false;
            _lowestHealthRoleFilterRow.Visible = false;
            _roleRow.Visible = false;
            _reverseRow.Visible = false;
            _auraRow.Visible = false;
            _aurasRow.Visible = false;
            _auraCountRow.Visible = false;
            _dispelRow.Visible = false;
            UpdatePreview();
            return;
        }

        SetEnemyRowsVisible(false);
        if (IsCountCategory)
        {
            switch ((_selectorBox.SelectedItem as CountItem)?.Kind)
            {
                case CountKind.UnitsBelowHealth:
                    threshold = true;
                    break;
                case CountKind.UnitsWithoutAuraBelowHealth:
                    threshold = auraSingle = true;
                    break;
                case CountKind.UnitsWithAuraBelowHealth:
                    threshold = auraSingle = true;
                    break;
                case CountKind.UnitsWithAura:
                    auraSingle = true;
                    break;
                case CountKind.UnitsAboveHealingAbsorb:
                    threshold = true;
                    break;
                case CountKind.UnitsWithoutAuraAboveHealingAbsorb:
                case CountKind.UnitsWithAuraAboveHealingAbsorb:
                    threshold = auraSingle = true;
                    break;
            }
        }
        else
        {
            switch ((_selectorBox.SelectedItem as SelectorItem)?.Kind)
            {
                case UnitSelectorKind.LowestHealth:
                    threshold = lowestHealthAuraFilter = true;
                    lowestHealthRoleFilter = true;
                    role = SelectedLowestHealthRoleFilter() is not null;
                    switch (SelectedLowestHealthAuraFilter())
                    {
                        case LowestHealthAuraFilterKind.WithAnyAura:
                        case LowestHealthAuraFilterKind.WithoutAnyAura:
                            auraMulti = true;
                            break;
                        case LowestHealthAuraFilterKind.WithoutAura:
                        case LowestHealthAuraFilterKind.WithAura:
                            auraSingle = true;
                            break;
                        case LowestHealthAuraFilterKind.WithAuraCount:
                            auraSingle = auraCount = true;
                            break;
                    }

                    break;
                case UnitSelectorKind.HighestHealingAbsorb:
                    threshold = lowestHealthAuraFilter = true;
                    switch (SelectedLowestHealthAuraFilter())
                    {
                        case LowestHealthAuraFilterKind.WithAnyAura:
                        case LowestHealthAuraFilterKind.WithoutAnyAura:
                            auraMulti = true;
                            break;
                        case LowestHealthAuraFilterKind.WithoutAura:
                        case LowestHealthAuraFilterKind.WithAura:
                            auraSingle = true;
                            break;
                        case LowestHealthAuraFilterKind.WithAuraCount:
                            auraSingle = auraCount = true;
                            break;
                    }

                    break;
                case UnitSelectorKind.LowestHealthWithAnyAura:
                case UnitSelectorKind.LowestHealthWithoutAnyAura:
                    threshold = auraMulti = true;
                    break;
                case UnitSelectorKind.LowestHealthWithoutAura:
                case UnitSelectorKind.LowestHealthWithAura:
                    threshold = auraSingle = true;
                    break;
                case UnitSelectorKind.LowestHealthWithAuraCount:
                case UnitSelectorKind.HighestHealingAbsorbWithAuraCount:
                    threshold = auraSingle = auraCount = true;
                    break;
                case UnitSelectorKind.HighestHealingAbsorbWithAnyAura:
                case UnitSelectorKind.HighestHealingAbsorbWithoutAnyAura:
                    threshold = auraMulti = true;
                    break;
                case UnitSelectorKind.HighestHealingAbsorbWithoutAura:
                case UnitSelectorKind.HighestHealingAbsorbWithAura:
                    threshold = auraSingle = true;
                    break;
                case UnitSelectorKind.UnitWithRole:
                    role = reverse = true;
                    break;
                case UnitSelectorKind.UnitWithRoleWithoutAura:
                    role = reverse = auraSingle = true;
                    break;
                case UnitSelectorKind.UnitWithAura:
                case UnitSelectorKind.UnitWithAuraShortest:
                    auraSingle = true;
                    break;
                case UnitSelectorKind.UnitWithDispelType:
                    dispel = true;
                    break;
            }
        }

        var dynamicThreshold = IsDynamicThresholdMode();
        UpdateThresholdPresentation();
        _thresholdModeRow.Visible = threshold;
        _thresholdRow.Visible = threshold && !dynamicThreshold;
        _thresholdFieldRow.Visible = threshold && dynamicThreshold;
        _lowestHealthAuraFilterRow.Visible = lowestHealthAuraFilter;
        _lowestHealthRoleFilterRow.Visible = lowestHealthRoleFilter;
        _roleRow.Visible = role;
        _reverseRow.Visible = reverse;
        _auraRow.Visible = auraSingle;
        _aurasRow.Visible = auraMulti;
        _auraCountRow.Visible = auraCount;
        _dispelRow.Visible = dispel;
        UpdatePreview();
    }

    private void UpdateEnemyParamVisibility()
    {
        var healthFilter = SelectedEnemyHealthFilter() != EnemyThresholdFilterKind.None;
        var rangeFilter = SelectedEnemyRangeFilter() != EnemyThresholdFilterKind.None;
        var auraFilter = SelectedEnemyAuraFilter();

        _enemyHealthFilterRow.Visible = true;
        _enemyAuraFilterRow.Visible = true;
        _enemyRangeFilterRow.Visible = true;
        _enemyCombatFilterRow.Visible = true;
        SetThresholdGroupVisible(_enemyHealthThreshold, healthFilter);
        SetThresholdGroupVisible(_enemyRangeThreshold, rangeFilter);
        _enemyAuraRow.Visible = auraFilter is EnemyAuraFilterKind.WithAura or EnemyAuraFilterKind.WithoutAura;
        _enemyAurasRow.Visible = auraFilter is EnemyAuraFilterKind.WithAnyAura or EnemyAuraFilterKind.WithoutAnyAura;
    }

    private void SetEnemyRowsVisible(bool visible)
    {
        _enemyHealthFilterRow.Visible = visible;
        _enemyAuraFilterRow.Visible = visible;
        _enemyRangeFilterRow.Visible = visible;
        _enemyCombatFilterRow.Visible = visible;
        _enemyAuraRow.Visible = visible;
        _enemyAurasRow.Visible = visible;
        SetThresholdGroupVisible(_enemyHealthThreshold, visible);
        SetThresholdGroupVisible(_enemyRangeThreshold, visible);
    }

    private static void SetThresholdGroupVisible(ThresholdGroup group, bool visible)
    {
        group.ModeRow.Visible = visible;
        group.ValueRow.Visible = visible && !group.UsesDynamicField;
        group.FieldRow.Visible = visible && group.UsesDynamicField;
    }

    private void UpdatePreview()
    {
        var text = BuildPreviewText();
        _previewLabel.Text = string.IsNullOrEmpty(text) ? "预览: -" : $"预览: {text}";
    }

    // 用当前控件状态构造一个宽容的(不校验、不弹框)单位/数量, 复用 UnitSummary 渲染预览。
    private string BuildPreviewText()
    {
        if (IsEnemyCountCategory)
        {
            return UnitSummary.Describe(BuildEnemyCount(_nameBox.Text.Trim()), ResolveNameplateAuraName);
        }

        if (IsCountCategory)
        {
            var count = new ModuleCountField
            {
                Name = _nameBox.Text.Trim(),
                Kind = (_selectorBox.SelectedItem as CountItem)?.Kind ?? CountKind.UnitsBelowHealth,
                AuraSpellId = SelectedAura()
            };
            ApplyPreviewThreshold(v => count.HealthThreshold = v, f => count.HealthThresholdField = f);
            return UnitSummary.Describe(count, ResolveAuraName);
        }

        var unit = new ModuleUnit
        {
            Name = _nameBox.Text.Trim(),
            Kind = ResolveSelectedUnitKind(),
            RoleFilter = SelectedLowestHealthRoleFilter(),
            Role = SelectedRole(),
            Reverse = _reverseBox.Checked,
            AuraCount = (int)_auraCountBox.Value,
            DispelType = SelectedDispelType()
        };
        unit.AuraSpellIds = unit.Kind is UnitSelectorKind.LowestHealthWithAnyAura
            or UnitSelectorKind.LowestHealthWithoutAnyAura
            or UnitSelectorKind.HighestHealingAbsorbWithAnyAura
            or UnitSelectorKind.HighestHealingAbsorbWithoutAnyAura
            ? CheckedAuras()
            : SingleAuraList();
        ApplyPreviewThreshold(v => unit.HealthThreshold = v, f => unit.HealthThresholdField = f);
        return UnitSummary.Describe(unit, ResolveAuraName);
    }

    // "生命值最低"/"治疗吸收最高" 的具体子类型取决于光环筛选下拉, 与 OnConfirm 的分支保持一致。
    private UnitSelectorKind ResolveSelectedUnitKind()
    {
        var kind = (_selectorBox.SelectedItem as SelectorItem)?.Kind ?? UnitSelectorKind.LowestHealth;
        if (kind is not UnitSelectorKind.LowestHealth and not UnitSelectorKind.HighestHealingAbsorb)
        {
            return kind;
        }

        var healingAbsorb = kind == UnitSelectorKind.HighestHealingAbsorb;
        return SelectedLowestHealthAuraFilter() switch
        {
            LowestHealthAuraFilterKind.WithAnyAura => healingAbsorb
                ? UnitSelectorKind.HighestHealingAbsorbWithAnyAura
                : UnitSelectorKind.LowestHealthWithAnyAura,
            LowestHealthAuraFilterKind.WithoutAnyAura => healingAbsorb
                ? UnitSelectorKind.HighestHealingAbsorbWithoutAnyAura
                : UnitSelectorKind.LowestHealthWithoutAnyAura,
            LowestHealthAuraFilterKind.WithoutAura => healingAbsorb
                ? UnitSelectorKind.HighestHealingAbsorbWithoutAura
                : UnitSelectorKind.LowestHealthWithoutAura,
            LowestHealthAuraFilterKind.WithAura => healingAbsorb
                ? UnitSelectorKind.HighestHealingAbsorbWithAura
                : UnitSelectorKind.LowestHealthWithAura,
            LowestHealthAuraFilterKind.WithAuraCount => healingAbsorb
                ? UnitSelectorKind.HighestHealingAbsorbWithAuraCount
                : UnitSelectorKind.LowestHealthWithAuraCount,
            _ => kind
        };
    }

    private void ApplyPreviewThreshold(Action<int?> setFixed, Action<string?> setField)
    {
        if (IsDynamicThresholdMode())
        {
            setField(_thresholdFieldBox.SelectedItem?.ToString()?.Trim());
        }
        else
        {
            setFixed((int)_thresholdBox.Value);
        }
    }

    // 用当前控件状态构造敌人数量字段; 预览与确定共用, 校验留给 OnConfirm。
    private ModuleEnemyCountField BuildEnemyCount(string name)
    {
        var count = new ModuleEnemyCountField
        {
            Name = name,
            HealthFilter = SelectedEnemyHealthFilter(),
            AuraFilter = SelectedEnemyAuraFilter(),
            RangeFilter = SelectedEnemyRangeFilter(),
            CombatFilter = SelectedEnemyCombatFilter()
        };

        if (count.HealthFilter != EnemyThresholdFilterKind.None)
        {
            ReadThresholdGroup(_enemyHealthThreshold, out var fixedValue, out var field);
            count.HealthThreshold = fixedValue;
            count.HealthThresholdField = field;
        }

        if (count.RangeFilter != EnemyThresholdFilterKind.None)
        {
            ReadThresholdGroup(_enemyRangeThreshold, out var fixedValue, out var field);
            count.RangeThreshold = fixedValue;
            count.RangeThresholdField = field;
        }

        count.AuraSpellIds = count.AuraFilter switch
        {
            EnemyAuraFilterKind.WithAura or EnemyAuraFilterKind.WithoutAura => SingleAuraList(_enemyAuraBox),
            EnemyAuraFilterKind.WithAnyAura or EnemyAuraFilterKind.WithoutAnyAura => CheckedAuras(_enemyAurasBox),
            _ => null
        };

        return count;
    }

    private static void ReadThresholdGroup(ThresholdGroup group, out int? fixedValue, out string? field)
    {
        if (group.UsesDynamicField)
        {
            fixedValue = null;
            field = group.FieldBox.SelectedItem?.ToString()?.Trim();
            return;
        }

        fixedValue = (int)group.ValueBox.Value;
        field = null;
    }

    private void SeedThresholdGroup(
        ThresholdGroup group,
        EnemyThresholdFilterKind filter,
        int? fixedValue,
        string? field)
    {
        if (filter == EnemyThresholdFilterKind.None)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(field))
        {
            SelectThresholdMode(group.ModeBox, usesDynamicField: true);
            SelectThresholdField(group.FieldBox, field.Trim());
            return;
        }

        SelectThresholdMode(group.ModeBox, usesDynamicField: false);
        if (fixedValue is { } value)
        {
            group.ValueBox.Value = Clamp(value, group.ValueBox);
        }
    }

    private void Seed(ModuleUnit? unit, ModuleCountField? count, ModuleEnemyCountField? enemyCount)
    {
        if (enemyCount is not null)
        {
            _nameBox.Text = enemyCount.Name;
            _categoryBox.SelectedIndex = 2;
            PopulateSelectors();
            SelectEnemyThresholdFilter(_enemyHealthFilterBox, enemyCount.HealthFilter);
            SelectEnemyThresholdFilter(_enemyRangeFilterBox, enemyCount.RangeFilter);
            SelectEnemyAuraFilter(enemyCount.AuraFilter);
            SelectEnemyCombatFilter(enemyCount.CombatFilter);
            SeedThresholdGroup(_enemyHealthThreshold, enemyCount.HealthFilter, enemyCount.HealthThreshold, enemyCount.HealthThresholdField);
            SeedThresholdGroup(_enemyRangeThreshold, enemyCount.RangeFilter, enemyCount.RangeThreshold, enemyCount.RangeThresholdField);
            var auraSpellIds = enemyCount.AuraSpellIds ?? [];
            SelectAura(_enemyAuraBox, auraSpellIds.Count > 0 ? auraSpellIds[0] : null);
            CheckAuras(_enemyAurasBox, auraSpellIds);
            return;
        }

        if (count is not null)
        {
            _nameBox.Text = count.Name;
            _categoryBox.SelectedIndex = 1;
            PopulateSelectors();
            SelectSelector(count.Kind);
            if (count.HealthThreshold is { } th)
            {
                _thresholdBox.Value = Clamp(th, _thresholdBox);
            }

            SeedThresholdField(count.HealthThresholdField);
            SelectAura(_auraBox, count.AuraSpellId ?? ResolveLegacyAura(count.AuraName));
            return;
        }

        if (unit is not null)
        {
            _nameBox.Text = unit.Name;
            _healthNameBox.Text = unit.HealthName ?? string.Empty;
            _categoryBox.SelectedIndex = 0;
            PopulateSelectors();
            SelectSelector(DisplaySelectorKind(unit.Kind));
            SelectLowestHealthAuraFilter(unit.Kind);
            SelectLowestHealthRoleFilter(unit.RoleFilter);
            if (unit.HealthThreshold is { } th)
            {
                _thresholdBox.Value = Clamp(th, _thresholdBox);
            }

            SeedThresholdField(unit.HealthThresholdField);
            if (unit.Role is { } r)
            {
                SelectRole(r);
            }

            _reverseBox.Checked = unit.Reverse;
            var auraSpellIds = unit.AuraSpellIds is { Count: > 0 }
                ? unit.AuraSpellIds
                : (unit.AuraNames ?? []).Select(ResolveLegacyAura).Where(id => id is not null).Select(id => id!.Value).ToList();
            SelectAura(_auraBox, auraSpellIds is { Count: > 0 } ? auraSpellIds[0] : null);
            CheckAuras(auraSpellIds);
            if (unit.AuraCount is { } ac)
            {
                _auraCountBox.Value = Clamp(ac, _auraCountBox);
            }

            if (unit.DispelType is { } dt)
            {
                SelectDispelType(dt);
            }
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

        if (IsEnemyCountCategory)
        {
            var enemyCount = BuildEnemyCount(name);
            if (enemyCount.HealthFilter != EnemyThresholdFilterKind.None
                && enemyCount.HealthThreshold is null
                && string.IsNullOrWhiteSpace(enemyCount.HealthThresholdField))
            {
                MessageBox.Show("请选择动态生命值阈值。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (enemyCount.RangeFilter != EnemyThresholdFilterKind.None
                && enemyCount.RangeThreshold is null
                && string.IsNullOrWhiteSpace(enemyCount.RangeThresholdField))
            {
                MessageBox.Show("请选择动态距离阈值。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (enemyCount.AuraFilter != EnemyAuraFilterKind.None
                && (enemyCount.AuraSpellIds is null || enemyCount.AuraSpellIds.Count == 0))
            {
                MessageBox.Show("请选择光环。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ResultEnemyCount = enemyCount;
            DialogResult = DialogResult.OK;
            return;
        }

        if (IsCountCategory)
        {
            var kind = (_selectorBox.SelectedItem as CountItem)?.Kind ?? CountKind.UnitsBelowHealth;
            var count = new ModuleCountField { Name = name, Kind = kind };
            switch (kind)
            {
                case CountKind.UnitsBelowHealth:
                    if (!ApplyThreshold(count))
                    {
                        return;
                    }

                    break;
                case CountKind.UnitsWithoutAuraBelowHealth:
                    if (!ApplyThreshold(count))
                    {
                        return;
                    }

                    count.AuraSpellId = SelectedAura();
                    break;
                case CountKind.UnitsWithAuraBelowHealth:
                    if (!ApplyThreshold(count))
                    {
                        return;
                    }

                    count.AuraSpellId = SelectedAura();
                    break;
                case CountKind.UnitsWithAura:
                    count.AuraSpellId = SelectedAura();
                    break;
                case CountKind.UnitsAboveHealingAbsorb:
                    if (!ApplyThreshold(count))
                    {
                        return;
                    }

                    break;
                case CountKind.UnitsWithoutAuraAboveHealingAbsorb:
                case CountKind.UnitsWithAuraAboveHealingAbsorb:
                    if (!ApplyThreshold(count))
                    {
                        return;
                    }

                    count.AuraSpellId = SelectedAura();
                    break;
            }

            if (RequiresAura(kind) && count.AuraSpellId is null)
            {
                MessageBox.Show("请选择光环。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ResultCount = count;
            DialogResult = DialogResult.OK;
            return;
        }

        var selectorKind = (_selectorBox.SelectedItem as SelectorItem)?.Kind ?? UnitSelectorKind.LowestHealth;
        var moduleUnit = new ModuleUnit { Name = name, Kind = selectorKind };
        switch (selectorKind)
        {
            case UnitSelectorKind.LowestHealth:
            case UnitSelectorKind.HighestHealingAbsorb:
                if (!ApplyThreshold(moduleUnit))
                {
                    return;
                }

                var healingAbsorb = selectorKind == UnitSelectorKind.HighestHealingAbsorb;
                if (!healingAbsorb && SelectedLowestHealthRoleFilter() is { } roleFilter)
                {
                    moduleUnit.RoleFilter = roleFilter;
                    moduleUnit.Role = SelectedRole();
                }

                switch (SelectedLowestHealthAuraFilter())
                {
                    case LowestHealthAuraFilterKind.WithAnyAura:
                        moduleUnit.Kind = healingAbsorb
                            ? UnitSelectorKind.HighestHealingAbsorbWithAnyAura
                            : UnitSelectorKind.LowestHealthWithAnyAura;
                        moduleUnit.AuraSpellIds = CheckedAuras();
                        if (moduleUnit.AuraSpellIds.Count == 0)
                        {
                            MessageBox.Show("请至少勾选一个光环。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        break;
                    case LowestHealthAuraFilterKind.WithoutAnyAura:
                        moduleUnit.Kind = healingAbsorb
                            ? UnitSelectorKind.HighestHealingAbsorbWithoutAnyAura
                            : UnitSelectorKind.LowestHealthWithoutAnyAura;
                        moduleUnit.AuraSpellIds = CheckedAuras();
                        if (moduleUnit.AuraSpellIds.Count == 0)
                        {
                            MessageBox.Show("请至少勾选一个光环。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        break;
                    case LowestHealthAuraFilterKind.WithoutAura:
                        moduleUnit.Kind = healingAbsorb
                            ? UnitSelectorKind.HighestHealingAbsorbWithoutAura
                            : UnitSelectorKind.LowestHealthWithoutAura;
                        moduleUnit.AuraSpellIds = SingleAuraList();
                        break;
                    case LowestHealthAuraFilterKind.WithAura:
                        moduleUnit.Kind = healingAbsorb
                            ? UnitSelectorKind.HighestHealingAbsorbWithAura
                            : UnitSelectorKind.LowestHealthWithAura;
                        moduleUnit.AuraSpellIds = SingleAuraList();
                        break;
                    case LowestHealthAuraFilterKind.WithAuraCount:
                        moduleUnit.Kind = healingAbsorb
                            ? UnitSelectorKind.HighestHealingAbsorbWithAuraCount
                            : UnitSelectorKind.LowestHealthWithAuraCount;
                        moduleUnit.AuraSpellIds = SingleAuraList();
                        moduleUnit.AuraCount = (int)_auraCountBox.Value;
                        break;
                }

                break;
            case UnitSelectorKind.LowestHealthWithAnyAura:
            case UnitSelectorKind.LowestHealthWithoutAnyAura:
            case UnitSelectorKind.HighestHealingAbsorbWithAnyAura:
            case UnitSelectorKind.HighestHealingAbsorbWithoutAnyAura:
                if (!ApplyThreshold(moduleUnit))
                {
                    return;
                }

                moduleUnit.AuraSpellIds = CheckedAuras();
                if (moduleUnit.AuraSpellIds.Count == 0)
                {
                    MessageBox.Show("请至少勾选一个光环。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                break;
            case UnitSelectorKind.LowestHealthWithoutAura:
            case UnitSelectorKind.LowestHealthWithAura:
            case UnitSelectorKind.HighestHealingAbsorbWithoutAura:
            case UnitSelectorKind.HighestHealingAbsorbWithAura:
                if (!ApplyThreshold(moduleUnit))
                {
                    return;
                }

                moduleUnit.AuraSpellIds = SingleAuraList();
                break;
            case UnitSelectorKind.LowestHealthWithAuraCount:
            case UnitSelectorKind.HighestHealingAbsorbWithAuraCount:
                if (!ApplyThreshold(moduleUnit))
                {
                    return;
                }

                moduleUnit.AuraSpellIds = SingleAuraList();
                moduleUnit.AuraCount = (int)_auraCountBox.Value;
                break;
            case UnitSelectorKind.UnitWithRole:
                moduleUnit.Role = SelectedRole();
                moduleUnit.Reverse = _reverseBox.Checked;
                break;
            case UnitSelectorKind.UnitWithRoleWithoutAura:
                moduleUnit.Role = SelectedRole();
                moduleUnit.Reverse = _reverseBox.Checked;
                moduleUnit.AuraSpellIds = SingleAuraList();
                break;
            case UnitSelectorKind.UnitWithAura:
            case UnitSelectorKind.UnitWithAuraShortest:
                moduleUnit.AuraSpellIds = SingleAuraList();
                break;
            case UnitSelectorKind.UnitWithDispelType:
                moduleUnit.DispelType = SelectedDispelType();
                break;
        }

        if (UnitRequiresAura(moduleUnit.Kind) && (moduleUnit.AuraSpellIds is null || moduleUnit.AuraSpellIds.Count == 0))
        {
            MessageBox.Show("请选择光环。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var healthName = SupportsHealthName() ? _healthNameBox.Text.Trim() : string.Empty;
        if (healthName.Length > 0)
        {
            if (string.Equals(healthName, name, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("值名称不能与名称相同。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!ValidateName(healthName, out var healthMessage))
            {
                MessageBox.Show($"值名称: {healthMessage}", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        moduleUnit.HealthName = healthName.Length == 0 ? null : healthName;
        ResultUnit = moduleUnit;
        DialogResult = DialogResult.OK;
    }

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

    private bool IsCountCategory => _categoryBox.SelectedIndex == 1;

    private bool IsEnemyCountCategory => _categoryBox.SelectedIndex == 2;

    private bool IsDynamicThresholdMode()
        => (_thresholdModeBox.SelectedItem as ThresholdModeItem)?.UsesDynamicField == true;

    private bool IsHealingAbsorbSelector()
    {
        if (IsCountCategory)
        {
            return (_selectorBox.SelectedItem as CountItem)?.Kind is
                CountKind.UnitsAboveHealingAbsorb
                or CountKind.UnitsWithoutAuraAboveHealingAbsorb
                or CountKind.UnitsWithAuraAboveHealingAbsorb;
        }

        return (_selectorBox.SelectedItem as SelectorItem)?.Kind == UnitSelectorKind.HighestHealingAbsorb;
    }

    private void UpdateThresholdPresentation()
    {
        var usesHealingAbsorb = IsHealingAbsorbSelector();
        if (usesHealingAbsorb != _usesHealingAbsorbThreshold)
        {
            if (usesHealingAbsorb)
            {
                _thresholdBox.Minimum = 0;
                _thresholdBox.Value = 0;
            }
            else
            {
                _thresholdBox.Value = 100;
                _thresholdBox.Minimum = 1;
            }

            _usesHealingAbsorbThreshold = usesHealingAbsorb;
        }

        _thresholdLabel.Text = usesHealingAbsorb
            ? "治疗吸收阈值 (>)"
            : "血量阈值 (<)";
    }

    private bool ApplyThreshold(ModuleUnit unit)
    {
        if (TryReadThreshold(out var fixedValue, out var field))
        {
            unit.HealthThreshold = fixedValue;
            unit.HealthThresholdField = field;
            return true;
        }

        return false;
    }

    private bool ApplyThreshold(ModuleCountField count)
    {
        if (TryReadThreshold(out var fixedValue, out var field))
        {
            count.HealthThreshold = fixedValue;
            count.HealthThresholdField = field;
            return true;
        }

        return false;
    }

    private bool TryReadThreshold(out int? fixedValue, out string? field)
    {
        if (!IsDynamicThresholdMode())
        {
            fixedValue = (int)_thresholdBox.Value;
            field = null;
            return true;
        }

        fixedValue = null;
        field = _thresholdFieldBox.SelectedItem?.ToString()?.Trim();
        if (!string.IsNullOrWhiteSpace(field))
        {
            return true;
        }

        MessageBox.Show("请选择动态阈值。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private bool SupportsHealthName()
        => !IsCountCategory
            && !IsEnemyCountCategory
            && (_selectorBox.SelectedItem as SelectorItem)?.Kind == UnitSelectorKind.LowestHealth;

    private static bool RequiresAura(CountKind kind)
        => kind is CountKind.UnitsWithoutAuraBelowHealth
            or CountKind.UnitsWithAuraBelowHealth
            or CountKind.UnitsWithAura
            or CountKind.UnitsWithoutAuraAboveHealingAbsorb
            or CountKind.UnitsWithAuraAboveHealingAbsorb;

    private static bool UnitRequiresAura(UnitSelectorKind kind)
        => kind is UnitSelectorKind.LowestHealthWithAnyAura
            or UnitSelectorKind.LowestHealthWithoutAnyAura
            or UnitSelectorKind.LowestHealthWithoutAura
            or UnitSelectorKind.LowestHealthWithAura
            or UnitSelectorKind.LowestHealthWithAuraCount
            or UnitSelectorKind.HighestHealingAbsorbWithAnyAura
            or UnitSelectorKind.HighestHealingAbsorbWithoutAnyAura
            or UnitSelectorKind.HighestHealingAbsorbWithoutAura
            or UnitSelectorKind.HighestHealingAbsorbWithAura
            or UnitSelectorKind.HighestHealingAbsorbWithAuraCount
            or UnitSelectorKind.UnitWithRoleWithoutAura
            or UnitSelectorKind.UnitWithAura
            or UnitSelectorKind.UnitWithAuraShortest;

    private long? SelectedAura()
        => TryReadAuraSpellId(_auraBox.SelectedItem, out var spellId) ? spellId : null;

    private LowestHealthAuraFilterKind SelectedLowestHealthAuraFilter()
        => (_lowestHealthAuraFilterBox.SelectedItem as LowestHealthAuraFilterItem)?.Kind
            ?? LowestHealthAuraFilterKind.None;

    private UnitRoleFilterKind? SelectedLowestHealthRoleFilter()
        => (_lowestHealthRoleFilterBox.SelectedItem as LowestHealthRoleFilterItem)?.Kind;

    private EnemyThresholdFilterKind SelectedEnemyHealthFilter()
        => (_enemyHealthFilterBox.SelectedItem as EnemyThresholdFilterItem)?.Kind ?? EnemyThresholdFilterKind.None;

    private EnemyThresholdFilterKind SelectedEnemyRangeFilter()
        => (_enemyRangeFilterBox.SelectedItem as EnemyThresholdFilterItem)?.Kind ?? EnemyThresholdFilterKind.None;

    private EnemyCombatFilterKind SelectedEnemyCombatFilter()
        => (_enemyCombatFilterBox.SelectedItem as EnemyCombatFilterItem)?.Kind ?? EnemyCombatFilterKind.None;

    private EnemyAuraFilterKind SelectedEnemyAuraFilter()
        => (_enemyAuraFilterBox.SelectedItem as EnemyAuraFilterItem)?.Kind ?? EnemyAuraFilterKind.None;

    private List<long> SingleAuraList() => SingleAuraList(_auraBox);

    private static List<long> SingleAuraList(UiDropDown box)
    {
        return TryReadAuraSpellId(box.SelectedItem, out var spellId)
            ? new List<long> { spellId }
            : new List<long>();
    }

    private List<long> CheckedAuras() => CheckedAuras(_aurasBox);

    private static List<long> CheckedAuras(CheckedListBox box)
    {
        var list = new List<long>();
        foreach (var item in box.CheckedItems)
        {
            if (TryReadAuraSpellId(item, out var spellId) && !list.Contains(spellId))
            {
                list.Add(spellId);
            }
        }

        return list;
    }

    private int SelectedRole() => (_roleBox.SelectedItem as RoleOption)?.Value ?? 1;

    private int SelectedDispelType() => (_dispelTypeBox.SelectedItem as DispelTypeOption)?.Value ?? 1;

    private static UnitSelectorKind DisplaySelectorKind(UnitSelectorKind kind)
        => kind is UnitSelectorKind.LowestHealthWithAnyAura
            or UnitSelectorKind.LowestHealthWithoutAnyAura
            or UnitSelectorKind.LowestHealthWithoutAura
            or UnitSelectorKind.LowestHealthWithAura
            or UnitSelectorKind.LowestHealthWithAuraCount
                ? UnitSelectorKind.LowestHealth
            : kind is UnitSelectorKind.HighestHealingAbsorbWithAnyAura
                or UnitSelectorKind.HighestHealingAbsorbWithoutAnyAura
                or UnitSelectorKind.HighestHealingAbsorbWithoutAura
                or UnitSelectorKind.HighestHealingAbsorbWithAura
                or UnitSelectorKind.HighestHealingAbsorbWithAuraCount
                    ? UnitSelectorKind.HighestHealingAbsorb
                    : kind;

    private void SelectLowestHealthAuraFilter(UnitSelectorKind kind)
    {
        var filter = kind switch
        {
            UnitSelectorKind.LowestHealthWithAnyAura => LowestHealthAuraFilterKind.WithAnyAura,
            UnitSelectorKind.LowestHealthWithoutAnyAura => LowestHealthAuraFilterKind.WithoutAnyAura,
            UnitSelectorKind.LowestHealthWithoutAura => LowestHealthAuraFilterKind.WithoutAura,
            UnitSelectorKind.LowestHealthWithAura => LowestHealthAuraFilterKind.WithAura,
            UnitSelectorKind.LowestHealthWithAuraCount => LowestHealthAuraFilterKind.WithAuraCount,
            UnitSelectorKind.HighestHealingAbsorbWithAnyAura => LowestHealthAuraFilterKind.WithAnyAura,
            UnitSelectorKind.HighestHealingAbsorbWithoutAnyAura => LowestHealthAuraFilterKind.WithoutAnyAura,
            UnitSelectorKind.HighestHealingAbsorbWithoutAura => LowestHealthAuraFilterKind.WithoutAura,
            UnitSelectorKind.HighestHealingAbsorbWithAura => LowestHealthAuraFilterKind.WithAura,
            UnitSelectorKind.HighestHealingAbsorbWithAuraCount => LowestHealthAuraFilterKind.WithAuraCount,
            _ => LowestHealthAuraFilterKind.None
        };

        for (var i = 0; i < _lowestHealthAuraFilterBox.Items.Count; i++)
        {
            if (_lowestHealthAuraFilterBox.Items[i] is LowestHealthAuraFilterItem item && item.Kind == filter)
            {
                _lowestHealthAuraFilterBox.SelectedIndex = i;
                return;
            }
        }

        _lowestHealthAuraFilterBox.SelectedIndex = 0;
    }

    private void SelectLowestHealthRoleFilter(UnitRoleFilterKind? kind)
    {
        for (var i = 0; i < _lowestHealthRoleFilterBox.Items.Count; i++)
        {
            if (_lowestHealthRoleFilterBox.Items[i] is LowestHealthRoleFilterItem item && item.Kind == kind)
            {
                _lowestHealthRoleFilterBox.SelectedIndex = i;
                return;
            }
        }

        _lowestHealthRoleFilterBox.SelectedIndex = 0;
    }

    private void SelectSelector(UnitSelectorKind kind)
    {
        for (var i = 0; i < _selectorBox.Items.Count; i++)
        {
            if (_selectorBox.Items[i] is SelectorItem item && item.Kind == kind)
            {
                _selectorBox.SelectedIndex = i;
                return;
            }
        }
    }

    private void SelectSelector(CountKind kind)
    {
        for (var i = 0; i < _selectorBox.Items.Count; i++)
        {
            if (_selectorBox.Items[i] is CountItem item && item.Kind == kind)
            {
                _selectorBox.SelectedIndex = i;
                return;
            }
        }
    }

    private void SeedThresholdField(string? field)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            SelectThresholdMode(usesDynamicField: false);
            return;
        }

        SelectThresholdMode(usesDynamicField: true);
        SelectThresholdField(field.Trim());
    }

    private void SelectThresholdMode(bool usesDynamicField)
        => SelectThresholdMode(_thresholdModeBox, usesDynamicField);

    private static void SelectThresholdMode(UiDropDown box, bool usesDynamicField)
    {
        for (var i = 0; i < box.Items.Count; i++)
        {
            if (box.Items[i] is ThresholdModeItem item && item.UsesDynamicField == usesDynamicField)
            {
                box.SelectedIndex = i;
                return;
            }
        }

        box.SelectedIndex = 0;
    }

    private void SelectThresholdField(string field)
        => SelectThresholdField(_thresholdFieldBox, field);

    private static void SelectThresholdField(UiDropDown box, string field)
    {
        var index = box.Items.IndexOf(field);
        if (index < 0)
        {
            box.Items.Add(field);
            index = box.Items.Count - 1;
        }

        box.SelectedIndex = index;
    }

    private static void SelectEnemyThresholdFilter(UiDropDown box, EnemyThresholdFilterKind kind)
    {
        for (var i = 0; i < box.Items.Count; i++)
        {
            if (box.Items[i] is EnemyThresholdFilterItem item && item.Kind == kind)
            {
                box.SelectedIndex = i;
                return;
            }
        }

        box.SelectedIndex = 0;
    }

    private void SelectEnemyCombatFilter(EnemyCombatFilterKind kind)
    {
        for (var i = 0; i < _enemyCombatFilterBox.Items.Count; i++)
        {
            if (_enemyCombatFilterBox.Items[i] is EnemyCombatFilterItem item && item.Kind == kind)
            {
                _enemyCombatFilterBox.SelectedIndex = i;
                return;
            }
        }

        _enemyCombatFilterBox.SelectedIndex = 0;
    }

    private void SelectEnemyAuraFilter(EnemyAuraFilterKind kind)
    {
        for (var i = 0; i < _enemyAuraFilterBox.Items.Count; i++)
        {
            if (_enemyAuraFilterBox.Items[i] is EnemyAuraFilterItem item && item.Kind == kind)
            {
                _enemyAuraFilterBox.SelectedIndex = i;
                return;
            }
        }

        _enemyAuraFilterBox.SelectedIndex = 0;
    }

    private void SelectRole(int role)
    {
        for (var i = 0; i < _roleBox.Items.Count; i++)
        {
            if (_roleBox.Items[i] is RoleOption option && option.Value == role)
            {
                _roleBox.SelectedIndex = i;
                return;
            }
        }
    }

    private void SelectDispelType(int dispelType)
    {
        for (var i = 0; i < _dispelTypeBox.Items.Count; i++)
        {
            if (_dispelTypeBox.Items[i] is DispelTypeOption option && option.Value == dispelType)
            {
                _dispelTypeBox.SelectedIndex = i;
                return;
            }
        }

        _dispelTypeBox.SelectedIndex = 0;
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

    private void CheckAuras(List<long>? auraSpellIds) => CheckAuras(_aurasBox, auraSpellIds);

    private static void CheckAuras(CheckedListBox box, IReadOnlyList<long>? auraSpellIds)
    {
        if (auraSpellIds is null)
        {
            return;
        }

        foreach (var auraSpellId in auraSpellIds)
        {
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
                index = box.Items.Add(UnknownAura(auraSpellId));
            }

            box.SetItemChecked(index, true);
        }
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

    private long? ResolveLegacyAura(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var matches = _auraFields
            .Where(field => string.Equals(
                field.DisplayName.Split(" / ", 2, StringSplitOptions.TrimEntries)[0],
                name.Trim(),
                StringComparison.Ordinal))
            .Select(field => TryReadAuraSpellId(field, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static decimal Clamp(int value, NumericUpDown box)
    {
        return Math.Clamp(value, (int)box.Minimum, (int)box.Maximum);
    }

    private static Panel BuildLabeledRow(string label, Control control, int height = 44)
    {
        var panel = new Panel
        {
            Width = RowWidth,
            Height = height,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0, 1, 0, 5)
        };

        var labelControl = new Label
        {
            Text = label,
            ForeColor = UiTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            Bounds = new Rectangle(0, height > 50 ? 4 : Math.Max(0, (height - 24) / 2), LabelWidth, 24),
            AutoEllipsis = true
        };

        control.Bounds = new Rectangle(ControlLeft, 3, RowWidth - ControlLeft, height - 6);
        panel.Controls.Add(control);
        panel.Controls.Add(labelControl);
        return panel;
    }

    private Control BuildSplitRow(string labelA, Control controlA, string labelB, Control controlB)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0)
        };

        var labelAControl = new Label { Text = labelA, ForeColor = UiTheme.Muted, TextAlign = ContentAlignment.MiddleLeft, Bounds = new Rectangle(0, 5, 72, 28), AutoEllipsis = true };
        controlA.Bounds = new Rectangle(80, 5, 230, 28);
        var labelBControl = new Label { Text = labelB, ForeColor = UiTheme.Muted, TextAlign = ContentAlignment.MiddleLeft, Bounds = new Rectangle(330, 5, 130, 28), AutoEllipsis = true };
        controlB.Bounds = new Rectangle(466, 5, RowWidth - 466, 28);
        if (ReferenceEquals(controlB, _selectorBox))
        {
            // 敌人数量没有选择器, 需要连标签一起隐藏。
            _selectorLabel = labelBControl;
        }

        panel.Controls.Add(controlA);
        panel.Controls.Add(labelAControl);
        panel.Controls.Add(controlB);
        panel.Controls.Add(labelBControl);
        return panel;
    }

    private Control BuildNameRow()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0)
        };

        var nameLabel = new Label { Text = "名称", ForeColor = UiTheme.Muted, TextAlign = ContentAlignment.MiddleLeft, Bounds = new Rectangle(0, 5, 72, 28), AutoEllipsis = true };
        UiTheme.StyleTextBox(_nameBox);
        _nameBox.Bounds = new Rectangle(80, 5, 230, 28);
        _healthNameLabel.Text = "值名称";
        _healthNameLabel.ForeColor = UiTheme.Muted;
        _healthNameLabel.TextAlign = ContentAlignment.MiddleLeft;
        _healthNameLabel.Bounds = new Rectangle(330, 5, 130, 28);
        _healthNameLabel.AutoEllipsis = true;
        UiTheme.StyleTextBox(_healthNameBox);
        _healthNameBox.Bounds = new Rectangle(466, 5, RowWidth - 466, 28);
        _toolTip.SetToolTip(_healthNameBox, "可选：把该单位生命值暴露为同名数值条件字段（如 最低血量 < 50）");
        _toolTip.SetToolTip(_healthNameLabel, "可选：把该单位生命值暴露为同名数值条件字段（如 最低血量 < 50）");

        panel.Controls.Add(_nameBox);
        panel.Controls.Add(nameLabel);
        panel.Controls.Add(_healthNameBox);
        panel.Controls.Add(_healthNameLabel);
        return panel;
    }

    private sealed record SelectorItem(string Text, UnitSelectorKind Kind)
    {
        public override string ToString() => Text;
    }

    private sealed record LowestHealthAuraFilterItem(string Text, LowestHealthAuraFilterKind Kind)
    {
        public override string ToString() => Text;
    }

    private sealed record LowestHealthRoleFilterItem(string Text, UnitRoleFilterKind? Kind)
    {
        public override string ToString() => Text;
    }

    private sealed record CountItem(string Text, CountKind Kind)
    {
        public override string ToString() => Text;
    }

    private sealed record ThresholdModeItem(string Text, bool UsesDynamicField)
    {
        public override string ToString() => Text;
    }

    private sealed record EnemyThresholdFilterItem(string Text, EnemyThresholdFilterKind Kind)
    {
        public override string ToString() => Text;
    }

    private sealed record EnemyAuraFilterItem(string Text, EnemyAuraFilterKind Kind)
    {
        public override string ToString() => Text;
    }

    private sealed record EnemyCombatFilterItem(string Text, EnemyCombatFilterKind Kind)
    {
        public override string ToString() => Text;
    }

    /// <summary>一组「阈值类型 + 固定阈值 + 动态阈值」控件与其所在的三行。</summary>
    private sealed class ThresholdGroup
    {
        public UiDropDown ModeBox { get; } = new();
        public NumericUpDown ValueBox { get; } = new();
        public UiDropDown FieldBox { get; } = new();
        public Panel ModeRow { get; set; } = null!;
        public Panel ValueRow { get; set; } = null!;
        public Panel FieldRow { get; set; } = null!;

        public bool UsesDynamicField => (ModeBox.SelectedItem as ThresholdModeItem)?.UsesDynamicField == true;
    }

    private sealed record RoleOption(string Text, int Value)
    {
        public override string ToString() => Text;
    }

    private sealed record DispelTypeOption(string Text, int Value)
    {
        public override string ToString() => Text;
    }

    private enum LowestHealthAuraFilterKind
    {
        None,
        WithAnyAura,
        WithoutAnyAura,
        WithoutAura,
        WithAura,
        WithAuraCount
    }
}
