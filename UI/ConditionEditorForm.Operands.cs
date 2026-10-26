using System.Globalization;

namespace Shigure;

public sealed partial class ConditionEditorForm
{
    private void ShowOperandMenu(int rowIndex, int columnIndex)
    {
        CloseConditionComboDropDown();
        _conditionsGrid.EndEdit();
        if (rowIndex < 0 || rowIndex >= _conditionsGrid.Rows.Count) return;
        _conditionsGrid.CurrentCell = _conditionsGrid.Rows[rowIndex].Cells[columnIndex];
        var menu = CreateOperandMenu(rowIndex, columnIndex);
        _conditionComboDropDown = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_conditionComboDropDown, menu)) _conditionComboDropDown = null;
            // 原生菜单在 Closed 返回后还会访问句柄，退出本次消息再释放。
            if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)menu.Dispose);
            else menu.Dispose();
        };
        var anchor = _conditionsGrid.GetCellDisplayRectangle(columnIndex, rowIndex, cutOverflow: true);
        menu.Show(_conditionsGrid, new Point(anchor.Left, anchor.Bottom));
    }

    private ContextMenuStrip CreateOperandMenu(int rowIndex, int columnIndex)
    {
        var menu = UiCascadingMenu.Create(_conditionsGrid);
        var row = _conditionsGrid.Rows[rowIndex];
        var right = _conditionsGrid.Columns[columnIndex].Name == ValueColumn;
        var inOperator = ConditionExpression.IsInOperator(row.Cells[OperatorColumn].Value?.ToString());
        var selectedField = SelectedField(row);
        menu.SuspendLayout();
        try
        {
            menu.Items.Add(UiCascadingMenu.Header(_conditionsGrid, "字段"));
            foreach (var category in CategoryItems)
            {
                var fields = _fields.Where(field => field.Category == category.Category
                    && (_allowRuleSettings || !ShigureConditionFields.IsRuleSetting(field.Name))
                    && (!right || !ShigureConditionFields.IsRuleSetting(field.Name))).ToArray();
                var classified = category.Category is ConditionFieldCategory.State or ConditionFieldCategory.Aura
                    or ConditionFieldCategory.Spell or ConditionFieldCategory.Timeline;
                var root = UiCascadingMenu.LazyItem(_conditionsGrid, category.Display, children =>
                {
                    if (classified)
                    {
                        foreach (var classification in ClassificationOptions(category.Category))
                        {
                            var groupFields = fields.Where(field => NormalizeClassification(field.Classification) == classification).ToArray();
                            var group = UiCascadingMenu.LazyItem(_conditionsGrid,
                                GetClassificationDisplayName(category.Category, classification),
                                entries => AddOperandFields(entries, groupFields, row, columnIndex));
                            group.Enabled = groupFields.Length > 0;
                            children.Add(group);
                        }
                    }
                    else AddOperandFields(children, fields, row, columnIndex);
                });
                root.Enabled = (classified || fields.Length > 0) && (!right || !inOperator
                    && !IsRuleSettingField(selectedField) && selectedField?.Name != ShigureConditionFields.SoundRecognition);
                menu.Items.Add(root);
            }
            menu.Items.Add(UiCascadingMenu.Header(_conditionsGrid, "变量"));
            menu.Items.Add(UiCascadingMenu.Item(_conditionsGrid, "常数", () => SelectConstant(row, columnIndex)));
            if (right) AddLiteralChoices(menu.Items, row, columnIndex);
        }
        finally { menu.ResumeLayout(performLayout: true); }
        return menu;
    }

    private void AddOperandFields(ToolStripItemCollection items, IEnumerable<ConditionField> fields,
        DataGridViewRow row, int columnIndex)
    {
        var current = _conditionsGrid.Columns[columnIndex].Name == FieldColumn ? SelectedField(row)?.Name
            : ConditionOperand.TryReference(ReadRowValue(row), out var reference) ? reference : ReadRowValue(row);
        var entries = new List<ToolStripItem>();
        foreach (var field in fields.OrderBy(field => field.Category == ConditionFieldCategory.Shigure ? field.Name switch
        {
            ShigureConditionFields.Delay => 0, ShigureConditionFields.LogicDelay => 1,
            ShigureConditionFields.HitDelay => 2, ShigureConditionFields.MatchDelay => 3,
            ShigureConditionFields.ContinueLogic => 4, _ => 5
        } : 0))
        {
            var value = new FieldItem(field.Name, field.DisplayName, field.Type, field.Category,
                field.Classification, IsCustom: false, field.ItemId);
            var item = UiCascadingMenu.Item(_conditionsGrid, field.DisplayName,
                () => SelectOperandField(row, columnIndex, field), ResolveFieldIcon(value));
            item.Checked = string.Equals(current, field.Name, StringComparison.Ordinal);
            entries.Add(item);
        }
        items.AddRange(entries.ToArray());
    }

    private IReadOnlyList<string> ClassificationOptions(ConditionFieldCategory category)
    {
        var options = category switch
        {
            ConditionFieldCategory.State => ClassStateCatalog.TopCategories.ToList(),
            ConditionFieldCategory.Timeline => EncounterTimelineStateLayout.Classifications.ToList(),
            ConditionFieldCategory.Spell => new List<string>
                { CooldownConditionClassifications.Spell, CooldownConditionClassifications.Item },
            _ => new List<string>()
        };
        foreach (var field in _fields.Where(field => field.Category == category))
        {
            var name = NormalizeClassification(field.Classification);
            if (!options.Contains(name, StringComparer.Ordinal)) options.Add(name);
        }
        if (options.Count == 0) options.Add(Unclassified);
        return options;
    }

    private void SelectOperandField(DataGridViewRow row, int columnIndex, ConditionField field)
    {
        var previousUpdating = _updatingGrid;
        _updatingGrid = true;
        try
        {
            if (_conditionsGrid.Columns[columnIndex].Name == FieldColumn)
            {
                var value = ReadRowValue(row);
                var op = row.Cells[OperatorColumn].Value?.ToString();
                if (ShigureConditionFields.IsRuleSetting(field.Name))
                    value = field.Type == ConditionFieldType.Bool ? "true" : "0";
                row.Cells[TypeColumn].Value = CategoryItems.First(item => item.Category == field.Category).Display;
                ConfigureType(row, field.Name, field.Classification, op, value, preserveRaw: true);
            }
            else
            {
                row.Cells[columnIndex] = new FieldReferenceValueCell
                {
                    Value = ConditionOperand.Reference(field.Name),
                    ToolTipText = $"{CategoryItems.First(item => item.Category == field.Category).Display} / {field.DisplayName}"
                };
                row.Cells[columnIndex].ReadOnly = true;
            }
        }
        finally { _updatingGrid = previousUpdating; }
        _conditionsGrid.CurrentCell = row.Cells[columnIndex];
        RefreshConnectors();
        UpdatePreview();
        _conditionsGrid.InvalidateRow(row.Index);
    }

    private void SelectConstant(DataGridViewRow row, int columnIndex)
    {
        var current = _conditionsGrid.Columns[columnIndex].Name == FieldColumn
            ? SelectedField(row)?.Name : ReadRowValue(row);
        var value = ConditionOperand.TryNumber(current, out _) ? current : "0";
        var previousUpdating = _updatingGrid;
        _updatingGrid = true;
        try
        {
            row.Cells[columnIndex] = new ConstantOperandCell { Value = value };
            if (_conditionsGrid.Columns[columnIndex].Name == FieldColumn)
            {
                row.Cells[TypeColumn].Value = "自定义";
                PopulateOps(row, row.Cells[OperatorColumn].Value?.ToString());
            }
        }
        finally { _updatingGrid = previousUpdating; }
        UpdatePreview();
        var cell = row.Cells[columnIndex];
        BeginInvoke(() => { if (!IsDisposed && cell.DataGridView is not null) BeginEditValueCell(cell); });
    }

    private void AddLiteralChoices(ToolStripItemCollection items, DataGridViewRow row, int columnIndex)
    {
        // 布尔、技能、物品、声音、副本与数组沿用原来的值及 ID，不改变已有条件。
        var field = SelectedField(row);
        Func<IEnumerable<UiDropDownOption>>? choices = null;
        var title = "固定值";
        if (SpellIdConditionFields.Contains(field?.Name))
        {
            title = "法术";
            if (_spells.Count > 0)
                choices = () => _spells.Select(spell => new UiDropDownOption(spell.SpellId.ToString(CultureInfo.InvariantCulture),
                    spell.DisplayName, Catalog.Get(spell.SpellId)));
        }
        else if (ItemIdConditionFields.Contains(field?.Name))
        {
            title = "物品";
            if (_items.Count > 0)
                choices = () => _items.Select(item => new UiDropDownOption(item.ItemId.ToString(CultureInfo.InvariantCulture),
                    item.DisplayName, Catalog.GetItem(item.ItemId)));
        }
        else if (field?.Name == ShigureConditionFields.SoundRecognition)
        {
            title = "音效";
            choices = () => SoundCatalog.GetNames().Select(name => new UiDropDownOption(name, name))
                .Append(new UiDropDownOption(SoundCatalog.ImportOption, SoundCatalog.ImportOption));
        }
        else if (field is { IsCustom: false, Type: ConditionFieldType.Bool })
        {
            choices = () => [new UiDropDownOption("true", "是 (true)"), new UiDropDownOption("false", "否 (false)")];
        }
        if (choices is not null)
        {
            var group = UiCascadingMenu.LazyItem(_conditionsGrid, title, children =>
            {
                var entries = choices().Select(choice => UiCascadingMenu.Item(_conditionsGrid, choice.Display, () =>
                {
                    var stored = choice.Value?.ToString() ?? "";
                    if (stored == SoundCatalog.ImportOption) { ImportOperandSound(row); return; }
                    ConfigureValueCell(row, stored, preserveRaw: true);
                    UpdatePreview();
                }, choice.Icon)).ToArray();
                children.AddRange(entries);
            });
            items.Add(group);
        }
        if (ConditionExpression.IsInOperator(row.Cells[OperatorColumn].Value?.ToString()))
        {
            var arrays = UiCascadingMenu.LazyItem(_conditionsGrid, "数组", children =>
                children.AddRange(_numberArrayNames.Select(name => UiCascadingMenu.Item(_conditionsGrid, name, () =>
                {
                    row.Cells[ValueColumn] = new ReferenceValueCell { Value = name };
                    UpdatePreview();
                })).ToArray()));
            arrays.Enabled = _numberArrayNames.Count > 0;
            items.Add(arrays);
            items.Add(UiCascadingMenu.Item(_conditionsGrid, "手动输入列表", () =>
            {
                row.Cells[ValueColumn] = new ReferenceValueCell { Value = ReadRowValue(row) };
                BeginInvoke(() => BeginEditValueCell(row.Cells[ValueColumn]));
            }));
        }
        if (field?.Name == "首领战")
            AddBossNumberChoices(items, row, columnIndex);
        else if (field?.Name is "地下城" or "团队副本")
            items.Add(UiCascadingMenu.Item(_conditionsGrid, "选择编号…", () =>
            {
                ConfigureValueCell(row, "0", preserveRaw: true);
                BeginInvoke(() =>
                {
                    ShowInstanceNumberDropDown(row.Index, columnIndex);
                });
            }));
        if (field?.Type == ConditionFieldType.String && field.Name != ShigureConditionFields.SoundRecognition)
            items.Add(UiCascadingMenu.Item(_conditionsGrid, "文本常量", () =>
            {
                row.Cells[ValueColumn] = new ReferenceValueCell { Value = "" };
                BeginInvoke(() => BeginEditValueCell(row.Cells[ValueColumn]));
            }));
    }

    private void AddBossNumberChoices(ToolStripItemCollection items, DataGridViewRow row, int columnIndex)
    {
        var root = UiCascadingMenu.LazyItem(_conditionsGrid, "选择编号…", children =>
        {
            children.Add(Choice("非首领战（0）", 0));
            var catalog = BossCatalog.Load(_dataVersion);
            if (catalog.Error is { } error)
            {
                var unavailable = UiCascadingMenu.Item(_conditionsGrid, error);
                unavailable.Enabled = false;
                children.Add(unavailable);
                return;
            }

            var instanceId = BossCatalog.ResolveInstance(_dataVersion, CollectTerms());
            var expansions = catalog.Entries.Where(entry => instanceId is null || entry.InstanceId == instanceId)
                .GroupBy(entry => entry.ExpansionName ?? "未分类")
                .OrderBy(group => group.Min(entry => entry.Expansion ?? int.MaxValue))
                .ThenBy(group => group.Key, StringComparer.Ordinal);
            foreach (var expansion in expansions)
                children.Add(UiCascadingMenu.LazyItem(_conditionsGrid, expansion.Key, instancesMenu =>
                {
                    var instances = expansion.GroupBy(entry => entry.InstanceId).ToArray();
                    foreach (var instance in instances)
                    {
                        var first = instance.First();
                        var name = $"{first.InstanceName}（{first.InstanceType}）";
                        if (instances.Count(group => group.First().InstanceName == first.InstanceName
                            && group.First().InstanceType == first.InstanceType) > 1)
                            name += $" · {first.InstanceId}";
                        instancesMenu.Add(UiCascadingMenu.LazyItem(_conditionsGrid, name, bossesMenu =>
                            bossesMenu.AddRange(instance.OrderBy(entry => entry.Id)
                                .SelectMany(boss => boss.GetEncounterIds().Select(id => Choice($"{boss.Name}（{id}）", id)))
                                .ToArray())));
                    }
                }));
        });
        items.Add(root);

        ToolStripMenuItem Choice(string title, int id)
        {
            var value = id.ToString(CultureInfo.InvariantCulture);
            var choice = UiCascadingMenu.Item(_conditionsGrid, title, () =>
            {
                ConfigureValueCell(row, value, preserveRaw: true);
                _conditionsGrid.CurrentCell = row.Cells[columnIndex];
                _conditionsGrid.InvalidateCell(row.Cells[columnIndex]);
                UpdatePreview();
            });
            choice.Checked = string.Equals(ReadRowValue(row).Trim(), value, StringComparison.Ordinal);
            return choice;
        }
    }

    private void ImportOperandSound(DataGridViewRow row)
    {
        using var picker = new OpenFileDialog
        {
            Title = "导入声音识别音效", Filter = "音效文件 (*.wav;*.ogg)|*.wav;*.ogg", CheckFileExists = true
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var name = SoundCatalog.Import(picker.FileName);
            ConfigureValueCell(row, name, preserveRaw: true);
            UpdatePreview();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "导入音效", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private bool TryValidateConstants()
    {
        foreach (DataGridViewRow row in _conditionsGrid.Rows)
            foreach (var column in new[] { FieldColumn, ValueColumn })
                if (row.Cells[column] is ConstantOperandCell cell && !ConditionOperand.TryNumber(cell.Value?.ToString(), out _))
                {
                    MessageBox.Show(this, $"第 {row.Index + 1} 行的常数必须为有效的整数或小数。",
                        "编辑条件", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _conditionsGrid.CurrentCell = cell;
                    _conditionsGrid.BeginEdit(selectAll: true);
                    return false;
                }
        return true;
    }

    private sealed class ConstantOperandCell : DataGridViewTextBoxCell;
    private sealed class FieldReferenceValueCell : DataGridViewTextBoxCell;
}
