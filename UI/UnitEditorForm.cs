using System.Drawing;

namespace Shigure;

/// <summary>
/// 队友单位、数量与平均血量字段的编辑弹窗：按类别和统计对象动态显隐筛选控件。
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
        new("正序首个", UnitSelectorKind.UnitWithRole),
        new("逆序首个", UnitSelectorKind.UnitWithRole, true)
    ];

    private static readonly UnitTargetItem[] HealthTargetOptions =
    [
        new("不参与目标选择", UnitSelectorKind.UnitWithRole),
        new("生命值最低", UnitSelectorKind.LowestHealth)
    ];

    private static readonly UnitTargetItem[] HealingAbsorbTargetOptions =
    [
        new("不参与目标选择", UnitSelectorKind.UnitWithRole),
        new("治疗吸收最高", UnitSelectorKind.HighestHealingAbsorb)
    ];

    private static readonly UnitOrderItem[] UnitOrderOptions =
    [
        new("正序首个", false),
        new("逆序首个", true)
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

    private static readonly EnemyThresholdFilterItem[] AllyHealingAbsorbFilterOptions =
    [
        new("不筛选治疗吸收", EnemyThresholdFilterKind.None),
        new("治疗吸收大于", EnemyThresholdFilterKind.Above),
        new("治疗吸收小于", EnemyThresholdFilterKind.Below)
    ];

    private static readonly AllyDispelFilterItem[] AllyDispelFilterOptions =
    [
        new("不筛选驱散", AllyDispelFilterKind.None),
        new("有某驱散类型", AllyDispelFilterKind.WithType),
        new("没有某驱散类型", AllyDispelFilterKind.WithoutType)
    ];

    private static readonly AuraDurationFilterItem[] AuraDurationFilterOptions =
    [
        new("不筛选光环时长", AuraDurationFilterKind.None),
        new("持续最长", AuraDurationFilterKind.Longest),
        new("持续最短", AuraDurationFilterKind.Shortest)
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
        new("没有任一光环 (多选)", EnemyAuraFilterKind.WithoutAnyAura)
    ];

    private static readonly AverageTargetItem[] AverageTargetOptions =
    [
        new("队友", AverageHealthTargetKind.Allies),
        new("敌人", AverageHealthTargetKind.Enemies)
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
    private readonly UiDropDown _healthTargetBox = new();
    private readonly UiDropDown _enemyAuraFilterBox = new();
    private readonly UiDropDown _enemyRangeFilterBox = new();
    private readonly UiDropDown _enemyCombatFilterBox = new();
    private readonly UiDropDown _enemyAuraBox = new();
    private readonly CheckedListBox _enemyAurasBox = new();
    private readonly ThresholdGroup _enemyHealthThreshold = new();
    private readonly ThresholdGroup _enemyRangeThreshold = new();
    private readonly UiDropDown _allyHealingAbsorbFilterBox = new();
    private readonly UiDropDown _healingAbsorbTargetBox = new();
    private readonly ThresholdGroup _allyHealingAbsorbThreshold = new();
    private readonly UiDropDown _allyDispelFilterBox = new();
    private readonly UiDropDown _allyDispelTypeBox = new();
    private readonly UiDropDown _auraDurationFilterBox = new();
    private readonly UiDropDown _auraDurationAuraBox = new();
    private readonly UiDropDown _roleOrderBox = new();
    private readonly UiDropDown _dispelOrderBox = new();
    private readonly UiDropDown _auraOrderBox = new();

    private Label _selectorLabel = null!;
    private Panel _enemyHealthFilterRow = null!;
    private Panel _healthTargetRow = null!;
    private Panel _enemyAuraFilterRow = null!;
    private Panel _enemyRangeFilterRow = null!;
    private Panel _enemyCombatFilterRow = null!;
    private Panel _enemyAuraRow = null!;
    private Panel _enemyAurasRow = null!;
    private Panel _auraDurationFilterRow = null!;
    private Panel _auraDurationAuraRow = null!;
    private Panel _healingAbsorbTargetRow = null!;
    private Panel _roleOrderRow = null!;
    private Panel _dispelOrderRow = null!;
    private Panel _auraOrderRow = null!;
    private UiCardPanel _enemyHealthSection = null!;
    private UiCardPanel _enemyAuraSection = null!;
    private UiCardPanel _auraDurationSection = null!;
    private UiCardPanel _enemyRangeSection = null!;
    private UiCardPanel _enemyCombatSection = null!;
    private UiCardPanel _thresholdSection = null!;
    private UiCardPanel _allyAuraSection = null!;
    private UiCardPanel _allyRoleSection = null!;
    private UiCardPanel _orderSection = null!;
    private UiCardPanel _dispelSection = null!;
    private UiCardPanel _allyHealingAbsorbSection = null!;
    private UiCardPanel _allyDispelSection = null!;
    private Label _thresholdSectionTitle = null!;
    private Panel _allyHealingAbsorbFilterRow = null!;
    private Panel _allyDispelFilterRow = null!;
    private Panel _allyDispelTypeRow = null!;
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
    private bool _syncingUnitSelectionControls;

    public ModuleUnit? ResultUnit { get; private set; }
    public ModuleCountField? ResultCount { get; private set; }
    public ModuleEnemyCountField? ResultEnemyCount { get; private set; }
    public ModuleAverageHealthField? ResultAverageHealth { get; private set; }

    public UnitEditorForm(
        IReadOnlyList<ConditionField> auraFields,
        IReadOnlyList<ConditionField> nameplateAuraFields,
        IReadOnlyList<string> thresholdFields,
        IReadOnlyCollection<string> takenNames,
        ModuleUnit? existingUnit,
        ModuleCountField? existingCount,
        ModuleEnemyCountField? existingEnemyCount,
        ModuleAverageHealthField? existingAverageHealth = null)
    {
        _auraFields = auraFields;
        _nameplateAuraFields = nameplateAuraFields;
        _thresholdFields = thresholdFields;
        _takenNames = new HashSet<string>(takenNames, StringComparer.OrdinalIgnoreCase);
        InitializeComponent();
        Seed(existingUnit, existingCount, existingEnemyCount, existingAverageHealth);
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
        Text = "编辑单位与统计";
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
        _categoryBox.Items.AddRange(["队友单位", "队友数量", "敌人数量", "平均血量"]);
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
            if (IsAverageHealthCategory)
            {
                PopulateFilterAuras();
            }
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
        InitializeUnitOrderBox(_roleOrderBox);

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
        BuildAllyCountParamRows();

        _thresholdSection = BuildFilterSection("生命值", _thresholdModeRow, _thresholdRow, _thresholdFieldRow);
        _thresholdSectionTitle = _thresholdSection.Controls.OfType<Label>().Single();
        _allyAuraSection = BuildFilterSection(
            "光环",
            _lowestHealthAuraFilterRow,
            _auraRow,
            _aurasRow,
            _auraCountRow);
        _roleOrderRow = BuildLabeledRow("选择顺序", _roleOrderBox);
        _allyRoleSection = BuildFilterSection("职责", _lowestHealthRoleFilterRow, _roleRow, _roleOrderRow);
        _orderSection = BuildFilterSection("顺序", _reverseRow);
        _dispelSection = BuildFilterSection("驱散", _dispelRow);

        _paramPanel.Controls.AddRange([
            _thresholdSection,
            _allyAuraSection,
            _enemyHealthSection,
            _allyHealingAbsorbSection,
            _allyRoleSection,
            _allyDispelSection,
            _enemyAuraSection,
            _auraDurationSection,
            _orderSection,
            _dispelSection,
            _enemyRangeSection,
            _enemyCombatSection
        ]);
    }

    private void BuildAllyCountParamRows()
    {
        UiTheme.StyleComboBox(_allyHealingAbsorbFilterBox);
        _allyHealingAbsorbFilterBox.DropDownWidth = 240;
        _allyHealingAbsorbFilterBox.Items.AddRange(AllyHealingAbsorbFilterOptions.Cast<object>().ToArray());
        _allyHealingAbsorbFilterBox.SelectedIndex = 0;
        _allyHealingAbsorbFilterBox.SelectedIndexChanged += (_, _) => UpdateParamVisibility();
        UiTheme.StyleComboBox(_healingAbsorbTargetBox);
        _healingAbsorbTargetBox.DropDownWidth = 220;
        _healingAbsorbTargetBox.Items.AddRange(HealingAbsorbTargetOptions.Cast<object>().ToArray());
        _healingAbsorbTargetBox.SelectedIndex = 0;
        _healingAbsorbTargetBox.SelectedIndexChanged += (_, _) => HandleUnitTargetSelectionChanged(UnitTargetSource.HealingAbsorb);
        InitializeThresholdGroup(_allyHealingAbsorbThreshold, "治疗吸收阈值", 0);
        _allyHealingAbsorbFilterRow = BuildLabeledRow("治疗吸收筛选", _allyHealingAbsorbFilterBox);
        _healingAbsorbTargetRow = BuildLabeledRow("目标选择", _healingAbsorbTargetBox);
        _allyHealingAbsorbSection = BuildFilterSection(
            "治疗吸收",
            _allyHealingAbsorbFilterRow,
            _allyHealingAbsorbThreshold.ModeRow,
            _allyHealingAbsorbThreshold.ValueRow,
            _allyHealingAbsorbThreshold.FieldRow,
            _healingAbsorbTargetRow);

        UiTheme.StyleComboBox(_allyDispelFilterBox);
        _allyDispelFilterBox.DropDownWidth = 220;
        _allyDispelFilterBox.Items.AddRange(AllyDispelFilterOptions.Cast<object>().ToArray());
        _allyDispelFilterBox.SelectedIndex = 0;
        _allyDispelFilterBox.SelectedIndexChanged += (_, _) => UpdateParamVisibility();

        UiTheme.StyleComboBox(_allyDispelTypeBox);
        _allyDispelTypeBox.DropDownWidth = 180;
        _allyDispelTypeBox.Items.AddRange(DispelTypeOptions.Cast<object>().ToArray());
        _allyDispelTypeBox.SelectedIndex = 0;
        _allyDispelTypeBox.SelectedIndexChanged += (_, _) => UpdatePreview();
        InitializeUnitOrderBox(_dispelOrderBox);

        _allyDispelFilterRow = BuildLabeledRow("驱散筛选", _allyDispelFilterBox);
        _allyDispelTypeRow = BuildLabeledRow("驱散类型", _allyDispelTypeBox);
        _dispelOrderRow = BuildLabeledRow("选择顺序", _dispelOrderBox);
        _allyDispelSection = BuildFilterSection("驱散", _allyDispelFilterRow, _allyDispelTypeRow, _dispelOrderRow);
    }

    // 敌人数量: 生命值 / 光环 / 距离 / 战斗 四组筛选彼此独立, 前三组各自带一套阈值控件。
    private void BuildEnemyParamRows()
    {
        UiTheme.StyleComboBox(_enemyHealthFilterBox);
        _enemyHealthFilterBox.DropDownWidth = 220;
        _enemyHealthFilterBox.Items.AddRange(EnemyHealthFilterOptions.Cast<object>().ToArray());
        _enemyHealthFilterBox.SelectedIndex = 0;
        _enemyHealthFilterBox.SelectedIndexChanged += (_, _) => UpdateParamVisibility();
        UiTheme.StyleComboBox(_healthTargetBox);
        _healthTargetBox.DropDownWidth = 220;
        _healthTargetBox.Items.AddRange(HealthTargetOptions.Cast<object>().ToArray());
        _healthTargetBox.SelectedIndex = 0;
        _healthTargetBox.SelectedIndexChanged += (_, _) => HandleUnitTargetSelectionChanged(UnitTargetSource.Health);

        UiTheme.StyleComboBox(_enemyAuraFilterBox);
        _enemyAuraFilterBox.DropDownWidth = 220;
        _enemyAuraFilterBox.Items.AddRange(EnemyAuraFilterOptions.Cast<object>().ToArray());
        _enemyAuraFilterBox.SelectedIndex = 0;
        _enemyAuraFilterBox.SelectedIndexChanged += (_, _) => UpdateParamVisibility();
        InitializeUnitOrderBox(_auraOrderBox);

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
        PopulateFilterAuras();

        _enemyAuraBox.SelectedIndexChanged += (_, _) => UpdatePreview();
        _enemyAurasBox.ItemCheck += (_, _) =>
        {
            if (IsHandleCreated)
            {
                BeginInvoke(new Action(UpdatePreview));
            }
        };

        UiTheme.StyleComboBox(_auraDurationFilterBox);
        _auraDurationFilterBox.DropDownWidth = 220;
        _auraDurationFilterBox.Items.AddRange(AuraDurationFilterOptions.Cast<object>().ToArray());
        _auraDurationFilterBox.SelectedIndex = 0;
        _auraDurationFilterBox.SelectedIndexChanged += (_, _) => HandleUnitTargetSelectionChanged(UnitTargetSource.AuraDuration);

        UiTheme.StyleComboBox(_auraDurationAuraBox);
        _auraDurationAuraBox.DropDownWidth = 360;
        _auraDurationAuraBox.SelectedIndexChanged += (_, _) => UpdatePreview();

        InitializeThresholdGroup(_enemyHealthThreshold, "生命值阈值", 0);
        InitializeThresholdGroup(_enemyRangeThreshold, "距离阈值", 0);

        _enemyHealthFilterRow = BuildLabeledRow("生命值筛选", _enemyHealthFilterBox);
        _healthTargetRow = BuildLabeledRow("目标选择", _healthTargetBox);
        _enemyAuraFilterRow = BuildLabeledRow("光环筛选", _enemyAuraFilterBox);
        _enemyRangeFilterRow = BuildLabeledRow("距离筛选", _enemyRangeFilterBox);
        _enemyCombatFilterRow = BuildLabeledRow("战斗筛选", _enemyCombatFilterBox);
        _enemyAuraRow = BuildLabeledRow("光环", _enemyAuraBox);
        _enemyAurasRow = BuildLabeledRow("光环 (可多选)", _enemyAurasBox, 116);
        _auraDurationAuraRow = BuildLabeledRow("光环", _auraDurationAuraBox);
        _auraDurationFilterRow = BuildLabeledRow("时长筛选", _auraDurationFilterBox);
        _auraOrderRow = BuildLabeledRow("选择顺序", _auraOrderBox);

        _enemyHealthSection = BuildFilterSection(
            "生命值",
            _enemyHealthFilterRow,
            _enemyHealthThreshold.ModeRow,
            _enemyHealthThreshold.ValueRow,
            _enemyHealthThreshold.FieldRow,
            _healthTargetRow);
        _enemyAuraSection = BuildFilterSection(
            "光环存在",
            _enemyAuraFilterRow,
            _enemyAuraRow,
            _enemyAurasRow,
            _auraOrderRow);
        _auraDurationSection = BuildFilterSection(
            "光环时长",
            _auraDurationAuraRow,
            _auraDurationFilterRow);
        _enemyRangeSection = BuildFilterSection(
            "距离",
            _enemyRangeFilterRow,
            _enemyRangeThreshold.ModeRow,
            _enemyRangeThreshold.ValueRow,
            _enemyRangeThreshold.FieldRow);
        _enemyCombatSection = BuildFilterSection("战斗", _enemyCombatFilterRow);
    }

    private void PopulateFilterAuras()
    {
        var source = IsEnemyCountCategory
            || IsAverageHealthCategory && SelectedAverageTarget() == AverageHealthTargetKind.Enemies
            ? _nameplateAuraFields
            : _auraFields;
        _enemyAuraBox.BeginUpdate();
        _enemyAurasBox.BeginUpdate();
        _auraDurationAuraBox.BeginUpdate();
        try
        {
            _enemyAuraBox.Items.Clear();
            _enemyAurasBox.Items.Clear();
            _auraDurationAuraBox.Items.Clear();
            foreach (var aura in source)
            {
                _enemyAuraBox.Items.Add(aura);
                _enemyAurasBox.Items.Add(aura);
                _auraDurationAuraBox.Items.Add(aura);
            }

            if (_enemyAuraBox.Items.Count > 0)
            {
                _enemyAuraBox.SelectedIndex = 0;
            }

            if (_auraDurationAuraBox.Items.Count > 0)
            {
                _auraDurationAuraBox.SelectedIndex = 0;
            }
        }
        finally
        {
            _enemyAuraBox.EndUpdate();
            _enemyAurasBox.EndUpdate();
            _auraDurationAuraBox.EndUpdate();
        }
    }

    private static UiCardPanel BuildFilterSection(string title, params Control[] rows)
    {
        var section = new UiCardPanel
        {
            Width = RowWidth + 8,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(4, 4, 4, 6),
            Margin = new Padding(0, 3, 0, 7),
            ColumnCount = 1,
            RowCount = 2
        };
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        section.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titleLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            ForeColor = UiTheme.Text,
            Text = title,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0),
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(0)
        };

        var body = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        foreach (var row in rows)
        {
            body.Controls.Add(row);
        }

        section.Controls.Add(titleLabel, 0, 0);
        section.Controls.Add(body, 0, 1);
        return section;
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

    private void InitializeUnitOrderBox(UiDropDown box)
    {
        UiTheme.StyleComboBox(box);
        box.DropDownWidth = 160;
        box.Items.AddRange(UnitOrderOptions.Cast<object>().ToArray());
        box.SelectedIndex = 0;
        box.SelectedIndexChanged += (_, _) => SynchronizeUnitOrder(box);
    }

    private void HandleUnitTargetSelectionChanged(UnitTargetSource source)
    {
        if (_syncingUnitSelectionControls)
        {
            return;
        }

        var activated = source switch
        {
            UnitTargetSource.Health => SelectedHealthTarget() == UnitSelectorKind.LowestHealth,
            UnitTargetSource.HealingAbsorb => SelectedHealingAbsorbTarget() == UnitSelectorKind.HighestHealingAbsorb,
            UnitTargetSource.AuraDuration => SelectedAuraDurationFilter() is AuraDurationFilterKind.Longest
                or AuraDurationFilterKind.Shortest,
            _ => false
        };
        if (IsUnitCategory && activated)
        {
            _syncingUnitSelectionControls = true;
            try
            {
                if (source != UnitTargetSource.Health)
                {
                    _healthTargetBox.SelectedIndex = 0;
                }

                if (source != UnitTargetSource.HealingAbsorb)
                {
                    _healingAbsorbTargetBox.SelectedIndex = 0;
                }

                if (source != UnitTargetSource.AuraDuration)
                {
                    _auraDurationFilterBox.SelectedIndex = 0;
                }

                SetUnitOrderCore(reverse: false);
            }
            finally
            {
                _syncingUnitSelectionControls = false;
            }
        }

        UpdateParamVisibility();
        UpdateHealthNameState();
    }

    private void SynchronizeUnitOrder(UiDropDown source)
    {
        if (_syncingUnitSelectionControls)
        {
            return;
        }

        var reverse = (source.SelectedItem as UnitOrderItem)?.Reverse == true;
        _syncingUnitSelectionControls = true;
        try
        {
            SetUnitOrderCore(reverse);
        }
        finally
        {
            _syncingUnitSelectionControls = false;
        }

        UpdatePreview();
    }

    private void SetUnitOrderCore(bool reverse)
    {
        var index = reverse ? 1 : 0;
        _roleOrderBox.SelectedIndex = index;
        _dispelOrderBox.SelectedIndex = index;
        _auraOrderBox.SelectedIndex = index;
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
        // 只有平均血量需要在页头选择统计对象；队友单位的目标选择已移动到各筛选卡片。
        var hasSelector = IsAverageHealthCategory;
        _selectorBox.Visible = hasSelector;
        _categoryBox.Bounds = hasSelector
            ? new Rectangle(80, 5, 230, 28)
            : new Rectangle(80, 5, RowWidth - 80, 28);
        if (_selectorLabel is not null)
        {
            _selectorLabel.Visible = hasSelector;
            _selectorLabel.Text = "统计对象";
        }

        if (!hasSelector)
        {
            PopulateFilterAuras();
            return;
        }

        if (IsAverageHealthCategory)
        {
            _selectorBox.Items.AddRange(AverageTargetOptions.Cast<object>().ToArray());
        }
        else
        {
            _selectorBox.Items.AddRange(UnitSelectors.Cast<object>().ToArray());
        }

        if (_selectorBox.Items.Count > 0)
        {
            _selectorBox.SelectedIndex = 0;
        }

        PopulateFilterAuras();
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

        if (IsUnitCategory || IsCountCategory || IsEnemyCountCategory || IsAverageHealthCategory)
        {
            // 队友单位隐藏顶部选择器；其他类别显示。
            _selectorBox.Visible = !IsUnitCategory;
            _selectorLabel.Visible = !IsUnitCategory;

            var allyWithExtendedFilters = IsUnitCategory || IsCountCategory;
            var enemyTarget = IsEnemyCountCategory || SelectedAverageTarget() == AverageHealthTargetKind.Enemies;
            UpdateEnemyParamVisibility(enemyTarget, IsUnitCategory);
            UpdateAllyCountParamVisibility(allyWithExtendedFilters);
            _thresholdSection.Visible = false;
            _allyAuraSection.Visible = false;
            _allyRoleSection.Visible = allyWithExtendedFilters || IsAverageHealthCategory && !enemyTarget;
            _orderSection.Visible = false;
            _dispelSection.Visible = false;
            _thresholdModeRow.Visible = false;
            _thresholdRow.Visible = false;
            _thresholdFieldRow.Visible = false;
            _lowestHealthAuraFilterRow.Visible = false;
            _lowestHealthRoleFilterRow.Visible = allyWithExtendedFilters || IsAverageHealthCategory && !enemyTarget;
            _roleRow.Visible = (allyWithExtendedFilters || IsAverageHealthCategory && !enemyTarget)
                && SelectedLowestHealthRoleFilter() is not null;
            var hasValueTarget = HasUnitValueTarget();
            _healthTargetRow.Visible = IsUnitCategory;
            _healingAbsorbTargetRow.Visible = IsUnitCategory;
            _roleOrderRow.Visible = IsUnitCategory && SelectedLowestHealthRoleFilter() is not null;
            _dispelOrderRow.Visible = IsUnitCategory && SelectedAllyDispelFilter() != AllyDispelFilterKind.None;
            _auraOrderRow.Visible = IsUnitCategory && SelectedEnemyAuraFilter() != EnemyAuraFilterKind.None;
            _roleOrderBox.Enabled = !hasValueTarget;
            _dispelOrderBox.Enabled = !hasValueTarget;
            _auraOrderBox.Enabled = !hasValueTarget;
            _reverseRow.Visible = false;
            _auraRow.Visible = false;
            _aurasRow.Visible = false;
            _auraCountRow.Visible = false;
            _dispelRow.Visible = false;
            UpdatePreview();
            return;
        }

        // 旧版筛选路径：显示选择器。
        _selectorBox.Visible = true;
        _selectorLabel.Visible = true;

        SetEnemyRowsVisible(false);
        _allyHealingAbsorbSection.Visible = false;
        _allyDispelSection.Visible = false;
        if (IsCountCategory)
        {
            lowestHealthRoleFilter = true;
            role = SelectedLowestHealthRoleFilter() is not null;
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
        _thresholdSection.Visible = threshold;
        _allyAuraSection.Visible = lowestHealthAuraFilter || auraSingle || auraMulti || auraCount;
        _allyRoleSection.Visible = lowestHealthRoleFilter || role;
        _orderSection.Visible = reverse;
        _dispelSection.Visible = dispel;
        UpdatePreview();
    }

    private void UpdateEnemyParamVisibility(bool enemyTarget, bool allowAuraDuration)
    {
        var healthFilter = SelectedEnemyHealthFilter() != EnemyThresholdFilterKind.None;
        var rangeFilter = SelectedEnemyRangeFilter() != EnemyThresholdFilterKind.None;
        var auraFilter = SelectedEnemyAuraFilter();

        _enemyHealthSection.Visible = true;
        _enemyAuraSection.Visible = true;
        _auraDurationSection.Visible = allowAuraDuration;
        _enemyRangeSection.Visible = enemyTarget;
        _enemyCombatSection.Visible = enemyTarget;
        _enemyHealthFilterRow.Visible = true;
        _healthTargetRow.Visible = allowAuraDuration;
        _enemyAuraFilterRow.Visible = true;
        _enemyRangeFilterRow.Visible = enemyTarget;
        _enemyCombatFilterRow.Visible = enemyTarget;
        SetThresholdGroupVisible(_enemyHealthThreshold, healthFilter);
        SetThresholdGroupVisible(_enemyRangeThreshold, enemyTarget && rangeFilter);
        _enemyAuraRow.Visible = auraFilter is EnemyAuraFilterKind.WithAura or EnemyAuraFilterKind.WithoutAura;
        _enemyAurasRow.Visible = auraFilter is EnemyAuraFilterKind.WithAnyAura or EnemyAuraFilterKind.WithoutAnyAura;
        _auraDurationFilterRow.Visible = allowAuraDuration;
        _auraDurationAuraRow.Visible = allowAuraDuration;
    }

    private void UpdateAllyCountParamVisibility(bool visible)
    {
        _allyHealingAbsorbSection.Visible = visible;
        _allyHealingAbsorbFilterRow.Visible = visible;
        _healingAbsorbTargetRow.Visible = IsUnitCategory;
        SetThresholdGroupVisible(
            _allyHealingAbsorbThreshold,
            visible && SelectedAllyHealingAbsorbFilter() != EnemyThresholdFilterKind.None);

        _allyDispelSection.Visible = visible;
        _allyDispelFilterRow.Visible = visible;
        _allyDispelTypeRow.Visible = visible && SelectedAllyDispelFilter() != AllyDispelFilterKind.None;
        _dispelOrderRow.Visible = IsUnitCategory && SelectedAllyDispelFilter() != AllyDispelFilterKind.None;
    }

    private void SetEnemyRowsVisible(bool visible)
    {
        _enemyHealthSection.Visible = visible;
        _enemyAuraSection.Visible = visible;
        _auraDurationSection.Visible = visible;
        _enemyRangeSection.Visible = visible;
        _enemyCombatSection.Visible = visible;
        _enemyHealthFilterRow.Visible = visible;
        _enemyAuraFilterRow.Visible = visible;
        _enemyRangeFilterRow.Visible = visible;
        _enemyCombatFilterRow.Visible = visible;
        _enemyAuraRow.Visible = visible;
        _enemyAurasRow.Visible = visible;
        _auraDurationFilterRow.Visible = visible;
        _auraDurationAuraRow.Visible = visible;
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

    private ModuleCountField BuildAllyCount(string name)
    {
        var count = new ModuleCountField
        {
            Name = name,
            FilterVersion = ModuleCountField.CurrentFilterVersion,
            HealthFilter = SelectedEnemyHealthFilter(),
            HealingAbsorbFilter = SelectedAllyHealingAbsorbFilter(),
            AuraFilter = SelectedEnemyAuraFilter(),
            RoleFilter = SelectedLowestHealthRoleFilter(),
            Role = SelectedLowestHealthRoleFilter() is null ? null : SelectedRole(),
            DispelFilter = SelectedAllyDispelFilter(),
            DispelType = SelectedAllyDispelFilter() == AllyDispelFilterKind.None
                ? null
                : SelectedAllyDispelType()
        };

        if (count.HealthFilter != EnemyThresholdFilterKind.None)
        {
            ReadThresholdGroup(_enemyHealthThreshold, out var fixedValue, out var field);
            count.HealthThreshold = fixedValue;
            count.HealthThresholdField = field;
        }

        if (count.HealingAbsorbFilter != EnemyThresholdFilterKind.None)
        {
            ReadThresholdGroup(_allyHealingAbsorbThreshold, out var fixedValue, out var field);
            count.HealingAbsorbThreshold = fixedValue;
            count.HealingAbsorbThresholdField = field;
        }

        count.AuraSpellIds = count.AuraFilter switch
        {
            EnemyAuraFilterKind.WithAura or EnemyAuraFilterKind.WithoutAura => SingleAuraList(_enemyAuraBox),
            EnemyAuraFilterKind.WithAnyAura or EnemyAuraFilterKind.WithoutAnyAura => CheckedAuras(_enemyAurasBox),
            _ => null
        };
        return count;
    }

    private ModuleUnit BuildFilteredUnit(string name)
    {
        var auraDurationFilter = SelectedAuraDurationFilter();
        var kind = auraDurationFilter is AuraDurationFilterKind.Longest or AuraDurationFilterKind.Shortest
            ? UnitSelectorKind.UnitWithRole
            : SelectedHealthTarget() == UnitSelectorKind.LowestHealth
                ? UnitSelectorKind.LowestHealth
                : SelectedHealingAbsorbTarget() == UnitSelectorKind.HighestHealingAbsorb
                    ? UnitSelectorKind.HighestHealingAbsorb
                    : UnitSelectorKind.UnitWithRole;
        var hasValueTarget = kind is UnitSelectorKind.LowestHealth or UnitSelectorKind.HighestHealingAbsorb
            || auraDurationFilter is AuraDurationFilterKind.Longest or AuraDurationFilterKind.Shortest;
        var unit = new ModuleUnit
        {
            Name = name,
            FilterVersion = ModuleUnit.CurrentFilterVersion,
            Kind = kind,
            Reverse = !hasValueTarget && HasDirectionalUnitFilter() && SelectedUnitReverse(),
            HealthFilter = SelectedEnemyHealthFilter(),
            HealingAbsorbFilter = SelectedAllyHealingAbsorbFilter(),
            AuraFilter = SelectedEnemyAuraFilter(),
            AuraDurationFilter = auraDurationFilter,
            RoleFilter = SelectedLowestHealthRoleFilter(),
            Role = SelectedLowestHealthRoleFilter() is null ? null : SelectedRole(),
            DispelFilter = SelectedAllyDispelFilter(),
            DispelType = SelectedAllyDispelFilter() == AllyDispelFilterKind.None
                ? null
                : SelectedAllyDispelType()
        };

        if (unit.HealthFilter != EnemyThresholdFilterKind.None)
        {
            ReadThresholdGroup(_enemyHealthThreshold, out var fixedValue, out var field);
            unit.HealthThreshold = fixedValue;
            unit.HealthThresholdField = field;
        }

        if (unit.HealingAbsorbFilter != EnemyThresholdFilterKind.None)
        {
            ReadThresholdGroup(_allyHealingAbsorbThreshold, out var fixedValue, out var field);
            unit.HealingAbsorbThreshold = fixedValue;
            unit.HealingAbsorbThresholdField = field;
        }

        unit.AuraSpellIds = unit.AuraFilter switch
        {
            EnemyAuraFilterKind.WithAura or EnemyAuraFilterKind.WithoutAura => SingleAuraList(_enemyAuraBox),
            EnemyAuraFilterKind.WithAnyAura or EnemyAuraFilterKind.WithoutAnyAura => CheckedAuras(_enemyAurasBox),
            _ => null
        };
        unit.AuraDurationSpellId = unit.AuraDurationFilter is AuraDurationFilterKind.Longest
            or AuraDurationFilterKind.Shortest
                ? TryReadAuraSpellId(_auraDurationAuraBox.SelectedItem, out var durationAuraSpellId)
                    ? durationAuraSpellId
                    : null
                : null;
        return unit;
    }

    private ModuleAverageHealthField BuildAverageHealth(string name)
    {
        var target = SelectedAverageTarget();
        var field = new ModuleAverageHealthField
        {
            Name = name,
            Target = target,
            HealthFilter = SelectedEnemyHealthFilter(),
            AuraFilter = SelectedEnemyAuraFilter(),
            RoleFilter = target == AverageHealthTargetKind.Allies ? SelectedLowestHealthRoleFilter() : null,
            Role = target == AverageHealthTargetKind.Allies && SelectedLowestHealthRoleFilter() is not null
                ? SelectedRole()
                : null,
            RangeFilter = target == AverageHealthTargetKind.Enemies
                ? SelectedEnemyRangeFilter()
                : EnemyThresholdFilterKind.None,
            CombatFilter = target == AverageHealthTargetKind.Enemies
                ? SelectedEnemyCombatFilter()
                : EnemyCombatFilterKind.None
        };

        if (field.HealthFilter != EnemyThresholdFilterKind.None)
        {
            ReadThresholdGroup(_enemyHealthThreshold, out var fixedValue, out var dynamicField);
            field.HealthThreshold = fixedValue;
            field.HealthThresholdField = dynamicField;
        }

        if (field.RangeFilter != EnemyThresholdFilterKind.None)
        {
            ReadThresholdGroup(_enemyRangeThreshold, out var fixedValue, out var dynamicField);
            field.RangeThreshold = fixedValue;
            field.RangeThresholdField = dynamicField;
        }

        field.AuraSpellIds = field.AuraFilter switch
        {
            EnemyAuraFilterKind.WithAura or EnemyAuraFilterKind.WithoutAura => SingleAuraList(_enemyAuraBox),
            EnemyAuraFilterKind.WithAnyAura or EnemyAuraFilterKind.WithoutAnyAura => CheckedAuras(_enemyAurasBox),
            _ => null
        };
        return field;
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
            PopulateFilterAuras();
            SelectEnemyThresholdFilter(_enemyHealthFilterBox, averageHealth.HealthFilter);
            SelectEnemyThresholdFilter(_enemyRangeFilterBox, averageHealth.RangeFilter);
            SelectEnemyAuraFilter(averageHealth.AuraFilter);
            SelectEnemyCombatFilter(averageHealth.CombatFilter);
            SelectLowestHealthRoleFilter(averageHealth.RoleFilter);
            if (averageHealth.Role is { } role)
            {
                SelectRole(role);
            }
            SeedThresholdGroup(_enemyHealthThreshold, averageHealth.HealthFilter, averageHealth.HealthThreshold, averageHealth.HealthThresholdField);
            SeedThresholdGroup(_enemyRangeThreshold, averageHealth.RangeFilter, averageHealth.RangeThreshold, averageHealth.RangeThresholdField);
            var auraSpellIds = averageHealth.AuraSpellIds ?? [];
            SelectAura(_enemyAuraBox, auraSpellIds.Count > 0 ? auraSpellIds[0] : null);
            CheckAuras(_enemyAurasBox, auraSpellIds);
            return;
        }

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
            SelectEnemyThresholdFilter(_enemyHealthFilterBox, count.HealthFilter);
            SelectEnemyThresholdFilter(_allyHealingAbsorbFilterBox, count.HealingAbsorbFilter);
            SelectEnemyAuraFilter(count.AuraFilter);
            SelectLowestHealthRoleFilter(count.RoleFilter);
            if (count.Role is { } role)
            {
                SelectRole(role);
            }
            SelectAllyDispelFilter(count.DispelFilter);
            if (count.DispelType is { } dispelType)
            {
                SelectAllyDispelType(dispelType);
            }

            SeedThresholdGroup(_enemyHealthThreshold, count.HealthFilter, count.HealthThreshold, count.HealthThresholdField);
            SeedThresholdGroup(
                _allyHealingAbsorbThreshold,
                count.HealingAbsorbFilter,
                count.HealingAbsorbThreshold,
                count.HealingAbsorbThresholdField);
            var auraSpellIds = count.AuraSpellIds is { Count: > 0 }
                ? count.AuraSpellIds
                : count.AuraSpellId is { } auraSpellId
                    ? [auraSpellId]
                    : ResolveLegacyAura(count.AuraName) is { } legacyAuraSpellId
                        ? [legacyAuraSpellId]
                        : [];
            SelectAura(_enemyAuraBox, auraSpellIds.Count > 0 ? auraSpellIds[0] : null);
            CheckAuras(_enemyAurasBox, auraSpellIds);
            return;
        }

        if (unit is not null)
        {
            _nameBox.Text = unit.Name;
            _healthNameBox.Text = unit.HealthName ?? string.Empty;
            _categoryBox.SelectedIndex = 0;
            PopulateSelectors();
            if (unit.FilterVersion == ModuleUnit.CurrentFilterVersion)
            {
                SelectEnemyThresholdFilter(_enemyHealthFilterBox, unit.HealthFilter);
                SelectEnemyThresholdFilter(_allyHealingAbsorbFilterBox, unit.HealingAbsorbFilter);
                SelectEnemyAuraFilter(unit.AuraFilter);
                SelectAuraDurationFilter(unit.AuraDurationFilter);
                SelectLowestHealthRoleFilter(unit.RoleFilter);
                if (unit.Role is { } filteredRole)
                {
                    SelectRole(filteredRole);
                }

                SelectAllyDispelFilter(unit.DispelFilter);
                if (unit.DispelType is { } filteredDispelType)
                {
                    SelectAllyDispelType(filteredDispelType);
                }
                SelectUnitTargetControls(unit);

                SeedThresholdGroup(
                    _enemyHealthThreshold,
                    unit.HealthFilter,
                    unit.HealthThreshold,
                    unit.HealthThresholdField);
                SeedThresholdGroup(
                    _allyHealingAbsorbThreshold,
                    unit.HealingAbsorbFilter,
                    unit.HealingAbsorbThreshold,
                    unit.HealingAbsorbThresholdField);
                var filteredAuraIds = unit.AuraSpellIds is { Count: > 0 }
                    ? unit.AuraSpellIds
                    : (unit.AuraNames ?? [])
                        .Select(ResolveLegacyAura)
                        .Where(id => id is not null)
                        .Select(id => id!.Value)
                        .ToList();
                SelectAura(_enemyAuraBox, filteredAuraIds.Count > 0 ? filteredAuraIds[0] : null);
                CheckAuras(_enemyAurasBox, filteredAuraIds);
                SelectAura(
                    _auraDurationAuraBox,
                    unit.AuraDurationSpellId
                        ?? (unit.AuraDurationFilter != AuraDurationFilterKind.None && filteredAuraIds.Count > 0
                            ? filteredAuraIds[0]
                            : null));
                return;
            }

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

        if (IsAverageHealthCategory)
        {
            var average = BuildAverageHealth(name);
            if (!ValidateAggregateFilters(
                    average.HealthFilter,
                    average.HealthThreshold,
                    average.HealthThresholdField,
                    average.AuraFilter,
                    average.AuraSpellIds,
                    average.RangeFilter,
                    average.RangeThreshold,
                    average.RangeThresholdField))
            {
                return;
            }

            ResultAverageHealth = average;
            DialogResult = DialogResult.OK;
            return;
        }

        if (IsEnemyCountCategory)
        {
            var enemyCount = BuildEnemyCount(name);
            if (!ValidateAggregateFilters(
                    enemyCount.HealthFilter,
                    enemyCount.HealthThreshold,
                    enemyCount.HealthThresholdField,
                    enemyCount.AuraFilter,
                    enemyCount.AuraSpellIds,
                    enemyCount.RangeFilter,
                    enemyCount.RangeThreshold,
                    enemyCount.RangeThresholdField))
            {
                return;
            }

            ResultEnemyCount = enemyCount;
            DialogResult = DialogResult.OK;
            return;
        }

        if (IsCountCategory)
        {
            var count = BuildAllyCount(name);
            if (!ValidateAllyCountFilters(count))
            {
                return;
            }

            ResultCount = count;
            DialogResult = DialogResult.OK;
            return;
        }

        if (IsUnitCategory)
        {
            var unit = BuildFilteredUnit(name);
            if (!ValidateFilteredUnit(unit))
            {
                return;
            }

            var filteredHealthName = SupportsHealthName() ? _healthNameBox.Text.Trim() : string.Empty;
            if (filteredHealthName.Length > 0)
            {
                if (string.Equals(filteredHealthName, name, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("值名称不能与名称相同。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!ValidateName(filteredHealthName, out var healthMessage))
                {
                    MessageBox.Show($"值名称: {healthMessage}", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            unit.HealthName = filteredHealthName.Length == 0 ? null : filteredHealthName;
            ResultUnit = unit;
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

    private static bool ValidateAggregateFilters(
        EnemyThresholdFilterKind healthFilter,
        int? healthThreshold,
        string? healthThresholdField,
        EnemyAuraFilterKind auraFilter,
        IReadOnlyList<long>? auraSpellIds,
        EnemyThresholdFilterKind rangeFilter,
        int? rangeThreshold,
        string? rangeThresholdField)
    {
        if (healthFilter != EnemyThresholdFilterKind.None
            && healthThreshold is null
            && string.IsNullOrWhiteSpace(healthThresholdField))
        {
            MessageBox.Show("请选择动态生命值阈值。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (rangeFilter != EnemyThresholdFilterKind.None
            && rangeThreshold is null
            && string.IsNullOrWhiteSpace(rangeThresholdField))
        {
            MessageBox.Show("请选择动态距离阈值。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (auraFilter != EnemyAuraFilterKind.None && (auraSpellIds is null || auraSpellIds.Count == 0))
        {
            MessageBox.Show("请选择光环。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        return true;
    }

    private static bool ValidateAllyCountFilters(ModuleCountField count)
    {
        if (!ValidateAggregateFilters(
                count.HealthFilter,
                count.HealthThreshold,
                count.HealthThresholdField,
                count.AuraFilter,
                count.AuraSpellIds,
                EnemyThresholdFilterKind.None,
                null,
                null))
        {
            return false;
        }

        if (count.HealingAbsorbFilter != EnemyThresholdFilterKind.None
            && count.HealingAbsorbThreshold is null
            && string.IsNullOrWhiteSpace(count.HealingAbsorbThresholdField))
        {
            MessageBox.Show("请选择动态治疗吸收阈值。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (count.DispelFilter != AllyDispelFilterKind.None && count.DispelType is null)
        {
            MessageBox.Show("请选择驱散类型。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        return true;
    }

    private bool ValidateFilteredUnit(ModuleUnit unit)
    {
        var targetCount = 0;
        targetCount += SelectedHealthTarget() == UnitSelectorKind.LowestHealth ? 1 : 0;
        targetCount += SelectedHealingAbsorbTarget() == UnitSelectorKind.HighestHealingAbsorb ? 1 : 0;
        targetCount += SelectedAuraDurationFilter() is AuraDurationFilterKind.Longest
            or AuraDurationFilterKind.Shortest ? 1 : 0;
        if (targetCount > 1)
        {
            MessageBox.Show("生命值、治疗吸收和光环时长只能选择一种目标选择方式。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (!ValidateAggregateFilters(
                unit.HealthFilter,
                unit.HealthThreshold,
                unit.HealthThresholdField,
                unit.AuraFilter,
                unit.AuraSpellIds,
                EnemyThresholdFilterKind.None,
                null,
                null))
        {
            return false;
        }

        if (unit.HealingAbsorbFilter != EnemyThresholdFilterKind.None
            && unit.HealingAbsorbThreshold is null
            && string.IsNullOrWhiteSpace(unit.HealingAbsorbThresholdField))
        {
            MessageBox.Show("请选择动态治疗吸收阈值。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (unit.DispelFilter != AllyDispelFilterKind.None && unit.DispelType is null)
        {
            MessageBox.Show("请选择驱散类型。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (unit.AuraDurationFilter is AuraDurationFilterKind.Longest or AuraDurationFilterKind.Shortest
            && unit.AuraDurationSpellId is null)
        {
            MessageBox.Show("请选择光环。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        return true;
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

    private bool IsUnitCategory => _categoryBox.SelectedIndex == 0;

    private bool IsCountCategory => _categoryBox.SelectedIndex == 1;

    private bool IsEnemyCountCategory => _categoryBox.SelectedIndex == 2;

    private bool IsAverageHealthCategory => _categoryBox.SelectedIndex == 3;

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
        _thresholdSectionTitle.Text = usesHealingAbsorb ? "治疗吸收" : "生命值";
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
        => IsUnitCategory && SelectedHealthTarget() == UnitSelectorKind.LowestHealth;

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

    private UnitSelectorKind SelectedHealthTarget()
        => (_healthTargetBox.SelectedItem as UnitTargetItem)?.Kind ?? UnitSelectorKind.UnitWithRole;

    private EnemyThresholdFilterKind SelectedEnemyRangeFilter()
        => (_enemyRangeFilterBox.SelectedItem as EnemyThresholdFilterItem)?.Kind ?? EnemyThresholdFilterKind.None;

    private EnemyThresholdFilterKind SelectedAllyHealingAbsorbFilter()
        => (_allyHealingAbsorbFilterBox.SelectedItem as EnemyThresholdFilterItem)?.Kind
            ?? EnemyThresholdFilterKind.None;

    private UnitSelectorKind SelectedHealingAbsorbTarget()
        => (_healingAbsorbTargetBox.SelectedItem as UnitTargetItem)?.Kind ?? UnitSelectorKind.UnitWithRole;

    private EnemyCombatFilterKind SelectedEnemyCombatFilter()
        => (_enemyCombatFilterBox.SelectedItem as EnemyCombatFilterItem)?.Kind ?? EnemyCombatFilterKind.None;

    private EnemyAuraFilterKind SelectedEnemyAuraFilter()
        => (_enemyAuraFilterBox.SelectedItem as EnemyAuraFilterItem)?.Kind ?? EnemyAuraFilterKind.None;

    private AverageHealthTargetKind SelectedAverageTarget()
        => (_selectorBox.SelectedItem as AverageTargetItem)?.Kind ?? AverageHealthTargetKind.Allies;

    private AllyDispelFilterKind SelectedAllyDispelFilter()
        => (_allyDispelFilterBox.SelectedItem as AllyDispelFilterItem)?.Kind ?? AllyDispelFilterKind.None;

    private AuraDurationFilterKind SelectedAuraDurationFilter()
        => (_auraDurationFilterBox.SelectedItem as AuraDurationFilterItem)?.Kind
            ?? AuraDurationFilterKind.None;

    private bool HasUnitValueTarget()
        => IsUnitCategory
            && (SelectedHealthTarget() == UnitSelectorKind.LowestHealth
                || SelectedHealingAbsorbTarget() == UnitSelectorKind.HighestHealingAbsorb
                || SelectedAuraDurationFilter() is AuraDurationFilterKind.Longest
                    or AuraDurationFilterKind.Shortest);

    private bool HasDirectionalUnitFilter()
        => SelectedLowestHealthRoleFilter() is not null
            || SelectedAllyDispelFilter() != AllyDispelFilterKind.None
            || SelectedEnemyAuraFilter() != EnemyAuraFilterKind.None;

    private bool SelectedUnitReverse()
        => (_roleOrderBox.SelectedItem as UnitOrderItem)?.Reverse == true;

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

    private int SelectedAllyDispelType() => (_allyDispelTypeBox.SelectedItem as DispelTypeOption)?.Value ?? 1;

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

    private void SelectSelector(UnitSelectorKind kind, bool reverse = false)
    {
        for (var i = 0; i < _selectorBox.Items.Count; i++)
        {
            if (_selectorBox.Items[i] is SelectorItem item
                && item.Kind == kind
                && item.Reverse == reverse)
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

    private void SelectAuraDurationFilter(AuraDurationFilterKind kind)
    {
        for (var i = 0; i < _auraDurationFilterBox.Items.Count; i++)
        {
            if (_auraDurationFilterBox.Items[i] is AuraDurationFilterItem item && item.Kind == kind)
            {
                _auraDurationFilterBox.SelectedIndex = i;
                return;
            }
        }

        _auraDurationFilterBox.SelectedIndex = 0;
    }

    private void SelectUnitTargetControls(ModuleUnit unit)
    {
        _syncingUnitSelectionControls = true;
        try
        {
            var durationTarget = unit.AuraDurationFilter is AuraDurationFilterKind.Longest
                or AuraDurationFilterKind.Shortest;
            _healthTargetBox.SelectedIndex = !durationTarget && unit.Kind == UnitSelectorKind.LowestHealth ? 1 : 0;
            _healingAbsorbTargetBox.SelectedIndex = !durationTarget
                && unit.Kind == UnitSelectorKind.HighestHealingAbsorb ? 1 : 0;
            SelectAuraDurationFilter(unit.AuraDurationFilter);
            var hasValueTarget = durationTarget
                || unit.Kind is UnitSelectorKind.LowestHealth or UnitSelectorKind.HighestHealingAbsorb;
            SetUnitOrderCore(!hasValueTarget && HasDirectionalUnitFilter() && unit.Reverse);
        }
        finally
        {
            _syncingUnitSelectionControls = false;
        }
    }

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

    private void SelectAllyDispelFilter(AllyDispelFilterKind kind)
    {
        for (var i = 0; i < _allyDispelFilterBox.Items.Count; i++)
        {
            if (_allyDispelFilterBox.Items[i] is AllyDispelFilterItem item && item.Kind == kind)
            {
                _allyDispelFilterBox.SelectedIndex = i;
                return;
            }
        }

        _allyDispelFilterBox.SelectedIndex = 0;
    }

    private void SelectAllyDispelType(int dispelType)
    {
        for (var i = 0; i < _allyDispelTypeBox.Items.Count; i++)
        {
            if (_allyDispelTypeBox.Items[i] is DispelTypeOption option && option.Value == dispelType)
            {
                _allyDispelTypeBox.SelectedIndex = i;
                return;
            }
        }

        _allyDispelTypeBox.SelectedIndex = 0;
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

    private sealed record SelectorItem(string Text, UnitSelectorKind Kind, bool Reverse = false)
    {
        public override string ToString() => Text;
    }

    private sealed record UnitTargetItem(string Text, UnitSelectorKind Kind)
    {
        public override string ToString() => Text;
    }

    private sealed record UnitOrderItem(string Text, bool Reverse)
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

    private sealed record AverageTargetItem(string Text, AverageHealthTargetKind Kind)
    {
        public override string ToString() => Text;
    }

    private sealed record AllyDispelFilterItem(string Text, AllyDispelFilterKind Kind)
    {
        public override string ToString() => Text;
    }

    private sealed record AuraDurationFilterItem(string Text, AuraDurationFilterKind Kind)
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

    private enum UnitTargetSource
    {
        Health,
        HealingAbsorb,
        AuraDuration
    }
}
