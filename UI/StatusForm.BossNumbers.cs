using System.Globalization;

namespace Shigure;

public sealed partial class StatusForm
{
    private Control BuildBossNumbersPage()
    {
        var version = _resolveProfile().Version;
        var catalog = BossCatalog.Load(version);
        var list = UiTheme.CreateListView(Font, "boss-numbers-instance-v2",
            new UiTheme.ListColumn("类型", 80, 100),
            new UiTheme.ListColumn("副本编号", 80, 96, FixedWidth: true),
            new UiTheme.ListColumn("副本", 150, 300),
            new UiTheme.ListColumn("首领编号", 80, 96, FixedWidth: true),
            new UiTheme.ListColumn("首领", 180, 1000, FillRemaining: true),
            new UiTheme.ListColumn("游戏首领战 ID", 100, 130));
        list.Name = "boss-numbers-list";
        list.Disposed += (_, _) => _listCounts.Remove(list);

        var typeFilter = CreateFilter("boss-filter-type", "类型");
        var expansionFilter = CreateFilter("boss-filter-expansion", "版本");
        var instanceFilter = CreateFilter("boss-filter-instance", "副本");
        var filterRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 3, RowCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        filterRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        filterRow.Controls.Add(typeFilter, 0, 0);
        filterRow.Controls.Add(expansionFilter, 1, 0);
        filterRow.Controls.Add(instanceFilter, 2, 0);
        instanceFilter.Margin = new Padding(0, 4, 0, 4);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 1, RowCount = 2,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, UiTheme.Scale(this, 44)));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(filterRow, 0, 0);
        list.Dock = DockStyle.Fill;
        list.Margin = Padding.Empty;
        content.Controls.Add(list, 0, 1);
        var page = BuildFixedWidthSectionPage("首领", content,
            catalog.Error ?? $"{version} · 每个副本从 1 开始编号；结合地下城／团队副本条件使用，0 表示未匹配首领战。",
            list, UiTheme.ReferencePageWidth, scaleHeader: true);

        var updatingFilters = false;
        static string? Selected(UiDropDown filter) => (filter.SelectedItem as ReferenceFilterOption)?.Value;
        static string ExpansionName(BossCatalogEntry entry) => entry.ExpansionName ?? "未分类";

        UiDropDown CreateFilter(string name, string label)
        {
            var filter = CreateReferenceFilter(250);
            filter.Name = name;
            filter.AccessibleName = label;
            filter.Dock = DockStyle.Fill;
            filter.Margin = new Padding(0, 4, 10, 4);
            filter.Enabled = catalog.Error is null;
            return filter;
        }

        void SetOptions(UiDropDown filter, string label, IEnumerable<ReferenceFilterOption> options)
        {
            var selected = Selected(filter);
            filter.BeginUpdate();
            try
            {
                filter.Items.Clear();
                filter.Items.Add(new ReferenceFilterOption($"{label}：全部", null));
                foreach (var option in options) filter.Items.Add(option);
                var index = filter.Items.Cast<ReferenceFilterOption>().ToList().FindIndex(option => option.Value == selected);
                filter.SelectedIndex = index < 0 ? 0 : index;
                _toolTip.SetToolTip(filter, filter.SelectedItem?.ToString());
            }
            finally { filter.EndUpdate(); }
        }

        IEnumerable<BossCatalogEntry> TypeEntries()
            => catalog.Entries.Where(entry => Selected(typeFilter) is not { } type || entry.InstanceType == type);

        IEnumerable<BossCatalogEntry> ExpansionEntries()
            => TypeEntries().Where(entry => Selected(expansionFilter) is not { } expansion || ExpansionName(entry) == expansion);

        void RefreshFilters(bool refreshExpansions)
        {
            updatingFilters = true;
            try
            {
                if (refreshExpansions)
                    SetOptions(expansionFilter, "版本", TypeEntries().GroupBy(ExpansionName)
                        .OrderBy(group => group.Min(entry => entry.Expansion ?? int.MaxValue))
                        .ThenBy(group => group.Key, StringComparer.Ordinal)
                        .Select(group => new ReferenceFilterOption($"版本：{group.Key}", group.Key)));
                SetOptions(instanceFilter, "副本", ExpansionEntries().DistinctBy(entry => entry.InstanceId)
                    .Select(entry => new ReferenceFilterOption($"副本：{entry.InstanceName}（{ExpansionName(entry)}）",
                        entry.InstanceId.ToString(CultureInfo.InvariantCulture))));
            }
            finally { updatingFilters = false; }
            ApplyFilters();
        }

        void ApplyFilters()
        {
            var selectedInstance = Selected(instanceFilter);
            ReplaceItems(list, ExpansionEntries()
                .Where(entry => selectedInstance is null || entry.InstanceId.ToString(CultureInfo.InvariantCulture) == selectedInstance)
                .Select(entry => new ListViewItem([entry.InstanceType,
                    entry.InstanceNumber > 0 ? entry.InstanceNumber.ToString(CultureInfo.InvariantCulture) : "-",
                    entry.InstanceName, entry.Id.ToString(),
                    entry.Name, entry.SourceId > 0 ? entry.SourceId.ToString() : "待核实"])
                {
                    Tag = entry,
                    ToolTipText = $"{entry.DisplayName} · {ExpansionName(entry)} · 副本内序号 {entry.Id}\n{entry.Note}"
                }).ToArray());
        }

        SetOptions(typeFilter, "类型", catalog.Entries.Select(entry => entry.InstanceType).Distinct(StringComparer.Ordinal)
            .Select(type => new ReferenceFilterOption($"类型：{type}", type)));
        typeFilter.SelectedIndexChanged += (_, _) => { if (!updatingFilters) RefreshFilters(refreshExpansions: true); };
        expansionFilter.SelectedIndexChanged += (_, _) => { if (!updatingFilters) RefreshFilters(refreshExpansions: false); };
        instanceFilter.SelectedIndexChanged += (_, _) =>
        {
            if (updatingFilters) return;
            _toolTip.SetToolTip(instanceFilter, instanceFilter.SelectedItem?.ToString());
            ApplyFilters();
        };
        RefreshFilters(refreshExpansions: true);
        return page;
    }
}
