using System.Collections.Concurrent;

namespace Shigure;

public sealed class ShigureRuntime
{
    private const string BurstKeyName = "XBUTTON1";
    private const string BurstStateKey = "爆发开关";
    private static readonly TimeSpan BurstDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BurstDebounce = TimeSpan.FromMilliseconds(120);

    private readonly AppOptions _options;
    private readonly IRuntimeScreenScanner _scanner;
    private readonly IRuntimeStateBuilder _stateBuilder;
    private readonly IRuntimeKeyOutput _keySender;
    private readonly ITriggerKeyState _triggerKeyState;
    private readonly IRuntimeLogic _logic;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentQueue<RuntimeCommand> _pendingCommands = new();

    private GameState? _state;
    private string? _className;
    private string? _specName;
    private int? _classId;
    private int? _specId;
    private string? _moduleName;
    private string? _scanFailureReason;
    private string _currentStep = "等待启动";
    private IReadOnlyDictionary<string, object?> _unitInfo = new Dictionary<string, object?>();
    private bool _enabled;
    private bool _burstActive;
    private DateTimeOffset _burstUntil = DateTimeOffset.MinValue;
    private bool _clickPending;
    private readonly Dictionary<string, DateTimeOffset> _lastRuleSentAt = new(StringComparer.Ordinal);
    private DateTimeOffset _logicPausedUntil = DateTimeOffset.MinValue;

    internal ShigureRuntime(
        AppOptions options,
        IRuntimeScreenScanner scanner,
        IRuntimeStateBuilder stateBuilder,
        IRuntimeKeyOutput keySender,
        ITriggerKeyState triggerKeyState,
        IRuntimeLogic logic,
        TimeProvider timeProvider)
    {
        _options = options;
        _scanner = scanner;
        _stateBuilder = stateBuilder;
        _keySender = keySender;
        _triggerKeyState = triggerKeyState;
        _logic = logic;
        _timeProvider = timeProvider;
    }

    public event Action<RenderSnapshot>? SnapshotUpdated;

    public AppOptions Options => _options;

    public void SetEnabled(bool enabled)
    {
        _pendingCommands.Enqueue(RuntimeCommand.SetEnabled(enabled));
    }

    public void ToggleEnabled()
    {
        _pendingCommands.Enqueue(RuntimeCommand.ToggleEnabled());
    }

    public void ActivateBurst()
    {
        _pendingCommands.Enqueue(RuntimeCommand.ActivateBurst());
    }

    public void ToggleBurst()
    {
        _pendingCommands.Enqueue(RuntimeCommand.ToggleBurst());
    }

    private void ApplyEnabled(bool enabled)
    {
        _enabled = enabled;
        _clickPending = false;
        if (!enabled)
        {
            _lastRuleSentAt.Clear();
            _logicPausedUntil = DateTimeOffset.MinValue;
        }

        _currentStep = enabled ? "手动开启" : "手动关闭";
        PublishSnapshot();
    }

    private void ApplyBurst(bool active)
    {
        var now = _timeProvider.GetUtcNow();
        if (active)
        {
            StartBurst(now);
        }
        else
        {
            ClearBurst("爆发关闭");
        }

        if (_state is not null)
        {
            WriteBurstState(_state);
        }

        PublishSnapshot();
    }

    private void ClearBurst(string step)
    {
        if (!_burstActive && _burstUntil == DateTimeOffset.MinValue)
        {
            return;
        }

        _burstActive = false;
        _burstUntil = DateTimeOffset.MinValue;
        _currentStep = step;
    }

    private void StartBurst(DateTimeOffset now)
    {
        _burstActive = true;
        _burstUntil = _options.BurstAutoOff
            ? now.Add(BurstDuration)
            : DateTimeOffset.MaxValue;
        _currentStep = "爆发开启";
    }

    private void WriteBurstState(GameState state)
    {
        state.Values[BurstStateKey] = _burstActive ? 1 : 0;
    }

    private int GetBurstRemainingSeconds(DateTimeOffset now)
    {
        if (!_burstActive || !_options.BurstAutoOff)
        {
            return 0;
        }

        var remaining = _burstUntil - now;
        if (remaining <= TimeSpan.Zero)
        {
            return 0;
        }

        return (int)Math.Ceiling(remaining.TotalSeconds);
    }

    private void DrainPendingCommands()
    {
        while (_pendingCommands.TryDequeue(out var command))
        {
            switch (command.Kind)
            {
                case RuntimeCommandKind.SetEnabled:
                    ApplyEnabled(command.Enabled);
                    break;
                case RuntimeCommandKind.ToggleEnabled:
                    ApplyEnabled(!_enabled);
                    break;
                case RuntimeCommandKind.ActivateBurst:
                    ApplyBurst(true);
                    break;
                case RuntimeCommandKind.ToggleBurst:
                    ApplyBurst(!_burstActive);
                    break;
            }
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var toggleVk = _triggerKeyState.ResolveVirtualKey(_options.ToggleKey);
        if (toggleVk is null)
        {
            _currentStep = $"无法识别触发键: {_options.ToggleKey}";
            PublishSnapshot();
            return;
        }

        var burstVk = _triggerKeyState.ResolveVirtualKey(BurstKeyName);
        var burstSharesToggleKey = burstVk is not null && burstVk.Value == toggleVk.Value;

        var previousPressed = false;
        var previousBurstPressed = false;
        var lastLogicAt = DateTimeOffset.MinValue;
        var lastRenderAt = DateTimeOffset.MinValue;
        var lastToggleAt = DateTimeOffset.MinValue;
        var lastBurstAt = DateTimeOffset.MinValue;
        _currentStep = "已启动";
        PublishSnapshot();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                DrainPendingCommands();
                var now = _timeProvider.GetUtcNow();
                var pressed = _triggerKeyState.IsPressed(toggleVk.Value);
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
                        _lastRuleSentAt.Clear();
                        _logicPausedUntil = DateTimeOffset.MinValue;
                        _currentStep = "按住结束";
                    }
                }

                previousPressed = pressed;

                if (!burstSharesToggleKey && burstVk is not null)
                {
                    var burstPressed = _triggerKeyState.IsPressed(burstVk.Value);
                    var burstRising = burstPressed
                        && !previousBurstPressed
                        && now - lastBurstAt >= BurstDebounce;
                    if (burstRising)
                    {
                        lastBurstAt = now;
                        ApplyBurst(!_burstActive);
                    }

                    previousBurstPressed = burstPressed;
                }

                if (_options.BurstAutoOff && _burstActive && now >= _burstUntil)
                {
                    ClearBurst("爆发超时关闭");
                    PublishSnapshot();
                }

                if (now - lastLogicAt >= _options.LogicInterval)
                {
                    lastLogicAt = now;
                    if (now >= _logicPausedUntil)
                    {
                        TickLogic();
                    }
                }

                if (now - lastRenderAt >= _options.RenderInterval)
                {
                    lastRenderAt = now;
                    PublishSnapshot();
                }

                await Task.Delay(TimeSpan.FromMilliseconds(25), _timeProvider, cancellationToken);
            }
        }
        finally
        {
            _enabled = false;
            _burstActive = false;
            _burstUntil = DateTimeOffset.MinValue;
            _clickPending = false;
            _logicPausedUntil = DateTimeOffset.MinValue;
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
                if (!_enabled)
                {
                    _lastRuleSentAt.Clear();
                    _logicPausedUntil = DateTimeOffset.MinValue;
                }

                _currentStep = _enabled ? "逻辑开启" : "逻辑关闭";
                break;
        }
    }

    private void TickLogic()
    {
        var scan = _scanner.ScanScreenData();
        _scanFailureReason = scan.FailureReason;

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

        _state = _stateBuilder.Build(scan.RowData, scan.BarData, scan.HealAbsorbData);
        WriteBurstState(_state);
        _classId = _state.GetInt("职业");
        _specId = _state.GetInt("专精");
        (_className, _specName) = ClassNames.GetClassAndSpecName(_classId, _specId);
        if (!_state.GetBool("有效性"))
        {
            _moduleName = null;
            _currentStep = "等待游戏状态";
            _unitInfo = new Dictionary<string, object?>();
            return;
        }

        var evaluation = _logic.Evaluate(_classId, _specId, _specName, _state, _enabled);
        _moduleName = evaluation.ModuleName;

        if (!_enabled)
        {
            _unitInfo = new Dictionary<string, object?>();
            return;
        }

        var decision = evaluation.Decision;
        if (decision is null)
        {
            _currentStep = "逻辑未返回决策";
            _unitInfo = new Dictionary<string, object?>();
            return;
        }

        _currentStep = decision.Step;
        _unitInfo = decision.UnitInfo;
        _moduleName = decision.ModuleName;

        if (_options.Mode == SendMode.Click)
        {
            if (_clickPending
                && !string.IsNullOrWhiteSpace(decision.Hotkey))
            {
                var sendAttemptAt = _timeProvider.GetUtcNow();
                if (CanSend(decision, sendAttemptAt))
                {
                    SendAndPauseLogic(decision, scan.TargetWindowHandle);
                }
            }

            _enabled = false;
            _clickPending = false;
            return;
        }

        if (!string.IsNullOrWhiteSpace(decision.Hotkey))
        {
            var sendAttemptAt = _timeProvider.GetUtcNow();
            if (CanSend(decision, sendAttemptAt))
            {
                SendAndPauseLogic(decision, scan.TargetWindowHandle);
            }
        }
    }

    private void SendAndPauseLogic(LogicDecision decision, nint targetWindowHandle)
    {
        var sendResult = _keySender.Send(decision.Hotkey!, targetWindowHandle);
        if (!sendResult.Succeeded)
        {
            var info = _unitInfo.ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.Ordinal);
            info["发送失败"] = sendResult.FailureReason ?? "未知原因";
            _unitInfo = info;
            _currentStep = $"{decision.Step}（按键发送失败）";
            return;
        }

        var sentAt = _timeProvider.GetUtcNow();
        RecordSent(decision, sentAt);
        if (decision.LogicDelayMs > 0)
        {
            _logicPausedUntil = sentAt.AddMilliseconds(decision.LogicDelayMs);
        }
    }

    private bool CanSend(LogicDecision decision, DateTimeOffset now)
    {
        if (decision.DelayMs <= 0)
        {
            return true;
        }

        var key = string.IsNullOrWhiteSpace(decision.RateLimitKey)
            ? decision.Hotkey ?? string.Empty
            : decision.RateLimitKey;
        if (_lastRuleSentAt.TryGetValue(key, out var lastSentAt)
            && now - lastSentAt < TimeSpan.FromMilliseconds(decision.DelayMs))
        {
            return false;
        }

        return true;
    }

    private void RecordSent(LogicDecision decision, DateTimeOffset now)
    {
        if (decision.DelayMs <= 0)
        {
            return;
        }

        var key = string.IsNullOrWhiteSpace(decision.RateLimitKey)
            ? decision.Hotkey ?? string.Empty
            : decision.RateLimitKey;
        _lastRuleSentAt[key] = now;
    }

    private void PublishSnapshot()
    {
        var now = _timeProvider.GetUtcNow();
        SnapshotUpdated?.Invoke(new RenderSnapshot(
            _enabled,
            _burstActive,
            GetBurstRemainingSeconds(now),
            _className,
            _specName,
            _classId,
            _specId,
            _moduleName,
            _state,
            _currentStep,
            _unitInfo,
            BuildDynamicValues(_state),
            _scanFailureReason));
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

    private enum RuntimeCommandKind
    {
        SetEnabled,
        ToggleEnabled,
        ActivateBurst,
        ToggleBurst
    }

    private readonly record struct RuntimeCommand(RuntimeCommandKind Kind, bool Enabled)
    {
        public static RuntimeCommand SetEnabled(bool enabled)
            => new(RuntimeCommandKind.SetEnabled, enabled);

        public static RuntimeCommand ToggleEnabled()
            => new(RuntimeCommandKind.ToggleEnabled, false);

        public static RuntimeCommand ActivateBurst()
            => new(RuntimeCommandKind.ActivateBurst, true);

        public static RuntimeCommand ToggleBurst()
            => new(RuntimeCommandKind.ToggleBurst, false);
    }
}
