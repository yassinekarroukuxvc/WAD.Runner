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
    /*
     * Overlay candidate dimensions.
     *
     * This contains every FG Overlay dimension marked "X" for 4516 in
     * Prod_style_Rev.03, plus the existing overlay/PGB keys that must remain
     * available to the overlay pipeline.
     *
     * GetAllowedDimensionTableKeys(...) performs the final drawing-table
     * filtering, so the FG table itself remains exactly Excel/X-driven.
     */
    private static readonly IReadOnlyList<string> OverlayDimensionKeyList =
        Array.AsReadOnly(new[]
        {
            "B", "BA", "BF", "BR", "C", "CA", "CBL",
            "CD", "CGD", "CGO", "CGR", "CL", "ERD", "ERL",
            "ERW", "F", "FD", "FL", "FLC", "FLER", "FLG",
            "FNA", "FNO", "FR", "FRO", "GA", "GD", "GD1",
            "GO", "GR", "GR1", "H", "HA", "ID", "IDFA",
            "IDTD", "ISA", "MB", "MO", "P", "RA", "RC",
            "T", "T1", "TBF", "TD", "TDF", "TL", "VBL",
            "VBLR", "W", "W2", "W2A", "Y", "VW", "VR",
            "VRR", "VRA", "NR", "HH", "HW", "SW", "G",
            "CBRD", "CBRL"
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

    // ================================================================
    // DIMENSION TABLE KEYS
    // ================================================================

    // FG dimension-table content comes directly from the X marks in
    // Prod_style_Rev.03 for Product_Type 4516.
    private static readonly IReadOnlySet<string> FgProductionDrawingTableKeys =
        Keys(
            "B", "BA", "BF", "BR", "C", "CA", "CBL",
            "CD", "CGD", "CGO", "CGR", "CL", "ERD", "ERL",
            "ERW", "F", "FD", "FL", "FLC", "FLER", "FLG",
            "FNA", "FNO", "FR", "FRO", "GA", "GD", "GD1",
            "GO", "GR", "GR1", "H", "HA", "ID", "IDFA",
            "IDTD", "ISA", "MB", "MO", "P", "RA", "RC",
            "T", "T1", "TBF", "TD", "TDF", "TL", "VBL",
            "VBLR", "W", "W2", "W2A", "Y");

    private static readonly IReadOnlySet<string> FgCustomerDrawingTableKeys =
        Keys(
            "B", "BA", "BF", "BR", "C", "CD", "CGD",
            "CGO", "CGR", "CL", "F", "FL", "FNA", "FR",
            "FRO", "GA", "GD", "GD1", "GO", "GR", "GR1",
            "H", "HA", "ISA", "RA", "T", "T1", "TD",
            "TDF", "TL", "VBL", "VBLR", "W", "W2", "Y");

    private static readonly IReadOnlySet<string> FgOverlayTableKeys =
        Keys(
            "B", "BA", "BF", "BR", "C", "CA", "CBL",
            "CD", "CGD", "CGO", "CGR", "CL", "ERD", "ERL",
            "ERW", "F", "FD", "FL", "FLC", "FLER", "FLG",
            "FNA", "FNO", "FR", "FRO", "GA", "GD", "GD1",
            "GO", "GR", "GR1", "H", "HA", "ID", "IDFA",
            "IDTD", "ISA", "MB", "MO", "P", "RA", "RC",
            "T", "T1", "TBF", "TD", "TDF", "TL", "VBL",
            "VBLR", "W", "W2", "W2A", "Y");

    private static readonly IReadOnlySet<string> PgbProductionTableKeys =
        Keys(
            "TD", "TDF", "W", "ISA", "VW", "VR", "VRA",
            "TL", "BA", "T", "FL", "VBL");

    private static readonly IReadOnlySet<string> PgbOverlayTableKeys =
        Keys(
            "TD", "TDF", "W", "ISA", "VW", "VR", "VRA",
            "TL", "BA", "T", "FL", "VBL");

    // ================================================================
    // CONSTRUCTOR / PROFILES
    // ================================================================

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

    // ================================================================
    // WEDGE
    // ================================================================

    public WedgeType WedgeType =>
        global::WAD.Runner.DataManagement.Domain.Wedge.WedgeType._4516;

    // ================================================================
    // DRAWING BEHAVIOR
    // ================================================================

    public DrawingWedgeBehavior Behavior { get; } = new(
        OverlayMagnificationSourceKey: "FL",

        OverlayDimensionKeys: OverlayDimensionKeyList,
        OverlayReferencePointSketch: "ref_point_right",

        RepositionPrimaryOverlayViews: false,

        DeleteFrontOverlayViewWhenVrIsZero: true,

        HideVrExtremaWhenOverlayCompressed: false,

        BreaklineTlOverrideMm: 18.0m);

    public IReadOnlyList<DrawingProfile> Profiles { get; }

    // ================================================================
    // OVERLAY POSITIONING
    // ================================================================

    public IOverlayViewPositioningRule OverlayPositioningRule { get; } =
        new _4516OverlayViewPositioningRule();

    // ================================================================
    // ANNOTATIONS
    // ================================================================

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

    // ================================================================
    // ANNOTATION PROFILE RESOLUTION
    // ================================================================

    public AnnotationCleanupProfile ResolveAnnotationProfile(
        WedgeSubclass subclass,
        DrawingType drawingType)
    {
        /*
         * 4516 PGB Production and Customer use the same
         * annotation cleanup profile.
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

    // ================================================================
    // REFERENCED CONFIGURATION RESOLUTION
    // ================================================================

    public string? ResolveReferencedConfiguration(
        string logicalView,
        WedgeSubclass subclass,
        DrawingType drawingType,
        bool hasVw,
        bool hasVr)
    {
        /*
         * Production / Customer
         * ---------------------
         * Both use the normal Default model configuration.
         */
        if (drawingType is DrawingType.Production or DrawingType.Customer)
            return "Default";

        if (drawingType != DrawingType.Overlay)
            return null;

        if (IsView(
                logicalView,
                DrawingViewNames.Detail))
        {
            return "right_view";
        }

        if (IsView(
                logicalView,
                DrawingViewNames.Section))
        {
            return "left_view";
        }

        return "Default";
    }

    // ================================================================
    // DIMENSION TABLE FILTERING
    // ================================================================

    public IReadOnlySet<string>? GetAllowedDimensionTableKeys(
        WedgeSubclass subclass,
        DrawingType drawingType)
        => (subclass, drawingType) switch
        {
            (WedgeSubclass.FG, DrawingType.Production) =>
                FgProductionDrawingTableKeys,

            (WedgeSubclass.FG, DrawingType.Customer) =>
                FgCustomerDrawingTableKeys,

            (WedgeSubclass.FG, DrawingType.Overlay) =>
                FgOverlayTableKeys,

            (WedgeSubclass.PGB, DrawingType.Production) =>
                PgbProductionTableKeys,

            (WedgeSubclass.PGB, DrawingType.Overlay) =>
                PgbOverlayTableKeys,

            _ =>
                null
        };

    // ================================================================
    // HELPERS
    // ================================================================

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