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

    public IReadOnlyCollection<ExpectedAnnotation> BuildKeepSet(AnnotationCleanupContext ctx)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));

        return _catalogs
            .GetRules(ctx.Profile)
            .Where(rule => rule.When.IsMatch(ctx))
            .Select(rule => new ExpectedAnnotation(
                rule.View,
                rule.Name.Resolve(ctx),
                rule.Id,
                rule.Reason))
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

    private static string Key(AnnotationView view, string fullName)
    {
        return $"{view}||{fullName.Trim().ToUpperInvariant()}";
    }
}
