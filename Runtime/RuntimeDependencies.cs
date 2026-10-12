namespace Shigure;

public sealed record LogicDecision(
    string? Hotkey,
    string Step,
    IReadOnlyDictionary<string, object?> UnitInfo,
    string? ModuleName = null,
    int DelayMs = 0,
    string? RateLimitKey = null,
    int LogicDelayMs = 0,
    bool SendOnce = false,
    long SoundEventId = 0);

internal interface IRuntimeScreenScanner : IDisposable
{
    ScreenScanResult ScanScreenData();
}

internal interface IRuntimeStateBuilder
{
    GameState Build(
        IReadOnlyDictionary<int, int> rowData,
        IReadOnlyDictionary<int, int> barData,
        IReadOnlyDictionary<int, int>? healAbsorbData = null);
}

internal interface IRuntimeLogic
{
    void DecisionSent(LogicDecision decision) { }
    void ResetTriggers() { }
    LogicEvaluation Evaluate(
        int? classId,
        int? specId,
        string? specName,
        GameState state,
        bool runLogic);
}

internal interface IRuntimeSoundInput : IDisposable
{
    void Configure(IReadOnlyList<string> names, bool enabled);
    SoundRecognitionEvent? ReadEvent();
    string? FailureReason { get; }
}

internal sealed record SoundRecognitionEvent(string Name, long Id, DateTimeOffset DetectedAt);

internal interface IRuntimeVolumeInput
{
    VolumePeakSample ReadPeak(nint expectedWindow);
}

internal readonly record struct VolumePeakSample(double? PeakPercent, string? FailureReason = null);

public sealed record LogicEvaluation(string? ModuleName, IReadOnlyList<LogicDecision> Decisions)
{
    public LogicDecision? Decision => Decisions.Count == 0 ? null : Decisions[^1];
}

internal interface IRuntimeKeyOutput
{
    KeySendResult Send(string hotkey, nint expectedWindow);
}

public readonly record struct KeySendResult(bool Succeeded, string? FailureReason)
{
    public static KeySendResult Success { get; } = new(true, null);

    public static KeySendResult Failure(string reason) => new(false, reason);
}

internal interface ITriggerKeyState
{
    int? ResolveVirtualKey(string keyName);

    TriggerKeySample Read(int virtualKey);
}

internal readonly record struct TriggerKeySample(bool IsDown, bool WasPressed);
