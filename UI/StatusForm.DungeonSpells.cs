using System.Globalization;

namespace Shigure;

public sealed partial class StatusForm
{
    private ListView? _dungeonSpellList;
    private sealed record DungeonSpellFilterOption(string Display, string? Value)
    {
        public override string ToString() => Display;
    }

    private Control CreateLazyDungeonSpellsPage()
    {
        var host = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Margin = Padding.Empty };
        var built = false;
        host.VisibleChanged += (_, _) =>
        {
            if (!host.Visible || built || host.IsDisposed) return;
            built = true;
            var page = BuildDungeonSpellsPage();
            page.Dock = DockStyle.Fill;
            host.Controls.Add(page);
        };
        return host;
    }

    private Control BuildDungeonSpellsPage()
    {
        var data = DungeonSpellCatalog.Load(AppPaths.BaseDirectory);
        var list = UiTheme.CreateListView(Font, "dungeon-spells",
            new UiTheme.ListColumn("名称", 112, 280, FixedWidth: true),
            new UiTheme.ListColumn("技能 ID", 92, 112, FixedWidth: true),
            new UiTheme.ListColumn("副本", 140, 240),
            new UiTheme.ListColumn("首领", 160, 280),
            new UiTheme.ListColumn("类型", 100, 120, FixedWidth: true),
            new UiTheme.ListColumn("图标 ID", 88, 110, FixedWidth: true),
            new UiTheme.ListColumn("NPC", 160, 350),
            new UiTheme.ListColumn("职业", 100, 240, FillRemaining: true));
        list.Name = "dungeon-spells-list";
        _dungeonSpellList = list;
        UiTheme.SetListViewSubItemIconResolver(list, (item, column) =>
            column == 0 && item.Tag is DungeonSpellInfo spell ? SpellIconCatalog.Get(spell.SpellId) : null);

        var fields = Enum.GetValues<DungeonSpellFilter>();
        string[] labels = ["副本", "首领", "类型", "NPC", "职业"];
        var filters = new Dictionary<DungeonSpellFilter, UiDropDown>();
        var selections = fields.ToDictionary(field => field, _ => (string?)null);
        var filterGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = fields.Length, RowCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        for (var column = 0; column < fields.Length; column++)
            filterGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / fields.Length));
        filterGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (var i = 0; i < fields.Length; i++)
        {
            var filter = CreateExBossEventFilter(250);
            filter.Name = $"dungeon-spell-filter-{fields[i]}";
            filter.AccessibleName = labels[i];
            filter.Dock = DockStyle.Fill;
            filter.Margin = new Padding(0, 4, i == fields.Length - 1 ? 0 : 10, 4);
            filter.Enabled = data.Error is null;
            filters.Add(fields[i], filter);
            filterGrid.Controls.Add(filter, i, 0);
        }

        var spellIdInput = CreateIdInput("dungeon-spell-id-filter", "spellId", "输入技能 ID");
        var iconIdInput = CreateIdInput("dungeon-icon-id-filter", "iconId", "输入图标 ID");
        var idFilterRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 4, RowCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        idFilterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiTheme.Scale(this, 64)));
        idFilterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        idFilterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiTheme.Scale(this, 64)));
        idFilterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        idFilterRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddIdInput("spellId", spellIdInput, 0);
        AddIdInput("iconId", iconIdInput, 2);

        TextBox CreateIdInput(string name, string label, string placeholder)
        {
            var input = new TextBox
            {
                Name = name, AccessibleName = label, PlaceholderText = placeholder,
                Anchor = AnchorStyles.Left | AnchorStyles.Right, Enabled = data.Error is null
            };
            UiTheme.StyleTextBox(input);
            return input;
        }

        void AddIdInput(string label, TextBox input, int column)
        {
            idFilterRow.Controls.Add(new Label
            {
                Text = label, Dock = DockStyle.Fill, ForeColor = UiTheme.Muted,
                BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty
            }, column, 0);
            input.Margin = new Padding(0, 4, column == 0 ? 10 : 0, 4);
            idFilterRow.Controls.Add(input, column + 1, 0);
        }

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 1, RowCount = 3,
            Margin = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, UiTheme.Scale(this, 44)));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, UiTheme.Scale(this, 44)));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(filterGrid, 0, 0);
        content.Controls.Add(idFilterRow, 0, 1);
        list.Dock = DockStyle.Fill;
        list.Margin = Padding.Empty;
        content.Controls.Add(list, 0, 2);
        var page = BuildFixedWidthSectionPage("副本技能", content,
            data.Error ?? "五项筛选联动；输入 spellId 或 iconId 精确筛选，清空输入恢复结果", list, UiTheme.EventPageWidth, scaleHeader: true);

        var updatingFilters = false;
        string DescribeValue(DungeonSpellFilter field, string value)
        {
            if (value.Length == 0) return "未指定";
            return field switch
            {
                DungeonSpellFilter.Map => DungeonSpellCatalog.DescribeNumber(int.Parse(value), data.MapNames),
                DungeonSpellFilter.Boss => DungeonSpellCatalog.DescribeNumber(int.Parse(value), data.BossNames),
                DungeonSpellFilter.Type => DungeonSpellCatalog.DescribeType(value),
                DungeonSpellFilter.Class => DungeonSpellCatalog.DescribeClass(value),
                _ => value
            };
        }

        void ApplyFilters()
        {
            if (updatingFilters) return;
            var spellIdValid = ReadIdFilter(spellIdInput, out var selectedSpellId);
            var iconIdValid = ReadIdFilter(iconIdInput, out var selectedIconId);
            bool MatchesIds(DungeonSpellInfo spell) => spellIdValid && iconIdValid
                && (selectedSpellId is null || spell.SpellId == selectedSpellId)
                && (selectedIconId is null || spell.IconId == selectedIconId);
            updatingFilters = true;
            try
            {
                for (var i = 0; i < fields.Length; i++)
                {
                    var field = fields[i];
                    // 当前下拉框不参与自己的候选值计算，允许切换或恢复“全部”。
                    var candidates = data.Spells.Where(spell => MatchesIds(spell)
                            && DungeonSpellCatalog.Matches(spell, selections, field))
                        .SelectMany(spell => DungeonSpellCatalog.Values(spell, field))
                        .Distinct(StringComparer.Ordinal).ToList();
                    // 输入中的 ID 可能尚未完成或与已选条件冲突；保留已选条件，避免意外放宽筛选。
                    if (selections[field] is { } current && !candidates.Contains(current, StringComparer.Ordinal))
                        candidates.Add(current);
                    var values = candidates
                        .OrderBy(value => value.Length == 0)
                        .ThenBy(value => long.TryParse(value, out var id) ? id : 0)
                        .ThenBy(value => DescribeValue(field, value), StringComparer.Ordinal).ToArray();
                    var filter = filters[field];
                    filter.BeginUpdate();
                    try
                    {
                        filter.Items.Clear();
                        filter.Items.Add(new DungeonSpellFilterOption($"{labels[i]}：全部", null));
                        foreach (var value in values)
                            filter.Items.Add(new DungeonSpellFilterOption($"{labels[i]}：{DescribeValue(field, value)}", value));
                        var selected = filter.Items.Cast<DungeonSpellFilterOption>().ToList()
                            .FindIndex(option => option.Value == selections[field]);
                        filter.SelectedIndex = selected < 0 ? 0 : selected;
                        selections[field] = (filter.SelectedItem as DungeonSpellFilterOption)?.Value;
                        _toolTip.SetToolTip(filter, filter.SelectedItem?.ToString());
                    }
                    finally { filter.EndUpdate(); }
                }
                ReplaceItems(list, data.Spells.Where(spell => MatchesIds(spell)
                        && DungeonSpellCatalog.Matches(spell, selections))
                    .Select(spell => new ListViewItem(
                    [
                        spell.Name, spell.SpellId.ToString(),
                        DungeonSpellCatalog.DescribeNumber(spell.MapId, data.MapNames),
                        DungeonSpellCatalog.DescribeNumber(spell.Boss, data.BossNames),
                        DungeonSpellCatalog.DescribeType(spell.Type), spell.IconId.ToString(),
                        spell.Npcs.Count == 0 ? "未指定" : string.Join("、", spell.Npcs),
                        spell.Classes.Count == 0 ? "未指定" : string.Join("、", spell.Classes.Select(DungeonSpellCatalog.DescribeClass))
                    ])
                    {
                        Tag = spell,
                        ToolTipText = $"{spell.Name} · spellId {spell.SpellId} · iconId {spell.IconId} · iconName {spell.IconName}"
                            + $"\n副本：{DungeonSpellCatalog.DescribeNumber(spell.MapId, data.MapNames)}"
                            + $" · 首领：{DungeonSpellCatalog.DescribeNumber(spell.Boss, data.BossNames)}"
                            + $"\nNPC：{(spell.Npcs.Count == 0 ? "未指定" : string.Join("、", spell.Npcs))}"
                            + $" · 职业：{(spell.Classes.Count == 0 ? "未指定" : string.Join("、", spell.Classes.Select(DungeonSpellCatalog.DescribeClass)))}"
                    }).ToArray());
            }
            finally { updatingFilters = false; }
        }

        foreach (var (field, filter) in filters)
            filter.SelectedIndexChanged += (_, _) =>
            {
                if (updatingFilters) return;
                selections[field] = (filter.SelectedItem as DungeonSpellFilterOption)?.Value;
                ApplyFilters();
            };
        spellIdInput.TextChanged += (_, _) => ApplyFilters();
        iconIdInput.TextChanged += (_, _) => ApplyFilters();
        ApplyFilters();
        return page;

        bool ReadIdFilter(TextBox input, out long? id)
        {
            id = null;
            var text = input.Text.Trim();
            var valid = text.Length == 0 || (long.TryParse(text, NumberStyles.None,
                CultureInfo.InvariantCulture, out var value) && value > 0);
            if (valid && text.Length > 0)
                id = long.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
            input.ForeColor = valid ? UiTheme.Text : UiTheme.Danger;
            _toolTip.SetToolTip(input, valid ? "按 ID 精确匹配；清空则取消该条件。" : "请输入正整数。");
            return valid;
        }
    }
}
