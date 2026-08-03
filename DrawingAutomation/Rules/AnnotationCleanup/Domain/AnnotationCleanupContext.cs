namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

public sealed class AnnotationCleanupContext
{
    public required AnnotationCleanupProfile Profile { get; init; }
    public required ShankType Shank { get; init; }
    public required FootOption Foot { get; init; }
    public required AnnotationViewNameMap ViewNames { get; init; }
    public required SketchNameSet Sketches { get; init; }
    public required DimensionFacts Dimensions { get; init; }

    /// <summary>
    /// Normalized value of the database/model property named Wed-Type.
    /// CKVD uses this to distinguish LW_STYLE_A_CKVD from
    /// LW_STYLE_B_CKVD without inferring the style from dimensions.
    /// </summary>
    public string WedTypeToken { get; init; } = string.Empty;

    public string? KAnnotationFullName { get; init; }
    public string? ErdAnnotationFullName { get; init; }
}
