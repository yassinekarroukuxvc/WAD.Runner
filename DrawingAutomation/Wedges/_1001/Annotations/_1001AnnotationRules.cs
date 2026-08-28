using System;
using System.Collections.Generic;

using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Conditions.AnnotationConditions;
using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain.AnnotationView;

namespace WAD.Runner.DrawingAutomation.Wedges._1001.Annotations;

public abstract class _1001AnnotationRuleCatalogBase : AnnotationRuleCatalogBase
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
                _1001AnnotationShankTypes.Std);

        var isRev =
            ShankIs(
                _1001AnnotationShankTypes.Rev);

        var hasVbl =
            DimPositive(
                "VBL");

        var noVbl =
            Not(
                hasVbl);

        return new List<AnnotationKeepRule>
        {
            Keep($"{idPrefix}-SIDE-STD-BA", Side, $"BA@{AnnotationStdFrontPlan}", All(isStd, noVbl), "_1001 PGB STD Side keeps BA when VBL is not positive."),
            Keep($"{idPrefix}-SIDE-STD-VBL", Side, $"VBL@{AnnotationStdFrontPlan}", All(isStd, hasVbl), "_1001 PGB STD Side keeps VBL when VBL is positive."),
            Keep($"{idPrefix}-SIDE-STD-BA-SLB", Side, $"BA_SLB@{AnnotationStdFrontPlan}", All(isStd, hasVbl), "_1001 PGB STD Side keeps BA_SLB when VBL is positive."),

            Keep($"{idPrefix}-SIDE-REV-BA", Side, $"BA@{AnnotationRevFrontPlan}", All(isRev, noVbl), "_1001 PGB REV Side keeps BA when VBL is not positive."),
            Keep($"{idPrefix}-SIDE-REV-VBL", Side, $"VBL@{AnnotationRevFrontPlan}", All(isRev, hasVbl), "_1001 PGB REV Side keeps VBL when VBL is positive."),
            Keep($"{idPrefix}-SIDE-REV-BA-SLB", Side, $"BA_SLB@{AnnotationRevFrontPlan}", All(isRev, hasVbl), "_1001 PGB REV Side keeps BA_SLB when VBL is positive."),

            Keep($"{idPrefix}-FRONT-TL", Front, $"TL@{AnnotationRightPlan}", Always(), "_1001 PGB Front always keeps TL."),
            Keep($"{idPrefix}-FRONT-VR", Front, $"VR@{AnnotationRightPlan}", DimPositive("VR"), "_1001 PGB Front keeps VR when VR is positive."),

            Keep($"{idPrefix}-TOP-TD", Top, $"TD@{AnnotationTopPlan}", Always(), "_1001 PGB Top always keeps TD."),
            Keep($"{idPrefix}-TOP-TDF-STD", Top, $"TDF_STD@{AnnotationTopPlan}", isStd, "_1001 PGB Top keeps TDF_STD for SW_STD."),
            Keep($"{idPrefix}-TOP-TDF-REV", Top, $"TDF_REV@{AnnotationTopPlan}", isRev, "_1001 PGB Top keeps TDF_REV for SW_180REV."),

            Keep($"{idPrefix}-DETAIL-W", Detail, $"W_NOM@{AnnotationRightPlan}", Always(), "_1001 PGB Detail keeps W_NOM."),
            Keep($"{idPrefix}-DETAIL-ISA", Detail, $"ISA@{AnnotationRightPlan}", Always(), "_1001 PGB Detail keeps ISA."),
            Keep($"{idPrefix}-DETAIL-VW", Detail, $"VW@{AnnotationRightPlan}", DimPositive("VW"), "_1001 PGB Detail keeps VW when VW is positive."),
            Keep($"{idPrefix}-DETAIL-VRA", Detail, $"VRA@{AnnotationRightPlan}", DimPositive("VR"), "_1001 PGB Detail keeps VRA when VR is positive."),

            Keep($"{idPrefix}-SECTION-STD-FL", Section, $"FL@{AnnotationStdFrontPlan}", isStd, "_1001 PGB STD Section keeps FL."),
            Keep($"{idPrefix}-SECTION-STD-T", Section, $"T@{AnnotationStdFrontPlan}", isStd, "_1001 PGB STD Section keeps T."),

            Keep($"{idPrefix}-SECTION-REV-FL", Section, $"FL@{AnnotationRevFrontPlan}", isRev, "_1001 PGB REV Section keeps FL."),
            Keep($"{idPrefix}-SECTION-REV-T", Section, $"T@{AnnotationRevFrontPlan}", isRev, "_1001 PGB REV Section keeps T.")
        };
    }

    protected IReadOnlyList<AnnotationKeepRule>
        BuildFgProductionCustomerRules(
            string idPrefix,
            bool includeProductionOnlyRules)
    {
        var isStd =
            ShankIs(
                _1001AnnotationShankTypes.Std);

        var isRev =
            ShankIs(
                _1001AnnotationShankTypes.Rev);

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
                _1001AnnotationTraitNames.FroEqualsFr,
                _1001AnnotationTraitValues.True);

        var froDiffersFromFr =
            Not(
                froEqualsFr);

        var isStdHole =
            FeedHoleIs(
                _1001AnnotationFeedHoleTypes.Standard);

        var isOvalHole =
            FeedHoleIs(
                _1001AnnotationFeedHoleTypes.Oval);

        var isSlot =
            FeedHoleIs(
                _1001AnnotationFeedHoleTypes.Slot);

        var isVgFoot =
            FootIs(
                _1001AnnotationFootOptions.Vg);

        var isGFoot =
            FootIs(
                _1001AnnotationFootOptions.G);

        var isCFoot =
            FootIn(
                _1001AnnotationFootOptions.C,
                _1001AnnotationFootOptions.CWithCbr);

        var rules =
            new List<AnnotationKeepRule>
            {
                Keep($"{idPrefix}-SIDE-STD-BA", Side, $"BA@{AnnotationStdFrontPlan}", All(isStd, noVbl), "_1001 FG STD Side keeps BA when VBL is not positive."),
                Keep($"{idPrefix}-SIDE-STD-VBL", Side, $"VBL@{AnnotationStdFrontPlan}", All(isStd, hasVbl), "_1001 FG STD Side keeps VBL when VBL is positive."),
                Keep($"{idPrefix}-SIDE-STD-BA-SLB", Side, $"BA_SLB@{AnnotationStdFrontPlan}", All(isStd, hasVbl), "_1001 FG STD Side keeps BA_SLB when VBL is positive."),

                Keep($"{idPrefix}-SIDE-REV-BA", Side, $"BA@{AnnotationRevFrontPlan}", All(isRev, noVbl), "_1001 FG REV Side keeps BA when VBL is not positive."),
                Keep($"{idPrefix}-SIDE-REV-VBL", Side, $"VBL@{AnnotationRevFrontPlan}", All(isRev, hasVbl), "_1001 FG REV Side keeps VBL when VBL is positive."),
                Keep($"{idPrefix}-SIDE-REV-BA-SLB", Side, $"BA_SLB@{AnnotationRevFrontPlan}", All(isRev, hasVbl), "_1001 FG REV Side keeps BA_SLB when VBL is positive."),

                Keep($"{idPrefix}-FRONT-TL", Front, $"TL@{AnnotationRightPlan}", Always(), "_1001 FG Front always keeps TL."),
                Keep($"{idPrefix}-FRONT-VR", Front, $"VR@{AnnotationRightPlan}", DimPositive("VR"), "_1001 FG Front keeps VR when VR is positive."),

                Keep($"{idPrefix}-TOP-TD", Top, $"TD@{AnnotationTopPlan}", Always(), "_1001 FG Top always keeps TD."),
                Keep($"{idPrefix}-TOP-TDF-STD", Top, $"TDF_STD@{AnnotationTopPlan}", isStd, "_1001 FG Top keeps TDF_STD for SW_STD."),
                Keep($"{idPrefix}-TOP-TDF-REV", Top, $"TDF_REV@{AnnotationTopPlan}", isRev, "_1001 FG Top keeps TDF_REV for SW_180REV."),

                Keep($"{idPrefix}-DETAIL-W", Detail, $"W@{AnnotationRightPlan}", Always(), "_1001 FG Detail keeps W."),
                Keep($"{idPrefix}-DETAIL-ISA", Detail, $"ISA@{AnnotationRightPlan}", Always(), "_1001 FG Detail keeps ISA."),
                Keep($"{idPrefix}-DETAIL-VW", Detail, $"VW@{AnnotationRightPlan}", DimPositive("VW"), "_1001 FG Detail keeps VW when VW is positive."),
                Keep($"{idPrefix}-DETAIL-VRA", Detail, $"VRA@{AnnotationRightPlan}", DimPositive("VR"), "_1001 FG Detail keeps VRA when VR is positive."),

                Keep($"{idPrefix}-DETAIL-VG-B", Detail, $"B@{AnnotationRightPlan}", isVgFoot, "_1001 FG Detail keeps B for VG."),
                Keep($"{idPrefix}-DETAIL-VG-GA", Detail, $"GA@{AnnotationRightPlan}", isVgFoot, "_1001 FG Detail keeps GA for VG."),
                Keep($"{idPrefix}-DETAIL-VG-GD", Detail, $"GD@{AnnotationRightPlan}", isVgFoot, "_1001 FG Detail keeps GD for VG."),

                Keep($"{idPrefix}-DETAIL-G-GD", Detail, $"GD_G@{AnnotationRightPlan}", isGFoot, "_1001 FG Detail keeps GD_G for G."),
                Keep($"{idPrefix}-DETAIL-G-GO", Detail, $"GO@{AnnotationRightPlan}", isGFoot, "_1001 FG Detail keeps GO for G."),

                Keep($"{idPrefix}-DETAIL-C-CL", Detail, $"CL@{AnnotationRightPlan}", isCFoot, "_1001 FG Detail keeps CL for C/C-with-CBR."),
                Keep($"{idPrefix}-DETAIL-C-CD", Detail, $"CD_NOM@{AnnotationRightPlan}", isCFoot, "_1001 FG Detail keeps CD_NOM for C/C-with-CBR."),

                Keep($"{idPrefix}-SECTION-STD-FL", Section, $"FL@{AnnotationStdFrontPlan}", isStd, "_1001 FG STD Section keeps FL."),
                Keep($"{idPrefix}-SECTION-STD-T", Section, $"T@{AnnotationStdFrontPlan}", isStd, "_1001 FG STD Section keeps T."),
                Keep($"{idPrefix}-SECTION-STD-RA", Section, $"RA@{AnnotationStdFrontPlan}", isStd, "_1001 FG STD Section keeps RA."),
                Keep($"{idPrefix}-SECTION-STD-C", Section, $"C@{AnnotationStdFrontPlan}", isStd, "_1001 FG STD Section keeps C."),
                Keep($"{idPrefix}-SECTION-STD-NA", Section, $"NA@{AnnotationStdFrontPlan}", isStd, "_1001 FG STD Section keeps NA."),
                Keep($"{idPrefix}-SECTION-STD-FTA", Section, $"FTA@{AnnotationStdFrontPlan}", isStd, "_1001 FG STD Section keeps FTA."),
                Keep($"{idPrefix}-SECTION-STD-ND", Section, $"ND@{AnnotationStdFrontPlan}", isStd, "_1001 FG STD Section keeps ND."),
                Keep($"{idPrefix}-SECTION-STD-NR", Section, $"NR@{AnnotationStdFrontPlan}", isStd, "_1001 FG STD Section keeps NR."),
                Keep($"{idPrefix}-SECTION-STD-RA2", Section, $"RA2@{AnnotationStdFrontPlan}", All(isStd, hasRa2), "_1001 FG STD Section keeps RA2 when RA2 is positive."),
                Keep($"{idPrefix}-SECTION-STD-FNA", Section, $"FNA@{AnnotationStdFrontPlan}", isStd, "_1001 FG STD Section keeps FNA."),
                Keep($"{idPrefix}-SECTION-STD-HA", Section, $"HA@{AnnotationStdFrontPlan}", isStd, "_1001 FG STD Section keeps HA."),
                Keep($"{idPrefix}-SECTION-STD-H", Section, $"H@{AnnotationStdFrontPlan}", All(isStd, isStdHole), "_1001 FG STD Section keeps H for a standard hole."),
                Keep($"{idPrefix}-SECTION-STD-HH", Section, $"HH@{AnnotationStdFrontPlan}", All(isStd, isOvalHole), "_1001 FG STD Section keeps HH for an oval hole."),
                Keep($"{idPrefix}-SECTION-STD-ST", Section, $"ST@{AnnotationStdFrontPlan}", All(isStd, isSlot), "_1001 FG STD Section keeps ST for a slot."),
                Keep($"{idPrefix}-SECTION-STD-FR", Section, $"FR@{AnnotationStdFrontPlan}", All(isStd, froDiffersFromFr), "_1001 FG STD Section keeps FR only when FRO differs from FR."),
                Keep($"{idPrefix}-SECTION-STD-BR", Section, $"BR@{AnnotationStdFrontPlan}", All(isStd, noCbr), "_1001 FG STD Section keeps BR only when CBRL/CBRD do not define CBR."),
                Keep($"{idPrefix}-SECTION-STD-FRO", Section, $"FRO@{AnnotationStdFrontPlan}", isStd, "_1001 FG STD Section keeps FRO."),

                Keep($"{idPrefix}-SECTION-REV-FL", Section, $"FL@{AnnotationRevFrontPlan}", isRev, "_1001 FG REV Section keeps FL."),
                Keep($"{idPrefix}-SECTION-REV-T", Section, $"T@{AnnotationRevFrontPlan}", isRev, "_1001 FG REV Section keeps T."),
                Keep($"{idPrefix}-SECTION-REV-RA", Section, $"RA@{AnnotationRevFrontPlan}", isRev, "_1001 FG REV Section keeps RA."),
                Keep($"{idPrefix}-SECTION-REV-RA2", Section, $"RA2@{AnnotationRevFrontPlan}", All(isRev, hasRa2), "_1001 FG REV Section keeps RA2 when RA2 is positive."),
                Keep($"{idPrefix}-SECTION-REV-ND", Section, $"ND@{AnnotationRevFrontPlan}", isRev, "_1001 FG REV Section keeps ND."),
                Keep($"{idPrefix}-SECTION-REV-NR", Section, $"NR@{AnnotationRevFrontPlan}", isRev, "_1001 FG REV Section keeps NR."),
                Keep($"{idPrefix}-SECTION-REV-NA", Section, $"NA@{AnnotationRevFrontPlan}", isRev, "_1001 FG REV Section keeps NA."),
                Keep($"{idPrefix}-SECTION-REV-C", Section, $"C@{AnnotationRevFrontPlan}", isRev, "_1001 FG REV Section keeps C."),
                Keep($"{idPrefix}-SECTION-REV-FNA", Section, $"FNA@{AnnotationRevFrontPlan}", isRev, "_1001 FG REV Section keeps FNA."),
                Keep($"{idPrefix}-SECTION-REV-HA", Section, $"HA@{AnnotationRevFrontPlan}", isRev, "_1001 FG REV Section keeps HA."),
                Keep($"{idPrefix}-SECTION-REV-H", Section, $"H@{AnnotationRevFrontPlan}", All(isRev, isStdHole), "_1001 FG REV Section keeps H for a standard hole."),
                Keep($"{idPrefix}-SECTION-REV-HH", Section, $"HH@{AnnotationRevFrontPlan}", All(isRev, isOvalHole), "_1001 FG REV Section keeps HH for an oval hole."),
                Keep($"{idPrefix}-SECTION-REV-ST", Section, $"ST@{AnnotationRevFrontPlan}", All(isRev, isSlot), "_1001 FG REV Section keeps ST for a slot."),
                Keep($"{idPrefix}-SECTION-REV-FR", Section, $"FR@{AnnotationRevFrontPlan}", All(isRev, froDiffersFromFr), "_1001 FG REV Section keeps FR only when FRO differs from FR."),
                Keep($"{idPrefix}-SECTION-REV-BR", Section, $"BR@{AnnotationRevFrontPlan}", All(isRev, noCbr), "_1001 FG REV Section keeps BR only when CBRL/CBRD do not define CBR."),
                Keep($"{idPrefix}-SECTION-REV-FRO", Section, $"FRO@{AnnotationRevFrontPlan}", isRev, "_1001 FG REV Section keeps FRO.")
            };

        if (!includeProductionOnlyRules)
            return rules;

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-PROD-STD-BF",
                Section,
                $"BF@{AnnotationFrontPlan}",
                isStd,
                "_1001 FG Production STD Section keeps BF."));

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-PROD-STD-F",
                Section,
                $"F@{AnnotationFrontPlan}",
                isStd,
                "_1001 FG Production STD Section keeps F."));

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-PROD-STD-Y",
                Section,
                $"Y@{AnnotationFrontPlan}",
                isStd,
                "_1001 FG Production STD Section keeps Y."));

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-PROD-STD-CBRL",
                Section,
                $"CBRL@{AnnotationFrontPlan}",
                isStd,
                "_1001 FG Production STD Section keeps CBRL."));

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-PROD-STD-CBRA",
                Section,
                $"CBRA@{AnnotationFrontPlan}",
                isStd,
                "_1001 FG Production STD Section keeps CBRA."));

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-PROD-REV-BF",
                Section,
                $"BF@{AnnotationRevPlan}",
                isRev,
                "_1001 FG Production REV Section keeps BF."));

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-PROD-REV-F",
                Section,
                $"F@{AnnotationRevPlan}",
                isRev,
                "_1001 FG Production REV Section keeps F."));

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-PROD-REV-Y",
                Section,
                $"Y@{AnnotationRevPlan}",
                isRev,
                "_1001 FG Production REV Section keeps Y."));

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-PROD-REV-CBRL",
                Section,
                $"CBRL@{AnnotationRevPlan}",
                isRev,
                "_1001 FG Production REV Section keeps CBRL."));

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-PROD-REV-CBRA",
                Section,
                $"CBRA@{AnnotationRevPlan}",
                isRev,
                "_1001 FG Production REV Section keeps CBRA."));

        return rules;
    }

    protected static IReadOnlyList<AnnotationKeepRule>
        BuildEmptyOverlayRules()
        => Array.Empty<AnnotationKeepRule>();
}

public sealed class _1001FgProductionAnnotationRules : _1001AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile =>
        AnnotationCleanupProfile._1001FgProduction;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public _1001FgProductionAnnotationRules()
        => Rules =
            BuildFgProductionCustomerRules(
                "_1001-FG-PROD",
                includeProductionOnlyRules: true);
}

public sealed class _1001FgCustomerAnnotationRules : _1001AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile =>
        AnnotationCleanupProfile._1001FgCustomer;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public _1001FgCustomerAnnotationRules()
        => Rules =
            BuildFgProductionCustomerRules(
                "_1001-FG-CUST",
                includeProductionOnlyRules: false);
}

public sealed class _1001FgOverlayAnnotationRules : _1001AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile =>
        AnnotationCleanupProfile._1001FgOverlay;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; } =
        BuildEmptyOverlayRules();
}

public sealed class _1001PgbProductionAnnotationRules : _1001AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile =>
        AnnotationCleanupProfile._1001PgbProduction;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public _1001PgbProductionAnnotationRules()
        => Rules =
            BuildPgbProductionCustomerRules(
                "_1001-PGB-PROD");
}

public sealed class _1001PgbOverlayAnnotationRules : _1001AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile =>
        AnnotationCleanupProfile._1001PgbOverlay;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; } =
        BuildEmptyOverlayRules();
}