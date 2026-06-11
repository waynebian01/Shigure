namespace Shigure;

public sealed record RenderSnapshot(
    bool Enabled,
    string? ClassName,
    string? SpecName,
    int? ClassId,
    int? SpecId,
    string? ModuleName,
    GameState? State,
    string CurrentStep,
    IReadOnlyDictionary<string, object?> UnitInfo,
    double ScanMs);
