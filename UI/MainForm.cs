using System.Drawing;

namespace Shigure;

public sealed class MainForm : Form, IMessageFilter
{
    private const int ResizeGripSize = 8;
    private const int RoundedCornerResizeDebounceMs = 80;
    private const string HeaderIconResourcePath = "Assets.arasaka-icon-transparent.png";
    private static readonly Color DefaultHeaderIconColor = Color.White;
    private static readonly IReadOnlyDictionary<int, Color> ClassIconColors = new Dictionary<int, Color>
    {
        [1] = ColorTranslator.FromHtml("#C79C6E"),
        [2] = ColorTranslator.FromHtml("#F58CBA"),
        [3] = ColorTranslator.FromHtml("#ABD473"),
        [4] = ColorTranslator.FromHtml("#FFF569"),
        [5] = ColorTranslator.FromHtml("#FFFFFF"),
        [6] = ColorTranslator.FromHtml("#C41F3B"),
        [7] = ColorTranslator.FromHtml("#0070DE"),
        [8] = ColorTranslator.FromHtml("#69CCF0"),
        [9] = ColorTranslator.FromHtml("#9482C9"),
        [10] = ColorTranslator.FromHtml("#00FF96"),
        [11] = ColorTranslator.FromHtml("#FF7D0A"),
        [12] = ColorTranslator.FromHtml("#A330C9"),
        [13] = ColorTranslator.FromHtml("#33937F")
    };

    private Button _toggleKeyButton = null!;
    private ComboBox _modeComboBox = null!;
    private ComboBox _moduleComboBox = null!;
    private Label _moduleFilterLabel = null!;
    private Label _moduleCountLabel = null!;
    private Button _settingsButton = null!;
    private string _toggleKeyName = "XBUTTON2";
    private string? _selectedModuleId;
    private bool _isCapturingToggleKey;
    private bool _suppressModuleSelectionChanged;
    private string? _lastModuleSelectorSignature;
    private bool _usesDwmRoundedCorners = true;

    private Button _enableButton = null!;

    private PictureBox _headerIcon = null!;
    private Label _titleLabel = null!;
    private Label _runtimeStatusLabel = null!;
    private Bitmap? _headerIconMask;
    private Color? _currentHeaderIconColor;

    private readonly StatusForm _statusForm;
    private readonly ModuleStore _moduleStore;
    private readonly ModuleEditorControl _moduleEditor;
    private readonly AppOptions _initialOptions;
    private readonly UiCacheState _uiCache;
    private readonly System.Windows.Forms.Timer _roundedCornerResizeTimer;
    private IReadOnlyList<RecognizedAuraInfo> _recognizedAuras = Array.Empty<RecognizedAuraInfo>();
    private ShigureRuntime? _runtime;
    private CancellationTokenSource? _runtimeCts;
    private Task? _runtimeTask;
    private RenderSnapshot? _lastSnapshot;
    private string? _lastLoggedStep;
    private string? _lastLoggedStepTarget;
    private string? _lastLoggedClass;
    private string? _lastLoggedModule;
    private bool? _lastLoggedEnabled;

    public MainForm(AppOptions? initialOptions = null)
    {
        _initialOptions = initialOptions ?? AppOptions.FromArgs(Array.Empty<string>());
        _uiCache = UiCacheStore.Load();
        _moduleStore = new ModuleStore(ModuleStore.ResolveModuleDirectory(AppPaths.BaseDirectory));
        _statusForm = new StatusForm();
        _roundedCornerResizeTimer = new System.Windows.Forms.Timer
        {
            Interval = RoundedCornerResizeDebounceMs
        };
        _roundedCornerResizeTimer.Tick += (_, _) =>
        {
            _roundedCornerResizeTimer.Stop();
            if (IsHandleCreated && !_usesDwmRoundedCorners)
            {
                UiTheme.ApplyFallbackRoundedCorners(this);
            }
        };
        Application.AddMessageFilter(this);
        InitializeComponent();
        _statusForm.AttachSettingsPanel(BuildSettingsPanel());
        _moduleEditor = new ModuleEditorControl(_moduleStore, RestartRuntimeFromEditor, AppPaths.BaseDirectory);
        _statusForm.AttachModuleEditor(_moduleEditor);
        var auraRecognitionPanel = new AuraRecognitionSettingsControl(
            _initialOptions.WindowTitle,
            () => (_lastSnapshot?.ClassId, _lastSnapshot?.SpecId));
        auraRecognitionPanel.RecognizedAurasChanged += HandleRecognizedAurasChanged;
        _statusForm.AttachAuraRecognitionPanel(auraRecognitionPanel);
        _statusForm.FormClosing += (_, _) =>
        {
            CancelToggleKeyCapture();
            SaveUiCache();
        };
        TryApplyApplicationIcon();
        ApplyCachedWindowState();
        ApplyInitialOptions();
        WireSettingEvents();
        SetRuntimeControls(running: false);
        AppendLog("界面已就绪");
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UiTheme.ApplyDarkTitleBar(this);
        UiTheme.ApplyTranslucentBackground(this);
        _usesDwmRoundedCorners = UiTheme.ApplyRoundedCorners(this);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        StartRuntime();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveUiCache();
        _roundedCornerResizeTimer.Stop();
        Application.RemoveMessageFilter(this);
        _runtimeCts?.Cancel();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _roundedCornerResizeTimer.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (IsHandleCreated && !_usesDwmRoundedCorners)
        {
            ScheduleFallbackRoundedCornerUpdate();
        }
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        if (IsHandleCreated && !_usesDwmRoundedCorners)
        {
            _roundedCornerResizeTimer.Stop();
            UiTheme.ApplyFallbackRoundedCorners(this);
        }
    }

    private void ScheduleFallbackRoundedCornerUpdate()
    {
        _roundedCornerResizeTimer.Stop();
        _roundedCornerResizeTimer.Start();
    }

    protected override void WndProc(ref Message m)
    {
        const int WmNcHitTest = 0x0084;
        if (m.Msg == WmNcHitTest)
        {
            base.WndProc(ref m);
            if (m.Result == NativeMethods.HtClient)
            {
                m.Result = HitTestResizeGrip(PointToClient(Cursor.Position));
            }

            return;
        }

        base.WndProc(ref m);
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        Text = GetWindowTitle();

        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ClientSize = new Size(680, 64);
        MinimumSize = new Size(420, 56);
        BackColor = Color.FromArgb(18, 21, 26);
        ForeColor = UiTheme.Text;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(12),
            RowCount = 1,
            ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        root.Controls.Add(BuildTopBar(), 0, 0);

        ResumeLayout(false);
    }

    private Control BuildTopBar()
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var brand = new FlowLayoutPanel
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 12, 0),
            Padding = new Padding(0)
        };

        _headerIcon = CreateHeaderIcon();
        UpdateHeaderIconColor(null);

        _titleLabel = new Label
        {
            Text = "Shigure",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font(Font.FontFamily, 13F, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Margin = new Padding(8, 0, 0, 0)
        };

        brand.Controls.Add(_headerIcon);
        brand.Controls.Add(_titleLabel);

        _runtimeStatusLabel = new Label
        {
            Text = string.Empty,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UiTheme.Muted
        };

        EnableDrag(bar);
        EnableDrag(brand);
        EnableDrag(_headerIcon);
        EnableDrag(_titleLabel);
        EnableDrag(_runtimeStatusLabel);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };

        _enableButton = UiTheme.CreateButton("开关", UiTheme.Field, UiTheme.Text);
        ConfigureTopBarButton(_enableButton);
        _enableButton.Click += (_, _) => ToggleEnabled();

        _settingsButton = UiTheme.CreateButton("设置", UiTheme.Field, UiTheme.Text);
        ConfigureTopBarButton(_settingsButton);
        _settingsButton.Click += (_, _) => ShowSettingsView();

        var closeButton = UiTheme.CreateButton("✕", UiTheme.Field, UiTheme.Muted);
        ConfigureTopBarButton(closeButton);
        closeButton.FlatAppearance.MouseOverBackColor = UiTheme.Danger;
        closeButton.Click += (_, _) => Close();

        buttons.Controls.AddRange(new Control[] { _enableButton, _settingsButton, closeButton });

        bar.Controls.Add(brand, 0, 0);
        bar.Controls.Add(_runtimeStatusLabel, 1, 0);
        bar.Controls.Add(buttons, 2, 0);
        return bar;
    }

    private static PictureBox CreateHeaderIcon()
    {
        var box = new PictureBox
        {
            Size = new Size(32, 32),
            MinimumSize = new Size(32, 32),
            MaximumSize = new Size(32, 32),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Anchor = AnchorStyles.Left
        };

        return box;
    }

    private void UpdateHeaderIconColor(int? classId)
    {
        var color = ResolveClassIconColor(classId);
        if (_currentHeaderIconColor == color)
        {
            return;
        }

        _currentHeaderIconColor = color;
        _headerIconMask ??= LoadHeaderIconMask();
        if (_headerIconMask is null)
        {
            return;
        }

        var previous = _headerIcon.Image;
        _headerIcon.Image = TintHeaderIcon(_headerIconMask, color);
        previous?.Dispose();
    }

    private static Color ResolveClassIconColor(int? classId)
        => classId is not null && ClassIconColors.TryGetValue(classId.Value, out var color)
            ? color
            : DefaultHeaderIconColor;

    private static Bitmap? LoadHeaderIconMask()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream(GetHeaderIconResourceName());
        if (stream is null)
        {
            return null;
        }

        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    private static string GetHeaderIconResourceName() =>
        $"{typeof(MainForm).Namespace}.{HeaderIconResourcePath}";

    private static Bitmap TintHeaderIcon(Bitmap mask, Color color)
    {
        var bitmap = new Bitmap(mask.Width, mask.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        bitmap.SetResolution(mask.HorizontalResolution, mask.VerticalResolution);

        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var pixel = mask.GetPixel(x, y);
                if (pixel.A == 0)
                {
                    continue;
                }

                bitmap.SetPixel(x, y, Color.FromArgb(pixel.A, color));
            }
        }

        return bitmap;
    }

    private Control BuildSettingsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding = new Padding(0),
            ColumnCount = 1,
            Margin = new Padding(0),
            RowCount = 2
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var settingsGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(16, 16, 16, 10),
            Margin = new Padding(0, 0, 0, 14)
        };
        settingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        settingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settingsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        settingsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        Label CreateSettingLabel(string text) => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UiTheme.Muted,
            Margin = new Padding(0, 0, 12, 14)
        };

        const int settingControlWidth = 190;

        _toggleKeyButton = UiTheme.CreateButton("XBUTTON2", UiTheme.Field, UiTheme.Text);
        _toggleKeyButton.AutoSize = false;
        _toggleKeyButton.Width = settingControlWidth;
        _toggleKeyButton.Height = 36;
        _toggleKeyButton.Padding = new Padding(6, 0, 6, 0);
        _toggleKeyButton.TextAlign = ContentAlignment.MiddleCenter;
        _toggleKeyButton.Anchor = AnchorStyles.Left;
        _toggleKeyButton.Margin = new Padding(0, 0, 0, 12);
        _toggleKeyButton.Click += (_, _) => BeginCaptureToggleKey();
        settingsGrid.Controls.Add(CreateSettingLabel("触发键"), 0, 0);
        settingsGrid.Controls.Add(_toggleKeyButton, 1, 0);

        _modeComboBox = new ComboBox();
        UiTheme.StyleComboBox(_modeComboBox);
        _modeComboBox.Items.AddRange(new object[] { "开关", "单击", "按住" });
        _modeComboBox.SelectedIndex = 0;
        _modeComboBox.Width = settingControlWidth;
        _modeComboBox.Anchor = AnchorStyles.Left;
        _modeComboBox.Margin = new Padding(0, 0, 0, 12);
        settingsGrid.Controls.Add(CreateSettingLabel("发送模式"), 0, 1);
        settingsGrid.Controls.Add(_modeComboBox, 1, 1);

        var moduleInfo = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(16, 16, 16, 14),
            Margin = new Padding(0)
        };
        moduleInfo.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        moduleInfo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        moduleInfo.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        moduleInfo.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _moduleComboBox = new ComboBox();
        UiTheme.StyleComboBox(_moduleComboBox);
        _moduleComboBox.Width = 520;
        _moduleComboBox.Anchor = AnchorStyles.Left;
        _moduleComboBox.Margin = new Padding(0, 0, 0, 12);
        moduleInfo.Controls.Add(CreateSettingLabel("模块选择"), 0, 0);
        moduleInfo.Controls.Add(_moduleComboBox, 1, 0);

        var moduleStatus = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0)
        };
        moduleStatus.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        moduleStatus.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        moduleStatus.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var moduleInfoText = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0)
        };

        _moduleFilterLabel = CreateInfoLabel("筛选: 等待游戏状态");
        _moduleCountLabel = CreateInfoLabel("可选模块: 0");
        moduleInfoText.Controls.Add(_moduleFilterLabel);
        moduleInfoText.Controls.Add(_moduleCountLabel);

        var refreshModulesButton = UiTheme.CreateButton("刷新模块", UiTheme.Field, UiTheme.Text);
        refreshModulesButton.AutoSize = false;
        refreshModulesButton.Size = new Size(116, 38);
        refreshModulesButton.Padding = new Padding(8, 0, 8, 0);
        refreshModulesButton.TextAlign = ContentAlignment.MiddleCenter;
        refreshModulesButton.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        refreshModulesButton.Margin = new Padding(0, 0, 0, 8);
        refreshModulesButton.Click += (_, _) => RefreshModuleSelector(_lastSnapshot, reloadModules: true);

        moduleStatus.Controls.Add(refreshModulesButton, 0, 0);
        moduleStatus.Controls.Add(moduleInfoText, 0, 1);
        moduleInfo.Controls.Add(moduleStatus, 1, 1);

        panel.Controls.Add(settingsGrid, 0, 0);
        panel.Controls.Add(moduleInfo, 0, 1);
        return panel;
    }

    private void ApplyInitialOptions()
    {
        var cachedToggleKey = _uiCache.ToggleKey?.Trim();
        var initialToggleKey = !string.IsNullOrWhiteSpace(cachedToggleKey)
            ? cachedToggleKey
            : _initialOptions.ToggleKey.Trim();
        initialToggleKey = string.IsNullOrWhiteSpace(initialToggleKey) ? "XBUTTON2" : initialToggleKey;
        _toggleKeyName = IsUnsupportedToggleKey(initialToggleKey) ? "XBUTTON2" : initialToggleKey;
        _selectedModuleId = string.IsNullOrWhiteSpace(_uiCache.SelectedModuleId)
            ? null
            : _uiCache.SelectedModuleId.Trim();
        SetToggleKeyButtonText();
        _modeComboBox.SelectedIndex = _initialOptions.Mode switch
        {
            SendMode.Click => 1,
            SendMode.Hold => 2,
            _ => 0
        };
        RefreshModuleSelector(_lastSnapshot, reloadModules: false);
    }

    private void WireSettingEvents()
    {
        _modeComboBox.SelectedIndexChanged += HandleSettingCommitted;
        _moduleComboBox.SelectedIndexChanged += HandleModuleSelectionChanged;
    }

    private async void HandleSettingCommitted(object? sender, EventArgs e)
    {
        await RestartRuntimeAfterSettingChangeAsync();
    }

    private void StartRuntime()
    {
        if (_runtimeTask is { IsCompleted: false })
        {
            return;
        }

        var options = BuildOptions();
        if (IsUnsupportedToggleKey(options.ToggleKey))
        {
            MessageBox.Show("触发键不支持 ALT，请选择其他按键。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (KeySender.GetVk(options.ToggleKey) is null)
        {
            MessageBox.Show($"无法识别触发键: {options.ToggleKey}", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            _runtimeCts = new CancellationTokenSource();
            _moduleStore.Reload();
            _runtime = new ShigureRuntime(AppPaths.BaseDirectory, options, _moduleStore);
            _runtime.SnapshotUpdated += HandleSnapshotUpdated;
            _runtime.SetRecognizedAuras(_recognizedAuras);
            _runtimeTask = Task.Run(() => RunRuntimeAsync(_runtime, _runtimeCts.Token));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppendLog($"启动失败: {ex.Message}");
            return;
        }

        _lastLoggedStep = null;
        _lastLoggedStepTarget = null;
        _lastLoggedClass = null;
        _lastLoggedModule = null;
        _lastLoggedEnabled = null;
        SetRuntimeControls(running: true);
        AppendLog($"运行已启动: {options.WindowTitle} / {options.ToggleKey} / {ModeLabel(options.Mode)}");
    }

    private async Task RunRuntimeAsync(ShigureRuntime runtime, CancellationToken cancellationToken)
    {
        try
        {
            await runtime.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Normal stop path.
        }
        catch (Exception ex)
        {
            PostToUi(() =>
            {
                AppendLog($"运行异常: {ex.Message}");
                _titleLabel.ForeColor = UiTheme.Danger;
            });
        }
        finally
        {
            PostToUi(() => SetRuntimeControls(running: false));
        }
    }

    private async Task StopRuntimeAsync()
    {
        if (_runtimeCts is null)
        {
            return;
        }

        _runtimeCts.Cancel();

        if (_runtimeTask is not null)
        {
            try
            {
                await _runtimeTask;
            }
            catch (OperationCanceledException)
            {
                // Already handled by the runtime task.
            }
        }

        if (_runtime is not null)
        {
            _runtime.SnapshotUpdated -= HandleSnapshotUpdated;
        }

        _runtimeCts.Dispose();
        _runtimeCts = null;
        _runtimeTask = null;
        _runtime = null;
        SetRuntimeControls(running: false);
        AppendLog("运行已停止");
    }

    private async void RestartRuntimeFromEditor()
    {
        RefreshModuleSelector(_lastSnapshot, reloadModules: true);
        if (_runtime is null)
        {
            _moduleStore.Reload();
            return;
        }

        AppendLog("模块已变更, 重新启动运行");
        await StopRuntimeAsync();
        StartRuntime();
    }

    private void ToggleEnabled()
    {
        if (_runtime is null)
        {
            return;
        }

        var nextEnabled = !(_lastSnapshot?.Enabled ?? false);
        _runtime.SetEnabled(nextEnabled);
    }

    private AppOptions BuildOptions()
    {
        var toggleKey = string.IsNullOrWhiteSpace(_toggleKeyName)
            ? "XBUTTON2"
            : _toggleKeyName.Trim();

        return _initialOptions with { ToggleKey = toggleKey, Mode = ReadMode(), ModuleId = _selectedModuleId };
    }

    private SendMode ReadMode()
    {
        return _modeComboBox.SelectedIndex switch
        {
            1 => SendMode.Click,
            2 => SendMode.Hold,
            _ => SendMode.Switch
        };
    }

    private void HandleSnapshotUpdated(RenderSnapshot snapshot)
    {
        PostToUi(() => ApplySnapshot(snapshot));
    }

    private void HandleRecognizedAurasChanged(IReadOnlyList<RecognizedAuraInfo> auras)
    {
        _recognizedAuras = auras.Count == 0
            ? Array.Empty<RecognizedAuraInfo>()
            : auras.ToArray();
        _statusForm.SetRecognizedAuras(_recognizedAuras);
        _moduleEditor.SetRecognizedAuras(_recognizedAuras);
        _runtime?.SetRecognizedAuras(_recognizedAuras);
    }

    private void ApplySnapshot(RenderSnapshot snapshot)
    {
        _lastSnapshot = snapshot;

        UpdateHeaderIconColor(snapshot.ClassId);
        UpdateLogicStatusLabel(snapshot.Enabled);
        _enableButton.Text = snapshot.Enabled ? "关闭" : "开启";

        RefreshModuleSelector(snapshot, reloadModules: false);
        _statusForm.ApplySnapshot(snapshot);
        WriteSnapshotLog(snapshot);
    }

    private void RefreshModuleSelector(RenderSnapshot? snapshot, bool reloadModules)
    {
        if (_moduleComboBox is null)
        {
            return;
        }

        if (reloadModules)
        {
            _moduleStore.Reload();
        }

        var hasValidState = snapshot?.State?.GetBool("有效性") == true;
        var (classId, specId, partyType, heroTalent, filterText) = GetModuleFilter(snapshot, hasValidState);
        var modules = !hasValidState
            ? _moduleStore.GetModules()
            : _moduleStore.FindMatches(classId, specId, partyType, heroTalent);
        var signature = BuildModuleSelectorSignature(
            hasValidState,
            classId,
            specId,
            partyType,
            heroTalent,
            modules);
        if (!reloadModules && signature == _lastModuleSelectorSignature)
        {
            return;
        }

        _lastModuleSelectorSignature = signature;

        _suppressModuleSelectionChanged = true;
        try
        {
            _moduleComboBox.BeginUpdate();
            try
            {
                _moduleComboBox.Items.Clear();
                _moduleComboBox.Items.Add(ModuleSelectionOption.Auto);
                foreach (var module in modules)
                {
                    _moduleComboBox.Items.Add(new ModuleSelectionOption(module.Id, ModuleDisplay.FormatListItem(module)));
                }

                var selectedIndex = 0;
                var selectedModuleVisible = string.IsNullOrWhiteSpace(_selectedModuleId);
                if (!string.IsNullOrWhiteSpace(_selectedModuleId))
                {
                    for (var i = 1; i < _moduleComboBox.Items.Count; i++)
                    {
                        if (_moduleComboBox.Items[i] is ModuleSelectionOption option
                            && string.Equals(option.ModuleId, _selectedModuleId, StringComparison.OrdinalIgnoreCase))
                        {
                            selectedIndex = i;
                            selectedModuleVisible = true;
                            break;
                        }
                    }
                }

                _moduleComboBox.SelectedIndex = selectedIndex;
                _moduleCountLabel.Text = selectedModuleVisible
                    ? $"可选模块: {modules.Count}"
                    : $"可选模块: {modules.Count}，已选模块不符合当前筛选";
            }
            finally
            {
                _moduleComboBox.EndUpdate();
            }
        }
        finally
        {
            _suppressModuleSelectionChanged = false;
        }

        _moduleFilterLabel.Text = filterText;
    }

    private string BuildModuleSelectorSignature(
        bool hasValidState,
        int? classId,
        int? specId,
        int? partyType,
        int? heroTalent,
        IReadOnlyList<ModuleDefinition> modules)
    {
        var moduleText = string.Join("|", modules.Select(module => $"{module.Id}:{module.Name}:{ModuleDisplay.FormatMatch(module.Match)}"));
        return $"{hasValidState}:{classId}:{specId}:{partyType}:{heroTalent}:{_selectedModuleId}:{moduleText}";
    }

    private static (int? ClassId, int? SpecId, int? PartyType, int? HeroTalent, string Text) GetModuleFilter(
        RenderSnapshot? snapshot,
        bool hasValidState)
    {
        if (!hasValidState || snapshot?.State is null)
        {
            return (null, null, null, null, "筛选: 等待游戏状态，暂时显示全部模块");
        }

        var partyType = snapshot.State.GetInt("队伍类型");
        var heroTalent = snapshot.State.GetInt("英雄天赋");
        return (
            snapshot.ClassId,
            snapshot.SpecId,
            partyType,
            heroTalent,
            $"筛选: {ModuleDisplay.FormatState(snapshot)}");
    }

    private async void HandleModuleSelectionChanged(object? sender, EventArgs e)
    {
        if (_suppressModuleSelectionChanged)
        {
            return;
        }

        _selectedModuleId = _moduleComboBox.SelectedItem is ModuleSelectionOption option
            ? option.ModuleId
            : null;
        SaveUiCache();
        AppendLog($"模块选择: {(_selectedModuleId is null ? "自动选择" : _moduleComboBox.Text)}");
        await RestartRuntimeAfterSettingChangeAsync();
    }

    private async Task RestartRuntimeAfterSettingChangeAsync()
    {
        var options = BuildOptions();
        if (_runtime is not null && options == _runtime.Options)
        {
            return;
        }

        AppendLog("设置已变更, 重新启动运行");
        await StopRuntimeAsync();
        StartRuntime();
    }

    private void WriteSnapshotLog(RenderSnapshot snapshot)
    {
        var classSpec = snapshot.ClassName is null ? null : $"{snapshot.ClassName} / {snapshot.SpecName ?? "-"}";
        if (!string.IsNullOrWhiteSpace(classSpec) && classSpec != _lastLoggedClass)
        {
            _lastLoggedClass = classSpec;
            AppendLog($"识别职业: {classSpec}");
        }

        if (_lastLoggedEnabled != snapshot.Enabled)
        {
            _lastLoggedEnabled = snapshot.Enabled;
            AppendLog(snapshot.Enabled ? "逻辑已开启" : "逻辑已关闭");
        }

        if (snapshot.ModuleName != _lastLoggedModule)
        {
            _lastLoggedModule = snapshot.ModuleName;
            if (!string.IsNullOrWhiteSpace(snapshot.ModuleName))
            {
                AppendLog($"匹配模块: {snapshot.ModuleName}");
            }
        }

        if (!string.IsNullOrWhiteSpace(snapshot.CurrentStep))
        {
            var target = GetActionTarget(snapshot);
            if (snapshot.CurrentStep != _lastLoggedStep || target != _lastLoggedStepTarget)
            {
                _lastLoggedStep = snapshot.CurrentStep;
                _lastLoggedStepTarget = target;
                var targetText = string.IsNullOrWhiteSpace(target) ? string.Empty : $"，目标: {target}";
                AppendLog($"步骤: {snapshot.CurrentStep}{targetText}");
            }
        }
    }

    private static string? GetActionTarget(RenderSnapshot snapshot)
    {
        if (!snapshot.UnitInfo.TryGetValue("动作单位", out var value))
        {
            return null;
        }

        var text = UiTheme.FormatValue(value);
        return string.IsNullOrWhiteSpace(text) || text == "-" ? null : text;
    }

    private void SetRuntimeControls(bool running)
    {
        if (!running)
        {
            UpdateHeaderIconColor(null);
            UpdateLogicStatusLabel(enabled: false);
        }

        _enableButton.Enabled = running;
    }

    private void UpdateLogicStatusLabel(bool enabled)
    {
        _runtimeStatusLabel.Text = string.Empty;
        _titleLabel.ForeColor = enabled ? UiTheme.Accent : UiTheme.Text;
    }

    private void PostToUi(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
                // Form is closing.
            }

            return;
        }

        action();
    }

    private void AppendLog(string message)
    {
        _statusForm.AppendLog(message);
    }

    private void BeginCaptureToggleKey()
    {
        ShowSettingsView();

        if (_isCapturingToggleKey)
        {
            return;
        }

        _isCapturingToggleKey = true;
        _toggleKeyButton.Text = "请按任意键...";
        ActiveControl = null;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_isCapturingToggleKey)
        {
            return TryHandleCapturedKey(keyData & Keys.KeyCode);
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private bool TryHandleCapturedKey(Keys key)
    {
        if (key is Keys.Escape)
        {
            _isCapturingToggleKey = false;
            SetToggleKeyButtonText();
            AppendLog("已取消按键录入");
            return true;
        }

        if (IsUnsupportedToggleKey(key.ToString()))
        {
            _toggleKeyButton.Text = "ALT 不支持";
            AppendLog("触发键不支持 ALT, 请重试");
            _ = ResetCaptureButtonTextAsync();
            _isCapturingToggleKey = false;
            return true;
        }

        var keyName = TryMapKeyToHotkey(key);
        if (keyName is null)
        {
            _toggleKeyButton.Text = "不支持";
            AppendLog("该按键暂不支持, 请重试");
            _ = ResetCaptureButtonTextAsync();
            _isCapturingToggleKey = false;
            return true;
        }

        _isCapturingToggleKey = false;
        _toggleKeyName = keyName;
        SetToggleKeyButtonText();
        SaveUiCache();
        AppendLog($"已录入触发键: {_toggleKeyName}");
        HandleSettingCommitted(this, EventArgs.Empty);
        return true;
    }

    public bool PreFilterMessage(ref Message m)
    {
        if (!_isCapturingToggleKey)
        {
            return false;
        }

        const int WmXButtonDown = 0x020B;
        const int WmKeyDown = 0x0100;
        const int WmSysKeyDown = 0x0104;
        if (m.Msg is WmKeyDown or WmSysKeyDown)
        {
            return TryHandleCapturedKey((Keys)(int)m.WParam);
        }

        if (m.Msg != WmXButtonDown)
        {
            return false;
        }

        var xButton = (((int)m.WParam) >> 16) & 0xFFFF;
        var keyName = xButton switch
        {
            1 => "XBUTTON1",
            2 => "XBUTTON2",
            _ => null
        };

        if (keyName is null)
        {
            return false;
        }

        _isCapturingToggleKey = false;
        _toggleKeyName = keyName;
        SetToggleKeyButtonText();
        SaveUiCache();
        AppendLog($"已录入触发键: {_toggleKeyName}");
        HandleSettingCommitted(this, EventArgs.Empty);
        return true;
    }

    private void ApplyCachedWindowState()
    {
        if (_uiCache.MainWindowBounds is { } mainBounds)
        {
            var restoredBounds = new Rectangle(
                mainBounds.X,
                mainBounds.Y,
                Math.Max(MinimumSize.Width, mainBounds.Width),
                Math.Max(MinimumSize.Height, mainBounds.Height));
            if (UiCacheStore.IsBoundsVisible(restoredBounds))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = restoredBounds;
            }
        }
        else if (_uiCache.MainWindowLocation is { } mainLocation)
        {
            var restoredBounds = new Rectangle(mainLocation.X, mainLocation.Y, Width, Height);
            if (UiCacheStore.IsBoundsVisible(restoredBounds))
            {
                StartPosition = FormStartPosition.Manual;
                Location = new Point(mainLocation.X, mainLocation.Y);
            }
        }

        _statusForm.ApplyCachedBounds(_uiCache.SettingsWindowBounds);
    }

    private void SaveUiCache()
    {
        var latestCache = UiCacheStore.Load();
        _uiCache.ModuleRulesGridColumns = latestCache.ModuleRulesGridColumns;

        _uiCache.MainWindowBounds = new WindowBounds
        {
            X = Left,
            Y = Top,
            Width = Width,
            Height = Height
        };
        _uiCache.MainWindowLocation = new WindowLocation
        {
            X = Left,
            Y = Top
        };

        if (_statusForm.HasKnownBounds)
        {
            _uiCache.SettingsWindowBounds = _statusForm.GetCachedBounds();
        }

        _uiCache.ToggleKey = _toggleKeyName;
        _uiCache.SelectedModuleId = _selectedModuleId;
        UiCacheStore.Save(_uiCache);
    }

    private void ShowSettingsView()
    {
        RefreshModuleSelector(_lastSnapshot, reloadModules: true);
        _statusForm.ShowSettings(_lastSnapshot);
    }

    private async Task ResetCaptureButtonTextAsync()
    {
        await Task.Delay(1000);
        if (!IsDisposed)
        {
            PostToUi(SetToggleKeyButtonText);
        }
    }

    private void CancelToggleKeyCapture()
    {
        if (!_isCapturingToggleKey)
        {
            return;
        }

        _isCapturingToggleKey = false;
        SetToggleKeyButtonText();
    }

    private void SetToggleKeyButtonText()
    {
        _toggleKeyButton.Text = _toggleKeyName;
    }

    private nint HitTestResizeGrip(Point clientPoint)
    {
        var left = clientPoint.X <= ResizeGripSize;
        var right = clientPoint.X >= ClientSize.Width - ResizeGripSize;
        var top = clientPoint.Y <= ResizeGripSize;
        var bottom = clientPoint.Y >= ClientSize.Height - ResizeGripSize;

        if (top && left)
        {
            return NativeMethods.HtTopLeft;
        }

        if (top && right)
        {
            return NativeMethods.HtTopRight;
        }

        if (bottom && left)
        {
            return NativeMethods.HtBottomLeft;
        }

        if (bottom && right)
        {
            return NativeMethods.HtBottomRight;
        }

        if (left)
        {
            return NativeMethods.HtLeft;
        }

        if (right)
        {
            return NativeMethods.HtRight;
        }

        if (top)
        {
            return NativeMethods.HtTop;
        }

        if (bottom)
        {
            return NativeMethods.HtBottom;
        }

        return NativeMethods.HtClient;
    }

    private static string? TryMapKeyToHotkey(Keys key)
    {
        var keyName = key.ToString().ToUpperInvariant();
        if (IsUnsupportedToggleKey(keyName))
        {
            return null;
        }

        if (key is >= Keys.D0 and <= Keys.D9)
        {
            return ((char)('0' + (key - Keys.D0))).ToString();
        }

        if (key is >= Keys.NumPad0 and <= Keys.NumPad9)
        {
            return $"NUMPAD{key - Keys.NumPad0}";
        }

        return keyName switch
        {
            "OEMCOMMA" => ",",
            "OEMPERIOD" => ".",
            "OEMQUESTION" => "/",
            "OEMSEMICOLON" => ";",
            "OEMQUOTES" => "'",
            "OEMOPENBRACKETS" => "[",
            "OEMCLOSEBRACKETS" => "]",
            "OEMPLUS" => "=",
            "OEMMINUS" => "-",
            "OEMTILDE" => "`",
            "OEMBACKSLASH" => "\\",
            "DECIMAL" => "NUMPADDECIMAL",
            "ADD" => "NUMPADPLUS",
            "SUBTRACT" => "NUMPADMINUS",
            "MULTIPLY" => "NUMPADMULTIPLY",
            "DIVIDE" => "NUMPADDIVIDE",
            _ => KeySender.GetVk(keyName) is not null ? keyName : null
        };
    }

    private static bool IsUnsupportedToggleKey(string keyName)
    {
        var key = keyName.Trim().ToUpperInvariant();
        return key is "ALT" or "MENU" or "LMENU" or "RMENU";
    }

    private static Label CreateInfoLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Margin = new Padding(0, 0, 0, 4)
        };
    }

    private static string GetWindowTitle()
    {
        var randomizedName = Environment.GetEnvironmentVariable(AppPaths.RandomizedDisplayNameEnvironmentKey);
        return string.IsNullOrWhiteSpace(randomizedName) ? "Shigure" : randomizedName;
    }

    private void EnableDrag(Control control)
    {
        control.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessageW(Handle, NativeMethods.WmNcLButtonDown, NativeMethods.HtCaption, 0);
            }
        };
    }

    private static void ConfigureTopBarButton(Button button)
    {
        button.AutoSize = false;
        button.Size = new Size(88, 36);
        button.Padding = new Padding(4, 1, 4, 1);
    }

    private sealed record ModuleSelectionOption(string? ModuleId, string Text)
    {
        public static readonly ModuleSelectionOption Auto = new(null, "自动选择（最匹配）");

        public override string ToString()
        {
            return Text;
        }
    }

    private static Icon? LoadApplicationIcon()
    {
        try
        {
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
            return null;
        }
    }

    private void TryApplyApplicationIcon()
    {
        var icon = LoadApplicationIcon();
        if (icon != null)
        {
            Icon = icon;
        }
    }

    private static string ModeLabel(SendMode mode)
    {
        return mode switch
        {
            SendMode.Click => "单击",
            SendMode.Hold => "按住",
            _ => "开关"
        };
    }
}
