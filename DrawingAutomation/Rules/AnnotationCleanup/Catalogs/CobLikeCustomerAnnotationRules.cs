using System.Collections.Generic;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Conditions.AnnotationConditions;
using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain.AnnotationView;
using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain.FootOption;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;

public sealed class CobLikeCustomerAnnotationRules : AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.CobLikeCustomer;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public CobLikeCustomerAnnotationRules()
    {
        Rules = new List<AnnotationKeepRule>
        {
            // FRONT VIEW
            Keep("COB-CUST-FRONT-TL-MAIN", Front, "TL@{FrontSketch}", Always(), "Base total length in the active front shank sketch."),
            Keep("COB-CUST-FRONT-TL-AXIS", Front, "TL@part_axis", Always(), "Legacy TL dimension from part axis."),
            KeepOptionalOverride("COB-CUST-FRONT-K", Front, ctx => ctx.KAnnotationFullName, "K@Engraving", Always(), "Engraving location. Uses override when a specific full name is supplied."),
            Keep("COB-CUST-FRONT-VR", Front, "VR@ANNOT_LEFT_sketch", DimsPositive("VW", "VR"), "Keep front VR only when both VW and VR are positive."),

            // SIDE VIEW
            Keep("COB-CUST-SIDE-BA", Side, "BA@{FrontSketch}", Always(), "Base side BA dimension."),
            Keep("COB-CUST-SIDE-TL", Side, "TL@{FrontSketch}", Always(), "Base side TL dimension."),
            Keep("COB-CUST-SIDE-VBL", Side, "VBL@{FrontSketch}", DimPositive("VBL"), "Keep VBL only when VBL is positive."),

            // TOP VIEW
            Keep("COB-CUST-TOP-TD", Top, "TD@{TopSketch}", Always(), "Base top TD dimension."),
            Keep("COB-CUST-TOP-TDF", Top, "TDF@{TopSketch}", Always(), "Base top TDF dimension."),

            // DETAIL VIEW - customer excludes production-only GD, B, and GR_*.
            Keep("COB-CUST-DETAIL-W", Detail, "W@ANNOT_LEFT_sketch", Always(), "Base width annotation."),
            Keep("COB-CUST-DETAIL-ISA", Detail, "ISA@ANNOT_LEFT_sketch", Not(DimsPositive("VW", "VR")), "Keep ISA only when VW/VR is not active."),
            Keep("COB-CUST-DETAIL-VW", Detail, "VW@ANNOT_LEFT_sketch", DimsPositive("VW", "VR"), "Keep VW when both VW and VR are positive."),
            Keep("COB-CUST-DETAIL-VR", Detail, "VR@ANNOT_LEFT_sketch", DimsPositive("VW", "VR"), "Keep VR when both VW and VR are positive."),
            Keep("COB-CUST-DETAIL-VRA", Detail, "VRA@ANNOT_LEFT_sketch", DimsPositive("VW", "VR"), "Legacy rule: VRA follows VW/VR, not VRA positivity."),
            Keep("COB-CUST-DETAIL-W2", Detail, "W2@ANNOT_LEFT_sketch", DimPositive("W2"), "Keep W2 only when W2 is positive."),
            Keep("COB-CUST-DETAIL-GA", Detail, "GA@ANNOT_FOOT_OPTIONS_LEFT_sketch", DimPositive("GA"), "Keep GA only when GA is positive."),
            Keep("COB-CUST-DETAIL-CD", Detail, "CD@ANNOT_FOOT_OPTIONS_LEFT_sketch", DimPositive("CD"), "Keep CD only when CD is positive."),

            // SECTION VIEW - customer base dimensions
            Keep("COB-CUST-SECTION-T", Section, "T@{FrontSketch}", Always(), "Base customer section T dimension."),
            Keep("COB-CUST-SECTION-H", Section, "H@{FrontSketch}", Always(), "Base customer section H dimension."),
            Keep("COB-CUST-SECTION-HA", Section, "HA@{FrontSketch}", Always(), "Base customer section HA dimension."),
            Keep("COB-CUST-SECTION-FNA", Section, "FNA@{FrontSketch}", Always(), "Base customer section FNA dimension."),
            Keep("COB-CUST-SECTION-RA", Section, "RA@{FrontSketch}", Always(), "Base customer section RA dimension."),
            Keep("COB-CUST-SECTION-RA2", Section, "RA2@{FrontSketch}", DimPositive("RA2"), "Keep RA2 only when RA2 is positive."),

            // SECTION VIEW - FL foot annotations. Legacy behavior: these are controlled by F, not FL.
            Keep("COB-CUST-SECTION-FL-C", Section, "FL_C@{FrontSketch}", All(DimPositive("F"), FootIn(C, CC, C_WITH_CBR)), "Keep FL_C when F is positive and foot is C, CC, or C_WITH_CBR."),
            Keep("COB-CUST-SECTION-FL-G", Section, "FL_G@{FrontSketch}", All(DimPositive("F"), FootIs(G)), "Keep FL_G when F is positive and foot is G."),
            Keep("COB-CUST-SECTION-FL-VG", Section, "FL_VG@{FrontSketch}", All(DimPositive("F"), FootIs(VG)), "Keep FL_VG when F is positive and foot is VG."),

            // SECTION VIEW - customer keeps only G for CG/CC. CGR/CGD are production-only.
            Keep("COB-CUST-SECTION-G", Section, "G@{FrontSketch}", All(DimPositive("G"), FootIn(CG, CC)), "Customer: keep G for CG/CC when G is positive."),

            // SECTION VIEW - FR/BR suffix annotations. FR and BR are intentionally independent.
            Keep("COB-CUST-SECTION-FR-C", Section, "FR_C@{FrBrSketch}", All(DimPositive("FR"), FootIn(C, CG, CC, C_WITH_CBR)), "Keep FR_C when FR is positive and the foot uses C suffix."),
            Keep("COB-CUST-SECTION-BR-C", Section, "BR_C@{FrBrSketch}", All(DimPositive("BR"), FootIn(C, CG, CC, C_WITH_CBR)), "Keep BR_C when BR is positive and the foot uses C suffix."),
            Keep("COB-CUST-SECTION-FR-G", Section, "FR_G@{FrBrSketch}", All(DimPositive("FR"), FootIs(G)), "Keep FR_G when FR is positive and foot is G."),
            Keep("COB-CUST-SECTION-BR-G", Section, "BR_G@{FrBrSketch}", All(DimPositive("BR"), FootIs(G)), "Keep BR_G when BR is positive and foot is G."),
            Keep("COB-CUST-SECTION-FR-VG", Section, "FR_VG@{FrBrSketch}", All(DimPositive("FR"), FootIs(VG)), "Keep FR_VG when FR is positive and foot is VG."),
            Keep("COB-CUST-SECTION-BR-VG", Section, "BR_VG@{FrBrSketch}", All(DimPositive("BR"), FootIs(VG)), "Keep BR_VG when BR is positive and foot is VG.")
        };
    }
}
