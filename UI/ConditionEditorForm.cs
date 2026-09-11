using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Shigure;

/// <summary>
/// 单个比较项: 与上一项的连接方式(且/或)、字段、判断符号、值。
/// </summary>
public sealed record ConditionTerm(bool OrWithPrevious, string Field, string Op, string Value);

/// <summary>
/// 条件表达式文本与比较项列表之间的双向转换。
/// 语法与 ModuleConditionEvaluator 保持一致: && 优先于 ||, 不支持括号嵌套。
/// </summary>
public static class ConditionExpression
{
    private static readonly Regex InRegex = new(
        @"^\s*(?<field>.+?)\s+(?<op>not\s+in|in)\s*\((?<value>.*?)\)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ComparisonRegex = new(
        @"^\s*(?<field>.+?)\s*(?<op>==|!=|>=|<=|>|<)\s*(?<value>.+?)\s*$",
        RegexOptions.Compiled);

    public static List<ConditionTerm> Parse(string? expression)
    {
        var terms = new List<ConditionTerm>();
        if (string.IsNullOrWhiteSpace(expression))
        {
            return terms;
        }

        foreach (var orPart in Regex.Split(expression, @"\s*\|\|\s*"))
        {
            var firstInGroup = true;
            foreach (var andPart in Regex.Split(orPart, @"\s*&&\s*"))
            {
                var trimmed = andPart.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                terms.Add(ParseTerm(trimmed, orWithPrevious: firstInGroup && terms.Count > 0));
                firstInGroup = false;
            }
        }

        return terms;
    }

    public static string Build(IEnumerable<ConditionTerm> terms)
    {
        var builder = new StringBuilder();
        foreach (var term in terms)
        {
            var op = NormalizeOperator(term.Op);
            var value = IsInOperator(op) ? NormalizeInValue(term.Value) : term.Value.Trim();
            if (string.IsNullOrWhiteSpace(term.Field) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(term.OrWithPrevious ? " || " : " && ");
            }

            builder.Append(term.Field).Append(' ').Append(op).Append(' ');
            if (IsInOperator(op))
            {
                builder.Append('(').Append(value).Append(')');
            }
            else
            {
                builder.Append(value);
            }
        }

        return builder.ToString();
    }

    private static ConditionTerm ParseTerm(string term, bool orWithPrevious)
    {
        var inMatch = InRegex.Match(term);
        if (inMatch.Success)
        {
            return new ConditionTerm(
                orWithPrevious,
                inMatch.Groups["field"].Value.Trim(),
                NormalizeOperator(inMatch.Groups["op"].Value),
                NormalizeInValue(inMatch.Groups["value"].Value));
        }

        var comparison = ComparisonRegex.Match(term);
        if (comparison.Success)
        {
            return new ConditionTerm(
                orWithPrevious,
                comparison.Groups["field"].Value.Trim(),
                comparison.Groups["op"].Value,
                comparison.Groups["value"].Value.Trim());
        }

        // 布尔简写: `字段` 表示为真, `!字段` 表示为假, 归一化为显式比较。
        return term.StartsWith('!')
            ? new ConditionTerm(orWithPrevious, term[1..].Trim(), "==", "false")
            : new ConditionTerm(orWithPrevious, term, "==", "true");
    }

    public static bool IsInOperator(string? op)
    {
        return NormalizeOperator(op) is "in" or "not in";
    }

    public static string NormalizeOperator(string? op)
    {
        return Regex.Replace(op?.Trim().ToLowerInvariant() ?? string.Empty, @"\s+", " ");
    }

    public static string NormalizeInValue(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.StartsWith('(') && text.EndsWith(')') && text.Length >= 2)
        {
            text = text[1..^1].Trim();
        }

        return text;
    }
}

/// <summary>
/// 条件可视化编辑弹窗: 每行一个比较项(连接/类型/字段/判断/值/删除),
/// 字段下拉按类型过滤, 值控件按字段类型自适应。
/// </summary>
public sealed class ConditionEditorForm : Form
{
    private const string ConnectorColumn = "Connector";
    private const string TypeColumn = "Type";
    private const string ClassificationColumn = "Classification";
    private const string FieldColumn = "Field";
    private const string OperatorColumn = "Operator";
    private const string ValueColumn = "Value";
    private const string DeleteColumn = "Delete";
    private const string Unclassified = "未分类";
    private const int ConditionRowHeight = 46;

    private static readonly string[] AllOperators = ["==", "!=", ">", ">=", "<", "<=", "in", "not in"];
    private static readonly string[] TextOperators = ["==", "!=", "in", "not in"];
    private static readonly string[] BoolOperators = ["==", "!="];
    private static readonly string[] SpellOperators = ["==", "!="];
    private static readonly string[] DelayOperators = ["=="];
    private static readonly CategoryItem[] CategoryItems =
    [
        new("状态", ConditionFieldCategory.State),
        new("Shigure", ConditionFieldCategory.Shigure),
        new("光环", ConditionFieldCategory.Aura),
        new("冷却", ConditionFieldCategory.Spell),
        new("动态单位", ConditionFieldCategory.DynamicUnit),
        new("动态数值", ConditionFieldCategory.DynamicValue)
    ];

    private readonly IReadOnlyList<ConditionField> _fields;
    // 「值」侧可直接引用的动态数值字段(动态单位的数量/生命值、动态数值), 与手动数字二选一。
    private readonly IReadOnlyList<ConditionField> _valueReferenceFields;
    private readonly IReadOnlyList<ConditionSpell> _spells;
    private readonly IReadOnlyList<ConditionItem> _items;
    private readonly Func<IReadOnlyList<ConditionField>>? _conditionFieldsProvider;
    private readonly Func<IReadOnlyList<ConditionSpell>>? _conditionSpellsProvider;
    private readonly Func<IReadOnlyList<ConditionItem>>? _conditionItemsProvider;
    private readonly string _originalCondition;
    private readonly bool _allowSubConditions;
    private readonly bool _allowRuleSettings;
    private readonly DataGridView _conditionsGrid = new();
    private readonly Label _previewLabel = new();
    private readonly ToolTip _previewToolTip = new();
    private readonly List<string> _subConditions = new();
    private readonly ListBox _subList = new();
    private ToolStripDropDown? _conditionComboDropDown;
    private bool _updatingGrid;

    public string ConditionText { get; private set; } = string.Empty;
    public int? DelayMs { get; private set; }
    public int? LogicDelayMs { get; private set; }
    public bool? ContinueLogic { get; private set; }

    // 子条件: 与主条件是「且」、子条件彼此是「或」。allowSubConditions=false(默认)时不显示该区,
    // 也用于子条件自身的嵌套编辑弹窗防止无限递归。
    public IReadOnlyList<string> SubConditions => _subConditions;

    public ConditionEditorForm(
        IReadOnlyList<ConditionField> fields,
        string? condition,
        IEnumerable<string>? subConditions = null,
        bool allowSubConditions = false,
        int? delayMs = null,
        int? logicDelayMs = null,
        bool? continueLogic = null,
        bool allowRuleSettings = false,
        Func<IReadOnlyList<ConditionField>>? conditionFieldsProvider = null,
        IReadOnlyList<ConditionSpell>? spells = null,
        Func<IReadOnlyList<ConditionSpell>>? conditionSpellsProvider = null,
        IReadOnlyList<ConditionItem>? items = null,
        Func<IReadOnlyList<ConditionItem>>? conditionItemsProvider = null)
    {
        // 保存独立快照，避免父级字段集合后续刷新时影响当前弹窗；子弹窗则通过 provider 取得最新目录。
        _fields = fields.ToArray();
        _valueReferenceFields = _fields
            .Where(field => field.Type == ConditionFieldType.Int
                && field.Category is ConditionFieldCategory.DynamicValue
                    or ConditionFieldCategory.DynamicUnit)
            .ToArray();
        _spells = (spells ?? []).ToArray();
        _items = (items ?? []).ToArray();
        _conditionFieldsProvider = conditionFieldsProvider;
        _conditionSpellsProvider = conditionSpellsProvider;
        _conditionItemsProvider = conditionItemsProvider;
        _originalCondition = condition ?? string.Empty;
        _allowSubConditions = allowSubConditions;
        _allowRuleSettings = allowRuleSettings;
        if (subConditions is not null)
        {
            _subConditions.AddRange(subConditions
                .Select(sub => sub?.Trim() ?? string.Empty)
                .Where(sub => sub.Length > 0));
        }

        InitializeComponent();
        SpellIconCatalog.CatalogChanged += OnSpellIconCatalogChanged;

        foreach (var term in ConditionExpression.Parse(condition))
        {
            AddRow(term);
        }

        if (_allowRuleSettings && delayMs is > 0)
        {
            AddRow(new ConditionTerm(
                OrWithPrevious: false,
                ShigureConditionFields.Delay,
                "==",
                delayMs.Value.ToString(CultureInfo.InvariantCulture)));
        }

        if (_allowRuleSettings && logicDelayMs is > 0)
        {
            AddRow(new ConditionTerm(
                OrWithPrevious: false,
                ShigureConditionFields.LogicDelay,
                "==",
                logicDelayMs.Value.ToString(CultureInfo.InvariantCulture)));
        }

        if (_allowRuleSettings && continueLogic is true)
        {
            AddRow(new ConditionTerm(
                OrWithPrevious: false,
                ShigureConditionFields.ContinueLogic,
                "==",
                "true"));
        }

        // 空条件直接落在一条可填行上, 无需先去找"添加条件"。
        if (_conditionsGrid.Rows.Count == 0)
        {
            AddRow(null);
        }

        RefreshConnectors();
        UpdatePreview();
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
        SpellIconCatalog.CatalogChanged -= OnSpellIconCatalogChanged;
        CloseConditionComboDropDown();
        SaveWindowSize();
        base.OnFormClosed(e);
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

        _conditionsGrid.Invalidate();
    }

    private void RestoreCachedWindowSize()
    {
        var cached = UiCacheStore.Load().ConditionEditorWindowSize;
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
        cache.ConditionEditorWindowSize = new WindowSize
        {
            Width = Width,
            Height = Height
        };
        UiCacheStore.Save(cache);
    }

    private void InitializeComponent()
    {
        Text = "编辑条件";
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.Text;
        var initialHeight = _allowSubConditions ? 650 : 460;
        ClientSize = new Size(1080, initialHeight);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(780, initialHeight);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding = new Padding(UiTheme.CardPadding, 10, UiTheme.CardPadding, 10),
            ColumnCount = 1
        };
        Controls.Add(root);

        ConfigureConditionsGrid();

        var rowIndex = 0;
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildConditionsCard(), 0, rowIndex++);

        if (_allowSubConditions)
        {
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 200));
            root.Controls.Add(BuildSubConditionsPanel(), 0, rowIndex++);
        }

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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.Controls.Add(previewCard, 0, rowIndex++);

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.Controls.Add(BuildActionRow(), 0, rowIndex++);
        root.RowCount = rowIndex;
    }

    private Control BuildConditionsCard()
    {
        var card = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(UiTheme.CardPadding),
            Margin = new Padding(0, 0, 0, UiTheme.PageGap),
            ColumnCount = 1,
            RowCount = 2
        };
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        _conditionsGrid.Margin = new Padding(0);
        card.Controls.Add(_conditionsGrid, 0, 0);
        card.Controls.Add(BuildAddConditionRow(), 0, 1);
        return card;
    }

    // 子条件区: 标题 + 暗色列表 + 添加/编辑/删除。每条子条件本身也是一条完整条件,
    // 通过嵌套的(无子条件区的)条件编辑弹窗来编辑。
    private Control BuildSubConditionsPanel()
    {
        var panel = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(UiTheme.CardPadding),
            Margin = new Padding(0, 0, 0, UiTheme.PageGap),
            ColumnCount = 2,
            RowCount = 2
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "子条件 (满足任一即可, 与主条件为「且」关系)",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        };
        panel.Controls.Add(title, 0, 0);
        panel.SetColumnSpan(title, 2);

        _subList.Dock = DockStyle.Fill;
        UiTheme.StyleListBox(_subList, Font);
        _subList.BackColor = UiTheme.Surface;
        _subList.Margin = new Padding(0, 0, 12, 0);
        _subList.DoubleClick += (_, _) => EditSelectedSubCondition();
        panel.Controls.Add(_subList, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0, 0, 4, 0)
        };
        buttons.Controls.Add(CreateSubButton("添加子条件", UiTheme.Text, AddSubCondition));
        buttons.Controls.Add(CreateSubButton("编辑", UiTheme.Text, EditSelectedSubCondition));
        buttons.Controls.Add(CreateSubButton("删除", UiTheme.Danger, DeleteSelectedSubCondition));
        panel.Controls.Add(buttons, 1, 1);

        RefreshSubList();
        return panel;
    }

    private static Button CreateSubButton(string text, Color foreColor, Action onClick)
    {
        var button = UiTheme.CreateButton(text, UiTheme.Field, foreColor);
        button.AutoSize = false;
        button.Width = 124;
        button.Height = 36;
        button.Margin = new Padding(0, 0, 0, 8);
        button.Click += (_, _) => onClick();
        return button;
    }

    private void AddSubCondition()
    {
        var text = PromptSubCondition(string.Empty);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _subConditions.Add(text.Trim());
        RefreshSubList();
        _subList.SelectedIndex = _subConditions.Count - 1;
        UpdatePreview();
    }

    private void EditSelectedSubCondition()
    {
        var index = _subList.SelectedIndex;
        if (index < 0 || index >= _subConditions.Count)
        {
            return;
        }

        var text = PromptSubCondition(_subConditions[index]);
        if (text is null)
        {
            return;
        }

        // 编辑后清空 = 删除该子条件。
        if (string.IsNullOrWhiteSpace(text))
        {
            _subConditions.RemoveAt(index);
        }
        else
        {
            _subConditions[index] = text.Trim();
        }

        RefreshSubList();
        UpdatePreview();
    }

    private void DeleteSelectedSubCondition()
    {
        var index = _subList.SelectedIndex;
        if (index < 0 || index >= _subConditions.Count)
        {
            return;
        }

        _subConditions.RemoveAt(index);
        RefreshSubList();
        UpdatePreview();
    }

    // 返回 null = 用户取消; 空串 = 用户清空了条件(编辑时表示删除)。
    private string? PromptSubCondition(string current)
    {
        var fields = _conditionFieldsProvider?.Invoke() ?? _fields;
        var spells = _conditionSpellsProvider?.Invoke() ?? _spells;
        var items = _conditionItemsProvider?.Invoke() ?? _items;
        using var editor = new ConditionEditorForm(
            fields,
            current,
            conditionFieldsProvider: _conditionFieldsProvider,
            spells: spells,
            conditionSpellsProvider: _conditionSpellsProvider,
            items: items,
            conditionItemsProvider: _conditionItemsProvider);
        return editor.ShowDialog(this) == DialogResult.OK ? editor.ConditionText : null;
    }

    private void RefreshSubList()
    {
        _subList.BeginUpdate();
        _subList.Items.Clear();
        foreach (var sub in _subConditions)
        {
            _subList.Items.Add(sub);
        }

        _subList.EndUpdate();
    }

    private void ConfigureConditionsGrid()
    {
        UiTheme.StyleDataGridView(_conditionsGrid);
        _conditionsGrid.Dock = DockStyle.Fill;
        _conditionsGrid.Margin = new Padding(0, 4, 0, 6);
        _conditionsGrid.AllowUserToAddRows = false;
        _conditionsGrid.AllowUserToDeleteRows = false;
        _conditionsGrid.AllowUserToResizeColumns = true;
        _conditionsGrid.AllowUserToResizeRows = false;
        _conditionsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _conditionsGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _conditionsGrid.RowHeadersVisible = false;
        _conditionsGrid.RowTemplate.Height = UiTheme.Scale(_conditionsGrid, ConditionRowHeight);
        _conditionsGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _conditionsGrid.MultiSelect = false;
        // 下拉单元格由深色受控列表处理；文本值单元格在 CellClick 中显式进入编辑态。
        _conditionsGrid.EditMode = DataGridViewEditMode.EditProgrammatically;

        _conditionsGrid.Columns.Add(CreateComboColumn(ConnectorColumn, "连接", 82, 64));
        _conditionsGrid.Columns.Add(CreateComboColumn(TypeColumn, "类型", 118, 90));
        _conditionsGrid.Columns.Add(CreateComboColumn(ClassificationColumn, "分类", 130, 100));

        var fieldColumn = CreateComboColumn(FieldColumn, "字段", 260, 160);
        fieldColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        fieldColumn.FillWeight = 180;
        // 字段列始终保存唯一字段名，显示名称只负责界面文本。
        // 避免 DataGridView 在对象值与格式化字符串之间切换，导致预览丢行或跨行串值。
        fieldColumn.DisplayMember = nameof(FieldItem.Display);
        fieldColumn.ValueMember = nameof(FieldItem.Name);
        fieldColumn.ValueType = typeof(string);
        _conditionsGrid.Columns.Add(fieldColumn);

        _conditionsGrid.Columns.Add(CreateComboColumn(OperatorColumn, "判断", 88, 70));
        _conditionsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = ValueColumn,
            HeaderText = "值",
            Width = 140,
            MinimumWidth = 100,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _conditionsGrid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = DeleteColumn,
            HeaderText = string.Empty,
            Text = "✕",
            UseColumnTextForButtonValue = true,
            Width = 42,
            MinimumWidth = 42,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Resizable = DataGridViewTriState.False,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            FlatStyle = FlatStyle.Flat
        });
        _conditionsGrid.Columns[DeleteColumn]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _conditionsGrid.Columns[DeleteColumn]!.DefaultCellStyle.ForeColor = UiTheme.Danger;

        if (_conditionsGrid.Columns[TypeColumn] is DataGridViewComboBoxColumn typeColumn)
        {
            typeColumn.Items.AddRange(CategoryItems
                .Where(item => item.Category != ConditionFieldCategory.Shigure || _allowRuleSettings)
                .Select(item => item.Display)
                .ToArray());
        }

        if (_conditionsGrid.Columns[ConnectorColumn] is DataGridViewComboBoxColumn connectorColumn)
        {
            connectorColumn.Items.AddRange([string.Empty, "且", "或"]);
        }

        _conditionsGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_conditionsGrid.IsCurrentCellDirty
                && _conditionsGrid.CurrentCell is DataGridViewComboBoxCell)
            {
                _conditionsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _conditionsGrid.CellValueChanged += OnGridCellValueChanged;
        _conditionsGrid.CellEndEdit += (_, _) => UpdatePreview();
        _conditionsGrid.CellFormatting += OnConditionsGridCellFormatting;
        _conditionsGrid.CellMouseClick += OnConditionsGridCellMouseClick;
        _conditionsGrid.CellPainting += OnConditionsGridCellPainting;
        _conditionsGrid.KeyDown += OnConditionsGridKeyDown;
        _conditionsGrid.CellContentClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == _conditionsGrid.Columns[DeleteColumn]!.Index)
            {
                _conditionsGrid.Rows.RemoveAt(e.RowIndex);
                RefreshConnectors();
                UpdatePreview();
            }
        };
        _conditionsGrid.DataError += (_, e) => e.ThrowException = false;
        UiTheme.CacheDataGridViewColumnWidths(_conditionsGrid, "condition-editor");
    }

    private void OnConditionsGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0)
        {
            return;
        }

        var cell = _conditionsGrid.Rows[e.RowIndex].Cells[e.ColumnIndex];
        cell.ToolTipText = string.Empty;
        var missingSpell = GetMissingSpellMessage(_conditionsGrid.Rows[e.RowIndex]);
        var missingItem = GetMissingItemMessage(_conditionsGrid.Rows[e.RowIndex]);
        var missingMessage = missingSpell ?? missingItem;
        if (missingMessage is not null)
        {
            e.CellStyle.BackColor = UiTheme.DangerSoft;
            e.CellStyle.ForeColor = UiTheme.Danger;
            e.CellStyle.SelectionBackColor = UiTheme.Danger;
            e.CellStyle.SelectionForeColor = UiTheme.Background;
            cell.ToolTipText = missingMessage;
            return;
        }

        if (e.ColumnIndex == _conditionsGrid.Columns[FieldColumn]!.Index)
        {
            cell.ToolTipText = cell.Value?.ToString() switch
            {
                "一键辅助" => "一键辅助，条件保存技能列表中的 spellId",
                "插入法术" => "插入法术，条件保存技能列表中的 spellId",
                "插入物品" => "插入物品，条件保存物品列表中的 itemId",
                "施法技能" => "施法技能，条件保存技能列表中的 spellId",
                "上个技能" => "上个技能，条件保存技能列表中的 spellId",
                _ => e.Value?.ToString() ?? string.Empty
            };
            return;
        }

        if (e.ColumnIndex == _conditionsGrid.Columns[ValueColumn]!.Index
            && cell is ReferenceValueCell
            && _valueReferenceFields.FirstOrDefault(field => string.Equals(
                field.Name,
                e.Value?.ToString()?.Trim(),
                StringComparison.OrdinalIgnoreCase)) is { } reference)
        {
            cell.ToolTipText = $"引用动态数值: {reference.DisplayName}";
            return;
        }

        if (e.ColumnIndex == _conditionsGrid.Columns[ValueColumn]!.Index
            && IsCastOrChannelTimeField(SelectedField(_conditionsGrid.Rows[e.RowIndex]))
            && decimal.TryParse(
                e.Value?.ToString(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var value))
        {
            cell.ToolTipText = $"约{(value / 10m).ToString("0.#", CultureInfo.InvariantCulture)}秒";
        }
    }

    private static bool IsCastOrChannelTimeField(FieldItem? field)
    {
        var name = field?.Name;
        return name?.EndsWith("施法(正计时)", StringComparison.Ordinal) == true
            || name?.EndsWith("施法(倒计时)", StringComparison.Ordinal) == true
            || name?.EndsWith("引导", StringComparison.Ordinal) == true;
    }

    private static DataGridViewComboBoxColumn CreateComboColumn(
        string name,
        string headerText,
        int width,
        int minimumWidth)
        => new()
        {
            Name = name,
            HeaderText = headerText,
            Width = width,
            MinimumWidth = minimumWidth,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            FlatStyle = FlatStyle.Flat,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };

    private void OnConditionsGridCellMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        var cell = _conditionsGrid.Rows[e.RowIndex].Cells[e.ColumnIndex];
        if (cell is BossValueCell or ReferenceValueCell)
        {
            var buttonBounds = UiTheme.GetDropDownButtonBounds(
                _conditionsGrid,
                new Rectangle(0, 0, cell.Size.Width, cell.Size.Height));
            if (buttonBounds.Contains(e.X, e.Y))
            {
                if (cell is BossValueCell)
                {
                    ShowBossNumberDropDown(e.RowIndex, e.ColumnIndex);
                }
                else
                {
                    ShowValueReferenceDropDown(e.RowIndex, e.ColumnIndex);
                }
            }
            else if (!cell.ReadOnly)
            {
                CloseConditionComboDropDown();
                _conditionsGrid.CurrentCell = cell;
                _conditionsGrid.BeginEdit(selectAll: true);
            }
            return;
        }

        if (cell is DataGridViewComboBoxCell combo
            && !combo.ReadOnly
            && combo.DisplayStyle != DataGridViewComboBoxDisplayStyle.Nothing)
        {
            ShowConditionComboDropDown(e.RowIndex, e.ColumnIndex);
            return;
        }

        CloseConditionComboDropDown();
        if (_conditionsGrid.Columns[e.ColumnIndex].Name == ValueColumn && !cell.ReadOnly)
        {
            _conditionsGrid.CurrentCell = cell;
            _conditionsGrid.BeginEdit(selectAll: true);
        }
    }

    private void OnConditionsGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (_conditionsGrid.CurrentCell is BossValueCell bossCell
            && (e.KeyCode == Keys.F4 || e.KeyCode == Keys.Down && e.Alt))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            ShowBossNumberDropDown(bossCell.RowIndex, bossCell.ColumnIndex);
            return;
        }

        if (_conditionsGrid.CurrentCell is ReferenceValueCell referenceCell
            && (e.KeyCode == Keys.F4 || e.KeyCode == Keys.Down && e.Alt))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            ShowValueReferenceDropDown(referenceCell.RowIndex, referenceCell.ColumnIndex);
            return;
        }

        if (_conditionsGrid.CurrentCell is not DataGridViewComboBoxCell cell
            || cell.ReadOnly
            || cell.DisplayStyle == DataGridViewComboBoxDisplayStyle.Nothing
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
        ShowConditionComboDropDown(cell.RowIndex, cell.ColumnIndex);
    }

    private void ShowConditionComboDropDown(int rowIndex, int columnIndex)
    {
        CloseConditionComboDropDown();
        _conditionsGrid.EndEdit();

        if (rowIndex < 0
            || rowIndex >= _conditionsGrid.Rows.Count
            || _conditionsGrid.Rows[rowIndex].Cells[columnIndex] is not DataGridViewComboBoxCell cell
            || cell.ReadOnly
            || cell.DisplayStyle == DataGridViewComboBoxDisplayStyle.Nothing)
        {
            return;
        }

        _conditionsGrid.CurrentCell = cell;
        var sourceItems = cell.Items.Cast<object>().ToList();
        if (sourceItems.Count == 0 && cell.OwningColumn is DataGridViewComboBoxColumn column)
        {
            sourceItems.AddRange(column.Items.Cast<object>());
        }

        var items = sourceItems
            .Select(item => item switch
            {
                FieldItem field => new UiDropDownOption(
                    field.Name,
                    field.Display,
                    ResolveFieldIcon(field)),
                SpellValueItem spell => new UiDropDownOption(
                    spell.Value,
                    spell.Display,
                    spell.Missing ? null : SpellIconCatalog.Get(spell.SpellId),
                    spell.Index?.ToString(CultureInfo.InvariantCulture)),
                ItemValueItem itemEntry => new UiDropDownOption(
                    itemEntry.Value,
                    itemEntry.Display,
                    itemEntry.Missing ? null : SpellIconCatalog.GetItem(itemEntry.ItemId),
                    itemEntry.Index?.ToString(CultureInfo.InvariantCulture)),
                _ => new UiDropDownOption(item.ToString() ?? string.Empty, item.ToString() ?? string.Empty)
            })
            .DistinctBy(item => item.Value?.ToString(), StringComparer.Ordinal)
            .ToList();
        var currentValue = cell.Value is FieldItem field
            ? field.Name
            : cell.Value?.ToString() ?? string.Empty;
        if (!items.Any(item => string.Equals(item.Value?.ToString(), currentValue, StringComparison.Ordinal)))
        {
            items.Insert(0, new UiDropDownOption(
                currentValue,
                cell.FormattedValue?.ToString() ?? currentValue));
        }

        if (items.Count == 0)
        {
            return;
        }

        var cellBounds = _conditionsGrid.GetCellDisplayRectangle(columnIndex, rowIndex, cutOverflow: true);
        ToolStripDropDown? dropDown = null;
        dropDown = UiDropDownPopup.Show(
            _conditionsGrid,
            cellBounds,
            items,
            currentValue,
            selected =>
            {
                cell.Value = selected.Value?.ToString() ?? string.Empty;
                _conditionsGrid.InvalidateCell(cell);
            },
            closed: () =>
            {
                if (ReferenceEquals(_conditionComboDropDown, dropDown))
                {
                    _conditionComboDropDown = null;
                }
            });
        _conditionComboDropDown = dropDown;
    }

    private void ShowBossNumberDropDown(int rowIndex, int columnIndex)
    {
        CloseConditionComboDropDown();
        _conditionsGrid.EndEdit();
        if (rowIndex < 0 || rowIndex >= _conditionsGrid.Rows.Count
            || _conditionsGrid.Rows[rowIndex].Cells[columnIndex] is not BossValueCell cell)
        {
            return;
        }

        _conditionsGrid.CurrentCell = cell;
        var options = new List<UiDropDownOption>
        {
            new("0", "非首领战 / -", LeadingText: "0")
        };
        options.AddRange(StatusForm.GetBossNumberOptions().Select(boss => new UiDropDownOption(
            boss.Number.ToString(CultureInfo.InvariantCulture),
            $"{boss.Dungeon} / {boss.Name}",
            LeadingText: boss.Number.ToString(CultureInfo.InvariantCulture))));

        var currentValue = cell.Value?.ToString()?.Trim() ?? string.Empty;
        var cellBounds = _conditionsGrid.GetCellDisplayRectangle(columnIndex, rowIndex, cutOverflow: true);
        ToolStripDropDown? dropDown = null;
        dropDown = UiDropDownPopup.Show(
            _conditionsGrid,
            cellBounds,
            options,
            currentValue,
            selected =>
            {
                cell.Value = selected.Value?.ToString() ?? string.Empty;
                _conditionsGrid.InvalidateCell(cell);
                UpdatePreview();
            },
            preferredWidth: 390,
            closed: () =>
            {
                if (ReferenceEquals(_conditionComboDropDown, dropDown))
                {
                    _conditionComboDropDown = null;
                }
            });
        _conditionComboDropDown = dropDown;
    }

    private static Image? ResolveFieldIcon(FieldItem field)
    {
        if (field.ItemId is > 0)
        {
            return SpellIconCatalog.GetItem(field.ItemId.Value);
        }

        if (field.IsCustom
            || field.Category is not (ConditionFieldCategory.Spell or ConditionFieldCategory.Aura))
        {
            return null;
        }

        if (SpellFieldKey.TryParseSpell(field.Name, out var spellId, out _)
            || SpellFieldKey.TryParseAura(field.Name, out _, out spellId, out _)
            || SpellFieldKey.TryParseAuraMember(field.Name, out spellId, out _))
        {
            return SpellIconCatalog.Get(spellId);
        }

        return null;
    }

    private void CloseConditionComboDropDown()
    {
        _conditionComboDropDown?.Close(ToolStripDropDownCloseReason.AppClicked);
        _conditionComboDropDown = null;
    }

    private void OnConditionsGridCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }


        var cell = _conditionsGrid.Rows[e.RowIndex].Cells[e.ColumnIndex];
        if (cell is BossValueCell or ReferenceValueCell)
        {
            UiTheme.PaintDataGridViewComboBoxCell(_conditionsGrid, e, showButton: true);
            return;
        }

        if (cell is not DataGridViewComboBoxCell combo)
        {
            return;
        }

        PaintConditionComboBoxCell(e, combo);
    }

    private void PaintConditionComboBoxCell(
        DataGridViewCellPaintingEventArgs e,
        DataGridViewComboBoxCell cell)
    {
        var showButton = !cell.ReadOnly
            && cell.DisplayStyle != DataGridViewComboBoxDisplayStyle.Nothing;
        UiTheme.PaintDataGridViewComboBoxCell(_conditionsGrid, e, showButton);
    }

    // 「添加条件」按钮单独成行, 放在主条件行下方、子条件区上方。
    private Control BuildAddConditionRow()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 8, 0, 0)
        };

        var addButton = UiTheme.CreateButton("添加条件", UiTheme.Field, UiTheme.Text);
        addButton.AutoSize = false;
        addButton.Width = 114;
        addButton.Height = 36;
        addButton.Margin = new Padding(0);
        addButton.Click += (_, _) =>
        {
            _conditionsGrid.EndEdit();
            var row = AddRow(null);
            RefreshConnectors();
            UpdatePreview();
            ShowConditionComboDropDown(
                row.Index,
                _conditionsGrid.Columns[TypeColumn]!.Index);
        };
        panel.Controls.Add(addButton);
        return panel;
    }

    private Control BuildActionRow()
    {
        var row = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(UiTheme.CardPadding, 10, UiTheme.CardPadding, 10),
            ColumnCount = 2,
            RowCount = 1
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 184));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var rightButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };

        var okButton = UiTheme.CreateButton("确定", UiTheme.ButtonKind.Primary);
        UiTheme.StyleActionButton(okButton, 80);
        okButton.Margin = new Padding(8, 0, 0, 0);
        okButton.Click += (_, _) => TryConfirm();

        var cancelButton = UiTheme.CreateButton("取消", UiTheme.ButtonKind.Secondary);
        UiTheme.StyleActionButton(cancelButton, 80);
        cancelButton.Margin = new Padding(8, 0, 0, 0);
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;

        rightButtons.Controls.Add(okButton);
        rightButtons.Controls.Add(cancelButton);
        row.Controls.Add(rightButtons, 1, 0);

        AcceptButton = okButton;
        CancelButton = cancelButton;
        return row;
    }

    // 提交前对两类静默丢失给出确认: 不完整行被忽略、或结果为空会把条件清成"始终命中"。
    private void TryConfirm()
    {
        _conditionsGrid.EndEdit();
        var incomplete = _conditionsGrid.Rows.Cast<DataGridViewRow>().Count(IsRowIncomplete);
        if (incomplete > 0
            && MessageBox.Show(
                $"有 {incomplete} 行不完整(字段或值为空), 将被忽略。继续？",
                "Shigure",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning) != DialogResult.OK)
        {
            return;
        }

        var text = ConditionExpression.Build(CollectTerms());
        if (!TryReadDelay(out var delayMs))
        {
            return;
        }

        if (!TryReadLogicDelay(out var logicDelayMs))
        {
            return;
        }

        if (!TryReadContinueLogic(out var continueLogic))
        {
            return;
        }

        // 仅当原本有条件、现在主条件与子条件都为空时才提醒(避免把已有规则误清成"始终命中")。
        if (text.Length == 0
            && _subConditions.Count == 0
            && !string.IsNullOrWhiteSpace(_originalCondition)
            && MessageBox.Show(
                "当前条件为空, 将清除该规则的条件(始终命中)。继续？",
                "Shigure",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning) != DialogResult.OK)
        {
            return;
        }

        ConditionText = text;
        DelayMs = delayMs;
        LogicDelayMs = logicDelayMs;
        ContinueLogic = continueLogic;
        DialogResult = DialogResult.OK;
    }

    // 恰好字段与值一空一非空 → 不完整; 两者都空只是空行(静默忽略, 不算不完整)。
    private static bool IsRowIncomplete(DataGridViewRow row)
    {
        if (IsRuleSettingField(SelectedField(row)))
        {
            return false;
        }

        var field = SelectedField(row)?.Name.Trim() ?? string.Empty;
        var value = ReadRowValue(row);
        return (field.Length == 0) ^ (value.Length == 0);
    }

    private DataGridViewRow AddRow(ConditionTerm? term)
    {
        var index = _conditionsGrid.Rows.Add();
        var row = _conditionsGrid.Rows[index];
        row.Height = _conditionsGrid.RowTemplate.Height;
        row.Cells[ConnectorColumn].Value = term?.OrWithPrevious == true ? "或" : "且";

        var category = ResolveCategory(term?.Field);
        var classification = ResolveClassification(term?.Field, category);
        _updatingGrid = true;
        try
        {
            row.Cells[TypeColumn].Value = CategoryItems.First(item => item.Category == category).Display;
            ConfigureType(row, term?.Field, classification, term?.Op, term?.Value, preserveRaw: true);
        }
        finally
        {
            _updatingGrid = false;
        }

        RefreshConnectors();
        return row;
    }

    private void OnGridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_updatingGrid || e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        var row = _conditionsGrid.Rows[e.RowIndex];
        var columnName = _conditionsGrid.Columns[e.ColumnIndex].Name;
        _updatingGrid = true;
        try
        {
            if (columnName == TypeColumn)
            {
                ConfigureType(row, null, null, null, null, preserveRaw: false);
            }
            else if (columnName == ClassificationColumn)
            {
                ConfigureFields(row, null);
                ConfigureField(row, null, null, preserveRaw: false);
            }
            else if (columnName == FieldColumn)
            {
                ConfigureField(
                    row,
                    row.Cells[OperatorColumn].Value?.ToString(),
                    ReadRowValue(row),
                    preserveRaw: false);
            }
            else if (columnName == OperatorColumn)
            {
                ConfigureValueCell(row, ReadRowValue(row), preserveRaw: true);
            }
        }
        finally
        {
            _updatingGrid = false;
        }

        RefreshConnectors();
        UpdatePreview();
        _conditionsGrid.InvalidateRow(e.RowIndex);
    }

    private void RefreshConnectors()
    {
        var wasUpdating = _updatingGrid;
        _updatingGrid = true;
        try
        {
            for (var i = 0; i < _conditionsGrid.Rows.Count; i++)
            {
                var cell = (DataGridViewComboBoxCell)_conditionsGrid.Rows[i].Cells[ConnectorColumn];
                var visible = i > 0 && !IsRuleSettingField(SelectedField(_conditionsGrid.Rows[i]));
                cell.ReadOnly = !visible;
                cell.DisplayStyle = visible
                    ? DataGridViewComboBoxDisplayStyle.DropDownButton
                    : DataGridViewComboBoxDisplayStyle.Nothing;
                var desiredValue = visible && cell.Value?.ToString() is ("且" or "或")
                    ? cell.Value?.ToString()
                    : visible ? "且" : string.Empty;
                if (!string.Equals(cell.Value?.ToString(), desiredValue, StringComparison.Ordinal))
                {
                    cell.Value = desiredValue;
                }
            }
        }
        finally
        {
            _updatingGrid = wasUpdating;
        }
    }

    private void ConfigureType(
        DataGridViewRow row,
        string? currentField,
        string? desiredClassification,
        string? desiredOp,
        string? rawValue,
        bool preserveRaw)
    {
        var category = SelectedCategory(row);
        ConfigureClassification(row, category, currentField, desiredClassification);
        ConfigureFields(row, currentField);
        ConfigureField(row, desiredOp, rawValue, preserveRaw);
    }

    private void ConfigureClassification(
        DataGridViewRow row,
        ConditionFieldCategory category,
        string? currentField,
        string? desiredClassification)
    {
        var cell = (DataGridViewComboBoxCell)row.Cells[ClassificationColumn];
        cell.Items.Clear();
        var enabled = category is ConditionFieldCategory.State
            or ConditionFieldCategory.Aura
            or ConditionFieldCategory.Spell;
        cell.ReadOnly = !enabled;
        cell.DisplayStyle = enabled
            ? DataGridViewComboBoxDisplayStyle.DropDownButton
            : DataGridViewComboBoxDisplayStyle.Nothing;
        if (!enabled)
        {
            cell.Value = null;
            return;
        }

        var options = category == ConditionFieldCategory.State
            ? ClassStateCatalog.TopCategories.ToList()
            : category == ConditionFieldCategory.Spell
                ? new List<string>
                {
                    CooldownConditionClassifications.Spell,
                    CooldownConditionClassifications.Item
                }
                : _fields
                    .Where(field => field.Category == category)
                    .Select(field => NormalizeClassification(field.Classification))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
        if (category is ConditionFieldCategory.State or ConditionFieldCategory.Spell)
        {
            // 固定分类之后仍保留配置中出现的扩展分类。
            foreach (var classification in _fields
                         .Where(field => field.Category == category)
                         .Select(field => NormalizeClassification(field.Classification))
                         .Distinct(StringComparer.Ordinal))
            {
                if (!options.Contains(classification, StringComparer.Ordinal))
                {
                    options.Add(classification);
                }
            }
        }

        if (options.Count == 0)
        {
            options.Add(Unclassified);
        }

        cell.Items.AddRange(options
            .Select(option => GetClassificationDisplayName(category, option))
            .Cast<object>()
            .ToArray());
        var currentDefinition = FindField(currentField);
        var selected = desiredClassification
            ?? currentDefinition?.Classification;
        selected = string.IsNullOrWhiteSpace(selected)
            ? options[0]
            : NormalizeClassification(selected);
        if (!options.Contains(selected, StringComparer.Ordinal))
        {
            cell.Items.Add(selected);
        }

        cell.Value = GetClassificationDisplayName(category, selected);
    }

    private void ConfigureFields(DataGridViewRow row, string? currentField)
    {
        var category = SelectedCategory(row);
        var classification = GetClassificationStorageName(
            category,
            row.Cells[ClassificationColumn].Value?.ToString());
        var cell = (DataGridViewComboBoxCell)row.Cells[FieldColumn];
        cell.Items.Clear();

        foreach (var field in _fields.Where(field =>
                     field.Category == category
                     && (category is not (ConditionFieldCategory.State
                             or ConditionFieldCategory.Aura
                             or ConditionFieldCategory.Spell)
                         || string.Equals(
                             NormalizeClassification(field.Classification),
                             classification,
                             StringComparison.Ordinal))))
        {
            cell.Items.Add(new FieldItem(
                field.Name,
                field.DisplayName,
                field.Type,
                field.Category,
                field.Classification,
                IsCustom: false,
                field.ItemId));
        }

        FieldItem? selected = null;
        if (!string.IsNullOrWhiteSpace(currentField))
        {
            selected = cell.Items.Cast<FieldItem>().FirstOrDefault(item =>
                string.Equals(item.Name, currentField, StringComparison.OrdinalIgnoreCase));
            if (selected is null)
            {
                selected = new FieldItem(
                    currentField,
                    $"{currentField} (自定义)",
                    ConditionFieldType.Int,
                    category,
                    classification,
                    IsCustom: true);
                cell.Items.Add(selected);
            }
        }

        selected ??= cell.Items.Cast<FieldItem>().FirstOrDefault();
        cell.Value = selected?.Name;
    }

    private void ConfigureField(
        DataGridViewRow row,
        string? desiredOp,
        string? rawValue,
        bool preserveRaw)
    {
        PopulateOps(row, desiredOp);
        ConfigureValueCell(row, rawValue, preserveRaw);
    }

    private ConditionFieldCategory ResolveCategory(string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return ConditionFieldCategory.State;
        }

        var field = _fields.FirstOrDefault(field =>
            string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase));
        if (field is not null)
        {
            return field.Category;
        }

        if (fieldName.StartsWith("auras.", StringComparison.OrdinalIgnoreCase)
            || fieldName.StartsWith("aura.", StringComparison.OrdinalIgnoreCase))
        {
            return ConditionFieldCategory.Aura;
        }

        if (fieldName.StartsWith("spells.", StringComparison.OrdinalIgnoreCase)
            || fieldName.StartsWith("spell.", StringComparison.OrdinalIgnoreCase))
        {
            return ConditionFieldCategory.Spell;
        }

        return ConditionFieldCategory.State;
    }

    private string? ResolveClassification(string? fieldName, ConditionFieldCategory category)
    {
        var field = FindField(fieldName);
        return field?.Classification
            ?? (category == ConditionFieldCategory.Aura
                ? fieldName?.StartsWith("auras.目标", StringComparison.OrdinalIgnoreCase) == true
                    ? "目标光环"
                    : fieldName?.StartsWith("auras.焦点", StringComparison.OrdinalIgnoreCase) == true
                        ? "焦点光环"
                        : "玩家"
                : category == ConditionFieldCategory.Spell
                    ? fieldName?.StartsWith("spells.", StringComparison.OrdinalIgnoreCase) == true
                      || fieldName?.StartsWith("spell.", StringComparison.OrdinalIgnoreCase) == true
                        ? CooldownConditionClassifications.Spell
                        : CooldownConditionClassifications.Item
                    : ClassStateCatalog.CategoryState);
    }

    private ConditionField? FindField(string? fieldName)
    {
        return string.IsNullOrWhiteSpace(fieldName)
            ? null
            : _fields.FirstOrDefault(field =>
                string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase));
    }

    private static void PopulateOps(DataGridViewRow row, string? desiredOp)
    {
        var field = SelectedField(row);
        var isRuleSetting = IsRuleSettingField(field);
        var ops = isRuleSetting
            ? DelayOperators
            : SpellIdConditionFields.Contains(field?.Name) || ItemIdConditionFields.Contains(field?.Name)
                ? SpellOperators
                : field is { IsCustom: false, Type: ConditionFieldType.Bool }
                    ? BoolOperators
                    : field is { IsCustom: false, Type: ConditionFieldType.String }
                        ? TextOperators
                        : AllOperators;

        var cell = (DataGridViewComboBoxCell)row.Cells[OperatorColumn];
        cell.Items.Clear();
        cell.Items.AddRange(ops);
        var normalizedOp = isRuleSetting ? "==" : ConditionExpression.NormalizeOperator(desiredOp);
        var index = normalizedOp.Length == 0 ? -1 : Array.IndexOf(ops, normalizedOp);
        cell.Value = ops[index >= 0 ? index : 0];
        cell.ReadOnly = isRuleSetting;
        cell.DisplayStyle = isRuleSetting
            ? DataGridViewComboBoxDisplayStyle.Nothing
            : DataGridViewComboBoxDisplayStyle.DropDownButton;
    }

    private void ConfigureValueCell(DataGridViewRow row, string? rawValue, bool preserveRaw)
    {
        var field = SelectedField(row);
        if (SpellIdConditionFields.Contains(field?.Name))
        {
            ConfigureSpellValueCell(row, rawValue, preserveRaw);
            return;
        }

        if (ItemIdConditionFields.Contains(field?.Name))
        {
            ConfigureItemValueCell(row, rawValue, preserveRaw);
            return;
        }

        if (string.Equals(field?.Name, "首领战", StringComparison.Ordinal))
        {
            var bossValue = rawValue?.Trim() ?? string.Empty;
            if (!preserveRaw && bossValue.Length == 0)
            {
                bossValue = "0";
            }
            row.Cells[ValueColumn] = new BossValueCell { Value = bossValue.Length == 0 ? "0" : bossValue };
            return;
        }

        if (field is { IsCustom: false, Type: ConditionFieldType.Bool })
        {
            var combo = new DataGridViewComboBoxCell
            {
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                FlatStyle = FlatStyle.Flat
            };
            combo.Items.AddRange(["是 (true)", "否 (false)"]);
            combo.Value = IsFalseText(rawValue) ? "否 (false)" : "是 (true)";
            row.Cells[ValueColumn] = combo;
            return;
        }

        var text = rawValue?.Trim() ?? string.Empty;
        if (ConditionExpression.IsInOperator(row.Cells[OperatorColumn].Value?.ToString()))
        {
            text = ConditionExpression.NormalizeInValue(text);
        }
        else if (!preserveRaw && field is { IsCustom: false, Type: ConditionFieldType.Int })
        {
            text = TryParseIntegerText(text, out var number)
                ? number.ToString("0", CultureInfo.InvariantCulture)
                : "0";
        }
        else if (text.Length == 0 && field is { IsCustom: false, Type: ConditionFieldType.Int })
        {
            text = "0";
        }

        row.Cells[ValueColumn] = SupportsValueReference(row, field)
            ? new ReferenceValueCell { Value = text }
            : new DataGridViewTextBoxCell { Value = text };
    }

    // 数值字段的值既可手填数字, 也可引用动态数值字段; in/not in 是字面量列表, 不参与。
    private bool SupportsValueReference(DataGridViewRow row, FieldItem? field)
    {
        return _valueReferenceFields.Count > 0
            && field is not null
            && (field.IsCustom || field.Type == ConditionFieldType.Int)
            && !IsRuleSettingField(field)
            && !ConditionExpression.IsInOperator(row.Cells[OperatorColumn].Value?.ToString());
    }

    private void ShowValueReferenceDropDown(int rowIndex, int columnIndex)
    {
        CloseConditionComboDropDown();
        _conditionsGrid.EndEdit();
        if (rowIndex < 0 || rowIndex >= _conditionsGrid.Rows.Count
            || _conditionsGrid.Rows[rowIndex].Cells[columnIndex] is not ReferenceValueCell cell)
        {
            return;
        }

        _conditionsGrid.CurrentCell = cell;
        var currentValue = cell.Value?.ToString()?.Trim() ?? string.Empty;
        // 首项回到手动数字: 当前填的是数字就保留, 填的是引用名则回落到 0。
        var manualValue = currentValue.Length > 0 && TryParseIntegerText(currentValue, out var number)
            ? number.ToString("0", CultureInfo.InvariantCulture)
            : "0";
        var options = new List<UiDropDownOption>
        {
            new(manualValue, "手动输入数字", LeadingText: manualValue)
        };
        options.AddRange(_valueReferenceFields.Select(field =>
            new UiDropDownOption(field.Name, field.DisplayName)));

        var cellBounds = _conditionsGrid.GetCellDisplayRectangle(columnIndex, rowIndex, cutOverflow: true);
        ToolStripDropDown? dropDown = null;
        dropDown = UiDropDownPopup.Show(
            _conditionsGrid,
            cellBounds,
            options,
            currentValue,
            selected =>
            {
                var value = selected.Value?.ToString() ?? string.Empty;
                cell.Value = value;
                _conditionsGrid.InvalidateCell(cell);
                UpdatePreview();
                if (string.Equals(value, manualValue, StringComparison.Ordinal))
                {
                    BeginInvoke(() => BeginEditValueCell(cell));
                }
            },
            preferredWidth: 300,
            closed: () =>
            {
                if (ReferenceEquals(_conditionComboDropDown, dropDown))
                {
                    _conditionComboDropDown = null;
                }
            });
        _conditionComboDropDown = dropDown;
    }

    private void BeginEditValueCell(DataGridViewCell cell)
    {
        if (IsDisposed || Disposing || cell.DataGridView is null || cell.ReadOnly)
        {
            return;
        }

        _conditionsGrid.CurrentCell = cell;
        _conditionsGrid.BeginEdit(selectAll: true);
    }

    private void ConfigureSpellValueCell(DataGridViewRow row, string? rawValue, bool preserveRaw)
    {
        var combo = new DataGridViewComboBoxCell
        {
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            FlatStyle = FlatStyle.Flat,
            DisplayMember = nameof(SpellValueItem.Display),
            ValueMember = nameof(SpellValueItem.Value),
            ValueType = typeof(string)
        };
        foreach (var spell in _spells
                     .Where(spell => spell.SpellId > 0)
                     .DistinctBy(spell => spell.SpellId)
                     .OrderBy(spell => spell.Index)
                     .ThenBy(spell => spell.SpellId))
        {
            combo.Items.Add(new SpellValueItem(
                spell.SpellId.ToString(CultureInfo.InvariantCulture),
                spell.DisplayName,
                spell.SpellId,
                spell.Index));
        }

        var value = preserveRaw ? rawValue?.Trim() ?? string.Empty : string.Empty;
        if (value.Length == 0 && combo.Items.Count > 0)
        {
            value = ((SpellValueItem)combo.Items[0]!).Value;
        }

        if (value.Length > 0
            && !combo.Items.Cast<SpellValueItem>().Any(item =>
                string.Equals(item.Value, value, StringComparison.Ordinal)))
        {
            var parsed = long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var spellId)
                ? spellId
                : 0;
            combo.Items.Insert(0, new SpellValueItem(
                value,
                $"spellId {value}（不存在）",
                parsed,
                Missing: true));
        }

        combo.Value = value;
        row.Cells[ValueColumn] = combo;
    }

    private void ConfigureItemValueCell(DataGridViewRow row, string? rawValue, bool preserveRaw)
    {
        var combo = new DataGridViewComboBoxCell
        {
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            FlatStyle = FlatStyle.Flat,
            DisplayMember = nameof(ItemValueItem.Display),
            ValueMember = nameof(ItemValueItem.Value),
            ValueType = typeof(string)
        };
        foreach (var item in _items
                     .Where(item => item.ItemId > 0)
                     .DistinctBy(item => item.ItemId)
                     .OrderBy(item => item.Index)
                     .ThenBy(item => item.ItemId))
        {
            combo.Items.Add(new ItemValueItem(
                item.ItemId.ToString(CultureInfo.InvariantCulture),
                item.DisplayName,
                item.ItemId,
                item.Index));
        }

        var value = preserveRaw ? rawValue?.Trim() ?? string.Empty : string.Empty;
        if (value.Length == 0 && combo.Items.Count > 0)
        {
            value = ((ItemValueItem)combo.Items[0]!).Value;
        }

        if (value.Length > 0
            && !combo.Items.Cast<ItemValueItem>().Any(item =>
                string.Equals(item.Value, value, StringComparison.Ordinal)))
        {
            var parsed = long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId)
                ? itemId
                : 0;
            combo.Items.Insert(0, new ItemValueItem(
                value,
                $"itemId {value}（不存在）",
                parsed,
                Missing: true));
        }

        combo.Value = value;
        row.Cells[ValueColumn] = combo;
    }

    private string? GetMissingSpellMessage(DataGridViewRow row)
    {
        var field = SelectedField(row)?.Name;
        if (!SpellIdConditionFields.Contains(field))
        {
            return null;
        }

        var value = row.Cells[ValueColumn].Value?.ToString()?.Trim() ?? string.Empty;
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var spellId)
            || spellId <= 0
            || !_spells.Any(spell => spell.SpellId == spellId))
        {
            return $"{field}不存在 spellId 为 {value} 的法术";
        }

        return null;
    }

    private string? GetMissingItemMessage(DataGridViewRow row)
    {
        var field = SelectedField(row)?.Name;
        if (!ItemIdConditionFields.Contains(field))
        {
            return null;
        }

        var value = row.Cells[ValueColumn].Value?.ToString()?.Trim() ?? string.Empty;
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId)
            || itemId <= 0
            || !_items.Any(item => item.ItemId == itemId))
        {
            return $"{field}不存在 itemId 为 {value} 的物品";
        }

        return null;
    }

    private List<ConditionTerm> CollectTerms()
    {
        var terms = new List<ConditionTerm>();
        for (var i = 0; i < _conditionsGrid.Rows.Count; i++)
        {
            var row = _conditionsGrid.Rows[i];
            var fieldItem = SelectedField(row);
            if (IsRuleSettingField(fieldItem))
            {
                continue;
            }

            var field = fieldItem?.Name.Trim() ?? string.Empty;
            var op = row.Cells[OperatorColumn].Value?.ToString() ?? "==";
            var value = ReadRowValue(row);
            if (field.Length == 0 || value.Length == 0)
            {
                continue;
            }

            terms.Add(new ConditionTerm(
                OrWithPrevious: i > 0 && row.Cells[ConnectorColumn].Value?.ToString() == "或",
                field,
                op,
                value));
        }

        return terms;
    }

    private static string ReadRowValue(DataGridViewRow row)
    {
        var value = row.Cells[ValueColumn].Value?.ToString() ?? string.Empty;
        return SelectedField(row) is { IsCustom: false, Type: ConditionFieldType.Bool }
            ? value.StartsWith("否", StringComparison.Ordinal) ? "false" : "true"
            : ConditionExpression.IsInOperator(row.Cells[OperatorColumn].Value?.ToString())
                ? ConditionExpression.NormalizeInValue(value)
                : value.Trim();
    }

    private static FieldItem? SelectedField(DataGridViewRow row)
    {
        var cell = row.Cells[FieldColumn];
        if (cell.Value is FieldItem selected)
        {
            return selected;
        }

        // 新表格统一保存字段名；同时兼容已创建单元格或 WinForms 回写的显示文字。
        var text = cell.Value?.ToString()?.Trim();

        if (string.IsNullOrWhiteSpace(text) || cell is not DataGridViewComboBoxCell combo)
        {
            return null;
        }

        return combo.Items
            .OfType<FieldItem>()
            .FirstOrDefault(item =>
                string.Equals(item.Name, text, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Display, text, StringComparison.Ordinal));
    }

    private static ConditionFieldCategory SelectedCategory(DataGridViewRow row)
    {
        var display = row.Cells[TypeColumn].Value?.ToString();
        return CategoryItems.FirstOrDefault(item =>
            string.Equals(item.Display, display, StringComparison.Ordinal))?.Category
            ?? ConditionFieldCategory.State;
    }

    private static string NormalizeClassification(string? value)
        => string.IsNullOrWhiteSpace(value) ? Unclassified : value.Trim();

    private static string GetClassificationDisplayName(
        ConditionFieldCategory category,
        string classification)
        => category == ConditionFieldCategory.State
            ? ClassStateCatalog.GetCategoryDisplayName(classification)
            : classification;

    private static string GetClassificationStorageName(
        ConditionFieldCategory category,
        string? displayName)
    {
        var normalized = NormalizeClassification(displayName);
        return category == ConditionFieldCategory.State
            ? ClassStateCatalog.GetStorageCategoryFromDisplay(normalized)
            : normalized;
    }

    private void UpdatePreview()
    {
        _ = TryReadDelay(out var delayMs, showWarning: false);
        _ = TryReadLogicDelay(out var logicDelayMs, showWarning: false);
        _ = TryReadContinueLogic(out var continueLogic, showWarning: false);
        var full = ComposePreview(ConditionExpression.Build(CollectTerms()), delayMs, logicDelayMs, continueLogic);
        _previewLabel.Text = full.Length == 0 ? "预览: (无条件, 始终命中)" : $"预览: {full}";
        // 单行预览会被省略号截断, 悬停看完整表达式。
        _previewToolTip.SetToolTip(_previewLabel, full.Length == 0 ? string.Empty : full);
    }

    // 把主条件文本与子条件合成为可读的整体表达式(与 ModuleRule.DescribeCondition 同形)。
    private string ComposePreview(string mainText, int? delayMs, int? logicDelayMs, bool? continueLogic)
    {
        var conditionText = mainText;
        if (_subConditions.Count > 0)
        {
            var any = string.Join(" | ", _subConditions);
            conditionText = mainText.Length == 0 ? $"任一({any})" : $"{mainText}  且任一({any})";
        }

        if (delayMs is > 0)
        {
            conditionText = conditionText.Length == 0
                ? $"延迟 {delayMs.Value} ms"
                : $"{conditionText}；延迟 {delayMs.Value} ms";
        }

        if (logicDelayMs is > 0)
        {
            conditionText = conditionText.Length == 0
                ? $"逻辑延迟 {logicDelayMs.Value} ms"
                : $"{conditionText}；逻辑延迟 {logicDelayMs.Value} ms";
        }

        if (continueLogic is true)
        {
            conditionText = conditionText.Length == 0
                ? "继续逻辑"
                : $"{conditionText}；继续逻辑";
        }

        return conditionText;
    }

    private bool TryReadDelay(out int? delayMs, bool showWarning = true)
    {
        return TryReadRuleSettingDelay(
            ShigureConditionFields.Delay,
            "延迟",
            out delayMs,
            showWarning);
    }

    private bool TryReadLogicDelay(out int? delayMs, bool showWarning = true)
    {
        return TryReadRuleSettingDelay(
            ShigureConditionFields.LogicDelay,
            "逻辑延迟",
            out delayMs,
            showWarning);
    }

    private bool TryReadContinueLogic(out bool? continueLogic, bool showWarning = true)
    {
        continueLogic = null;
        var rows = _conditionsGrid.Rows
            .Cast<DataGridViewRow>()
            .Where(row => IsRuleSettingField(SelectedField(row), ShigureConditionFields.ContinueLogic))
            .ToList();
        if (rows.Count > 1)
        {
            if (showWarning)
            {
                MessageBox.Show(
                    "每条规则只能设置一个“继续逻辑”。请删除多余的 Shigure 继续逻辑行。",
                    "Shigure",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            return false;
        }

        if (rows.Count == 0)
        {
            return true;
        }

        continueLogic = IsFalseText(ReadRowValue(rows[0])) ? null : true;
        return true;
    }

    private bool TryReadRuleSettingDelay(
        string fieldName,
        string displayName,
        out int? delayMs,
        bool showWarning)
    {
        delayMs = null;
        var delayRows = _conditionsGrid.Rows
            .Cast<DataGridViewRow>()
            .Where(row => IsRuleSettingField(SelectedField(row), fieldName))
            .ToList();
        if (delayRows.Count > 1)
        {
            if (showWarning)
            {
                MessageBox.Show(
                    $"每条规则只能设置一个“{displayName}”。请删除多余的 Shigure {displayName}行。",
                    "Shigure",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            return false;
        }

        if (delayRows.Count == 0)
        {
            return true;
        }

        var value = ReadRowValue(delayRows[0]);
        if (!TryParseDelayText(value, out var parsed))
        {
            if (showWarning)
            {
                MessageBox.Show(
                    $"{displayName}必须是 0 到 2147483647 之间的整数，单位为 ms。",
                    "Shigure",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            return false;
        }

        delayMs = parsed > 0 ? parsed : null;
        return true;
    }

    private static bool IsDelayField(FieldItem? field)
    {
        return IsRuleSettingField(field, ShigureConditionFields.Delay);
    }

    private static bool IsLogicDelayField(FieldItem? field)
    {
        return IsRuleSettingField(field, ShigureConditionFields.LogicDelay);
    }

    private static bool IsContinueLogicField(FieldItem? field)
    {
        return IsRuleSettingField(field, ShigureConditionFields.ContinueLogic);
    }

    private static bool IsRuleSettingField(FieldItem? field)
    {
        return IsDelayField(field) || IsLogicDelayField(field) || IsContinueLogicField(field);
    }

    private static bool IsRuleSettingField(FieldItem? field, string fieldName)
    {
        return field is not null
            && field.Category == ConditionFieldCategory.Shigure
            && string.Equals(field.Name, fieldName, StringComparison.Ordinal);
    }

    private static bool TryParseDelayText(string? text, out int value)
    {
        return int.TryParse(
            text?.Trim(),
            NumberStyles.Integer | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out value)
            && value >= 0;
    }

    private static bool IsFalseText(string? value)
    {
        return value?.Trim().ToLowerInvariant() is "false" or "no" or "否" or "0";
    }

    private static bool TryParseIntegerText(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            || parsed != decimal.Truncate(parsed)
            || parsed < -1000000
            || parsed > 1000000)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private sealed record CategoryItem(string Display, ConditionFieldCategory Category)
    {
        public override string ToString() => Display;
    }

    private sealed record FieldItem(
        string Name,
        string Display,
        ConditionFieldType Type,
        ConditionFieldCategory Category,
        string? Classification,
        bool IsCustom,
        long? ItemId = null)
    {
        public override string ToString() => Display;
    }

    private sealed record SpellValueItem(
        string Value,
        string Display,
        long SpellId,
        int? Index = null,
        bool Missing = false)
    {
        public override string ToString() => Display;
    }

    private sealed record ItemValueItem(
        string Value,
        string Display,
        long ItemId,
        int? Index = null,
        bool Missing = false)
    {
        public override string ToString() => Display;
    }

    private sealed class BossValueCell : DataGridViewTextBoxCell;

    // 值列: 可手填数字, 也可从动态数值字段中选一个作为引用。
    private sealed class ReferenceValueCell : DataGridViewTextBoxCell;
}
