using System;
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DrawingAutomation;
using WAD.Runner.DrawingAutomation.Common;
using WAD.Runner.DrawingAutomation.Core;
using WAD.Runner.DrawingAutomation.Metadata;
using WAD.Runner.DrawingAutomation.Planning;
using WAD.Runner.DrawingAutomation.Rules.Annotation;
using WAD.Runner.DrawingAutomation.SolidWorks;
using WAD.Runner.DrawingAutomation.Tables;
using WAD.Runner.DrawingAutomation.Views;

namespace WAD.Runner.DrawingAutomation.Execution.Production;

public sealed class ProductionDrawingPipeline : IDrawingPipeline
{
    public bool CanHandle(
        DrawingAutomationContext context)
    {
        return context.DrawingData.DrawingType
            != WAD.Runner.DataManagement.Domain.Wedge.DrawingType.Overlay;
    }

    public void Run(
        DrawingAutomationContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        Logger.Info(
            $"=== WAD Drawing ▶ " +
            $"{context.Run.Wedge.Subclass}/" +
            $"{context.DrawingData.DrawingType} | " +
            $"{context.Run.WedgeType} ===");

        DrawingPipelineState? state = null;

        try
        {
            // ---------------------------------------------------------
            // 1. Finish model automation before opening the drawing.
            // ---------------------------------------------------------
            RunModelPhaseBoundary(
                context);

            // ---------------------------------------------------------
            // 2. Open drawing, relink model, prepare the target sheet.
            // ---------------------------------------------------------
            state =
                OpenRelinkAndPrepare(
                    context);

            // ---------------------------------------------------------
            // 3. Stabilize the entire drawing-view layout.
            //
            // One coordinator owns:
            //
            // - Detail/Section configured scales
            // - Front/Side/Top autoscale
            // - breakline recalculation against current/final scale
            // - final view positions
            // - rebuild boundaries
            //
            // The pipeline no longer knows the internal sequencing.
            // ---------------------------------------------------------
            ApplyViewLayout(
                state,
                context);

            // ---------------------------------------------------------
            // 4. Plan dimensions only after geometry, scale, breaklines,
            // and positions are final.
            // ---------------------------------------------------------
            var planned =
                DrawingDimensionPlanner.Plan(
                    context.Run,
                    context.DrawingData);

            DrawingAnnotationCleanupStep.Run(
                state.DrawingService,
                state.ViewNames,
                context.Run,
                context.DrawingData);

            ApplyAnnotationPositions(
                state,
                context.Run,
                context.DrawingData,
                planned.AnnotationPlans);

            ApplyMetadata(
                state.DrawingService,
                context.DrawingData,
                context.Run.Wedge);

            DrawingTableStep.Run(
                context.SwApp,
                state.DrawingService,
                context.Run,
                context.DrawingData);

            DrawingExecutorCommon.FinalizeProduction(
                context.SwApp,
                state.DrawingService,
                context.Run.OutputPdfPath);
        }
        finally
        {
            state?.DrawingService.Close();
        }
    }

    private static void RunModelPhaseBoundary(
        DrawingAutomationContext context)
    {
        Logger.Info(
            "[Drawing/1] Ensure model phase has completed...");

        _ =
            context.RunPartAutomation();

        DrawingRunValidator.EnsureGeneratedPartExists(
            context.Run);
    }

    private static DrawingPipelineState OpenRelinkAndPrepare(
        DrawingAutomationContext context)
    {
        Logger.Info(
            "[Drawing/2] Open, relink and prepare drawing sheet...");

        var drawingService =
            DrawingExecutorCommon.InitializeAndRelink(
                context.SwApp,
                context.Run);

        try
        {
            var targetSheet =
                context.Profile.SheetSelector(
                    drawingService.GetSheetNames());

            TryActivateSheet(
                drawingService,
                targetSheet);

            var deleteResult =
                drawingService.DeleteAllSheetsExcept2(
                    targetSheet);

            if (!deleteResult.Ok)
            {
                Logger.Warn(
                    deleteResult.NotDeleted.Count > 0
                        ? $"[Sheets] Some sheets could not be deleted: " +
                          $"{string.Join(", ", deleteResult.NotDeleted)}"
                        : "[Sheets] DeleteAllSheetsExcept2 returned a warning.");
            }

            drawingService.ZoomToSheet();

            var viewNames =
                DrawingViewNameMap.FromProfile(
                    context.Profile);

            /*
             * Keep ViewPlacement initialized for DrawingPipelineState
             * compatibility with other pipelines.
             *
             * The new Production pipeline itself uses
             * DrawingViewLayoutCoordinator.
             */
            var placementCompatibility =
                new ViewPlacementService(
                    drawingService,
                    viewNames);

            return new DrawingPipelineState
            {
                DrawingService =
                    drawingService,

                ViewNames =
                    viewNames,

                ViewPlacement =
                    placementCompatibility,

                ActiveSheetName =
                    targetSheet
            };
        }
        catch
        {
            drawingService.Close();
            throw;
        }
    }

    private static void ApplyViewLayout(
        DrawingPipelineState state,
        DrawingAutomationContext context)
    {
        Logger.Info(
            "[Drawing/3] Stabilize drawing view layout...");

        var coordinator =
            new DrawingViewLayoutCoordinator(
                state.DrawingService,
                state.ViewNames);

        coordinator.Apply(
            context.Run,
            context.DrawingData,
            context.Profile);
    }

    private static void ApplyAnnotationPositions(
        DrawingPipelineState state,
        DrawingRun run,
        DrawingData drawingData,
        IEnumerable<AnnotationPositioner.Plan> plans)
    {
        Logger.Info(
            "[Drawing/4] Apply annotation positions...");

        try
        {
            var positioner =
                new AnnotationPositioner(
                    state.DrawingService,
                    state.ViewNames);

            positioner.Apply(
                run.Wedge,
                drawingData,
                plans);
        }
        catch (Exception ex)
        {
            Logger.Warn(
                $"[Drawing/4] Annotation positioning failed, continuing: " +
                $"{ex.Message}");
        }
    }

    private static void ApplyMetadata(
        DrawingService drawingService,
        DrawingData drawingData,
        WAD.Runner.DataManagement.Domain.Wedge.WedgeData wedge)
    {
        Logger.Info(
            "[Drawing/5] Apply drawing metadata...");

        try
        {
            MetadataApplier.Apply(
                drawingService,
                drawingData,
                wedge);

            drawingService.Rebuild();
        }
        catch (Exception ex)
        {
            Logger.Warn(
                $"[Metadata] Failed, continuing: {ex.Message}");
        }
    }

    private static void TryActivateSheet(
        DrawingService drawingService,
        string sheetName)
    {
        try
        {
            var drawing =
                drawingService.Drawing
                ?? throw new InvalidOperationException(
                    "No active drawing.");

            drawing.ActivateSheet(
                sheetName);

            Logger.Info(
                $"[Sheets] Activated: {sheetName}");

            drawingService.Rebuild();
        }
        catch (Exception ex)
        {
            Logger.Warn(
                $"[Sheets] Failed to activate '{sheetName}', continuing: " +
                $"{ex.Message}");
        }
    }
}
