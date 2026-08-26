using System;
using System.Collections.Generic;

using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Conditions.AnnotationConditions;
using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain.AnnotationView;

namespace WAD.Runner.DrawingAutomation.Wedges.M.Annotations;

public abstract class MAnnotationRuleCatalogBase : AnnotationRuleCatalogBase
{
    private const string AnnotationTopPlan = "annotation_top_plan";
    private const string AnnotationRightPlan = "annotation_right_plan";
    private const string AnnotationStdFrontPlan = "annotation_std_front_plan";
    private const string AnnotationRevFrontPlan = "annotation_rev_front_plan";
    private const string AnnotationFrontPlan = "annotation_front_plan";
    private const string AnnotationRevPlan = "annotation_rev_plan";

    protected IReadOnlyList<AnnotationKeepRule>
        BuildPgbProductionCustomerRules(
            string idPrefix)
    {
        var isStd =
            ShankIs(
                MAnnotationShankTypes.Std);

        var isRev =
            ShankIs(
                MAnnotationShankTypes.Rev);

        var hasVbl =
            DimPositive(
                "VBL");

        var noVbl =
            Not(
                hasVbl);

        return new List<AnnotationKeepRule>
        {
            Keep($"{idPrefix}-SIDE-STD-BA", Side, $"BA@{AnnotationStdFrontPlan}", All(isStd, noVbl), "M PGB STD Side keeps BA when VBL is not positive."),
            Keep($"{idPrefix}-SIDE-STD-VBL", Side, $"VBL@{AnnotationStdFrontPlan}", All(isStd, hasVbl), "M PGB STD Side keeps VBL when VBL is positive."),
            Keep($"{idPrefix}-SIDE-STD-BA-SLB", Side, $"BA_SLB@{AnnotationStdFrontPlan}", All(isStd, hasVbl), "M PGB STD Side keeps BA_SLB when VBL is positive."),

            Keep($"{idPrefix}-SIDE-REV-BA", Side, $"BA@{AnnotationRevFrontPlan}", All(isRev, noVbl), "M PGB REV Side keeps BA when VBL is not positive."),
            Keep($"{idPrefix}-SIDE-REV-VBL", Side, $"VBL@{AnnotationRevFrontPlan}", All(isRev, hasVbl), "M PGB REV Side keeps VBL when VBL is positive."),
            Keep($"{idPrefix}-SIDE-REV-BA-SLB", Side, $"BA_SLB@{AnnotationRevFrontPlan}", All(isRev, hasVbl), "M PGB REV Side keeps BA_SLB when VBL is positive."),

            Keep($"{idPrefix}-FRONT-TL", Front, $"TL@{AnnotationRightPlan}", Always(), "M PGB Front always keeps TL."),
            Keep($"{idPrefix}-FRONT-VR", Front, $"VR@{AnnotationRightPlan}", DimPositive("VR"), "M PGB Front keeps VR when VR is positive."),

            Keep($"{idPrefix}-TOP-TD", Top, $"TD@{AnnotationTopPlan}", Always(), "M PGB Top always keeps TD."),
            Keep($"{idPrefix}-TOP-TDF-STD", Top, $"TDF_STD@{AnnotationTopPlan}", isStd, "M PGB Top keeps TDF_STD for SW_STD."),
            Keep($"{idPrefix}-TOP-TDF-REV", Top, $"TDF_REV@{AnnotationTopPlan}", isRev, "M PGB Top keeps TDF_REV for SW_180REV."),

            Keep($"{idPrefix}-DETAIL-W", Detail, $"W_NOM@{AnnotationRightPlan}", Always(), "M PGB Detail keeps W_NOM."),
            Keep($"{idPrefix}-DETAIL-ISA", Detail, $"ISA@{AnnotationRightPlan}", Always(), "M PGB Detail keeps ISA."),
            Keep($"{idPrefix}-DETAIL-VW", Detail, $"VW@{AnnotationRightPlan}", DimPositive("VW"), "M PGB Detail keeps VW when VW is positive."),
            Keep($"{idPrefix}-DETAIL-VRA", Detail, $"VRA@{AnnotationRightPlan}", DimPositive("VR"), "M PGB Detail keeps VRA when VR is positive."),

            Keep($"{idPrefix}-SECTION-STD-FL", Section, $"FL@{AnnotationStdFrontPlan}", isStd, "M PGB STD Section keeps FL."),
            Keep($"{idPrefix}-SECTION-STD-T", Section, $"T@{AnnotationStdFrontPlan}", isStd, "M PGB STD Section keeps T."),

            Keep($"{idPrefix}-SECTION-REV-FL", Section, $"FL@{AnnotationRevFrontPlan}", isRev, "M PGB REV Section keeps FL."),
            Keep($"{idPrefix}-SECTION-REV-T", Section, $"T@{AnnotationRevFrontPlan}", isRev, "M PGB REV Section keeps T.")
        };
    }

    protected IReadOnlyList<AnnotationKeepRule>
        BuildFgProductionCustomerRules(
            string idPrefix,
            bool includeProductionOnlyRules)
    {
        var isStd =
            ShankIs(
                MAnnotationShankTypes.Std);

        var isRev =
            ShankIs(
                MAnnotationShankTypes.Rev);

        var hasVbl =
            DimPositive(
                "VBL");

        var noVbl =
            Not(
                hasVbl);

        var hasRa2 =
            DimPositive(
                "RA2");

        var hasCbr =
            DimsPositive(
                "CBRL",
                "CBRD");

        var noCbr =
            Not(
                hasCbr);

        var froEqualsFr =
            TraitIs(
                MAnnotationTraitNames.FroEqualsFr,
                MAnnotationTraitValues.True);

        var froDiffersFromFr =
            Not(
                froEqualsFr);

        var isStdHole =
            FeedHoleIs(
                MAnnotationFeedHoleTypes.Standard);

        var isOvalHole =
            FeedHoleIs(
                MAnnotationFeedHoleTypes.Oval);

        var isSlot =
            FeedHoleIs(
                MAnnotationFeedHoleTypes.Slot);

        var isVgFoot =
            FootIs(
                MAnnotationFootOptions.Vg);

        var isGFoot =
            FootIs(
                MAnnotationFootOptions.G);

        var isCFoot =
            FootIn(
                MAnnotationFootOptions.C,
                MAnnotationFootOptions.CWithCbr);

        var rules =
            new List<AnnotationKeepRule>
            {
                Keep($"{idPrefix}-SIDE-STD-BA", Side, $"BA@{AnnotationStdFrontPlan}", All(isStd, noVbl), "M FG STD Side keeps BA when VBL is not positive."),
                Keep($"{idPrefix}-SIDE-STD-VBL", Side, $"VBL@{AnnotationStdFrontPlan}", All(isStd, hasVbl), "M FG STD Side keeps VBL when VBL is positive."),
                Keep($"{idPrefix}-SIDE-STD-BA-SLB", Side, $"BA_SLB@{AnnotationStdFrontPlan}", All(isStd, hasVbl), "M FG STD Side keeps BA_SLB when VBL is positive."),

                Keep($"{idPrefix}-SIDE-REV-BA", Side, $"BA@{AnnotationRevFrontPlan}", All(isRev, noVbl), "M FG REV Side keeps BA when VBL is not positive."),
                Keep($"{idPrefix}-SIDE-REV-VBL", Side, $"VBL@{AnnotationRevFrontPlan}", All(isRev, hasVbl), "M FG REV Side keeps VBL when VBL is positive."),
                Keep($"{idPrefix}-SIDE-REV-BA-SLB", Side, $"BA_SLB@{AnnotationRevFrontPlan}", All(isRev, hasVbl), "M FG REV Side keeps BA_SLB when VBL is positive."),

                Keep($"{idPrefix}-FRONT-TL", Front, $"TL@{AnnotationRightPlan}", Always(), "M FG Front always keeps TL."),
                Keep($"{idPrefix}-FRONT-VR", Front, $"VR@{AnnotationRightPlan}", DimPositive("VR"), "M FG Front keeps VR when VR is positive."),

                Keep($"{idPrefix}-TOP-TD", Top, $"TD@{AnnotationTopPlan}", Always(), "M FG Top always keeps TD."),
                Keep($"{idPrefix}-TOP-TDF-STD", Top, $"TDF_STD@{AnnotationTopPlan}", isStd, "M FG Top keeps TDF_STD for SW_STD."),
                Keep($"{idPrefix}-TOP-TDF-REV", Top, $"TDF_REV@{AnnotationTopPlan}", isRev, "M FG Top keeps TDF_REV for SW_180REV."),

                Keep($"{idPrefix}-DETAIL-W", Detail, $"W@{AnnotationRightPlan}", Always(), "M FG Detail keeps W."),
                Keep($"{idPrefix}-DETAIL-ISA", Detail, $"ISA@{AnnotationRightPlan}", Always(), "M FG Detail keeps ISA."),
                Keep($"{idPrefix}-DETAIL-VW", Detail, $"VW@{AnnotationRightPlan}", DimPositive("VW"), "M FG Detail keeps VW when VW is positive."),
                Keep($"{idPrefix}-DETAIL-VRA", Detail, $"VRA@{AnnotationRightPlan}", DimPositive("VR"), "M FG Detail keeps VRA when VR is positive."),

                Keep($"{idPrefix}-DETAIL-VG-B", Detail, $"B@{AnnotationRightPlan}", isVgFoot, "M FG Detail keeps B for VG."),
                Keep($"{idPrefix}-DETAIL-VG-GA", Detail, $"GA@{AnnotationRightPlan}", isVgFoot, "M FG Detail keeps GA for VG."),
                Keep($"{idPrefix}-DETAIL-VG-GD", Detail, $"GD@{AnnotationRightPlan}", isVgFoot, "M FG Detail keeps GD for VG."),

                Keep($"{idPrefix}-DETAIL-G-GD", Detail, $"GD_G@{AnnotationRightPlan}", isGFoot, "M FG Detail keeps GD_G for G."),
                Keep($"{idPrefix}-DETAIL-G-GO", Detail, $"GO@{AnnotationRightPlan}", isGFoot, "M FG Detail keeps GO for G."),

                Keep($"{idPrefix}-DETAIL-C-CL", Detail, $"CL@{AnnotationRightPlan}", isCFoot, "M FG Detail keeps CL for C/C-with-CBR."),
                Keep($"{idPrefix}-DETAIL-C-CD", Detail, $"CD_NOM@{AnnotationRightPlan}", isCFoot, "M FG Detail keeps CD_NOM for C/C-with-CBR."),

                Keep($"{idPrefix}-SECTION-STD-FL", Section, $"FL@{AnnotationStdFrontPlan}", isStd, "M FG STD Section keeps FL."),
                Keep($"{idPrefix}-SECTION-STD-T", Section, $"T@{AnnotationStdFrontPlan}", isStd, "M FG STD Section keeps T."),
                Keep($"{idPrefix}-SECTION-STD-RA", Section, $"RA@{AnnotationStdFrontPlan}", isStd, "M FG STD Section keeps RA."),
                Keep($"{idPrefix}-SECTION-STD-C", Section, $"C@{AnnotationStdFrontPlan}", isStd, "M FG STD Section keeps C."),
                Keep($"{idPrefix}-SECTION-STD-NA", Section, $"NA@{AnnotationStdFrontPlan}", isStd, "M FG STD Section keeps NA."),
                Keep($"{idPrefix}-SECTION-STD-FTA", Section, $"FTA@{AnnotationStdFrontPlan}", isStd, "M FG STD Section keeps FTA."),
                Keep($"{idPrefix}-SECTION-STD-ND", Section, $"ND@{AnnotationStdFrontPlan}", isStd, "M FG STD Section keeps ND."),
                Keep($"{idPrefix}-SECTION-STD-NR", Section, $"NR@{AnnotationStdFrontPlan}", isStd, "M FG STD Section keeps NR."),
                Keep($"{idPrefix}-SECTION-STD-RA2", Section, $"RA2@{AnnotationStdFrontPlan}", All(isStd, hasRa2), "M FG STD Section keeps RA2 when RA2 is positive."),
                Keep($"{idPrefix}-SECTION-STD-FNA", Section, $"FNA@{AnnotationStdFrontPlan}", isStd, "M FG STD Section keeps FNA."),
                Keep($"{idPrefix}-SECTION-STD-HA", Section, $"HA@{AnnotationStdFrontPlan}", isStd, "M FG STD Section keeps HA."),
                Keep($"{idPrefix}-SECTION-STD-H", Section, $"H@{AnnotationStdFrontPlan}", All(isStd, isStdHole), "M FG STD Section keeps H for a standard hole."),
                Keep($"{idPrefix}-SECTION-STD-HH", Section, $"HH@{AnnotationStdFrontPlan}", All(isStd, isOvalHole), "M FG STD Section keeps HH for an oval hole."),
                Keep($"{idPrefix}-SECTION-STD-ST", Section, $"ST@{AnnotationStdFrontPlan}", All(isStd, isSlot), "M FG STD Section keeps ST for a slot."),
                Keep($"{idPrefix}-SECTION-STD-FR", Section, $"FR@{AnnotationStdFrontPlan}", All(isStd, froDiffersFromFr), "M FG STD Section keeps FR only when FRO differs from FR."),
                Keep($"{idPrefix}-SECTION-STD-BR", Section, $"BR@{AnnotationStdFrontPlan}", All(isStd, noCbr), "M FG STD Section keeps BR only when CBRL/CBRD do not define CBR."),
                Keep($"{idPrefix}-SECTION-STD-FRO", Section, $"FRO@{AnnotationStdFrontPlan}", isStd, "M FG STD Section keeps FRO."),

                Keep($"{idPrefix}-SECTION-REV-FL", Section, $"FL@{AnnotationRevFrontPlan}", isRev, "M FG REV Section keeps FL."),
                Keep($"{idPrefix}-SECTION-REV-T", Section, $"T@{AnnotationRevFrontPlan}", isRev, "M FG REV Section keeps T."),
                Keep($"{idPrefix}-SECTION-REV-RA", Section, $"RA@{AnnotationRevFrontPlan}", isRev, "M FG REV Section keeps RA."),
                Keep($"{idPrefix}-SECTION-REV-RA2", Section, $"RA2@{AnnotationRevFrontPlan}", All(isRev, hasRa2), "M FG REV Section keeps RA2 when RA2 is positive."),
                Keep($"{idPrefix}-SECTION-REV-ND", Section, $"ND@{AnnotationRevFrontPlan}", isRev, "M FG REV Section keeps ND."),
                Keep($"{idPrefix}-SECTION-REV-NR", Section, $"NR@{AnnotationRevFrontPlan}", isRev, "M FG REV Section keeps NR."),
                Keep($"{idPrefix}-SECTION-REV-NA", Section, $"NA@{AnnotationRevFrontPlan}", isRev, "M FG REV Section keeps NA."),
                Keep($"{idPrefix}-SECTION-REV-C", Section, $"C@{AnnotationRevFrontPlan}", isRev, "M FG REV Section keeps C."),
                Keep($"{idPrefix}-SECTION-REV-FNA", Section, $"FNA@{AnnotationRevFrontPlan}", isRev, "M FG REV Section keeps FNA."),
                Keep($"{idPrefix}-SECTION-REV-HA", Section, $"HA@{AnnotationRevFrontPlan}", isRev, "M FG REV Section keeps HA."),
                Keep($"{idPrefix}-SECTION-REV-H", Section, $"H@{AnnotationRevFrontPlan}", All(isRev, isStdHole), "M FG REV Section keeps H for a standard hole."),
                Keep($"{idPrefix}-SECTION-REV-HH", Section, $"HH@{AnnotationRevFrontPlan}", All(isRev, isOvalHole), "M FG REV Section keeps HH for an oval hole."),
                Keep($"{idPrefix}-SECTION-REV-ST", Section, $"ST@{AnnotationRevFrontPlan}", All(isRev, isSlot), "M FG REV Section keeps ST for a slot."),
                Keep($"{idPrefix}-SECTION-REV-FR", Section, $"FR@{AnnotationRevFrontPlan}", All(isRev, froDiffersFromFr), "M FG REV Section keeps FR only when FRO differs from FR."),
                Keep($"{idPrefix}-SECTION-REV-BR", Section, $"BR@{AnnotationRevFrontPlan}", All(isRev, noCbr), "M FG REV Section keeps BR only when CBRL/CBRD do not define CBR."),
                Keep($"{idPrefix}-SECTION-REV-FRO", Section, $"FRO@{AnnotationRevFrontPlan}", isRev, "M FG REV Section keeps FRO.")
            };

        if (!includeProductionOnlyRules)
            return rules;

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-PROD-STD-BF",
                Section,
                $"BF@{AnnotationFrontPlan}",
                isStd,
                "M FG Production STD Section keeps BF."));

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-PROD-STD-F",
                Section,
                $"F@{AnnotationFrontPlan}",
                isStd,
                "M FG Production STD Section keeps F."));

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-PROD-STD-Y",
                Section,
                $"Y@{AnnotationFrontPlan}",
                isStd,
                "M FG Production STD Section keeps Y."));

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-PROD-STD-CBRL",
                Section,
                $"CBRL@{AnnotationFrontPlan}",
                isStd,
                "M FG Production STD Section keeps CBRL."));

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-PROD-STD-CBRA",
                Section,
                $"CBRA@{AnnotationFrontPlan}",
                isStd,
                "M FG Production STD Section keeps CBRA."));

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-PROD-REV-BF",
                Section,
                $"BF@{AnnotationRevPlan}",
                isRev,
                "M FG Production REV Section keeps BF."));

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-PROD-REV-F",
                Section,
                $"F@{AnnotationRevPlan}",
                isRev,
                "M FG Production REV Section keeps F."));

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-PROD-REV-Y",
                Section,
                $"Y@{AnnotationRevPlan}",
                isRev,
                "M FG Production REV Section keeps Y."));

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-PROD-REV-CBRL",
                Section,
                $"CBRL@{AnnotationRevPlan}",
                isRev,
                "M FG Production REV Section keeps CBRL."));

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-PROD-REV-CBRA",
                Section,
                $"CBRA@{AnnotationRevPlan}",
                isRev,
                "M FG Production REV Section keeps CBRA."));

        return rules;
    }

    protected static IReadOnlyList<AnnotationKeepRule>
        BuildEmptyOverlayRules()
        => Array.Empty<AnnotationKeepRule>();
}

public sealed class MFgProductionAnnotationRules : MAnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile =>
        AnnotationCleanupProfile.MFgProduction;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public MFgProductionAnnotationRules()
        => Rules =
            BuildFgProductionCustomerRules(
                "M-FG-PROD",
                includeProductionOnlyRules: true);
}

public sealed class MFgCustomerAnnotationRules : MAnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile =>
        AnnotationCleanupProfile.MFgCustomer;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public MFgCustomerAnnotationRules()
        => Rules =
            BuildFgProductionCustomerRules(
                "M-FG-CUST",
                includeProductionOnlyRules: false);
}

public sealed class MFgOverlayAnnotationRules : MAnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile =>
        AnnotationCleanupProfile.MFgOverlay;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; } =
        BuildEmptyOverlayRules();
}

public sealed class MPgbProductionAnnotationRules : MAnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile =>
        AnnotationCleanupProfile.MPgbProduction;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public MPgbProductionAnnotationRules()
        => Rules =
            BuildPgbProductionCustomerRules(
                "M-PGB-PROD");
}

public sealed class MPgbOverlayAnnotationRules : MAnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile =>
        AnnotationCleanupProfile.MPgbOverlay;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; } =
        BuildEmptyOverlayRules();
}