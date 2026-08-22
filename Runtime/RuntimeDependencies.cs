namespace Shigure;

public sealed record LogicDecision(
    string? Hotkey,
    string Step,
    IReadOnlyDictionary<string, object?> UnitInfo,
    string? ModuleName = null,
    int DelayMs = 0,
    string? RateLimitKey = null,
    int LogicDelayMs = 0);

internal interface IRuntimeScreenScanner
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
    LogicEvaluation Evaluate(
        int? classId,
        int? specId,
        string? specName,
        GameState state,
        bool runLogic);
}

public sealed record LogicEvaluation(string? ModuleName, LogicDecision? Decision);

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
