using System.Drawing;
using System.Globalization;

namespace Shigure;

/// <summary>模块数字数组编辑器；图标和名称始终从法术图标包按数字查询。</summary>
public sealed class ModuleNumberArrayEditorForm : Form
{
    private readonly TextBox _nameBox = new UiThemedTextBox();
    private readonly DataGridView _grid = new UiThemedDataGridView();
    private readonly HashSet<string> _takenNames;

    public ModuleNumberArray? Result { get; private set; }

    public ModuleNumberArrayEditorForm(ModuleNumberArray? source, IEnumerable<string> takenNames)
    {
        _takenNames = new HashSet<string>(takenNames, StringComparer.Ordinal);
        InitializeComponent();
        _nameBox.Text = source?.Name ?? string.Empty;
        foreach (var number in source?.Numbers ?? [])
        {
            var index = _grid.Rows.Add(null!, string.Empty, number.ToString(CultureInfo.InvariantCulture));
            UpdateSpellInfo(_grid.Rows[index]);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UiTheme.ApplyDarkTitleBar(this);
    }

    private void InitializeComponent()
    {
        Text = "编辑数组";
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.Text;
        ClientSize = new Size(660, 560);
        MinimumSize = new Size(520, 400);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(UiTheme.CardPadding),
            BackColor = UiTheme.Surface
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        Controls.Add(root);

        var nameRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        nameRow.Controls.Add(new Label
        {
            Text = "名称",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UiTheme.Text
        }, 0, 0);
        UiTheme.StyleTextBox(_nameBox);
        _nameBox.Dock = DockStyle.Fill;
        _nameBox.Margin = new Padding(0, 6, 0, 6);
        nameRow.Controls.Add(_nameBox, 1, 0);
        root.Controls.Add(nameRow, 0, 0);

        UiTheme.StyleDataGridView(_grid);
        _grid.AllowUserToAddRows = true;
        _grid.AllowUserToDeleteRows = true;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.Columns.Add(new DataGridViewImageColumn
        {
            Name = "Icon", HeaderText = "图标", Width = 64, ReadOnly = true,
            ImageLayout = DataGridViewImageCellLayout.Zoom
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name", HeaderText = "名称", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            ReadOnly = true
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Number", HeaderText = "数字", Width = 160
        });
        _grid.CellEndEdit += (_, e) =>
        {
            if (e.RowIndex >= 0 && !_grid.Rows[e.RowIndex].IsNewRow)
                UpdateSpellInfo(_grid.Rows[e.RowIndex]);
        };
        _grid.DataError += (_, e) => e.ThrowException = false;
        root.Controls.Add(_grid, 0, 1);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = UiTheme.Surface
        };
        var save = UiTheme.CreateButton("保存", UiTheme.ButtonKind.Primary);
        save.Width = 108;
        save.Height = 38;
        save.Click += (_, _) => SaveArray();
        var cancel = UiTheme.CreateButton("取消", UiTheme.ButtonKind.Secondary);
        cancel.Width = 108;
        cancel.Height = 38;
        cancel.DialogResult = DialogResult.Cancel;
        var remove = UiTheme.CreateButton("删除选中行", UiTheme.ButtonKind.Secondary);
        remove.Width = 120;
        remove.Height = 38;
        remove.Click += (_, _) =>
        {
            foreach (var row in _grid.SelectedCells.Cast<DataGridViewCell>()
                         .Select(cell => cell.OwningRow).OfType<DataGridViewRow>()
                         .Distinct().Where(row => !row.IsNewRow).ToArray())
                _grid.Rows.Remove(row);
        };
        footer.Controls.Add(save);
        footer.Controls.Add(cancel);
        footer.Controls.Add(remove);
        root.Controls.Add(footer, 0, 2);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private void UpdateSpellInfo(DataGridViewRow row)
    {
        var text = row.Cells["Number"].Value?.ToString()?.Trim();
        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            row.Cells["Icon"].Value = null;
            row.Cells["Name"].Value = string.Empty;
            return;
        }
        row.Cells["Icon"].Value = SpellIconCatalog.Get(number);
        row.Cells["Name"].Value = SpellIconCatalog.GetSpellName(number) ?? string.Empty;
    }

    private void SaveArray()
    {
        _grid.EndEdit();
        var name = _nameBox.Text.Trim();
        if (!ConditionExpression.IsArrayName(name))
        {
            MessageBox.Show("数组名称不能为空，也不能包含空格、括号、逗号、& 或 |，且不能是纯数字。", "Shigure",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_takenNames.Contains(name))
        {
            MessageBox.Show("数组名称已存在。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var numbers = new List<long>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            var text = row.Cells["Number"].Value?.ToString()?.Trim();
            if (string.IsNullOrEmpty(text)) continue;
            if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                MessageBox.Show($"第 {row.Index + 1} 行请输入整数。", "Shigure",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            numbers.Add(number);
        }

        Result = new ModuleNumberArray { Name = name, Numbers = numbers.Distinct().ToList() };
        DialogResult = DialogResult.OK;
        Close();
    }
}
