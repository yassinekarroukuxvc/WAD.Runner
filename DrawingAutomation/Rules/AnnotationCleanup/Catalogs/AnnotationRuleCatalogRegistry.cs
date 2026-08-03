using System;
using System.Collections.Generic;
using System.Linq;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using WAD.Runner.DrawingAutomation.Wedges;


namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;

public sealed class AnnotationRuleCatalogRegistry
{
    private readonly IReadOnlyDictionary<AnnotationCleanupProfile, IAnnotationRuleCatalog> _catalogs;

    public AnnotationRuleCatalogRegistry(IEnumerable<IAnnotationRuleCatalog> catalogs)
    {
        var list = (catalogs ?? throw new ArgumentNullException(nameof(catalogs)))
            .Where(x => x is not null)
            .ToList();

        Validate(list);
        _catalogs = list.ToDictionary(x => x.Profile);
    }

    public IReadOnlyList<AnnotationKeepRule> GetRules(AnnotationCleanupProfile profile)
    {
        if (!_catalogs.TryGetValue(profile, out var catalog))
            throw new InvalidOperationException($"No annotation rule catalog is registered for profile '{profile}'.");

        return catalog.Rules;
    }

    public static AnnotationRuleCatalogRegistry CreateDefault()
        => new(DrawingWedgeModuleRegistry.GetAnnotationCatalogs());

    private static void Validate(IReadOnlyCollection<IAnnotationRuleCatalog> catalogs)
    {
        if (catalogs.Count == 0)
            throw new InvalidOperationException("At least one annotation rule catalog must be registered.");

        var duplicateProfiles = catalogs
            .GroupBy(x => x.Profile)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.ToString())
            .ToList();

        if (duplicateProfiles.Count > 0)
        {
            throw new InvalidOperationException(
                "Duplicate annotation catalog profiles: " + string.Join(", ", duplicateProfiles));
        }

        foreach (var catalog in catalogs)
        {
            if (catalog.Rules is null)
                throw new InvalidOperationException($"Catalog '{catalog.Profile}' has a null rules collection.");

            foreach (var rule in catalog.Rules)
            {
                if (rule is null)
                    throw new InvalidOperationException($"Catalog '{catalog.Profile}' contains a null rule.");

                if (rule.Profile != catalog.Profile)
                {
                    throw new InvalidOperationException(
                        $"Rule '{rule.Id}' belongs to profile '{rule.Profile}' but is registered in '{catalog.Profile}'.");
                }

                if (string.IsNullOrWhiteSpace(rule.Id))
                    throw new InvalidOperationException($"Catalog '{catalog.Profile}' contains a rule with an empty ID.");

                if (rule.Name is null || string.IsNullOrWhiteSpace(rule.Name.Pattern))
                    throw new InvalidOperationException($"Rule '{rule.Id}' has no annotation name pattern.");

                if (rule.When is null)
                    throw new InvalidOperationException($"Rule '{rule.Id}' has no condition.");
            }

            var duplicateIds = catalog.Rules
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateIds.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Catalog '{catalog.Profile}' contains duplicate rule IDs: {string.Join(", ", duplicateIds)}");
            }
        }

        var globalDuplicateIds = catalogs
            .SelectMany(c => c.Rules.Select(r => new { c.Profile, Rule = r }))
            .GroupBy(x => x.Rule.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(x => x.Profile).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (globalDuplicateIds.Count > 0)
        {
            throw new InvalidOperationException(
                "Rule IDs must be globally unique across profiles. Duplicates: " +
                string.Join(", ", globalDuplicateIds));
        }
    }
}
