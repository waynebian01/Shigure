namespace Shigure;

public sealed partial class ModuleEditorControl
{
    private readonly DataGridView _customConditionsGrid = new UiThemedDataGridView { Name = "CustomConditions" };
    private readonly DataGridView _postAdjustmentsGrid = new UiThemedDataGridView { Name = "PostAdjustments" };

    private static (string Text, ConditionFieldCategory Category)[] PostAdjustmentTypeOptions =>
    [
        .. AdjustmentTypeOptions,
        ("动态单位", ConditionFieldCategory.DynamicUnit),
        ("动态数值", ConditionFieldCategory.DynamicValue),
        ("自定义", ConditionFieldCategory.Custom)
    ];

    private IEnumerable<DataGridViewRow> AdjustmentRows()
        => _adjustmentsGrid.Rows.Cast<DataGridViewRow>().Concat(_postAdjustmentsGrid.Rows.Cast<DataGridViewRow>());

    private static bool IsPostAdjustmentGrid(DataGridView grid) => grid.Name == "PostAdjustments";
    private static bool IsPostAdjustmentRow(DataGridViewRow row) => row.DataGridView is { } grid && IsPostAdjustmentGrid(grid);
    private static ModuleComputationStage GetAdjustmentStage(DataGridView grid)
        => IsPostAdjustmentGrid(grid) ? ModuleComputationStage.PostAdjustment : ModuleComputationStage.Adjustment;
    private static string AdjustmentPageLabel(DataGridViewRow row)
        => IsCustomAdjustmentRow(row) ? "自定义" : IsPostAdjustmentRow(row) ? "最终调整" : "初次调整";

    private bool IsCustomBooleanTarget(DataGridViewRow row)
        => IsPostAdjustmentRow(row) && !IsTeammateAdjustmentRow(row) && GetEditorStages().IsCustomField(CellText(row, "Field"));

    private void NormalizePostBooleanTarget(DataGridViewRow row)
    {
        if (!IsCustomBooleanTarget(row)) return;
        var suppress = _suppressAdjustmentTypeChange;
        _suppressAdjustmentTypeChange = true;
        try
        {
            row.Cells["Operation"].Value = "=";
            var value = CellText(row, "Delta");
            if (value != ModuleValueAdjustment.ConditionBooleanValue && !bool.TryParse(value, out _))
                row.Cells["Delta"].Value = ModuleValueAdjustment.ConditionBooleanValue;
        }
        finally { _suppressAdjustmentTypeChange = suppress; }
    }

    private Control BuildCustomConditionsGrid()
    {
        var grid = _customConditionsGrid;
        UiTheme.StyleDataGridView(grid);
        grid.AllowUserToAddRows = true;
        grid.AllowUserToDeleteRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "启用", Width = 68 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Field", HeaderText = "名称", Width = 240 });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Delta", HeaderText = "赋值", Width = 140, ReadOnly = true,
            DefaultCellStyle = new DataGridViewCellStyle { NullValue = ModuleValueAdjustment.ConditionBooleanValue }
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Condition", HeaderText = "条件 (点击编辑)", ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        AddDeleteColumn(grid);
        grid.DefaultValuesNeeded += (_, e) =>
        {
            e.Row.Cells["Enabled"].Value = true;
            e.Row.Cells["Delta"].Value = ModuleValueAdjustment.ConditionBooleanValue;
        };
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewCheckBoxCell)
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.CellValueChanged += (_, _) =>
        {
            InvalidateConditionFieldValidation();
            RefreshAdjustmentFieldColumn();
        };
        grid.CellEndEdit += (_, _) => RefreshAdjustmentFieldColumn();
        grid.DataError += (_, e) => e.ThrowException = false;
        grid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var row = grid.Rows[e.RowIndex];
            var column = grid.Columns[e.ColumnIndex].Name;
            if (column == "Delete" && !row.IsNewRow)
            {
                grid.Rows.RemoveAt(e.RowIndex);
                RefreshAdjustmentFieldColumn();
            }
            else if (column == "Condition") OpenCustomConditionEditor(row);
        };
        UiTheme.CacheDataGridViewColumnWidths(grid, "module-custom-conditions");
        return grid;
    }

    private void OpenCustomConditionEditor(DataGridViewRow row)
    {
        _customConditionsGrid.EndEdit();
        using var editor = new ConditionEditorForm(
            GetStageConditionFields(ModuleComputationStage.Custom), row.IsNewRow ? "" : CellText(row, "Condition"),
            conditionFieldsProvider: () => GetStageConditionFields(ModuleComputationStage.Custom),
            spells: RefreshAndBuildConditionSpells(), conditionSpellsProvider: RefreshAndBuildConditionSpells,
            items: RefreshAndBuildConditionItems(), conditionItemsProvider: RefreshAndBuildConditionItems,
            numberArrayNames: _numberArrays.Select(array => array.Name).ToArray(), dataVersion: _resolveProfile().Version);
        if (editor.ShowDialog(FindForm()) != DialogResult.OK) return;
        if (row.IsNewRow)
        {
            if (string.IsNullOrWhiteSpace(editor.ConditionText)) return;
            _customConditionsGrid.Rows.Add(true, "", ModuleValueAdjustment.ConditionBooleanValue, editor.ConditionText);
        }
        else row.Cells["Condition"].Value = editor.ConditionText;
        RefreshAdjustmentFieldColumn();
    }

    private ModuleValueAdjustment ReadComputationRow(DataGridViewRow row)
    {
        if (row.DataGridView == _formulaAdjustmentsGrid)
        {
            TryGetAdjustmentField(CellText(row, "Field"), CellText(row, "Formula"), out var field);
            return new() { Field = field, Formula = FormulaEvaluator.NormalizeExpression(CellText(row, "Formula")) };
        }
        return new()
        {
            Enabled = CellBool(row, "Enabled", defaultValue: true),
            Field = CellText(row, "Field"), Condition = CellText(row, "Condition"),
            Value = IsCustomAdjustmentRow(row) ? ModuleValueAdjustment.ConditionBooleanValue : CellText(row, "Delta"),
            IsCustom = IsCustomAdjustmentRow(row),
            IsPostAdjustment = IsPostAdjustmentRow(row),
            Operation = ReadAdjustmentOperation(row),
            TeammateTarget = IsTeammateAdjustmentRow(row)
                ? ModuleTeammateTarget.ParseSelectionValue(CellText(row, "Target")) : null
        };
    }

    private ModuleDefinition GetEditorComputationModule() => new()
    {
        Units = _units, Counts = _counts, EnemyCounts = _enemyCounts, AverageHealthFields = _averageHealthFields,
        ValueAdjustments = new[] { _adjustmentsGrid, _postAdjustmentsGrid, _formulaAdjustmentsGrid, _customConditionsGrid }
            .SelectMany(grid => grid.Rows.Cast<DataGridViewRow>())
            .Where(row => !row.IsNewRow).Select(ReadComputationRow).ToList()
    };

    private ModuleComputationStages GetEditorStages() => new(GetEditorComputationModule());

    private IReadOnlyList<ConditionField> GetStageConditionFields(ModuleComputationStage stage)
    {
        var stages = GetEditorStages();
        return RefreshAndBuildConditionFields().Where(field => stages.IsAvailable(field.Name, stage)).ToArray();
    }

    private static IEnumerable<(string Name, List<ModuleCountConditionGroup> Groups)> GetDynamicFilters(ModuleDefinition module)
        => module.Units.Select(value => (value.Name, value.FilterGroups))
            .Concat(module.Counts.Select(value => (value.Name, value.FilterGroups)))
            .Concat(module.EnemyCounts.Select(value => (value.Name, value.FilterGroups)))
            .Concat(module.AverageHealthFields.Select(value => (value.Name, value.FilterGroups)));
}
