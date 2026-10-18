namespace Shigure;

// 只保存等待与单击批次状态；条件保持计时仍由 RuleMatchTracker 独立管理。
internal sealed class RuleHitDelayTracker(TimeProvider timeProvider)
{
    private readonly Dictionary<string, WaitState> _waits = new(StringComparer.Ordinal);
    private readonly HashSet<string> _singleRules = new(StringComparer.Ordinal);
    private bool _capturing;

    public bool IsSingleTrigger { get; private set; }
    public bool HasPending => IsSingleTrigger ? _singleRules.Count > 0 : _waits.Count > 0;
    public bool Contains(string key) => _waits.ContainsKey(key) || _singleRules.Contains(key);
    public bool CanEvaluate(string key) => !IsSingleTrigger || _capturing || _singleRules.Contains(key);

    public void BeginSingleTrigger()
    {
        Clear();
        IsSingleTrigger = true;
        _capturing = true;
    }

    public void EndEvaluation() => _capturing = false;

    public int WaitRemaining(string key, int? delayMs, long soundEventId)
    {
        if (IsSingleTrigger && _capturing) _singleRules.Add(key);
        if (delayMs is not > 0) return 0;

        var now = timeProvider.GetTimestamp();
        if (!_waits.TryGetValue(key, out var previous) || previous.SoundEventId != soundEventId)
        {
            previous = new WaitState(now, delayMs.Value, soundEventId);
            _waits[key] = previous;
        }
        var elapsedMs = timeProvider.GetElapsedTime(previous.StartedAt, now).TotalMilliseconds;
        return (int)Math.Ceiling(Math.Max(0, previous.DelayMs - elapsedMs));
    }

    public void DecisionSent(string key)
    {
        if (IsSingleTrigger)
        {
            Forget(key);
        }
        else if (_waits.TryGetValue(key, out var previous))
        {
            // 仅发送成功才重新计时；限频跳过或发送失败均保留已就绪状态。
            _waits[key] = previous with { StartedAt = timeProvider.GetTimestamp() };
        }
    }

    public void Forget(string key)
    {
        _waits.Remove(key);
        _singleRules.Remove(key);
    }

    public void Clear()
    {
        _waits.Clear();
        _singleRules.Clear();
        IsSingleTrigger = false;
        _capturing = false;
    }

    private readonly record struct WaitState(long StartedAt, int DelayMs, long SoundEventId);
}
