namespace Shigure;

internal sealed class RuleMatchTracker(TimeProvider timeProvider)
{
    private readonly Dictionary<string, MatchState> _matches = new(StringComparer.Ordinal);
    public string? LockedRuleKey { get; private set; }

    public (bool Matched, long SoundEventId) Apply(string key, int? delayMs, bool matched, long soundEventId)
    {
        if (delayMs is not > 0)
        {
            Forget(key);
            return (matched, soundEventId);
        }
        if (matched)
        {
            _matches[key] = new MatchState(null, soundEventId);
            return (true, soundEventId);
        }
        if (!_matches.TryGetValue(key, out var previous)) return (false, 0);

        // 使用单调时钟；从第一次不命中的采样开始计时，恢复命中即取消倒计时。
        var now = timeProvider.GetTimestamp();
        var missedAt = previous.MissedAt ?? now;
        if (timeProvider.GetElapsedTime(missedAt, now) >= TimeSpan.FromMilliseconds(delayMs.Value))
        {
            Forget(key);
            return (false, 0);
        }
        _matches[key] = previous with { MissedAt = missedAt };
        // 保留音效事件编号，避免保持命中时把同一次声音重新发送。
        return (true, previous.SoundEventId);
    }

    public void Lock(string key)
    {
        if (_matches.ContainsKey(key)) LockedRuleKey = key;
    }

    public void Forget(string key)
    {
        _matches.Remove(key);
        if (LockedRuleKey == key) LockedRuleKey = null;
    }

    public void Clear()
    {
        _matches.Clear();
        LockedRuleKey = null;
    }

    private readonly record struct MatchState(long? MissedAt, long SoundEventId);
}
