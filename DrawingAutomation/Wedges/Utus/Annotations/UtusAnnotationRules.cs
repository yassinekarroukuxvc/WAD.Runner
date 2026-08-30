using System;
using System.Collections.Generic;

using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Conditions.AnnotationConditions;
using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain.AnnotationView;

namespace WAD.Runner.DrawingAutomation.Wedges.Utus.Annotations;

public abstract class UtusAnnotationRuleCatalogBase : AnnotationRuleCatalogBase
{
    private const string AnnotationTopPlan = "annotation_top_plan";
    private const string AnnotationTopPlanTemplateTypo = "annoation_top_plan";
    private const string AnnotationRightPlan = "annotation_right_plan";
    private const string AnnotationStdFrontPlan = "annotation_std_front_plan";
    private const string AnnotationRevFrontPlan = "annotation_rev_front_plan";
    private const string AnnotationFrontPlan = "annotation_front_plan";
    private const string AnnotationRevPlan = "annotation_rev_plan";

    protected IReadOnlyList<AnnotationKeepRule> BuildPgbProductionCustomerRules(string idPrefix)
    {
        var isStd = ShankIs(UtusAnnotationShankTypes.Std);
        var isRev = ShankIs(UtusAnnotationShankTypes.Rev);
        var hasVbl = DimPositive("VBL");
        var noVbl = Not(hasVbl);

        return new List<AnnotationKeepRule>
        {
            Keep($"{idPrefix}-SIDE-STD-BA", Side, $"BA@{AnnotationStdFrontPlan}", All(isStd, noVbl), "UTUS PGB STD Side keeps BA when VBL is not positive."),
            Keep($"{idPrefix}-SIDE-STD-VBL", Side, $"VBL@{AnnotationStdFrontPlan}", All(isStd, hasVbl), "UTUS PGB STD Side keeps VBL when VBL is positive."),
            Keep($"{idPrefix}-SIDE-STD-BA-SLB", Side, $"BA_SLB@{AnnotationStdFrontPlan}", All(isStd, hasVbl), "UTUS PGB STD Side keeps BA_SLB when VBL is positive."),
            Keep($"{idPrefix}-SIDE-REV-BA", Side, $"BA@{AnnotationRevFrontPlan}", All(isRev, noVbl), "UTUS PGB REV Side keeps BA when VBL is not positive."),
            Keep($"{idPrefix}-SIDE-REV-VBL", Side, $"VBL@{AnnotationRevFrontPlan}", All(isRev, hasVbl), "UTUS PGB REV Side keeps VBL when VBL is positive."),
            Keep($"{idPrefix}-SIDE-REV-BA-SLB", Side, $"BA_SLB@{AnnotationRevFrontPlan}", All(isRev, hasVbl), "UTUS PGB REV Side keeps BA_SLB when VBL is positive."),

            Keep($"{idPrefix}-FRONT-TL", Front, $"TL@{AnnotationRightPlan}", Always(), "UTUS PGB Front always keeps TL."),
            Keep($"{idPrefix}-FRONT-VR", Front, $"VR@{AnnotationRightPlan}", DimPositive("VR"), "UTUS PGB Front keeps VR when VR is positive."),

            KeepWithAliases($"{idPrefix}-TOP-TD", Top, $"TD@{AnnotationTopPlan}", new[] { $"TD@{AnnotationTopPlanTemplateTypo}" }, Always(), "UTUS PGB Top always keeps TD."),
            KeepWithAliases($"{idPrefix}-TOP-TDF-STD", Top, $"TDF_STD@{AnnotationTopPlan}", new[] { $"TDF_STD@{AnnotationTopPlanTemplateTypo}" }, isStd, "UTUS PGB Top keeps TDF_STD for SW_STD."),
            KeepWithAliases($"{idPrefix}-TOP-TDF-REV", Top, $"TDF_REV@{AnnotationTopPlan}", new[] { $"TDF_REV@{AnnotationTopPlanTemplateTypo}" }, isRev, "UTUS PGB Top keeps TDF_REV for SW_180REV."),

            Keep($"{idPrefix}-DETAIL-W", Detail, $"W_NOM@{AnnotationRightPlan}", Always(), "UTUS PGB Detail always keeps W."),
            Keep($"{idPrefix}-DETAIL-ISA", Detail, $"ISA@{AnnotationRightPlan}", Always(), "UTUS PGB Detail always keeps ISA."),
            Keep($"{idPrefix}-DETAIL-VW", Detail, $"VW@{AnnotationRightPlan}", DimPositive("VW"), "UTUS PGB Detail keeps VW when VW is positive."),
            Keep($"{idPrefix}-DETAIL-VRA", Detail, $"VRA@{AnnotationRightPlan}", DimPositive("VR"), "UTUS PGB Detail keeps VRA when VR is positive."),

            Keep($"{idPrefix}-SECTION-STD-FL", Section, $"FL@{AnnotationStdFrontPlan}", isStd, "UTUS PGB STD Section keeps FL."),
            Keep($"{idPrefix}-SECTION-STD-T", Section, $"T@{AnnotationStdFrontPlan}", isStd, "UTUS PGB STD Section keeps T."),
            Keep($"{idPrefix}-SECTION-REV-FL", Section, $"FL@{AnnotationRevFrontPlan}", isRev, "UTUS PGB REV Section keeps FL."),
            Keep($"{idPrefix}-SECTION-REV-T", Section, $"T@{AnnotationRevFrontPlan}", isRev, "UTUS PGB REV Section keeps T.")
        };
    }

    protected IReadOnlyList<AnnotationKeepRule> BuildFgProductionCustomerRules(
        string idPrefix,
        bool includeProductionOnlyRules)
    {
        var isStd = ShankIs(UtusAnnotationShankTypes.Std);
        var isRev = ShankIs(UtusAnnotationShankTypes.Rev);
        var hasVbl = DimPositive("VBL");
        var noVbl = Not(hasVbl);
        var hasRa2 = DimPositive("RA2");
        var hasCbr = DimsPositive("CBRL", "CBRD");
        var noCbr = Not(hasCbr);
        var froEqualsFr = TraitIs(UtusAnnotationTraitNames.FroEqualsFr, UtusAnnotationTraitValues.True);
        var froDiffersFromFr = Not(froEqualsFr);
        var isStdHole = FeedHoleIs(UtusAnnotationFeedHoleTypes.Standard);
        var isOvalHole = FeedHoleIs(UtusAnnotationFeedHoleTypes.Oval);
        var isSlot = FeedHoleIs(UtusAnnotationFeedHoleTypes.Slot);
        var isVgFoot = FootIs(UtusAnnotationFootOptions.Vg);
        var isGFoot = FootIs(UtusAnnotationFootOptions.G);
        var isCFoot = FootIn(UtusAnnotationFootOptions.C, UtusAnnotationFootOptions.CWithCbr);

        var rules = new List<AnnotationKeepRule>
        {
            Keep($"{idPrefix}-SIDE-STD-BA", Side, $"BA@{AnnotationStdFrontPlan}", All(isStd, noVbl), "UTUS FG STD Side keeps BA when VBL is not positive."),
            Keep($"{idPrefix}-SIDE-STD-VBL", Side, $"VBL@{AnnotationStdFrontPlan}", All(isStd, hasVbl), "UTUS FG STD Side keeps VBL when VBL is positive."),
            Keep($"{idPrefix}-SIDE-STD-BA-SLB", Side, $"BA_SLB@{AnnotationStdFrontPlan}", All(isStd, hasVbl), "UTUS FG STD Side keeps BA_SLB when VBL is positive."),
            Keep($"{idPrefix}-SIDE-REV-BA", Side, $"BA@{AnnotationRevFrontPlan}", All(isRev, noVbl), "UTUS FG REV Side keeps BA when VBL is not positive."),
            Keep($"{idPrefix}-SIDE-REV-VBL", Side, $"VBL@{AnnotationRevFrontPlan}", All(isRev, hasVbl), "UTUS FG REV Side keeps VBL when VBL is positive."),
            Keep($"{idPrefix}-SIDE-REV-BA-SLB", Side, $"BA_SLB@{AnnotationRevFrontPlan}", All(isRev, hasVbl), "UTUS FG REV Side keeps BA_SLB when VBL is positive."),

            Keep($"{idPrefix}-FRONT-TL", Front, $"TL@{AnnotationRightPlan}", Always(), "UTUS FG Front always keeps TL."),
            Keep($"{idPrefix}-FRONT-VR", Front, $"VR@{AnnotationRightPlan}", DimPositive("VR"), "UTUS FG Front keeps VR when VR is positive."),

            KeepWithAliases($"{idPrefix}-TOP-TD", Top, $"TD@{AnnotationTopPlan}", new[] { $"TD@{AnnotationTopPlanTemplateTypo}" }, Always(), "UTUS FG Top always keeps TD."),
            KeepWithAliases($"{idPrefix}-TOP-TDF-STD", Top, $"TDF_STD@{AnnotationTopPlan}", new[] { $"TDF_STD@{AnnotationTopPlanTemplateTypo}" }, isStd, "UTUS FG Top keeps TDF_STD for SW_STD."),
            KeepWithAliases($"{idPrefix}-TOP-TDF-REV", Top, $"TDF_REV@{AnnotationTopPlan}", new[] { $"TDF_REV@{AnnotationTopPlanTemplateTypo}" }, isRev, "UTUS FG Top keeps TDF_REV for SW_180REV."),

            Keep($"{idPrefix}-DETAIL-W", Detail, $"W_NOM@{AnnotationRightPlan}", Always(), "UTUS FG Detail always keeps W."),
            Keep($"{idPrefix}-DETAIL-ISA", Detail, $"ISA@{AnnotationRightPlan}", Always(), "UTUS FG Detail always keeps ISA."),
            Keep($"{idPrefix}-DETAIL-W2", Detail, $"W2@{AnnotationRightPlan}", Always(), "UTUS FG Detail keeps W2."),
            Keep($"{idPrefix}-DETAIL-VW", Detail, $"VW@{AnnotationRightPlan}", DimPositive("VW"), "UTUS FG Detail keeps VW when VW is positive."),
            Keep($"{idPrefix}-DETAIL-VRA", Detail, $"VRA@{AnnotationRightPlan}", DimPositive("VR"), "UTUS FG Detail keeps VRA when VR is positive."),
            Keep($"{idPrefix}-DETAIL-VG-B", Detail, $"B_NOM@{AnnotationRightPlan}", isVgFoot, "UTUS FG Detail keeps B for VG."),
            Keep($"{idPrefix}-DETAIL-VG-GA", Detail, $"GA@{AnnotationRightPlan}", isVgFoot, "UTUS FG Detail keeps GA for VG."),
            Keep($"{idPrefix}-DETAIL-VG-GD", Detail, $"GD@{AnnotationRightPlan}", isVgFoot, "UTUS FG Detail keeps GD for VG."),
            Keep($"{idPrefix}-DETAIL-G-GD", Detail, $"GD_G@{AnnotationRightPlan}", isGFoot, "UTUS FG Detail keeps GD_G for G."),
            Keep($"{idPrefix}-DETAIL-G-GO", Detail, $"GO@{AnnotationRightPlan}", isGFoot, "UTUS FG Detail keeps GO for G."),
            Keep($"{idPrefix}-DETAIL-C-CL", Detail, $"CL@{AnnotationRightPlan}", isCFoot, "UTUS FG Detail keeps CL for C/C-with-CBR."),
            Keep($"{idPrefix}-DETAIL-C-CD", Detail, $"CD_NOM@{AnnotationRightPlan}", isCFoot, "UTUS FG Detail keeps CD for C/C-with-CBR."),

            Keep($"{idPrefix}-SECTION-STD-FL", Section, $"FL@{AnnotationStdFrontPlan}", isStd, "UTUS FG STD Section keeps FL."),
            Keep($"{idPrefix}-SECTION-STD-T", Section, $"T@{AnnotationStdFrontPlan}", isStd, "UTUS FG STD Section keeps T."),
            Keep($"{idPrefix}-SECTION-STD-RA", Section, $"RA@{AnnotationStdFrontPlan}", isStd, "UTUS FG STD Section keeps RA."),
            Keep($"{idPrefix}-SECTION-STD-RA2", Section, $"RA2@{AnnotationStdFrontPlan}", All(isStd, hasRa2), "UTUS FG STD Section keeps RA2 when RA2 is positive."),
            Keep($"{idPrefix}-SECTION-STD-CA", Section, $"CA@{AnnotationStdFrontPlan}", isStd, "UTUS FG STD Section keeps CA."),
            Keep($"{idPrefix}-SECTION-STD-FD", Section, $"FD@{AnnotationStdFrontPlan}", isStd, "UTUS FG STD Section keeps FD."),
            Keep($"{idPrefix}-SECTION-STD-ERL", Section, $"ERL@{AnnotationStdFrontPlan}", isStd, "UTUS FG STD Section keeps ERL."),
            Keep($"{idPrefix}-SECTION-STD-FNA", Section, $"FNA@{AnnotationStdFrontPlan}", isStd, "UTUS FG STD Section keeps FNA."),
            //Keep($"{idPrefix}-SECTION-STD-HA", Section, $"HA@{AnnotationStdFrontPlan}", isStd, "UTUS FG STD Section keeps HA."),
            Keep($"{idPrefix}-SECTION-STD-H", Section, $"H@{AnnotationStdFrontPlan}", All(isStd, isStdHole), "UTUS FG STD Section keeps H for a standard hole."),
            Keep($"{idPrefix}-SECTION-STD-HH", Section, $"HH@{AnnotationStdFrontPlan}", All(isStd, isOvalHole), "UTUS FG STD Section keeps HH for an oval hole."),
            Keep($"{idPrefix}-SECTION-STD-ST", Section, $"ST@{AnnotationStdFrontPlan}", All(isStd, isSlot), "UTUS FG STD Section keeps ST for a slot."),
            Keep($"{idPrefix}-SECTION-STD-FR", Section, $"FR@{AnnotationStdFrontPlan}", All(isStd, froDiffersFromFr), "UTUS FG STD Section keeps FR only when FRO differs from FR."),
            Keep($"{idPrefix}-SECTION-STD-BR", Section, $"BR@{AnnotationStdFrontPlan}", All(isStd, noCbr), "UTUS FG STD Section keeps BR only when CBRL/CBRD do not define CBR."),
            Keep($"{idPrefix}-SECTION-STD-FRO", Section, $"FRO@{AnnotationStdFrontPlan}", isStd, "UTUS FG STD Section keeps FRO."),

            Keep($"{idPrefix}-SECTION-REV-FL", Section, $"FL@{AnnotationRevFrontPlan}", isRev, "UTUS FG REV Section keeps FL."),
            Keep($"{idPrefix}-SECTION-REV-T", Section, $"T@{AnnotationRevFrontPlan}", isRev, "UTUS FG REV Section keeps T."),
            Keep($"{idPrefix}-SECTION-REV-RA", Section, $"RA@{AnnotationRevFrontPlan}", isRev, "UTUS FG REV Section keeps RA."),
            Keep($"{idPrefix}-SECTION-REV-RA2", Section, $"RA2@{AnnotationRevFrontPlan}", All(isRev, hasRa2), "UTUS FG REV Section keeps RA2 when RA2 is positive."),
            Keep($"{idPrefix}-SECTION-REV-CA", Section, $"CA@{AnnotationRevFrontPlan}", isRev, "UTUS FG REV Section keeps CA."),
            Keep($"{idPrefix}-SECTION-REV-FD", Section, $"D1@{AnnotationRevFrontPlan}", isRev, "UTUS FG REV Section keeps FD."),
            Keep($"{idPrefix}-SECTION-REV-ERL", Section, $"ERL@{AnnotationRevFrontPlan}", isRev, "UTUS FG REV Section keeps ERL."),
            Keep($"{idPrefix}-SECTION-REV-FNA", Section, $"FNA@{AnnotationRevFrontPlan}", isRev, "UTUS FG REV Section keeps FNA."),
            //Keep($"{idPrefix}-SECTION-REV-HA", Section, $"HA@{AnnotationRevFrontPlan}", isRev, "UTUS FG REV Section keeps HA."),
            Keep($"{idPrefix}-SECTION-REV-H", Section, $"H@{AnnotationRevFrontPlan}", All(isRev, isStdHole), "UTUS FG REV Section keeps H for a standard hole."),
            Keep($"{idPrefix}-SECTION-REV-HH", Section, $"HH@{AnnotationRevFrontPlan}", All(isRev, isOvalHole), "UTUS FG REV Section keeps HH for an oval hole."),
            Keep($"{idPrefix}-SECTION-REV-ST", Section, $"ST@{AnnotationRevFrontPlan}", All(isRev, isSlot), "UTUS FG REV Section keeps ST for a slot."),
            Keep($"{idPrefix}-SECTION-REV-FR", Section, $"fr@{AnnotationRevFrontPlan}", All(isRev, froDiffersFromFr), "UTUS FG REV Section keeps FR only when FRO differs from FR."),
            Keep($"{idPrefix}-SECTION-REV-BR", Section, $"BR@{AnnotationRevFrontPlan}", All(isRev, noCbr), "UTUS FG REV Section keeps BR only when CBRL/CBRD do not define CBR."),
            Keep($"{idPrefix}-SECTION-REV-FRO", Section, $"FRO@{AnnotationRevFrontPlan}", isRev, "UTUS FG REV Section keeps FRO.")
        };

        if (!includeProductionOnlyRules)
            return rules;

        rules.Add(KeepWithAliases($"{idPrefix}-SECTION-PROD-STD-BF", Section, $"BF@{AnnotationFrontPlan}", new[] { $"BF@{AnnotationStdFrontPlan}" }, isStd, "UTUS FG Production STD Section keeps BF."));
        rules.Add(KeepWithAliases($"{idPrefix}-SECTION-PROD-STD-F", Section, $"F@{AnnotationFrontPlan}", new[] { $"F@{AnnotationStdFrontPlan}" }, isStd, "UTUS FG Production STD Section keeps F."));
        rules.Add(KeepWithAliases($"{idPrefix}-SECTION-PROD-STD-Y", Section, $"Y@{AnnotationFrontPlan}", new[] { $"Y@{AnnotationStdFrontPlan}" }, isStd, "UTUS FG Production STD Section keeps Y."));
        rules.Add(KeepWithAliases($"{idPrefix}-SECTION-PROD-STD-CBRL", Section, $"CBRL@{AnnotationFrontPlan}", new[] { $"CBRL@{AnnotationStdFrontPlan}" }, isStd, "UTUS FG Production STD Section keeps CBRL."));
        rules.Add(KeepWithAliases($"{idPrefix}-SECTION-PROD-STD-CBRA", Section, $"CBRA@{AnnotationFrontPlan}", new[] { $"CBRA@{AnnotationStdFrontPlan}" }, isStd, "UTUS FG Production STD Section keeps CBRA."));
        rules.Add(KeepWithAliases($"{idPrefix}-SECTION-PROD-REV-BF", Section, $"BF@{AnnotationRevPlan}", new[] { $"BF@{AnnotationRevFrontPlan}" }, isRev, "UTUS FG Production REV Section keeps BF."));
        rules.Add(KeepWithAliases($"{idPrefix}-SECTION-PROD-REV-F", Section, $"F@{AnnotationRevPlan}", new[] { $"F@{AnnotationRevFrontPlan}" }, isRev, "UTUS FG Production REV Section keeps F."));
        rules.Add(KeepWithAliases($"{idPrefix}-SECTION-PROD-REV-Y", Section, $"Y@{AnnotationRevPlan}", new[] { $"Y@{AnnotationRevFrontPlan}" }, isRev, "UTUS FG Production REV Section keeps Y."));
        rules.Add(KeepWithAliases($"{idPrefix}-SECTION-PROD-REV-CBRL", Section, $"CBRL@{AnnotationRevPlan}", new[] { $"CBRL@{AnnotationRevFrontPlan}" }, isRev, "UTUS FG Production REV Section keeps CBRL."));
        rules.Add(KeepWithAliases($"{idPrefix}-SECTION-PROD-REV-CBRA", Section, $"CBRA@{AnnotationRevPlan}", new[] { $"CBRA@{AnnotationRevFrontPlan}" }, isRev, "UTUS FG Production REV Section keeps CBRA."));

        return rules;
    }

    protected static IReadOnlyList<AnnotationKeepRule> BuildEmptyOverlayRules()
        => Array.Empty<AnnotationKeepRule>();
}

public sealed class UtusFgProductionAnnotationRules : UtusAnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.UtusFgProduction;
    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public UtusFgProductionAnnotationRules()
        => Rules = BuildFgProductionCustomerRules("UTUS-FG-PROD", includeProductionOnlyRules: true);
}

public sealed class UtusFgCustomerAnnotationRules : UtusAnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.UtusFgCustomer;
    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public UtusFgCustomerAnnotationRules()
        => Rules = BuildFgProductionCustomerRules("UTUS-FG-CUST", includeProductionOnlyRules: false);
}

public sealed class UtusFgOverlayAnnotationRules : UtusAnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.UtusFgOverlay;
    public override IReadOnlyList<AnnotationKeepRule> Rules { get; } = BuildEmptyOverlayRules();
}

public sealed class UtusPgbProductionAnnotationRules : UtusAnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.UtusPgbProduction;
    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public UtusPgbProductionAnnotationRules()
        => Rules = BuildPgbProductionCustomerRules("UTUS-PGB-PROD");
}

public sealed class UtusPgbOverlayAnnotationRules : UtusAnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.UtusPgbOverlay;
    public override IReadOnlyList<AnnotationKeepRule> Rules { get; } = BuildEmptyOverlayRules();
}