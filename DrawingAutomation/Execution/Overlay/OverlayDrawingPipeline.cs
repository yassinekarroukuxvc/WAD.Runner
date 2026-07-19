using System;
using System.Collections.Generic;

using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
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
        var behavior = DrawingWedgeBehaviorCatalog.Get(run.WedgeType);
        DrawingService? drawingService = null;

        Logger.Info(
            $"=== WAD Overlay Drawing ▶ {run.Wedge.Subclass}/Overlay | {run.WedgeType} ===");

        try
        {
            Logger.Info("[Overlay/1] Ensure model phase has completed...");
            _ = context.RunPartAutomation();
            DrawingRunValidator.EnsureGeneratedPartExists(run);

            Logger.Info("[Overlay/2] Open, relink and prepare overlay sheet...");
            drawingService = OverlaySheetHelper.OpenRelinkAndPrepareOverlaySheet(
                context.SwApp,
                run,
                drawingData,
                out var viewNames);

            BindReferencedConfigurations(
                drawingService,
                run,
                drawingData,
                viewNames,
                behavior.Family == DrawingWedgeFamily.Ckvd);

            Logger.Info("[Overlay/3] Compute magnification, calibration and payload...");
            var (layoutContext, overlayMagnification, overlayCalibrationUm) =
                OverlayMagnificationService.ComputeOverlayMagCal(run, drawingData);

            var overlayKeys = OverlayMagnificationService.DefaultOverlayDimKeys(run.WedgeType);
            var overlayPayload = OverlayPayloadBuilder.BuildOverlayPayload(
                run,
                drawingData,
                overlayKeys);

            drawingService.Rebuild();

            Logger.Info("[Overlay/4] Apply overlay view scales and positions...");
            OverlayViewScaler.ApplyOverlayViewScales(
                drawingService,
                viewNames,
                overlayMagnification);

            OverlayViewScaler.TryRepositionAllOverlayViews(
                context.SwApp,
                drawingService,
                run,
                viewNames,
                overlayMagnification);

            if (behavior.DeleteFrontOverlayViewWhenVrIsZero)
            {
                Logger.Info("[Overlay/5] Apply overlay view cleanup...");
                OverlayViewScaler.DeleteFrontViewIfVrZero(
                    drawingService,
                    viewNames,
                    layoutContext);
            }

            drawingService.Rebuild();
            drawingService.ZoomToSheet();

            Logger.Info("[Overlay/6] Plan overlay dimensions and create table...");
            var (dimensions, plans) = OverlayAnnotationHelper.PlanOverlayDimensions(
                layoutContext,
                run.WedgeType,
                context.PlannedOverlayDimensions);

            OverlayAnnotationHelper.TryCreateOverlayDimTable(
                context.SwApp,
                drawingService,
                drawingData,
                overlayPayload);

            Logger.Info("[Overlay/7] Apply overlay annotations and metadata...");
            OverlayAnnotationHelper.TryApplyAnnotationPositions(
                drawingService,
                viewNames,
                run,
                drawingData,
                plans);

            OverlayAnnotationHelper.TryApplyOverlayMetadata(
                drawingService,
                drawingData,
                run);

            OverlayAnnotationHelper.TryCleanupZeroDims(
                drawingService,
                viewNames,
                layoutContext,
                drawingData,
                dimensions,
                run);

            Logger.Info("[Overlay/8] Draw calibration box/note and export TIFF...");
            OverlayAnnotationHelper.TryCalibrationBoxAndNote(
                drawingService,
                overlayMagnification,
                overlayCalibrationUm);

            OverlayTiffExporter.ExportOverlayTiff(
                context.SwApp,
                drawingService,
                run);
        }
        finally
        {
            // ExportOverlayTiff closes on the successful path. This also closes the
            // drawing when an earlier SolidWorks step throws.
            drawingService?.Close();
        }
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
            OverlayAnnotationHelper.TryBindOverlayViewConfigurations(
                drawingService,
                run,
                viewNames);
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
            if (drawingService.Model is not ModelDoc2 model)
                return;

            BindView("Front", DrawingType.Production);
            BindView("Side", DrawingType.Production);
            BindView("Top", DrawingType.Production);
            BindView("Detail", drawingData.DrawingType);
            BindView("Section", drawingData.DrawingType);

            void BindView(string logicalView, DrawingType drawingType)
            {
                if (!viewNames.TryGetValue(logicalView, out var actualView) ||
                    string.IsNullOrWhiteSpace(actualView))
                {
                    return;
                }

                DrawingViewConfigBinder.SetReferencedConfigurationForView(
                    model,
                    actualView,
                    run.Wedge.Subclass,
                    drawingType);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(
                $"[Overlay/PGB] View configuration binding failed, continuing: {ex.Message}");
        }
    }
}
