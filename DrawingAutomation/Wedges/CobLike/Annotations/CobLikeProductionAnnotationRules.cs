using System.Collections.Generic;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Conditions.AnnotationConditions;
using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain.AnnotationView;
using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain.FootOption;

namespace WAD.Runner.DrawingAutomation.Wedges.CobLike.Annotations;

public sealed class CobLikeProductionAnnotationRules : AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.CobLikeProduction;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public CobLikeProductionAnnotationRules()
    {
        Rules = new List<AnnotationKeepRule>
        {
            // FRONT VIEW
            Keep("COB-PROD-FRONT-TL-MAIN", Front, "TL@{FrontSketch}", Always(), "Base total length in the active front shank sketch."),
            Keep("COB-PROD-FRONT-TL-AXIS", Front, "TL@part_axis", Always(), "Legacy TL dimension from part axis."),
            KeepOptionalOverride("COB-PROD-FRONT-K", Front, ctx => ctx.KAnnotationFullName, "K@Engraving", Always(), "Engraving location. Uses override when a specific full name is supplied."),
            Keep("COB-PROD-FRONT-VR", Front, "VR@ANNOT_LEFT_sketch", DimsPositive("VW", "VR"), "Keep front VR only when both VW and VR are positive."),

            // SIDE VIEW
            Keep("COB-PROD-SIDE-BA", Side, "BA@{FrontSketch}", Always(), "Base side BA dimension."),
            Keep("COB-PROD-SIDE-TL", Side, "TL@{FrontSketch}", Always(), "Base side TL dimension."),
            Keep("COB-PROD-SIDE-VBL", Side, "VBL@{FrontSketch}", DimPositive("VBL"), "Keep VBL only when VBL is positive."),

            // TOP VIEW
            Keep("COB-PROD-TOP-TD", Top, "TD@{TopSketch}", Always(), "Base top TD dimension."),
            Keep("COB-PROD-TOP-TDF", Top, "TDF@{TopSketch}", Always(), "Base top TDF dimension."),

            // DETAIL VIEW
            Keep("COB-PROD-DETAIL-W", Detail, "W@ANNOT_LEFT_sketch", Always(), "Base width annotation."),
            Keep("COB-PROD-DETAIL-ISA", Detail, "ISA@ANNOT_LEFT_sketch", Not(DimsPositive("VW", "VR")), "Keep ISA only when VW/VR is not active."),
            Keep("COB-PROD-DETAIL-VW", Detail, "VW@ANNOT_LEFT_sketch", DimsPositive("VW", "VR"), "Keep VW when both VW and VR are positive."),
            Keep("COB-PROD-DETAIL-VR", Detail, "VR@ANNOT_LEFT_sketch", DimsPositive("VW", "VR"), "Keep VR when both VW and VR are positive."),
            Keep("COB-PROD-DETAIL-VRA", Detail, "VRA@ANNOT_LEFT_sketch", DimsPositive("VW", "VR"), "Legacy rule: VRA follows VW/VR, not VRA positivity."),
            Keep("COB-PROD-DETAIL-W2", Detail, "W2@ANNOT_LEFT_sketch", DimPositive("W2"), "Keep W2 only when W2 is positive."),
            Keep("COB-PROD-DETAIL-GA", Detail, "GA@ANNOT_FOOT_OPTIONS_LEFT_sketch", DimPositive("GA"), "Keep GA only when GA is positive."),
            Keep("COB-PROD-DETAIL-CL", Detail, "CL@ANNOT_FOOT_OPTIONS_LEFT_sketch", DimPositive("CL"), "Keep CD only when CD is positive."),
            Keep("COB-PROD-DETAIL-CD", Detail, "CD@ANNOT_FOOT_OPTIONS_LEFT_sketch", DimPositive("CD"), "Keep CD only when CD is positive."),
            Keep("COB-PROD-DETAIL-GD", Detail, "GD@ANNOT_FOOT_OPTIONS_LEFT_sketch", DimPositive("GD"), "Production only: keep GD when GD is positive."),
            Keep("COB-PROD-DETAIL-B", Detail, "B@ANNOT_FOOT_OPTIONS_LEFT_sketch", DimPositive("B"), "Production only: keep B when B is positive."),
            Keep("COB-PROD-DETAIL-GR-G", Detail, "GR_G@ANNOT_FOOT_OPTIONS_LEFT_sketch", All(DimPositive("GR"), FootIs(G)), "Production only: keep GR_G when GR is positive and foot is G."),
            Keep("COB-PROD-DETAIL-GR-VG", Detail, "GR_VG@ANNOT_FOOT_OPTIONS_LEFT_sketch", All(DimPositive("GR"), FootIs(VG)), "Production only: keep GR_VG when GR is positive and foot is VG."),

            // SECTION VIEW - base production dimensions
            Keep("COB-PROD-SECTION-T", Section, "T@{FrontSketch}", Always(), "Base section T dimension."),
            Keep("COB-PROD-SECTION-H", Section, "H@{FrontSketch}", Always(), "Base section H dimension."),
            Keep("COB-PROD-SECTION-HA", Section, "HA@{FrontSketch}", Always(), "Base section HA dimension."),
            Keep("COB-PROD-SECTION-FNA", Section, "FNA@{FrontSketch}", Always(), "Base section FNA dimension."),
            Keep("COB-PROD-SECTION-RA", Section, "RA@{FrontSketch}", Always(), "Base section RA dimension."),
            Keep("COB-PROD-SECTION-RA2", Section, "RA2@{FrontSketch}", DimPositive("RA2"), "Keep RA2 only when RA2 is positive."),
            Keep("COB-PROD-SECTION-FD", Section, "FD@{FrontSketch}", Always(), "Production only: keep FD."),
            Keep("COB-PROD-SECTION-ERL", Section, "ERL@{FrontSketch}", Always(), "Production only: keep ERL."),
            Keep("COB-PROD-SECTION-CA", Section, "CA@{FrontSketch}", Always(), "Production only: keep CA."),
            Keep("COB-PROD-SECTION-BA", Section, "BA@{FrontSketch}", Always(), "Production only: keep BA."),
            KeepOptionalOverride("COB-PROD-SECTION-ERD", Section, ctx => ctx.ErdAnnotationFullName, "ERD@{FrontSketch}", DimPositive("ERD"), "Production only: keep ERD when ERD is positive; use full-name override when supplied."),

            // SECTION VIEW - FL foot annotations. Legacy behavior: these are controlled by F, not FL.
            Keep("COB-PROD-SECTION-FL-C", Section, "FL_C@{FrBrSketch}", All(DimPositive("TL"), FootIn(C, CC, C_WITH_CBR)), "Keep FL_C when T is positive and foot is C, CC, or C_WITH_CBR."),
            Keep("COB-PROD-SECTION-FL-G", Section, "FL_G@{FrBrSketch}", All(DimPositive("TL"), FootIs(G)), "Keep FL_G when T is positive and foot is G."),
            Keep("COB-PROD-SECTION-FL-VG", Section, "FL_VG@{FrBrSketch}", All(DimPositive("TL"), FootIs(VG)), "Keep FL_VG when T is positive and foot is VG."),

            // SECTION VIEW - CG/CC dimensions
            Keep("COB-PROD-SECTION-G", Section, "G@{FrontSketch}", All(DimPositive("G"), FootIn(CG, CC)), "Keep G for CG/CC when G is positive."),
            Keep("COB-PROD-SECTION-CGR", Section, "CGR@{FrontSketch}", All(DimPositive("CGR"), FootIn(CG, CC)), "Keep CGR for CG/CC when CGR is positive."),
            Keep("COB-PROD-SECTION-CGD", Section, "CGD@{FrontSketch}", All(DimPositive("CGD"), FootIn(CG, CC)), "Keep CGD for CG/CC when CGD is positive."),
            Keep("COB-PROD-SECTION-G-180-TYPO", Section, "G@{CgDeg180TypoSketch}", All(ShankIs(ShankType.Deg180Rev), DimPositive("G"), FootIn(CG, CC)), "Preserve legacy 180-degree typo sketch name for G."),
            Keep("COB-PROD-SECTION-CGR-180-TYPO", Section, "CGR@{CgDeg180TypoSketch}", All(ShankIs(ShankType.Deg180Rev), DimPositive("CGR"), FootIn(CG, CC)), "Preserve legacy 180-degree typo sketch name for CGR."),
            Keep("COB-PROD-SECTION-CGD-180-TYPO", Section, "CGD@{CgDeg180TypoSketch}", All(ShankIs(ShankType.Deg180Rev), DimPositive("CGD"), FootIn(CG, CC)), "Preserve legacy 180-degree typo sketch name for CGD."),

            // SECTION VIEW - C_WITH_CBR dimensions
            Keep("COB-PROD-SECTION-CBRA", Section, "CBRA@{FrontSketch}", All(FootIs(C_WITH_CBR), DimPositive("CBRA")), "Keep CBRA only for C_WITH_CBR when CBRA is positive."),
            Keep("COB-PROD-SECTION-CBRL", Section, "CBRL@{FrontSketch}", All(FootIs(C_WITH_CBR), DimPositive("CBRL")), "Keep CBRL only for C_WITH_CBR when CBRL is positive."),

            // SECTION VIEW - FR/BR suffix annotations. FR and BR are intentionally independent.
            Keep("COB-PROD-SECTION-FR-C", Section, "FR_C@{FrBrSketch}", All(DimPositive("FR"), FootIn(C, CG, CC, C_WITH_CBR)), "Keep FR_C when FR is positive and the foot uses C suffix."),
            Keep("COB-PROD-SECTION-BR-C", Section, "BR_C@{FrBrSketch}", All(DimPositive("BR"), FootIn(C, CG, CC, C_WITH_CBR)), "Keep BR_C when BR is positive and the foot uses C suffix."),
            Keep("COB-PROD-SECTION-FR-G", Section, "FR_G@{FrBrSketch}", All(DimPositive("FR"), FootIs(G)), "Keep FR_G when FR is positive and foot is G."),
            Keep("COB-PROD-SECTION-BR-G", Section, "BR_G@{FrBrSketch}", All(DimPositive("BR"), FootIs(G)), "Keep BR_G when BR is positive and foot is G."),
            Keep("COB-PROD-SECTION-FR-VG", Section, "FR_VG@{FrBrSketch}", All(DimPositive("FR"), FootIs(VG)), "Keep FR_VG when FR is positive and foot is VG."),
            Keep("COB-PROD-SECTION-BR-VG", Section, "BR_VG@{FrBrSketch}", All(DimPositive("BR"), FootIs(VG)), "Keep BR_VG when BR is positive and foot is VG.")
        };
    }
}
