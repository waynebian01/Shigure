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
    private const int GamepadCaptureIntervalMs = 50;
    private const int DefaultMainBarLongEdge = 476;
    private const int DefaultMainBarShortEdge = 64;
    private const int MinimumMainBarLongEdge = 294;
    private const int MinimumMainBarShortEdge = 56;
    private const int MainBarSizeVersion = 1;
    private const int TopBarButtonGap = 12;
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
    private UiDropDown _defaultClassComboBox = null!;
    private UiDropDown _defaultSpecComboBox = null!;
    private UiDropDown _defaultHeroTalentComboBox = null!;
    private UiDropDown _defaultPartyTypeComboBox = null!;
    private UiDropDown _defaultModuleComboBox = null!;
    private Button _setDefaultModuleButton = null!;
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

    private readonly List<TopBarIconButton> _enableButtons = [];
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
    private readonly System.Windows.Forms.Timer _gamepadCaptureTimer;
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

    private sealed record ClassModuleSaveResult(
        int SavedCount,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<string> Errors);

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
        _gamepadCaptureTimer = new System.Windows.Forms.Timer
        {
            Interval = GamepadCaptureIntervalMs
        };
        _gamepadCaptureTimer.Tick += HandleGamepadCaptureTick;
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
            UpdateClassConfigAfterSaveAsync);
        _statusForm.AttachConfigEditor(_classConfigEditor);
        _classConfigEditor.DirtyStateChanged += dirty => _statusForm.SetPageDirty(SettingsPage.Config, dirty);
        _classMacrosEditor = new ClassMacrosEditorControl(
            () => Path.Combine(_addonSyncService.SourceRoot, "core", "classmacros.lua"),
            UpdateClassConfigAfterSaveAsync);
        _statusForm.AttachMacrosEditor(_classMacrosEditor);
        _classMacrosEditor.DirtyStateChanged += dirty => _statusForm.SetPageDirty(SettingsPage.Macros, dirty);
        _statusForm.FormClosing += (_, _) =>
        {
            CancelToggleKeyCapture();
            _gamepadCaptureTimer.Dispose();
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
            result = _moduleDependencyService.Import(_moduleStore.GetModulesForImport());
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

        var cleanedModuleCount = 0;
        var cleanupErrors = new List<string>();
        foreach (var module in result.SanitizedModules)
        {
            try
            {
                _moduleStore.SaveDependenciesInPlace(module);
                cleanedModuleCount++;
            }
            catch (Exception ex)
            {
                cleanupErrors.Add($"{module.Name}: {ex.Message}");
            }
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
        foreach (var field in result.RemovedStateFields.Take(50))
        {
            AppendLog($"模块“{field.ModuleName}”已忽略未识别配置字段: {field.Category}.{field.Name}");
        }
        if (result.RemovedStateFields.Count > 0)
        {
            AppendLog($"已从 {cleanedModuleCount} 个模块删除 {result.RemovedStateFields.Count} 个未识别配置字段，未导入本地配置。");
        }
        foreach (var error in cleanupErrors)
        {
            AppendLog($"模块依赖字段清理回写失败: {error}");
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

        if (showFeedback && (result.HasChanges
                             || result.Rejected.Count > 0
                             || result.Conflicts.Count > 0
                             || result.RemovedStateFields.Count > 0))
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
            if (result.RemovedStateFields.Count > 0)
            {
                lines.Add($"已忽略 {result.RemovedStateFields.Count} 个未识别配置字段，并从 {cleanedModuleCount} 个模块中删除；这些字段未写入本地配置。");
            }
            if (cleanupErrors.Count > 0)
            {
                lines.Add($"有 {cleanupErrors.Count} 个模块无法回写清理结果；本次仍未导入这些未识别字段，详情见日志。");
            }
            if (!string.IsNullOrWhiteSpace(postUpdateError))
            {
                lines.Add($"本地依赖已写入，但 config/keymap 或游戏同步更新失败：{postUpdateError}");
            }
            var hasWarning = result.Rejected.Count > 0 || cleanupErrors.Count > 0 || postUpdateError is not null;
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
        ClientSize = new Size(DefaultMainBarLongEdge, DefaultMainBarShortEdge);
        MinimumSize = new Size(MinimumMainBarLongEdge, MinimumMainBarShortEdge);
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

        var enableButton = CreateTopBarIconButton("play-fill", UiTheme.Field, UiTheme.Text, vertical);
        enableButton.Click += (_, _) => ToggleEnabled();
        _settingsToolTip.SetToolTip(enableButton, "开启");
        var settingsButton = CreateTopBarIconButton("gear-wide-connected", UiTheme.Field, UiTheme.Text, vertical);
        settingsButton.Click += (_, _) => ShowSettingsView();
        _settingsToolTip.SetToolTip(settingsButton, "设置");
        var closeButton = CreateTopBarButton(vertical ? "X" : "✕", UiTheme.Field, UiTheme.Muted, vertical);
        closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(196, 43, 28);
        closeButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(153, 27, 21);
        closeButton.Click += (_, _) => Close();
        _settingsToolTip.SetToolTip(closeButton, "关闭");

        _enableButtons.Add(enableButton);
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
        const int settingsContentWidth = 1200;
        const int settingsActionButtonHeight = UiTheme.ActionButtonHeight;
        const int primaryControlWidth = 200;

        var scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            AutoScroll = true,
            Margin = new Padding(0)
        };
        var stack = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = UiTheme.Surface,
            Margin = new Padding(0),
            Padding = new Padding(0),
            Width = settingsContentWidth
        };

        Label CreateRowTitle(string text) => new()
        {
            Text = text,
            AutoSize = true,
            ForeColor = UiTheme.Text,
            BackColor = Color.Transparent,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 2)
        };

        Label CreateRowDescription(string text) => new()
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(720, 0),
            ForeColor = UiTheme.Muted,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };

        Label CreateSectionHeader(string text, bool first = false) => new()
        {
            Text = text,
            AutoSize = true,
            Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            BackColor = Color.Transparent,
            Margin = new Padding(2, first ? 0 : 18, 0, 8)
        };

        FlowLayoutPanel CreateActionsHost() => new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        void SizeActionControl(Control control, int width, int rightGap = 0)
        {
            control.AutoSize = false;
            control.Dock = DockStyle.None;
            control.Size = new Size(width, settingsActionButtonHeight);
            control.Margin = new Padding(0, 0, rightGap, 0);
        }

        UiCardPanel CreateSettingRow(string title, Control description, Control actions)
        {
            var card = new UiCardPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Width = settingsContentWidth,
                MinimumSize = new Size(settingsContentWidth, 0),
                MaximumSize = new Size(settingsContentWidth, 0),
                Padding = new Padding(UiTheme.CardPadding, 14, UiTheme.CardPadding, 14),
                Margin = new Padding(0, 0, 0, UiTheme.PageGap)
            };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            card.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var text = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            text.Controls.Add(CreateRowTitle(title));
            text.Controls.Add(description);

            actions.Margin = new Padding(24, 0, 0, 0);
            // Anchor.None：在自动增高的行里垂直居中，并落在右侧 AutoSize 列。
            actions.Anchor = AnchorStyles.None;
            card.Controls.Add(text, 0, 0);
            card.Controls.Add(actions, 1, 0);
            return card;
        }

        stack.Controls.Add(CreateSectionHeader("输入与运行", first: true));

        _toggleKeyButton = UiTheme.CreateButton("XBUTTON2", UiTheme.ButtonKind.Secondary);
        SizeActionControl(_toggleKeyButton, primaryControlWidth);
        _toggleKeyButton.TextAlign = ContentAlignment.MiddleCenter;
        _toggleKeyButton.Click += (_, _) => BeginCaptureToggleKey();
        _settingsToolTip.SetToolTip(_toggleKeyButton, "点击后按下新的键盘键或鼠标侧键");
        var toggleActions = CreateActionsHost();
        toggleActions.Controls.Add(_toggleKeyButton);
        stack.Controls.Add(CreateSettingRow(
            "触发键",
            CreateRowDescription("点击后按下新的键盘键或鼠标侧键；修改后运行循环会自动重启"),
            toggleActions));

        _modeComboBox = new UiDropDown();
        UiTheme.StyleComboBox(_modeComboBox);
        _modeComboBox.Items.AddRange(new object[] { "开关", "单击", "按住" });
        _modeComboBox.SelectedIndex = 0;
        SizeActionControl(_modeComboBox, primaryControlWidth);
        _settingsToolTip.SetToolTip(_modeComboBox, "开关：按一次切换；单击：每次触发发送一次；按住：持续按下时运行");
        var modeActions = CreateActionsHost();
        modeActions.Controls.Add(_modeComboBox);
        stack.Controls.Add(CreateSettingRow(
            "发送模式",
            CreateRowDescription("开关：按一次切换；单击：每次触发发送一次；按住：持续按下时运行"),
            modeActions));

        stack.Controls.Add(CreateSectionHeader("配置同步"));

        _configSourceLabel = CreateRowDescription("项目目录是唯一配置源；尚未执行手动更新");
        _settingsToolTip.SetToolTip(_configSourceLabel, _configSourceLabel.Text);
        _updateConfigButton = UiTheme.CreateButton("更新配置", UiTheme.ButtonKind.Secondary);
        SizeActionControl(_updateConfigButton, primaryControlWidth);
        _updateConfigButton.Click += async (_, _) => await UpdateConfigFromProjectWithFeedbackAsync();
        var configActions = CreateActionsHost();
        configActions.Controls.Add(_updateConfigButton);
        stack.Controls.Add(CreateSettingRow(
            "更新配置",
            _configSourceLabel,
            configActions));

        stack.Controls.Add(CreateSectionHeader("模块"));

        var moduleDescription = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        moduleDescription.Controls.Add(CreateRowDescription("按实时职业与专精自动匹配，或手动指定模块"));
        _moduleFilterLabel = CreateInfoLabel("筛选: 等待游戏状态");
        _moduleCountLabel = CreateInfoLabel("可选模块: 0");
        moduleDescription.Controls.Add(_moduleFilterLabel);
        moduleDescription.Controls.Add(_moduleCountLabel);

        _moduleComboBox = new UiDropDown();
        UiTheme.StyleComboBox(_moduleComboBox);
        SizeActionControl(_moduleComboBox, 260, rightGap: 10);
        _settingsToolTip.SetToolTip(_moduleComboBox, "列表会根据当前游戏状态筛选可用模块");
        var refreshModulesButton = UiTheme.CreateButton("刷新模块", UiTheme.ButtonKind.Secondary);
        SizeActionControl(refreshModulesButton, primaryControlWidth);
        refreshModulesButton.Click += async (_, _) =>
        {
            await ReloadModulesWithDependenciesAsync();
            RefreshModuleSelector(_lastSnapshot, forceRefresh: false);
            RefreshDefaultModuleSelector();
        };
        var moduleActions = CreateActionsHost();
        moduleActions.Controls.Add(_moduleComboBox);
        moduleActions.Controls.Add(refreshModulesButton);
        stack.Controls.Add(CreateSettingRow("模块选择", moduleDescription, moduleActions));

        _defaultClassComboBox = CreateDefaultFilterComboBox();
        _defaultSpecComboBox = CreateDefaultFilterComboBox();
        _defaultHeroTalentComboBox = CreateDefaultFilterComboBox();
        _defaultPartyTypeComboBox = CreateDefaultFilterComboBox();
        var defaultFilters = new[]
        {
            _defaultClassComboBox,
            _defaultSpecComboBox,
            _defaultHeroTalentComboBox,
            _defaultPartyTypeComboBox
        };
        for (var i = 0; i < defaultFilters.Length; i++)
        {
            var filter = defaultFilters[i];
            filter.AutoSize = false;
            filter.Dock = DockStyle.Fill;
            filter.Margin = new Padding(i == 0 ? 0 : 5, 0, i == defaultFilters.Length - 1 ? 0 : 5, 0);
        }

        _settingsToolTip.SetToolTip(_defaultClassComboBox, "职业");
        _settingsToolTip.SetToolTip(_defaultSpecComboBox, "专精");
        _settingsToolTip.SetToolTip(_defaultHeroTalentComboBox, "英雄天赋");
        _settingsToolTip.SetToolTip(_defaultPartyTypeComboBox, "队伍类型");

        _defaultModuleComboBox = new UiDropDown();
        UiTheme.StyleComboBox(_defaultModuleComboBox);
        SizeActionControl(_defaultModuleComboBox, 200, rightGap: 10);
        _defaultModuleComboBox.Anchor = AnchorStyles.Left;
        _settingsToolTip.SetToolTip(_defaultModuleComboBox, "列表按上方条件筛选；已保存的模块会标记为“当前默认”");
        _setDefaultModuleButton = UiTheme.CreateButton("设为默认", UiTheme.ButtonKind.Secondary);
        SizeActionControl(_setDefaultModuleButton, primaryControlWidth);
        _setDefaultModuleButton.Click += HandleSetDefaultModuleClick;

        var defaultModuleCard = new UiCardPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            Width = settingsContentWidth,
            MinimumSize = new Size(settingsContentWidth, 0),
            MaximumSize = new Size(settingsContentWidth, 0),
            Padding = new Padding(UiTheme.CardPadding, 14, UiTheme.CardPadding, 14),
            Margin = new Padding(0, 0, 0, UiTheme.PageGap)
        };
        defaultModuleCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        defaultModuleCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        defaultModuleCard.RowStyles.Add(new RowStyle(SizeType.Absolute, settingsActionButtonHeight + 12));
        defaultModuleCard.RowStyles.Add(new RowStyle(SizeType.Absolute, settingsActionButtonHeight + 4));

        var defaultModuleHeader = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(0)
        };
        defaultModuleHeader.Controls.Add(CreateRowTitle("默认模块"));
        defaultModuleHeader.Controls.Add(
            CreateRowDescription("为指定环境设置自动选择时优先使用的模块，并将选中项保存为默认"));
        defaultModuleCard.Controls.Add(defaultModuleHeader, 0, 0);

        var defaultFilterRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        for (var i = 0; i < 4; i++)
        {
            defaultFilterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        }

        defaultFilterRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (var i = 0; i < defaultFilters.Length; i++)
        {
            defaultFilterRow.Controls.Add(defaultFilters[i], i, 0);
        }

        defaultModuleCard.Controls.Add(defaultFilterRow, 0, 1);

        var defaultModuleRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        defaultModuleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        defaultModuleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        defaultModuleRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _defaultModuleComboBox.Anchor = AnchorStyles.Left;
        defaultModuleRow.Controls.Add(_defaultModuleComboBox, 0, 0);
        _setDefaultModuleButton.Anchor = AnchorStyles.Right;
        defaultModuleRow.Controls.Add(_setDefaultModuleButton, 1, 0);
        defaultModuleCard.Controls.Add(defaultModuleRow, 0, 2);
        stack.Controls.Add(defaultModuleCard);

        ResetDefaultClassOptions();
        ResetDefaultSpecOptions(null);
        ResetDefaultHeroTalentOptions(null, null);
        ResetDefaultPartyTypeOptions();
        _defaultClassComboBox.SelectedIndexChanged += (_, _) =>
        {
            var classId = ReadDefaultFilterValue(_defaultClassComboBox);
            ResetDefaultSpecOptions(classId);
            ResetDefaultHeroTalentOptions(classId, ReadDefaultFilterValue(_defaultSpecComboBox));
            RefreshDefaultModuleSelector();
        };
        _defaultSpecComboBox.SelectedIndexChanged += (_, _) =>
        {
            ResetDefaultHeroTalentOptions(
                ReadDefaultFilterValue(_defaultClassComboBox),
                ReadDefaultFilterValue(_defaultSpecComboBox));
            RefreshDefaultModuleSelector();
        };
        _defaultHeroTalentComboBox.SelectedIndexChanged += (_, _) => RefreshDefaultModuleSelector();
        _defaultPartyTypeComboBox.SelectedIndexChanged += (_, _) => RefreshDefaultModuleSelector();

        var moduleWebsiteLabel = CreateRowDescription(ModuleWebsiteUrl);
        moduleWebsiteLabel.ForeColor = UiTheme.Accent;
        moduleWebsiteLabel.Cursor = Cursors.Hand;
        moduleWebsiteLabel.Click += (_, _) => OpenModuleWebsite();
        _settingsToolTip.SetToolTip(moduleWebsiteLabel, $"在默认浏览器中打开 {ModuleWebsiteUrl}");

        var moduleWebsiteButtonColor = Color.FromArgb(252, 238, 10);
        var openModuleWebsiteButton = UiTheme.CreateButton("获取模块", moduleWebsiteButtonColor, Color.Black);
        SizeActionControl(openModuleWebsiteButton, primaryControlWidth, rightGap: 10);
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
        SizeActionControl(openModuleDirectoryButton, primaryControlWidth);
        openModuleDirectoryButton.Click += (_, _) => OpenModuleDirectory();
        _settingsToolTip.SetToolTip(openModuleDirectoryButton, "在资源管理器中打开本地模块目录");

        var getModuleActions = CreateActionsHost();
        getModuleActions.Controls.Add(openModuleWebsiteButton);
        getModuleActions.Controls.Add(openModuleDirectoryButton);
        stack.Controls.Add(CreateSettingRow("获取模块", moduleWebsiteLabel, getModuleActions));

        stack.Controls.Add(CreateSectionHeader("界面"));

        _horizontalLayoutButton = UiTheme.CreateButton("横向布局", UiTheme.ButtonKind.Secondary);
        SizeActionControl(_horizontalLayoutButton, primaryControlWidth, rightGap: 10);
        _horizontalLayoutButton.Click += (_, _) => SetMainWindowLayout(MainWindowLayout.Horizontal);
        _verticalLayoutButton = UiTheme.CreateButton("纵向布局", UiTheme.ButtonKind.Secondary);
        SizeActionControl(_verticalLayoutButton, primaryControlWidth);
        _verticalLayoutButton.Click += (_, _) => SetMainWindowLayout(MainWindowLayout.Vertical);
        var layoutActions = CreateActionsHost();
        layoutActions.Controls.Add(_horizontalLayoutButton);
        layoutActions.Controls.Add(_verticalLayoutButton);
        stack.Controls.Add(CreateSettingRow(
            "界面布局",
            CreateRowDescription("选择主界面浮动条的排列方向；切换时会交换宽高"),
            layoutActions));

        _minimizeToTrayButton = UiTheme.CreateButton("最小化到系统栏", UiTheme.ButtonKind.Secondary);
        SizeActionControl(_minimizeToTrayButton, primaryControlWidth, rightGap: 10);
        _minimizeToTrayButton.Click += (_, _) => SetCloseButtonBehavior(CloseButtonBehavior.MinimizeToTray);
        _exitOnCloseButton = UiTheme.CreateButton("完全退出Shigure", UiTheme.ButtonKind.Secondary);
        SizeActionControl(_exitOnCloseButton, primaryControlWidth);
        _exitOnCloseButton.Click += (_, _) => SetCloseButtonBehavior(CloseButtonBehavior.Exit);
        var closeBehaviorActions = CreateActionsHost();
        closeBehaviorActions.Controls.Add(_minimizeToTrayButton);
        closeBehaviorActions.Controls.Add(_exitOnCloseButton);
        stack.Controls.Add(CreateSettingRow(
            "点击 X 时",
            CreateRowDescription("最小化后可通过系统栏图标重新打开；完全退出会停止运行"),
            closeBehaviorActions));

        stack.Controls.Add(CreateSectionHeader("资源"));

        _spellIconPackageStatusLabel = CreateRowDescription(
            "从 GitHub Release 下载或更新技能/物品图标数据包");
        _downloadSpellIconPackageButton = UiTheme.CreateButton("下载数据包", UiTheme.ButtonKind.Secondary);
        SizeActionControl(_downloadSpellIconPackageButton, primaryControlWidth);
        _downloadSpellIconPackageButton.Click += (_, _) => StartSpellIconPackageDownload();
        var spellIconActions = CreateActionsHost();
        spellIconActions.Controls.Add(_downloadSpellIconPackageButton);
        stack.Controls.Add(CreateSettingRow(
            "下载数据包",
            _spellIconPackageStatusLabel,
            spellIconActions));

        UpdateLayoutButtons();
        UpdateCloseBehaviorButtons();
        UpdateSpellIconPackageCard();
        RefreshDefaultModuleSelector();

        void SyncContentLayout()
        {
            if (stack.Width != settingsContentWidth)
            {
                stack.Width = settingsContentWidth;
            }

            var viewWidth = scrollHost.ClientSize.Width;
            var left = viewWidth > settingsContentWidth
                ? (viewWidth - settingsContentWidth) / 2
                : 0;
            if (stack.Left != left)
            {
                stack.Left = left;
            }

            if (stack.Top != 0)
            {
                stack.Top = 0;
            }

            var minSize = new Size(settingsContentWidth, 0);
            if (scrollHost.AutoScrollMinSize != minSize)
            {
                scrollHost.AutoScrollMinSize = minSize;
            }
        }

        scrollHost.Controls.Add(stack);
        scrollHost.Resize += (_, _) => SyncContentLayout();
        scrollHost.HandleCreated += (_, _) => BeginInvoke(SyncContentLayout);
        stack.SizeChanged += (_, _) => SyncContentLayout();
        SyncContentLayout();
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

        AppendLog("开始检查 GitHub 技能/物品图标数据包");
        try
        {
            var result = await _spellIconPackageDownloadService.UpdateAsync(progress, cancellationToken);
            if (_shutdownStarted)
            {
                return;
            }

            var sizeText = $"{result.Size / 1024d / 1024d:F2} MiB";
            var hashText = result.Sha256[..Math.Min(12, result.Sha256.Length)];
            var kind = SpellIconCatalog.IsItemDatabaseAvailable ? "完整包" : "仅技能旧包";
            if (result.UpToDate)
            {
                _spellIconPackageStatusLabel.Text = $"已是最新（{kind}）：{sizeText}，SHA-256 {hashText}…";
                AppendLog("技能/物品图标数据包已是最新，本地文件未修改");
            }
            else
            {
                _spellIconPackageStatusLabel.Text = $"安装完成（{kind}）：{sizeText}，SHA-256 {hashText}…";
                AppendLog("技能/物品图标数据包已下载、校验并热加载");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!_shutdownStarted)
            {
                _spellIconPackageStatusLabel.Text = "下载已取消；原数据包未修改。";
                AppendLog("技能/物品图标数据包下载已取消");
            }
        }
        catch (Exception ex)
        {
            if (!_shutdownStarted)
            {
                var releaseUrl = SpellIconPackageDownloadService.ReleasesPageUrl;
                var status = $"下载失败，请到 {releaseUrl} 手动下载 SpellIcons.shgpack。";
                _spellIconPackageStatusLabel.Text = status;
                _settingsToolTip.SetToolTip(
                    _spellIconPackageStatusLabel,
                    status + Environment.NewLine + Environment.NewLine + ex);
                AppendLog($"技能/物品图标数据包下载失败: {ex.Message}");
                var openPage = MessageBox.Show(
                    this,
                    "自动下载失败。请到以下页面手动下载 SpellIcons.shgpack，放到程序目录的 data 文件夹："
                    + Environment.NewLine
                    + Environment.NewLine
                    + releaseUrl
                    + Environment.NewLine
                    + Environment.NewLine
                    + $"保存位置：{SpellIconCatalog.PackagePath}"
                    + Environment.NewLine
                    + Environment.NewLine
                    + $"原因：{ex.Message}"
                    + Environment.NewLine
                    + Environment.NewLine
                    + "是否现在打开下载页面？",
                    "下载数据包失败",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (openPage == DialogResult.Yes)
                {
                    OpenSpellIconReleasePage();
                }
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
            var sizeText = $"{length / 1024d / 1024d:F2} MiB";
            _spellIconPackageStatusLabel.Text = SpellIconCatalog.IsItemDatabaseAvailable
                ? $"已安装完整包：{sizeText}。点击检查 GitHub 更新。"
                : $"已安装仅技能旧包：{sizeText}。物品搜索库不可用，可检查更新以获取完整包。";
            _downloadSpellIconPackageButton.Text = "检查更新";
        }
        else if (File.Exists(packagePath))
        {
            _spellIconPackageStatusLabel.Text = "本地数据包损坏或格式不受支持；技能/物品图标与添加联想不可用。";
            _downloadSpellIconPackageButton.Text = "重新下载";
        }
        else
        {
            _spellIconPackageStatusLabel.Text = "未安装；技能/物品图标与添加技能、物品联想不可用。";
            _downloadSpellIconPackageButton.Text = "下载数据包";
        }

        _downloadSpellIconPackageButton.Enabled = true;
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

    private void OpenSpellIconReleasePage()
    {
        var releaseUrl = SpellIconPackageDownloadService.ReleasesPageUrl;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(releaseUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"无法打开下载页面：{ex.Message}"
                + Environment.NewLine
                + Environment.NewLine
                + releaseUrl,
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

    private async Task<ClassConfigPostSaveResult> UpdateClassConfigAfterSaveAsync(
        string savedAddonFilePath,
        int classId)
    {
        var moduleResult = SaveModulesForClass(classId);
        var result = await QueueProjectConfigUpdateAsync(savedAddonFilePath);
        if (moduleResult.Errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"该职业有 {moduleResult.Errors.Count} 个模块保存失败；"
                + $"已成功保存 {moduleResult.SavedCount} 个模块。详情见日志。");
        }

        return new ClassConfigPostSaveResult(
            DescribeAddonSyncIssue(result.AddonSync),
            moduleResult.SavedCount,
            moduleResult.Warnings);
    }

    private ClassModuleSaveResult SaveModulesForClass(int classId)
    {
        var modules = _moduleStore.GetModulesForDisplay()
            .Where(module => module.Match.ClassId == classId)
            .ToList();
        var warnings = new List<string>();
        var errors = new List<string>();
        var savedCount = 0;

        foreach (var module in modules)
        {
            try
            {
                var warning = _moduleDependencyService.Capture(module);
                _moduleStore.SaveDependenciesInPlace(module);
                savedCount++;
                if (!string.IsNullOrWhiteSpace(warning))
                {
                    warnings.Add($"{module.Name}: {warning}");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{module.Name}: {ex.Message}");
            }
        }

        var className = ClassNames.GetClassAndSpecName(classId, null).ClassName ?? $"职业{classId}";
        AppendLog($"已同步保存 {savedCount}/{modules.Count} 个{className}模块。");
        foreach (var warning in warnings)
        {
            AppendLog($"模块依赖保存提示: {warning}");
        }
        foreach (var error in errors)
        {
            AppendLog($"模块依赖保存失败: {error}");
        }

        RefreshModuleSelector(_lastSnapshot, forceRefresh: true);
        RefreshDefaultModuleSelector();
        return new ClassModuleSaveResult(savedCount, warnings, errors);
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
        RefreshDefaultModuleSelector();
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
            enableButton.IconName = snapshot.Enabled ? "stop-fill" : "play-fill";
            _settingsToolTip.SetToolTip(enableButton, snapshot.Enabled ? "关闭" : "开启");
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

    private UiDropDown CreateDefaultFilterComboBox()
    {
        var comboBox = new UiDropDown();
        UiTheme.StyleComboBox(comboBox);
        comboBox.Dock = DockStyle.Fill;
        comboBox.Margin = new Padding(0, 4, 8, 4);
        return comboBox;
    }

    private void ResetDefaultClassOptions()
    {
        _defaultClassComboBox.Items.Clear();
        _defaultClassComboBox.Items.Add(new DefaultFilterOption("职业：任意", null));
        foreach (var item in ClassNames.GetClasses())
        {
            _defaultClassComboBox.Items.Add(new DefaultFilterOption($"职业：{item.Name}", item.Id));
        }

        _defaultClassComboBox.SelectedIndex = 0;
    }

    private void ResetDefaultSpecOptions(int? classId)
    {
        _defaultSpecComboBox.Items.Clear();
        _defaultSpecComboBox.Items.Add(new DefaultFilterOption("专精：任意", null));
        if (classId is not null)
        {
            foreach (var item in ClassNames.GetSpecs(classId.Value))
            {
                _defaultSpecComboBox.Items.Add(new DefaultFilterOption($"专精：{item.Name}", item.Id));
            }
        }

        _defaultSpecComboBox.SelectedIndex = 0;
    }

    private void ResetDefaultHeroTalentOptions(int? classId, int? specId)
    {
        _defaultHeroTalentComboBox.Items.Clear();
        _defaultHeroTalentComboBox.Items.Add(new DefaultFilterOption("英雄天赋：任意", null));
        if (classId is not null && specId is not null)
        {
            foreach (var item in ClassNames.GetHeroTalents(classId.Value, specId.Value))
            {
                _defaultHeroTalentComboBox.Items.Add(new DefaultFilterOption($"英雄天赋：{item.Name}", item.Id));
            }
        }

        _defaultHeroTalentComboBox.SelectedIndex = 0;
    }

    private void ResetDefaultPartyTypeOptions()
    {
        _defaultPartyTypeComboBox.Items.Clear();
        _defaultPartyTypeComboBox.Items.AddRange(
        [
            new DefaultPartyTypeOption("队伍类型：任意", null),
            new DefaultPartyTypeOption("队伍类型：单人", "0"),
            new DefaultPartyTypeOption("队伍类型：团队", "1-40"),
            new DefaultPartyTypeOption("队伍类型：队伍", "46")
        ]);
        _defaultPartyTypeComboBox.SelectedIndex = 0;
    }

    private void RefreshDefaultModuleSelector()
    {
        if (_defaultModuleComboBox is null)
        {
            return;
        }

        var classId = ReadDefaultFilterValue(_defaultClassComboBox);
        var specId = ReadDefaultFilterValue(_defaultSpecComboBox);
        var heroTalent = ReadDefaultFilterValue(_defaultHeroTalentComboBox);
        var partyType = ReadDefaultPartyTypeValue();
        var currentDefault = (_uiCache.DefaultModules ?? [])
            .LastOrDefault(selection => selection.HasSameFilter(classId, specId, partyType, heroTalent));
        var modules = _moduleStore.GetModules()
            .Where(module => ModuleMatchesDefaultFilter(module.Match, classId, specId, partyType, heroTalent))
            .ToList();

        _defaultModuleComboBox.BeginUpdate();
        try
        {
            _defaultModuleComboBox.Items.Clear();
            foreach (var module in modules)
            {
                var isCurrentDefault = string.Equals(
                    module.Id,
                    currentDefault?.ModuleId,
                    StringComparison.OrdinalIgnoreCase);
                var text = ModuleDisplay.FormatListItem(module)
                    + (isCurrentDefault ? "（当前默认）" : string.Empty);
                _defaultModuleComboBox.Items.Add(new DefaultModuleOption(module.Id, text));
            }

            if (_defaultModuleComboBox.Items.Count == 0)
            {
                _defaultModuleComboBox.Items.Add(DefaultModuleOption.Empty);
                _defaultModuleComboBox.SelectedIndex = 0;
                _setDefaultModuleButton.Enabled = false;
                return;
            }

            var selectedIndex = 0;
            if (currentDefault is not null)
            {
                for (var i = 0; i < _defaultModuleComboBox.Items.Count; i++)
                {
                    if (_defaultModuleComboBox.Items[i] is DefaultModuleOption option
                        && string.Equals(option.ModuleId, currentDefault.ModuleId, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }

            _defaultModuleComboBox.SelectedIndex = selectedIndex;
            _setDefaultModuleButton.Enabled = true;
        }
        finally
        {
            _defaultModuleComboBox.EndUpdate();
        }
    }

    private static bool ModuleMatchesDefaultFilter(
        ModuleMatch match,
        int? classId,
        int? specId,
        string? partyType,
        int? heroTalent)
    {
        if (classId is not null && match.ClassId is not null && match.ClassId != classId)
        {
            return false;
        }

        if (specId is not null && match.SpecId is not null && match.SpecId != specId)
        {
            return false;
        }

        if (heroTalent is not null && match.HeroTalent is not null && match.HeroTalent != heroTalent)
        {
            return false;
        }

        var normalizedFilter = ModuleMatch.NormalizePartyTypeValue(partyType);
        var normalizedMatch = ModuleMatch.NormalizePartyTypeValue(match.PartyType);
        return normalizedFilter is null
            || normalizedMatch is null
            || string.Equals(normalizedFilter, normalizedMatch, StringComparison.OrdinalIgnoreCase);
    }

    private async void HandleSetDefaultModuleClick(object? sender, EventArgs e)
    {
        if (_defaultModuleComboBox.SelectedItem is not DefaultModuleOption { ModuleId: not null } option)
        {
            return;
        }

        var classId = ReadDefaultFilterValue(_defaultClassComboBox);
        var specId = ReadDefaultFilterValue(_defaultSpecComboBox);
        var heroTalent = ReadDefaultFilterValue(_defaultHeroTalentComboBox);
        var partyType = ReadDefaultPartyTypeValue();
        _uiCache.DefaultModules ??= [];
        _uiCache.DefaultModules.RemoveAll(
            selection => selection.HasSameFilter(classId, specId, partyType, heroTalent));
        _uiCache.DefaultModules.Add(new DefaultModuleSelection
        {
            ClassId = classId,
            SpecId = specId,
            HeroTalent = heroTalent,
            PartyType = partyType,
            ModuleId = option.ModuleId
        });
        SaveUiCache();
        RefreshDefaultModuleSelector();
        AppendLog($"默认模块: {option.Text}");

        if (_runtimeSession.HasSession)
        {
            AppendLog("默认模块已变更, 重新启动运行");
            await StartOrRestartRuntimeAsync(restart: true);
        }
    }

    private static int? ReadDefaultFilterValue(UiDropDown comboBox)
        => comboBox.SelectedItem is DefaultFilterOption option ? option.Value : null;

    private string? ReadDefaultPartyTypeValue()
        => _defaultPartyTypeComboBox.SelectedItem is DefaultPartyTypeOption option ? option.Value : null;

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
        _gamepadCaptureTimer.Start();
    }

    private void HandleGamepadCaptureTick(object? sender, EventArgs e)
    {
        if (!_isCapturingToggleKey)
        {
            _gamepadCaptureTimer.Stop();
            return;
        }

        foreach (var keyName in WindowsTriggerKeyState.GamepadKeyNames)
        {
            var virtualKey = _triggerKeyState.ResolveVirtualKey(keyName);
            if (virtualKey is not null && _triggerKeyState.Read(virtualKey.Value).IsDown)
            {
                _isCapturingToggleKey = false;
                _gamepadCaptureTimer.Stop();
                _toggleKeyName = keyName;
                SetToggleKeyButtonText();
                SaveUiCache();
                AppendLog($"已录入触发键: {_toggleKeyName}");
                HandleSettingCommitted(this, EventArgs.Empty);
                return;
            }
        }
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
            _gamepadCaptureTimer.Stop();
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
            _gamepadCaptureTimer.Stop();
            return true;
        }

        var keyName = TryMapKeyToHotkey(key);
        if (keyName is null)
        {
            _toggleKeyButton.Text = "不支持";
            AppendLog("该按键暂不支持, 请重试");
            _ = ResetCaptureButtonTextAsync();
            _isCapturingToggleKey = false;
            _gamepadCaptureTimer.Stop();
            return true;
        }

        _isCapturingToggleKey = false;
        _gamepadCaptureTimer.Stop();
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
        _gamepadCaptureTimer.Stop();
        _toggleKeyName = keyName;
        SetToggleKeyButtonText();
        SaveUiCache();
        AppendLog($"已录入触发键: {_toggleKeyName}");
        HandleSettingCommitted(this, EventArgs.Empty);
        return true;
    }

    private void ApplyCachedWindowState()
    {
        MigrateMainBarWindowSizeIfNeeded();

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

    private void MigrateMainBarWindowSizeIfNeeded()
    {
        if (_uiCache.MainBarSizeVersion >= MainBarSizeVersion)
        {
            return;
        }

        // 顶栏默认宽度从 680 缩到 476 后，旧缓存尺寸会盖住新默认值；只缩窄长边，保留位置与短边。
        ScaleMainBarLongEdge(_uiCache.HorizontalMainWindowBounds, vertical: false);
        ScaleMainBarLongEdge(_uiCache.VerticalMainWindowBounds, vertical: true);
        var legacyLayout = ParseMainWindowLayout(_uiCache.MainWindowLayout);
        ScaleMainBarLongEdge(_uiCache.MainWindowBounds, vertical: legacyLayout == MainWindowLayout.Vertical);
        _uiCache.MainBarSizeVersion = MainBarSizeVersion;
    }

    private static void ScaleMainBarLongEdge(WindowBounds? bounds, bool vertical)
    {
        if (bounds is null)
        {
            return;
        }

        if (vertical)
        {
            bounds.Height = Math.Max(
                MinimumMainBarLongEdge,
                (int)Math.Round(bounds.Height * 0.7));
        }
        else
        {
            bounds.Width = Math.Max(
                MinimumMainBarLongEdge,
                (int)Math.Round(bounds.Width * 0.7));
        }
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
        _uiCache.MainBarSizeVersion = MainBarSizeVersion;
        _uiCache.ToggleKey = _toggleKeyName;
        _uiCache.SelectedModuleId = _selectedModuleId;
        UiCacheStore.Save(_uiCache);
    }

    private void ShowSettingsView()
    {
        RefreshModuleSelector(_lastSnapshot, forceRefresh: false);
        RefreshDefaultModuleSelector();
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
        _gamepadCaptureTimer.Stop();
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
        button.Size = new Size(36, 36);
        button.Padding = new Padding(0);
    }

    private static TopBarIconButton CreateTopBarIconButton(
        string iconName,
        Color backColor,
        Color foreColor,
        bool vertical)
    {
        var button = new TopBarIconButton();
        UiTheme.StyleButton(button, string.Empty, backColor, foreColor);
        button.IconName = iconName;
        ConfigureTopBarButton(button);
        button.Margin = vertical
            ? new Padding(0, TopBarButtonGap, 0, 0)
            : new Padding(TopBarButtonGap, 0, 0, 0);
        return button;
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
        button.Margin = vertical
            ? new Padding(0, TopBarButtonGap, 0, 0)
            : new Padding(TopBarButtonGap, 0, 0, 0);
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
            MinimumSize = vertical
                ? new Size(MinimumMainBarShortEdge, MinimumMainBarLongEdge)
                : new Size(MinimumMainBarLongEdge, MinimumMainBarShortEdge);
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

    private sealed class TopBarIconButton : Button
    {
        private string _iconName = string.Empty;
        private bool _suppressBaseText;

        [System.ComponentModel.DesignerSerializationVisibility(
            System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string IconName
        {
            get => _iconName;
            set
            {
                var next = value ?? string.Empty;
                if (string.Equals(_iconName, next, StringComparison.Ordinal))
                {
                    return;
                }

                _iconName = next;
                Invalidate();
            }
        }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string Text
        {
            get => _suppressBaseText ? string.Empty : base.Text;
            set => base.Text = string.Empty;
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

            if (string.IsNullOrEmpty(_iconName) || ClientSize.Width <= 1 || ClientSize.Height <= 1)
            {
                return;
            }

            var scale = Math.Max(1f, DeviceDpi / 96f);
            var iconSize = Math.Max(14, (int)Math.Round(16 * scale));
            iconSize = Math.Min(iconSize, Math.Min(ClientSize.Width, ClientSize.Height) - 8);
            if (iconSize <= 0)
            {
                return;
            }

            UiIconCatalog.Draw(
                pevent.Graphics,
                _iconName,
                new Rectangle(
                    (ClientSize.Width - iconSize) / 2,
                    (ClientSize.Height - iconSize) / 2,
                    iconSize,
                    iconSize),
                ForeColor);
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

    private sealed record DefaultFilterOption(string Text, int? Value)
    {
        public override string ToString() => Text;
    }

    private sealed record DefaultPartyTypeOption(string Text, string? Value)
    {
        public override string ToString() => Text;
    }

    private sealed record DefaultModuleOption(string? ModuleId, string Text)
    {
        public static readonly DefaultModuleOption Empty = new(null, "暂无符合筛选的模块");

        public override string ToString() => Text;
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
