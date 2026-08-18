using System;
using System.Collections.Generic;

using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Conditions.AnnotationConditions;
using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain.AnnotationView;

namespace WAD.Runner.DrawingAutomation.Wedges.AB16.Annotations;

public abstract class Ab16AnnotationRuleCatalogBase : AnnotationRuleCatalogBase
{
    private const string AnnotationTopPlan = "annotation_top_plan";
    private const string AnnotationRightPlan = "annotation_right_plan";
    private const string AnnotationFrontPlan = "annotation_front_plan";

    protected IReadOnlyList<AnnotationKeepRule> BuildPgbProductionCustomerRules(string idPrefix)
    {
        var hasSlb = DimsPositive("VBL", "VBLR");

        return new List<AnnotationKeepRule>
        {
            Keep($"{idPrefix}-SIDE-BA", Side, $"BA@{AnnotationFrontPlan}", Always(), "AB16 PGB Side always keeps BA."),
            Keep($"{idPrefix}-SIDE-BA-SLB", Side, $"BA_SLB@{AnnotationFrontPlan}", hasSlb, "AB16 PGB Side keeps BA_SLB when the SLB dimensions are positive."),

            Keep($"{idPrefix}-FRONT-TL", Front, $"TL@{AnnotationRightPlan}", Always(), "AB16 PGB Front always keeps TL."),
            Keep($"{idPrefix}-FRONT-VR", Front, $"VR@{AnnotationRightPlan}", DimPositive("VR"), "AB16 PGB Front keeps VR when VR is positive."),

            Keep($"{idPrefix}-TOP-TD", Top, $"TD@{AnnotationTopPlan}", Always(), "AB16 PGB Top keeps TD."),
            Keep($"{idPrefix}-TOP-TDF", Top, $"TDF@{AnnotationTopPlan}", Always(), "AB16 PGB Top keeps TDF."),

            Keep($"{idPrefix}-DETAIL-W", Detail, $"W@{AnnotationRightPlan}", Always(), "AB16 PGB Detail keeps W."),
            Keep($"{idPrefix}-DETAIL-ISA", Detail, $"ISA@{AnnotationRightPlan}", Always(), "AB16 PGB Detail keeps ISA."),
            Keep($"{idPrefix}-DETAIL-VW", Detail, $"VW@{AnnotationRightPlan}", DimPositive("VW"), "AB16 PGB Detail keeps VW when VW is positive."),
            Keep($"{idPrefix}-DETAIL-W2", Detail, $"W2@{AnnotationRightPlan}", DimPositive("W2"), "AB16 PGB Detail keeps W2 when W2 is positive."),
            Keep($"{idPrefix}-DETAIL-VRA", Detail, $"VRA@{AnnotationRightPlan}", DimPositive("VRA"), "AB16 PGB Detail keeps VRA when VRA is positive."),

            Keep($"{idPrefix}-SECTION-FL", Section, $"FL@{AnnotationFrontPlan}", Always(), "AB16 PGB Section keeps FL."),
            Keep($"{idPrefix}-SECTION-T", Section, $"T@{AnnotationFrontPlan}", Always(), "AB16 PGB Section keeps T.")
        };
    }

    protected IReadOnlyList<AnnotationKeepRule> BuildFgProductionCustomerRules(
        string idPrefix,
        bool includeProductionOnlyRules)
    {
        var hasSlb = DimsPositive("VBL", "VBLR");
        var isVgFoot = FootIs(Ab16AnnotationFootOptions.Vg);
        var isCgFoot = FootIs(Ab16AnnotationFootOptions.Cg);
        var isOvalHole = FeedHoleIs(Ab16AnnotationFeedHoleTypes.Oval);
        var isSlotHole = FeedHoleIs(Ab16AnnotationFeedHoleTypes.Slot);

        var rules = new List<AnnotationKeepRule>
        {
            Keep($"{idPrefix}-SIDE-BA", Side, $"BA@{AnnotationFrontPlan}", Always(), "AB16 FG Side always keeps BA."),
            Keep($"{idPrefix}-SIDE-VBL", Side, $"VBL@{AnnotationFrontPlan}", hasSlb, "AB16 FG Side keeps VBL when the SLB dimensions are positive."),
            Keep($"{idPrefix}-SIDE-BA-VBL", Side, $"BA_VBL@{AnnotationFrontPlan}", hasSlb, "AB16 FG Side keeps BA_VBL when the SLB dimensions are positive."),

            Keep($"{idPrefix}-FRONT-TL", Front, $"TL@{AnnotationRightPlan}", Always(), "AB16 FG Front always keeps TL."),
            Keep($"{idPrefix}-FRONT-VR", Front, $"VR@{AnnotationRightPlan}", DimPositive("VR"), "AB16 FG Front keeps VR when VR is positive."),

            Keep($"{idPrefix}-TOP-TD", Top, $"TD@{AnnotationTopPlan}", Always(), "AB16 FG Top keeps TD."),
            Keep($"{idPrefix}-TOP-TDF", Top, $"TDF@{AnnotationTopPlan}", Always(), "AB16 FG Top keeps TDF."),

            Keep($"{idPrefix}-DETAIL-W", Detail, $"W@{AnnotationRightPlan}", Always(), "AB16 FG Detail keeps W."),
            Keep($"{idPrefix}-DETAIL-ISA", Detail, $"ISA@{AnnotationRightPlan}", Always(), "AB16 FG Detail keeps ISA."),
            Keep($"{idPrefix}-DETAIL-VW", Detail, $"VW@{AnnotationRightPlan}", DimPositive("VW"), "AB16 FG Detail keeps VW when VW is positive."),
            Keep($"{idPrefix}-DETAIL-W2", Detail, $"W2@{AnnotationRightPlan}", DimPositive("W2"), "AB16 FG Detail keeps W2 when W2 is positive."),
            Keep($"{idPrefix}-DETAIL-VRA", Detail, $"VRA@{AnnotationRightPlan}", DimPositive("VRA"), "AB16 FG Detail keeps VRA when VRA is positive."),
            Keep($"{idPrefix}-DETAIL-VG-B", Detail, $"B@{AnnotationRightPlan}", isVgFoot, "AB16 FG Detail keeps B for VG."),
            Keep($"{idPrefix}-DETAIL-VG-GA", Detail, $"GA@{AnnotationRightPlan}", isVgFoot, "AB16 FG Detail keeps GA for VG."),
            Keep($"{idPrefix}-DETAIL-VG-GD", Detail, $"GD@{AnnotationRightPlan}", isVgFoot, "AB16 FG Detail keeps GD for VG."),

            Keep($"{idPrefix}-SECTION-FL", Section, $"FL@{AnnotationFrontPlan}", Always(), "AB16 FG Section keeps FL."),
            Keep($"{idPrefix}-SECTION-T", Section, $"T@{AnnotationFrontPlan}", Always(), "AB16 FG Section keeps T."),
            Keep($"{idPrefix}-SECTION-FNA", Section, $"FNA@{AnnotationFrontPlan}", Always(), "AB16 FG Section keeps FNA."),
            Keep($"{idPrefix}-SECTION-HA", Section, $"HA@{AnnotationFrontPlan}", Always(), "AB16 FG Section keeps HA."),
            Keep($"{idPrefix}-SECTION-H", Section, $"H@{AnnotationFrontPlan}", Always(), "AB16 FG Section keeps H."),
            Keep($"{idPrefix}-SECTION-HH", Section, $"HH@{AnnotationFrontPlan}", isOvalHole, "AB16 FG Section keeps HH for an oval hole."),
            Keep($"{idPrefix}-SECTION-ST", Section, $"ST@{AnnotationFrontPlan}", isSlotHole, "AB16 FG Section keeps ST for a slot hole."),
            Keep($"{idPrefix}-SECTION-FR", Section, $"FR@{AnnotationFrontPlan}", Always(), "AB16 FG Section keeps FR."),
            Keep($"{idPrefix}-SECTION-BR", Section, $"BR@{AnnotationFrontPlan}", Always(), "AB16 FG Section keeps BR."),
            Keep($"{idPrefix}-SECTION-CG-CGD", Section, $"CGD@{AnnotationFrontPlan}", isCgFoot, "AB16 FG Section keeps CGD for CG."),
            Keep($"{idPrefix}-SECTION-CG-G", Section, $"G@{AnnotationFrontPlan}", isCgFoot, "AB16 FG Section keeps G for CG.")
        };

        if (!includeProductionOnlyRules)
            return rules;

        rules.Add(Keep($"{idPrefix}-SECTION-PROD-BF", Section, $"BF@{AnnotationFrontPlan}", Always(), "AB16 FG Production Section keeps BF."));
        rules.Add(Keep($"{idPrefix}-SECTION-PROD-F", Section, $"F@{AnnotationFrontPlan}", Always(), "AB16 FG Production Section keeps F."));
        rules.Add(Keep($"{idPrefix}-SECTION-PROD-Y", Section, $"Y@{AnnotationFrontPlan}", Always(), "AB16 FG Production Section keeps Y."));
        rules.Add(Keep($"{idPrefix}-SECTION-PROD-CG-CGR", Section, $"CGR@{AnnotationFrontPlan}", isCgFoot, "AB16 FG Production Section keeps CGR for CG."));

        return rules;
    }

    protected static IReadOnlyList<AnnotationKeepRule> BuildEmptyOverlayRules()
        => Array.Empty<AnnotationKeepRule>();
}

public sealed class Ab16FgProductionAnnotationRules : Ab16AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.Ab16FgProduction;
    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public Ab16FgProductionAnnotationRules()
        => Rules = BuildFgProductionCustomerRules("AB16-FG-PROD", includeProductionOnlyRules: true);
}

public sealed class Ab16FgCustomerAnnotationRules : Ab16AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.Ab16FgCustomer;
    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public Ab16FgCustomerAnnotationRules()
        => Rules = BuildFgProductionCustomerRules("AB16-FG-CUST", includeProductionOnlyRules: false);
}

public sealed class Ab16FgOverlayAnnotationRules : Ab16AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.Ab16FgOverlay;
    public override IReadOnlyList<AnnotationKeepRule> Rules { get; } = BuildEmptyOverlayRules();
}

public sealed class Ab16PgbProductionAnnotationRules : Ab16AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.Ab16PgbProduction;
    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public Ab16PgbProductionAnnotationRules()
        => Rules = BuildPgbProductionCustomerRules("AB16-PGB-PROD");
}

public sealed class Ab16PgbOverlayAnnotationRules : Ab16AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.Ab16PgbOverlay;
    public override IReadOnlyList<AnnotationKeepRule> Rules { get; } = BuildEmptyOverlayRules();
}