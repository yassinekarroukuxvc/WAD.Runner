using System;
using System.Collections.Generic;

using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Conditions.AnnotationConditions;
using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain.AnnotationView;

namespace WAD.Runner.DrawingAutomation.Wedges._4516.Annotations;

public abstract class _4516AnnotationRuleCatalogBase :
    AnnotationRuleCatalogBase
{
    private const string AnnotationTopPlan =
        "annotation_top_plan";

    private const string AnnotationRightPlan =
        "annotation_right_plan";

    private const string AnnotationFrontPlan =
        "annotation_front_plan";

    // ================================================================
    // PGB PRODUCTION / CUSTOMER
    // ================================================================

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
            // ========================================================
            // SIDE VIEW
            // ========================================================

            /*
             * D1@annotation_front_plan represents BA.
             */
            Keep(
                $"{idPrefix}-SIDE-BA",
                Side,
                $"D1@{AnnotationFrontPlan}",
                Always(),
                "4516 PGB Side keeps D1, which represents BA."),

            Keep(
                $"{idPrefix}-SIDE-VBL",
                Side,
                $"VBL@{AnnotationFrontPlan}",
                hasSlb,
                "4516 PGB Side keeps VBL when VBL and VBLR are positive."),

            Keep(
                $"{idPrefix}-SIDE-BA-VBL",
                Side,
                $"BA_VBL@{AnnotationFrontPlan}",
                hasSlb,
                "4516 PGB Side keeps BA_VBL when VBL and VBLR are positive."),

            // ========================================================
            // FRONT VIEW
            // ========================================================

            /*
             * D1@annotation_right_plan represents TL.
             *
             * This annotation must always remain in the Front view.
             */
            Keep(
                $"{idPrefix}-FRONT-TL",
                Front,
                $"D1@{AnnotationRightPlan}",
                Always(),
                "4516 PGB Front always keeps D1, which represents TL."),

            Keep(
                $"{idPrefix}-FRONT-VR",
                Front,
                $"VR@{AnnotationRightPlan}",
                DimPositive("VR"),
                "4516 PGB Front keeps VR when VR is positive."),

            // ========================================================
            // TOP VIEW
            // ========================================================

            Keep(
                $"{idPrefix}-TOP-TD",
                Top,
                $"TD@{AnnotationTopPlan}",
                Always(),
                "4516 PGB Top keeps TD."),

            Keep(
                $"{idPrefix}-TOP-TDF",
                Top,
                $"TDF@{AnnotationTopPlan}",
                Always(),
                "4516 PGB Top keeps TDF."),

            // ========================================================
            // DETAIL VIEW
            // ========================================================

            Keep(
                $"{idPrefix}-DETAIL-W",
                Detail,
                $"W@{AnnotationRightPlan}",
                Always(),
                "4516 PGB Detail keeps W."),

            Keep(
                $"{idPrefix}-DETAIL-ISA",
                Detail,
                $"ISA@{AnnotationRightPlan}",
                Always(),
                "4516 PGB Detail keeps ISA."),

            Keep(
                $"{idPrefix}-DETAIL-VW",
                Detail,
                $"VW@{AnnotationRightPlan}",
                DimPositive("VW"),
                "4516 PGB Detail keeps VW when VW is positive."),

            Keep(
                $"{idPrefix}-DETAIL-VRA",
                Detail,
                $"VRA@{AnnotationRightPlan}",
                DimPositive("VR"),
                "4516 PGB Detail keeps VRA when VR is positive."),

            // ========================================================
            // SECTION VIEW
            // ========================================================

            Keep(
                $"{idPrefix}-SECTION-FL",
                Section,
                $"FL@{AnnotationFrontPlan}",
                Always(),
                "4516 PGB Section keeps FL."),

            Keep(
                $"{idPrefix}-SECTION-T",
                Section,
                $"T@{AnnotationFrontPlan}",
                Always(),
                "4516 PGB Section keeps T.")
        };
    }

    // ================================================================
    // FG PRODUCTION / CUSTOMER
    // ================================================================

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
                _4516AnnotationFootOptions.Vg);

        var isGFoot =
            FootIs(
                _4516AnnotationFootOptions.G);

        /*
         * C and C with CBR use the same Detail-view annotations.
         */
        var isCFoot =
            FootIn(
                _4516AnnotationFootOptions.C,
                _4516AnnotationFootOptions.CWithCbr);

        var rules =
            new List<AnnotationKeepRule>
            {
                // ====================================================
                // SIDE VIEW
                // ====================================================

                /*
                 * D1@annotation_front_plan represents BA.
                 */
                Keep(
                    $"{idPrefix}-SIDE-BA",
                    Side,
                    $"D1@{AnnotationFrontPlan}",
                    Always(),
                    "4516 FG Side keeps D1, which represents BA."),

                Keep(
                    $"{idPrefix}-SIDE-VBL",
                    Side,
                    $"VBL@{AnnotationFrontPlan}",
                    hasSlb,
                    "4516 FG Side keeps VBL when VBL and VBLR are positive."),

                Keep(
                    $"{idPrefix}-SIDE-BA-VBL",
                    Side,
                    $"BA_VBL@{AnnotationFrontPlan}",
                    hasSlb,
                    "4516 FG Side keeps BA_VBL when VBL and VBLR are positive."),

                // ====================================================
                // FRONT VIEW
                // ====================================================

                /*
                 * D1@annotation_right_plan represents TL.
                 *
                 * This annotation must never be removed from
                 * the Front view.
                 */
                Keep(
                    $"{idPrefix}-FRONT-TL",
                    Front,
                    $"D1@{AnnotationRightPlan}",
                    Always(),
                    "4516 FG Front always keeps D1, which represents TL."),

                Keep(
                    $"{idPrefix}-FRONT-VR",
                    Front,
                    $"VR@{AnnotationRightPlan}",
                    DimPositive("VR"),
                    "4516 FG Front keeps VR when VR is positive."),

                // ====================================================
                // TOP VIEW
                // ====================================================

                Keep(
                    $"{idPrefix}-TOP-TD",
                    Top,
                    $"TD@{AnnotationTopPlan}",
                    Always(),
                    "4516 FG Top keeps TD."),

                Keep(
                    $"{idPrefix}-TOP-TDF",
                    Top,
                    $"TDF@{AnnotationTopPlan}",
                    Always(),
                    "4516 FG Top keeps TDF."),

                // ====================================================
                // DETAIL VIEW - COMMON
                // ====================================================

                Keep(
                    $"{idPrefix}-DETAIL-W",
                    Detail,
                    $"W@{AnnotationRightPlan}",
                    Always(),
                    "4516 FG Detail keeps W."),

                Keep(
                    $"{idPrefix}-DETAIL-ISA",
                    Detail,
                    $"ISA@{AnnotationRightPlan}",
                    Always(),
                    "4516 FG Detail keeps ISA."),

                Keep(
                    $"{idPrefix}-DETAIL-VW",
                    Detail,
                    $"VW@{AnnotationRightPlan}",
                    DimPositive("VW"),
                    "4516 FG Detail keeps VW when VW is positive."),

                Keep(
                    $"{idPrefix}-DETAIL-VRA",
                    Detail,
                    $"VRA@{AnnotationRightPlan}",
                    DimPositive("VR"),
                    "4516 FG Detail keeps VRA when VR is positive."),

                // ====================================================
                // DETAIL VIEW - VG FOOT
                // ====================================================

                Keep(
                    $"{idPrefix}-DETAIL-VG-B",
                    Detail,
                    $"B@{AnnotationRightPlan}",
                    isVgFoot,
                    "4516 FG Detail keeps B for the VG foot option."),

                Keep(
                    $"{idPrefix}-DETAIL-VG-GA",
                    Detail,
                    $"GA@{AnnotationRightPlan}",
                    isVgFoot,
                    "4516 FG Detail keeps GA for the VG foot option."),

                Keep(
                    $"{idPrefix}-DETAIL-VG-GD",
                    Detail,
                    $"GD@{AnnotationRightPlan}",
                    isVgFoot,
                    "4516 FG Detail keeps GD for the VG foot option."),

                // ====================================================
                // DETAIL VIEW - G FOOT
                // ====================================================

                Keep(
                    $"{idPrefix}-DETAIL-G-GD",
                    Detail,
                    $"GD_G@{AnnotationRightPlan}",
                    isGFoot,
                    "4516 FG Detail keeps GD_G for the G foot option."),

                Keep(
                    $"{idPrefix}-DETAIL-G-GO",
                    Detail,
                    $"GO@{AnnotationRightPlan}",
                    isGFoot,
                    "4516 FG Detail keeps GO for the G foot option."),

                // ====================================================
                // DETAIL VIEW - C / C WITH CBR
                // ====================================================

                Keep(
                    $"{idPrefix}-DETAIL-C-CL",
                    Detail,
                    $"CL@{AnnotationRightPlan}",
                    isCFoot,
                    "4516 FG Detail keeps CL for C and C-with-CBR foot options."),

                Keep(
                    $"{idPrefix}-DETAIL-C-CD",
                    Detail,
                    $"CD@{AnnotationRightPlan}",
                    isCFoot,
                    "4516 FG Detail keeps CD for C and C-with-CBR foot options."),

                // ====================================================
                // SECTION VIEW - PRODUCTION AND CUSTOMER
                // ====================================================

                Keep(
                    $"{idPrefix}-SECTION-FL",
                    Section,
                    $"FL@{AnnotationFrontPlan}",
                    Always(),
                    "4516 FG Section keeps FL."),

                Keep(
                    $"{idPrefix}-SECTION-T",
                    Section,
                    $"T@{AnnotationFrontPlan}",
                    Always(),
                    "4516 FG Section keeps T."),

                Keep(
                    $"{idPrefix}-SECTION-FNA",
                    Section,
                    $"FNA@{AnnotationFrontPlan}",
                    Always(),
                    "4516 FG Section keeps FNA."),

                Keep(
                    $"{idPrefix}-SECTION-HA",
                    Section,
                    $"HA@{AnnotationFrontPlan}",
                    Always(),
                    "4516 FG Section keeps HA."),

                Keep(
                    $"{idPrefix}-SECTION-H",
                    Section,
                    $"H@{AnnotationFrontPlan}",
                    Always(),
                    "4516 FG Section keeps H."),

                Keep(
                    $"{idPrefix}-SECTION-FR",
                    Section,
                    $"FR@{AnnotationFrontPlan}",
                    Always(),
                    "4516 FG Section keeps FR."),

                Keep(
                    $"{idPrefix}-SECTION-BR",
                    Section,
                    $"BR@{AnnotationFrontPlan}",
                    Always(),
                    "4516 FG Section keeps BR."),

                Keep(
                    $"{idPrefix}-SECTION-CGD",
                    Section,
                    $"CGD@{AnnotationFrontPlan}",
                    Always(),
                    "4516 FG Section keeps CGD."),

                Keep(
                    $"{idPrefix}-SECTION-G",
                    Section,
                    $"G@{AnnotationFrontPlan}",
                    Always(),
                    "4516 FG Section keeps G.")
            };

        if (!includeProductionOnlyRules)
            return rules;

        // ============================================================
        // FG PRODUCTION-ONLY SECTION ANNOTATIONS
        // ============================================================

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-C",
                Section,
                $"C@{AnnotationFrontPlan}",
                Always(),
                "4516 FG Production Section keeps C."));

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-NR",
                Section,
                $"NR@{AnnotationFrontPlan}",
                Always(),
                "4516 FG Production Section keeps NR."));

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-BF",
                Section,
                $"BF@{AnnotationFrontPlan}",
                Always(),
                "4516 FG Production Section keeps BF."));

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-F",
                Section,
                $"F@{AnnotationFrontPlan}",
                Always(),
                "4516 FG Production Section keeps F."));

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-Y",
                Section,
                $"Y@{AnnotationFrontPlan}",
                Always(),
                "4516 FG Production Section keeps Y."));

        rules.Add(
            Keep(
                $"{idPrefix}-SECTION-CGR",
                Section,
                $"CGR@{AnnotationFrontPlan}",
                Always(),
                "4516 FG Production Section keeps CGR."));

        return rules;
    }

    // ================================================================
    // OVERLAY
    // ================================================================

    /*
     * Overlay annotation rules are intentionally empty for now.
     */
    protected static IReadOnlyList<AnnotationKeepRule>
        BuildEmptyOverlayRules()
        => Array.Empty<AnnotationKeepRule>();
}

// ====================================================================
// FG PRODUCTION
// ====================================================================

public sealed class _4516FgProductionAnnotationRules :
    _4516AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile =>
        AnnotationCleanupProfile._4516FgProduction;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public _4516FgProductionAnnotationRules()
    {
        Rules =
            BuildFgProductionCustomerRules(
                "4516-FG-PROD",
                includeProductionOnlyRules: true);
    }
}

// ====================================================================
// FG CUSTOMER
// ====================================================================

public sealed class _4516FgCustomerAnnotationRules :
    _4516AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile =>
        AnnotationCleanupProfile._4516FgCustomer;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public _4516FgCustomerAnnotationRules()
    {
        Rules =
            BuildFgProductionCustomerRules(
                "4516-FG-CUST",
                includeProductionOnlyRules: false);
    }
}

// ====================================================================
// FG OVERLAY - EMPTY FOR NOW
// ====================================================================

public sealed class _4516FgOverlayAnnotationRules :
    _4516AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile =>
        AnnotationCleanupProfile._4516FgOverlay;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public _4516FgOverlayAnnotationRules()
    {
        Rules =
            BuildEmptyOverlayRules();
    }
}

// ====================================================================
// PGB PRODUCTION / CUSTOMER
// ====================================================================

public sealed class _4516PgbProductionAnnotationRules :
    _4516AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile =>
        AnnotationCleanupProfile._4516PgbProduction;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public _4516PgbProductionAnnotationRules()
    {
        Rules =
            BuildPgbProductionCustomerRules(
                "4516-PGB-PROD");
    }
}

// ====================================================================
// PGB OVERLAY - EMPTY FOR NOW
// ====================================================================

public sealed class _4516PgbOverlayAnnotationRules :
    _4516AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile =>
        AnnotationCleanupProfile._4516PgbOverlay;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public _4516PgbOverlayAnnotationRules()
    {
        Rules =
            BuildEmptyOverlayRules();
    }
}