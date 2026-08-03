using System;
using System.Collections.Generic;
using System.IO;

using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;

namespace WAD.Runner.DrawingAutomation.Overlay.Positioning;

public static class OverlayViewMacroPositioner
{
    private static readonly IReadOnlyList<string> ModuleCandidates =
        Array.AsReadOnly(new[] { "Macro1", "Module1" });

    public static void RunIfAvailable(
        SldWorks swApp,
        string? macroFile,
        string logicalViewName,
        string referencePointName,
        double xIn,
        double yIn,
        IDictionary<string, string>? logicalToActual = null)
    {
        if (swApp is null)
            throw new ArgumentNullException(nameof(swApp));

        if (string.IsNullOrWhiteSpace(logicalViewName))
            throw new ArgumentException("A logical view name is required.", nameof(logicalViewName));

        if (string.IsNullOrWhiteSpace(referencePointName))
            throw new ArgumentException("A reference-point name is required.", nameof(referencePointName));

        if (string.IsNullOrWhiteSpace(macroFile) || !File.Exists(macroFile))
        {
            Logger.Warn(
                $"[Overlay/Position] Macro file was not found; " +
                $"'{logicalViewName}' was not repositioned. Path='{macroFile ?? "(null)"}'.");
            return;
        }

        var actualViewName = ResolveActualViewName(logicalViewName, logicalToActual);

        foreach (var moduleName in ModuleCandidates)
        {
            var runner = new SolidWorksMacroRunner(macroFile, moduleName, "main");
            runner.PrepareArgs(actualViewName, referencePointName, xIn, yIn);

            if (!runner.Run(swApp))
                continue;

            Logger.Success(
                $"[Overlay/Position] Positioned '{actualViewName}' using " +
                $"macro module '{moduleName}'.");
            return;
        }

        Logger.Warn(
            $"[Overlay/Position] Macro failed for '{actualViewName}'. " +
            $"Tried modules: {string.Join(", ", ModuleCandidates)}.");
    }

    private static string ResolveActualViewName(
        string logicalViewName,
        IDictionary<string, string>? logicalToActual)
        => logicalToActual is not null &&
           logicalToActual.TryGetValue(logicalViewName, out var mapped) &&
           !string.IsNullOrWhiteSpace(mapped)
            ? mapped
            : logicalViewName;
}
