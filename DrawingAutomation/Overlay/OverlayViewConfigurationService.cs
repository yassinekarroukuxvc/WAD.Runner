using System;
using System.Collections.Generic;

using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Core;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Overlay;

public static class OverlayViewConfigurationService
{
    public static void Bind(
        DrawingService drawingService,
        DrawingRun run,
        DrawingData drawingData,
        IDictionary<string, string> viewNames)
    {
        if (drawingService is null) throw new ArgumentNullException(nameof(drawingService));
        if (run is null) throw new ArgumentNullException(nameof(run));
        if (drawingData is null) throw new ArgumentNullException(nameof(drawingData));
        if (viewNames is null) throw new ArgumentNullException(nameof(viewNames));

        try
        {
            if (drawingService.Model is not ModelDoc2 model)
            {
                Logger.Warn(
                    "[Overlay/ConfigBind] Drawing model is unavailable; " +
                    "configuration binding was skipped.");
                return;
            }

            var facts = new DrawingWedgeFacts(run.Wedge);
            var hasVw = facts.HasPositiveLength("VW");
            var hasVr = facts.HasPositiveLength("VR") ||
                        facts.HasPositiveLength("VRR");

            BindView(DrawingViewNames.Front, DrawingType.Production);
            BindView(DrawingViewNames.Side, DrawingType.Production);
            BindView(DrawingViewNames.Top, DrawingType.Production);
            BindView(DrawingViewNames.Detail, drawingData.DrawingType);
            BindView(DrawingViewNames.Section, drawingData.DrawingType);

            void BindView(string logicalView, DrawingType effectiveDrawingType)
            {
                if (!viewNames.TryGetValue(logicalView, out var actualViewName) ||
                    string.IsNullOrWhiteSpace(actualViewName))
                {
                    Logger.Info(
                        $"[Overlay/ConfigBind] '{logicalView}' is not present in the view map; skipping.");
                    return;
                }

                if (!DrawingViewConfigBinder.Bind(
                        model,
                        logicalView,
                        actualViewName,
                        run.Wedge.Subclass,
                        effectiveDrawingType,
                        run.WedgeType,
                        hasVw,
                        hasVr))
                {
                    Logger.Warn(
                        "[Overlay/ConfigBind] Could not bind " +
                        $"LogicalView='{logicalView}', ActualView='{actualViewName}', " +
                        $"DrawingType='{effectiveDrawingType}'.");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(
                "[Overlay/ConfigBind] Configuration binding failed; " +
                $"continuing: {ex.Message}");
        }
    }
}
