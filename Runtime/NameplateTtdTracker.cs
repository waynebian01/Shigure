namespace Shigure;

/// <summary>
/// 为每个姓名板槽位维护最近 15 秒生命值样本，自适应窗口回归并平滑掉血速度。
/// 暂时无法估算时最多沿用 3 秒；姓名板消失或死亡立即清空。
/// 目标/焦点/首领 TTD 只转发映射槽位结果，不维护第二份历史。
/// </summary>
internal sealed class NameplateTtdTracker
{
    private const double HistorySeconds = 15;
    private const double MinSpanSeconds = 1;
    private const double EstimateRetentionSeconds = 3;
    private const double SmoothingSeconds = 2;
    private static readonly double[] WindowSeconds = [5, 10, HistorySeconds];

    private readonly Dictionary<int, SlotHistory> _history = new();

    public void Clear() => _history.Clear();

    public void Apply(GameState state, DateTimeOffset now)
    {
        var nameplates = EnsureMutableNameplates(state);
        for (var slot = 1; slot <= NameplateStateLayout.SlotCount; slot++)
        {
            var key = slot.ToString();
            if (!nameplates.TryGetValue(key, out var plate) || plate is not Dictionary<string, object?> values)
            {
                ClearSlot(slot);
                continue;
            }

            var present = values.TryGetValue("存在", out var presentObj) && presentObj is true;
            var health = values.TryGetValue("生命值", out var healthObj) && healthObj is int hp ? hp : 0;
            if (!present || health <= 0)
            {
                ClearSlot(slot);
                values.Remove("TTD");
                continue;
            }

            var ttd = UpdateSlot(slot, health, now);
            if (ttd is int seconds)
            {
                values["TTD"] = seconds;
            }
            else
            {
                values.Remove("TTD");
            }
        }

        ForwardUnitAliases(state, nameplates);
    }

    private void ForwardUnitAliases(
        GameState state,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> nameplates)
    {
        foreach (var alias in NameplateStateLayout.UnitTtdAliases)
        {
            state.Values.Remove(alias.TtdField);
            var mappedSlot = state.GetInt(alias.MappingField, 0);
            if (mappedSlot is < 1 or > NameplateStateLayout.SlotCount)
            {
                continue;
            }

            if (!nameplates.TryGetValue(mappedSlot.ToString(), out var plate)
                || !plate.TryGetValue("TTD", out var ttd)
                || ttd is not int seconds)
            {
                continue;
            }

            state.Values[alias.TtdField] = seconds;
        }
    }

    private int? UpdateSlot(int slot, int health, DateTimeOffset now)
    {
        if (!_history.TryGetValue(slot, out var history))
        {
            history = new SlotHistory();
            _history[slot] = history;
        }

        var samples = history.Samples;
        // 时钟回退或扫描间断超过历史窗口时，旧趋势已不可用。
        if (samples.Count > 0
            && (now < samples[^1].At || (now - samples[^1].At).TotalSeconds > HistorySeconds))
        {
            samples.Clear();
            history.LossPerSecond = null;
            history.LastEstimateAt = null;
        }
        samples.Add(new HpSample(now, health));
        var cutoff = now - TimeSpan.FromSeconds(HistorySeconds);
        samples.RemoveAll(sample => sample.At < cutoff);

        double? lossPerSecond = null;
        foreach (var windowSeconds in WindowSeconds)
        {
            var windowStart = now - TimeSpan.FromSeconds(windowSeconds);
            var startIndex = samples.FindIndex(sample => sample.At >= windowStart);
            lossPerSecond = TryComputeLossPerSecond(samples, startIndex);
            if (lossPerSecond.HasValue)
            {
                break;
            }
        }

        if (lossPerSecond is double currentLoss)
        {
            if (history.LossPerSecond is double previousLoss
                && history.LastEstimateAt is DateTimeOffset previousAt
                && (now - previousAt).TotalSeconds <= EstimateRetentionSeconds)
            {
                // 按实际采样间隔计算权重，使 2 秒时间常数不受扫描频率影响。
                var weight = 1 - Math.Exp(-(now - previousAt).TotalSeconds / SmoothingSeconds);
                history.LossPerSecond = previousLoss + weight * (currentLoss - previousLoss);
            }
            else
            {
                history.LossPerSecond = currentLoss;
            }
            history.LastEstimateAt = now;
        }
        else if (history.LastEstimateAt is not DateTimeOffset estimatedAt
            || (now - estimatedAt).TotalSeconds > EstimateRetentionSeconds)
        {
            // 保留期间不更新成功估算时间，避免旧值被无限续期。
            history.LossPerSecond = null;
            history.LastEstimateAt = null;
        }

        // 保留的是掉血速度；始终使用当前血量，不把旧 TTD 当作倒计时。
        return history.LossPerSecond is double loss
            ? (int)Math.Min(int.MaxValue, Math.Ceiling(health / loss))
            : null;
    }

    private void ClearSlot(int slot) => _history.Remove(slot);

    private static double? TryComputeLossPerSecond(IReadOnlyList<HpSample> samples, int startIndex)
    {
        if (startIndex < 0 || samples.Count - startIndex < 2)
        {
            return null;
        }

        var first = samples[startIndex];
        var last = samples[^1];
        var spanSeconds = (last.At - first.At).TotalSeconds;
        if (spanSeconds < MinSpanSeconds)
        {
            return null;
        }

        // 净掉血：窗口首尾生命值必须下降；回血会计入净趋势，持平或上升则不可用。
        if (last.Health >= first.Health)
        {
            return null;
        }

        double n = samples.Count - startIndex;
        double sumX = 0;
        double sumY = 0;
        double sumXy = 0;
        double sumXx = 0;
        var origin = first.At;
        for (var index = startIndex; index < samples.Count; index++)
        {
            var sample = samples[index];
            var x = (sample.At - origin).TotalSeconds;
            var y = sample.Health;
            sumX += x;
            sumY += y;
            sumXy += x * y;
            sumXx += x * x;
        }

        var denominator = n * sumXx - sumX * sumX;
        if (denominator <= 0)
        {
            return null;
        }

        var slope = (n * sumXy - sumX * sumY) / denominator;
        if (slope >= 0)
        {
            return null;
        }

        return double.IsFinite(slope) ? -slope : null;
    }

    private static Dictionary<string, IReadOnlyDictionary<string, object?>> EnsureMutableNameplates(GameState state)
    {
        if (state.Values.TryGetValue("nameplates", out var existing)
            && existing is Dictionary<string, IReadOnlyDictionary<string, object?>> current)
        {
            return current;
        }

        var created = new Dictionary<string, IReadOnlyDictionary<string, object?>>();
        state.Values["nameplates"] = created;
        return created;
    }

    private sealed class SlotHistory
    {
        public List<HpSample> Samples { get; } = [];
        public double? LossPerSecond { get; set; }
        public DateTimeOffset? LastEstimateAt { get; set; }
    }

    private readonly record struct HpSample(DateTimeOffset At, int Health);
}
