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
    private const int ConnectorWidth = 90;
    private const int CategoryWidth = 110;
    private const int FieldWidth = 270;
    private const int OpWidth = 90;
    private const int ValueWidth = 170;
    private const int DeleteWidth = 36;
    private const int RowTotalWidth = ConnectorWidth + CategoryWidth + FieldWidth + OpWidth + ValueWidth + DeleteWidth;

    private static readonly string[] AllOperators = ["==", "!=", ">", ">=", "<", "<=", "in", "not in"];
    private static readonly string[] TextOperators = ["==", "!=", "in", "not in"];
    private static readonly string[] BoolOperators = ["==", "!="];
    private static readonly CategoryItem RecognizedAuraCategory = new("识别光环", ConditionFieldCategory.RecognizedAura);
    private static readonly CategoryItem[] CategoryItems =
    [
        new("状态", ConditionFieldCategory.State),
        new("光环", ConditionFieldCategory.Aura),
        new("技能", ConditionFieldCategory.Spell),
        new("动态单位", ConditionFieldCategory.DynamicUnit)
    ];

    private readonly IReadOnlyList<ConditionField> _fields;
    private readonly string _originalCondition;
    private readonly bool _allowSubConditions;
    private readonly FlowLayoutPanel _rowsPanel = new();
    private readonly Label _previewLabel = new();
    private readonly ToolTip _previewToolTip = new();
    private readonly List<ConditionRow> _rows = new();
    private readonly List<string> _subConditions = new();
    private readonly ListBox _subList = new();

    public string ConditionText { get; private set; } = string.Empty;

    // 子条件: 与主条件是「且」、子条件彼此是「或」。allowSubConditions=false(默认)时不显示该区,
    // 也用于子条件自身的嵌套编辑弹窗防止无限递归。
    public IReadOnlyList<string> SubConditions => _subConditions;

    public ConditionEditorForm(
        IReadOnlyList<ConditionField> fields,
        string? condition,
        IEnumerable<string>? subConditions = null,
        bool allowSubConditions = false)
    {
        _fields = fields;
        _originalCondition = condition ?? string.Empty;
        _allowSubConditions = allowSubConditions;
        if (subConditions is not null)
        {
            _subConditions.AddRange(subConditions
                .Select(sub => sub?.Trim() ?? string.Empty)
                .Where(sub => sub.Length > 0));
        }

        InitializeComponent();

        foreach (var term in ConditionExpression.Parse(condition))
        {
            AddRow(term);
        }

        // 空条件直接落在一条可填行上, 无需先去找"添加条件"。
        if (_rows.Count == 0)
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

    private void InitializeComponent()
    {
        Text = "编辑条件";
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = UiTheme.Background;
        ForeColor = UiTheme.Text;
        // 加一个滚动条宽度, 避免行数多时垂直滚动条盖住每行的 ✕ 删除按钮。
        // 子条件区会额外占高度, 允许时把窗口加高, 给主条件行留出空间。
        var initialHeight = _allowSubConditions ? 650 : 460;
        ClientSize = new Size(RowTotalWidth + 50 + SystemInformation.VerticalScrollBarWidth, initialHeight);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        // 允许纵向放大以一次看到更多条件行; 不小于初始尺寸。
        MinimumSize = Size;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Background,
            Padding = new Padding(12, 10, 12, 10),
            ColumnCount = 1
        };
        Controls.Add(root);

        var rowIndex = 0;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.Controls.Add(BuildHeaderRow(), 0, rowIndex++);

        _rowsPanel.Dock = DockStyle.Fill;
        _rowsPanel.BackColor = UiTheme.SurfaceRaised;
        _rowsPanel.FlowDirection = FlowDirection.TopDown;
        _rowsPanel.WrapContents = false;
        _rowsPanel.AutoScroll = true;
        _rowsPanel.Margin = new Padding(0, 4, 0, 6);
        _rowsPanel.Padding = new Padding(8, 6, 8, 6);
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(_rowsPanel, 0, rowIndex++);

        // 「添加条件」紧贴主条件行下方, 明确它作用于上面的主条件(而非子条件)。
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.Controls.Add(BuildAddConditionRow(), 0, rowIndex++);

        if (_allowSubConditions)
        {
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 172));
            root.Controls.Add(BuildSubConditionsPanel(), 0, rowIndex++);
        }

        _previewLabel.Dock = DockStyle.Fill;
        _previewLabel.ForeColor = UiTheme.Muted;
        _previewLabel.TextAlign = ContentAlignment.MiddleLeft;
        _previewLabel.AutoEllipsis = true;
        _previewLabel.Margin = new Padding(0);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.Controls.Add(_previewLabel, 0, rowIndex++);

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.Controls.Add(BuildActionRow(), 0, rowIndex++);
        root.RowCount = rowIndex;
    }

    // 子条件区: 标题 + 暗色列表 + 添加/编辑/删除。每条子条件本身也是一条完整条件,
    // 通过嵌套的(无子条件区的)条件编辑弹窗来编辑。
    private Control BuildSubConditionsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Background,
            Margin = new Padding(0, 4, 0, 4),
            ColumnCount = 2,
            RowCount = 2
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
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
        _subList.BackColor = UiTheme.Field;
        _subList.ForeColor = UiTheme.Text;
        _subList.BorderStyle = BorderStyle.FixedSingle;
        _subList.IntegralHeight = false;
        _subList.Margin = new Padding(0, 0, 8, 0);
        _subList.DoubleClick += (_, _) => EditSelectedSubCondition();
        panel.Controls.Add(_subList, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = UiTheme.Background,
            Margin = new Padding(0)
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
        button.Width = 108;
        button.Height = 30;
        button.Margin = new Padding(0, 0, 0, 6);
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
        using var editor = new ConditionEditorForm(_fields, current);
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

    private Control BuildHeaderRow()
    {
        var header = new TableLayoutPanel
        {
            Width = RowTotalWidth,
            Height = 24,
            BackColor = UiTheme.Background,
            Margin = new Padding(0),
            ColumnCount = 6,
            RowCount = 1
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ConnectorWidth));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, CategoryWidth));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, FieldWidth));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, OpWidth));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ValueWidth));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DeleteWidth));

        header.Controls.Add(CreateHeaderLabel("连接"), 0, 0);
        header.Controls.Add(CreateHeaderLabel("类型"), 1, 0);
        header.Controls.Add(CreateHeaderLabel("字段"), 2, 0);
        header.Controls.Add(CreateHeaderLabel("判断"), 3, 0);
        header.Controls.Add(CreateHeaderLabel("值"), 4, 0);
        header.Controls.Add(CreateHeaderLabel(string.Empty), 5, 0);
        return header;
    }

    // 「添加条件」按钮单独成行, 放在主条件行下方、子条件区上方。
    private Control BuildAddConditionRow()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = UiTheme.Background,
            Margin = new Padding(0, 2, 0, 2)
        };

        var addButton = UiTheme.CreateButton("添加条件", UiTheme.Field, UiTheme.Text);
        addButton.Width = 96;
        addButton.Height = 30;
        addButton.Margin = new Padding(0, 2, 0, 0);
        addButton.Click += (_, _) =>
        {
            var row = AddRow(null);
            RefreshConnectors();
            UpdatePreview();
            _rowsPanel.ScrollControlIntoView(row.Panel);
            row.CategoryBox.Focus();
        };
        panel.Controls.Add(addButton);
        return panel;
    }

    private Control BuildActionRow()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Background,
            Margin = new Padding(0),
            ColumnCount = 2,
            RowCount = 1
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));

        var rightButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = UiTheme.Background,
            Margin = new Padding(0)
        };

        var okButton = UiTheme.CreateButton("确定", UiTheme.Accent, Color.Black);
        okButton.Width = 72;
        okButton.Height = 30;
        okButton.Margin = new Padding(6, 4, 0, 0);
        okButton.Click += (_, _) => TryConfirm();

        var cancelButton = UiTheme.CreateButton("取消", UiTheme.Field, UiTheme.Text);
        cancelButton.Width = 72;
        cancelButton.Height = 30;
        cancelButton.Margin = new Padding(6, 4, 0, 0);
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
        var incomplete = _rows.Count(IsRowIncomplete);
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
        DialogResult = DialogResult.OK;
    }

    // 恰好字段与值一空一非空 → 不完整; 两者都空只是空行(静默忽略, 不算不完整)。
    private static bool IsRowIncomplete(ConditionRow row)
    {
        var field = row.SelectedField?.Name.Trim() ?? string.Empty;
        var value = ReadRowValue(row);
        return (field.Length == 0) ^ (value.Length == 0);
    }

    private ConditionRow AddRow(ConditionTerm? term)
    {
        var panel = new TableLayoutPanel
        {
            Width = RowTotalWidth,
            Height = 34,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0, 1, 0, 5),
            ColumnCount = 6,
            RowCount = 1
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ConnectorWidth));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, CategoryWidth));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, FieldWidth));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, OpWidth));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ValueWidth));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DeleteWidth));

        var connectorBox = new ComboBox();
        UiTheme.StyleComboBox(connectorBox);
        connectorBox.Items.AddRange(["且", "或"]);
        connectorBox.SelectedIndex = term?.OrWithPrevious == true ? 1 : 0;
        connectorBox.Dock = DockStyle.Fill;
        connectorBox.Margin = new Padding(0, 2, 8, 2);

        var categoryBox = new ComboBox();
        UiTheme.StyleComboBox(categoryBox);
        categoryBox.Items.AddRange(CategoryItems);
        categoryBox.Items.Add(RecognizedAuraCategory);
        categoryBox.Dock = DockStyle.Fill;
        categoryBox.Margin = new Padding(0, 2, 8, 2);

        var fieldBox = new ComboBox();
        UiTheme.StyleComboBox(fieldBox);
        fieldBox.Dock = DockStyle.Fill;
        fieldBox.Margin = new Padding(0, 2, 8, 2);
        fieldBox.AutoCompleteSource = AutoCompleteSource.ListItems;
        fieldBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;

        var opBox = new ComboBox();
        UiTheme.StyleComboBox(opBox);
        opBox.Dock = DockStyle.Fill;
        opBox.Margin = new Padding(0, 2, 8, 2);

        var valueHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0, 2, 8, 2)
        };

        var deleteButton = UiTheme.CreateButton("✕", UiTheme.Field, UiTheme.Danger);
        deleteButton.AutoSize = false;
        deleteButton.Width = DeleteWidth - 6;
        deleteButton.Height = 30;
        deleteButton.Margin = new Padding(0, 2, 0, 2);
        deleteButton.Padding = new Padding(0);

        panel.Controls.Add(connectorBox, 0, 0);
        panel.Controls.Add(categoryBox, 1, 0);
        panel.Controls.Add(fieldBox, 2, 0);
        panel.Controls.Add(opBox, 3, 0);
        panel.Controls.Add(valueHost, 4, 0);
        panel.Controls.Add(deleteButton, 5, 0);

        var row = new ConditionRow(panel, connectorBox, categoryBox, fieldBox, opBox, valueHost);
        SelectCategory(row, ResolveCategory(term?.Field));
        PopulateFields(row, term?.Field);
        PopulateOps(row, term?.Op);
        CreateValueControl(row, term?.Value, preserveRaw: true);

        connectorBox.SelectedIndexChanged += (_, _) => UpdatePreview();
        categoryBox.SelectedIndexChanged += (_, _) =>
        {
            PopulateFields(row, null);
            OnFieldChanged(row);
            UpdatePreview();
        };
        fieldBox.SelectedIndexChanged += (_, _) =>
        {
            OnFieldChanged(row);
            UpdatePreview();
        };
        fieldBox.TextChanged += (_, _) =>
        {
            if (row.SelectedCategory?.Category == ConditionFieldCategory.RecognizedAura)
            {
                OnFieldChanged(row);
                UpdatePreview();
            }
        };
        opBox.SelectedIndexChanged += (_, _) =>
        {
            OnOperatorChanged(row);
            UpdatePreview();
        };
        deleteButton.Click += (_, _) => RemoveRow(row);

        _rows.Add(row);
        _rowsPanel.Controls.Add(panel);
        return row;
    }

    private void RemoveRow(ConditionRow row)
    {
        _rows.Remove(row);
        _rowsPanel.Controls.Remove(row.Panel);
        row.Panel.Dispose();
        RefreshConnectors();
        UpdatePreview();
    }

    private void RefreshConnectors()
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            _rows[i].Connector.Visible = i > 0;
        }
    }

    private void PopulateFields(ConditionRow row, string? currentField)
    {
        var fieldBox = row.FieldBox;
        var category = row.SelectedCategory?.Category ?? ConditionFieldCategory.State;
        var isRecognizedAura = category == ConditionFieldCategory.RecognizedAura;
        fieldBox.DropDownStyle = isRecognizedAura ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList;
        fieldBox.Items.Clear();
        foreach (var field in _fields.Where(field => field.Category == category))
        {
            fieldBox.Items.Add(new FieldItem(field.Name, field.DisplayName, field.Type, field.Category, IsCustom: false));
        }

        if (!string.IsNullOrWhiteSpace(currentField))
        {
            if (isRecognizedAura)
            {
                currentField = RecognizedAuraFields.ToFieldName(currentField);
            }

            var index = FindFieldIndex(fieldBox, currentField);
            if (index < 0)
            {
                // 目录里没有的字段(如 group.* 或手写字段)保留为自定义项, 避免丢失原条件。
                if (isRecognizedAura && RecognizedAuraFields.TryGetName(currentField, out var auraName))
                {
                    fieldBox.Items.Add(new FieldItem(currentField, auraName, ConditionFieldType.Int, category, IsCustom: false));
                }
                else
                {
                    fieldBox.Items.Add(new FieldItem(currentField, $"{currentField} (自定义)", ConditionFieldType.Int, category, IsCustom: true));
                }

                index = fieldBox.Items.Count - 1;
            }

            fieldBox.SelectedIndex = index;
            return;
        }

        if (fieldBox.Items.Count > 0)
        {
            fieldBox.SelectedIndex = 0;
        }
        else if (isRecognizedAura)
        {
            fieldBox.Text = RecognizedAuraFields.Prefix;
        }
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

        if (RecognizedAuraFields.TryGetName(fieldName, out _))
        {
            return ConditionFieldCategory.RecognizedAura;
        }

        return ConditionFieldCategory.State;
    }

    private static void SelectCategory(ConditionRow row, ConditionFieldCategory category)
    {
        for (var i = 0; i < row.CategoryBox.Items.Count; i++)
        {
            if (row.CategoryBox.Items[i] is CategoryItem item && item.Category == category)
            {
                row.CategoryBox.SelectedIndex = i;
                return;
            }
        }

        row.CategoryBox.SelectedIndex = 0;
    }

    private static int FindFieldIndex(ComboBox fieldBox, string fieldName)
    {
        for (var i = 0; i < fieldBox.Items.Count; i++)
        {
            if (fieldBox.Items[i] is FieldItem item
                && string.Equals(item.Name, fieldName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static void PopulateOps(ConditionRow row, string? desiredOp)
    {
        var field = row.SelectedField;
        var ops = field is { IsCustom: false, Type: ConditionFieldType.Bool }
            ? BoolOperators
            : field is { IsCustom: false, Type: ConditionFieldType.String }
                ? TextOperators
                : AllOperators;

        row.OpBox.Items.Clear();
        row.OpBox.Items.AddRange(ops);
        var normalizedOp = ConditionExpression.NormalizeOperator(desiredOp);
        var index = normalizedOp.Length == 0 ? -1 : Array.IndexOf(ops, normalizedOp);
        row.OpBox.SelectedIndex = index >= 0 ? index : 0;
    }

    private void CreateValueControl(ConditionRow row, string? rawValue, bool preserveRaw)
    {
        foreach (Control old in row.ValueHost.Controls)
        {
            old.Dispose();
        }

        row.ValueHost.Controls.Clear();

        var field = row.SelectedField;
        var usesListValue = ConditionExpression.IsInOperator(row.OpBox.SelectedItem?.ToString());
        Control control;
        if (field is { IsCustom: false, Type: ConditionFieldType.Bool })
        {
            var combo = new ComboBox();
            UiTheme.StyleComboBox(combo);
            combo.Items.AddRange(["是 (true)", "否 (false)"]);
            combo.SelectedIndex = IsFalseText(rawValue) ? 1 : 0;
            combo.SelectedIndexChanged += (_, _) => UpdatePreview();
            control = combo;
        }
        else if (!usesListValue
            && field is { IsCustom: false, Type: ConditionFieldType.Int }
            && (TryParseIntegerText(rawValue, out var number) || !preserveRaw))
        {
            // 解析失败时 number 为 0, 仅在 preserveRaw=false 的字段切换场景下走到这里。
            var numeric = new NumericUpDown
            {
                Minimum = -1000000,
                Maximum = 1000000,
                Value = number
            };
            UiTheme.StyleNumericUpDown(numeric);
            numeric.ValueChanged += (_, _) => UpdatePreview();
            numeric.TextChanged += (_, _) => UpdatePreview();
            control = numeric;
        }
        else
        {
            // 字符串字段、自定义字段或无法转成数字的旧值用文本框兜底。
            var box = new TextBox
            {
                Text = usesListValue ? ConditionExpression.NormalizeInValue(rawValue) : rawValue ?? string.Empty
            };
            UiTheme.StyleTextBox(box);
            if (usesListValue)
            {
                box.PlaceholderText = "例如: 12, 13";
            }

            box.TextChanged += (_, _) => UpdatePreview();
            control = box;
        }

        control.Dock = DockStyle.Fill;
        row.ValueHost.Controls.Add(control);
        row.ValueControl = control;
    }

    private void OnFieldChanged(ConditionRow row)
    {
        var currentOp = row.OpBox.SelectedItem?.ToString();
        var currentValue = ReadRowValue(row);
        PopulateOps(row, currentOp);
        CreateValueControl(row, currentValue, preserveRaw: false);
    }

    private void OnOperatorChanged(ConditionRow row)
    {
        var currentValue = ReadRowValue(row);
        CreateValueControl(row, currentValue, preserveRaw: true);
    }

    private List<ConditionTerm> CollectTerms()
    {
        var terms = new List<ConditionTerm>();
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            var field = row.SelectedField?.Name.Trim() ?? string.Empty;
            var op = row.OpBox.SelectedItem?.ToString() ?? "==";
            var value = ReadRowValue(row);
            if (field.Length == 0 || value.Length == 0)
            {
                continue;
            }

            terms.Add(new ConditionTerm(
                OrWithPrevious: i > 0 && row.Connector.SelectedIndex == 1,
                field,
                op,
                value));
        }

        return terms;
    }

    private static string ReadRowValue(ConditionRow row)
    {
        return row.ValueControl switch
        {
            NumericUpDown numeric => ReadNumericText(numeric),
            ComboBox combo => combo.SelectedIndex == 1 ? "false" : "true",
            TextBox box => ConditionExpression.IsInOperator(row.OpBox.SelectedItem?.ToString())
                ? ConditionExpression.NormalizeInValue(box.Text)
                : box.Text.Trim(),
            _ => string.Empty
        };
    }

    private static string ReadNumericText(NumericUpDown numeric)
    {
        // 输入中的文本可能尚未提交到 Value, 优先读文本。
        return decimal.TryParse(numeric.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var typed)
            ? decimal.Truncate(typed).ToString("0", CultureInfo.InvariantCulture)
            : numeric.Value.ToString("0", CultureInfo.InvariantCulture);
    }

    private void UpdatePreview()
    {
        var full = ComposePreview(ConditionExpression.Build(CollectTerms()));
        _previewLabel.Text = full.Length == 0 ? "预览: (无条件, 始终命中)" : $"预览: {full}";
        // 单行预览会被省略号截断, 悬停看完整表达式。
        _previewToolTip.SetToolTip(_previewLabel, full.Length == 0 ? string.Empty : full);
    }

    // 把主条件文本与子条件合成为可读的整体表达式(与 ModuleRule.DescribeCondition 同形)。
    private string ComposePreview(string mainText)
    {
        if (_subConditions.Count == 0)
        {
            return mainText;
        }

        var any = string.Join(" | ", _subConditions);
        return mainText.Length == 0 ? $"任一({any})" : $"{mainText}  且任一({any})";
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

    private static Label CreateHeaderLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        };
    }

    private sealed record CategoryItem(string Display, ConditionFieldCategory Category)
    {
        public override string ToString() => Display;
    }

    private sealed record FieldItem(string Name, string Display, ConditionFieldType Type, ConditionFieldCategory Category, bool IsCustom)
    {
        public override string ToString() => Display;
    }

    private sealed class ConditionRow(
        TableLayoutPanel panel,
        ComboBox connector,
        ComboBox categoryBox,
        ComboBox fieldBox,
        ComboBox opBox,
        Panel valueHost)
    {
        public TableLayoutPanel Panel { get; } = panel;
        public ComboBox Connector { get; } = connector;
        public ComboBox CategoryBox { get; } = categoryBox;
        public ComboBox FieldBox { get; } = fieldBox;
        public ComboBox OpBox { get; } = opBox;
        public Panel ValueHost { get; } = valueHost;
        public Control? ValueControl { get; set; }

        public CategoryItem? SelectedCategory => CategoryBox.SelectedItem as CategoryItem;
        public FieldItem? SelectedField
        {
            get
            {
                if (SelectedCategory?.Category == ConditionFieldCategory.RecognizedAura)
                {
                    if (FieldBox.SelectedItem is FieldItem selected
                        && (string.Equals(FieldBox.Text.Trim(), selected.Display, StringComparison.Ordinal)
                            || string.Equals(FieldBox.Text.Trim(), selected.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        return selected with
                        {
                            Name = RecognizedAuraFields.ToFieldName(selected.Name),
                            Type = ConditionFieldType.Int,
                            Category = ConditionFieldCategory.RecognizedAura,
                            IsCustom = false
                        };
                    }

                    var text = FieldBox.Text.Trim();
                    var name = RecognizedAuraFields.TryGetName(text, out var parsedName)
                        ? parsedName.Trim()
                        : text;
                    if (name.Length == 0 || RecognizedAuraFields.IsBarePrefix(name))
                    {
                        return null;
                    }

                    var fieldName = RecognizedAuraFields.ToFieldName(name);
                    var isCustom = true;
                    foreach (var candidateObject in FieldBox.Items)
                    {
                        if (candidateObject is FieldItem candidate
                            && string.Equals(candidate.Name, fieldName, StringComparison.OrdinalIgnoreCase))
                        {
                            isCustom = candidate.IsCustom;
                            break;
                        }
                    }

                    return new FieldItem(
                        fieldName,
                        name,
                        ConditionFieldType.Int,
                        ConditionFieldCategory.RecognizedAura,
                        IsCustom: false);
                }

                return FieldBox.SelectedItem as FieldItem;
            }
        }
    }
}
