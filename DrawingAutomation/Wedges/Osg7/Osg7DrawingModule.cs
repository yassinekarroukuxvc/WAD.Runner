using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Overlay.Positioning;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using WAD.Runner.DrawingAutomation.Wedges.Osg7.Annotations;

namespace WAD.Runner.DrawingAutomation.Wedges.Osg7;

public sealed class Osg7DrawingModule : IDrawingWedgeModule
{
    private static readonly IReadOnlyList<string> OverlayDimensionKeyList =
        Array.AsReadOnly(new[]
        {
            "FL", "FR", "F", "W", "BR", "GD", "GR", "B", "E", "FX", "X",
            "TD", "TDF", "TL"
        });

    private static readonly ViewNames FgViews = new(
        Front: "Drawing View19",
        Side: "Drawing View17",
        Top: "Drawing View18",
        Detail: "Drawing View20",
        Section: "Section View AC-AC");

    private static readonly ViewNames FgOverlayViews = new(
        Front: "OSG7_FG_Ovl_Front",
        Side: "OSG7_FG_Ovl_Side",
        Top: "OSG7_FG_Ovl_Top",
        Detail: "Drawing View3",
        Section: "Drawing View4");

    private static readonly ViewNames PgbProductionViews = new(
        Front: "Drawing View14",
        Side: "Drawing View12",
        Top: "Drawing View13",
        Detail: "Drawing View15",
        Section: "Section View AD-AD");

    private static readonly ViewNames PgbOverlayViews = new(
        Front: "OSG7_PGB_Ovl_Front",
        Side: "OSG7_PGB_Ovl_Side",
        Top: "OSG7_PGB_Ovl_Top",
        Detail: "Drawing View5",
        Section: "Drawing View6");

    private static readonly IReadOnlySet<string> FgDrawingTableKeys = Keys(
        "FL", "F", "B", "GD", "GR", "VR", "W", "VW", "FR", "BR", "FX", "X",
        "FRX", "BRX", "VFL", "TD", "TDF", "TL");

    private static readonly IReadOnlySet<string> FgOverlayTableKeys = Keys(
        "TL", "TD", "TDF", "W", "ISA", "K", "BA", "FA", "VFL");

    private static readonly IReadOnlySet<string> PgbProductionTableKeys = Keys(
        "W", "ISA", "T", "FL", "TD", "TDF", "TL");

    private static readonly IReadOnlySet<string> PgbOverlayTableKeys = Keys(
        "TL", "TD", "TDF", "W", "ISA", "K", "BA");

    public Osg7DrawingModule()
    {
        Profiles = Array.AsReadOnly(new[]
        {
            DrawingProfileFactory.Create(
                WedgeType,
                WedgeSubclass.FG,
                DrawingType.Production,
                "OSG7 FG Production",
                FgViews,
                new[] { "CUSTOMER" }),

            DrawingProfileFactory.Create(
                WedgeType,
                WedgeSubclass.FG,
                DrawingType.Customer,
                "OSG7 FG Customer",
                FgViews,
                new[] { "CUSTOMER" }),

            DrawingProfileFactory.Create(
                WedgeType,
                WedgeSubclass.FG,
                DrawingType.Overlay,
                "OSG7 FG Overlay",
                FgOverlayViews,
                new[] { "FG" },
                DrawingViewNames.NoBreaklineViews),

            DrawingProfileFactory.Create(
                WedgeType,
                WedgeSubclass.PGB,
                DrawingType.Production,
                "OSG7 PGB Production",
                PgbProductionViews,
                new[] { "PGB" }),

            DrawingProfileFactory.Create(
                WedgeType,
                WedgeSubclass.PGB,
                DrawingType.Overlay,
                "OSG7 PGB Overlay",
                PgbOverlayViews,
                new[] { "PGB_OVERLAY" },
                DrawingViewNames.NoBreaklineViews)
        });
    }

    public WedgeType WedgeType => global::WAD.Runner.DataManagement.Domain.Wedge.WedgeType.OSG7;

    public DrawingWedgeBehavior Behavior { get; } = new(
        OverlayMagnificationSourceKey: "FL",
        OverlayDimensionKeys: OverlayDimensionKeyList,
        OverlayReferencePointSketch: "ref_point",
        RepositionPrimaryOverlayViews: false,
        DeleteFrontOverlayViewWhenVrIsZero: false,
        HideVrExtremaWhenOverlayCompressed: false,
        BreaklineTlOverrideMm: 63.5m);

    public IReadOnlyList<DrawingProfile> Profiles { get; }

    public IOverlayViewPositioningRule OverlayPositioningRule { get; } =
        new Osg7OverlayViewPositioningRule();

    public IReadOnlyList<IAnnotationRuleCatalog> AnnotationCatalogs { get; } =
        Array.AsReadOnly<IAnnotationRuleCatalog>(new IAnnotationRuleCatalog[]
        {
            new Osg7FgProductionAnnotationRules(),
            new Osg7FgCustomerAnnotationRules(),
            new Osg7FgOverlayAnnotationRules(),
            new Osg7PgbProductionAnnotationRules(),
            new Osg7PgbOverlayAnnotationRules()
        });

    public AnnotationCleanupProfile ResolveAnnotationProfile(
        WedgeSubclass subclass,
        DrawingType drawingType)
    {
        if (subclass == WedgeSubclass.PGB)
        {
            return drawingType == DrawingType.Overlay
                ? AnnotationCleanupProfile.Osg7PgbOverlay
                : AnnotationCleanupProfile.Osg7PgbProduction;
        }

        return drawingType switch
        {
            DrawingType.Overlay => AnnotationCleanupProfile.Osg7FgOverlay,
            DrawingType.Customer => AnnotationCleanupProfile.Osg7FgCustomer,
            _ => AnnotationCleanupProfile.Osg7FgProduction
        };
    }

    public string? ResolveReferencedConfiguration(
        string logicalView,
        WedgeSubclass subclass,
        DrawingType drawingType,
        bool hasVw,
        bool hasVr)
        => subclass switch
        {
            WedgeSubclass.FG => "FG",
            WedgeSubclass.PGB => "PGB",
            _ => null
        };

    public IReadOnlySet<string>? GetAllowedDimensionTableKeys(
        WedgeSubclass subclass,
        DrawingType drawingType)
        => (subclass, drawingType) switch
        {
            (WedgeSubclass.FG, DrawingType.Production) => FgDrawingTableKeys,
            (WedgeSubclass.FG, DrawingType.Customer) => FgDrawingTableKeys,
            (WedgeSubclass.FG, DrawingType.Overlay) => FgOverlayTableKeys,
            (WedgeSubclass.PGB, DrawingType.Production) => PgbProductionTableKeys,
            (WedgeSubclass.PGB, DrawingType.Overlay) => PgbOverlayTableKeys,
            _ => null
        };

    private static IReadOnlySet<string> Keys(params string[] values)
        => new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
}
