using System.Collections.Generic;
using System.Linq;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;

public sealed class CobLikeOverlayAnnotationRules : AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.CobLikeOverlay;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public CobLikeOverlayAnnotationRules()
    {
        // Legacy behavior treated non-customer COB-like drawings as production.
        // Keep Overlay isolated as its own profile so overlay-specific rules can be added later
        // without touching COB-like Production.
        Rules = new CobLikeProductionAnnotationRules()
            .Rules
            .Select(rule => CloneForProfile(rule, Profile, "COB-PROD", "COB-OVERLAY"))
            .ToList();
    }
}
