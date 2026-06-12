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
            if (string.IsNullOrWhiteSpace(term.Field) || string.IsNullOrWhiteSpace(term.Value))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(term.OrWithPrevious ? " || " : " && ");
            }

            builder.Append(term.Field).Append(' ').Append(term.Op).Append(' ').Append(term.Value);
        }

        return builder.ToString();
    }

    private static ConditionTerm ParseTerm(string term, bool orWithPrevious)
    {
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
}

/// <summary>
/// 条件可视化编辑弹窗: 每行一个比较项(连接/字段/判断/值/删除),
/// 字段下拉来自 config.json 按职业/专精过滤后的目录, 值控件按字段类型自适应。
/// </summary>
public sealed class ConditionEditorForm : Form
{
    private const int ConnectorWidth = 70;
    private const int FieldWidth = 230;
    private const int OpWidth = 74;
    private const int ValueWidth = 150;
    private const int DeleteWidth = 36;
    private const int RowTotalWidth = ConnectorWidth + FieldWidth + OpWidth + ValueWidth + DeleteWidth;

    private static readonly string[] AllOperators = ["==", "!=", ">", ">=", "<", "<="];
    private static readonly string[] EqualityOperators = ["==", "!="];

    private readonly IReadOnlyList<ConditionField> _fields;
    private readonly FlowLayoutPanel _rowsPanel = new();
    private readonly Label _previewLabel = new();
    private readonly List<ConditionRow> _rows = new();

    public string ConditionText { get; private set; } = string.Empty;

    public ConditionEditorForm(IReadOnlyList<ConditionField> fields, string? condition)
    {
        _fields = fields;
        InitializeComponent();

        foreach (var term in ConditionExpression.Parse(condition))
        {
            AddRow(term);
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
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.Text;
        ClientSize = new Size(RowTotalWidth + 50, 430);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding = new Padding(12, 10, 12, 10),
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        Controls.Add(root);

        root.Controls.Add(BuildHeaderRow(), 0, 0);

        _rowsPanel.Dock = DockStyle.Fill;
        _rowsPanel.BackColor = UiTheme.Surface;
        _rowsPanel.FlowDirection = FlowDirection.TopDown;
        _rowsPanel.WrapContents = false;
        _rowsPanel.AutoScroll = true;
        _rowsPanel.Margin = new Padding(0);
        root.Controls.Add(_rowsPanel, 0, 1);

        _previewLabel.Dock = DockStyle.Fill;
        _previewLabel.ForeColor = UiTheme.Muted;
        _previewLabel.TextAlign = ContentAlignment.MiddleLeft;
        _previewLabel.AutoEllipsis = true;
        _previewLabel.Margin = new Padding(0);
        root.Controls.Add(_previewLabel, 0, 2);

        root.Controls.Add(BuildActionRow(), 0, 3);
    }

    private Control BuildHeaderRow()
    {
        var header = new TableLayoutPanel
        {
            Width = RowTotalWidth,
            Height = 24,
            BackColor = UiTheme.Surface,
            Margin = new Padding(0),
            ColumnCount = 5,
            RowCount = 1
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ConnectorWidth));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, FieldWidth));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, OpWidth));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ValueWidth));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DeleteWidth));

        header.Controls.Add(CreateHeaderLabel("连接"), 0, 0);
        header.Controls.Add(CreateHeaderLabel("字段"), 1, 0);
        header.Controls.Add(CreateHeaderLabel("判断"), 2, 0);
        header.Controls.Add(CreateHeaderLabel("值"), 3, 0);
        header.Controls.Add(CreateHeaderLabel(string.Empty), 4, 0);
        return header;
    }

    private Control BuildActionRow()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Margin = new Padding(0),
            ColumnCount = 2,
            RowCount = 1
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));

        var leftButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = UiTheme.Surface,
            Margin = new Padding(0)
        };
        var addButton = UiTheme.CreateButton("添加条件", UiTheme.Field, UiTheme.Text);
        addButton.Width = 96;
        addButton.Height = 30;
        addButton.Margin = new Padding(0, 4, 0, 0);
        addButton.Click += (_, _) =>
        {
            AddRow(null);
            RefreshConnectors();
            UpdatePreview();
        };
        leftButtons.Controls.Add(addButton);
        row.Controls.Add(leftButtons, 0, 0);

        var rightButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = UiTheme.Surface,
            Margin = new Padding(0)
        };

        var okButton = UiTheme.CreateButton("确定", UiTheme.Accent, Color.Black);
        okButton.Width = 72;
        okButton.Height = 30;
        okButton.Margin = new Padding(6, 4, 0, 0);
        okButton.Click += (_, _) =>
        {
            ConditionText = ConditionExpression.Build(CollectTerms());
            DialogResult = DialogResult.OK;
        };

        var cancelButton = UiTheme.CreateButton("取消", UiTheme.Field, UiTheme.Text);
        cancelButton.Width = 72;
        cancelButton.Height = 30;
        cancelButton.Margin = new Padding(6, 4, 0, 0);
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;

        rightButtons.Controls.Add(okButton);
        rightButtons.Controls.Add(cancelButton);
        row.Controls.Add(rightButtons, 1, 0);

        CancelButton = cancelButton;
        return row;
    }

    private void AddRow(ConditionTerm? term)
    {
        var panel = new TableLayoutPanel
        {
            Width = RowTotalWidth,
            Height = 34,
            BackColor = UiTheme.Surface,
            Margin = new Padding(0, 2, 0, 2),
            ColumnCount = 5,
            RowCount = 1
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ConnectorWidth));
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

        var fieldBox = new ComboBox();
        UiTheme.StyleComboBox(fieldBox);
        fieldBox.Dock = DockStyle.Fill;
        fieldBox.Margin = new Padding(0, 2, 8, 2);
        PopulateFields(fieldBox, term?.Field);

        var opBox = new ComboBox();
        UiTheme.StyleComboBox(opBox);
        opBox.Dock = DockStyle.Fill;
        opBox.Margin = new Padding(0, 2, 8, 2);

        var valueHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Margin = new Padding(0, 2, 8, 2)
        };

        var deleteButton = UiTheme.CreateButton("✕", UiTheme.Field, UiTheme.Danger);
        deleteButton.AutoSize = false;
        deleteButton.Width = DeleteWidth - 6;
        deleteButton.Height = 26;
        deleteButton.Margin = new Padding(0, 2, 0, 2);
        deleteButton.Padding = new Padding(0);

        panel.Controls.Add(connectorBox, 0, 0);
        panel.Controls.Add(fieldBox, 1, 0);
        panel.Controls.Add(opBox, 2, 0);
        panel.Controls.Add(valueHost, 3, 0);
        panel.Controls.Add(deleteButton, 4, 0);

        var row = new ConditionRow(panel, connectorBox, fieldBox, opBox, valueHost);
        PopulateOps(row, term?.Op);
        CreateValueControl(row, term?.Value, preserveRaw: true);

        connectorBox.SelectedIndexChanged += (_, _) => UpdatePreview();
        fieldBox.SelectedIndexChanged += (_, _) =>
        {
            OnFieldChanged(row);
            UpdatePreview();
        };
        opBox.SelectedIndexChanged += (_, _) => UpdatePreview();
        deleteButton.Click += (_, _) => RemoveRow(row);

        _rows.Add(row);
        _rowsPanel.Controls.Add(panel);
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

    private void PopulateFields(ComboBox fieldBox, string? currentField)
    {
        fieldBox.Items.Clear();
        foreach (var field in _fields)
        {
            fieldBox.Items.Add(new FieldItem(field.Name, field.DisplayName, field.Type, IsCustom: false));
        }

        if (!string.IsNullOrWhiteSpace(currentField))
        {
            var index = FindFieldIndex(fieldBox, currentField);
            if (index < 0)
            {
                // 目录里没有的字段(如 group.* 或手写字段)保留为自定义项, 避免丢失原条件。
                fieldBox.Items.Add(new FieldItem(currentField, $"{currentField} (自定义)", ConditionFieldType.Int, IsCustom: true));
                index = fieldBox.Items.Count - 1;
            }

            fieldBox.SelectedIndex = index;
            return;
        }

        if (fieldBox.Items.Count > 0)
        {
            fieldBox.SelectedIndex = 0;
        }
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
        var ops = field is { IsCustom: false, Type: ConditionFieldType.Bool or ConditionFieldType.String }
            ? EqualityOperators
            : AllOperators;

        row.OpBox.Items.Clear();
        row.OpBox.Items.AddRange(ops);
        var index = desiredOp is null ? -1 : Array.IndexOf(ops, desiredOp);
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
        else if (field is { IsCustom: false, Type: ConditionFieldType.Int }
            && (TryParseIntegerText(rawValue, out var number) || !preserveRaw))
        {
            // 解析失败时 number 为 0, 仅在 preserveRaw=false 的字段切换场景下走到这里。
            var numeric = new NumericUpDown
            {
                Minimum = -1000000,
                Maximum = 1000000,
                Value = number,
                BackColor = UiTheme.Field,
                ForeColor = UiTheme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            numeric.ValueChanged += (_, _) => UpdatePreview();
            numeric.TextChanged += (_, _) => UpdatePreview();
            control = numeric;
        }
        else
        {
            // 字符串字段、自定义字段或无法转成数字的旧值用文本框兜底。
            var box = new TextBox
            {
                Text = rawValue ?? string.Empty,
                BackColor = UiTheme.Field,
                ForeColor = UiTheme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
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
            TextBox box => box.Text.Trim(),
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
        var text = ConditionExpression.Build(CollectTerms());
        _previewLabel.Text = text.Length == 0 ? "预览: (无条件, 始终命中)" : $"预览: {text}";
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

    private sealed record FieldItem(string Name, string Display, ConditionFieldType Type, bool IsCustom)
    {
        public override string ToString() => Display;
    }

    private sealed class ConditionRow(
        TableLayoutPanel panel,
        ComboBox connector,
        ComboBox fieldBox,
        ComboBox opBox,
        Panel valueHost)
    {
        public TableLayoutPanel Panel { get; } = panel;
        public ComboBox Connector { get; } = connector;
        public ComboBox FieldBox { get; } = fieldBox;
        public ComboBox OpBox { get; } = opBox;
        public Panel ValueHost { get; } = valueHost;
        public Control? ValueControl { get; set; }

        public FieldItem? SelectedField => FieldBox.SelectedItem as FieldItem;
    }
}
