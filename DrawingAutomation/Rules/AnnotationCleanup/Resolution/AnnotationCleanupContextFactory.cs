using System;
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using WAD.Runner.DrawingAutomation.Wedges;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

public sealed class AnnotationCleanupContextFactory
{
    public AnnotationCleanupContext Create(
        DrawingRun run,
        DrawingData drawingData,
        IDictionary<string, string>? viewNameMap,
        string logPrefix)
    {
        if (run is null)
            throw new ArgumentNullException(nameof(run));

        if (drawingData is null)
            throw new ArgumentNullException(nameof(drawingData));

        var wedge = run.Wedge;
        var module = DrawingWedgeModuleRegistry.Get(run.WedgeType);
        var profile = module.ResolveAnnotationProfile(
            wedge.Subclass,
            drawingData.DrawingType);

        var wedgeContext = module.AnnotationContextResolver.Resolve(wedge);
        var dimensions = DimensionFactResolver.Resolve(wedge, logPrefix);
        var viewNames = ViewNameResolver.Resolve(viewNameMap);

        Logger.Blue(
            $"[{logPrefix}.Resolve] " +
            $"Profile={profile}, " +
            $"Shank={Display(wedgeContext.Traits.Get(AnnotationTraitNames.ShankType))}, " +
            $"Foot={Display(wedgeContext.Traits.Get(AnnotationTraitNames.FootOption))}, " +
            $"FeedHole={Display(wedgeContext.Traits.Get(AnnotationTraitNames.FeedHoleType))}, " +
            $"Wed-Type={Display(wedgeContext.Traits.Get(AnnotationTraitNames.WedType))}");

        return new AnnotationCleanupContext
        {
            Profile = profile,
            Traits = wedgeContext.Traits,
            Dimensions = dimensions,
            ViewNames = viewNames,
            Sketches = wedgeContext.Sketches,
            KAnnotationFullName = null,
            ErdAnnotationFullName = null
        };
    }

    private static string Display(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "<none>"
            : value;
}
