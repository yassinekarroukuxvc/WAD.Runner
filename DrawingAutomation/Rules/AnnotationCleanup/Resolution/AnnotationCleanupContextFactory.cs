using System;
using System.Collections.Generic;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

public sealed class AnnotationCleanupContextFactory
{
    public AnnotationCleanupContext Create(
        DrawingRun run,
        DrawingData drawingData,
        IDictionary<string, string>? viewNameMap,
        string logPrefix)
    {
        if (run is null) throw new ArgumentNullException(nameof(run));
        if (drawingData is null) throw new ArgumentNullException(nameof(drawingData));

        var wedge = run.Wedge;
        var profile = DrawingProfileResolver.Resolve(run, drawingData);
        var shank = ShankTypeResolver.Resolve(wedge);
        var foot = FootOptionResolver.Resolve(wedge);
        var dimensions = DimensionFactResolver.Resolve(wedge, logPrefix);
        var viewNames = ViewNameResolver.Resolve(viewNameMap);
        var sketches = SketchNameResolver.Resolve(shank);

        Logger.Blue($"[{logPrefix}.Resolve] Profile={profile}, Shank={shank}, Foot={foot}");

        return new AnnotationCleanupContext
        {
            Profile = profile,
            Shank = shank,
            Foot = foot,
            Dimensions = dimensions,
            ViewNames = viewNames,
            Sketches = sketches,
            KAnnotationFullName = null,
            ErdAnnotationFullName = null
        };
    }

    public static AnnotationCleanupContext CreateFromResolvedInputs(
        AnnotationCleanupProfile profile,
        ShankType shank,
        FootOption foot,
        DimensionFacts dimensions,
        AnnotationViewNameMap viewNames,
        string? kAnnotationFullName = null,
        string? erdAnnotationFullName = null)
    {
        return new AnnotationCleanupContext
        {
            Profile = profile,
            Shank = shank,
            Foot = foot,
            Dimensions = dimensions,
            ViewNames = viewNames,
            Sketches = SketchNameResolver.Resolve(shank),
            KAnnotationFullName = kAnnotationFullName,
            ErdAnnotationFullName = erdAnnotationFullName
        };
    }
}
