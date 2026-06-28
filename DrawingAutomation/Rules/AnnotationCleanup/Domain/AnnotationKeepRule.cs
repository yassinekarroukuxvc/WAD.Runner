using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Conditions;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

public sealed record AnnotationKeepRule
{
    public required string Id { get; init; }
    public required AnnotationCleanupProfile Profile { get; init; }
    public required AnnotationView View { get; init; }
    public required AnnotationNameTemplate Name { get; init; }
    public required IAnnotationCondition When { get; init; }
    public string? Reason { get; init; }
}
