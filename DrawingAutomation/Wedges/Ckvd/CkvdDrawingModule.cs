using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Overlay.Positioning;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;
using WAD.Runner.DrawingAutomation.Wedges.Ckvd.Annotations;

namespace WAD.Runner.DrawingAutomation.Wedges.Ckvd;

public sealed class CkvdDrawingModule : IDrawingWedgeModule
{
    private static readonly IReadOnlyList<string> OverlayDimensionKeyList =
        Array.AsReadOnly(new[]
        {
            "FL", "FR", "F", "W", "BR", "GD", "GR", "B", "E", "FX", "X",
            "TD", "TDF", "TL"
        });

    private static readonly ViewNames FgProductionViews = new(
        Front: "Drawing View2",
        Side: "Drawing View1",
        Top: "Drawing View3",
        Detail: "Drawing View4",
        Section: "Section View AB-AB");

    private static readonly ViewNames FgOverlayViews = new(
        Front: "Drawing View5",
        Side: "Drawing View4",
        Top: "Drawing View3",
        Detail: "Drawing View1",
        Section: "Drawing View2");

    private static readonly IReadOnlySet<string> FgDrawingTableKeys = Keys(
        "FL", "F", "B", "GD", "GR", "VR", "W", "VW", "FR", "BR", "FX", "X", "E");

    private static readonly IReadOnlySet<string> FgOverlayTableKeys = Keys(
        "TL", "TD", "TDF", "W", "ISA", "K", "BA");

    private static readonly IReadOnlySet<string> PgbProductionTableKeys = Keys(
        "W", "ISA", "T", "FL");

    private static readonly IReadOnlySet<string> PgbOverlayTableKeys = Keys(
        "TL", "TD", "TDF", "W", "ISA", "K", "BA");

    public CkvdDrawingModule()
    {
        Profiles = Array.AsReadOnly(new[]
        {
            DrawingProfileFactory.Create(
                WedgeType,
                WedgeSubclass.FG,
                DrawingType.Production,
                "CKVD FG Production",
                FgProductionViews,
                new[] { "PGB/FG" },
                DrawingViewNames.SecondaryBreaklineViews),

            DrawingProfileFactory.Create(
                WedgeType,
                WedgeSubclass.FG,
                DrawingType.Customer,
                "CKVD FG Customer",
                FgProductionViews,
                new[] { "PGB/FG" },
                DrawingViewNames.SecondaryBreaklineViews),

            DrawingProfileFactory.Create(
                WedgeType,
                WedgeSubclass.FG,
                DrawingType.Overlay,
                "CKVD FG Overlay",
                FgOverlayViews,
                new[] { "OVERLAY" },
                DrawingViewNames.NoBreaklineViews),

            DrawingProfileFactory.Create(
                WedgeType,
                WedgeSubclass.PGB,
                DrawingType.Production,
                "CKVD PGB Production",
                FgProductionViews,
                new[] { "PGB/FG" },
                DrawingViewNames.SecondaryBreaklineViews),

            DrawingProfileFactory.Create(
                WedgeType,
                WedgeSubclass.PGB,
                DrawingType.Overlay,
                "CKVD PGB Overlay",
                FgOverlayViews,
                new[] { "PGB_OVERLAY" },
                DrawingViewNames.NoBreaklineViews)
        });
    }

    public WedgeType WedgeType => global::WAD.Runner.DataManagement.Domain.Wedge.WedgeType.CKVD;

    public DrawingWedgeBehavior Behavior { get; } = new(
        OverlayMagnificationSourceKey: "FL",
        OverlayDimensionKeys: OverlayDimensionKeyList,
        OverlayReferencePointSketch: "ref_point",
        RepositionPrimaryOverlayViews: false,
        DeleteFrontOverlayViewWhenVrIsZero: true,
        HideVrExtremaWhenOverlayCompressed: false);

    public IReadOnlyList<DrawingProfile> Profiles { get; }

    public IOverlayViewPositioningRule OverlayPositioningRule { get; } =
        new CkvdOverlayViewPositioningRule();

    public IReadOnlyList<IAnnotationRuleCatalog> AnnotationCatalogs { get; } =
        Array.AsReadOnly<IAnnotationRuleCatalog>(new IAnnotationRuleCatalog[]
        {
            new CkvdFgProductionAnnotationRules(),
            new CkvdFgCustomerAnnotationRules(),
            new CkvdFgOverlayAnnotationRules(),
            new CkvdPgbProductionAnnotationRules(),
            new CkvdPgbOverlayAnnotationRules()
        });

    public IAnnotationWedgeContextResolver AnnotationContextResolver { get; } =
        new CkvdAnnotationContextResolver();

    public AnnotationCleanupProfile ResolveAnnotationProfile(
        WedgeSubclass subclass,
        DrawingType drawingType)
    {
        if (subclass == WedgeSubclass.PGB)
        {
            return drawingType == DrawingType.Overlay
                ? AnnotationCleanupProfile.CkvdPgbOverlay
                : AnnotationCleanupProfile.CkvdPgbProduction;
        }

        return drawingType switch
        {
            DrawingType.Overlay => AnnotationCleanupProfile.CkvdFgOverlay,
            DrawingType.Customer => AnnotationCleanupProfile.CkvdFgCustomer,
            _ => AnnotationCleanupProfile.CkvdFgProduction
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
            ? "Overlay_non_std_cut"
            : "Overlay";
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
            (WedgeSubclass.PGB, DrawingType.Overlay) => PgbOverlayTableKeys,
            _ => null
        };

    private static bool IsDetail(string logicalView)
        => string.Equals(
            logicalView?.Trim(),
            DrawingViewNames.Detail,
            StringComparison.OrdinalIgnoreCase);

    private static IReadOnlySet<string> Keys(params string[] values)
        => new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
}
