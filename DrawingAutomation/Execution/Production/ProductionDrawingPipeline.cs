using System;
using System.Collections.Generic;

using SolidWorks.Interop.sldworks;

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
    public bool CanHandle(DrawingAutomationContext context)
    => context.DrawingData.DrawingType != WAD.Runner.DataManagement.Domain.Wedge.DrawingType.Overlay;

    public void Run(DrawingAutomationContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        Logger.Info($"=== WAD Drawing ▶ {context.Run.Wedge.Subclass}/{context.DrawingData.DrawingType} | {context.Run.WedgeType} ===");

        RunModelPhaseBoundary(context);

        var state = OpenRelinkAndPrepare(context);

        PlaceViews(state, context.DrawingData);
        ApplyBreaklines(state, context.Run, context.DrawingData);
        AutoScaleAndRepositionViews(state, context.DrawingData, context.Profile.Scale);

        var planned = DrawingDimensionPlanner.Plan(context.Run, context.DrawingData);

        DrawingAnnotationCleanupStep.Run(state.DrawingService, state.ViewNames, context.Run, context.DrawingData);
        ApplyAnnotationPositions(state, context.Run, context.DrawingData, planned.AnnotationPlans);
        ApplyMetadata(state.DrawingService, context.DrawingData, context.Run.Wedge);
        DrawingTableStep.Run(context.SwApp, state.DrawingService, context.Run, context.DrawingData);
        DrawingExecutorCommon.FinalizeProduction(context.SwApp, state.DrawingService, context.Run.OutputPdfPath);
    }

    private static void RunModelPhaseBoundary(DrawingAutomationContext context)
    {
        Logger.Info("[Drawing/1] Ensure model phase has completed...");
        _ = context.RunPartAutomation();
    }

    private static DrawingPipelineState OpenRelinkAndPrepare(DrawingAutomationContext context)
    {
        Logger.Info("[Drawing/2] Open, relink and prepare drawing sheet...");

        var drawingService = DrawingExecutorCommon.InitializeAndRelink(context.SwApp, context.Run);
        var targetSheet = context.Profile.SheetSelector(drawingService.GetSheetNames());

        TryActivateSheet(drawingService, targetSheet);

        var deleteResult = drawingService.DeleteAllSheetsExcept2(targetSheet);
        if (!deleteResult.Ok)
        {
            Logger.Warn(deleteResult.NotDeleted.Count > 0
                ? $"[Sheets] Some sheets could not be deleted: {string.Join(", ", deleteResult.NotDeleted)}"
                : "[Sheets] DeleteAllSheetsExcept2 returned a warning.");
        }

        drawingService.ZoomToSheet();

        var viewNames = DrawingViewNameMap.FromProfile(context.Profile);
        var placer = new ViewPlacementService(drawingService, viewNames);

        return new DrawingPipelineState
        {
            DrawingService = drawingService,
            ViewNames = viewNames,
            ViewPlacement = placer,
            ActiveSheetName = targetSheet
        };
    }

    private static void PlaceViews(DrawingPipelineState state, DrawingData drawingData)
    {
        Logger.Info("[Drawing/3] Place primary and secondary views...");
        state.ViewPlacement.Apply("Front", drawingData);
        state.ViewPlacement.Apply("Side", drawingData);
        state.ViewPlacement.Apply("Top", drawingData);
        state.ViewPlacement.ApplyDetailAndSection(drawingData);
        state.DrawingService.Rebuild();
    }

    private static void ApplyBreaklines(DrawingPipelineState state, DrawingRun run, DrawingData drawingData)
    {
        Logger.Info("[Drawing/4] Apply breaklines...");
        EnsureDefaultBreaklineGaps(drawingData);

        foreach (var logicalView in new[] { "Front", "Side", "Detail", "Section" })
            TryApplyBreakline(state.DrawingService, state.ViewNames, logicalView, run, drawingData);

        state.DrawingService.Rebuild();
    }

    private static void AutoScaleAndRepositionViews(
        DrawingPipelineState state,
        DrawingData drawingData,
        WAD.Runner.DrawingAutomation.Profiles.ScalePolicy scalePolicy)
    {
        Logger.Info("[Drawing/5] Auto-scale and re-apply view positions...");

        var autoscale = new ViewAutoScaleService(state.DrawingService);
        var policy = new ViewAutoScaleService.Policy(
            FillRatioHeight: scalePolicy.FillRatioHeight,
            MinScale: scalePolicy.MinScale,
            MaxScale: scalePolicy.MaxScale,
            Step: scalePolicy.Step,
            TopMarginMm: scalePolicy.TopMarginMm,
            BottomMarginMm: scalePolicy.BottomMarginMm);

        autoscale.ApplyUnifiedScaleFromFront(drawingData, policy, state.ViewNames);

        state.ViewPlacement.Apply("Front", drawingData);
        state.ViewPlacement.Apply("Side", drawingData);
        state.ViewPlacement.Apply("Top", drawingData);
        state.ViewPlacement.ApplyDetailAndSection(drawingData);
        state.DrawingService.Rebuild();
    }

    private static void ApplyAnnotationPositions(
        DrawingPipelineState state,
        DrawingRun run,
        DrawingData drawingData,
        IEnumerable<AnnotationPositioner.Plan> plans)
    {
        Logger.Info("[Drawing/6] Apply annotation positions...");
        try
        {
            var positioner = new AnnotationPositioner(state.DrawingService, state.ViewNames);
            positioner.Apply(run.Wedge, drawingData, plans);
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Drawing/6] Annotation positioning failed, continuing: {ex.Message}");
        }
    }

    private static void ApplyMetadata(DrawingService drawingService, DrawingData drawingData, WAD.Runner.DataManagement.Domain.Wedge.WedgeData wedge)
    {
        Logger.Info("[Drawing/7] Apply drawing metadata...");
        try
        {
            MetadataApplier.Apply(drawingService, drawingData, wedge);
            drawingService.Rebuild();
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Metadata] Failed, continuing: {ex.Message}");
        }
    }

    private static void TryActivateSheet(DrawingService drawingService, string sheetName)
    {
        try
        {
            var drawing = drawingService.Drawing ?? throw new InvalidOperationException("No active drawing.");
            drawing.ActivateSheet(sheetName);
            Logger.Info($"[Sheets] Activated: {sheetName}");
            drawingService.Rebuild();
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Sheets] Failed to activate '{sheetName}', continuing: {ex.Message}");
        }
    }

    private static void EnsureDefaultBreaklineGaps(DrawingData drawingData)
    {
        if (drawingData.Views is null) return;
        SetDefaultGap(drawingData, "Front", 2.0);
        SetDefaultGap(drawingData, "Side", 2.0);
        SetDefaultGap(drawingData, "Detail", 50.0);
        SetDefaultGap(drawingData, "Section", 50.0);
    }

    private static void SetDefaultGap(DrawingData drawingData, string logicalView, double defaultMm)
    {
        if (!drawingData.Views.TryGetValue(logicalView, out var view) || view is null) return;
        if (!view.Params.ContainsKey("breakline_gap_mm"))
            view.Params["breakline_gap_mm"] = defaultMm;
    }

    private static void TryApplyBreakline(
        DrawingService drawingService,
        IDictionary<string, string> viewNames,
        string logicalView,
        DrawingRun run,
        DrawingData drawingData)
    {
        try
        {
            var model = drawingService.Model;
            var drawing = drawingService.Drawing;
            if (model is null || drawing is null) return;

            var view = FindView(drawingService, logicalView, viewNames);
            if (view is null)
            {
                Logger.Warn($"[Breaklines] View '{logicalView}' not found; skipping.");
                return;
            }

            var handler = new BreaklineHandler(view, model);
            var ok = handler.ApplyBreakline(
                logicalView,
                drawingData.DrawingType,
                run.Wedge.Subclass,
                run.Wedge,
                drawingData);

            if (!ok)
                Logger.Warn($"[Breaklines] Apply failed for '{logicalView}'.");
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Breaklines] '{logicalView}' failed, continuing: {ex.Message}");
        }
    }

    private static View? FindView(DrawingService drawingService, string logicalName, IDictionary<string, string> viewNames)
    {
        if (drawingService.Drawing is not DrawingDoc drawing) return null;

        var actualName = viewNames.TryGetValue(logicalName, out var mapped) && !string.IsNullOrWhiteSpace(mapped)
            ? mapped
            : logicalName;

        var view = drawing.IGetFirstView();
        view = view?.IGetNextView();

        var guard = 0;
        while (view is not null && guard++ < 512)
        {
            try
            {
                if (string.Equals(view.Name, actualName, StringComparison.OrdinalIgnoreCase))
                    return view;
            }
            catch { }

            view = view.IGetNextView();
        }

        return null;
    }
}
