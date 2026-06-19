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
    private IReadOnlyList<RecognizedAuraInfo> _recognizedAuras = Array.Empty<RecognizedAuraInfo>();
    private bool _enabled;
    private bool _clickPending;

    public ShigureRuntime(string baseDirectory, AppOptions options, ModuleStore moduleStore)
    {
        _options = options;
        _config = ConfigService.LoadFromBaseDirectory(baseDirectory);
        _keymap = new KeymapService(baseDirectory, _config);
        _scanner = new PixelScanner(options.WindowTitle);
        _stateBuilder = new StateBuilder(_config);
        _keySender = new KeySender(options.WindowTitle);
        _logicRegistry = new LogicRegistry(_keymap, moduleStore, options.ModuleId);
    }

    public event Action<RenderSnapshot>? SnapshotUpdated;

    public AppOptions Options => _options;

    public void SetRecognizedAuras(IReadOnlyList<RecognizedAuraInfo> auras)
    {
        _recognizedAuras = auras.Count == 0
            ? Array.Empty<RecognizedAuraInfo>()
            : auras.ToArray();

        if (_state is not null)
        {
            ApplyRecognizedAuras(_state);
        }
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        _clickPending = false;
        _currentStep = enabled ? "手动开启" : "手动关闭";
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
        var scan = _scanner.ScanScreenData();

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
        ApplyRecognizedAuras(_state);
        _classId = _state.GetInt("职业");
        _specId = _state.GetInt("专精");
        (_className, _specName) = ClassNames.GetClassAndSpecName(_classId, _specId);
        _keymap.SelectForClass(_classId);

        if (!_state.GetBool("有效性"))
        {
            _moduleName = null;
            _currentStep = "等待游戏状态";
            _unitInfo = new Dictionary<string, object?>();
            return;
        }

        _moduleName = _logicRegistry.ResolveDynamicState(_classId, _specId, _state);

        if (!_enabled)
        {
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
            BuildDynamicValues(_state)));
    }

    private void ApplyRecognizedAuras(GameState state)
    {
        state.Values[RecognizedAuraFields.StateKey] = RecognizedAuraFields.BuildValueMap(_recognizedAuras);
    }

    private static IReadOnlyList<DynamicValueSnapshot> BuildDynamicValues(GameState? state)
    {
        if (state is null)
        {
            return [];
        }

        var values = new List<DynamicValueSnapshot>();
        if (state.Values.TryGetValue("$units", out var unitsObj)
            && unitsObj is IReadOnlyDictionary<string, string?> units)
        {
            foreach (var (name, slot) in units.OrderBy(kv => kv.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                values.Add(new DynamicValueSnapshot("单位", name, FormatUnitSlot(state, slot)));
            }
        }

        if (state.Values.TryGetValue("$unithealth", out var healthObj)
            && healthObj is IReadOnlyDictionary<string, object?> unitHealth)
        {
            foreach (var (name, value) in unitHealth.OrderBy(kv => kv.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                values.Add(new DynamicValueSnapshot("值名称", name, FormatSnapshotValue(value)));
            }
        }

        if (state.Values.TryGetValue("$counts", out var countsObj)
            && countsObj is IReadOnlyDictionary<string, int> counts)
        {
            foreach (var (name, value) in counts.OrderBy(kv => kv.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                values.Add(new DynamicValueSnapshot("数量", name, value.ToString()));
            }
        }

        if (state.Values.TryGetValue("$dynamicvalues", out var dynamicObj)
            && dynamicObj is IReadOnlyDictionary<string, object?> dynamicValues)
        {
            foreach (var (name, value) in dynamicValues.OrderBy(kv => kv.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                values.Add(new DynamicValueSnapshot("动态值", name, FormatSnapshotValue(value)));
            }
        }

        if (state.Values.TryGetValue(RecognizedAuraFields.StateKey, out var recognizedObj)
            && recognizedObj is IReadOnlyDictionary<string, object?> recognizedAuras)
        {
            foreach (var (name, value) in recognizedAuras.OrderBy(kv => kv.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                values.Add(new DynamicValueSnapshot("识别光环", name, FormatSnapshotValue(value)));
            }
        }

        return values;
    }

    private static string FormatUnitSlot(GameState state, string? slot)
    {
        if (string.IsNullOrWhiteSpace(slot))
        {
            return "-";
        }

        if (state.Group.TryGetValue(slot, out var member)
            && member.TryGetValue("生命值", out var health))
        {
            return $"{slot} (生命值 {FormatSnapshotValue(health)})";
        }

        return slot;
    }

    private static string FormatSnapshotValue(object? value)
    {
        return value switch
        {
            null => "-",
            bool b => b ? "是" : "否",
            _ => value.ToString() ?? "-"
        };
    }
}
