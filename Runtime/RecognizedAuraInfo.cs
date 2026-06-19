namespace Shigure;

public sealed record RecognizedAuraInfo(
    string Name,
    int Value,
    string Row,
    int Index,
    string Hash,
    int? HashDistance,
    double? TemplateScore);
