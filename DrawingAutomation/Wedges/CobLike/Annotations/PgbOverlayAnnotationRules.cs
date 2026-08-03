using System.Collections.Generic;
using System.Linq;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

namespace WAD.Runner.DrawingAutomation.Wedges.CobLike.Annotations;

public sealed class PgbOverlayAnnotationRules : AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.PgbOverlay;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public PgbOverlayAnnotationRules()
    {
        // Current PGB overlay cleanup did not have a separate rule set in the legacy shared file.
        // This profile intentionally starts from PGB production behavior and is separated for future overlay changes.
        Rules = new PgbProductionAnnotationRules()
            .Rules
            .Select(rule => CloneForProfile(rule, Profile, "PGB-PROD", "PGB-OVERLAY"))
            .ToList();
    }
}
