using System;
using System.Collections.Generic;
using System.Linq;
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
        => KeepWithAliases(id, view, pattern, Array.Empty<string>(), when, reason);

    protected AnnotationKeepRule KeepWithAliases(
        string id,
        AnnotationView view,
        string primaryPattern,
        IEnumerable<string> aliases,
        IAnnotationCondition when,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(primaryPattern))
            throw new ArgumentException("A primary annotation pattern is required.", nameof(primaryPattern));

        var aliasTemplates = (aliases ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => new AnnotationNameTemplate(x.Trim()))
            .ToList()
            .AsReadOnly();

        return new AnnotationKeepRule
        {
            Id = id,
            Profile = Profile,
            View = view,
            Name = new AnnotationNameTemplate(primaryPattern),
            Aliases = aliasTemplates,
            When = when,
            Reason = reason
        };
    }

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
