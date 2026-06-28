using System;
using System.Collections.Generic;
using System.Linq;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;

public sealed class AnnotationRuleCatalogRegistry
{
    private readonly IReadOnlyDictionary<AnnotationCleanupProfile, IAnnotationRuleCatalog> _catalogs;

    public AnnotationRuleCatalogRegistry(IEnumerable<IAnnotationRuleCatalog> catalogs)
    {
        _catalogs = (catalogs ?? throw new ArgumentNullException(nameof(catalogs)))
            .ToDictionary(x => x.Profile);
    }

    public IReadOnlyList<AnnotationKeepRule> GetRules(AnnotationCleanupProfile profile)
    {
        if (!_catalogs.TryGetValue(profile, out var catalog))
            throw new InvalidOperationException($"No annotation rule catalog is registered for profile '{profile}'.");

        return catalog.Rules;
    }

    public static AnnotationRuleCatalogRegistry CreateDefault()
        => new(new IAnnotationRuleCatalog[]
        {
            new CobLikeProductionAnnotationRules(),
            new CobLikeCustomerAnnotationRules(),
            new CobLikeOverlayAnnotationRules(),
            new PgbProductionAnnotationRules(),
            new PgbOverlayAnnotationRules(),

            new CkvdFgProductionAnnotationRules(),
            new CkvdFgCustomerAnnotationRules(),
            new CkvdFgOverlayAnnotationRules(),
            new CkvdPgbProductionAnnotationRules(),
            new CkvdPgbOverlayAnnotationRules(),

            new Osg7FgProductionAnnotationRules(),
            new Osg7FgCustomerAnnotationRules(),
            new Osg7FgOverlayAnnotationRules(),
            new Osg7PgbProductionAnnotationRules(),
            new Osg7PgbOverlayAnnotationRules()
        });
}
