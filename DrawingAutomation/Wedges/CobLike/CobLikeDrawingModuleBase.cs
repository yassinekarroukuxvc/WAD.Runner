using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Overlay.Positioning;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using WAD.Runner.DrawingAutomation.Wedges.CobLike.Annotations;

namespace WAD.Runner.DrawingAutomation.Wedges.CobLike;

public abstract class CobLikeDrawingModuleBase : IDrawingWedgeModule
{
    private static readonly IReadOnlyList<string> OverlayDimensionKeyList =
        Array.AsReadOnly(new[]
        {
            "W", "FD", "T", "VBL", "VBLR", "VW", "VR", "VRR", "RA2H", "RA", "RA2",
            "TL", "TD", "TDF", "CGR", "G", "CGD", "FRO", "CR", "RC", "CD", "GR", "GD",
            "B", "MB", "H", "FNO", "FL", "ERL", "ERD", "CBRL", "CBRD", "FLC", "CL",
            "MI", "Y", "ERW", "FLER", "MFL", "BF", "FR", "CBL", "HA", "FNA"
        });

    private static readonly ViewNames FgProductionViews = new(
        Front: "Drawing View17",
        Side: "Drawing View16",
        Top: "Drawing View18",
        Detail: "Drawing View19",
        Section: "Section View AA-AA");

    private static readonly ViewNames FgCustomerViews = new(
        Front: "Drawing View2",
        Side: "Drawing View1",
        Top: "Drawing View3",
        Detail: "Drawing View4",
        Section: "Section View AA-AA");

    private static readonly ViewNames FgOverlayViews = new(
        Front: "COB_FG_Ovl_Front",
        Side: "COB_FG_Ovl_Side",
        Top: "COB_FG_Ovl_Top",
        Detail: "Drawing View3",
        Section: "Drawing View4");

    private static readonly ViewNames PgbProductionViews = new(
        Front: "Drawing View22",
        Side: "Drawing View21",
        Top: "Drawing View23",
        Detail: "Drawing View24",
        Section: "Section View AA-AA");

    private static readonly ViewNames PgbOverlayViews = new(
        Front: "COB_PGB_Ovl_Front",
        Side: "COB_PGB_Ovl_Side",
        Top: "COB_PGB_Ovl_Top",
        Detail: "Drawing View1",
        Section: "Drawing View2");

    private static readonly IReadOnlySet<string> FgProductionTableKeys = Keys(
        "T", "F", "FD", "FL", "H", "VBL", "RC", "CD", "GR", "GD", "B", "BF",
        "G", "VR", "W", "VW", "FR", "BR", "ERW", "TL", "TD", "TDF", "VFL", "Y");

    private static readonly IReadOnlySet<string> FgCustomerTableKeys = Keys(
        "T", "H", "VBL", "B", "BF", "G", "VR", "W", "VW", "W2", "FR", "BR",
        "TD", "TDF", "TL");

    private static readonly IReadOnlySet<string> FgOverlayTableKeys = Keys(
        "TL", "TD", "TDF", "W", "ISA", "K", "RA", "T", "BA");

    private static readonly IReadOnlySet<string> PgbProductionTableKeys = Keys(
        "W", "ISA", "T", "FD");

    private static readonly IReadOnlySet<string> PgbOverlayTableKeys = Keys(
        "TL", "TD", "TDF", "W", "ISA", "K", "BA");

    protected CobLikeDrawingModuleBase(
        WedgeType wedgeType,
        string displayName)
    {
        WedgeType = wedgeType;
        Profiles = BuildProfiles(wedgeType, displayName);
    }

    public WedgeType WedgeType { get; }

    public DrawingWedgeBehavior Behavior { get; } = new(
        OverlayMagnificationSourceKey: "T",
        OverlayDimensionKeys: OverlayDimensionKeyList,
        OverlayReferencePointSketch: "ref_point_sketch",
        RepositionPrimaryOverlayViews: false,
        DeleteFrontOverlayViewWhenVrIsZero: false,
        HideVrExtremaWhenOverlayCompressed: true);

    public IReadOnlyList<DrawingProfile> Profiles { get; }

    public IOverlayViewPositioningRule OverlayPositioningRule { get; } =
        new CobLikeOverlayViewPositioningRule();

    public IReadOnlyList<IAnnotationRuleCatalog> AnnotationCatalogs { get; } =
        Array.AsReadOnly<IAnnotationRuleCatalog>(new IAnnotationRuleCatalog[]
        {
            new CobLikeProductionAnnotationRules(),
            new CobLikeCustomerAnnotationRules(),
            new CobLikeOverlayAnnotationRules(),
            new PgbProductionAnnotationRules(),
            new PgbOverlayAnnotationRules()
        });

    public AnnotationCleanupProfile ResolveAnnotationProfile(
        WedgeSubclass subclass,
        DrawingType drawingType)
    {
        if (subclass == WedgeSubclass.PGB)
        {
            return drawingType == DrawingType.Overlay
                ? AnnotationCleanupProfile.PgbOverlay
                : AnnotationCleanupProfile.PgbProduction;
        }

        return drawingType switch
        {
            DrawingType.Overlay => AnnotationCleanupProfile.CobLikeOverlay,
            DrawingType.Customer => AnnotationCleanupProfile.CobLikeCustomer,
            _ => AnnotationCleanupProfile.CobLikeProduction
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
            return "std_cut";

        if (drawingType != DrawingType.Overlay)
            return null;

        return IsDetail(logicalView) && hasVw && hasVr
            ? "non_std_cut"
            : "std_cut";
    }

    public IReadOnlySet<string>? GetAllowedDimensionTableKeys(
        WedgeSubclass subclass,
        DrawingType drawingType)
        => (subclass, drawingType) switch
        {
            (WedgeSubclass.FG, DrawingType.Production) => FgProductionTableKeys,
            (WedgeSubclass.FG, DrawingType.Customer) => FgCustomerTableKeys,
            (WedgeSubclass.FG, DrawingType.Overlay) => FgOverlayTableKeys,
            (WedgeSubclass.PGB, DrawingType.Production) => PgbProductionTableKeys,
            (WedgeSubclass.PGB, DrawingType.Overlay) => PgbOverlayTableKeys,
            _ => null
        };

    private static IReadOnlyList<DrawingProfile> BuildProfiles(
        WedgeType wedgeType,
        string displayName)
        => Array.AsReadOnly(new[]
        {
            DrawingProfileFactory.Create(
                wedgeType,
                WedgeSubclass.FG,
                DrawingType.Production,
                $"{displayName} FG Production",
                FgProductionViews,
                new[] { "PRODUCTION", "PRODCUTION" }),

            DrawingProfileFactory.Create(
                wedgeType,
                WedgeSubclass.FG,
                DrawingType.Customer,
                $"{displayName} FG Customer",
                FgCustomerViews,
                new[] { "CUSTOMER" }),

            DrawingProfileFactory.Create(
                wedgeType,
                WedgeSubclass.FG,
                DrawingType.Overlay,
                $"{displayName} FG Overlay",
                FgOverlayViews,
                new[] { "FG" },
                DrawingViewNames.NoBreaklineViews),

            DrawingProfileFactory.Create(
                wedgeType,
                WedgeSubclass.PGB,
                DrawingType.Production,
                $"{displayName} PGB Production",
                PgbProductionViews,
                new[] { "PGB" }),

            DrawingProfileFactory.Create(
                wedgeType,
                WedgeSubclass.PGB,
                DrawingType.Overlay,
                $"{displayName} PGB Overlay",
                PgbOverlayViews,
                new[] { "PGB" },
                DrawingViewNames.NoBreaklineViews)
        });

    private static bool IsDetail(string logicalView)
        => string.Equals(
            logicalView?.Trim(),
            DrawingViewNames.Detail,
            StringComparison.OrdinalIgnoreCase);

    private static IReadOnlySet<string> Keys(params string[] values)
        => new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
}
