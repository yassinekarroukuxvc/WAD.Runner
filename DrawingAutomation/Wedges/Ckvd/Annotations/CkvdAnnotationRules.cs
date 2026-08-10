using System.Collections.Generic;

using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Conditions.AnnotationConditions;
using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain.AnnotationView;

namespace WAD.Runner.DrawingAutomation.Wedges.Ckvd.Annotations;

public abstract class CkvdAnnotationRuleCatalogBase :
    AnnotationRuleCatalogBase
{
    private const string AnnotationTopPlan =
        "annotation_top_plan";

    private const string AnnotationRightPlan =
        "annotation_right_plan";

    private const string StyleAAnnotationFrontPlan =
        "style_a_annotation_front_plan";

    private const string StyleBAnnotationFrontPlan =
        "style_b_annotation_front_plan";

    /// <summary>
    /// Builds the CKVD Production/Customer annotation rules for FG.
    ///
    /// Important view correction from the designer screenshots:
    /// - The screenshot called "Right view" is the logical Front view.
    /// - The screenshot called "Front view" is the logical Side view.
    ///
    /// View positioning is deliberately not handled here.
    /// </summary>
    protected IReadOnlyList<AnnotationKeepRule>
        BuildFgProductionCustomerRules(string idPrefix)
    {
        var isStyleA = WedTypeIs(CkvdAnnotationStyles.StyleA);
        var isStyleB = WedTypeIs(CkvdAnnotationStyles.StyleB);

        return new List<AnnotationKeepRule>
        {
            // FRONT VIEW
            // Designer screenshot label: "Right view".
            Keep(
                $"{idPrefix}-FRONT-TL",
                Front,
                $"TL@{AnnotationRightPlan}",
                Always(),
                "CKVD FG Front keeps TL."),

            Keep(
                $"{idPrefix}-FRONT-VR",
                Front,
                $"VR@{AnnotationRightPlan}",
                DimPositive("VR"),
                "CKVD FG Front keeps optional VR when VR is positive."),

            // SIDE VIEW
            // STYLE A: BA and E only.
            Keep(
                $"{idPrefix}-SIDE-STYLE-A-BA",
                Side,
                $"BA@{StyleAAnnotationFrontPlan}",
                isStyleA,
                "CKVD FG Style A Side keeps BA."),

            Keep(
                $"{idPrefix}-SIDE-STYLE-A-E",
                Side,
                $"E@{StyleAAnnotationFrontPlan}",
                isStyleA,
                "CKVD FG Style A Side keeps E."),

            // STYLE B: FA, BA and E; X/FX only when supplied by DB.
            Keep(
                $"{idPrefix}-SIDE-STYLE-B-FA",
                Side,
                $"FA@{StyleBAnnotationFrontPlan}",
                isStyleB,
                "CKVD FG Style B Side keeps FA."),

            Keep(
                $"{idPrefix}-SIDE-STYLE-B-BA",
                Side,
                $"BA@{StyleBAnnotationFrontPlan}",
                isStyleB,
                "CKVD FG Style B Side keeps BA."),

            Keep(
                $"{idPrefix}-SIDE-STYLE-B-E",
                Side,
                $"E@{StyleBAnnotationFrontPlan}",
                isStyleB,
                "CKVD FG Style B Side keeps E."),

            Keep(
                $"{idPrefix}-SIDE-STYLE-B-X",
                Side,
                $"X@{StyleBAnnotationFrontPlan}",
                All(isStyleB, DimPresent("X")),
                "CKVD FG Style B Side keeps X only when X was supplied by DB."),

            Keep(
                $"{idPrefix}-SIDE-STYLE-B-FX",
                Side,
                $"FX@{StyleBAnnotationFrontPlan}",
                All(isStyleB, DimPresent("FX")),
                "CKVD FG Style B Side keeps FX only when FX was supplied by DB."),

            // TOP VIEW
            Keep(
                $"{idPrefix}-TOP-TD",
                Top,
                $"TD@{AnnotationTopPlan}",
                Always(),
                "CKVD FG Top keeps TD."),

            Keep(
                $"{idPrefix}-TOP-TDF",
                Top,
                $"TDF@{AnnotationTopPlan}",
                Always(),
                "CKVD FG Top keeps TDF."),

            // DETAIL VIEW
            Keep(
                $"{idPrefix}-DETAIL-W",
                Detail,
                $"W@{AnnotationRightPlan}",
                Always(),
                "CKVD FG Detail keeps W."),

            Keep(
                $"{idPrefix}-DETAIL-ISA",
                Detail,
                $"ISA@{AnnotationRightPlan}",
                Always(),
                "CKVD FG Detail keeps ISA."),

            Keep(
                $"{idPrefix}-DETAIL-GD",
                Detail,
                $"GD@{AnnotationRightPlan}",
                Always(),
                "CKVD FG Detail keeps GD."),

            Keep(
                $"{idPrefix}-DETAIL-B",
                Detail,
                $"B@{AnnotationRightPlan}",
                Always(),
                "CKVD FG Detail keeps B."),

            Keep(
                $"{idPrefix}-DETAIL-GA",
                Detail,
                $"GA@{AnnotationRightPlan}",
                Always(),
                "CKVD FG Detail keeps GA."),

            Keep(
                $"{idPrefix}-DETAIL-VW",
                Detail,
                $"VW@{AnnotationRightPlan}",
                DimPositive("VW"),
                "CKVD FG Detail keeps optional VW when VW is positive."),

            Keep(
                $"{idPrefix}-DETAIL-VRA",
                Detail,
                $"VRA@{AnnotationRightPlan}",
                DimPositive("VRA"),
                "CKVD FG Detail keeps optional VRA when VRA is positive."),

            // SECTION VIEW - STYLE A
            Keep(
                $"{idPrefix}-SECTION-STYLE-A-FL",
                Section,
                $"FL@{StyleAAnnotationFrontPlan}",
                isStyleA,
                "CKVD FG Style A Section keeps FL."),

            Keep(
                $"{idPrefix}-SECTION-STYLE-A-F",
                Section,
                $"F@{StyleAAnnotationFrontPlan}",
                isStyleA,
                "CKVD FG Style A Section keeps F."),

            Keep(
                $"{idPrefix}-SECTION-STYLE-A-BR",
                Section,
                $"BR@{StyleAAnnotationFrontPlan}",
                isStyleA,
                "CKVD FG Style A Section keeps BR."),

            Keep(
                $"{idPrefix}-SECTION-STYLE-A-FR",
                Section,
                $"FR@{StyleAAnnotationFrontPlan}",
                isStyleA,
                "CKVD FG Style A Section keeps FR."),

            Keep(
                $"{idPrefix}-SECTION-STYLE-A-BRX",
                Section,
                $"BRX@{StyleAAnnotationFrontPlan}",
                All(isStyleA, DimPresent("BRX")),
                "CKVD FG Style A Section keeps BRX only when supplied by DB."),

            Keep(
                $"{idPrefix}-SECTION-STYLE-A-FRX",
                Section,
                $"FRX@{StyleAAnnotationFrontPlan}",
                All(isStyleA, DimPresent("FRX")),
                "CKVD FG Style A Section keeps FRX only when supplied by DB."),

            // SECTION VIEW - STYLE B
            Keep(
                $"{idPrefix}-SECTION-STYLE-B-FL",
                Section,
                $"FL@{StyleBAnnotationFrontPlan}",
                isStyleB,
                "CKVD FG Style B Section keeps FL."),

            Keep(
                $"{idPrefix}-SECTION-STYLE-B-F",
                Section,
                $"F@{StyleBAnnotationFrontPlan}",
                isStyleB,
                "CKVD FG Style B Section keeps F."),

            Keep(
                $"{idPrefix}-SECTION-STYLE-B-BR",
                Section,
                $"BR@{StyleBAnnotationFrontPlan}",
                isStyleB,
                "CKVD FG Style B Section keeps BR."),

            Keep(
                $"{idPrefix}-SECTION-STYLE-B-FR",
                Section,
                $"FR@{StyleBAnnotationFrontPlan}",
                isStyleB,
                "CKVD FG Style B Section keeps FR."),

            Keep(
                $"{idPrefix}-SECTION-STYLE-B-BRX",
                Section,
                $"BRX@{StyleBAnnotationFrontPlan}",
                All(isStyleB, DimPresent("BRX")),
                "CKVD FG Style B Section keeps BRX only when supplied by DB."),

            Keep(
                $"{idPrefix}-SECTION-STYLE-B-FRX",
                Section,
                $"FRX@{StyleBAnnotationFrontPlan}",
                All(isStyleB, DimPresent("FRX")),
                "CKVD FG Style B Section keeps FRX only when supplied by DB.")
        };
    }

    /// <summary>
    /// Builds the shared CKVD PGB Production/Customer rules.
    /// PGB Customer intentionally resolves to the PGB Production profile,
    /// because the designer specified the same annotation set for both.
    /// </summary>
    protected IReadOnlyList<AnnotationKeepRule>
        BuildPgbProductionCustomerRules(string idPrefix)
    {
        var isStyleA = WedTypeIs(CkvdAnnotationStyles.StyleA);
        var isStyleB = WedTypeIs(CkvdAnnotationStyles.StyleB);

        return new List<AnnotationKeepRule>
        {
            // FRONT VIEW
            // Designer screenshot label: "Right view".
            Keep(
                $"{idPrefix}-FRONT-TL",
                Front,
                $"TL@{AnnotationRightPlan}",
                Always(),
                "CKVD PGB Front keeps TL."),

            Keep(
                $"{idPrefix}-FRONT-VR",
                Front,
                $"VR@{AnnotationRightPlan}",
                DimPositive("VR"),
                "CKVD PGB Front keeps optional VR when VR is positive."),

            // SIDE VIEW
            // Designer screenshot label: "Front view".
            // STYLE A: BA and E only.
            Keep(
                $"{idPrefix}-SIDE-STYLE-A-BA",
                Side,
                $"BA@{StyleAAnnotationFrontPlan}",
                isStyleA,
                "CKVD PGB Style A Side keeps BA."),

            Keep(
                $"{idPrefix}-SIDE-STYLE-A-E",
                Side,
                $"E@{StyleAAnnotationFrontPlan}",
                isStyleA,
                "CKVD PGB Style A Side keeps E."),

            // STYLE B: FA, BA and E; X/FX only when supplied by DB.
            Keep(
                $"{idPrefix}-SIDE-STYLE-B-FA",
                Side,
                $"FA@{StyleBAnnotationFrontPlan}",
                isStyleB,
                "CKVD PGB Style B Side keeps FA."),

            Keep(
                $"{idPrefix}-SIDE-STYLE-B-BA",
                Side,
                $"BA@{StyleBAnnotationFrontPlan}",
                isStyleB,
                "CKVD PGB Style B Side keeps BA."),

            Keep(
                $"{idPrefix}-SIDE-STYLE-B-E",
                Side,
                $"E@{StyleBAnnotationFrontPlan}",
                isStyleB,
                "CKVD PGB Style B Side keeps E."),

            Keep(
                $"{idPrefix}-SIDE-STYLE-B-X",
                Side,
                $"X@{StyleBAnnotationFrontPlan}",
                All(isStyleB, DimPresent("X")),
                "CKVD PGB Style B Side keeps X only when supplied by DB."),

            Keep(
                $"{idPrefix}-SIDE-STYLE-B-FX",
                Side,
                $"FX@{StyleBAnnotationFrontPlan}",
                All(isStyleB, DimPresent("FX")),
                "CKVD PGB Style B Side keeps FX only when supplied by DB."),

            // TOP VIEW
            Keep(
                $"{idPrefix}-TOP-TD",
                Top,
                $"TD@{AnnotationTopPlan}",
                Always(),
                "CKVD PGB Top keeps TD."),

            Keep(
                $"{idPrefix}-TOP-TDF",
                Top,
                $"TDF@{AnnotationTopPlan}",
                Always(),
                "CKVD PGB Top keeps TDF."),

            // DETAIL VIEW
            Keep(
                $"{idPrefix}-DETAIL-W",
                Detail,
                $"W@{AnnotationRightPlan}",
                Always(),
                "CKVD PGB Detail keeps W."),

            Keep(
                $"{idPrefix}-DETAIL-ISA",
                Detail,
                $"ISA@{AnnotationRightPlan}",
                Always(),
                "CKVD PGB Detail keeps ISA."),

            Keep(
                $"{idPrefix}-DETAIL-VW",
                Detail,
                $"VW@{AnnotationRightPlan}",
                DimPositive("VW"),
                "CKVD PGB Detail keeps optional VW when VW is positive."),

            Keep(
                $"{idPrefix}-DETAIL-VRA",
                Detail,
                $"VRA@{AnnotationRightPlan}",
                DimPositive("VRA"),
                "CKVD PGB Detail keeps optional VRA when VRA is positive."),

            // SECTION VIEW
            Keep(
                $"{idPrefix}-SECTION-STYLE-A-FL",
                Section,
                $"FL@{StyleAAnnotationFrontPlan}",
                isStyleA,
                "CKVD PGB Style A Section keeps FL."),

            Keep(
                $"{idPrefix}-SECTION-STYLE-B-FL",
                Section,
                $"FL@{StyleBAnnotationFrontPlan}",
                isStyleB,
                "CKVD PGB Style B Section keeps FL.")
        };
    }

    /// <summary>
    /// Preserves the CKVD FG Overlay annotation behavior from the input folder.
    /// Overlay annotation requirements were not part of this update.
    /// </summary>
    protected IReadOnlyList<AnnotationKeepRule>
        BuildLegacyFgOverlayRules(string idPrefix)
        => new List<AnnotationKeepRule>
        {
            Keep(
                $"{idPrefix}-FRONT-TL",
                Front,
                "TL@TL_cutting",
                Always(),
                "Legacy CKVD FG overlay total length."),

            Keep(
                $"{idPrefix}-FRONT-W",
                Front,
                "W@sketch_ISA_grinding",
                Always(),
                "Legacy CKVD FG overlay front width."),

            Keep(
                $"{idPrefix}-FRONT-VW",
                Front,
                "VW@sketch_VW_VR_grinding",
                DimPositive("VR"),
                "Legacy CKVD FG overlay VW rule."),

            Keep(
                $"{idPrefix}-FRONT-VR",
                Front,
                "VR@sketch_VW_VR_grinding",
                DimPositive("VR"),
                "Legacy CKVD FG overlay VR rule."),

            Keep(
                $"{idPrefix}-SIDE-BA",
                Side,
                "BA@sketch_FA_BA_grinding",
                Always(),
                "Legacy CKVD FG overlay BA."),

            Keep(
                $"{idPrefix}-SIDE-FA",
                Side,
                "FA@sketch_FA_BA_grinding",
                Always(),
                "Legacy CKVD FG overlay FA."),

            Keep(
                $"{idPrefix}-SIDE-E",
                Side,
                "E@sketch_FA_BA_grinding",
                Always(),
                "Legacy CKVD FG overlay E."),

            Keep(
                $"{idPrefix}-SIDE-X",
                Side,
                "X@sketch_FA_BA_grinding",
                DimPositive("X"),
                "Legacy CKVD FG overlay X."),

            Keep(
                $"{idPrefix}-SIDE-FX",
                Side,
                "FX@sketch_FA_BA_grinding",
                DimPositive("FX"),
                "Legacy CKVD FG overlay FX."),

            Keep(
                $"{idPrefix}-TOP-TD",
                Top,
                "TD@sketch_TL_cutting",
                Always(),
                "Legacy CKVD FG overlay TD."),

            Keep(
                $"{idPrefix}-TOP-TDF",
                Top,
                "TDF@sketch_TDF_cutting",
                Always(),
                "Legacy CKVD FG overlay TDF."),

            Keep(
                $"{idPrefix}-DETAIL-W",
                Detail,
                "W@sketch_ISA_grinding",
                Always(),
                "Legacy CKVD FG overlay detail W."),

            Keep(
                $"{idPrefix}-DETAIL-B",
                Detail,
                "B@sketch_section_V-Groove",
                Always(),
                "Legacy CKVD FG overlay detail B."),

            Keep(
                $"{idPrefix}-DETAIL-GA",
                Detail,
                "GA@sketch_section_V-Groove",
                Always(),
                "Legacy CKVD FG overlay detail GA."),

            Keep(
                $"{idPrefix}-DETAIL-GR",
                Detail,
                "GR@sketch_section_V-Groove",
                Always(),
                "Legacy CKVD FG overlay detail GR."),

            Keep(
                $"{idPrefix}-DETAIL-GD",
                Detail,
                "GD@sketch_section_V-Groove",
                Always(),
                "Legacy CKVD FG overlay detail GD."),

            Keep(
                $"{idPrefix}-SECTION-FR",
                Section,
                "FR@FG_Production_Wed_F",
                Always(),
                "Legacy CKVD FG overlay section FR."),

            Keep(
                $"{idPrefix}-SECTION-BR",
                Section,
                "BR@FG_Production_Wed_F",
                Always(),
                "Legacy CKVD FG overlay section BR."),

            Keep(
                $"{idPrefix}-SECTION-F",
                Section,
                "F@FG_Production_Wed_F",
                Always(),
                "Legacy CKVD FG overlay section F."),

            Keep(
                $"{idPrefix}-SECTION-FL",
                Section,
                "FL@sketch_FA_BA_grinding",
                Always(),
                "Legacy CKVD FG overlay section FL.")
        };

    /// <summary>
    /// Preserves the CKVD PGB Overlay annotation behavior from the input folder.
    /// </summary>
    protected IReadOnlyList<AnnotationKeepRule>
        BuildLegacyPgbOverlayRules(string idPrefix)
        => new List<AnnotationKeepRule>
        {
            Keep(
                $"{idPrefix}-FRONT-TL",
                Front,
                "TL@TL_cutting",
                Always(),
                "Legacy CKVD PGB overlay total length."),

            Keep(
                $"{idPrefix}-SIDE-BA",
                Side,
                "BA@sketch_FA_BA_grinding",
                Always(),
                "Legacy CKVD PGB overlay BA."),

            Keep(
                $"{idPrefix}-SIDE-FA",
                Side,
                "FA@sketch_FA_BA_grinding",
                Always(),
                "Legacy CKVD PGB overlay FA."),

            Keep(
                $"{idPrefix}-SIDE-E",
                Side,
                "E@sketch_FA_BA_grinding",
                Always(),
                "Legacy CKVD PGB overlay E."),

            Keep(
                $"{idPrefix}-SIDE-X",
                Side,
                "X@sketch_FA_BA_grinding",
                DimPositive("X"),
                "Legacy CKVD PGB overlay X."),

            Keep(
                $"{idPrefix}-SIDE-FX",
                Side,
                "FX@sketch_FA_BA_grinding",
                DimPositive("FX"),
                "Legacy CKVD PGB overlay FX."),

            Keep(
                $"{idPrefix}-TOP-TD",
                Top,
                "TD@sketch_TL_cutting",
                Always(),
                "Legacy CKVD PGB overlay TD."),

            Keep(
                $"{idPrefix}-TOP-TDF",
                Top,
                "TDF@sketch_TDF_cutting",
                Always(),
                "Legacy CKVD PGB overlay TDF."),

            Keep(
                $"{idPrefix}-DETAIL-W",
                Detail,
                "W@sketch_ISA_grinding",
                Always(),
                "Legacy CKVD PGB overlay detail W."),

            Keep(
                $"{idPrefix}-SECTION-FL",
                Section,
                "FL@sketch_FA_BA_grinding",
                Always(),
                "Legacy CKVD PGB overlay section FL.")
        };
}

public sealed class CkvdFgProductionAnnotationRules :
    CkvdAnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile
        => AnnotationCleanupProfile.CkvdFgProduction;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public CkvdFgProductionAnnotationRules()
    {
        Rules = BuildFgProductionCustomerRules(
            "CKVD-FG-PROD");
    }
}

public sealed class CkvdFgCustomerAnnotationRules :
    CkvdAnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile
        => AnnotationCleanupProfile.CkvdFgCustomer;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public CkvdFgCustomerAnnotationRules()
    {
        Rules = BuildFgProductionCustomerRules(
            "CKVD-FG-CUST");
    }
}

public sealed class CkvdFgOverlayAnnotationRules :
    CkvdAnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile
        => AnnotationCleanupProfile.CkvdFgOverlay;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public CkvdFgOverlayAnnotationRules()
    {
        Rules = BuildLegacyFgOverlayRules(
            "CKVD-FG-OVERLAY");
    }
}

public sealed class CkvdPgbProductionAnnotationRules :
    CkvdAnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile
        => AnnotationCleanupProfile.CkvdPgbProduction;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public CkvdPgbProductionAnnotationRules()
    {
        Rules = BuildPgbProductionCustomerRules(
            "CKVD-PGB-PROD");
    }
}

public sealed class CkvdPgbOverlayAnnotationRules :
    CkvdAnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile
        => AnnotationCleanupProfile.CkvdPgbOverlay;

    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public CkvdPgbOverlayAnnotationRules()
    {
        Rules = BuildLegacyPgbOverlayRules(
            "CKVD-PGB-OVERLAY");
    }
}
