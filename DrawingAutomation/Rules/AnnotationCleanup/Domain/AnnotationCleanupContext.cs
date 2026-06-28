namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

public sealed class AnnotationCleanupContext
{
    public required AnnotationCleanupProfile Profile { get; init; }
    public required ShankType Shank { get; init; }
    public required FootOption Foot { get; init; }
    public required AnnotationViewNameMap ViewNames { get; init; }
    public required SketchNameSet Sketches { get; init; }
    public required DimensionFacts Dimensions { get; init; }

    public string? KAnnotationFullName { get; init; }
    public string? ErdAnnotationFullName { get; init; }
}
