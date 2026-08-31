using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Overlay.Positioning;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;
using WAD.Runner.DrawingAutomation.Wedges._1001.Annotations;

namespace WAD.Runner.DrawingAutomation.Wedges._1001;

public sealed class _1001DrawingModule : IDrawingWedgeModule
{
    private static readonly IReadOnlyList<string> OverlayDimensionKeyList =
        Array.AsReadOnly(new[]
        {
            "TD", "TDF", "W", "ISA", "VW", "VR", "VRR", "VRA", "TL",
            "B", "GA", "GD", "GO", "CL", "CD", "BA", "T", "FL", "C",
            "HH", "BR", "FR", "H", "HA", "FNA", "F", "BF", "Y",
            "G", "CGR", "CGD", "VBL", "VBLR", "RA", "RA2"
        });

    private static readonly ViewNames ProductionCustomerViews = new(
        Front: "Drawing View2",
        Side: "Drawing View1",
        Top: "Drawing View3",
        Detail: "Drawing View4",
        Section: "Section View AC-AC");

    private static readonly ViewNames OverlayViews = new(
        Front: "Drawing View5",
        Side: "Drawing View4",
        Top: "Drawing View3",
        Detail: "Drawing View1",
        Section: "Drawing View2");

    private static readonly IReadOnlySet<string> FgDrawingTableKeys = Keys(
        "TD", "TDF", "W", "ISA", "VW", "VR", "VRA", "TL",
        "B", "GA", "GD", "GO", "CL", "CD", "BA", "T", "FL", "C",
        "HH", "BR", "FR", "H", "HA", "FNA", "F", "BF", "Y",
        "G", "CGR", "CGD", "VBL", "RA", "RA2");

    private static readonly IReadOnlySet<string> FgOverlayTableKeys = Keys(
        "TD", "TDF", "W", "ISA", "VW", "VR", "VRA", "TL",
        "B", "GA", "GD", "GO", "CL", "CD", "BA", "T", "FL", "C",
        "HH", "BR", "FR", "H", "HA", "FNA", "F", "BF", "Y",
        "G", "CGR", "CGD", "VBL", "RA", "RA2");

    private static readonly IReadOnlySet<string> PgbProductionTableKeys = Keys(
        "TD", "TDF", "W", "ISA", "VW", "VR", "VRA",
        "TL", "BA", "T", "FL", "VBL");

    private static readonly IReadOnlySet<string> PgbOverlayTableKeys = Keys(
        "TD", "TDF", "W", "ISA", "VW", "VR", "VRA",
        "TL", "BA", "T", "FL", "VBL");

    public _1001DrawingModule()
    {
        Profiles = Array.AsReadOnly(new[]
        {
            DrawingProfileFactory.Create(WedgeType, WedgeSubclass.FG, DrawingType.Production, "1001 FG Production", ProductionCustomerViews, new[] { "SHEET1" }, DrawingViewNames.SecondaryBreaklineViews),
            DrawingProfileFactory.Create(WedgeType, WedgeSubclass.FG, DrawingType.Customer, "1001 FG Customer", ProductionCustomerViews, new[] { "SHEET1" }, DrawingViewNames.SecondaryBreaklineViews),
            DrawingProfileFactory.Create(WedgeType, WedgeSubclass.FG, DrawingType.Overlay, "1001 FG Overlay", OverlayViews, new[] { "OVERLAY" }, DrawingViewNames.NoBreaklineViews),
            DrawingProfileFactory.Create(WedgeType, WedgeSubclass.PGB, DrawingType.Production, "1001 PGB Production", ProductionCustomerViews, new[] { "SHEET1" }, DrawingViewNames.SecondaryBreaklineViews),
            DrawingProfileFactory.Create(WedgeType, WedgeSubclass.PGB, DrawingType.Customer, "1001 PGB Customer", ProductionCustomerViews, new[] { "SHEET1" }, DrawingViewNames.SecondaryBreaklineViews),
            DrawingProfileFactory.Create(WedgeType, WedgeSubclass.PGB, DrawingType.Overlay, "1001 PGB Overlay", OverlayViews, new[] { "OVERLAY" }, DrawingViewNames.NoBreaklineViews)
        });
    }

    public WedgeType WedgeType => global::WAD.Runner.DataManagement.Domain.Wedge.WedgeType._1001;

    public DrawingWedgeBehavior Behavior { get; } = new(
        OverlayMagnificationSourceKey: "T",
        OverlayDimensionKeys: OverlayDimensionKeyList,
        OverlayReferencePointSketch: "std_ref_point_right",
        RepositionPrimaryOverlayViews: false,
        DeleteFrontOverlayViewWhenVrIsZero: false,
        HideVrExtremaWhenOverlayCompressed: false,
        BreaklineTlOverrideMm: 20.0m);

    public IReadOnlyList<DrawingProfile> Profiles { get; }

    public IOverlayViewPositioningRule OverlayPositioningRule { get; } =
        new _1001OverlayViewPositioningRule();

    public IReadOnlyList<IAnnotationRuleCatalog> AnnotationCatalogs { get; } =
        Array.AsReadOnly<IAnnotationRuleCatalog>(new IAnnotationRuleCatalog[]
        {
            new _1001FgProductionAnnotationRules(),
            new _1001FgCustomerAnnotationRules(),
            new _1001FgOverlayAnnotationRules(),
            new _1001PgbProductionAnnotationRules(),
            new _1001PgbOverlayAnnotationRules()
        });

    public IAnnotationWedgeContextResolver AnnotationContextResolver { get; } =
        new _1001AnnotationContextResolver();

    public AnnotationCleanupProfile ResolveAnnotationProfile(
        WedgeSubclass subclass,
        DrawingType drawingType)
    {
        if (subclass == WedgeSubclass.PGB)
        {
            return drawingType == DrawingType.Overlay
                ? AnnotationCleanupProfile._1001PgbOverlay
                : AnnotationCleanupProfile._1001PgbProduction;
        }

        return drawingType switch
        {
            DrawingType.Overlay => AnnotationCleanupProfile._1001FgOverlay,
            DrawingType.Customer => AnnotationCleanupProfile._1001FgCustomer,
            _ => AnnotationCleanupProfile._1001FgProduction
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

        if (IsView(logicalView, DrawingViewNames.Detail))
            return "right_view";

        if (IsView(logicalView, DrawingViewNames.Section))
            return "left_view";

        return "Default";
    }

    public IReadOnlySet<string>? GetAllowedDimensionTableKeys(
        WedgeSubclass subclass,
        DrawingType drawingType)
        => (subclass, drawingType) switch
        {
            (WedgeSubclass.FG, DrawingType.Production) => FgDrawingTableKeys,
            (WedgeSubclass.FG, DrawingType.Customer) => FgDrawingTableKeys,
            (WedgeSubclass.FG, DrawingType.Overlay) => FgOverlayTableKeys,
            (WedgeSubclass.PGB, DrawingType.Production) => PgbProductionTableKeys,
            (WedgeSubclass.PGB, DrawingType.Customer) => PgbProductionTableKeys,
            (WedgeSubclass.PGB, DrawingType.Overlay) => PgbOverlayTableKeys,
            _ => null
        };

    private static bool IsView(
        string logicalView,
        string expected)
        => string.Equals(
            logicalView?.Trim(),
            expected,
            StringComparison.OrdinalIgnoreCase);

    private static IReadOnlySet<string> Keys(
        params string[] values)
        => new HashSet<string>(
            values,
            StringComparer.OrdinalIgnoreCase);
}