using System;
using System.Collections.Generic;
using System.Linq;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Engine;

public sealed class AnnotationCleanupPlanner
{
    private readonly AnnotationRuleCatalogRegistry _catalogs;

    public AnnotationCleanupPlanner(AnnotationRuleCatalogRegistry catalogs)
    {
        _catalogs = catalogs ?? throw new ArgumentNullException(nameof(catalogs));
    }

    public bool HasConfiguredRules(AnnotationCleanupProfile profile)
        => _catalogs.GetRules(profile).Count > 0;

    public IReadOnlyCollection<ExpectedAnnotation> BuildKeepSet(AnnotationCleanupContext ctx)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));

        return _catalogs
            .GetRules(ctx.Profile)
            .Where(rule => rule.When.IsMatch(ctx))
            .SelectMany(rule => ResolveAcceptedNames(rule, ctx))
            .Where(x => !string.IsNullOrWhiteSpace(x.FullName))
            .GroupBy(x => Key(x.View, x.FullName), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> BuildExpectedFullNamesByView(
        AnnotationCleanupContext ctx)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));

        var dict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var ann in BuildKeepSet(ctx))
        {
            var viewName = ctx.ViewNames.Resolve(ann.View);

            if (!dict.TryGetValue(viewName, out var list))
            {
                list = new List<string>();
                dict[viewName] = list;
            }

            list.Add(ann.FullName);
        }

        return dict.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyCollection<string>)kv.Value
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<ExpectedAnnotation> ResolveAcceptedNames(
        AnnotationKeepRule rule,
        AnnotationCleanupContext ctx)
    {
        yield return new ExpectedAnnotation(
            rule.View,
            rule.Name.Resolve(ctx),
            rule.Id,
            rule.Reason);

        foreach (var alias in rule.Aliases ?? Array.Empty<AnnotationNameTemplate>())
        {
            yield return new ExpectedAnnotation(
                rule.View,
                alias.Resolve(ctx),
                rule.Id,
                rule.Reason);
        }
    }

    private static string Key(AnnotationView view, string fullName)
        => $"{view}||{AnnotationNameIdentity.Normalize(fullName)}";
}
