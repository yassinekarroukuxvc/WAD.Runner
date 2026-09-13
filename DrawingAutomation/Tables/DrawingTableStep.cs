using System;

using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DrawingAutomation;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Tables;

public static class DrawingTableStep
{
    public static void Run(SldWorks swApp, DrawingService drawingService, DrawingRun run, DrawingData drawingData)
    {
        if (swApp is null) throw new ArgumentNullException(nameof(swApp));
        if (drawingService is null) throw new ArgumentNullException(nameof(drawingService));
        if (run is null) throw new ArgumentNullException(nameof(run));
        if (drawingData is null) throw new ArgumentNullException(nameof(drawingData));

        try
        {
            var model = drawingService.Model as ModelDoc2;
            if (model is null || drawingService.Drawing is null)
            {
                Logger.Warn("[Tables] Skipped: model or drawing is null.");
                return;
            }

            var tables = new TableService(swApp, model);

            TryCreate("DimTable", () =>
            {
                Logger.Info(
                    $"[Tables] Dimension table request -> " +
                    $"wedge={run.WedgeType}, subclass={run.Wedge.Subclass}, " +
                    $"drawingType={drawingData.DrawingType}, " +
                    $"sourceDimensions={run.Wedge.Dimensions?.Count ?? 0}.");

                var created = tables.CreateDimensionTable(
                    run.Wedge,
                    drawingData,
                    wedgeType: run.WedgeType,
                    tableId: "DimTable",
                    header: "DIMENSIONS");

                if (created)
                {
                    Logger.Success(
                        $"[Tables] DimTable created -> " +
                        $"wedge={run.WedgeType}, subclass={run.Wedge.Subclass}, " +
                        $"drawingType={drawingData.DrawingType}.");
                }
                else
                {
                    Logger.Warn(
                        $"[Tables] DimTable was NOT created -> " +
                        $"wedge={run.WedgeType}, subclass={run.Wedge.Subclass}, " +
                        $"drawingType={drawingData.DrawingType}. " +
                        "See the preceding [Tables] diagnostics for the exact reason.");
                }
            });

            TryCreate("HowToOrder", () =>
            {
                if (drawingData.Tables?.ContainsKey("HowToOrder") == true)
                    tables.CreateHowToOrderTable(run.Wedge, drawingData, headerText: "HOW TO ORDER", tableId: "HowToOrder");
            });

            TryCreate("LabelAs", () =>
            {
                if (drawingData.Tables?.ContainsKey("LabelAs") == true)
                    tables.CreateLabelAsTable(drawingData, tableId: "LabelAs");
            });

            TryCreate("Polish", () =>
            {
                if (drawingData.Tables?.ContainsKey("Polish") == true)
                    tables.CreatePolishTable(drawingData, tableId: "Polish");
            });

            drawingService.Rebuild();
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Tables] Step failed, continuing: {ex.Message}");
        }
    }

    private static void TryCreate(string tableName, Action create)
    {
        try { create(); }
        catch (Exception ex) { Logger.Warn($"[Tables] {tableName} failed: {ex.Message}"); }
    }
}