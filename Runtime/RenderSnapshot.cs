namespace Shigure;

public sealed record RenderSnapshot(
    bool Enabled,
    bool BurstActive,
    int BurstRemainingSeconds,
    string? ClassName,
    string? SpecName,
    int? ClassId,
    int? SpecId,
    string? ModuleName,
    GameState? State,
    string CurrentStep,
    IReadOnlyDictionary<string, object?> UnitInfo,
    IReadOnlyList<DynamicValueSnapshot> DynamicValues,
    string? ScanFailureReason);

public sealed record DynamicValueSnapshot(string Kind, string Name, string Value);
