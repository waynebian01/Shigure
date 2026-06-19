using System.Diagnostics;
using System.Drawing;

namespace Shigure;

public sealed class AuraRecognitionSettingsControl : UserControl
{
    private const int RealtimeScanIntervalMs = 500;
    private const int SlotIconColumnIndex = 1;
    private const int SlotNameColumnIndex = 2;
    private const int SavedIconColumnIndex = 0;
    private const int SavedNameColumnIndex = 1;
    private const int SavedHashColumnIndex = 2;
    private const int SavedFileColumnIndex = 3;
    private const int SavedStatusColumnIndex = 4;

    private readonly string _windowTitle;
    private readonly Func<(int? ClassId, int? SpecId)> _classSpecProvider;
    private readonly TextBox _templateDirectoryBox = new();
    private readonly Button _scanButton;
    private readonly Button _toggleAuraImportButton;
    private readonly Button _refreshTemplatesButton;
    private readonly Button _openTemplateDirectoryButton;
    private readonly Button _saveNameButton;
    private readonly Button _scanResultsTabButton;
    private readonly Button _savedIconsTabButton;
    private readonly System.Windows.Forms.Timer _realtimeScanTimer = new() { Interval = RealtimeScanIntervalMs };
    private readonly ToolTip _toolbarToolTip = new();
    private readonly TrackBar _hashDistanceTrackBar = new();
    private readonly Label _hashDistanceValueLabel;
    private readonly Label _summaryLabel;
    private readonly Label _anchorLabel;
    private readonly Label _templateLabel;
    private readonly Label _scopeLabel;
    private readonly Label _selectedHashLabel;
    private readonly Label _nameStatusLabel;
    private readonly Panel _auraIconTabContent = new DarkBorderPanel();
    private readonly ListView _slotList;
    private readonly ListView _savedIconList;
    private readonly ListView _candidateList;
    private readonly ImageList _slotIconList = new() { ImageSize = new Size(32, 32), ColorDepth = ColorDepth.Depth32Bit };
    private readonly ImageList _savedIconImageList = new() { ImageSize = new Size(32, 32), ColorDepth = ColorDepth.Depth32Bit };
    private readonly PictureBox _iconPreview;
    private readonly TextBox _renameBox = new();
    private readonly TextBox _detailsBox;
    private Dictionary<string, string> _auraNames = new(StringComparer.OrdinalIgnoreCase);
    private AuraRecognitionScanResult? _lastResult;
    private AuraIconRecognizer? _recognizer;
    private string? _recognizerSignature;
    private string? _currentSelectedHash;
    private (int ClassId, int SpecId) _activeScope = (0, 0);
    private bool _isScanning;
    private bool _auraImportEnabled;
    private bool _showSavedIconsTab;
    private bool _suppressSlotSelectionChanged;
    private bool _suppressSavedIconSelectionChanged;
    private int? _savedIconSortColumn;
    private SortOrder _savedIconSortOrder = SortOrder.Ascending;

    public event Action<IReadOnlyList<RecognizedAuraInfo>>? RecognizedAurasChanged;

    public AuraRecognitionSettingsControl(
        string windowTitle,
        Func<(int? ClassId, int? SpecId)>? classSpecProvider = null)
    {
        _windowTitle = string.IsNullOrWhiteSpace(windowTitle) ? "魔兽世界" : windowTitle;
        _classSpecProvider = classSpecProvider ?? (() => (null, null));
        Dock = DockStyle.Fill;
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.Text;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        _scanButton = UiTheme.CreateButton("手动扫描", UiTheme.Accent, Color.Black);
        _toggleAuraImportButton = UiTheme.CreateButton("启用识别", UiTheme.Field, UiTheme.Text);
        _refreshTemplatesButton = UiTheme.CreateButton("刷新图标", UiTheme.Field, UiTheme.Text);
        _openTemplateDirectoryButton = UiTheme.CreateButton("打开目录", UiTheme.Field, UiTheme.Text);
        _saveNameButton = UiTheme.CreateButton("保存名称", UiTheme.Accent, Color.Black);
        _scanResultsTabButton = UiTheme.CreateButton("识别结果", UiTheme.Surface, UiTheme.Text);
        _savedIconsTabButton = UiTheme.CreateButton("已有图标", UiTheme.Field, UiTheme.Muted);
        _hashDistanceValueLabel = CreateInfoLabel($"最大距离: {AuraIconRecognizer.DefaultMaxHashDistance}");
        _summaryLabel = CreateInfoLabel("等待扫描");
        _anchorLabel = CreateInfoLabel("定位框: -");
        _templateLabel = CreateInfoLabel("图标库: -");
        _scopeLabel = CreateInfoLabel("分类: 0/0");
        _selectedHashLabel = CreateInfoLabel("未选择光环");
        _nameStatusLabel = CreateInfoLabel("自定义名称: 0 个");
        _slotList = UiTheme.CreateListView(Font, ("#", 46), ("图标", 64), ("名称", 170), ("类型", 70), ("时间", 64), ("dHash", 142), ("距离", 64), ("相似度", 72), ("截图", 160), ("区域", 150));
        _slotList.SmallImageList = _slotIconList;
        _slotList.DrawSubItem += DrawSlotIconSubItem;
        _savedIconList = UiTheme.CreateListView(Font, ("图标", 64), ("名称", 170), ("dHash", 150), ("文件", 150), ("已保存", 72));
        _savedIconList.HeaderStyle = ColumnHeaderStyle.Clickable;
        _savedIconList.SmallImageList = _savedIconImageList;
        _savedIconList.DrawSubItem += DrawSavedIconSubItem;
        _candidateList = UiTheme.CreateListView(Font, ("候选", 190), ("距离", 64), ("相似度", 72), ("文件", 260));
        _iconPreview = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Field,
            BorderStyle = BorderStyle.FixedSingle,
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0, 0, 12, 0)
        };
        _detailsBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = UiTheme.Field,
            ForeColor = UiTheme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Regular, GraphicsUnit.Point)
        };

        InitializeComponent();
    }

    private void InitializeComponent()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 68));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        Controls.Add(root);

        root.Controls.Add(BuildToolbar(), 0, 0);
        root.Controls.Add(BuildResultsPane(), 0, 1);
        root.Controls.Add(BuildDetailsPane(), 0, 2);

        _scanButton.Click += (_, _) => Scan();
        _toggleAuraImportButton.Click += (_, _) => ToggleAuraImport();
        _refreshTemplatesButton.Click += (_, _) => RefreshTemplateInfo();
        _openTemplateDirectoryButton.Click += (_, _) => OpenTemplateDirectory();
        _saveNameButton.Click += (_, _) => SaveSelectedAuraName();
        _realtimeScanTimer.Tick += (_, _) => Scan(isRealtimeTick: true);
        _hashDistanceTrackBar.ValueChanged += (_, _) =>
        {
            _hashDistanceValueLabel.Text = $"最大距离: {_hashDistanceTrackBar.Value}";
            ResetRecognizer();
        };
        _renameBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            SaveSelectedAuraName();
            e.SuppressKeyPress = true;
        };
        _slotList.SelectedIndexChanged += (_, _) =>
        {
            if (!_suppressSlotSelectionChanged)
            {
                UpdateSelectedAura();
            }
        };
        _savedIconList.SelectedIndexChanged += (_, _) =>
        {
            if (!_suppressSavedIconSelectionChanged)
            {
                UpdateSelectedAura();
            }
        };
        _savedIconList.ColumnClick += (_, e) => SortSavedIconList(e.Column);
        _scanResultsTabButton.Click += (_, _) => SelectAuraIconTab(showSavedIcons: false);
        _savedIconsTabButton.Click += (_, _) => SelectAuraIconTab(showSavedIcons: true);
        Disposed += (_, _) =>
        {
            _realtimeScanTimer.Stop();
            _realtimeScanTimer.Dispose();
            PublishRecognizedAuras([]);
            ResetRecognizer();
        };

        LoadAuraNames();
        ClearSelectedAura();
        RefreshTemplateInfo();
    }

    private Control BuildToolbar()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(16),
            Margin = new Padding(0, 0, 0, 14)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 870));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0)
        };
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0, 0, 0, 12)
        };

        ConfigureActionButton(_toggleAuraImportButton, 108);
        ConfigureActionButton(_scanButton, 108);
        ConfigureActionButton(_refreshTemplatesButton, 108);
        ConfigureActionButton(_openTemplateDirectoryButton, 108);
        buttons.Controls.Add(_toggleAuraImportButton);
        buttons.Controls.Add(_scanButton);
        buttons.Controls.Add(_refreshTemplatesButton);
        buttons.Controls.Add(_openTemplateDirectoryButton);
        left.Controls.Add(buttons, 0, 0);

        left.Controls.Add(BuildHashDistanceSlider(), 0, 1);
        panel.Controls.Add(left, 0, 0);
        panel.Controls.Add(BuildRightToolbarPanel(), 1, 0);

        return panel;
    }

    private Control BuildRightToolbarPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(18, 0, 0, 0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(BuildTemplateDirectoryPanel(), 0, 0);
        panel.Controls.Add(BuildToolbarInfoPanel(), 0, 1);
        return panel;
    }

    private Control BuildTemplateDirectoryPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = 1,
            RowCount = 2,
            Height = 68,
            Margin = new Padding(0, 0, 0, 8)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        var label = CreateFieldLabel("图标根目录");
        label.Dock = DockStyle.Fill;
        label.Margin = new Padding(0, 0, 0, 4);

        _templateDirectoryBox.Text = AuraIconRecognizer.DefaultAuraDirectory;
        _templateDirectoryBox.Dock = DockStyle.Fill;
        _templateDirectoryBox.Margin = new Padding(0);
        UiTheme.StyleTextBox(_templateDirectoryBox);

        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(_templateDirectoryBox, 0, 1);
        return panel;
    }

    private Control BuildToolbarInfoPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = 1,
            RowCount = 4,
            Height = 112,
            Margin = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        ConfigureToolbarInfoLabel(_summaryLabel);
        ConfigureToolbarInfoLabel(_anchorLabel);
        ConfigureToolbarInfoLabel(_scopeLabel);
        ConfigureToolbarInfoLabel(_templateLabel);
        panel.Controls.Add(_summaryLabel, 0, 0);
        panel.Controls.Add(_anchorLabel, 0, 1);
        panel.Controls.Add(_scopeLabel, 0, 2);
        panel.Controls.Add(_templateLabel, 0, 3);
        return panel;
    }

    private Control BuildHashDistanceSlider()
    {
        var container = new TableLayoutPanel
        {
            AutoSize = false,
            BackColor = UiTheme.Surface,
            ColumnCount = 1,
            RowCount = 3,
            Width = 870,
            Height = 118,
            Padding = new Padding(12, 10, 12, 10),
            Margin = new Padding(0, 2, 0, 14)
        };
        container.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        container.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));

        var title = CreateFieldLabel("匹配距离");
        title.Dock = DockStyle.Fill;
        title.Margin = new Padding(0);

        _hashDistanceValueLabel.Dock = DockStyle.Fill;
        _hashDistanceValueLabel.TextAlign = ContentAlignment.MiddleRight;
        _hashDistanceValueLabel.Margin = new Padding(0);

        header.Controls.Add(title, 0, 0);
        header.Controls.Add(_hashDistanceValueLabel, 1, 0);

        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 1,
            RowCount = 1,
            Margin = new Padding(0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _hashDistanceTrackBar.Minimum = 0;
        _hashDistanceTrackBar.Maximum = AuraIconRecognizer.MaxHashDistance;
        _hashDistanceTrackBar.Value = AuraIconRecognizer.DefaultMaxHashDistance;
        _hashDistanceTrackBar.TickFrequency = 4;
        _hashDistanceTrackBar.SmallChange = 1;
        _hashDistanceTrackBar.LargeChange = 4;
        _hashDistanceTrackBar.Dock = DockStyle.Fill;
        _hashDistanceTrackBar.Margin = new Padding(0);
        _hashDistanceTrackBar.BackColor = UiTheme.Surface;

        row.Controls.Add(_hashDistanceTrackBar, 0, 0);

        var hint = CreateInfoLabel("匹配度, 数值越小匹配度越高。建议 6-10。");
        hint.Dock = DockStyle.Fill;
        hint.AutoSize = false;
        hint.TextAlign = ContentAlignment.MiddleLeft;
        hint.Margin = new Padding(0, 6, 0, 0);

        container.Controls.Add(header, 0, 0);
        container.Controls.Add(row, 0, 1);
        container.Controls.Add(hint, 0, 2);
        return container;
    }

    private Control BuildResultsPane()
    {
        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        split.Controls.Add(BuildSection("识别结果", BuildAuraIconTabs(), "扫描结果与已保存图标", isLast: false), 0, 0);
        split.Controls.Add(BuildSection("选中光环", BuildSelectedAuraPane(), "预览截图，并按 dHash 保存自定义名称"), 1, 0);
        return split;
    }

    private Control BuildAuraIconTabs()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var tabs = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        ConfigureTabButton(_scanResultsTabButton);
        ConfigureTabButton(_savedIconsTabButton);
        tabs.Controls.Add(_scanResultsTabButton);
        tabs.Controls.Add(_savedIconsTabButton);
        panel.Controls.Add(tabs, 0, 0);

        _auraIconTabContent.Dock = DockStyle.Fill;
        _auraIconTabContent.BackColor = UiTheme.Surface;
        _auraIconTabContent.BorderStyle = BorderStyle.None;
        _auraIconTabContent.Margin = new Padding(0);
        _auraIconTabContent.Padding = new Padding(1);
        panel.Controls.Add(_auraIconTabContent, 0, 1);

        SelectAuraIconTab(showSavedIcons: false);
        return panel;
    }

    private Control BuildSelectedAuraPane()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var previewRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        previewRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
        previewRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        previewRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _iconPreview.Dock = DockStyle.None;
        _iconPreview.Size = new Size(112, 112);
        _iconPreview.Margin = new Padding(0, 0, 12, 0);
        _iconPreview.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        previewRow.Controls.Add(_iconPreview, 0, 0);

        var previewInfo = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0)
        };
        previewInfo.Controls.Add(_selectedHashLabel);
        previewInfo.Controls.Add(_nameStatusLabel);
        previewRow.Controls.Add(previewInfo, 1, 0);
        panel.Controls.Add(previewRow, 0, 0);

        var renameRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = 3,
            RowCount = 1,
            Height = 36,
            Margin = new Padding(0, 8, 0, 8)
        };
        renameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        renameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        renameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        renameRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        var nameLabel = CreateFieldLabel("名称");
        nameLabel.Height = 36;
        nameLabel.Margin = new Padding(0);
        renameRow.Controls.Add(nameLabel, 0, 0);
        _renameBox.Dock = DockStyle.Fill;
        _renameBox.Multiline = true;
        _renameBox.Height = 36;
        _renameBox.Margin = new Padding(0, 0, 8, 0);
        UiTheme.StyleTextBox(_renameBox);
        renameRow.Controls.Add(_renameBox, 1, 0);
        ConfigureActionButton(_saveNameButton, 124);
        _saveNameButton.Height = 36;
        _saveNameButton.Margin = new Padding(0);
        renameRow.Controls.Add(_saveNameButton, 2, 0);
        panel.Controls.Add(renameRow, 0, 1);

        panel.Controls.Add(BuildCandidatePane(), 0, 2);
        return panel;
    }

    private Control BuildCandidatePane()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0, 8, 0, 0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label
        {
            Text = "候选",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Text,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        }, 0, 0);
        panel.Controls.Add(new Label
        {
            Text = "dHash 预筛选 + 模板匹配结果",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        }, 0, 1);
        _candidateList.Margin = new Padding(0, 10, 0, 0);
        panel.Controls.Add(_candidateList, 0, 2);
        return panel;
    }

    private Control BuildDetailsPane()
    {
        _detailsBox.Text =
            $"识别库来自 {AuraIconRecognizer.DefaultAuraDirectory}\\职业ID\\专精ID：AurasHash.json 保存“中文名称 -> 多个 dHash”的对象映射，同目录的 dHash.png 作为匹配图标。" + Environment.NewLine +
            $"启用识别后会每 {RealtimeScanIntervalMs}ms 实时扫描并导入光环列表；手动扫描只在点击按钮时执行。定位只使用新布局的彩色槽位框：R=2 为增益，R=3 为减益，G 为第几个光环，B 为剩余时间值。" + Environment.NewLine +
            $"每次扫描会把裁剪出的图标保存到 {AuraIconRecognizer.DefaultTempAuraDirectory}\\职业ID\\专精ID，文件名为 dHash.png；相同 dHash 已存在时会跳过写入。" + Environment.NewLine +
            $"“已有图标”会列出 tmp 里的待整理图标；保存名称后只会把对应图标复制到 {AuraIconRecognizer.DefaultAuraDirectory}\\职业ID\\专精ID，并写入当前分类目录的 AurasHash.json。职业或专精未知时会使用 0/0。";
        return BuildSection("说明", _detailsBox, "本页用于调试定位与图标候选，不会影响现有设置");
    }

    private Control BuildSection(string title, Control content, string subtitle, bool isLast = true)
    {
        var section = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14),
            Margin = new Padding(0, 0, isLast ? 0 : 12, 0)
        };
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        section.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        section.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Text,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        }, 0, 0);
        section.Controls.Add(new Label
        {
            Text = subtitle,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        }, 0, 1);

        content.Dock = DockStyle.Fill;
        content.Margin = new Padding(0, 12, 0, 0);
        section.Controls.Add(content, 0, 2);
        return section;
    }

    private void Scan(bool isRealtimeTick = false)
    {
        if (_isScanning)
        {
            return;
        }

        _isScanning = true;
        _scanButton.Enabled = false;
        _refreshTemplatesButton.Enabled = false;
        if (!isRealtimeTick)
        {
            _summaryLabel.Text = "正在扫描...";
        }

        try
        {
            UpdateActiveScope();
            LoadAuraNames();
            var tempAuraDirectory = GetScopedTempAuraDirectory();
            var matchAuraDirectory = GetScopedAuraDirectory();
            var recognizer = GetOrCreateRecognizer(matchAuraDirectory, tempAuraDirectory);
            _lastResult = recognizer.Scan();
            ApplyResult(_lastResult);
        }
        catch (Exception ex)
        {
            _summaryLabel.Text = $"扫描失败: {ex.Message}";
        }
        finally
        {
            _isScanning = false;
            _scanButton.Enabled = true;
            _refreshTemplatesButton.Enabled = true;
        }
    }

    private void ToggleAuraImport()
    {
        _auraImportEnabled = !_auraImportEnabled;
        UpdateAuraImportButton();
        if (_auraImportEnabled)
        {
            _realtimeScanTimer.Start();
            PublishRecognizedAuras(_lastResult);
            _summaryLabel.Text = _lastResult is null
                ? $"光环识别已启用，实时扫描频率 {RealtimeScanIntervalMs}ms"
                : $"光环识别已启用，已导入最近扫描结果，实时扫描频率 {RealtimeScanIntervalMs}ms";
            Scan(isRealtimeTick: true);
            return;
        }

        _realtimeScanTimer.Stop();
        PublishRecognizedAuras([]);
        _summaryLabel.Text = "光环识别已关闭，实时扫描已停止，已移除导入结果";
    }

    private void UpdateAuraImportButton()
    {
        _toggleAuraImportButton.Text = _auraImportEnabled ? "关闭识别" : "启用识别";
        _toggleAuraImportButton.BackColor = _auraImportEnabled ? UiTheme.Accent : UiTheme.Field;
        _toggleAuraImportButton.ForeColor = _auraImportEnabled ? Color.Black : UiTheme.Text;
    }

    private AuraIconRecognizer GetOrCreateRecognizer(string matchAuraDirectory, string tempAuraDirectory)
    {
        var maxHashDistance = _hashDistanceTrackBar.Value;
        var signature = $"{matchAuraDirectory}|{tempAuraDirectory}|{maxHashDistance}";
        if (_recognizer is not null && string.Equals(_recognizerSignature, signature, StringComparison.OrdinalIgnoreCase))
        {
            return _recognizer;
        }

        ResetRecognizer();
        _recognizerSignature = signature;
        _recognizer = new AuraIconRecognizer(
            _windowTitle,
            matchAuraDirectory,
            maxHashDistance: maxHashDistance,
            auraDirectory: tempAuraDirectory);
        return _recognizer;
    }

    private void ResetRecognizer()
    {
        _recognizer?.Dispose();
        _recognizer = null;
        _recognizerSignature = null;
    }

    private void ApplyResult(AuraRecognitionScanResult result)
    {
        _summaryLabel.Text = result.Message;
        _anchorLabel.Text = result.MarkerFound && result.MarkerLocation is { } marker
            ? $"定位框: ({marker.X}, {marker.Y})  框线: {result.MarkerCellSize}px  缩放: {result.Scale:0.##}"
            : "定位框: 未找到";
        _scopeLabel.Text = FormatScopeLabel();
        _templateLabel.Text = $"图标库: {result.TemplateCount} 个  目录: {result.TemplateDirectory}";
        UpdateToolbarInfoToolTips();

        var selectedHash = GetSelectedHash() ?? _currentSelectedHash;
        ListViewItem? itemToSelect = null;
        _suppressSlotSelectionChanged = true;
        _slotList.BeginUpdate();
        try
        {
            _slotList.Items.Clear();
            _slotIconList.Images.Clear();
            foreach (var slot in result.Slots)
            {
                var imageKey = AddSlotIcon(slot);
                var hash = AuraIconRecognizer.FormatHash(slot.IconHash);
                var item = new ListViewItem(new[]
                {
                    slot.Index.ToString(),
                    string.Empty,
                    GetAuraDisplayName(slot),
                    slot.Row,
                    slot.RemainingB.ToString(),
                    hash,
                    slot.HashDistance?.ToString() ?? "-",
                    slot.TemplateScore is null ? "-" : slot.TemplateScore.Value.ToString("0.000"),
                    slot.SavedIconPath is null ? "-" : Path.GetFileName(slot.SavedIconPath),
                    FormatRectangle(slot.SlotBounds)
                })
                {
                    Tag = slot,
                    ImageKey = imageKey ?? string.Empty,
                    ToolTipText = FormatSlotToolTip(slot)
                };
                _slotList.Items.Add(item);
                if (itemToSelect is null
                    && !string.IsNullOrWhiteSpace(selectedHash)
                    && hash.Equals(selectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    itemToSelect = item;
                }
            }

            itemToSelect ??= _slotList.Items.Count > 0 ? _slotList.Items[0] : null;
            if (itemToSelect is not null)
            {
                itemToSelect.Selected = true;
                itemToSelect.Focused = true;
            }
        }
        finally
        {
            _slotList.EndUpdate();
            _suppressSlotSelectionChanged = false;
        }

        RefreshSavedIconList(selectedHash ?? _currentSelectedHash);
        if (_slotList.Items.Count == 0)
        {
            ClearSelectedAura();
        }

        UpdateDetails(result);
        UpdateSelectedAura();
        PublishRecognizedAuras(result);
    }

    private void UpdateDetails(AuraRecognitionScanResult result)
    {
        var client = result.ClientBounds is { } bounds ? $"{bounds.Width}x{bounds.Height} @ ({bounds.X}, {bounds.Y})" : "-";
        var marker = result.MarkerLocation is { } point ? $"({point.X}, {point.Y})" : "-";
        _detailsBox.Text =
            $"窗口: {(result.WindowFound ? "已找到" : "未找到")}{Environment.NewLine}" +
            $"客户区: {client}{Environment.NewLine}" +
            $"定位框: {(result.MarkerFound ? "已找到" : "未找到")} {marker}{Environment.NewLine}" +
            $"分类: {_activeScope.ClassId}/{_activeScope.SpecId}{Environment.NewLine}" +
            $"图标库目录: {result.TemplateDirectory}{Environment.NewLine}" +
            $"临时截图目录: {GetScopedTempAuraDirectory()}{Environment.NewLine}" +
            $"已命名图标目录: {GetScopedAuraDirectory()}{Environment.NewLine}" +
            $"名称文件: {GetScopedNameFilePath()}{Environment.NewLine}" +
            $"自定义名称: {_auraNames.Count} 个{Environment.NewLine}" +
            $"最大距离: {_hashDistanceTrackBar.Value}{Environment.NewLine}" +
            $"图标数量: {result.TemplateCount}{Environment.NewLine}" +
            $"光环槽位: {result.Slots.Count}{Environment.NewLine}" +
            $"消息: {result.Message}";
    }

    private void UpdateSelectedAura()
    {
        var selected = GetSelectedAuraIcon();
        if (selected is null)
        {
            ClearSelectedAura();
            UpdateSelectedCandidates();
            return;
        }

        _currentSelectedHash = selected.Hash;
        _selectedHashLabel.Text = selected.Description;
        _renameBox.Enabled = true;
        _saveNameButton.Enabled = true;
        _renameBox.Text = selected.EditName;
        SetPreviewImage(LoadPreviewImage(selected.IconPath));
        UpdateSelectedCandidates();
    }

    private void UpdateSelectedCandidates()
    {
        _candidateList.BeginUpdate();
        try
        {
            _candidateList.Items.Clear();
            var slot = GetSelectedAuraIcon()?.Slot;
            if (slot is null)
            {
                return;
            }

            foreach (var candidate in slot.Candidates)
            {
                var item = new ListViewItem(new[]
                {
                    candidate.Name,
                    candidate.HashDistance.ToString(),
                    candidate.TemplateScore.ToString("0.000"),
                    Path.GetFileName(candidate.TemplatePath)
                })
                {
                    ToolTipText = candidate.TemplatePath
                };
                _candidateList.Items.Add(item);
            }
        }
        finally
        {
            _candidateList.EndUpdate();
        }
    }

    private SelectedAuraIcon? GetSelectedAuraIcon()
    {
        return _showSavedIconsTab
            ? FromSavedIcon(GetSelectedSavedIcon()) ?? FromSlot(GetSelectedSlot())
            : FromSlot(GetSelectedSlot()) ?? FromSavedIcon(GetSelectedSavedIcon());
    }

    private SelectedAuraIcon? FromSlot(AuraSlotRecognition? slot)
    {
        if (slot is null)
        {
            return null;
        }

        var hash = AuraIconRecognizer.FormatHash(slot.IconHash);
        var description =
            $"{slot.Row} #{slot.Index}{Environment.NewLine}" +
            $"dHash: {hash}{Environment.NewLine}" +
            $"截图: {(slot.SavedIconPath is null ? "-" : Path.GetFileName(slot.SavedIconPath))}";
        return new SelectedAuraIcon(
            hash,
            slot.SavedIconPath,
            description,
            GetManualAuraName(hash) ?? slot.Name ?? string.Empty,
            slot);
    }

    private SelectedAuraIcon? FromSavedIcon(SavedAuraIcon? savedIcon)
    {
        if (savedIcon is null)
        {
            return null;
        }

        var description =
            $"已有图标{Environment.NewLine}" +
            $"dHash: {savedIcon.Hash}{Environment.NewLine}" +
            $"文件: {Path.GetFileName(savedIcon.Path)}";
        return new SelectedAuraIcon(
            savedIcon.Hash,
            savedIcon.Path,
            description,
            GetManualAuraName(savedIcon.Hash) ?? string.Empty,
            null);
    }

    private void SaveSelectedAuraName()
    {
        var selected = GetSelectedAuraIcon();
        if (selected is null)
        {
            return;
        }

        var hash = selected.Hash;
        var name = _renameBox.Text.Trim();
        try
        {
            var savedIconPath = string.IsNullOrWhiteSpace(name)
                ? null
                : SaveNamedAuraIcon(selected.IconPath, hash);

            if (string.IsNullOrWhiteSpace(name))
            {
                _auraNames.Remove(hash);
            }
            else
            {
                _auraNames[hash] = name;
            }

            var nameFilePath = GetScopedNameFilePath();
            AuraNameStore.Save(nameFilePath, _auraNames);
            LoadAuraNames();
            RefreshAuraNameRows(hash);
            RefreshSavedIconList(hash);
            UpdateSelectedAura();
            RefreshTemplateInfo(refreshScope: false);
            PublishRecognizedAuras(_lastResult);
            _summaryLabel.Text = string.IsNullOrWhiteSpace(name)
                ? $"已移除 {hash} 的自定义名称"
                : $"已保存 {hash} 的名称: {name}，图标: {Path.GetFileName(savedIconPath)}，名称文件: {Path.GetFileName(nameFilePath)}";
            if (_lastResult is not null)
            {
                UpdateDetails(_lastResult);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法保存光环名称: {ex.Message}", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private string SaveNamedAuraIcon(string? sourcePath, string hash)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new InvalidOperationException("当前光环没有可用截图，无法保存图标。");
        }

        var directory = GetScopedAuraDirectory();
        Directory.CreateDirectory(directory);
        var iconPath = Path.Combine(directory, $"{hash}.png");
        File.Copy(sourcePath, iconPath, overwrite: true);
        return iconPath;
    }

    private void LoadAuraNames()
    {
        _auraNames = AuraNameStore.Load(GetScopedNameFilePath());
        _nameStatusLabel.Text = $"自定义名称: {_auraNames.Count} 个";
    }

    private void RefreshAuraNameRows(string hash)
    {
        foreach (ListViewItem item in _slotList.Items)
        {
            if (item.Tag is not AuraSlotRecognition slot
                || !hash.Equals(AuraIconRecognizer.FormatHash(slot.IconHash), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            item.SubItems[SlotNameColumnIndex].Text = GetAuraDisplayName(slot);
            item.ToolTipText = FormatSlotToolTip(slot);
        }

        foreach (ListViewItem item in _savedIconList.Items)
        {
            if (item.Tag is not SavedAuraIcon savedIcon
                || !hash.Equals(savedIcon.Hash, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            item.SubItems[SavedNameColumnIndex].Text = GetAuraDisplayName(savedIcon.Hash, null);
            item.SubItems[SavedStatusColumnIndex].Text = GetSavedIconStatus(savedIcon.Hash);
            item.ToolTipText = FormatSavedIconToolTip(savedIcon);
        }
    }

    private AuraSlotRecognition? GetSelectedSlot()
        => _slotList.SelectedItems.Count == 0
            ? null
            : _slotList.SelectedItems[0].Tag as AuraSlotRecognition;

    private SavedAuraIcon? GetSelectedSavedIcon()
        => _savedIconList.SelectedItems.Count == 0
            ? null
            : _savedIconList.SelectedItems[0].Tag as SavedAuraIcon;

    private string? GetSelectedHash()
    {
        return GetSelectedAuraIcon()?.Hash;
    }

    private string GetAuraDisplayName(AuraSlotRecognition slot)
    {
        var hash = AuraIconRecognizer.FormatHash(slot.IconHash);
        return GetAuraDisplayName(hash, slot.Name);
    }

    private string GetAuraDisplayName(string hash, string? fallbackName)
        => GetManualAuraName(hash) ?? fallbackName ?? "-";

    private void PublishRecognizedAuras(AuraRecognitionScanResult? result)
    {
        if (!_auraImportEnabled)
        {
            return;
        }

        PublishRecognizedAuras(result is null
            ? Array.Empty<RecognizedAuraInfo>()
            : BuildRecognizedAuraInfo(result));
    }

    private void PublishRecognizedAuras(IReadOnlyList<RecognizedAuraInfo> auras)
    {
        RecognizedAurasChanged?.Invoke(auras);
    }

    private IReadOnlyList<RecognizedAuraInfo> BuildRecognizedAuraInfo(AuraRecognitionScanResult result)
    {
        if (result.Slots.Count == 0)
        {
            return Array.Empty<RecognizedAuraInfo>();
        }

        var auras = new List<RecognizedAuraInfo>(result.Slots.Count);
        foreach (var slot in result.Slots)
        {
            var hash = AuraIconRecognizer.FormatHash(slot.IconHash);
            var name = GetManualAuraName(hash) ?? slot.Name;
            if (string.IsNullOrWhiteSpace(name) || name == "-")
            {
                continue;
            }

            auras.Add(new RecognizedAuraInfo(
                name.Trim(),
                slot.RemainingB,
                slot.Row,
                slot.Index,
                hash,
                slot.HashDistance,
                slot.TemplateScore));
        }

        return auras;
    }

    private string? GetManualAuraName(AuraSlotRecognition slot)
    {
        var hash = AuraIconRecognizer.FormatHash(slot.IconHash);
        return GetManualAuraName(hash);
    }

    private string? GetManualAuraName(string hash)
    {
        return _auraNames.TryGetValue(hash, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : null;
    }

    private void ClearSelectedAura()
    {
        _currentSelectedHash = null;
        _selectedHashLabel.Text = "未选择光环";
        _renameBox.Text = string.Empty;
        _renameBox.Enabled = false;
        _saveNameButton.Enabled = false;
        SetPreviewImage(null);
    }

    private string? AddSlotIcon(AuraSlotRecognition slot)
    {
        if (string.IsNullOrWhiteSpace(slot.SavedIconPath) || !File.Exists(slot.SavedIconPath))
        {
            return null;
        }

        var key = AuraIconRecognizer.FormatHash(slot.IconHash);
        if (_slotIconList.Images.ContainsKey(key))
        {
            return key;
        }

        try
        {
            _slotIconList.Images.Add(key, CreateListIcon(slot.SavedIconPath));
            return key;
        }
        catch
        {
            return null;
        }
    }

    private void RefreshSavedIconList(string? selectedHash = null)
    {
        var directory = GetScopedTempAuraDirectory();
        Directory.CreateDirectory(directory);

        var files = SortSavedIconFiles(
            Directory.EnumerateFiles(directory, "*.png")
                .Where(path => IsHashFileName(Path.GetFileNameWithoutExtension(path))));

        ListViewItem? itemToSelect = null;
        _suppressSavedIconSelectionChanged = true;
        _savedIconList.BeginUpdate();
        try
        {
            _savedIconList.Items.Clear();
            _savedIconImageList.Images.Clear();
            foreach (var path in files)
            {
                var hash = Path.GetFileNameWithoutExtension(path);
                var savedIcon = new SavedAuraIcon(hash, path);
                var imageKey = AddSavedIconImage(savedIcon);
                var item = new ListViewItem(new[]
                {
                    string.Empty,
                    GetAuraDisplayName(hash, null),
                    hash,
                    Path.GetFileName(path),
                    GetSavedIconStatus(hash)
                })
                {
                    Tag = savedIcon,
                    ImageKey = imageKey ?? string.Empty,
                    ToolTipText = FormatSavedIconToolTip(savedIcon)
                };
                _savedIconList.Items.Add(item);
                if (itemToSelect is null
                    && !string.IsNullOrWhiteSpace(selectedHash)
                    && hash.Equals(selectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    itemToSelect = item;
                }
            }

            itemToSelect ??= _savedIconList.Items.Count > 0 ? _savedIconList.Items[0] : null;
            if (itemToSelect is not null)
            {
                itemToSelect.Selected = true;
                itemToSelect.Focused = true;
            }
        }
        finally
        {
            _savedIconList.EndUpdate();
            _suppressSavedIconSelectionChanged = false;
        }
    }

    private void SortSavedIconList(int column)
    {
        if (column is not SavedHashColumnIndex and not SavedFileColumnIndex)
        {
            return;
        }

        if (_savedIconSortColumn == column)
        {
            _savedIconSortOrder = _savedIconSortOrder == SortOrder.Ascending
                ? SortOrder.Descending
                : SortOrder.Ascending;
        }
        else
        {
            _savedIconSortColumn = column;
            _savedIconSortOrder = SortOrder.Ascending;
        }

        RefreshSavedIconList(_currentSelectedHash);
    }

    private List<string> SortSavedIconFiles(IEnumerable<string> files)
    {
        var fileList = files.ToList();
        if (_savedIconSortColumn == SavedHashColumnIndex)
        {
            return SortSavedIconFilesBy(fileList, path => Path.GetFileNameWithoutExtension(path) ?? string.Empty);
        }

        if (_savedIconSortColumn == SavedFileColumnIndex)
        {
            return SortSavedIconFilesBy(fileList, Path.GetFileName);
        }

        return fileList
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<string> SortSavedIconFilesBy(List<string> files, Func<string, string> keySelector)
    {
        var ordered = _savedIconSortOrder == SortOrder.Descending
            ? files.OrderByDescending(keySelector, StringComparer.OrdinalIgnoreCase)
            : files.OrderBy(keySelector, StringComparer.OrdinalIgnoreCase);

        return ordered
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string GetSavedIconStatus(string hash)
    {
        var name = GetManualAuraName(hash);
        if (string.IsNullOrWhiteSpace(name))
        {
            return "-";
        }

        var expectedPath = Path.Combine(GetScopedAuraDirectory(), $"{hash}.png");
        return File.Exists(expectedPath) ? "有" : "-";
    }

    private string FormatSavedIconToolTip(SavedAuraIcon savedIcon)
        => $"名称: {GetAuraDisplayName(savedIcon.Hash, null)}  dHash: {savedIcon.Hash}  截图: {savedIcon.Path}";

    private static bool IsHashFileName(string? fileName)
        => !string.IsNullOrWhiteSpace(fileName)
            && fileName.Length == 16
            && fileName.All(Uri.IsHexDigit);

    private string? AddSavedIconImage(SavedAuraIcon savedIcon)
    {
        if (!File.Exists(savedIcon.Path))
        {
            return null;
        }

        if (_savedIconImageList.Images.ContainsKey(savedIcon.Hash))
        {
            return savedIcon.Hash;
        }

        try
        {
            _savedIconImageList.Images.Add(savedIcon.Hash, CreateListIcon(savedIcon.Path));
            return savedIcon.Hash;
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap CreateListIcon(string path)
    {
        using var source = LoadImageCopy(path);
        var icon = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(icon);
        graphics.Clear(Color.Transparent);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        graphics.DrawImage(source, new Rectangle(0, 0, 32, 32));
        return icon;
    }

    private static Image? LoadPreviewImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return LoadImageCopy(path);
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap LoadImageCopy(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var source = Image.FromStream(stream);
        return new Bitmap(source);
    }

    private void SetPreviewImage(Image? image)
    {
        var previous = _iconPreview.Image;
        _iconPreview.Image = image;
        previous?.Dispose();
    }

    private void DrawSlotIconSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        DrawIconSubItem(e, _slotIconList, SlotIconColumnIndex);
    }

    private void DrawSavedIconSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        DrawIconSubItem(e, _savedIconImageList, SavedIconColumnIndex);
    }

    private static void DrawIconSubItem(DrawListViewSubItemEventArgs e, ImageList imageList, int iconColumnIndex)
    {
        if (e.ColumnIndex != iconColumnIndex || e.Item is null)
        {
            return;
        }

        var key = e.Item.ImageKey;
        if (string.IsNullOrWhiteSpace(key) || !imageList.Images.ContainsKey(key))
        {
            return;
        }

        var image = imageList.Images[key];
        if (image is null)
        {
            return;
        }

        var size = Math.Min(32, Math.Min(e.Bounds.Width - 12, e.Bounds.Height - 6));
        if (size <= 0)
        {
            return;
        }

        var bounds = new Rectangle(
            e.Bounds.Left + (e.Bounds.Width - size) / 2,
            e.Bounds.Top + (e.Bounds.Height - size) / 2,
            size,
            size);
        e.Graphics.DrawImage(image, bounds);
    }

    private void SelectAuraIconTab(bool showSavedIcons)
    {
        _showSavedIconsTab = showSavedIcons;
        StyleTabButton(_scanResultsTabButton, !showSavedIcons);
        StyleTabButton(_savedIconsTabButton, showSavedIcons);

        var content = showSavedIcons ? _savedIconList : _slotList;
        if (!_auraIconTabContent.Controls.Contains(content))
        {
            _auraIconTabContent.Controls.Clear();
            content.Dock = DockStyle.Fill;
            content.Margin = new Padding(0);
            _auraIconTabContent.Controls.Add(content);
        }

        UpdateSelectedAura();
    }

    private void RefreshTemplateInfo(bool refreshScope = true)
    {
        try
        {
            if (refreshScope)
            {
                UpdateActiveScope();
            }

            LoadAuraNames();
            var directory = GetScopedAuraDirectory();
            Directory.CreateDirectory(directory);
            var count = CountNamedAuraIcons(directory);
            RefreshSavedIconList(_currentSelectedHash);
            ResetRecognizer();
            _scopeLabel.Text = FormatScopeLabel();
            _templateLabel.Text = $"图标库: {count} 个  目录: {directory}";
            UpdateToolbarInfoToolTips();
            _summaryLabel.Text = count == 0 ? "图标库为空" : "图标库已刷新";
        }
        catch (Exception ex)
        {
            _summaryLabel.Text = $"图标库刷新失败: {ex.Message}";
        }
    }

    private void OpenTemplateDirectory()
    {
        try
        {
            UpdateActiveScope();
            var directory = GetScopedAuraDirectory();
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开图标目录: {ex.Message}", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void UpdateActiveScope()
    {
        var (classId, specId) = _classSpecProvider();
        _activeScope = (NormalizeScopeValue(classId), NormalizeScopeValue(specId));
    }

    private string GetScopedTempAuraDirectory() => BuildScopedDirectory(AuraIconRecognizer.DefaultTempAuraDirectory);

    private string GetScopedAuraDirectory() => BuildScopedDirectory(ReadAuraBaseDirectory());

    private string GetScopedNameFilePath() => AuraNameStore.GetNameFilePath(GetScopedAuraDirectory());

    private string BuildScopedDirectory(string baseDirectory)
        => Path.Combine(baseDirectory, _activeScope.ClassId.ToString(), _activeScope.SpecId.ToString());

    private string FormatScopeLabel() => $"分类: {_activeScope.ClassId}/{_activeScope.SpecId}";

    private static int NormalizeScopeValue(int? value) => value is > 0 and <= 255 ? value.Value : 0;

    private string ReadAuraBaseDirectory()
    {
        var path = _templateDirectoryBox.Text.Trim();
        return string.IsNullOrWhiteSpace(path)
            ? AuraIconRecognizer.DefaultAuraDirectory
            : path;
    }

    private static int CountNamedAuraIcons(string directory)
        => AuraNameStore.Load(AuraNameStore.GetNameFilePath(directory))
            .Count(pair => File.Exists(Path.Combine(directory, $"{pair.Key}.png")));

    private static Label CreateInfoLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = UiTheme.Muted,
        Margin = new Padding(0, 0, 0, 4)
    };

    private void ConfigureToolbarInfoLabel(Label label)
    {
        label.AutoSize = false;
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.AutoEllipsis = true;
        label.Margin = new Padding(0);
        _toolbarToolTip.SetToolTip(label, label.Text);
    }

    private void UpdateToolbarInfoToolTips()
    {
        _toolbarToolTip.SetToolTip(_summaryLabel, _summaryLabel.Text);
        _toolbarToolTip.SetToolTip(_anchorLabel, _anchorLabel.Text);
        _toolbarToolTip.SetToolTip(_scopeLabel, _scopeLabel.Text);
        _toolbarToolTip.SetToolTip(_templateLabel, _templateLabel.Text);
    }

    private static Label CreateFieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = UiTheme.Muted,
        Margin = new Padding(0, 0, 12, 0)
    };

    private static void ConfigureActionButton(Button button, int width)
    {
        button.AutoSize = false;
        button.Size = new Size(width, 36);
        button.Padding = new Padding(8, 0, 8, 0);
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.Margin = new Padding(0, 0, 8, 0);
    }

    private static void ConfigureTabButton(Button button)
    {
        button.AutoSize = false;
        button.Size = new Size(112, 30);
        button.Padding = new Padding(8, 0, 8, 0);
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.Margin = new Padding(0, 0, 2, 0);
        button.FlatAppearance.BorderSize = 1;
    }

    private static void StyleTabButton(Button button, bool selected)
    {
        button.BackColor = selected ? UiTheme.Surface : UiTheme.Field;
        button.ForeColor = selected ? UiTheme.Text : UiTheme.Muted;
        button.FlatAppearance.BorderColor = selected ? UiTheme.Border : UiTheme.Field;
        button.FlatAppearance.MouseOverBackColor = selected ? UiTheme.Surface : UiTheme.Hover;
        button.FlatAppearance.MouseDownBackColor = UiTheme.Pressed;
    }

    private static string FormatRectangle(Rectangle rectangle)
        => $"{rectangle.X},{rectangle.Y} {rectangle.Width}x{rectangle.Height}";

    private static string ShortHash(string hash)
        => hash.Length <= 8 ? hash : hash[..8];

    private string FormatSlotToolTip(AuraSlotRecognition slot)
        => $"{slot.Row} #{slot.Index}  名称: {GetAuraDisplayName(slot)}  dHash: {AuraIconRecognizer.FormatHash(slot.IconHash)}  Icon: {FormatRectangle(slot.IconBounds)}  截图: {slot.SavedIconPath ?? "-"}";

    private sealed record SavedAuraIcon(string Hash, string Path);

    private sealed record SelectedAuraIcon(
        string Hash,
        string? IconPath,
        string Description,
        string EditName,
        AuraSlotRecognition? Slot);

    private sealed class DarkBorderPanel : Panel
    {
        public DarkBorderPanel()
        {
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(UiTheme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }
}
