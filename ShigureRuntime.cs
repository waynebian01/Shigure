using System.Diagnostics;

namespace Shigure;

public sealed class ShigureRuntime
{
    private readonly AppOptions _options;
    private readonly ConfigService _config;
    private readonly KeymapService _keymap;
    private readonly PixelScanner _scanner;
    private readonly StateBuilder _stateBuilder;
    private readonly KeySender _keySender;
    private readonly LogicRegistry _logicRegistry;

    private GameState? _state;
    private string? _className;
    private string? _specName;
    private int? _classId;
    private int? _specId;
    private string? _moduleName;
    private string _currentStep = "等待启动";
    private IReadOnlyDictionary<string, object?> _unitInfo = new Dictionary<string, object?>();
    private bool _enabled;
    private bool _clickPending;
    private double _scanMs;

    public ShigureRuntime(string baseDirectory, AppOptions options, ModuleStore moduleStore)
    {
        _options = options;
        _config = new ConfigService(Path.Combine(baseDirectory, "config.json"));
        _keymap = new KeymapService(baseDirectory, _config);
        _scanner = new PixelScanner(options.WindowTitle);
        _stateBuilder = new StateBuilder(_config);
        _keySender = new KeySender(options.WindowTitle);
        _logicRegistry = new LogicRegistry(_keymap, moduleStore);
    }

    public event Action<RenderSnapshot>? SnapshotUpdated;

    public AppOptions Options => _options;

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        _clickPending = false;
        _currentStep = enabled ? "手动开启" : "手动关闭";
        PublishSnapshot();
    }

    public void TriggerOnce()
    {
        _enabled = true;
        _clickPending = true;
        _currentStep = "单次触发";
        PublishSnapshot();
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var toggleVk = KeySender.GetVk(_options.ToggleKey);
        if (toggleVk is null)
        {
            _currentStep = $"无法识别触发键: {_options.ToggleKey}";
            PublishSnapshot();
            return;
        }

        var previousPressed = false;
        var lastLogicAt = DateTimeOffset.MinValue;
        var lastRenderAt = DateTimeOffset.MinValue;
        var lastToggleAt = DateTimeOffset.MinValue;
        _currentStep = "已启动";
        PublishSnapshot();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = DateTimeOffset.UtcNow;
                var pressed = NativeMethods.IsKeyDown(toggleVk.Value);
                var rising = pressed && !previousPressed && now - lastToggleAt >= TimeSpan.FromMilliseconds(120);
                var falling = !pressed && previousPressed;

                if (rising)
                {
                    lastToggleAt = now;
                    HandleRisingEdge();
                }

                if (_options.Mode == SendMode.Hold)
                {
                    _enabled = pressed;
                    if (falling)
                    {
                        _currentStep = "按住结束";
                    }
                }

                previousPressed = pressed;

                if (now - lastLogicAt >= _options.LogicInterval)
                {
                    lastLogicAt = now;
                    TickLogic();
                }

                if (now - lastRenderAt >= _options.RenderInterval)
                {
                    lastRenderAt = now;
                    PublishSnapshot();
                }

                await Task.Delay(25, cancellationToken);
            }
        }
        finally
        {
            _enabled = false;
            _clickPending = false;
            _currentStep = "已停止";
            PublishSnapshot();
        }
    }

    private void HandleRisingEdge()
    {
        switch (_options.Mode)
        {
            case SendMode.Click:
                _enabled = true;
                _clickPending = true;
                _currentStep = "单击触发";
                break;
            case SendMode.Hold:
                _enabled = true;
                _currentStep = "按住触发";
                break;
            default:
                _enabled = !_enabled;
                _clickPending = false;
                _currentStep = _enabled ? "逻辑开启" : "逻辑关闭";
                break;
        }
    }

    private void TickLogic()
    {
        var sw = Stopwatch.StartNew();
        var scan = _scanner.ScanScreenData();
        sw.Stop();
        _scanMs = sw.Elapsed.TotalMilliseconds;

        if (scan.RowData is null)
        {
            _state = null;
            _classId = null;
            _specId = null;
            _className = null;
            _specName = null;
            _moduleName = null;
            _unitInfo = new Dictionary<string, object?>();
            if (_enabled)
            {
                _currentStep = "等待游戏状态";
            }

            return;
        }

        _state = _stateBuilder.Build(scan.RowData, scan.BarData);
        _classId = _state.GetInt("职业");
        _specId = _state.GetInt("专精");
        (_className, _specName) = ClassNames.GetClassAndSpecName(_classId, _specId);
        _keymap.SelectForClass(_classId);

        if (!_enabled)
        {
            _unitInfo = new Dictionary<string, object?>();
            return;
        }

        if (!_state.GetBool("有效性"))
        {
            _currentStep = "等待游戏状态";
            _unitInfo = new Dictionary<string, object?>();
            return;
        }

        var decision = _logicRegistry.Run(_classId, _specId, _specName, _state);
        _currentStep = decision.Step;
        _unitInfo = decision.UnitInfo;
        _moduleName = decision.ModuleName;

        if (_options.Mode == SendMode.Click)
        {
            if (_clickPending && !string.IsNullOrWhiteSpace(decision.Hotkey))
            {
                _keySender.Send(decision.Hotkey);
            }

            _enabled = false;
            _clickPending = false;
            return;
        }

        if (!string.IsNullOrWhiteSpace(decision.Hotkey))
        {
            _keySender.Send(decision.Hotkey);
        }
    }

    private void PublishSnapshot()
    {
        SnapshotUpdated?.Invoke(new RenderSnapshot(
            _enabled,
            _className,
            _specName,
            _classId,
            _specId,
            _moduleName,
            _state,
            _currentStep,
            _unitInfo,
            _scanMs));
    }
}
