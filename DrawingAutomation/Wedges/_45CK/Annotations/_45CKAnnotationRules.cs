using System;
using System.Collections.Generic;

using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Conditions.AnnotationConditions;
using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain.AnnotationView;

namespace WAD.Runner.DrawingAutomation.Wedges._45CK.Annotations;

public abstract class _45CKAnnotationRuleCatalogBase :
    AnnotationRuleCatalogBase
{
    private const string AnnotationTopPlan =
        "annotation_top_plan";

    private const string AnnotationRightPlan =
        "annotation_right_plan";

    private const string AnnotationFrontPlan =
        "annotation_front_plan";

    protected IReadOnlyList<AnnotationKeepRule>
        BuildPgbProductionCustomerRules(
            string idPrefix)
    {
        var hasSlb =
            DimsPositive(
                "VBL",
                "VBLR");

        return new List<AnnotationKeepRule>
        {
            Keep(
                $"{idPrefix}-SIDE-BA",
                Side,
                $"BA@{AnnotationFrontPlan}",
                Always(),
                "_45CK PGB Side always keeps BA."),

            Keep(
                $"{idPrefix}-SIDE-BA-SLB",
                Side,
                $"BA_SLB@{AnnotationFrontPlan}",
                hasSlb,
                "_45CK PGB Side keeps BA_SLB when the SLB dimensions are positive."),

            Keep(
                $"{idPrefix}-FRONT-TL",
                Front,
                $"TL@{AnnotationRightPlan}",
                Always(),
                "_45CK PGB Front always keeps TL."),

            Keep(
                $"{idPrefix}-FRONT-VR",
                Front,
                $"VR@{AnnotationRightPlan}",
                DimPositive("VR"),
                "_45CK PGB Front keeps VR when VR is positive."),

            Keep(
                $"{idPrefix}-TOP-TD",
                Top,
                $"TD@{AnnotationTopPlan}",
                Always(),
                "_45CK PGB Top always keeps TD."),

            Keep(
                $"{idPrefix}-TOP-TDF",
                Top,
                $"TDF@{AnnotationTopPlan}",
                Always(),
                "_45CK PGB Top always keeps TDF."),

            Keep(
                $"{idPrefix}-DETAIL-W",
                Detail,
                $"W@{AnnotationRightPlan}",
                Always(),
                "_45CK PGB Detail always keeps W."),

            Keep(
                $"{idPrefix}-DETAIL-ISA",
                Detail,
                $"ISA@{AnnotationRightPlan}",
                Always(),
                "_45CK PGB Detail always keeps ISA."),

            Keep(
                $"{idPrefix}-DETAIL-VW",
                Detail,
                $"VW@{AnnotationRightPlan}",
                DimPositive("VW"),
                "_45CK PGB Detail keeps VW when VW is positive."),

            Keep(
                $"{idPrefix}-DETAIL-W2",
                Detail,
                $"W2@{AnnotationRightPlan}",
                DimPositive("W2"),
                "_45CK PGB Detail keeps W2 when W2 is positive."),

            Keep(
                $"{idPrefix}-DETAIL-VRA",
                Detail,
                $"VRA@{AnnotationRightPlan}",
                DimPositive("VRA"),
                "_45CK PGB Detail keeps VRA when VRA is positive."),

            Keep(
                $"{idPrefix}-SECTION-FL",
                Section,
                $"FL@{AnnotationFrontPlan}",
                Always(),
                "_45CK PGB Section always keeps FL."),

            Keep(
                $"{idPrefix}-SECTION-T",
                Section,
                $"T@{AnnotationFrontPlan}",
                Always(),
                "_45CK PGB Section always keeps T.")
        };
    }

    protected IReadOnlyList<AnnotationKeepRule>
        BuildFgProductionCustomerRules(
            string idPrefix,
            bool includeProductionOnlyRules)
    {
        var hasSlb =
            DimsPositive(
                "VBL",
                "VBLR");

        var isVgFoot =
            FootIs(
                _45CKAnnotationFootOptions.Vg);

        var isCgFoot =
            FootIs(
                _45CKAnnotationFootOptions.Cg);

        var isOvalHole =
            FeedHoleIs(
                _45CKAnnotationFeedHoleTypes.Oval);

        var isSlotHole =
            FeedHoleIs(
                _45CKAnnotationFeedHoleTypes.Slot);

        var rules =
            new List<AnnotationKeepRule>
            {
                Keep(
                    $"{idPrefix}-SIDE-BA",
                    Side,
                    $"BA@{AnnotationFrontPlan}",
                    Always(),
                    "_45CK FG Side always keeps BA."),

                Keep(
                    $"{idPrefix}-SIDE-VBL",
                    Side,
                    $"VBL@{AnnotationFrontPlan}",
                    hasSlb,
                    "_45CK FG Side keeps VBL when SLB is active."),

                Keep(
                    $"{idPrefix}-SIDE-BA-VBL",
                    Side,
                    $"BA_VBL@{AnnotationFrontPlan}",
                    hasSlb,
                    "_45CK FG Side keeps BA_VBL when SLB is active."),

                Keep(
                    $"{idPrefix}-FRONT-TL",
                    Front,
                    $"TL@{AnnotationRightPlan}",
                    Always(),
                    "_45CK FG Front always keeps TL."),

                Keep(
                    $"{idPrefix}-FRONT-VR",
                    Front,
                    $"VR@{AnnotationRightPlan}",
                    DimPositive("VR"),
                    "_45CK FG Front keeps VR when VR is positive."),

                Keep(
                    $"{idPrefix}-TOP-TD",
                    Top,
                    $"TD@{AnnotationTopPlan}",
                    Always(),
                    "_45CK FG Top always keeps TD."),

                Keep(
                    $"{idPrefix}-TOP-TDF",
                    Top,
                    $"TDF@{AnnotationTopPlan}",
                    Always(),
                    "_45CK FG Top always keeps TDF."),

                Keep(
                    $"{idPrefix}-DETAIL-W",
                    Detail,
                    $"W@{AnnotationRightPlan}",
                    Always(),
                    "_45CK FG Detail always keeps W."),

                Keep(
                    $"{idPrefix}-DETAIL-ISA",
                    Detail,
                    $"ISA@{AnnotationRightPlan}",
                    Always(),
                    "_45CK FG Detail always keeps ISA."),

                Keep(
                    $"{idPrefix}-DETAIL-VW",
                    Detail,
                    $"VW@{AnnotationRightPlan}",
                    DimPositive("VW"),
                    "_45CK FG Detail keeps VW when VW is positive."),

                Keep(
                    $"{idPrefix}-DETAIL-W2",
                    Detail,
                    $"W2@{AnnotationRightPlan}",
                    DimPositive("W2"),
                    "_45CK FG Detail keeps W2 when W2 is positive."),

                Keep(
                    $"{idPrefix}-DETAIL-VRA",
                    Detail,
                    $"VRA@{AnnotationRightPlan}",
                    DimPositive("VRA"),
                    "_45CK FG Detail keeps VRA when VRA is positive."),

                Keep(
                    $"{idPrefix}-DETAIL-VG-B",
                    Detail,
                    $"B@{AnnotationRightPlan}",
                    isVgFoot,
                    "_45CK FG Detail keeps B for VG."),

                Keep(
                    $"{idPrefix}-DETAIL-VG-GA",
                    Detail,
                    $"GA@{AnnotationRightPlan}",
                    isVgFoot,
                    "_45CK FG Detail keeps GA for VG."),

                Keep(
                    $"{idPrefix}-DETAIL-VG-GD",
                    Detail,
                    $"GD@{AnnotationRightPlan}",
                    isVgFoot,
                    "_45CK FG Detail keeps GD for VG."),

                Keep(
                    $"{idPrefix}-SECTION-FL",
                    Section,
                    $"FL@{AnnotationFrontPlan}",
                    Always(),
                    "_45CK FG Section always keeps FL."),

                Keep(
                    $"{idPrefix}-SECTION-T",
                    Section,
                    $"T@{AnnotationFrontPlan}",
                    Always(),
                    "_45CK FG Section always keeps T."),

                Keep(
                    $"{idPrefix}-SECTION-FNA",
                    Section,
                    $"FNA@{AnnotationFrontPlan}",
                    Always(),
                    "_45CK FG Section always keeps FNA."),

                Keep(
                    $"{idPrefix}-SECTION-HA",
                    Section,
                    $"HA@{AnnotationFrontPlan}",
                    Always(),
                    "_45CK FG Section always keeps HA."),

                Keep(
                    $"{idPrefix}-SECTION-H",
                    Section,
                    $"H@{AnnotationFrontPlan}",
                    Always(),
                    "_45CK FG Section always keeps H."),

                Keep(
                    $"{idPrefix}-SECTION-HH",
                    Section,
                    $"HH@{AnnotationFrontPlan}",
                    isOvalHole,
                    "_45CK FG Section keeps HH for Oval."),

                Keep(
                    $"{idPrefix}-SECTION-ST",
                    Section,
                    $"ST@{AnnotationFrontPlan}",
                    isSlotHole,
                    "_45CK FG Section keeps ST for Slot."),

                Keep(
                    $"{idPrefix}-SECTION-FR",
                    Section,
                    $"FR@{AnnotationFrontPlan}",
                    Always(),
                    "_45CK FG Section always keeps FR."),

                Keep(
                    $"{idPrefix}-SECTION-BR",
                    Section,
                    $"BR@{AnnotationFrontPlan}",
                    Always(),
                    "_45CK FG Section always keeps BR."),

                Keep(
                    $"{idPrefix}-SECTION-CG-CGD",
                    Section,
                    $"CGD@{AnnotationFrontPlan}",
                    isCgFoot,
                    "_45CK FG Section keeps CGD for CG."),

                Keep(
                    $"{idPrefix}-SECTION-CG-G",
                    Section,
                    $"G@{AnnotationFrontPlan}",
                    isCgFoot,
                    "_45CK FG Section keeps G for CG.")
            };

        if (!includeProductionOnlyRules)
            return rules;

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-PROD-BF",
                Section,
                $"BF@{AnnotationFrontPlan}",
                Always(),
                "_45CK FG Production Section keeps BF."));

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-PROD-F",
                Section,
                $"F@{AnnotationFrontPlan}",
                Always(),
                "_45CK FG Production Section keeps F."));

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-PROD-Y",
                Section,
                $"Y@{AnnotationFrontPlan}",
                Always(),
                "_45CK FG Production Section keeps Y."));

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-PROD-CG-CGR",
                Section,
                $"CGR@{AnnotationFrontPlan}",
                isCgFoot,
                "_45CK FG Production Section keeps CGR for CG."));

        return rules;
    }

    protected static IReadOnlyList<AnnotationKeepRule>
        BuildEmptyOverlayRules()
        => Array.Empty<AnnotationKeepRule>();
}

public sealed class _45CKFgProductionAnnotationRules :
    _45CKAnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile =>
        AnnotationCleanupProfile._45CKFgProduction;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public _45CKFgProductionAnnotationRules()
        => Rules =
            BuildFgProductionCustomerRules(
                "_45CK-FG-PROD",
                includeProductionOnlyRules: true);
}

public sealed class _45CKFgCustomerAnnotationRules :
    _45CKAnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile =>
        AnnotationCleanupProfile._45CKFgCustomer;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public _45CKFgCustomerAnnotationRules()
        => Rules =
            BuildFgProductionCustomerRules(
                "_45CK-FG-CUST",
                includeProductionOnlyRules: false);
}

public sealed class _45CKFgOverlayAnnotationRules :
    _45CKAnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile =>
        AnnotationCleanupProfile._45CKFgOverlay;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; } =
        BuildEmptyOverlayRules();
}

public sealed class _45CKPgbProductionAnnotationRules :
    _45CKAnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile =>
        AnnotationCleanupProfile._45CKPgbProduction;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public _45CKPgbProductionAnnotationRules()
        => Rules =
            BuildPgbProductionCustomerRules(
                "_45CK-PGB-PROD");
}

public sealed class _45CKPgbOverlayAnnotationRules :
    _45CKAnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile =>
        AnnotationCleanupProfile._45CKPgbOverlay;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; } =
        BuildEmptyOverlayRules();
}