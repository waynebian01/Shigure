using System.Drawing;

namespace Shigure;

public sealed class MainForm : Form, IMessageFilter
{
    private const int ResizeGripSize = 8;

    private Button _toggleKeyButton = null!;
    private ComboBox _modeComboBox = null!;
    private Button _settingsButton = null!;
    private string _toggleKeyName = "XBUTTON2";
    private bool _isCapturingToggleKey;

    private Button _enableButton = null!;

    private Label _titleLabel = null!;
    private Label _runtimeStatusLabel = null!;

    private readonly StatusForm _statusForm;
    private readonly ModuleStore _moduleStore;
    private readonly AppOptions _initialOptions;
    private readonly UiCacheState _uiCache;
    private ShigureRuntime? _runtime;
    private CancellationTokenSource? _runtimeCts;
    private Task? _runtimeTask;
    private RenderSnapshot? _lastSnapshot;
    private string? _lastLoggedStep;
    private string? _lastLoggedClass;
    private string? _lastLoggedModule;
    private bool? _lastLoggedEnabled;

    public MainForm(AppOptions? initialOptions = null)
    {
        _initialOptions = initialOptions ?? AppOptions.FromArgs(Array.Empty<string>());
        _uiCache = UiCacheStore.Load();
        _moduleStore = new ModuleStore(ModuleStore.ResolveModuleDirectory(AppContext.BaseDirectory));
        _statusForm = new StatusForm();
        Application.AddMessageFilter(this);
        InitializeComponent();
        _statusForm.AttachSettingsPanel(BuildSettingsPanel());
        _statusForm.AttachModuleEditor(new ModuleEditorControl(_moduleStore, RestartRuntimeFromEditor, AppContext.BaseDirectory));
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
        UiTheme.ApplyRoundedCorners(this);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        StartRuntime();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveUiCache();
        Application.RemoveMessageFilter(this);
        _runtimeCts?.Cancel();
        base.OnFormClosing(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (IsHandleCreated)
        {
            UiTheme.ApplyRoundedCorners(this);
        }
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

        _titleLabel = new Label
        {
            Text = "Shigure",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font(Font.FontFamily, 13F, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Margin = new Padding(2, 0, 12, 0)
        };

        _runtimeStatusLabel = new Label
        {
            Text = string.Empty,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UiTheme.Muted
        };

        EnableDrag(bar);
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
        _settingsButton.Click += (_, _) => _statusForm.ShowSettings(_lastSnapshot);

        var closeButton = UiTheme.CreateButton("✕", UiTheme.Field, UiTheme.Muted);
        ConfigureTopBarButton(closeButton);
        closeButton.FlatAppearance.MouseOverBackColor = UiTheme.Danger;
        closeButton.Click += (_, _) => Close();

        buttons.Controls.AddRange(new Control[] { _enableButton, _settingsButton, closeButton });

        bar.Controls.Add(_titleLabel, 0, 0);
        bar.Controls.Add(_runtimeStatusLabel, 1, 0);
        bar.Controls.Add(buttons, 2, 0);
        return bar;
    }

    private Control BuildSettingsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding = new Padding(4),
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
            BackColor = UiTheme.Surface,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0)
        };
        settingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        settingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settingsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        settingsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        Label CreateSettingLabel(string text) => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UiTheme.Muted,
            Margin = new Padding(0, 0, 10, 8)
        };

        const int settingControlWidth = 168;

        _toggleKeyButton = UiTheme.CreateButton("XBUTTON2", UiTheme.Field, UiTheme.Text);
        _toggleKeyButton.AutoSize = false;
        _toggleKeyButton.Width = settingControlWidth;
        _toggleKeyButton.Height = 30;
        _toggleKeyButton.Padding = new Padding(6, 1, 6, 1);
        _toggleKeyButton.Anchor = AnchorStyles.Left;
        _toggleKeyButton.Margin = new Padding(0, 0, 0, 8);
        _toggleKeyButton.Click += (_, _) => BeginCaptureToggleKey();
        settingsGrid.Controls.Add(CreateSettingLabel("触发键"), 0, 0);
        settingsGrid.Controls.Add(_toggleKeyButton, 1, 0);

        _modeComboBox = new ComboBox();
        UiTheme.StyleComboBox(_modeComboBox);
        _modeComboBox.Items.AddRange(new object[] { "开关", "单击", "按住" });
        _modeComboBox.SelectedIndex = 0;
        _modeComboBox.Width = settingControlWidth;
        _modeComboBox.Anchor = AnchorStyles.Left;
        _modeComboBox.Margin = new Padding(0, 0, 0, 8);
        settingsGrid.Controls.Add(CreateSettingLabel("发送模式"), 0, 1);
        settingsGrid.Controls.Add(_modeComboBox, 1, 1);

        panel.Controls.Add(settingsGrid, 0, 0);
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
        SetToggleKeyButtonText();
        _modeComboBox.SelectedIndex = _initialOptions.Mode switch
        {
            SendMode.Click => 1,
            SendMode.Hold => 2,
            _ => 0
        };
    }

    private void WireSettingEvents()
    {
        _modeComboBox.SelectedIndexChanged += HandleSettingCommitted;
    }

    private async void HandleSettingCommitted(object? sender, EventArgs e)
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
            _runtime = new ShigureRuntime(AppContext.BaseDirectory, options, _moduleStore);
            _runtime.SnapshotUpdated += HandleSnapshotUpdated;
            _runtimeTask = Task.Run(() => RunRuntimeAsync(_runtime, _runtimeCts.Token));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppendLog($"启动失败: {ex.Message}");
            return;
        }

        _lastLoggedStep = null;
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

        return _initialOptions with { ToggleKey = toggleKey, Mode = ReadMode() };
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

    private void ApplySnapshot(RenderSnapshot snapshot)
    {
        _lastSnapshot = snapshot;

        UpdateLogicStatusLabel(snapshot.Enabled);
        _enableButton.Text = snapshot.Enabled ? "关闭" : "开启";

        _statusForm.ApplySnapshot(snapshot);
        WriteSnapshotLog(snapshot);
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

        if (!string.IsNullOrWhiteSpace(snapshot.CurrentStep) && snapshot.CurrentStep != _lastLoggedStep)
        {
            _lastLoggedStep = snapshot.CurrentStep;
            AppendLog($"步骤: {snapshot.CurrentStep}");
        }
    }

    private void SetRuntimeControls(bool running)
    {
        if (!running)
        {
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
        _statusForm.ShowSettings(_lastSnapshot);

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
        UiCacheStore.Save(_uiCache);
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
