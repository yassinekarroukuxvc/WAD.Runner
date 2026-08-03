using System.Collections.Generic;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Conditions.AnnotationConditions;
using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain.AnnotationView;

namespace WAD.Runner.DrawingAutomation.Wedges.CobLike.Annotations;

public sealed class PgbProductionAnnotationRules : AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.PgbProduction;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public PgbProductionAnnotationRules()
    {
        Rules = new List<AnnotationKeepRule>
        {
            // FRONT VIEW
            Keep("PGB-PROD-FRONT-TL-MAIN", Front, "TL@{FrontSketch}", Always(), "PGB base total length in active front shank sketch."),
            Keep("PGB-PROD-FRONT-TL-AXIS", Front, "TL@part_axis", Always(), "Legacy TL dimension from part axis."),
            KeepOptionalOverride("PGB-PROD-FRONT-K", Front, ctx => ctx.KAnnotationFullName, "K@Engraving", Always(), "Engraving location. Uses override when a specific full name is supplied."),

            // SIDE VIEW - PGB does not keep VBL in the current legacy logic.
            Keep("PGB-PROD-SIDE-BA", Side, "BA@{FrontSketch}", Always(), "PGB side BA dimension."),
            Keep("PGB-PROD-SIDE-TL", Side, "TL@{FrontSketch}", Always(), "PGB side TL dimension."),

            // TOP VIEW
            Keep("PGB-PROD-TOP-TD", Top, "TD@{TopSketch}", Always(), "PGB top TD dimension."),
            Keep("PGB-PROD-TOP-TDF", Top, "TDF@{TopSketch}", Always(), "PGB top TDF dimension."),

            // DETAIL VIEW - PGB uses the customer-like detail base in the legacy code.
            Keep("PGB-PROD-DETAIL-W", Detail, "W@ANNOT_LEFT_sketch", Always(), "PGB base width annotation."),
            Keep("PGB-PROD-DETAIL-ISA", Detail, "ISA@ANNOT_LEFT_sketch", Not(DimsPositive("VW", "VR")), "Keep ISA only when VW/VR is not active."),
            Keep("PGB-PROD-DETAIL-VW", Detail, "VW@ANNOT_LEFT_sketch", DimsPositive("VW", "VR"), "Keep VW when both VW and VR are positive."),
            Keep("PGB-PROD-DETAIL-VR", Detail, "VR@ANNOT_LEFT_sketch", DimsPositive("VW", "VR"), "Keep VR when both VW and VR are positive."),
            Keep("PGB-PROD-DETAIL-VRA", Detail, "VRA@ANNOT_LEFT_sketch", DimsPositive("VW", "VR"), "Legacy rule: VRA follows VW/VR, not VRA positivity."),
            Keep("PGB-PROD-DETAIL-W2", Detail, "W2@ANNOT_LEFT_sketch", DimPositive("W2"), "Keep W2 only when W2 is positive."),
            Keep("PGB-PROD-DETAIL-GA", Detail, "GA@ANNOT_FOOT_OPTIONS_LEFT_sketch", DimPositive("GA"), "Keep GA only when GA is positive."),
            Keep("PGB-PROD-DETAIL-CD", Detail, "CD@ANNOT_FOOT_OPTIONS_LEFT_sketch", DimPositive("CD"), "Keep CD only when CD is positive."),

            // SECTION VIEW
            Keep("PGB-PROD-SECTION-T", Section, "T@{FrontSketch}", Always(), "PGB section T dimension."),
            Keep("PGB-PROD-SECTION-FD", Section, "FD@{FrontSketch}", Always(), "PGB section FD dimension."),
            Keep("PGB-PROD-SECTION-RA", Section, "RA@{FrontSketch}", Always(), "PGB section RA dimension.")
        };
    }
}
