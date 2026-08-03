using System;
using System.Collections.Generic;
using System.IO;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Planning;

using WAD.Runner.DrawingAutomation.Overlay;
using WAD.Runner.DrawingAutomation.Overlay.Positioning;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.SolidWorks;
using WAD.Runner.DrawingAutomation.Views;
using WAD.Runner.DrawingAutomation.Wedges;

namespace WAD.Runner.DrawingAutomation.Overlay
{
    /// <summary>
    /// Applies overlay scales and executes the positioning plan selected by
    /// the wedge module's overlay positioning rule.
    ///
    /// Wedge-specific reference-point and coordinate rules do not belong here.
    /// Add or modify an IOverlayViewPositioningRule instead.
    /// </summary>
    public static class OverlayViewScaler
    {
        public static void ApplyOverlayViewScales(
            DrawingService ds,
            IDictionary<string, string> nameMap,
            double overlayMag)
        {
            if (ds is null)
                throw new ArgumentNullException(nameof(ds));

            if (nameMap is null)
                throw new ArgumentNullException(nameof(nameMap));

            try
            {
                var drawing = ds.Drawing;
                if (drawing is null)
                {
                    Logger.Warn(
                        "[Overlay] ApplyOverlayViewScales: drawing is null.");

                    return;
                }

                nameMap.TryGetValue(
                    DrawingViewNames.Front,
                    out var frontName);

                nameMap.TryGetValue(
                    DrawingViewNames.Side,
                    out var sideName);

                nameMap.TryGetValue(
                    DrawingViewNames.Top,
                    out var topName);

                nameMap.TryGetValue(
                    DrawingViewNames.Detail,
                    out var detailName);

                nameMap.TryGetValue(
                    DrawingViewNames.Section,
                    out var sectionName);

                double detailSectionScale =
                    OverlayMagnificationService.GetViewScale(
                        overlayMag);

                object viewObject =
                    drawing.GetFirstView();

                while (viewObject is not null)
                {
                    if (viewObject is not View view)
                        break;

                    string viewName =
                        view.Name ?? string.Empty;

                    if (NamesMatch(viewName, frontName) ||
                        NamesMatch(viewName, sideName) ||
                        NamesMatch(viewName, topName))
                    {
                        view.ScaleDecimal =
                            OverlayViewScaleDefaults.PrimaryViewScale;

                        Logger.Info(
                            $"[Overlay] Set primary view '{viewName}' " +
                            $"ScaleDecimal=" +
                            $"{OverlayViewScaleDefaults.PrimaryViewScale:0.####}.");
                    }

                    if (NamesMatch(viewName, detailName) ||
                        NamesMatch(viewName, sectionName))
                    {
                        view.ScaleDecimal =
                            detailSectionScale;

                        Logger.Info(
                            $"[Overlay] Set view '{viewName}' scale from " +
                            $"overlayMag={overlayMag:0.####}X -> " +
                            $"ScaleDecimal={detailSectionScale:0.####}.");
                    }

                    viewObject =
                        view.GetNextView();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(
                    $"[Overlay] ApplyOverlayViewScales failed: " +
                    $"{ex.Message}");
            }
        }

        public static void TryRepositionAllOverlayViews(
            SldWorks swApp,
            DrawingService ds,
            DrawingRun run,
            IDictionary<string, string> nameMap,
            double overlayMagnification)
        {
            if (swApp is null)
                throw new ArgumentNullException(nameof(swApp));

            if (ds is null)
                throw new ArgumentNullException(nameof(ds));

            if (run is null)
                throw new ArgumentNullException(nameof(run));

            if (nameMap is null)
                throw new ArgumentNullException(nameof(nameMap));

            try
            {
                string macroPath =
                    GetOverlayMacroPath();

                var context =
                    new OverlayViewPositioningContext(
                        run,
                        overlayMagnification);

                IOverlayViewPositioningRule rule =
                    DrawingWedgeModuleRegistry
                        .Get(run.WedgeType)
                        .OverlayPositioningRule;

                IReadOnlyList<OverlayViewPlacement> placements =
                    rule.BuildPlacements(context)
                    ?? throw new InvalidOperationException(
                        $"Positioning rule '{rule.Name}' returned null.");

                ValidatePlacements(
                    rule,
                    placements);

                Logger.Info(
                    $"[Overlay] Selected positioning rule " +
                    $"'{rule.Name}' for wedge type " +
                    $"'{run.WedgeType}'. " +
                    $"Placements={placements.Count}.");

                foreach (var placement in placements)
                {
                    Logger.Info(
                        $"[Overlay] Positioning " +
                        $"'{placement.LogicalViewName}' with reference " +
                        $"'{placement.ReferencePointName}' at " +
                        $"X={placement.XIn:0.####} in, " +
                        $"Y={placement.YIn:0.####} in.");

                    OverlayViewMacroPositioner
                        .RunIfAvailable(
                            swApp,
                            macroFile: macroPath,
                            logicalViewName:
                                placement.LogicalViewName,
                            referencePointName:
                                placement.ReferencePointName,
                            xIn:
                                placement.XIn,
                            yIn:
                                placement.YIn,
                            logicalToActual:
                                nameMap);
                }

                ds.Rebuild();
            }
            catch (Exception ex)
            {
                Logger.Warn(
                    "[Overlay] Reposition-after-scaling step failed " +
                    $"(continuing): {ex.Message}");
            }
        }

        public static void DeleteFrontViewIfVrZero(
            DrawingService ds,
            IDictionary<string, string> nameMap,
            LayoutContext ctx)
        {
            if (ds is null)
                throw new ArgumentNullException(nameof(ds));

            if (nameMap is null)
                throw new ArgumentNullException(nameof(nameMap));

            if (ctx is null)
                throw new ArgumentNullException(nameof(ctx));

            try
            {
                double vr =
                    LayoutMath.Dmm(
                        ctx,
                        "VR");

                if (!double.IsFinite(vr))
                {
                    Logger.Info(
                        "[Overlay] VR missing/invalid; keeping Front view.");

                    return;
                }

                if (Math.Abs(vr) >
                    OverlayPositioningConstants.ZeroTolerance)
                {
                    Logger.Info(
                        $"[Overlay] VR={vr:0.####} mm; " +
                        "keeping Front view.");

                    return;
                }

                var model =
                    ds.Model;

                if (model is null)
                {
                    Logger.Warn(
                        "[Overlay] DeleteFrontViewIfVrZero: " +
                        "model is null.");

                    return;
                }

                if (!nameMap.TryGetValue(
                        DrawingViewNames.Front,
                        out var frontName) ||
                    string.IsNullOrWhiteSpace(frontName))
                {
                    frontName =
                        DrawingViewNames.Front;
                }

                model.ClearSelection2(true);

                bool selected =
                    model.Extension.SelectByID2(
                        frontName,
                        "DRAWINGVIEW",
                        0,
                        0,
                        0,
                        false,
                        0,
                        null,
                        0);

                if (!selected)
                {
                    Logger.Warn(
                        $"[Overlay] Could not select Front view " +
                        $"'{frontName}' for deletion.");

                    return;
                }

                bool deleted =
                    model.Extension.DeleteSelection2(
                        (int)swDeleteSelectionOptions_e
                            .swDelete_Absorbed);

                if (deleted)
                {
                    Logger.Info(
                        $"[Overlay] Front view '{frontName}' deleted " +
                        "because VR=0.");
                }
                else
                {
                    Logger.Warn(
                        $"[Overlay] VR=0 but deletion of Front view " +
                        $"'{frontName}' failed.");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(
                    $"[Overlay] DeleteFrontViewIfVrZero failed: " +
                    $"{ex.Message}");
            }
        }

        private static void ValidatePlacements(
            IOverlayViewPositioningRule rule,
            IReadOnlyList<OverlayViewPlacement> placements)
        {
            var logicalViewNames =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            for (int index = 0;
                 index < placements.Count;
                 index++)
            {
                var placement =
                    placements[index]
                    ?? throw new InvalidOperationException(
                        $"Positioning rule '{rule.Name}' returned a " +
                        $"null placement at index {index}.");

                if (string.IsNullOrWhiteSpace(
                        placement.LogicalViewName))
                {
                    throw new InvalidOperationException(
                        $"Positioning rule '{rule.Name}' returned a " +
                        $"placement without a logical view name at " +
                        $"index {index}.");
                }

                if (string.IsNullOrWhiteSpace(
                        placement.ReferencePointName))
                {
                    throw new InvalidOperationException(
                        $"Positioning rule '{rule.Name}' returned a " +
                        $"placement without a reference point for " +
                        $"view '{placement.LogicalViewName}'.");
                }

                if (!double.IsFinite(placement.XIn) ||
                    !double.IsFinite(placement.YIn))
                {
                    throw new InvalidOperationException(
                        $"Positioning rule '{rule.Name}' returned " +
                        $"invalid coordinates for view " +
                        $"'{placement.LogicalViewName}'.");
                }

                if (!logicalViewNames.Add(
                        placement.LogicalViewName))
                {
                    throw new InvalidOperationException(
                        $"Positioning rule '{rule.Name}' returned " +
                        $"more than one placement for view " +
                        $"'{placement.LogicalViewName}'.");
                }
            }
        }

        private static string GetOverlayMacroPath()
        {
            string baseDirectory =
                AppContext.BaseDirectory
                ?? string.Empty;

            string outputCandidate =
                Path.GetFullPath(
                    Path.Combine(
                        baseDirectory,
                        "Resources",
                        "Macros",
                        "OverlayMacro.swp"));

            if (File.Exists(outputCandidate))
            {
                Logger.Info(
                    $"[Overlay] Using macro from output folder: " +
                    $"{outputCandidate}");

                return outputCandidate;
            }

            string projectCandidate =
                Path.GetFullPath(
                    Path.Combine(
                        baseDirectory,
                        "..",
                        "..",
                        "..",
                        "Resources",
                        "Macros",
                        "OverlayMacro.swp"));

            if (File.Exists(projectCandidate))
            {
                Logger.Info(
                    $"[Overlay] Using macro from project folder: " +
                    $"{projectCandidate}");

                return projectCandidate;
            }

            Logger.Warn(
                "[Overlay] OverlayMacro.swp not found. " +
                $"Tried '{outputCandidate}' and " +
                $"'{projectCandidate}'.");

            return outputCandidate;
        }

        private static bool NamesMatch(
            string? first,
            string? second)
        {
            return
                !string.IsNullOrWhiteSpace(first) &&
                !string.IsNullOrWhiteSpace(second) &&
                string.Equals(
                    first.Trim(),
                    second.Trim(),
                    StringComparison.OrdinalIgnoreCase);
        }
    }
}
