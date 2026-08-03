using System;

using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Views;
using WAD.Runner.DrawingAutomation.Wedges;

namespace WAD.Runner.DrawingAutomation.Overlay;

public static class DrawingViewConfigBinder
{
    public static bool Bind(
        ModelDoc2 model,
        string logicalViewName,
        string actualViewName,
        WedgeSubclass subclass,
        DrawingType drawingType,
        WedgeType wedgeType,
        bool hasVw,
        bool hasVr)
    {
        if (model is null ||
            string.IsNullOrWhiteSpace(logicalViewName) ||
            string.IsNullOrWhiteSpace(actualViewName))
        {
            return false;
        }

        if (model is not DrawingDoc drawing)
        {
            Logger.Warn(
                "[ConfigBind] The active model is not a DrawingDoc; " +
                $"cannot bind '{actualViewName}'.");
            return false;
        }

        var view = ViewFinder.FindByName(drawing, actualViewName);
        if (view is null)
        {
            Logger.Warn($"[ConfigBind] View '{actualViewName}' was not found.");
            return false;
        }

        var targetConfiguration = DrawingWedgeModuleRegistry
            .Get(wedgeType)
            .ResolveReferencedConfiguration(
                logicalViewName,
                subclass,
                drawingType,
                hasVw,
                hasVr);

        if (string.IsNullOrWhiteSpace(targetConfiguration))
        {
            Logger.Warn(
                "[ConfigBind] No configuration was resolved for " +
                $"{wedgeType}/{subclass}/{drawingType}/{logicalViewName}.");
            return false;
        }

        Logger.Info(
            "[ConfigBind] " +
            $"LogicalView='{logicalViewName}', ActualView='{actualViewName}', " +
            $"WedgeType='{wedgeType}', Subclass='{subclass}', DrawingType='{drawingType}', " +
            $"HasVW={hasVw}, HasVRFamily={hasVr} -> '{targetConfiguration}'.");

        try
        {
            view.ReferencedConfiguration = targetConfiguration;
            TryRebuild(model);

            var actualConfiguration = SafeGetReferencedConfiguration(view);
            var succeeded = string.Equals(
                actualConfiguration,
                targetConfiguration,
                StringComparison.OrdinalIgnoreCase);

            if (!succeeded)
            {
                Logger.Warn(
                    $"[ConfigBind] '{actualViewName}' reports '{actualConfiguration}' " +
                    $"after requesting '{targetConfiguration}'.");
            }

            return succeeded;
        }
        catch (Exception ex)
        {
            Logger.Warn(
                $"[ConfigBind] Failed to set '{actualViewName}' to " +
                $"'{targetConfiguration}': {ex.Message}");
            return false;
        }
    }

    private static void TryRebuild(ModelDoc2 model)
    {
        try
        {
            model.ForceRebuild3(false);
        }
        catch (Exception ex)
        {
            Logger.Warn($"[ConfigBind] ForceRebuild3 failed: {ex.Message}");
        }

        try
        {
            model.EditRebuild3();
        }
        catch (Exception ex)
        {
            Logger.Warn($"[ConfigBind] EditRebuild3 failed: {ex.Message}");
        }
    }

    private static string SafeGetReferencedConfiguration(View view)
    {
        try
        {
            return view.ReferencedConfiguration ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
