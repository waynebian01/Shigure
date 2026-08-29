using System.Drawing;

namespace Shigure;

public sealed class MainForm : Form, IMessageFilter
{
    private enum MainWindowLayout
    {
        Horizontal,
        Vertical
    }

    private enum CloseButtonBehavior
    {
        MinimizeToTray,
        Exit
    }

    private const int ResizeGripSize = 8;
    private const int RoundedCornerResizeDebounceMs = 80;
    private const int WowProcessMonitorIntervalMs = 10_000;
    private const string HeaderIconResourcePath = "Assets.arasaka-icon-transparent.png";
    private const string ModuleWebsiteUrl = "https://www.shigure.club";
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
    private UiDropDown _modeComboBox = null!;
    private UiDropDown _moduleComboBox = null!;
    private Label _moduleFilterLabel = null!;
    private Label _moduleCountLabel = null!;
    private Label _configSourceLabel = null!;
    private Button _updateConfigButton = null!;
    private Label _spellIconPackageStatusLabel = null!;
    private Button _downloadSpellIconPackageButton = null!;
    private readonly ToolTip _settingsToolTip = new();
    private Button _horizontalLayoutButton = null!;
    private Button _verticalLayoutButton = null!;
    private Button _minimizeToTrayButton = null!;
    private Button _exitOnCloseButton = null!;
    private NotifyIcon _trayIcon = null!;
    private ContextMenuStrip _trayMenu = null!;
    private ToolStripMenuItem _trayToggleMenuItem = null!;
    private Icon? _trayDefaultIcon;
    private Icon? _trayEnabledIcon;
    private bool? _trayIconShowsEnabled;
    private string _toggleKeyName = "XBUTTON2";
    private string? _selectedModuleId;
    private bool _isCapturingToggleKey;
    private bool _suppressModuleSelectionChanged;
    private string? _lastModuleSelectorSignature;
    private bool _usesDwmRoundedCorners = true;

    private readonly List<Button> _enableButtons = [];
    private Button _verticalEnableButton = null!;
    private readonly List<PictureBox> _headerIcons = [];
    private readonly List<Label> _titleLabels = [];
    private readonly List<Label> _runtimeStatusLabels = [];
    private Control _horizontalTopBar = null!;
    private Control _verticalTopBar = null!;
    private MainWindowLayout _mainWindowLayout = MainWindowLayout.Horizontal;
    private CloseButtonBehavior _closeButtonBehavior = CloseButtonBehavior.MinimizeToTray;
    private Bitmap? _headerIconMask;
    private Color? _currentHeaderIconColor;

    private readonly StatusForm _statusForm;
    private readonly string _baseDirectory;
    private readonly ModuleStore _moduleStore;
    private readonly ITriggerKeyState _triggerKeyState;
    private readonly WowProcessLocator _processLocator;
    private readonly FuyutsuiAddonSyncService _addonSyncService;
    private readonly ModuleDependencyService _moduleDependencyService;
    private readonly RuntimeSessionCoordinator _runtimeSession;
    private readonly ModuleEditorControl _moduleEditor;
    private readonly ClassConfigEditorControl _classConfigEditor;
    private readonly ClassMacrosEditorControl _classMacrosEditor;
    private readonly AppOptions _initialOptions;
    private readonly UiCacheState _uiCache;
    private readonly System.Windows.Forms.Timer _roundedCornerResizeTimer;
    private readonly System.Windows.Forms.Timer _wowProcessMonitorTimer;
    private RenderSnapshot? _lastSnapshot;
    private string? _lastLoggedStep;
    private string? _lastLoggedStepDetails;
    private string? _lastLoggedScanFailureReason;
    private string? _lastLoggedClass;
    private string? _lastLoggedModule;
    private bool? _lastLoggedEnabled;
    private readonly object _configUpdateSync = new();
    private readonly SemaphoreSlim _moduleImportGate = new(1, 1);
    private readonly SpellIconPackageDownloadService _spellIconPackageDownloadService = new();
    private Task _configUpdateTail = Task.CompletedTask;
    private Task _spellIconPackageDownloadTask = Task.CompletedTask;
    private CancellationTokenSource? _spellIconPackageDownloadCts;
    private long _runtimeRequestVersion;
    private bool _exitRequested;
    private bool _shutdownStarted;
    private bool _shutdownCompleted;
    private bool _wasWowProcessWindowAvailable;

    private sealed record ProjectConfigUpdateResult(
        FuyutsuiConfigConverter.UpdateResult Config,
        FuyutsuiKeymapConverter.UpdateResult? Keymap,
        FuyutsuiAddonSyncResult AddonSync);

    internal MainForm(
        AppOptions initialOptions,
        string baseDirectory,
        ModuleStore moduleStore,
        ITriggerKeyState triggerKeyState,
        WowProcessLocator processLocator,
        RuntimeSessionCoordinator runtimeSession)
    {
        _initialOptions = initialOptions;
        _baseDirectory = baseDirectory;
        _moduleStore = moduleStore;
        _triggerKeyState = triggerKeyState;
        _processLocator = processLocator;
        var localAddonRoot = Path.Combine(_baseDirectory, "Fuyutsui");
        _addonSyncService = new FuyutsuiAddonSyncService(localAddonRoot, _processLocator);
        _moduleDependencyService = new ModuleDependencyService(_baseDirectory);
        _runtimeSession = runtimeSession;
        _uiCache = UiCacheStore.Load();
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
        _wasWowProcessWindowAvailable = _processLocator.FindFrontmostWindow() != 0;
        _wowProcessMonitorTimer = new System.Windows.Forms.Timer
        {
            Interval = WowProcessMonitorIntervalMs
        };
        _wowProcessMonitorTimer.Tick += HandleWowProcessMonitorTick;
        _wowProcessMonitorTimer.Start();
        Application.AddMessageFilter(this);
        InitializeComponent();
        TryApplyApplicationIcon();
        InitializeTrayIcon();
        _statusForm.AttachSettingsPanel(BuildSettingsPanel());
        _moduleEditor = new ModuleEditorControl(
            _moduleStore,
            RestartRuntimeFromEditorAsync,
            _moduleDependencyService.Capture,
            ReloadModulesWithDependenciesAsync,
            _baseDirectory);
        _statusForm.AttachModuleEditor(_moduleEditor);
        _classConfigEditor = new ClassConfigEditorControl(
            () => Path.Combine(_addonSyncService.SourceRoot, "class"),
            UpdateConfigAfterSaveAsync);
        _statusForm.AttachConfigEditor(_classConfigEditor);
        _classConfigEditor.DirtyStateChanged += dirty => _statusForm.SetPageDirty(SettingsPage.Config, dirty);
        _classMacrosEditor = new ClassMacrosEditorControl(
            () => Path.Combine(_addonSyncService.SourceRoot, "core", "classmacros.lua"),
            UpdateConfigAfterSaveAsync);
        _statusForm.AttachMacrosEditor(_classMacrosEditor);
        _classMacrosEditor.DirtyStateChanged += dirty => _statusForm.SetPageDirty(SettingsPage.Macros, dirty);
        _statusForm.FormClosing += (_, _) =>
        {
            CancelToggleKeyCapture();
            SaveUiCache();
        };
        ApplyCachedWindowState();
        ApplyInitialOptions();
        WireSettingEvents();
        _runtimeSession.SnapshotUpdated += HandleSnapshotUpdated;
        _runtimeSession.RuntimeFailed += HandleRuntimeFailed;
        _runtimeSession.RuntimeStopped += HandleRuntimeStopped;
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

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var runtimeDataGenerated = await GenerateRuntimeDataAtStartupIfMissingAsync();
        var dependenciesUpdated = await ImportModuleDependenciesAsync(reloadStore: true, showFeedback: true);
        if (!dependenciesUpdated && !runtimeDataGenerated)
        {
            await SynchronizeAddonAtStartupAsync();
        }
        await StartRuntimeAsync();
    }

    private async Task<bool> GenerateRuntimeDataAtStartupIfMissingAsync()
    {
        var configDirectory = Path.Combine(_baseDirectory, ConfigService.ConfigDirectoryName);
        var keymapDirectory = Path.Combine(_baseDirectory, "keymap");
        var hasAllConfigFiles = Directory.Exists(configDirectory)
            && File.Exists(Path.Combine(configDirectory, ConfigService.CommonConfigFileName))
            && ClassNames.GetClasses().All(item =>
                File.Exists(Path.Combine(configDirectory, $"{ClassNames.GetConfigFileName(item.Id)}.json")));
        var hasAllKeymapFiles = Directory.Exists(keymapDirectory)
            && ClassNames.GetClasses().All(item =>
                File.Exists(Path.Combine(
                    keymapDirectory,
                    $"{ClassNames.GetConfigFileName(item.Id).ToLowerInvariant()}.json")));
        if (hasAllConfigFiles && hasAllKeymapFiles)
        {
            return false;
        }

        AppendLog("检测到 config 或 keymap 缺失或不完整，正在从项目 Fuyutsui 自动生成");
        try
        {
            var result = await QueueProjectConfigUpdateAsync(savedAddonFilePath: null);
            AppendLog(
                $"已自动生成运行配置: config {result.Config.UpdatedFiles.Count} 个文件，" +
                $"keymap {result.Keymap?.UpdatedFiles.Count ?? 0} 个文件");
            return true;
        }
        catch (Exception ex)
        {
            AppendLog($"自动生成 config/keymap 失败，程序将继续启动: {ex.Message}");
            return false;
        }
    }

    private Task ReloadModulesWithDependenciesAsync()
        => ImportModuleDependenciesAsync(reloadStore: true, showFeedback: true);

    private async Task<bool> ImportModuleDependenciesAsync(bool reloadStore, bool showFeedback)
    {
        await _moduleImportGate.WaitAsync();
        try
        {
            return await ImportModuleDependenciesCoreAsync(reloadStore, showFeedback);
        }
        finally
        {
            _moduleImportGate.Release();
        }
    }

    private async Task<bool> ImportModuleDependenciesCoreAsync(bool reloadStore, bool showFeedback)
    {
        if (_classConfigEditor.HasUnsavedChanges || _classMacrosEditor.HasUnsavedChanges)
        {
            if (showFeedback)
            {
                MessageBox.Show(
                    "配置或宏页面存在未保存修改。请先保存或放弃修改，再刷新模块。",
                    "模块依赖未导入",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            return false;
        }

        if (reloadStore)
        {
            _moduleStore.Reload();
        }

        ModuleDependencyImportResult result;
        try
        {
            // 合并阶段保持在 UI 线程，避免配置/宏编辑器在检查脏状态后又并发写同一 Lua。
            result = _moduleDependencyService.Import(_moduleStore.GetModules());
        }
        catch (Exception ex)
        {
            AppendLog($"模块依赖导入失败: {ex.Message}");
            if (showFeedback)
            {
                MessageBox.Show(ex.Message, "模块依赖导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return false;
        }

        _moduleStore.SetImportIssues(
            result.Rejected.Select(item => item.ModuleId),
            result.ConflictedModuleIds);
        _moduleEditor.ReloadModulesFromStore(reloadStore: false);
        RefreshModuleSelector(_lastSnapshot, forceRefresh: false);

        foreach (var rejected in result.Rejected)
        {
            AppendLog($"模块“{rejected.ModuleName}”未导入: {rejected.Reason}");
        }
        foreach (var conflict in result.Conflicts.Take(50))
        {
            AppendLog($"模块依赖冲突: {conflict}");
        }

        string? postUpdateError = null;
        if (result.HasChanges)
        {
            AppendLog(
                $"已从模块补充本地依赖: 配置新增 {result.ConfigAdded} 项、整理 {result.ConfigUpdated} 项，宏 {result.MacrosAdded} 项；模块 {string.Join("、", result.ChangedModules)}");
            _classConfigEditor.ReloadFromAddon();
            _classMacrosEditor.ReloadFromAddon();
            try
            {
                await QueueProjectConfigUpdateAsync(savedAddonFilePath: null);
            }
            catch (Exception ex)
            {
                postUpdateError = ex.Message;
                AppendLog($"模块依赖已写入，但后续配置更新失败: {ex.Message}");
            }
        }

        if (showFeedback && (result.HasChanges || result.Rejected.Count > 0 || result.Conflicts.Count > 0))
        {
            var lines = new List<string>();
            if (result.HasChanges)
            {
                lines.Add($"成功处理配置：新增 {result.ConfigAdded} 项、整理 {result.ConfigUpdated} 项；宏 {result.MacrosAdded} 项。");
            }
            if (result.Rejected.Count > 0)
            {
                lines.Add("未导入模块：");
                lines.AddRange(result.Rejected.Select(item => $"- {item.ModuleName}: {item.Reason}"));
            }
            if (result.Conflicts.Count > 0)
            {
                lines.Add($"发现 {result.Conflicts.Count} 项冲突，均已保留本地内容；详情见日志。");
            }
            if (!string.IsNullOrWhiteSpace(postUpdateError))
            {
                lines.Add($"本地依赖已写入，但 config/keymap 或游戏同步更新失败：{postUpdateError}");
            }
            var hasWarning = result.Rejected.Count > 0 || postUpdateError is not null;
            MessageBox.Show(
                string.Join(Environment.NewLine, lines),
                hasWarning ? "模块导入完成（有警告）" : "模块导入完成",
                MessageBoxButtons.OK,
                hasWarning ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        return result.HasChanges;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_shutdownCompleted
            && !_exitRequested
            && e.CloseReason == CloseReason.UserClosing
            && _closeButtonBehavior == CloseButtonBehavior.MinimizeToTray)
        {
            e.Cancel = true;
            MinimizeToTray();
            base.OnFormClosing(e);
            return;
        }

        if (!_shutdownCompleted)
        {
            e.Cancel = true;
            if (!_shutdownStarted)
            {
                _shutdownStarted = true;
                SaveUiCache();
                _roundedCornerResizeTimer.Stop();
                _wowProcessMonitorTimer.Stop();
                _spellIconPackageDownloadCts?.Cancel();
                Application.RemoveMessageFilter(this);
                _ = CompleteShutdownAsync();
            }

            base.OnFormClosing(e);
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayMenu.Dispose();
        _trayDefaultIcon?.Dispose();
        _trayEnabledIcon?.Dispose();
        _roundedCornerResizeTimer.Dispose();
        _wowProcessMonitorTimer.Dispose();
        base.OnFormClosed(e);
    }

    private async void HandleWowProcessMonitorTick(object? sender, EventArgs e)
    {
        var isAvailable = _processLocator.FindFrontmostWindow() != 0;
        var justOpened = !_wasWowProcessWindowAvailable && isAvailable;
        _wasWowProcessWindowAvailable = isAvailable;

        if (!justOpened || _shutdownStarted)
        {
            return;
        }

        AppendLog("检测到目标游戏进程已打开，正在自动更新配置");
        try
        {
            await QueueProjectConfigUpdateAsync(savedAddonFilePath: null);
            if (!_shutdownStarted)
            {
                AppendLog("目标游戏进程启动后的配置更新已完成");
            }
        }
        catch (OperationCanceledException) when (_shutdownStarted)
        {
            // 关闭流程会等待队列收尾，无需再记录失败。
        }
        catch (Exception ex)
        {
            if (!_shutdownStarted)
            {
                AppendLog($"目标游戏进程启动后的配置更新失败: {ex.Message}");
            }
        }
    }

    private async Task CompleteShutdownAsync()
    {
        _runtimeSession.SnapshotUpdated -= HandleSnapshotUpdated;
        _runtimeSession.RuntimeFailed -= HandleRuntimeFailed;
        _runtimeSession.RuntimeStopped -= HandleRuntimeStopped;

        try
        {
            var runtimeShutdown = _runtimeSession.DisposeAsync().AsTask();
            await Task.WhenAll(
                runtimeShutdown,
                GetPendingConfigUpdateTask(),
                _spellIconPackageDownloadTask);
        }
        catch (Exception ex)
        {
            AppendLog($"停止运行失败: {ex.Message}");
        }
        finally
        {
            _statusForm.Dispose();
            _spellIconPackageDownloadCts?.Dispose();
            _spellIconPackageDownloadService.Dispose();
            _shutdownCompleted = true;
            if (!IsDisposed)
            {
                Close();
            }
        }
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

        Text = "Shigure";

        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ClientSize = new Size(680, 64);
        MinimumSize = new Size(420, 56);
        BackColor = Color.FromArgb(18, 21, 26);
        ForeColor = UiTheme.Text;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var root = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(12),
            Margin = new Padding(0)
        };
        Controls.Add(root);

        root.Controls.Add(BuildTopBars());

        ResumeLayout(false);
    }

    private Control BuildTopBars()
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };

        _horizontalTopBar = BuildHorizontalTopBar();
        _verticalTopBar = BuildVerticalTopBar();
        _verticalTopBar.Visible = false;
        host.Controls.Add(_horizontalTopBar);
        host.Controls.Add(_verticalTopBar);

        _currentHeaderIconColor = null;
        UpdateHeaderIconColor(null);
        return host;
    }

    private Control BuildHorizontalTopBar()
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
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

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

        var headerIcon = CreateHeaderIcon();
        var titleLabel = new Label
        {
            Text = "Shigure",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font(Font.FontFamily, 13F, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Margin = new Padding(8, 0, 0, 0)
        };
        var runtimeStatusLabel = new Label
        {
            Text = string.Empty,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UiTheme.Muted
        };

        brand.Controls.Add(headerIcon);
        brand.Controls.Add(titleLabel);
        var buttons = BuildTopBarButtons(vertical: false);

        RegisterTopBarPresentation(headerIcon, titleLabel, runtimeStatusLabel);
        EnableDrag(bar);
        EnableDrag(brand);
        EnableDrag(headerIcon);
        EnableDrag(titleLabel);
        EnableDrag(runtimeStatusLabel);

        bar.Controls.Add(brand, 0, 0);
        bar.Controls.Add(runtimeStatusLabel, 1, 0);
        bar.Controls.Add(buttons, 2, 0);
        return bar;
    }

    private Control BuildVerticalTopBar()
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bar.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var brand = new FlowLayoutPanel
        {
            AutoSize = true,
            Anchor = AnchorStyles.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        var headerIcon = CreateHeaderIcon();
        headerIcon.Anchor = AnchorStyles.Top;
        var titleFont = new Font(Font.FontFamily, 13F, FontStyle.Bold);
        // 旋转后由 GDI+ 绘制文字，保留 GDI 的默认字形外延并额外留出少量边距，
        // 避免末尾字符因两套文字测量方式的差异被裁掉。
        var titleSize = TextRenderer.MeasureText("Shigure", titleFont);
        var titleLabel = new RotatableLabel
        {
            Text = "Shigure",
            AutoSize = false,
            Size = new Size(titleSize.Width + 4, 32),
            Anchor = AnchorStyles.Top,
            Font = titleFont,
            ForeColor = UiTheme.Text,
            Margin = new Padding(0, 8, 0, 0)
        };
        titleLabel.Rotated = true;
        var runtimeStatusLabel = new RotatableLabel
        {
            Text = string.Empty,
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted
        };
        runtimeStatusLabel.Rotated = true;

        brand.Controls.Add(headerIcon);
        brand.Controls.Add(titleLabel);
        var buttons = BuildTopBarButtons(vertical: true);

        RegisterTopBarPresentation(headerIcon, titleLabel, runtimeStatusLabel);
        EnableDrag(bar);
        EnableDrag(brand);
        EnableDrag(headerIcon);
        EnableDrag(titleLabel);
        EnableDrag(runtimeStatusLabel);

        bar.Controls.Add(brand, 0, 0);
        bar.Controls.Add(runtimeStatusLabel, 0, 1);
        bar.Controls.Add(buttons, 0, 2);
        return bar;
    }

    private FlowLayoutPanel BuildTopBarButtons(bool vertical)
    {
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Anchor = vertical ? AnchorStyles.Bottom : AnchorStyles.Right,
            FlowDirection = vertical ? FlowDirection.TopDown : FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };

        var enableButton = CreateTopBarButton(vertical ? "开\r\n启" : "开关", UiTheme.Field, UiTheme.Text, vertical);
        enableButton.Click += (_, _) => ToggleEnabled();
        var settingsButton = CreateTopBarButton(vertical ? "设\r\n置" : "设置", UiTheme.Field, UiTheme.Text, vertical);
        settingsButton.Click += (_, _) => ShowSettingsView();
        var closeButton = CreateTopBarButton(vertical ? "X" : "✕", UiTheme.Field, UiTheme.Muted, vertical);
        closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(196, 43, 28);
        closeButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(153, 27, 21);
        closeButton.Click += (_, _) => Close();

        _enableButtons.Add(enableButton);
        if (vertical)
        {
            _verticalEnableButton = enableButton;
        }
        buttons.Controls.AddRange([enableButton, settingsButton, closeButton]);
        return buttons;
    }

    private void RegisterTopBarPresentation(PictureBox icon, Label title, Label status)
    {
        _headerIcons.Add(icon);
        _titleLabels.Add(title);
        _runtimeStatusLabels.Add(status);
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

        foreach (var headerIcon in _headerIcons)
        {
            var previous = headerIcon.Image;
            headerIcon.Image = TintHeaderIcon(_headerIconMask, color);
            previous?.Dispose();
        }
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
        var scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            AutoScroll = true,
            Margin = new Padding(0)
        };
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = UiTheme.Surface,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        const int settingsCardHeight = 190;
        const int settingsCardGap = UiTheme.PageGap;
        const int settingsActionButtonHeight = UiTheme.ActionButtonHeight;
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, settingsCardHeight + settingsCardGap));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, settingsCardHeight + settingsCardGap));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, settingsCardHeight + settingsCardGap));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, settingsCardHeight));

        Label CreateTitle(string text) => UiTheme.CreateSectionTitle(Font, text);
        Label CreateDescription(string text) => UiTheme.CreateDescription(text);
        Label CreateSettingLabel(string text) => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UiTheme.Muted,
            Margin = new Padding(0)
        };

        var inputCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(UiTheme.CardPadding),
            Margin = new Padding(0, 0, settingsCardGap / 2, settingsCardGap)
        };
        inputCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        inputCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inputCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        inputCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        inputCard.RowStyles.Add(new RowStyle(SizeType.Absolute, settingsActionButtonHeight + 8));
        inputCard.RowStyles.Add(new RowStyle(SizeType.Absolute, settingsActionButtonHeight + 8));
        inputCard.Controls.Add(CreateTitle("输入与运行"), 0, 0);
        inputCard.SetColumnSpan(inputCard.GetControlFromPosition(0, 0)!, 2);
        inputCard.Controls.Add(CreateDescription("设置触发方式；修改后运行循环会自动重启"), 0, 1);
        inputCard.SetColumnSpan(inputCard.GetControlFromPosition(0, 1)!, 2);

        _toggleKeyButton = UiTheme.CreateButton("XBUTTON2", UiTheme.ButtonKind.Secondary);
        _toggleKeyButton.AutoSize = false;
        _toggleKeyButton.Size = new Size(190, settingsActionButtonHeight);
        _toggleKeyButton.TextAlign = ContentAlignment.MiddleCenter;
        _toggleKeyButton.Anchor = AnchorStyles.Left;
        _toggleKeyButton.Margin = new Padding(0);
        _toggleKeyButton.Click += (_, _) => BeginCaptureToggleKey();
        _settingsToolTip.SetToolTip(_toggleKeyButton, "点击后按下新的键盘键或鼠标侧键");
        inputCard.Controls.Add(CreateSettingLabel("触发键"), 0, 2);
        inputCard.Controls.Add(_toggleKeyButton, 1, 2);

        _modeComboBox = new UiDropDown();
        UiTheme.StyleComboBox(_modeComboBox);
        _modeComboBox.Items.AddRange(new object[] { "开关", "单击", "按住" });
        _modeComboBox.SelectedIndex = 0;
        _modeComboBox.Width = 190;
        _modeComboBox.Anchor = AnchorStyles.Left;
        _modeComboBox.Margin = new Padding(0);
        _settingsToolTip.SetToolTip(_modeComboBox, "开关：按一次切换；单击：每次触发发送一次；按住：持续按下时运行");
        inputCard.Controls.Add(CreateSettingLabel("发送模式"), 0, 3);
        inputCard.Controls.Add(_modeComboBox, 1, 3);

        var configCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(UiTheme.CardPadding),
            Margin = new Padding(settingsCardGap / 2, 0, 0, settingsCardGap)
        };
        configCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        configCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        configCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        configCard.RowStyles.Add(new RowStyle(SizeType.Absolute, settingsActionButtonHeight));
        configCard.Controls.Add(CreateTitle("配置同步"), 0, 0);
        configCard.Controls.Add(CreateDescription("从项目 Fuyutsui 生成 config/keymap，并同步到游戏"), 0, 1);
        _configSourceLabel = CreateInfoLabel("项目目录是唯一配置源；尚未执行手动更新");
        _configSourceLabel.Dock = DockStyle.Fill;
        _configSourceLabel.AutoSize = false;
        _configSourceLabel.AutoEllipsis = true;
        _configSourceLabel.TextAlign = ContentAlignment.TopLeft;
        _configSourceLabel.Margin = new Padding(0, 10, 0, 8);
        _settingsToolTip.SetToolTip(_configSourceLabel, _configSourceLabel.Text);
        configCard.Controls.Add(_configSourceLabel, 0, 2);
        _updateConfigButton = UiTheme.CreateButton("更新配置", UiTheme.ButtonKind.Secondary);
        _updateConfigButton.AutoSize = false;
        _updateConfigButton.Size = new Size(122, settingsActionButtonHeight);
        _updateConfigButton.Dock = DockStyle.Left;
        _updateConfigButton.Margin = new Padding(0);
        _updateConfigButton.Click += async (_, _) => await UpdateConfigFromProjectWithFeedbackAsync();
        configCard.Controls.Add(_updateConfigButton, 0, 3);

        var moduleCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(UiTheme.CardPadding),
            Margin = new Padding(0, 0, settingsCardGap / 2, settingsCardGap)
        };
        moduleCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        moduleCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        moduleCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        moduleCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        moduleCard.RowStyles.Add(new RowStyle(SizeType.Absolute, settingsActionButtonHeight));
        moduleCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        moduleCard.Controls.Add(CreateTitle("模块选择"), 0, 0);
        moduleCard.SetColumnSpan(moduleCard.GetControlFromPosition(0, 0)!, 2);
        moduleCard.Controls.Add(CreateDescription("按实时职业与专精自动匹配，或手动指定模块"), 0, 1);
        moduleCard.SetColumnSpan(moduleCard.GetControlFromPosition(0, 1)!, 2);
        _moduleComboBox = new UiDropDown();
        UiTheme.StyleComboBox(_moduleComboBox);
        _moduleComboBox.Dock = DockStyle.Fill;
        _moduleComboBox.Margin = new Padding(0, 0, 14, 0);
        _settingsToolTip.SetToolTip(_moduleComboBox, "列表会根据当前游戏状态筛选可用模块");
        moduleCard.Controls.Add(_moduleComboBox, 0, 2);
        var refreshModulesButton = UiTheme.CreateButton("刷新模块", UiTheme.ButtonKind.Secondary);
        refreshModulesButton.AutoSize = false;
        refreshModulesButton.Dock = DockStyle.Fill;
        refreshModulesButton.Margin = new Padding(0);
        refreshModulesButton.Click += async (_, _) =>
        {
            await ReloadModulesWithDependenciesAsync();
            RefreshModuleSelector(_lastSnapshot, forceRefresh: false);
        };
        moduleCard.Controls.Add(refreshModulesButton, 1, 2);
        var moduleInfoText = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 8, 0, 0)
        };
        _moduleFilterLabel = CreateInfoLabel("筛选: 等待游戏状态");
        _moduleCountLabel = CreateInfoLabel("可选模块: 0");
        moduleInfoText.Controls.Add(_moduleFilterLabel);
        moduleInfoText.Controls.Add(_moduleCountLabel);
        moduleCard.Controls.Add(moduleInfoText, 0, 3);
        moduleCard.SetColumnSpan(moduleInfoText, 2);

        var getModulesCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(UiTheme.CardPadding),
            Margin = new Padding(settingsCardGap / 2, 0, 0, settingsCardGap)
        };
        getModulesCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        getModulesCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        getModulesCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        getModulesCard.RowStyles.Add(new RowStyle(SizeType.Absolute, settingsActionButtonHeight));
        getModulesCard.Controls.Add(CreateTitle("获取模块"), 0, 0);
        getModulesCard.Controls.Add(CreateDescription("访问 Shigure 官网，浏览并获取可用模块"), 0, 1);

        var moduleWebsiteLabel = CreateInfoLabel(ModuleWebsiteUrl);
        moduleWebsiteLabel.Dock = DockStyle.Fill;
        moduleWebsiteLabel.AutoSize = false;
        moduleWebsiteLabel.AutoEllipsis = true;
        moduleWebsiteLabel.TextAlign = ContentAlignment.TopLeft;
        moduleWebsiteLabel.ForeColor = UiTheme.Accent;
        moduleWebsiteLabel.Cursor = Cursors.Hand;
        moduleWebsiteLabel.Margin = new Padding(0, 10, 0, 8);
        moduleWebsiteLabel.Click += (_, _) => OpenModuleWebsite();
        _settingsToolTip.SetToolTip(moduleWebsiteLabel, $"在默认浏览器中打开 {ModuleWebsiteUrl}");
        getModulesCard.Controls.Add(moduleWebsiteLabel, 0, 2);

        var moduleActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        var moduleWebsiteButtonColor = Color.FromArgb(252, 238, 10);
        var openModuleWebsiteButton = UiTheme.CreateButton("获取模块", moduleWebsiteButtonColor, Color.Black);
        openModuleWebsiteButton.AutoSize = false;
        openModuleWebsiteButton.Size = new Size(160, settingsActionButtonHeight);
        openModuleWebsiteButton.Margin = new Padding(0, 0, 10, 0);
        openModuleWebsiteButton.Padding = new Padding(0, 2, 24, 2);
        openModuleWebsiteButton.FlatAppearance.BorderColor = moduleWebsiteButtonColor;
        openModuleWebsiteButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 244, 64);
        openModuleWebsiteButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(220, 207, 8);
        openModuleWebsiteButton.Paint += (_, e) => UiTheme.DrawExternalLinkIcon(
            e.Graphics,
            openModuleWebsiteButton.ClientRectangle,
            openModuleWebsiteButton.Text,
            openModuleWebsiteButton.Font,
            openModuleWebsiteButton.ForeColor,
            openModuleWebsiteButton.DeviceDpi / 96F);
        openModuleWebsiteButton.Click += (_, _) => OpenModuleWebsite();

        var openModuleDirectoryButton = UiTheme.CreateButton("打开模块目录", UiTheme.ButtonKind.Secondary);
        openModuleDirectoryButton.AutoSize = false;
        openModuleDirectoryButton.Size = new Size(160, settingsActionButtonHeight);
        openModuleDirectoryButton.Margin = new Padding(0);
        openModuleDirectoryButton.Click += (_, _) => OpenModuleDirectory();
        _settingsToolTip.SetToolTip(openModuleDirectoryButton, "在资源管理器中打开本地模块目录");

        moduleActions.Controls.Add(openModuleWebsiteButton);
        moduleActions.Controls.Add(openModuleDirectoryButton);
        getModulesCard.Controls.Add(moduleActions, 0, 3);

        var layoutCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(UiTheme.CardPadding),
            Margin = new Padding(0, 0, settingsCardGap / 2, 0)
        };
        layoutCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layoutCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layoutCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layoutCard.RowStyles.Add(new RowStyle(SizeType.Absolute, settingsActionButtonHeight));
        layoutCard.Controls.Add(CreateTitle("界面布局"), 0, 0);
        layoutCard.Controls.Add(CreateDescription("选择主界面浮动条的排列方向"), 0, 1);
        var layoutInfoLabel = CreateInfoLabel("切换时会交换主界面的宽高，控件与功能保持不变");
        layoutInfoLabel.Dock = DockStyle.Fill;
        layoutInfoLabel.AutoSize = false;
        layoutInfoLabel.TextAlign = ContentAlignment.TopLeft;
        layoutInfoLabel.Margin = new Padding(0, 10, 0, 8);
        layoutCard.Controls.Add(layoutInfoLabel, 0, 2);

        var layoutActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _horizontalLayoutButton = UiTheme.CreateButton("横向布局", UiTheme.ButtonKind.Secondary);
        _horizontalLayoutButton.AutoSize = false;
        _horizontalLayoutButton.Size = new Size(140, settingsActionButtonHeight);
        _horizontalLayoutButton.Margin = new Padding(0, 0, 10, 0);
        _horizontalLayoutButton.Click += (_, _) => SetMainWindowLayout(MainWindowLayout.Horizontal);
        _verticalLayoutButton = UiTheme.CreateButton("纵向布局", UiTheme.ButtonKind.Secondary);
        _verticalLayoutButton.AutoSize = false;
        _verticalLayoutButton.Size = new Size(140, settingsActionButtonHeight);
        _verticalLayoutButton.Margin = new Padding(0);
        _verticalLayoutButton.Click += (_, _) => SetMainWindowLayout(MainWindowLayout.Vertical);
        layoutActions.Controls.Add(_horizontalLayoutButton);
        layoutActions.Controls.Add(_verticalLayoutButton);
        layoutCard.Controls.Add(layoutActions, 0, 3);

        panel.Controls.Add(inputCard, 0, 0);
        panel.Controls.Add(configCard, 1, 0);
        panel.Controls.Add(moduleCard, 0, 1);
        panel.Controls.Add(getModulesCard, 1, 1);
        panel.Controls.Add(layoutCard, 0, 2);

        var closeBehaviorCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(UiTheme.CardPadding),
            Margin = new Padding(settingsCardGap / 2, 0, 0, 0)
        };
        closeBehaviorCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        closeBehaviorCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        closeBehaviorCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        closeBehaviorCard.RowStyles.Add(new RowStyle(SizeType.Absolute, settingsActionButtonHeight));
        closeBehaviorCard.Controls.Add(CreateTitle("点击 X 时（关闭窗口按钮）"), 0, 0);
        closeBehaviorCard.Controls.Add(CreateDescription("选择点击主界面关闭按钮后的行为"), 0, 1);

        var closeBehaviorInfo = CreateInfoLabel("最小化后可通过系统栏图标重新打开；完全退出会停止运行");
        closeBehaviorInfo.Dock = DockStyle.Fill;
        closeBehaviorInfo.AutoSize = false;
        closeBehaviorInfo.TextAlign = ContentAlignment.TopLeft;
        closeBehaviorInfo.Margin = new Padding(0, 10, 0, 8);
        closeBehaviorCard.Controls.Add(closeBehaviorInfo, 0, 2);

        var closeBehaviorActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _minimizeToTrayButton = UiTheme.CreateButton("最小化到系统栏", UiTheme.ButtonKind.Secondary);
        _minimizeToTrayButton.AutoSize = false;
        _minimizeToTrayButton.Size = new Size(160, settingsActionButtonHeight);
        _minimizeToTrayButton.Margin = new Padding(0, 0, 10, 0);
        _minimizeToTrayButton.Click += (_, _) => SetCloseButtonBehavior(CloseButtonBehavior.MinimizeToTray);
        _exitOnCloseButton = UiTheme.CreateButton("完全退出Shigure", UiTheme.ButtonKind.Secondary);
        _exitOnCloseButton.AutoSize = false;
        _exitOnCloseButton.Size = new Size(160, settingsActionButtonHeight);
        _exitOnCloseButton.Margin = new Padding(0);
        _exitOnCloseButton.Click += (_, _) => SetCloseButtonBehavior(CloseButtonBehavior.Exit);
        closeBehaviorActions.Controls.Add(_minimizeToTrayButton);
        closeBehaviorActions.Controls.Add(_exitOnCloseButton);
        closeBehaviorCard.Controls.Add(closeBehaviorActions, 0, 3);
        panel.Controls.Add(closeBehaviorCard, 1, 2);

        var spellIconPackageCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(UiTheme.CardPadding),
            Margin = new Padding(0)
        };
        spellIconPackageCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        spellIconPackageCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        spellIconPackageCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        spellIconPackageCard.RowStyles.Add(new RowStyle(SizeType.Absolute, settingsActionButtonHeight));
        spellIconPackageCard.Controls.Add(CreateTitle("下载数据包"), 0, 0);
        spellIconPackageCard.Controls.Add(
            CreateDescription("从 GitHub 下载或更新技能图标数据包；不会随发布包自动附带"),
            0,
            1);

        _spellIconPackageStatusLabel = CreateInfoLabel(string.Empty);
        _spellIconPackageStatusLabel.Dock = DockStyle.Fill;
        _spellIconPackageStatusLabel.AutoSize = false;
        _spellIconPackageStatusLabel.AutoEllipsis = true;
        _spellIconPackageStatusLabel.TextAlign = ContentAlignment.TopLeft;
        _spellIconPackageStatusLabel.Margin = new Padding(0, 10, 0, 8);
        spellIconPackageCard.Controls.Add(_spellIconPackageStatusLabel, 0, 2);

        _downloadSpellIconPackageButton = UiTheme.CreateButton(
            "下载数据包",
            UiTheme.ButtonKind.Secondary);
        _downloadSpellIconPackageButton.AutoSize = false;
        _downloadSpellIconPackageButton.Size = new Size(140, settingsActionButtonHeight);
        _downloadSpellIconPackageButton.Dock = DockStyle.Left;
        _downloadSpellIconPackageButton.Margin = new Padding(0);
        _downloadSpellIconPackageButton.Click += (_, _) => StartSpellIconPackageDownload();
        spellIconPackageCard.Controls.Add(_downloadSpellIconPackageButton, 0, 3);
        panel.Controls.Add(spellIconPackageCard, 0, 3);
        panel.SetColumnSpan(spellIconPackageCard, 2);

        UpdateLayoutButtons();
        UpdateCloseBehaviorButtons();
        UpdateSpellIconPackageCard();
        scrollHost.Controls.Add(panel);
        scrollHost.Resize += (_, _) => panel.Width = Math.Max(0, scrollHost.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
        return scrollHost;
    }

    private void StartSpellIconPackageDownload()
    {
        if (!_spellIconPackageDownloadTask.IsCompleted || _shutdownStarted)
        {
            return;
        }

        _spellIconPackageDownloadCts?.Dispose();
        _spellIconPackageDownloadCts = new CancellationTokenSource();
        _spellIconPackageDownloadTask = DownloadSpellIconPackageWithFeedbackAsync(
            _spellIconPackageDownloadCts.Token);
    }

    private async Task DownloadSpellIconPackageWithFeedbackAsync(CancellationToken cancellationToken)
    {
        _downloadSpellIconPackageButton.Enabled = false;
        var progress = new Progress<SpellIconDownloadProgress>(value =>
        {
            if (_shutdownStarted || _spellIconPackageStatusLabel.IsDisposed)
            {
                return;
            }

            _spellIconPackageStatusLabel.Text = value.Message;
            _settingsToolTip.SetToolTip(_spellIconPackageStatusLabel, value.Message);
            _downloadSpellIconPackageButton.Text = value.Percentage is { } percentage
                ? $"正在下载 {percentage}%"
                : "正在检查……";
        });

        AppendLog("开始检查 GitHub 技能图标数据包");
        try
        {
            var result = await _spellIconPackageDownloadService.UpdateAsync(progress, cancellationToken);
            if (_shutdownStarted)
            {
                return;
            }

            var sizeText = $"{result.Size / 1024d / 1024d:F2} MiB";
            var hashText = result.Sha256[..Math.Min(12, result.Sha256.Length)];
            if (result.UpToDate)
            {
                _spellIconPackageStatusLabel.Text = $"已是最新：{sizeText}，SHA-256 {hashText}…";
                AppendLog("技能图标数据包已是最新，本地文件未修改");
            }
            else
            {
                _spellIconPackageStatusLabel.Text = $"安装完成：{sizeText}，SHA-256 {hashText}…";
                AppendLog("技能图标数据包已下载、校验并热加载");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!_shutdownStarted)
            {
                _spellIconPackageStatusLabel.Text = "下载已取消；原数据包未修改。";
                AppendLog("技能图标数据包下载已取消");
            }
        }
        catch (Exception ex)
        {
            if (!_shutdownStarted)
            {
                _spellIconPackageStatusLabel.Text = $"下载失败：{ex.Message}";
                _settingsToolTip.SetToolTip(_spellIconPackageStatusLabel, ex.ToString());
                AppendLog($"技能图标数据包下载失败: {ex.Message}");
                MessageBox.Show(
                    this,
                    ex.Message,
                    "下载数据包失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        finally
        {
            if (!_shutdownStarted && !_downloadSpellIconPackageButton.IsDisposed)
            {
                _downloadSpellIconPackageButton.Enabled = true;
                _downloadSpellIconPackageButton.Text = SpellIconCatalog.IsPackageAvailable
                    ? "检查更新"
                    : "下载数据包";
            }
        }
    }

    private void UpdateSpellIconPackageCard()
    {
        var packagePath = SpellIconCatalog.PackagePath;
        if (SpellIconCatalog.IsPackageAvailable && File.Exists(packagePath))
        {
            var length = new FileInfo(packagePath).Length;
            _spellIconPackageStatusLabel.Text =
                $"已安装：{length / 1024d / 1024d:F2} MiB。点击检查 GitHub 更新。";
            _downloadSpellIconPackageButton.Text = "检查更新";
        }
        else if (File.Exists(packagePath))
        {
            _spellIconPackageStatusLabel.Text = "本地数据包损坏或格式不受支持；技能图标与添加技能联想不可用。";
            _downloadSpellIconPackageButton.Text = "重新下载";
        }
        else
        {
            _spellIconPackageStatusLabel.Text = "未安装；技能图标与添加技能联想不可用。";
            _downloadSpellIconPackageButton.Text = "下载数据包";
        }

        _settingsToolTip.SetToolTip(
            _spellIconPackageStatusLabel,
            _spellIconPackageStatusLabel.Text);
    }

    private void OpenModuleWebsite()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ModuleWebsiteUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"无法打开模块网站：{ex.Message}",
                "Shigure",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void OpenModuleDirectory()
    {
        var moduleDirectory = _moduleStore.ModuleDirectory;
        try
        {
            Directory.CreateDirectory(moduleDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{moduleDirectory}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"无法打开模块目录：{ex.Message}",
                "Shigure",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private async Task UpdateConfigFromProjectWithFeedbackAsync()
    {
        _updateConfigButton.Enabled = false;
        _updateConfigButton.Text = "更新中…";
        _configSourceLabel.ForeColor = UiTheme.Warning;
        _configSourceLabel.Text = "正在生成配置并同步游戏插件…";
        try
        {
            var updated = await UpdateConfigFromProjectAsync();
            _configSourceLabel.ForeColor = updated ? UiTheme.Success : UiTheme.Danger;
        }
        catch
        {
            _configSourceLabel.ForeColor = UiTheme.Danger;
            throw;
        }
        finally
        {
            _updateConfigButton.Text = "更新配置";
            _updateConfigButton.Enabled = true;
            _settingsToolTip.SetToolTip(_configSourceLabel, _configSourceLabel.Text);
        }
    }

    private async Task SynchronizeAddonAtStartupAsync()
    {
        try
        {
            var result = await Task.Run(_addonSyncService.SynchronizeAll);
            LogAddonSyncResult("启动插件同步", result);
        }
        catch (Exception ex)
        {
            AppendLog($"启动插件同步失败，程序将继续运行: {ex.Message}");
        }
    }

    private async Task<bool> UpdateConfigFromProjectAsync()
    {
        try
        {
            var result = await QueueProjectConfigUpdateAsync(savedAddonFilePath: null);
            if (!_shutdownStarted)
            {
                ShowProjectConfigUpdateResult(result);
            }
            return true;
        }
        catch (Exception ex)
        {
            if (_shutdownStarted)
            {
                return false;
            }

            AppendLog($"更新配置失败: {ex.Message}");
            MessageBox.Show(ex.Message, "更新配置失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _configSourceLabel.Text = $"更新失败：{ex.Message}";
            return false;
        }
    }

    private async Task<string?> UpdateConfigAfterSaveAsync(string savedAddonFilePath)
    {
        var result = await QueueProjectConfigUpdateAsync(savedAddonFilePath);
        return DescribeAddonSyncIssue(result.AddonSync);
    }

    private Task<ProjectConfigUpdateResult> QueueProjectConfigUpdateAsync(string? savedAddonFilePath)
    {
        lock (_configUpdateSync)
        {
            if (_shutdownStarted)
            {
                return Task.FromException<ProjectConfigUpdateResult>(
                    new OperationCanceledException("程序正在关闭。"));
            }

            var updateTask = RunQueuedConfigUpdateAsync(_configUpdateTail, savedAddonFilePath);
            _configUpdateTail = updateTask;
            return updateTask;
        }
    }

    private async Task<ProjectConfigUpdateResult> RunQueuedConfigUpdateAsync(
        Task previousUpdate,
        string? savedAddonFilePath)
    {
        await Task.Yield();
        try
        {
            await previousUpdate;
        }
        catch
        {
            // 前一个调用方会收到自己的异常；队列仍继续处理后续更新。
        }

        if (_shutdownStarted)
        {
            throw new OperationCanceledException("程序正在关闭。");
        }

        return await UpdateConfigFromProjectCoreAsync(savedAddonFilePath);
    }

    private Task GetPendingConfigUpdateTask()
    {
        lock (_configUpdateSync)
        {
            return _configUpdateTail;
        }
    }

    private async Task WaitForPendingConfigUpdatesAsync()
    {
        while (true)
        {
            var pending = GetPendingConfigUpdateTask();
            await pending;
            lock (_configUpdateSync)
            {
                if (ReferenceEquals(pending, _configUpdateTail))
                {
                    return;
                }
            }
        }
    }

    private async Task<ProjectConfigUpdateResult> UpdateConfigFromProjectCoreAsync(string? savedAddonFilePath)
    {
        if (_shutdownStarted)
        {
            throw new OperationCanceledException("程序正在关闭。");
        }

        var classDirectory = Path.Combine(_addonSyncService.SourceRoot, "class");
        var classMacrosPath = Path.Combine(_addonSyncService.SourceRoot, "core", "classmacros.lua");
        if (!Directory.Exists(classDirectory))
        {
            throw new DirectoryNotFoundException($"找不到项目 Fuyutsui class 目录: {classDirectory}");
        }

        _configSourceLabel.Text = File.Exists(classMacrosPath)
            ? $"项目 Fuyutsui: {classDirectory} + classmacros.lua"
            : $"项目 Fuyutsui class: {classDirectory}";
        var configDirectory = Path.Combine(_baseDirectory, ConfigService.ConfigDirectoryName);
        var keymapDirectory = Path.Combine(_baseDirectory, "keymap");
        Directory.CreateDirectory(keymapDirectory);

        try
        {
            UseWaitCursor = true;
            var result = await Task.Run(() =>
            {
                var configResult = FuyutsuiConfigConverter.UpdateFromClassDirectory(classDirectory, configDirectory);
                FuyutsuiKeymapConverter.UpdateResult? keymapResult = null;
                if (File.Exists(classMacrosPath))
                {
                    keymapResult = FuyutsuiKeymapConverter.UpdateFromClassMacros(classMacrosPath, keymapDirectory);
                }

                var addonSync = string.IsNullOrWhiteSpace(savedAddonFilePath)
                    ? _addonSyncService.SynchronizeAll()
                    : _addonSyncService.SynchronizeFile(savedAddonFilePath);
                return new ProjectConfigUpdateResult(configResult, keymapResult, addonSync);
            });

            if (_shutdownStarted)
            {
                throw new OperationCanceledException("程序正在关闭。");
            }

            _moduleEditor.ReloadCatalogs();
            AppendLog($"已从项目 Fuyutsui 更新配置: {result.Config.UpdatedFiles.Count} 个文件 ← {result.Config.ClassDirectory}");
            foreach (var warning in result.Config.Warnings.Take(20))
            {
                AppendLog($"配置警告: {warning}");
            }

            if (result.Keymap is { } keymap)
            {
                AppendLog($"已从 classmacros 更新 keymap: {keymap.UpdatedFiles.Count} 个文件 ← {keymap.ClassMacrosPath}");
                foreach (var warning in keymap.Warnings.Take(20))
                {
                    AppendLog($"keymap 警告: {warning}");
                }
            }
            else
            {
                AppendLog("项目 Fuyutsui 中未找到 core\\classmacros.lua，已跳过 keymap 更新");
            }

            LogAddonSyncResult(
                string.IsNullOrWhiteSpace(savedAddonFilePath) ? "游戏插件全量同步" : "游戏插件文件同步",
                result.AddonSync);

            if (_runtimeSession.HasSession)
            {
                AppendLog("配置已更新, 重新启动运行");
                await StartOrRestartRuntimeAsync(restart: true, waitForConfigUpdates: false);
            }

            if (_shutdownStarted)
            {
                throw new OperationCanceledException("程序正在关闭。");
            }

            return result;
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void ShowProjectConfigUpdateResult(ProjectConfigUpdateResult result)
    {
        var warningCount = result.Config.Warnings.Count + (result.Keymap?.Warnings.Count ?? 0);
        var warningText = warningCount == 0
            ? string.Empty
            : $"\n转换警告 {warningCount} 条（详见日志）。";
        var keymapText = result.Keymap is { } keymap
            ? $"\nkeymap: {keymap.UpdatedFiles.Count} 个文件"
            : "\nkeymap: 未更新（缺少 classmacros.lua）";
        var syncIssue = DescribeAddonSyncIssue(result.AddonSync);
        var syncText = syncIssue is null
            ? $"\n游戏插件: 已复制 {result.AddonSync.CopiedFiles.Count}，哈希相同 {result.AddonSync.SkippedFiles.Count}\n{result.AddonSync.TargetRoot}"
            : $"\n游戏插件: {syncIssue}";

        _configSourceLabel.Text = syncIssue is null && warningCount == 0
            ? $"已更新 {result.Config.UpdatedFiles.Count} 个配置文件，并完成游戏同步"
            : $"配置已更新；{syncIssue ?? $"存在 {warningCount} 条转换警告"}";
        _configSourceLabel.ForeColor = syncIssue is null && warningCount == 0
            ? UiTheme.Success
            : UiTheme.Warning;
        _settingsToolTip.SetToolTip(_configSourceLabel, _configSourceLabel.Text);

        if (syncIssue is not null || warningCount > 0)
        {
            MessageBox.Show(
                $"已从项目 Fuyutsui 更新 {result.Config.UpdatedFiles.Count} 个职业配置。{keymapText}{syncText}{warningText}",
                "更新配置",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void LogAddonSyncResult(string operation, FuyutsuiAddonSyncResult result)
    {
        if (!result.TargetFound)
        {
            AppendLog($"{operation}: {result.SkippedReason}");
            return;
        }

        AppendLog(
            $"{operation}: 已复制 {result.CopiedFiles.Count}，哈希相同 {result.SkippedFiles.Count} → {result.TargetRoot}");
        foreach (var failure in result.Failures.Take(20))
        {
            AppendLog($"插件同步失败: {failure.RelativePath}: {failure.Message}");
        }

        if (result.Failures.Count > 20)
        {
            AppendLog($"插件同步另有 {result.Failures.Count - 20} 个失败文件未展开。");
        }
    }

    private static string? DescribeAddonSyncIssue(FuyutsuiAddonSyncResult result)
    {
        if (!result.TargetFound)
        {
            return result.SkippedReason;
        }

        if (result.Failures.Count == 0)
        {
            return null;
        }

        var first = result.Failures[0];
        return result.Failures.Count == 1
            ? $"{first.RelativePath}: {first.Message}"
            : $"{result.Failures.Count} 个文件同步失败；首个失败为 {first.RelativePath}: {first.Message}";
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
        RefreshModuleSelector(_lastSnapshot, forceRefresh: false);
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

    private async Task StartRuntimeAsync()
    {
        if (_runtimeSession.IsRunning)
        {
            return;
        }

        await StartOrRestartRuntimeAsync(restart: false);
    }

    private async Task<bool> StartOrRestartRuntimeAsync(
        bool restart,
        bool waitForConfigUpdates = true)
    {
        if (_shutdownStarted)
        {
            return false;
        }

        var options = BuildOptions();
        if (!ValidateRuntimeOptions(options))
        {
            return false;
        }

        var requestVersion = Interlocked.Increment(ref _runtimeRequestVersion);

        try
        {
            if (waitForConfigUpdates)
            {
                await WaitForPendingConfigUpdatesAsync();
                if (_shutdownStarted || requestVersion != Volatile.Read(ref _runtimeRequestVersion))
                {
                    return false;
                }
            }

            if (restart)
            {
                await _runtimeSession.RestartAsync(options, requestVersion);
            }
            else
            {
                await _runtimeSession.StartAsync(options, requestVersion);
            }
        }
        catch (Exception ex)
        {
            if (_shutdownStarted || requestVersion != Volatile.Read(ref _runtimeRequestVersion))
            {
                return false;
            }

            var operation = restart ? "重启" : "启动";
            MessageBox.Show(ex.Message, $"{operation}失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppendLog($"{operation}失败: {ex.Message}");
            SetRuntimeControls(running: _runtimeSession.IsRunning);
            return false;
        }

        if (_shutdownStarted || requestVersion != Volatile.Read(ref _runtimeRequestVersion))
        {
            return false;
        }

        if (!_runtimeSession.IsRunning)
        {
            SetRuntimeControls(running: false);
            return false;
        }

        ResetRuntimeLogState();
        SetRuntimeControls(running: true);
        AppendLog($"运行已{(restart ? "重启" : "启动")}: {_processLocator.DescribeConfiguredProcesses()} / {options.ToggleKey} / {ModeLabel(options.Mode)}");
        return true;
    }

    private bool ValidateRuntimeOptions(AppOptions options)
    {
        if (IsUnsupportedToggleKey(options.ToggleKey))
        {
            MessageBox.Show("触发键不支持 ALT，请选择其他按键。", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (_triggerKeyState.ResolveVirtualKey(options.ToggleKey) is null)
        {
            MessageBox.Show($"无法识别触发键: {options.ToggleKey}", "Shigure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        return true;
    }

    private void ResetRuntimeLogState()
    {
        _lastLoggedStep = null;
        _lastLoggedStepDetails = null;
        _lastLoggedScanFailureReason = null;
        _lastLoggedClass = null;
        _lastLoggedModule = null;
        _lastLoggedEnabled = null;
    }

    private async Task RestartRuntimeFromEditorAsync()
    {
        RefreshModuleSelector(_lastSnapshot, forceRefresh: false);
        if (!_runtimeSession.HasSession)
        {
            return;
        }

        AppendLog("模块已变更, 重新启动运行");
        await StartOrRestartRuntimeAsync(restart: true);
    }

    private void ToggleEnabled()
    {
        if (!_runtimeSession.IsRunning)
        {
            return;
        }

        _runtimeSession.ToggleEnabled();
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

    private void HandleSnapshotUpdated(long sessionId, RenderSnapshot snapshot)
    {
        PostToUi(() =>
        {
            if (_runtimeSession.CurrentSessionId == sessionId)
            {
                ApplySnapshot(snapshot);
            }
        });
    }

    private void HandleRuntimeFailed(long sessionId, Exception exception)
    {
        PostToUi(() =>
        {
            if (_runtimeSession.CurrentSessionId != sessionId)
            {
                return;
            }

            AppendLog($"运行异常: {exception.Message}");
            foreach (var titleLabel in _titleLabels)
            {
                titleLabel.ForeColor = UiTheme.Danger;
            }
            SetRuntimeControls(running: false);
        });
    }

    private void HandleRuntimeStopped(long sessionId)
    {
        PostToUi(() =>
        {
            if (_runtimeSession.CurrentSessionId == sessionId)
            {
                SetRuntimeControls(running: false);
            }
        });
    }

    private void ApplySnapshot(RenderSnapshot snapshot)
    {
        _lastSnapshot = snapshot;

        UpdateHeaderIconColor(snapshot.ClassId);
        UpdateLogicStatusLabel(snapshot.Enabled);
        foreach (var enableButton in _enableButtons)
        {
            enableButton.Text = enableButton == _verticalEnableButton
                ? snapshot.Enabled ? "关\r\n闭" : "开\r\n启"
                : snapshot.Enabled ? "关闭" : "开启";
        }
        UpdateTrayToggleMenuItem(running: true);

        RefreshModuleSelector(snapshot, forceRefresh: false);
        _statusForm.ApplySnapshot(snapshot);
        WriteSnapshotLog(snapshot);
    }

    private void RefreshModuleSelector(RenderSnapshot? snapshot, bool forceRefresh)
    {
        if (_moduleComboBox is null)
        {
            return;
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
        if (!forceRefresh && signature == _lastModuleSelectorSignature)
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
        if (_runtimeSession.IsRunning && options == _runtimeSession.CurrentOptions)
        {
            return;
        }

        AppendLog("设置已变更, 重新启动运行");
        await StartOrRestartRuntimeAsync(restart: _runtimeSession.HasSession);
    }

    private void WriteSnapshotLog(RenderSnapshot snapshot)
    {
        if (!string.Equals(snapshot.ScanFailureReason, _lastLoggedScanFailureReason, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(snapshot.ScanFailureReason))
            {
                if (!string.IsNullOrWhiteSpace(_lastLoggedScanFailureReason))
                {
                    AppendLog("扫描已恢复");
                }
            }
            else
            {
                AppendLog($"扫描失败: {snapshot.ScanFailureReason}");
            }

            _lastLoggedScanFailureReason = snapshot.ScanFailureReason;
        }

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
            var details = BuildStepLogDetails(snapshot);
            if (snapshot.CurrentStep != _lastLoggedStep || details != _lastLoggedStepDetails)
            {
                _lastLoggedStep = snapshot.CurrentStep;
                _lastLoggedStepDetails = details;
                AppendLog($"步骤: {snapshot.CurrentStep}{details}");
            }
        }
    }

    private static string BuildStepLogDetails(RenderSnapshot snapshot)
    {
        var fields = new (string Key, string Label)[]
        {
            ("动作单位", "目标"),
            ("动作按键", "按键"),
            ("动作延迟", "动作延迟"),
            ("逻辑延迟", "逻辑延迟"),
            ("规则编号", "规则编号"),
            ("限流键", "限流键"),
            ("发送失败", "发送失败")
        };
        var details = new List<string>();
        foreach (var (key, label) in fields)
        {
            if (!snapshot.UnitInfo.TryGetValue(key, out var value))
            {
                continue;
            }

            var text = UiTheme.FormatValue(value);
            if (!string.IsNullOrWhiteSpace(text))
            {
                details.Add($"{label}: {text}");
            }
        }

        return details.Count == 0 ? string.Empty : $"，{string.Join("，", details)}";
    }

    private void SetRuntimeControls(bool running)
    {
        if (!running)
        {
            UpdateHeaderIconColor(null);
            UpdateLogicStatusLabel(enabled: false);
        }

        foreach (var enableButton in _enableButtons)
        {
            enableButton.Enabled = running;
        }

        UpdateTrayToggleMenuItem(running);
    }

    private void UpdateLogicStatusLabel(bool enabled)
    {
        foreach (var statusLabel in _runtimeStatusLabels)
        {
            statusLabel.Text = string.Empty;
        }
        foreach (var titleLabel in _titleLabels)
        {
            titleLabel.ForeColor = enabled ? UiTheme.Accent : UiTheme.Text;
        }
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
        var cachedLayout = ParseMainWindowLayout(_uiCache.MainWindowLayout);
        SetMainWindowLayout(cachedLayout, persist: false);
        _closeButtonBehavior = ParseCloseButtonBehavior(_uiCache.CloseButtonBehavior);
        UpdateCloseBehaviorButtons();

        var cachedBounds = GetCachedMainWindowBounds(cachedLayout) ?? _uiCache.MainWindowBounds;
        if (!TryApplyCachedMainWindowBounds(cachedBounds)
            && _uiCache.MainWindowLocation is { } mainLocation)
        {
            var restoredBounds = new Rectangle(mainLocation.X, mainLocation.Y, Width, Height);
            if (UiCacheStore.IsBoundsVisible(restoredBounds))
            {
                StartPosition = FormStartPosition.Manual;
                Location = new Point(mainLocation.X, mainLocation.Y);
            }
        }

        _statusForm.ApplyCachedBounds(_uiCache.SettingsWindowBounds);
        _statusForm.ApplyCachedPage(_uiCache.SelectedSettingsPage);
    }

    private void SaveUiCache()
    {
        var latestCache = UiCacheStore.Load();
        _uiCache.ModuleRulesGridColumns = latestCache.ModuleRulesGridColumns;
        _uiCache.ColumnWidths = latestCache.ColumnWidths;
        _uiCache.ConditionEditorWindowSize = latestCache.ConditionEditorWindowSize;
        _uiCache.UnitEditorWindowSize = latestCache.UnitEditorWindowSize;

        var currentBounds = CaptureMainWindowBounds();
        _uiCache.MainWindowBounds = currentBounds;
        SetCachedMainWindowBounds(_mainWindowLayout, currentBounds);
        _uiCache.MainWindowLocation = new WindowLocation
        {
            X = Left,
            Y = Top
        };

        if (_statusForm.HasKnownBounds)
        {
            _uiCache.SettingsWindowBounds = _statusForm.GetCachedBounds();
        }

        _uiCache.SelectedSettingsPage = _statusForm.SelectedPageKey;

        _uiCache.MainWindowLayout = _mainWindowLayout.ToString();
        _uiCache.CloseButtonBehavior = _closeButtonBehavior.ToString();
        _uiCache.ToggleKey = _toggleKeyName;
        _uiCache.SelectedModuleId = _selectedModuleId;
        UiCacheStore.Save(_uiCache);
    }

    private void ShowSettingsView()
    {
        RefreshModuleSelector(_lastSnapshot, forceRefresh: false);
        _statusForm.ShowSettings(_lastSnapshot);
    }

    private void InitializeTrayIcon()
    {
        _trayMenu = new ContextMenuStrip
        {
            BackColor = UiTheme.SurfaceRaised,
            ForeColor = UiTheme.Text,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Regular),
            Padding = new Padding(6),
            ShowImageMargin = false,
            ShowCheckMargin = false,
            DropShadowEnabled = true,
            Renderer = new TrayMenuRenderer()
        };

        ToolStripMenuItem CreateTrayMenuItem(string text)
        {
            return new ToolStripMenuItem(text)
            {
                AutoSize = false,
                Size = new Size(156, 38),
                Padding = new Padding(14, 0, 14, 0),
                ForeColor = UiTheme.Text
            };
        }

        var showMainMenuItem = CreateTrayMenuItem("主界面");
        showMainMenuItem.Click += (_, _) => ShowMainWindow();
        _trayToggleMenuItem = CreateTrayMenuItem("开启/关闭");
        _trayToggleMenuItem.Click += (_, _) => ToggleEnabled();
        var settingsMenuItem = CreateTrayMenuItem("设置");
        settingsMenuItem.Click += (_, _) => ShowSettingsView();
        var exitMenuItem = CreateTrayMenuItem("退出");
        exitMenuItem.ForeColor = UiTheme.Danger;
        exitMenuItem.Click += (_, _) => RequestExit();
        _trayMenu.Items.AddRange([showMainMenuItem, _trayToggleMenuItem, settingsMenuItem, exitMenuItem]);
        _trayMenu.Opening += (_, _) => UpdateTrayToggleMenuItem(_runtimeSession.IsRunning);
        UiTheme.ApplyControlRoundedRegion(_trayMenu, 10);

        _trayDefaultIcon = CreateTrayIcon(Color.White);
        _trayEnabledIcon = CreateTrayIcon(UiTheme.Success);

        _trayIcon = new NotifyIcon
        {
            Text = "Shigure - 已关闭",
            Icon = _trayDefaultIcon ?? Icon ?? SystemIcons.Application,
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowMainWindow();
            }
        };
    }

    private void ShowMainWindow()
    {
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }
        if (!Visible)
        {
            Show();
        }

        BringToFront();
        Activate();
    }

    private void MinimizeToTray()
    {
        CancelToggleKeyCapture();
        SaveUiCache();
        _statusForm.Hide();
        Hide();
    }

    private void RequestExit()
    {
        if (_shutdownStarted)
        {
            return;
        }

        _exitRequested = true;
        Close();
    }

    private void UpdateTrayToggleMenuItem(bool running)
    {
        if (_trayToggleMenuItem is null)
        {
            return;
        }

        var enabled = running && _lastSnapshot?.Enabled == true;
        _trayToggleMenuItem.Enabled = running;
        _trayToggleMenuItem.ForeColor = enabled ? UiTheme.Success : UiTheme.Text;

        if (_trayIconShowsEnabled == enabled)
        {
            return;
        }

        _trayIconShowsEnabled = enabled;
        _trayIcon.Icon = enabled
            ? _trayEnabledIcon ?? _trayDefaultIcon ?? Icon ?? SystemIcons.Application
            : _trayDefaultIcon ?? Icon ?? SystemIcons.Application;
        _trayIcon.Text = enabled ? "Shigure - 已开启" : "Shigure - 已关闭";
    }

    private Icon? CreateTrayIcon(Color color)
    {
        _headerIconMask ??= LoadHeaderIconMask();
        if (_headerIconMask is null)
        {
            return null;
        }

        using var tintedIcon = TintHeaderIcon(_headerIconMask, color);
        using var trayBitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(trayBitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.DrawImage(tintedIcon, new Rectangle(0, 0, trayBitmap.Width, trayBitmap.Height));
        }

        var iconHandle = trayBitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(iconHandle);
            return (Icon)icon.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(iconHandle);
        }
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

    private string? TryMapKeyToHotkey(Keys key)
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
            _ => _triggerKeyState.ResolveVirtualKey(keyName) is not null ? keyName : null
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

    private static Button CreateTopBarButton(string text, Color backColor, Color foreColor, bool vertical)
    {
        Button button;
        if (vertical)
        {
            var stackedButton = new StackedTextButton();
            UiTheme.StyleButton(stackedButton, text, backColor, foreColor);
            button = stackedButton;
        }
        else
        {
            button = UiTheme.CreateButton(text, backColor, foreColor);
        }

        ConfigureTopBarButton(button);
        button.Size = vertical ? new Size(36, 88) : new Size(88, 36);
        button.Margin = vertical ? new Padding(0, 6, 0, 0) : new Padding(6, 0, 0, 0);
        return button;
    }

    private void SetMainWindowLayout(MainWindowLayout layout, bool persist = true)
    {
        if (_mainWindowLayout == layout)
        {
            UpdateLayoutButtons();
            return;
        }

        if (persist)
        {
            SetCachedMainWindowBounds(_mainWindowLayout, CaptureMainWindowBounds());
        }

        var previousClientSize = ClientSize;
        _mainWindowLayout = layout;
        var vertical = layout == MainWindowLayout.Vertical;

        SuspendLayout();
        try
        {
            MinimumSize = Size.Empty;
            ClientSize = new Size(previousClientSize.Height, previousClientSize.Width);
            MinimumSize = vertical ? new Size(56, 420) : new Size(420, 56);
            _horizontalTopBar.Visible = !vertical;
            _verticalTopBar.Visible = vertical;
            (vertical ? _verticalTopBar : _horizontalTopBar).BringToFront();
        }
        finally
        {
            ResumeLayout(true);
        }

        if (persist)
        {
            TryApplyCachedMainWindowBounds(GetCachedMainWindowBounds(layout));
        }

        UpdateLayoutButtons();
        if (persist)
        {
            SaveUiCache();
        }
    }

    private WindowBounds CaptureMainWindowBounds()
    {
        return new WindowBounds
        {
            X = Left,
            Y = Top,
            Width = Width,
            Height = Height
        };
    }

    private WindowBounds? GetCachedMainWindowBounds(MainWindowLayout layout)
        => layout == MainWindowLayout.Vertical
            ? _uiCache.VerticalMainWindowBounds
            : _uiCache.HorizontalMainWindowBounds;

    private void SetCachedMainWindowBounds(MainWindowLayout layout, WindowBounds bounds)
    {
        if (layout == MainWindowLayout.Vertical)
        {
            _uiCache.VerticalMainWindowBounds = bounds;
        }
        else
        {
            _uiCache.HorizontalMainWindowBounds = bounds;
        }
    }

    private bool TryApplyCachedMainWindowBounds(WindowBounds? bounds)
    {
        if (bounds is null)
        {
            return false;
        }

        var restoredBounds = new Rectangle(
            bounds.X,
            bounds.Y,
            Math.Max(MinimumSize.Width, bounds.Width),
            Math.Max(MinimumSize.Height, bounds.Height));
        if (!UiCacheStore.IsBoundsVisible(restoredBounds))
        {
            return false;
        }

        StartPosition = FormStartPosition.Manual;
        Bounds = restoredBounds;
        return true;
    }

    private static MainWindowLayout ParseMainWindowLayout(string? value)
        => Enum.TryParse<MainWindowLayout>(value, ignoreCase: true, out var layout)
            ? layout
            : MainWindowLayout.Horizontal;

    private static CloseButtonBehavior ParseCloseButtonBehavior(string? value)
        => Enum.TryParse<CloseButtonBehavior>(value, ignoreCase: true, out var behavior)
            ? behavior
            : CloseButtonBehavior.MinimizeToTray;

    private void SetCloseButtonBehavior(CloseButtonBehavior behavior)
    {
        _closeButtonBehavior = behavior;
        UpdateCloseBehaviorButtons();
        SaveUiCache();
    }

    private void UpdateCloseBehaviorButtons()
    {
        if (_minimizeToTrayButton is null || _exitOnCloseButton is null)
        {
            return;
        }

        StyleLayoutButton(_minimizeToTrayButton, _closeButtonBehavior == CloseButtonBehavior.MinimizeToTray);
        StyleLayoutButton(_exitOnCloseButton, _closeButtonBehavior == CloseButtonBehavior.Exit);
    }

    private void UpdateLayoutButtons()
    {
        if (_horizontalLayoutButton is null || _verticalLayoutButton is null)
        {
            return;
        }

        StyleLayoutButton(_horizontalLayoutButton, _mainWindowLayout == MainWindowLayout.Horizontal);
        StyleLayoutButton(_verticalLayoutButton, _mainWindowLayout == MainWindowLayout.Vertical);
    }

    private static void StyleLayoutButton(Button button, bool selected)
    {
        button.BackColor = selected ? UiTheme.Accent : UiTheme.Field;
        button.ForeColor = selected ? Color.FromArgb(10, 31, 31) : UiTheme.Text;
        button.FlatAppearance.BorderColor = selected ? UiTheme.Accent : UiTheme.Border;
        button.FlatAppearance.MouseOverBackColor = selected ? Color.FromArgb(112, 234, 221) : UiTheme.Hover;
        button.FlatAppearance.MouseDownBackColor = selected ? Color.FromArgb(62, 194, 181) : UiTheme.Pressed;
    }

    private sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
    {
        public TrayMenuRenderer()
            : base(new TrayMenuColorTable())
        {
            RoundedEdges = true;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(UiTheme.SurfaceRaised);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using var path = UiTheme.CreateRoundedRectanglePath(bounds, 10);
            using var pen = new Pen(UiTheme.Border);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.DrawPath(pen, path);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected)
            {
                return;
            }

            var bounds = new Rectangle(2, 2, e.Item.Width - 4, e.Item.Height - 4);
            var background = e.Item.ForeColor == UiTheme.Danger ? UiTheme.DangerSoft : UiTheme.Hover;
            using var path = UiTheme.CreateRoundedRectanglePath(bounds, 7);
            using var brush = new SolidBrush(background);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, path);
        }
    }

    private sealed class TrayMenuColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => UiTheme.SurfaceRaised;
        public override Color MenuItemSelected => UiTheme.Hover;
        public override Color MenuItemBorder => UiTheme.Border;
        public override Color ToolStripBorder => UiTheme.Border;
        public override Color ImageMarginGradientBegin => UiTheme.SurfaceRaised;
        public override Color ImageMarginGradientMiddle => UiTheme.SurfaceRaised;
        public override Color ImageMarginGradientEnd => UiTheme.SurfaceRaised;
    }

    private sealed class RotatableLabel : Label
    {
        private string _displayText = string.Empty;
        private bool _rotated;
        private bool _suppressBaseText;

        [System.ComponentModel.DesignerSerializationVisibility(
            System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Rotated
        {
            get => _rotated;
            set
            {
                if (_rotated == value)
                {
                    return;
                }

                _rotated = value;
                var size = Size;
                Size = new Size(size.Height, size.Width);
                base.Text = value ? string.Empty : _displayText;
                Invalidate();
            }
        }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string Text
        {
            get => _suppressBaseText ? string.Empty : _displayText;
            set
            {
                _displayText = value ?? string.Empty;
                base.Text = _rotated ? string.Empty : _displayText;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!_rotated)
            {
                base.OnPaint(e);
                return;
            }

            _suppressBaseText = true;
            try
            {
                base.OnPaint(e);
            }
            finally
            {
                _suppressBaseText = false;
            }

            if (string.IsNullOrEmpty(_displayText))
            {
                return;
            }
            DrawRotatedText(e.Graphics, ClientRectangle, _displayText, Font, ForeColor);
        }
    }

    private sealed class StackedTextButton : Button
    {
        private string _displayText = string.Empty;
        private bool _suppressBaseText;

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string Text
        {
            get => _suppressBaseText ? string.Empty : _displayText;
            set
            {
                _displayText = value ?? string.Empty;
                base.Text = string.Empty;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            _suppressBaseText = true;
            try
            {
                base.OnPaint(pevent);
            }
            finally
            {
                _suppressBaseText = false;
            }

            var lines = _displayText.Replace("\r", string.Empty).Split('\n');
            if (lines.Length == 0)
            {
                return;
            }

            var flags = TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPadding
                | TextFormatFlags.NoPrefix;
            var lineHeight = TextRenderer.MeasureText(
                pevent.Graphics,
                "开",
                Font,
                Size.Empty,
                TextFormatFlags.NoPadding).Height;
            var totalHeight = lineHeight * lines.Length;
            var top = Math.Max(0, (ClientSize.Height - totalHeight) / 2);
            for (var index = 0; index < lines.Length; index++)
            {
                var lineBounds = new Rectangle(0, top + (index * lineHeight), ClientSize.Width, lineHeight);
                TextRenderer.DrawText(pevent.Graphics, lines[index], Font, lineBounds, ForeColor, flags);
            }
        }
    }

    private static void DrawRotatedText(
        Graphics graphics,
        Rectangle bounds,
        string text,
        Font font,
        Color color)
    {
        var state = graphics.Save();
        try
        {
            graphics.TranslateTransform(bounds.Left + bounds.Width / 2F, bounds.Top + bounds.Height / 2F);
            graphics.RotateTransform(90F);
            using var brush = new SolidBrush(color);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap
            };
            graphics.DrawString(
                text,
                font,
                brush,
                new RectangleF(-bounds.Height / 2F, -bounds.Width / 2F, bounds.Height, bounds.Width),
                format);
        }
        finally
        {
            graphics.Restore(state);
        }
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
