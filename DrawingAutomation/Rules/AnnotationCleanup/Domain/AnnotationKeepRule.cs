using System;
using System.Collections.Generic;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Conditions;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

public sealed record AnnotationKeepRule
{
    public required string Id { get; init; }
    public required AnnotationCleanupProfile Profile { get; init; }
    public required AnnotationView View { get; init; }
    public required AnnotationNameTemplate Name { get; init; }

    /// <summary>
    /// Additional accepted SolidWorks names for the same logical annotation.
    /// Aliases are useful when production/customer templates expose the same
    /// dimension under different model-linked or drawing-only names.
    /// </summary>
    public IReadOnlyList<AnnotationNameTemplate> Aliases { get; init; }
        = Array.Empty<AnnotationNameTemplate>();

    public required IAnnotationCondition When { get; init; }
    public string? Reason { get; init; }
}
