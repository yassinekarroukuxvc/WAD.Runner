using System;
using System.Collections.Generic;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Conditions;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;

public abstract class AnnotationRuleCatalogBase : IAnnotationRuleCatalog
{
    public abstract AnnotationCleanupProfile Profile { get; }
    public abstract IReadOnlyList<AnnotationKeepRule> Rules { get; }

    protected AnnotationKeepRule Keep(
        string id,
        AnnotationView view,
        string pattern,
        IAnnotationCondition when,
        string reason)
        => new()
        {
            Id = id,
            Profile = Profile,
            View = view,
            Name = new AnnotationNameTemplate(pattern),
            When = when,
            Reason = reason
        };

    protected AnnotationKeepRule KeepOptionalOverride(
        string id,
        AnnotationView view,
        Func<AnnotationCleanupContext, string?> overrideSelector,
        string fallbackPattern,
        IAnnotationCondition when,
        string reason)
        => new()
        {
            Id = id,
            Profile = Profile,
            View = view,
            Name = AnnotationNameTemplate.WithOptionalOverride(overrideSelector, fallbackPattern),
            When = when,
            Reason = reason
        };

    protected static AnnotationKeepRule CloneForProfile(
        AnnotationKeepRule source,
        AnnotationCleanupProfile profile,
        string oldIdToken,
        string newIdToken)
        => source with
        {
            Profile = profile,
            Id = source.Id.Replace(oldIdToken, newIdToken, StringComparison.OrdinalIgnoreCase)
        };
}
