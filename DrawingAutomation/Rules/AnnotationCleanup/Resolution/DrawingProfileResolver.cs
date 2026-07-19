using System;

using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Core;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

public static class DrawingProfileResolver
{
    public static AnnotationCleanupProfile Resolve(DrawingRun run, DrawingData drawingData)
    {
        if (run is null) throw new ArgumentNullException(nameof(run));
        if (drawingData is null) throw new ArgumentNullException(nameof(drawingData));

        var isOverlay = drawingData.DrawingType == DrawingType.Overlay;
        var isCustomer = drawingData.DrawingType == DrawingType.Customer;
        var isPgb = run.Wedge.Subclass == WedgeSubclass.PGB;
        var family = DrawingWedgeBehaviorCatalog.Get(run.WedgeType).Family;

        return family switch
        {
            DrawingWedgeFamily.Ckvd => ResolveCkvd(isPgb, isOverlay, isCustomer),
            DrawingWedgeFamily.Osg7 => ResolveOsg7(isPgb, isOverlay, isCustomer),
            DrawingWedgeFamily.CobLike => ResolveCobLike(isPgb, isOverlay, isCustomer),
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown drawing wedge family.")
        };
    }

    private static AnnotationCleanupProfile ResolveCobLike(
        bool isPgb,
        bool isOverlay,
        bool isCustomer)
    {
        if (isPgb && isOverlay) return AnnotationCleanupProfile.PgbOverlay;
        if (isPgb) return AnnotationCleanupProfile.PgbProduction;
        if (isOverlay) return AnnotationCleanupProfile.CobLikeOverlay;
        if (isCustomer) return AnnotationCleanupProfile.CobLikeCustomer;
        return AnnotationCleanupProfile.CobLikeProduction;
    }

    private static AnnotationCleanupProfile ResolveCkvd(
        bool isPgb,
        bool isOverlay,
        bool isCustomer)
    {
        if (isPgb && isOverlay) return AnnotationCleanupProfile.CkvdPgbOverlay;
        if (isPgb) return AnnotationCleanupProfile.CkvdPgbProduction;
        if (isOverlay) return AnnotationCleanupProfile.CkvdFgOverlay;
        if (isCustomer) return AnnotationCleanupProfile.CkvdFgCustomer;
        return AnnotationCleanupProfile.CkvdFgProduction;
    }

    private static AnnotationCleanupProfile ResolveOsg7(
        bool isPgb,
        bool isOverlay,
        bool isCustomer)
    {
        if (isPgb && isOverlay) return AnnotationCleanupProfile.Osg7PgbOverlay;
        if (isPgb) return AnnotationCleanupProfile.Osg7PgbProduction;
        if (isOverlay) return AnnotationCleanupProfile.Osg7FgOverlay;
        if (isCustomer) return AnnotationCleanupProfile.Osg7FgCustomer;
        return AnnotationCleanupProfile.Osg7FgProduction;
    }
}
