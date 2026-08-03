using System.Collections.Generic;
using System.Linq;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Conditions.AnnotationConditions;
using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain.AnnotationView;

namespace WAD.Runner.DrawingAutomation.Wedges.Osg7.Annotations;

public sealed class Osg7FgProductionAnnotationRules : AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.Osg7FgProduction;
    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public Osg7FgProductionAnnotationRules()
    {
        Rules = new List<AnnotationKeepRule>
        {
            // FRONT VIEW
            Keep("OSG7-FG-PROD-FRONT-TL", Front, "TL@ANNOT_RIGH_PLAN", Always(), "OSG7 FG base total length."),
            Keep("OSG7-FG-PROD-FRONT-VR", Front, "VR@ANNOT_RIGH_PLAN", DimPositive("VR"), "Keep front VR only when VR is positive."),

            // SIDE VIEW
            Keep("OSG7-FG-PROD-SIDE-FA", Side, "FA@ANNOT_FRONT_PLAN", Always(), "OSG7 side FA."),
            Keep("OSG7-FG-PROD-SIDE-BA", Side, "BA@ANNOT_FRONT_PLAN", Always(), "OSG7 side BA."),
            Keep("OSG7-FG-PROD-SIDE-X", Side, "X@ANNOT_FRONT_PLAN", DimPositive("X"), "Keep X only when X is positive."),
            Keep("OSG7-FG-PROD-SIDE-FX", Side, "FX@ANNOT_FRONT_PLAN", DimPositive("FX"), "Keep FX only when FX is positive."),
            Keep("OSG7-FG-PROD-SIDE-VFL", Side, "VFL@ANNOT_FRONT_PLAN", DimPositive("VFL"), "Keep VFL only when VFL is positive."),

            // TOP VIEW
            Keep("OSG7-FG-PROD-TOP-TD", Top, "TD@ANNOT_TOP_PLAN", Always(), "OSG7 top TD."),
            Keep("OSG7-FG-PROD-TOP-TDF", Top, "TDF@ANNOT_TOP_PLAN", Always(), "OSG7 top TDF."),

            // DETAIL VIEW
            Keep("OSG7-FG-PROD-DETAIL-W", Detail, "W@ANNOT_RIGH_PLAN", Always(), "OSG7 FG detail W."),
            Keep("OSG7-FG-PROD-DETAIL-GA", Detail, "GA@ANNOT_RIGH_PLAN", Always(), "OSG7 FG detail GA."),
            Keep("OSG7-FG-PROD-DETAIL-ISA", Detail, "ISA@ANNOT_RIGH_PLAN", Always(), "OSG7 FG detail ISA."),
            Keep("OSG7-FG-PROD-DETAIL-B", Detail, "B@ANNOT_RIGH_PLAN", Always(), "Production keeps B."),
            KeepWithAliases(
                "OSG7-FG-PROD-DETAIL-GD",
                Detail,
                "GD@ANNOT_RIGH_PLAN",
                new[] { "GD" },
                Always(),
                "Keep GD whether SolidWorks exposes the model-linked or drawing-only name."),
            KeepWithAliases(
                "OSG7-FG-PROD-DETAIL-GR",
                Detail,
                "GR@ANNOT_RIGH_PLAN",
                new[] { "GR" },
                Always(),
                "Keep GR whether SolidWorks exposes the model-linked or drawing-only name."),
            Keep("OSG7-FG-PROD-DETAIL-VW", Detail, "VW@ANNOT_RIGH_PLAN", DimPositive("VW"), "Keep VW only when VW is positive."),
            Keep("OSG7-FG-PROD-DETAIL-VRA", Detail, "VRA@ANNOT_RIGH_PLAN", DimPositive("VRA"), "Keep VRA only when VRA is positive."),

            // SECTION VIEW
            Keep("OSG7-FG-PROD-SECTION-FR", Section, "FR@ANNOT_FRONT_PLAN", Always(), "OSG7 FG section FR."),
            Keep("OSG7-FG-PROD-SECTION-BR", Section, "BR@ANNOT_FRONT_PLAN", Always(), "OSG7 FG section BR."),
            Keep("OSG7-FG-PROD-SECTION-FL", Section, "FL@ANNOT_FRONT_PLAN", Always(), "OSG7 FG section FL."),
            Keep("OSG7-FG-PROD-SECTION-F", Section, "F@ANNOT_FRONT_PLAN", Always(), "OSG7 FG section F."),
            KeepWithAliases(
                "OSG7-FG-PROD-SECTION-FRX",
                Section,
                "D3@ANNOT_FRONT_PLAN",
                new[] { "FRX", "FRX@ANNOT_FRONT_PLAN" },
                Always(),
                "Keep FRX across legacy D3 and explicit FRX template names."),
            KeepWithAliases(
                "OSG7-FG-PROD-SECTION-BRX",
                Section,
                "D2@ANNOT_FRONT_PLAN",
                new[] { "BRX", "BRX@ANNOT_FRONT_PLAN" },
                Always(),
                "Keep BRX across legacy D2 and explicit BRX template names.")
        };
    }
}

public sealed class Osg7FgCustomerAnnotationRules : AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.Osg7FgCustomer;
    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public Osg7FgCustomerAnnotationRules()
    {
        Rules = new List<AnnotationKeepRule>
        {
            // FRONT VIEW
            Keep("OSG7-FG-CUST-FRONT-TL", Front, "TL@ANNOT_RIGH_PLAN", Always(), "OSG7 FG customer base total length."),

            // SIDE VIEW
            Keep("OSG7-FG-CUST-SIDE-FA", Side, "FA@ANNOT_FRONT_PLAN", Always(), "OSG7 customer side FA."),
            Keep("OSG7-FG-CUST-SIDE-BA", Side, "BA@ANNOT_FRONT_PLAN", Always(), "OSG7 customer side BA."),
            Keep("OSG7-FG-CUST-SIDE-X", Side, "X@ANNOT_FRONT_PLAN", DimPositive("X"), "Keep X only when X is positive."),
            Keep("OSG7-FG-CUST-SIDE-FX", Side, "FX@ANNOT_FRONT_PLAN", DimPositive("FX"), "Keep FX only when FX is positive."),
            Keep("OSG7-FG-CUST-SIDE-VFL", Side, "VFL@ANNOT_FRONT_PLAN", DimPositive("VFL"), "Keep VFL only when VFL is positive."),

            // TOP VIEW
            Keep("OSG7-FG-CUST-TOP-TD", Top, "TD@ANNOT_TOP_PLAN", Always(), "OSG7 customer top TD."),
            Keep("OSG7-FG-CUST-TOP-TDF", Top, "TDF@ANNOT_TOP_PLAN", Always(), "OSG7 customer top TDF."),

            // DETAIL VIEW
            Keep("OSG7-FG-CUST-DETAIL-W", Detail, "W@ANNOT_RIGH_PLAN", Always(), "OSG7 FG customer detail W."),
            Keep("OSG7-FG-CUST-DETAIL-GA", Detail, "GA@ANNOT_RIGH_PLAN", Always(), "OSG7 FG customer detail GA."),
            Keep("OSG7-FG-CUST-DETAIL-ISA", Detail, "ISA@ANNOT_RIGH_PLAN", Always(), "OSG7 FG customer detail ISA."),
            Keep("OSG7-FG-CUST-DETAIL-B", Detail, "B@ANNOT_RIGH_PLAN", Always(), "Customer keeps B."),
            KeepWithAliases(
                "OSG7-FG-CUST-DETAIL-GD",
                Detail,
                "GD@ANNOT_RIGH_PLAN",
                new[] { "GD" },
                Always(),
                "Keep GD whether SolidWorks exposes the model-linked or drawing-only name."),
            KeepWithAliases(
                "OSG7-FG-CUST-DETAIL-GR",
                Detail,
                "GR@ANNOT_RIGH_PLAN",
                new[] { "GR" },
                Always(),
                "Keep GR whether SolidWorks exposes the model-linked or drawing-only name."),
            Keep("OSG7-FG-CUST-DETAIL-VW", Detail, "VW@ANNOT_RIGH_PLAN", DimPositive("VW"), "Keep VW only when VW is positive."),
            Keep("OSG7-FG-CUST-DETAIL-VRA", Detail, "VRA@ANNOT_RIGH_PLAN", DimPositive("VRA"), "Keep VRA only when VRA is positive."),

            // SECTION VIEW
            Keep("OSG7-FG-CUST-SECTION-FR", Section, "FR@ANNOT_FRONT_PLAN", Always(), "OSG7 FG customer section FR."),
            Keep("OSG7-FG-CUST-SECTION-BR", Section, "BR@ANNOT_FRONT_PLAN", Always(), "OSG7 FG customer section BR."),
            Keep("OSG7-FG-CUST-SECTION-FL", Section, "FL@ANNOT_FRONT_PLAN", Always(), "OSG7 FG customer section FL."),
            Keep("OSG7-FG-CUST-SECTION-F", Section, "F@ANNOT_FRONT_PLAN", Always(), "OSG7 FG customer section F."),
            KeepWithAliases(
                "OSG7-FG-CUST-SECTION-FRX",
                Section,
                "D3@ANNOT_FRONT_PLAN",
                new[] { "FRX", "FRX@ANNOT_FRONT_PLAN" },
                Always(),
                "Keep FRX across legacy D3 and explicit FRX template names."),
            KeepWithAliases(
                "OSG7-FG-CUST-SECTION-BRX",
                Section,
                "D2@ANNOT_FRONT_PLAN",
                new[] { "BRX", "BRX@ANNOT_FRONT_PLAN" },
                Always(),
                "Keep BRX across legacy D2 and explicit BRX template names.")
        };
    }
}

public sealed class Osg7FgOverlayAnnotationRules : AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.Osg7FgOverlay;
    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public Osg7FgOverlayAnnotationRules()
    {
        Rules = new Osg7FgProductionAnnotationRules()
            .Rules
            .Select(rule => CloneForProfile(rule, Profile, "OSG7-FG-PROD", "OSG7-FG-OVERLAY"))
            .ToList();
    }
}

public sealed class Osg7PgbProductionAnnotationRules : AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.Osg7PgbProduction;
    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public Osg7PgbProductionAnnotationRules()
    {
        Rules = new List<AnnotationKeepRule>
        {
            // FRONT VIEW
            Keep("OSG7-PGB-PROD-FRONT-TL", Front, "TL@ANNOT_RIGH_PLAN", Always(), "OSG7 PGB base total length."),

            // SIDE VIEW
            Keep("OSG7-PGB-PROD-SIDE-FA", Side, "FA@ANNOT_FRONT_PLAN", Always(), "OSG7 PGB side FA."),
            Keep("OSG7-PGB-PROD-SIDE-BA", Side, "BA@ANNOT_FRONT_PLAN", Always(), "OSG7 PGB side BA."),
            Keep("OSG7-PGB-PROD-SIDE-X", Side, "X@ANNOT_FRONT_PLAN", DimPositive("X"), "Keep X only when X is positive."),
            Keep("OSG7-PGB-PROD-SIDE-FX", Side, "FX@ANNOT_FRONT_PLAN", DimPositive("FX"), "Keep FX only when FX is positive."),
            Keep("OSG7-PGB-PROD-SIDE-VFL", Side, "VFL@ANNOT_FRONT_PLAN", DimPositive("VFL"), "Keep VFL only when VFL is positive."),

            // TOP VIEW
            Keep("OSG7-PGB-PROD-TOP-TD", Top, "TD@ANNOT_TOP_PLAN", Always(), "OSG7 PGB top TD."),
            Keep("OSG7-PGB-PROD-TOP-TDF", Top, "TDF@ANNOT_TOP_PLAN", Always(), "OSG7 PGB top TDF."),

            // DETAIL VIEW
            Keep("OSG7-PGB-PROD-DETAIL-W", Detail, "W@ANNOT_RIGH_PLAN", Always(), "OSG7 PGB detail W."),
            Keep("OSG7-PGB-PROD-DETAIL-VW", Detail, "VW@ANNOT_RIGH_PLAN", DimPositive("VW"), "Keep VW only when VW is positive."),
            Keep("OSG7-PGB-PROD-DETAIL-VRA", Detail, "VRA@ANNOT_RIGH_PLAN", DimPositive("VRA"), "Keep VRA only when VRA is positive."),

            // SECTION VIEW
            Keep("OSG7-PGB-PROD-SECTION-FL", Section, "FL@ANNOT_FRONT_PLAN", Always(), "OSG7 PGB section FL.")
        };
    }
}

public sealed class Osg7PgbOverlayAnnotationRules : AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.Osg7PgbOverlay;
    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public Osg7PgbOverlayAnnotationRules()
    {
        Rules = new Osg7PgbProductionAnnotationRules()
            .Rules
            .Select(rule => CloneForProfile(rule, Profile, "OSG7-PGB-PROD", "OSG7-PGB-OVERLAY"))
            .ToList();
    }
}
