using System;
using System.Collections.Generic;

using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Conditions.AnnotationConditions;
using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain.AnnotationView;

namespace WAD.Runner.DrawingAutomation.Wedges.ABT.Annotations;

public abstract class AbtAnnotationRuleCatalogBase : AnnotationRuleCatalogBase
{
    private const string AnnotationTopPlan = "annotation_top_plan";
    private const string AnnotationRightPlan = "annotation_right_plan";
    private const string AnnotationStdFrontPlan = "annotation_std_front_plan";
    private const string AnnotationRevFrontPlan = "annotation_rev_front_plan";
    private const string AnnotationFrontPlan = "annotation_front_plan";
    private const string AnnotationRevPlan = "annotation_rev_plan";

    protected IReadOnlyList<AnnotationKeepRule> BuildPgbProductionCustomerRules(string idPrefix)
    {
        var isStd = ShankIs(AbtAnnotationShankTypes.Std);
        var isRev = ShankIs(AbtAnnotationShankTypes.Rev);
        var hasVbl = DimPositive("VBL");
        var noVbl = Not(hasVbl);

        return new List<AnnotationKeepRule>
        {
            Keep($"{idPrefix}-SIDE-STD-BA", Side, $"BA@{AnnotationStdFrontPlan}", All(isStd, noVbl), "ABT PGB STD Side keeps BA when VBL is not positive."),
            Keep($"{idPrefix}-SIDE-STD-VBL", Side, $"VBL@{AnnotationStdFrontPlan}", All(isStd, hasVbl), "ABT PGB STD Side keeps VBL when VBL is positive."),
            Keep($"{idPrefix}-SIDE-STD-BA-SLB", Side, $"BA_SLB@{AnnotationStdFrontPlan}", All(isStd, hasVbl), "ABT PGB STD Side keeps BA_SLB when VBL is positive."),
            Keep($"{idPrefix}-SIDE-REV-BA", Side, $"BA@{AnnotationRevFrontPlan}", All(isRev, noVbl), "ABT PGB REV Side keeps BA when VBL is not positive."),
            Keep($"{idPrefix}-SIDE-REV-VBL", Side, $"VBL@{AnnotationRevFrontPlan}", All(isRev, hasVbl), "ABT PGB REV Side keeps VBL when VBL is positive."),
            Keep($"{idPrefix}-SIDE-REV-BA-SLB", Side, $"BA_SLB@{AnnotationRevFrontPlan}", All(isRev, hasVbl), "ABT PGB REV Side keeps BA_SLB when VBL is positive."),

            Keep($"{idPrefix}-FRONT-TL", Front, $"TL@{AnnotationRightPlan}", Always(), "ABT PGB Front always keeps TL."),
            Keep($"{idPrefix}-FRONT-VR", Front, $"VR@{AnnotationRightPlan}", DimPositive("VR"), "ABT PGB Front keeps VR when VR is positive."),

            Keep($"{idPrefix}-TOP-TD", Top, $"TD@{AnnotationTopPlan}", Always(), "ABT PGB Top keeps TD."),
            Keep($"{idPrefix}-TOP-TDF-STD", Top, $"TDF_STD@{AnnotationTopPlan}", isStd, "ABT PGB Top keeps TDF_STD for SW_STD."),
            Keep($"{idPrefix}-TOP-TDF-REV", Top, $"TDF_REV@{AnnotationTopPlan}", isRev, "ABT PGB Top keeps TDF_REV for SW_180REV."),

            Keep($"{idPrefix}-DETAIL-W", Detail, $"W_NOM@{AnnotationRightPlan}", Always(), "ABT PGB Detail keeps W_NOM."),
            Keep($"{idPrefix}-DETAIL-ISA", Detail, $"ISA@{AnnotationRightPlan}", Always(), "ABT PGB Detail keeps ISA."),
            Keep($"{idPrefix}-DETAIL-VW", Detail, $"VW@{AnnotationRightPlan}", DimPositive("VW"), "ABT PGB Detail keeps VW when VW is positive."),
            Keep($"{idPrefix}-DETAIL-VRA", Detail, $"VRA@{AnnotationRightPlan}", DimPositive("VR"), "ABT PGB Detail keeps VRA when VR is positive."),

            Keep($"{idPrefix}-SECTION-STD-FL", Section, $"FL@{AnnotationStdFrontPlan}", isStd, "ABT PGB STD Section keeps FL."),
            Keep($"{idPrefix}-SECTION-STD-T", Section, $"T@{AnnotationStdFrontPlan}", isStd, "ABT PGB STD Section keeps T."),
            Keep($"{idPrefix}-SECTION-REV-FL", Section, $"FL@{AnnotationRevFrontPlan}", isRev, "ABT PGB REV Section keeps FL."),
            Keep($"{idPrefix}-SECTION-REV-T", Section, $"T@{AnnotationRevFrontPlan}", isRev, "ABT PGB REV Section keeps T.")
        };
    }

    protected IReadOnlyList<AnnotationKeepRule> BuildFgProductionCustomerRules(string idPrefix, bool includeProductionOnlyRules)
    {
        var isStd = ShankIs(AbtAnnotationShankTypes.Std);
        var isRev = ShankIs(AbtAnnotationShankTypes.Rev);
        var hasVbl = DimPositive("VBL");
        var noVbl = Not(hasVbl);
        var hasRa2 = DimPositive("RA2");
        var hasCbr = DimPositive("CBR");
        var noCbr = Not(hasCbr);
        var froEqualsFr = TraitIs(AbtAnnotationTraitNames.FroEqualsFr, AbtAnnotationTraitValues.True);
        var froDiffersFromFr = Not(froEqualsFr);
        var isStdHole = FeedHoleIs(AbtAnnotationFeedHoleTypes.Standard);
        var isOvalHole = FeedHoleIs(AbtAnnotationFeedHoleTypes.Oval);
        var isSlot = FeedHoleIs(AbtAnnotationFeedHoleTypes.Slot);
        var isVgFoot = FootIs(AbtAnnotationFootOptions.Vg);
        var isGFoot = FootIs(AbtAnnotationFootOptions.G);
        var isCFoot = FootIn(AbtAnnotationFootOptions.C, AbtAnnotationFootOptions.CWithCbr);

        var rules = new List<AnnotationKeepRule>
        {
            Keep($"{idPrefix}-SIDE-STD-BA", Side, $"BA@{AnnotationStdFrontPlan}", All(isStd, noVbl), "ABT FG STD Side keeps BA when VBL is not positive."),
            Keep($"{idPrefix}-SIDE-STD-VBL", Side, $"VBL@{AnnotationStdFrontPlan}", All(isStd, hasVbl), "ABT FG STD Side keeps VBL when VBL is positive."),
            Keep($"{idPrefix}-SIDE-STD-BA-SLB", Side, $"BA_SLB@{AnnotationStdFrontPlan}", All(isStd, hasVbl), "ABT FG STD Side keeps BA_SLB when VBL is positive."),
            Keep($"{idPrefix}-SIDE-REV-BA", Side, $"BA@{AnnotationRevFrontPlan}", All(isRev, noVbl), "ABT FG REV Side keeps BA when VBL is not positive."),
            Keep($"{idPrefix}-SIDE-REV-VBL", Side, $"VBL@{AnnotationRevFrontPlan}", All(isRev, hasVbl), "ABT FG REV Side keeps VBL when VBL is positive."),
            Keep($"{idPrefix}-SIDE-REV-BA-SLB", Side, $"BA_SLB@{AnnotationRevFrontPlan}", All(isRev, hasVbl), "ABT FG REV Side keeps BA_SLB when VBL is positive."),

            Keep($"{idPrefix}-FRONT-TL", Front, $"TL@{AnnotationRightPlan}", Always(), "ABT FG Front always keeps TL."),
            Keep($"{idPrefix}-FRONT-VR", Front, $"VR@{AnnotationRightPlan}", DimPositive("VR"), "ABT FG Front keeps VR when VR is positive."),

            Keep($"{idPrefix}-TOP-TD", Top, $"TD@{AnnotationTopPlan}", Always(), "ABT FG Top keeps TD."),
            Keep($"{idPrefix}-TOP-TDF-STD", Top, $"TDF_STD@{AnnotationTopPlan}", isStd, "ABT FG Top keeps TDF_STD for SW_STD."),
            Keep($"{idPrefix}-TOP-TDF-REV", Top, $"TDF_REV@{AnnotationTopPlan}", isRev, "ABT FG Top keeps TDF_REV for SW_180REV."),

            Keep($"{idPrefix}-DETAIL-W", Detail, $"W_NOM@{AnnotationRightPlan}", Always(), "ABT FG Detail keeps W_NOM."),
            Keep($"{idPrefix}-DETAIL-ISA", Detail, $"ISA@{AnnotationRightPlan}", Always(), "ABT FG Detail keeps ISA."),
            Keep($"{idPrefix}-DETAIL-VW", Detail, $"VW@{AnnotationRightPlan}", DimPositive("VW"), "ABT FG Detail keeps VW when VW is positive."),
            Keep($"{idPrefix}-DETAIL-VRA", Detail, $"VRA@{AnnotationRightPlan}", DimPositive("VR"), "ABT FG Detail keeps VRA when VR is positive."),
            Keep($"{idPrefix}-DETAIL-VG-B", Detail, $"B@{AnnotationRightPlan}", isVgFoot, "ABT FG Detail keeps B for VG."),
            Keep($"{idPrefix}-DETAIL-VG-GA", Detail, $"GA@{AnnotationRightPlan}", isVgFoot, "ABT FG Detail keeps GA for VG."),
            Keep($"{idPrefix}-DETAIL-VG-GD", Detail, $"GD@{AnnotationRightPlan}", isVgFoot, "ABT FG Detail keeps GD for VG."),
            Keep($"{idPrefix}-DETAIL-G-GD", Detail, $"GD_G@{AnnotationRightPlan}", isGFoot, "ABT FG Detail keeps GD_G for G."),
            Keep($"{idPrefix}-DETAIL-G-GO", Detail, $"GO@{AnnotationRightPlan}", isGFoot, "ABT FG Detail keeps GO for G."),
            Keep($"{idPrefix}-DETAIL-C-CL", Detail, $"CL@{AnnotationRightPlan}", isCFoot, "ABT FG Detail keeps CL for C/C-with-CBR."),
            Keep($"{idPrefix}-DETAIL-C-CD", Detail, $"CD_NOM@{AnnotationRightPlan}", isCFoot, "ABT FG Detail keeps CD_NOM for C/C-with-CBR."),

            Keep($"{idPrefix}-SECTION-STD-FL", Section, $"FL@{AnnotationStdFrontPlan}", isStd, "ABT FG STD Section keeps FL."),
            Keep($"{idPrefix}-SECTION-STD-T", Section, $"T@{AnnotationStdFrontPlan}", isStd, "ABT FG STD Section keeps T."),
            Keep($"{idPrefix}-SECTION-STD-RA", Section, $"RA@{AnnotationStdFrontPlan}", isStd, "ABT FG STD Section keeps RA."),
            Keep($"{idPrefix}-SECTION-STD-RA2", Section, $"RA2@{AnnotationStdFrontPlan}", All(isStd, hasRa2), "ABT FG STD Section keeps RA2 when RA2 is positive."),
            Keep($"{idPrefix}-SECTION-STD-CA", Section, $"CA@{AnnotationStdFrontPlan}", isStd, "ABT FG STD Section keeps CA."),
            Keep($"{idPrefix}-SECTION-STD-FD", Section, $"FD@{AnnotationStdFrontPlan}", isStd, "ABT FG STD Section keeps FD."),
            Keep($"{idPrefix}-SECTION-STD-ERL", Section, $"ERL@{AnnotationStdFrontPlan}", isStd, "ABT FG STD Section keeps ERL."),
            Keep($"{idPrefix}-SECTION-STD-FNA", Section, $"FNA@{AnnotationStdFrontPlan}", isStd, "ABT FG STD Section keeps FNA."),
            //Keep($"{idPrefix}-SECTION-STD-HA", Section, $"HA@{AnnotationStdFrontPlan}", isStd, "ABT FG STD Section keeps HA."),
            Keep($"{idPrefix}-SECTION-STD-H", Section, $"H@{AnnotationStdFrontPlan}", All(isStd, isStdHole), "ABT FG STD Section keeps H for a standard hole."),
            Keep($"{idPrefix}-SECTION-STD-HH", Section, $"HH@{AnnotationStdFrontPlan}", All(isStd, isOvalHole), "ABT FG STD Section keeps HH for an oval hole."),
            Keep($"{idPrefix}-SECTION-STD-ST", Section, $"ST@{AnnotationStdFrontPlan}", All(isStd, isSlot), "ABT FG STD Section keeps ST for a slot."),
            Keep($"{idPrefix}-SECTION-STD-FR", Section, $"FR@{AnnotationStdFrontPlan}", All(isStd, froDiffersFromFr), "ABT FG STD Section keeps FR only when FRO differs from FR."),
            Keep($"{idPrefix}-SECTION-STD-BR", Section, $"BR@{AnnotationStdFrontPlan}", All(isStd, noCbr), "ABT FG STD Section keeps BR only when CBR is not positive."),
            Keep($"{idPrefix}-SECTION-STD-FRO", Section, $"FRO@{AnnotationStdFrontPlan}", isStd, "ABT FG STD Section keeps FRO."),

            Keep($"{idPrefix}-SECTION-REV-FL", Section, $"FL@{AnnotationRevFrontPlan}", isRev, "ABT FG REV Section keeps FL."),
            Keep($"{idPrefix}-SECTION-REV-T", Section, $"T@{AnnotationRevFrontPlan}", isRev, "ABT FG REV Section keeps T."),
            Keep($"{idPrefix}-SECTION-REV-RA", Section, $"RA@{AnnotationRevFrontPlan}", isRev, "ABT FG REV Section keeps RA."),
            Keep($"{idPrefix}-SECTION-REV-RA2", Section, $"RA2@{AnnotationRevFrontPlan}", All(isRev, hasRa2), "ABT FG REV Section keeps RA2 when RA2 is positive."),
            Keep($"{idPrefix}-SECTION-REV-CA", Section, $"CA@{AnnotationRevFrontPlan}", isRev, "ABT FG REV Section keeps CA."),
            Keep($"{idPrefix}-SECTION-REV-FD", Section, $"FD@{AnnotationRevFrontPlan}", isRev, "ABT FG REV Section keeps FD."),
            Keep($"{idPrefix}-SECTION-REV-ERL", Section, $"ERL@{AnnotationRevFrontPlan}", isRev, "ABT FG REV Section keeps ERL."),
            Keep($"{idPrefix}-SECTION-REV-FNA", Section, $"FNA@{AnnotationRevFrontPlan}", isRev, "ABT FG REV Section keeps FNA."),
            //Keep($"{idPrefix}-SECTION-REV-HA", Section, $"HA@annotation_rev_front_plan", isRev, "ABT FG REV Section keeps HA."),
            Keep($"{idPrefix}-SECTION-REV-H", Section, $"H@{AnnotationRevFrontPlan}", All(isRev, isStdHole), "ABT FG REV Section keeps H for a standard hole."),
            Keep($"{idPrefix}-SECTION-REV-HH", Section, $"HH@{AnnotationRevFrontPlan}", All(isRev, isOvalHole), "ABT FG REV Section keeps HH for an oval hole."),
            Keep($"{idPrefix}-SECTION-REV-ST", Section, $"ST@{AnnotationRevFrontPlan}", All(isRev, isSlot), "ABT FG REV Section keeps ST for a slot."),
            Keep($"{idPrefix}-SECTION-REV-FR", Section, $"FR@{AnnotationRevFrontPlan}", All(isRev, froDiffersFromFr), "ABT FG REV Section keeps FR only when FRO differs from FR."),
            Keep($"{idPrefix}-SECTION-REV-BR", Section, $"BR@{AnnotationRevFrontPlan}", All(isRev, noCbr), "ABT FG REV Section keeps BR only when CBR is not positive."),
            Keep($"{idPrefix}-SECTION-REV-FRO", Section, $"FRO@{AnnotationRevFrontPlan}", isRev, "ABT FG REV Section keeps FRO.")
        };

        if (!includeProductionOnlyRules)
            return rules;

        rules.Add(Keep($"{idPrefix}-SECTION-PROD-STD-BF", Section, $"BF@{AnnotationFrontPlan}", isStd, "ABT FG Production STD Section keeps BF."));
        rules.Add(Keep($"{idPrefix}-SECTION-PROD-STD-F", Section, $"F@{AnnotationFrontPlan}", isStd, "ABT FG Production STD Section keeps F."));
        rules.Add(Keep($"{idPrefix}-SECTION-PROD-STD-Y", Section, $"Y@{AnnotationFrontPlan}", isStd, "ABT FG Production STD Section keeps Y."));
        rules.Add(Keep($"{idPrefix}-SECTION-PROD-STD-CBRL", Section, $"CBRL@{AnnotationFrontPlan}", isStd, "ABT FG Production STD Section keeps CBRL."));
        rules.Add(Keep($"{idPrefix}-SECTION-PROD-STD-CBRA", Section, $"CBRA@{AnnotationFrontPlan}", isStd, "ABT FG Production STD Section keeps CBRA."));
        rules.Add(Keep($"{idPrefix}-SECTION-PROD-REV-BF", Section, $"BF@{AnnotationRevPlan}", isRev, "ABT FG Production REV Section keeps BF."));
        rules.Add(Keep($"{idPrefix}-SECTION-PROD-REV-F", Section, $"F@{AnnotationRevPlan}", isRev, "ABT FG Production REV Section keeps F."));
        rules.Add(Keep($"{idPrefix}-SECTION-PROD-REV-Y", Section, $"Y@{AnnotationRevPlan}", isRev, "ABT FG Production REV Section keeps Y."));
        rules.Add(Keep($"{idPrefix}-SECTION-PROD-REV-CBRL", Section, $"CBRL@{AnnotationRevPlan}", isRev, "ABT FG Production REV Section keeps CBRL."));
        rules.Add(Keep($"{idPrefix}-SECTION-PROD-REV-CBRA", Section, $"CBRA@{AnnotationRevPlan}", isRev, "ABT FG Production REV Section keeps CBRA."));

        return rules;
    }

    protected static IReadOnlyList<AnnotationKeepRule> BuildEmptyOverlayRules()
        => Array.Empty<AnnotationKeepRule>();
}

public sealed class AbtFgProductionAnnotationRules : AbtAnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.AbtFgProduction;
    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public AbtFgProductionAnnotationRules()
        => Rules = BuildFgProductionCustomerRules("ABT-FG-PROD", includeProductionOnlyRules: true);
}

public sealed class AbtFgCustomerAnnotationRules : AbtAnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.AbtFgCustomer;
    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public AbtFgCustomerAnnotationRules()
        => Rules = BuildFgProductionCustomerRules("ABT-FG-CUST", includeProductionOnlyRules: false);
}

public sealed class AbtFgOverlayAnnotationRules : AbtAnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.AbtFgOverlay;
    public override IReadOnlyList<AnnotationKeepRule> Rules { get; } = BuildEmptyOverlayRules();
}

public sealed class AbtPgbProductionAnnotationRules : AbtAnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.AbtPgbProduction;
    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public AbtPgbProductionAnnotationRules()
        => Rules = BuildPgbProductionCustomerRules("ABT-PGB-PROD");
}

public sealed class AbtPgbOverlayAnnotationRules : AbtAnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.AbtPgbOverlay;
    public override IReadOnlyList<AnnotationKeepRule> Rules { get; } = BuildEmptyOverlayRules();
}
