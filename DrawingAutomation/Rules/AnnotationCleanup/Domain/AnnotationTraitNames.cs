namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

/// <summary>
/// Shared names for common annotation traits.
///
/// Wedge-specific resolvers may add additional trait names without
/// changing the shared annotation cleanup infrastructure.
/// </summary>
public static class AnnotationTraitNames
{
    public const string FootOption = "foot-option";
    public const string ShankType = "shank-type";
    public const string WedType = "wed-type";
    public const string FeedHoleType = "feed-hole-type";
}
