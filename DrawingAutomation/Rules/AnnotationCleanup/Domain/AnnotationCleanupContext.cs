namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

public sealed class AnnotationCleanupContext
{
    public required AnnotationCleanupProfile Profile { get; init; }
    public required AnnotationTraitSet Traits { get; init; }
    public required AnnotationViewNameMap ViewNames { get; init; }
    public required SketchNameSet Sketches { get; init; }
    public required DimensionFacts Dimensions { get; init; }

    public string ShankToken =>
        Traits.Get(AnnotationTraitNames.ShankType);

    public string FootToken =>
        Traits.Get(AnnotationTraitNames.FootOption);

    public string WedTypeToken =>
        Traits.Get(AnnotationTraitNames.WedType);

    public string FeedHoleToken =>
        Traits.Get(AnnotationTraitNames.FeedHoleType);

    public string? KAnnotationFullName { get; init; }
    public string? ErdAnnotationFullName { get; init; }
}
