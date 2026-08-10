using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Overlay.Positioning;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;
using WAD.Runner.DrawingAutomation.Wedges._4516.Annotations;

namespace WAD.Runner.DrawingAutomation.Wedges._4516;

public sealed class _4516DrawingModule : IDrawingWedgeModule
{
    private static readonly IReadOnlyList<string> OverlayDimensionKeyList =
        Array.AsReadOnly(new[]
        {
            "TD", "TDF", "W", "ISA", "VW", "VR", "VRR", "VRA", "TL",
            "B", "GA", "GD", "GO", "CL", "CD", "BA", "T", "FL", "C",
            "NR", "BR", "FR", "H", "HA", "FNA", "F", "BF", "Y",
            "G", "CGR", "CGD", "VBL", "VBLR"
        });

    private static readonly ViewNames ProductionCustomerViews = new(
        Front: "Drawing View2",
        Side: "Drawing View1",
        Top: "Drawing View3",
        Detail: "Drawing View4",
        Section: "Section View AB-AB");

    private static readonly ViewNames OverlayViews = new(
        Front: "Drawing View5",
        Side: "Drawing View4",
        Top: "Drawing View3",
        Detail: "Drawing View1",
        Section: "Drawing View2");

    private static readonly IReadOnlySet<string> FgDrawingTableKeys = Keys(
        "TD", "TDF", "W", "ISA", "VW", "VR", "VRA", "TL",
        "B", "GA", "GD", "GO", "CL", "CD", "BA", "T", "FL", "C",
        "NR", "BR", "FR", "H", "HA", "FNA", "F", "BF", "Y",
        "G", "CGR", "CGD", "VBL");

    private static readonly IReadOnlySet<string> FgOverlayTableKeys = Keys(
        "TD", "TDF", "W", "ISA", "VW", "VR", "VRA", "TL",
        "B", "GA", "GD", "GO", "CL", "CD", "BA", "T", "FL", "C",
        "NR", "BR", "FR", "H", "HA", "FNA", "F", "BF", "Y",
        "G", "CGR", "CGD", "VBL");

    private static readonly IReadOnlySet<string> PgbProductionTableKeys = Keys(
        "TD", "TDF", "W", "ISA", "VW", "VR", "VRA",
        "TL", "BA", "T", "FL", "VBL");

    private static readonly IReadOnlySet<string> PgbOverlayTableKeys = Keys(
        "TD", "TDF", "W", "ISA", "VW", "VR", "VRA",
        "TL", "BA", "T", "FL", "VBL");

    public _4516DrawingModule()
    {
        Profiles = Array.AsReadOnly(new[]
        {
            DrawingProfileFactory.Create(
                WedgeType,
                WedgeSubclass.FG,
                DrawingType.Production,
                "4516 FG Production",
                ProductionCustomerViews,
                new[] { "Sheet1" },
                DrawingViewNames.SecondaryBreaklineViews),

            DrawingProfileFactory.Create(
                WedgeType,
                WedgeSubclass.FG,
                DrawingType.Customer,
                "4516 FG Customer",
                ProductionCustomerViews,
                new[] { "Sheet1" },
                DrawingViewNames.SecondaryBreaklineViews),

            DrawingProfileFactory.Create(
                WedgeType,
                WedgeSubclass.FG,
                DrawingType.Overlay,
                "4516 FG Overlay",
                OverlayViews,
                new[] { "OVERLAY" },
                DrawingViewNames.NoBreaklineViews),

            DrawingProfileFactory.Create(
                WedgeType,
                WedgeSubclass.PGB,
                DrawingType.Production,
                "4516 PGB Production",
                ProductionCustomerViews,
                new[] { "Sheet1" },
                DrawingViewNames.SecondaryBreaklineViews),

            DrawingProfileFactory.Create(
                WedgeType,
                WedgeSubclass.PGB,
                DrawingType.Overlay,
                "4516 PGB Overlay",
                OverlayViews,
                new[] { "PGB_OVERLAY" },
                DrawingViewNames.NoBreaklineViews)
        });
    }

    public WedgeType WedgeType =>
        global::WAD.Runner.DataManagement.Domain.Wedge.WedgeType._4516;

    public DrawingWedgeBehavior Behavior { get; } = new(
        OverlayMagnificationSourceKey: "FL",
        OverlayDimensionKeys: OverlayDimensionKeyList,
        OverlayReferencePointSketch: "ref_point_1",
        RepositionPrimaryOverlayViews: false,
        DeleteFrontOverlayViewWhenVrIsZero: true,
        HideVrExtremaWhenOverlayCompressed: false);

    public IReadOnlyList<DrawingProfile> Profiles { get; }

    public IOverlayViewPositioningRule OverlayPositioningRule { get; } =
        new _4516OverlayViewPositioningRule();

    public IReadOnlyList<IAnnotationRuleCatalog> AnnotationCatalogs { get; } =
        Array.AsReadOnly<IAnnotationRuleCatalog>(
            new IAnnotationRuleCatalog[]
            {
                new _4516FgProductionAnnotationRules(),
                new _4516FgCustomerAnnotationRules(),
                new _4516FgOverlayAnnotationRules(),
                new _4516PgbProductionAnnotationRules(),
                new _4516PgbOverlayAnnotationRules()
            });

    public IAnnotationWedgeContextResolver AnnotationContextResolver { get; } =
        new _4516AnnotationContextResolver();

    public AnnotationCleanupProfile ResolveAnnotationProfile(
        WedgeSubclass subclass,
        DrawingType drawingType)
    {
        /*
         * PGB Production and Customer use the same annotation rules.
         */
        if (subclass == WedgeSubclass.PGB)
        {
            return drawingType == DrawingType.Overlay
                ? AnnotationCleanupProfile._4516PgbOverlay
                : AnnotationCleanupProfile._4516PgbProduction;
        }

        return drawingType switch
        {
            DrawingType.Overlay =>
                AnnotationCleanupProfile._4516FgOverlay,

            DrawingType.Customer =>
                AnnotationCleanupProfile._4516FgCustomer,

            _ =>
                AnnotationCleanupProfile._4516FgProduction
        };
    }

    public string? ResolveReferencedConfiguration(
        string logicalView,
        WedgeSubclass subclass,
        DrawingType drawingType,
        bool hasVw,
        bool hasVr)
    {
        if (drawingType is DrawingType.Production or DrawingType.Customer)
            return "Default";

        if (drawingType != DrawingType.Overlay)
            return null;

        return IsDetail(logicalView) && hasVw && hasVr
            ? "overlay_non_std_cut"
            : "overlay_std_cut";
    }

    public IReadOnlySet<string>? GetAllowedDimensionTableKeys(
        WedgeSubclass subclass,
        DrawingType drawingType)
        => (subclass, drawingType) switch
        {
            (WedgeSubclass.FG, DrawingType.Production) =>
                FgDrawingTableKeys,

            (WedgeSubclass.FG, DrawingType.Customer) =>
                FgDrawingTableKeys,

            (WedgeSubclass.FG, DrawingType.Overlay) =>
                FgOverlayTableKeys,

            (WedgeSubclass.PGB, DrawingType.Production) =>
                PgbProductionTableKeys,

            (WedgeSubclass.PGB, DrawingType.Overlay) =>
                PgbOverlayTableKeys,

            _ => null
        };

    private static bool IsDetail(
        string logicalView)
        => string.Equals(
            logicalView?.Trim(),
            DrawingViewNames.Detail,
            StringComparison.OrdinalIgnoreCase);

    private static IReadOnlySet<string> Keys(
        params string[] values)
        => new HashSet<string>(
            values,
            StringComparer.OrdinalIgnoreCase);
}