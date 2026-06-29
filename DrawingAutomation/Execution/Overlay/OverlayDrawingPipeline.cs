using System;
using System.Collections.Generic;

using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation;
using WAD.Runner.DrawingAutomation.Common.Overlay;
using WAD.Runner.DrawingAutomation.Core;
using WAD.Runner.DrawingAutomation.Overlay;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Execution.Overlay;


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
        var drawingService = OverlaySheetHelper.OpenRelinkAndPrepareOverlaySheet(
            context.SwApp,
            run,
            drawingData,
            out var viewNames);

        BindReferencedConfigurations(drawingService, run, drawingData, viewNames, isCkvd);

        Logger.Info("[Overlay/3] Compute magnification, calibration and payload...");
        var (layoutContext, overlayMagnification, overlayCalibrationUm) =
            OverlayMagnificationService.ComputeOverlayMagCal(run, drawingData);

        var overlayKeys = OverlayMagnificationService.DefaultOverlayDimKeys(run.WedgeType);
        var overlayPayload = OverlayPayloadBuilder.BuildOverlayPayload(run, drawingData, overlayKeys);

        drawingService.Rebuild();

        Logger.Info("[Overlay/4] Apply overlay view scales and positions...");
        OverlayViewScaler.ApplyOverlayViewScales(drawingService, viewNames, overlayMagnification);
        OverlayViewScaler.TryRepositionAllOverlayViews(context.SwApp, drawingService, run, viewNames);

        if (isCkvd)
        {
            Logger.Info("[Overlay/5] Apply CKVD overlay view cleanup...");
            OverlayViewScaler.DeleteFrontViewIfVrZero(drawingService, viewNames, layoutContext);
        }

        drawingService.Rebuild();
        drawingService.ZoomToSheet();

        Logger.Info("[Overlay/6] Plan overlay dimensions and create table...");
        var (dims, plans) = OverlayAnnotationHelper.PlanOverlayDimensions(
            layoutContext,
            run.WedgeType,
            context.PlannedOverlayDimensions);

        OverlayAnnotationHelper.TryCreateOverlayDimTable(
            context.SwApp,
            drawingService,
            drawingData,
            overlayPayload);

        Logger.Info("[Overlay/7] Apply overlay annotations and metadata...");
        OverlayAnnotationHelper.TryApplyAnnotationPositions(drawingService, viewNames, run, drawingData, plans);
        OverlayAnnotationHelper.TryApplyOverlayMetadata(drawingService, drawingData, run);
        OverlayAnnotationHelper.TryCleanupZeroDims(drawingService, viewNames, layoutContext, drawingData, dims, run);

        Logger.Info("[Overlay/8] Draw calibration box/note and export TIFF...");
        OverlayAnnotationHelper.TryCalibrationBoxAndNote(drawingService, overlayMagnification, overlayCalibrationUm);
        OverlayTiffExporter.ExportOverlayTiff(context.SwApp, drawingService, run);
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
            OverlayAnnotationHelper.TryBindOverlayViewConfigurations(drawingService, run, viewNames);
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
