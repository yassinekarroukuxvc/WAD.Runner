namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

/// <summary>
/// Wedge-owned annotation semantics resolved before the generic cleanup
/// context is created.
/// </summary>
public sealed class AnnotationWedgeContext
{
    public static AnnotationWedgeContext Empty { get; } = new()
    {
        Traits = AnnotationTraitSet.Empty,
        Sketches = SketchNameSet.Empty
    };

    public required AnnotationTraitSet Traits { get; init; }
    public required SketchNameSet Sketches { get; init; }
}
