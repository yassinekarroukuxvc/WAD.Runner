using System;
using System.Collections.Generic;

using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation;
using WAD.Runner.DrawingAutomation.Common;
using WAD.Runner.DrawingAutomation.Core;
using WAD.Runner.DrawingAutomation.Overlay;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Execution.Overlay;

/// <summary>
/// Overlay drawing workflow.
///
/// This pipeline handles overlay sheet preparation, overlay view scales,
/// overlay annotation positions, overlay tables, calibration note/box, and TIFF export.
/// It deliberately contains no model feature handling.
/// </summary>
public sealed class OverlayDrawingPipeline : IDrawingPipeline
{
    public bool CanHandle(DrawingAutomationContext context)
        => context.DrawingData.DrawingType == DrawingType.Overlay;

    public void Run(DrawingAutomationContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        var run = context.Run;
        var drawingData = context.DrawingData;
        var isCkvd = run.WedgeType == WedgeType.CKVD;

        Logger.Info($"=== WAD Overlay Drawing ▶ {run.Wedge.Subclass}/Overlay | {run.WedgeType} ===");

        Logger.Info("[Overlay/1] Ensure model phase has completed...");
        _ = context.RunPartAutomation();

        Logger.Info("[Overlay/2] Open, relink and prepare overlay sheet...");
        var drawingService = OverlayDrawingExecutorCommon.OpenRelinkAndPrepareOverlaySheet(
            context.SwApp,
            run,
            drawingData,
            out var viewNames);

        BindReferencedConfigurations(drawingService, run, drawingData, viewNames, isCkvd);

        Logger.Info("[Overlay/3] Compute magnification, calibration and payload...");
        var (layoutContext, overlayMagnification, overlayCalibrationUm) =
            OverlayDrawingExecutorCommon.ComputeOverlayMagCal(run, drawingData);

        var overlayKeys = OverlayDrawingExecutorCommon.DefaultOverlayDimKeys(run.WedgeType);
        var overlayPayload = OverlayDrawingExecutorCommon.BuildOverlayPayload(run, drawingData, overlayKeys);

        drawingService.Rebuild();

        Logger.Info("[Overlay/4] Apply overlay view scales and positions...");
        OverlayDrawingExecutorCommon.ApplyOverlayViewScales(drawingService, viewNames, overlayMagnification);
        OverlayDrawingExecutorCommon.TryRepositionAllOverlayViews(context.SwApp, drawingService, run, viewNames);

        if (isCkvd)
        {
            Logger.Info("[Overlay/5] Apply CKVD overlay view cleanup...");
            OverlayDrawingExecutorCommon.DeleteFrontViewIfVrZero(drawingService, viewNames, layoutContext);
        }

        drawingService.Rebuild();
        drawingService.ZoomToSheet();

        Logger.Info("[Overlay/6] Plan overlay dimensions and create table...");
        var (dims, plans) = OverlayDrawingExecutorCommon.PlanOverlayDimensions(
            layoutContext,
            run.WedgeType,
            context.PlannedOverlayDimensions);

        OverlayDrawingExecutorCommon.TryCreateOverlayDimTable(
            context.SwApp,
            drawingService,
            drawingData,
            overlayPayload);

        Logger.Info("[Overlay/7] Apply overlay annotations and metadata...");
        OverlayDrawingExecutorCommon.TryApplyAnnotationPositions(drawingService, viewNames, run, drawingData, plans);
        OverlayDrawingExecutorCommon.TryApplyOverlayMetadata(drawingService, drawingData, run);
        OverlayDrawingExecutorCommon.TryCleanupZeroDims(drawingService, viewNames, layoutContext, drawingData, dims, run);

        Logger.Info("[Overlay/8] Draw calibration box/note and export TIFF...");
        OverlayDrawingExecutorCommon.TryCalibrationBoxAndNote(drawingService, overlayMagnification, overlayCalibrationUm);
        OverlayDrawingExecutorCommon.ExportOverlayTiff(context.SwApp, drawingService, run);
    }

    private static void BindReferencedConfigurations(
        DrawingService drawingService,
        DrawingRun run,
        DrawingData drawingData,
        IDictionary<string, string> viewNames,
        bool isCkvd)
    {
        if (isCkvd && run.Wedge.Subclass == WedgeSubclass.PGB)
        {
            TryBindPgbOverlayConfigs(drawingService, viewNames, run, drawingData);
            return;
        }

        if (!isCkvd)
        {
            OverlayDrawingExecutorCommon.TryBindOverlayViewConfigurations(drawingService, run, viewNames);
            return;
        }

        Logger.Info("[Overlay] CKVD FG does not require explicit view configuration binding.");
    }

    private static void TryBindPgbOverlayConfigs(
        DrawingService drawingService,
        IDictionary<string, string> viewNames,
        DrawingRun run,
        DrawingData drawingData)
    {
        try
        {
            if (drawingService.Model is not ModelDoc2 model) return;

            viewNames.TryGetValue("Front", out var frontViewName);
            viewNames.TryGetValue("Side", out var sideViewName);
            viewNames.TryGetValue("Top", out var topViewName);
            viewNames.TryGetValue("Detail", out var detailViewName);
            viewNames.TryGetValue("Section", out var sectionViewName);

            if (!string.IsNullOrWhiteSpace(frontViewName))
                DrawingViewConfigBinder.SetReferencedConfigurationForView(model, frontViewName, run.Wedge.Subclass, DrawingType.Production);
            if (!string.IsNullOrWhiteSpace(sideViewName))
                DrawingViewConfigBinder.SetReferencedConfigurationForView(model, sideViewName, run.Wedge.Subclass, DrawingType.Production);
            if (!string.IsNullOrWhiteSpace(topViewName))
                DrawingViewConfigBinder.SetReferencedConfigurationForView(model, topViewName, run.Wedge.Subclass, DrawingType.Production);
            if (!string.IsNullOrWhiteSpace(detailViewName))
                DrawingViewConfigBinder.SetReferencedConfigurationForView(model, detailViewName, run.Wedge.Subclass, drawingData.DrawingType);
            if (!string.IsNullOrWhiteSpace(sectionViewName))
                DrawingViewConfigBinder.SetReferencedConfigurationForView(model, sectionViewName, run.Wedge.Subclass, drawingData.DrawingType);
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Overlay/PGB] View configuration binding failed, continuing: {ex.Message}");
        }
    }
}
