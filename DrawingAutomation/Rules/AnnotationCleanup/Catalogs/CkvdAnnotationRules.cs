using System.Collections.Generic;
using System.Linq;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Conditions.AnnotationConditions;
using static WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain.AnnotationView;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;

public sealed class CkvdFgProductionAnnotationRules : AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.CkvdFgProduction;
    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public CkvdFgProductionAnnotationRules()
    {
        Rules = new List<AnnotationKeepRule>
        {
            // FRONT VIEW
            Keep("CKVD-FG-PROD-FRONT-TL", Front, "TL@TL_cutting", Always(), "CKVD FG base total length."),
            Keep("CKVD-FG-PROD-FRONT-W", Front, "W@sketch_ISA_grinding", Always(), "CKVD FG front width."),
            Keep("CKVD-FG-PROD-FRONT-VW", Front, "VW@sketch_VW_VR_grinding", DimPositive("VR"), "Legacy CKVD rule: keep VW when VR is positive."),
            Keep("CKVD-FG-PROD-FRONT-VR", Front, "VR@sketch_VW_VR_grinding", DimPositive("VR"), "Keep VR when VR is positive."),

            // SIDE VIEW
            Keep("CKVD-FG-PROD-SIDE-BA", Side, "BA@sketch_FA_BA_grinding", Always(), "CKVD FG side BA."),
            Keep("CKVD-FG-PROD-SIDE-FA", Side, "FA@sketch_FA_BA_grinding", Always(), "CKVD FG side FA."),
            Keep("CKVD-FG-PROD-SIDE-E", Side, "E@sketch_FA_BA_grinding", Always(), "CKVD FG side E."),
            Keep("CKVD-FG-PROD-SIDE-X", Side, "X@sketch_FA_BA_grinding", DimPositive("X"), "Keep X only when X is positive."),
            Keep("CKVD-FG-PROD-SIDE-FX", Side, "FX@sketch_FA_BA_grinding", DimPositive("FX"), "Keep FX only when FX is positive."),

            // TOP VIEW
            Keep("CKVD-FG-PROD-TOP-TD", Top, "TD@sketch_TL_cutting", Always(), "CKVD top TD."),
            Keep("CKVD-FG-PROD-TOP-TDF", Top, "TDF@sketch_TDF_cutting", Always(), "CKVD top TDF."),

            // DETAIL VIEW
            Keep("CKVD-FG-PROD-DETAIL-W", Detail, "W@sketch_ISA_grinding", Always(), "CKVD FG detail W."),
            Keep("CKVD-FG-PROD-DETAIL-B", Detail, "B@sketch_section_V-Groove", Always(), "Production keeps B."),
            Keep("CKVD-FG-PROD-DETAIL-GA", Detail, "GA@sketch_section_V-Groove", Always(), "Production keeps GA."),
            Keep("CKVD-FG-PROD-DETAIL-GR", Detail, "GR@sketch_section_V-Groove", Always(), "Production keeps GR."),
            Keep("CKVD-FG-PROD-DETAIL-GD", Detail, "GD@sketch_section_V-Groove", Always(), "Production keeps GD."),

            // SECTION VIEW
            Keep("CKVD-FG-PROD-SECTION-FR", Section, "FR@FG_Production_Wed_F", Always(), "CKVD FG section FR."),
            Keep("CKVD-FG-PROD-SECTION-BR", Section, "BR@FG_Production_Wed_F", Always(), "CKVD FG section BR."),
            Keep("CKVD-FG-PROD-SECTION-F", Section, "F@FG_Production_Wed_F", Always(), "CKVD FG section F."),
            Keep("CKVD-FG-PROD-SECTION-FL", Section, "FL@sketch_FA_BA_grinding", Always(), "CKVD FG section FL.")
        };
    }
}

public sealed class CkvdFgCustomerAnnotationRules : AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.CkvdFgCustomer;
    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public CkvdFgCustomerAnnotationRules()
    {
        Rules = new List<AnnotationKeepRule>
        {
            // FRONT VIEW
            Keep("CKVD-FG-CUST-FRONT-TL", Front, "TL@TL_cutting", Always(), "CKVD FG customer base total length."),
            Keep("CKVD-FG-CUST-FRONT-W", Front, "W@sketch_ISA_grinding", Always(), "CKVD FG customer front width."),
            Keep("CKVD-FG-CUST-FRONT-VW", Front, "VW@sketch_VW_VR_grinding", DimPositive("VR"), "Legacy CKVD customer rule: keep VW when VR is positive."),
            Keep("CKVD-FG-CUST-FRONT-VR", Front, "VR@sketch_VW_VR_grinding", DimPositive("VR"), "Keep VR when VR is positive."),

            // SIDE VIEW
            Keep("CKVD-FG-CUST-SIDE-BA", Side, "BA@sketch_FA_BA_grinding", Always(), "CKVD FG side BA."),
            Keep("CKVD-FG-CUST-SIDE-FA", Side, "FA@sketch_FA_BA_grinding", Always(), "CKVD FG side FA."),
            Keep("CKVD-FG-CUST-SIDE-E", Side, "E@sketch_FA_BA_grinding", Always(), "CKVD FG side E."),
            Keep("CKVD-FG-CUST-SIDE-X", Side, "X@sketch_FA_BA_grinding", DimPositive("X"), "Keep X only when X is positive."),
            Keep("CKVD-FG-CUST-SIDE-FX", Side, "FX@sketch_FA_BA_grinding", DimPositive("FX"), "Keep FX only when FX is positive."),

            // TOP VIEW
            Keep("CKVD-FG-CUST-TOP-TD", Top, "TD@sketch_TL_cutting", Always(), "CKVD top TD."),
            Keep("CKVD-FG-CUST-TOP-TDF", Top, "TDF@sketch_TDF_cutting", Always(), "CKVD top TDF."),

            // DETAIL VIEW - customer removes GR compared to production.
            Keep("CKVD-FG-CUST-DETAIL-W", Detail, "W@sketch_ISA_grinding", Always(), "CKVD FG customer detail W."),
            Keep("CKVD-FG-CUST-DETAIL-B", Detail, "B@sketch_section_V-Groove", Always(), "Customer keeps B."),
            Keep("CKVD-FG-CUST-DETAIL-GA", Detail, "GA@sketch_section_V-Groove", Always(), "Customer keeps GA."),
            Keep("CKVD-FG-CUST-DETAIL-GD", Detail, "GD@sketch_section_V-Groove", Always(), "Customer keeps GD."),

            // SECTION VIEW
            Keep("CKVD-FG-CUST-SECTION-FR", Section, "FR@FG_Production_Wed_F", Always(), "CKVD FG section FR."),
            Keep("CKVD-FG-CUST-SECTION-BR", Section, "BR@FG_Production_Wed_F", Always(), "CKVD FG section BR."),
            Keep("CKVD-FG-CUST-SECTION-F", Section, "F@FG_Production_Wed_F", Always(), "CKVD FG section F."),
            Keep("CKVD-FG-CUST-SECTION-FL", Section, "FL@sketch_FA_BA_grinding", Always(), "CKVD FG section FL.")
        };
    }
}

public sealed class CkvdFgOverlayAnnotationRules : AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.CkvdFgOverlay;
    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public CkvdFgOverlayAnnotationRules()
    {
        Rules = new CkvdFgProductionAnnotationRules()
            .Rules
            .Select(rule => CloneForProfile(rule, Profile, "CKVD-FG-PROD", "CKVD-FG-OVERLAY"))
            .ToList();
    }
}

public sealed class CkvdPgbProductionAnnotationRules : AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.CkvdPgbProduction;
    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public CkvdPgbProductionAnnotationRules()
    {
        Rules = new List<AnnotationKeepRule>
        {
            // FRONT VIEW
            Keep("CKVD-PGB-PROD-FRONT-TL", Front, "TL@TL_cutting", Always(), "CKVD PGB base total length."),

            // SIDE VIEW
            Keep("CKVD-PGB-PROD-SIDE-BA", Side, "BA@sketch_FA_BA_grinding", Always(), "CKVD PGB side BA."),
            Keep("CKVD-PGB-PROD-SIDE-FA", Side, "FA@sketch_FA_BA_grinding", Always(), "CKVD PGB side FA."),
            Keep("CKVD-PGB-PROD-SIDE-E", Side, "E@sketch_FA_BA_grinding", Always(), "CKVD PGB side E."),
            Keep("CKVD-PGB-PROD-SIDE-X", Side, "X@sketch_FA_BA_grinding", DimPositive("X"), "Keep X only when X is positive."),
            Keep("CKVD-PGB-PROD-SIDE-FX", Side, "FX@sketch_FA_BA_grinding", DimPositive("FX"), "Keep FX only when FX is positive."),

            // TOP VIEW
            Keep("CKVD-PGB-PROD-TOP-TD", Top, "TD@sketch_TL_cutting", Always(), "CKVD PGB top TD."),
            Keep("CKVD-PGB-PROD-TOP-TDF", Top, "TDF@sketch_TDF_cutting", Always(), "CKVD PGB top TDF."),

            // DETAIL VIEW
            Keep("CKVD-PGB-PROD-DETAIL-W", Detail, "W@sketch_ISA_grinding", Always(), "CKVD PGB detail W."),

            // SECTION VIEW
            Keep("CKVD-PGB-PROD-SECTION-FL", Section, "FL@sketch_FA_BA_grinding", Always(), "CKVD PGB section FL.")
        };
    }
}

public sealed class CkvdPgbOverlayAnnotationRules : AnnotationRuleCatalogBase
{
    public override AnnotationCleanupProfile Profile => AnnotationCleanupProfile.CkvdPgbOverlay;
    public override IReadOnlyList<AnnotationKeepRule> Rules { get; }

    public CkvdPgbOverlayAnnotationRules()
    {
        Rules = new CkvdPgbProductionAnnotationRules()
            .Rules
            .Select(rule => CloneForProfile(rule, Profile, "CKVD-PGB-PROD", "CKVD-PGB-OVERLAY"))
            .ToList();
    }
}
