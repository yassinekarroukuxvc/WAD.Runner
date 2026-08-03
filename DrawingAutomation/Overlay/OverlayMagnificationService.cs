using System;
using System.Linq;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Planning;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Core;
using WAD.Runner.DrawingAutomation.Wedges;

namespace WAD.Runner.DrawingAutomation.Overlay;

public static class OverlayMagnificationService
{
    private const double DefaultMagnification = 100.0;
    private const string DefaultCalibrationMicrons = "700";

    public static string[] DefaultOverlayDimKeys(WedgeType wedgeType)
        => DrawingWedgeModuleRegistry
            .Get(wedgeType)
            .Behavior
            .OverlayDimensionKeys
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static (LayoutContext ctx, double overlayMag, string overlayCalUm) ComputeOverlayMagCal(
        DrawingRun run,
        DrawingData drawingData)
    {
        if (run is null) throw new ArgumentNullException(nameof(run));
        if (drawingData is null) throw new ArgumentNullException(nameof(drawingData));

        var context = new LayoutContext(run.Wedge, drawingData);
        var sourceKey = DrawingWedgeModuleRegistry
            .Get(run.WedgeType)
            .Behavior
            .OverlayMagnificationSourceKey;
        var sourceValueMm = LayoutMath.Dmm(context, sourceKey);

        if (!double.IsFinite(sourceValueMm) || sourceValueMm <= 0.0)
        {
            Logger.Error(
                $"[Overlay] {sourceKey} missing/invalid for wedge type {run.WedgeType}; " +
                $"using fallback mag={DefaultMagnification:0}, cal={DefaultCalibrationMicrons} µm.");

            return (context, DefaultMagnification, DefaultCalibrationMicrons);
        }

        var (magnification, calibrationMicrons) = Resolve(sourceValueMm);

        Logger.Info(
            $"[Overlay] {sourceKey}={sourceValueMm:0.####} mm → " +
            $"mag={magnification:0}X, calib={calibrationMicrons} µm, wedgeType={run.WedgeType}.");

        return (context, magnification, calibrationMicrons);
    }

    public static double ComputeMagnification(
        WedgeData wedge,
        WedgeType wedgeType)
    {
        if (wedge is null) throw new ArgumentNullException(nameof(wedge));

        var sourceKey = DrawingWedgeModuleRegistry
            .Get(wedgeType)
            .Behavior
            .OverlayMagnificationSourceKey;
        var sourceValueMm = new DrawingWedgeFacts(wedge).GetLengthMmOrNaN(sourceKey);

        if (!double.IsFinite(sourceValueMm) || sourceValueMm <= 0.0)
        {
            Logger.Warn(
                $"[Overlay] {sourceKey} missing/invalid for wedge type {wedgeType}; " +
                $"using fallback mag={DefaultMagnification:0}X.");

            return DefaultMagnification;
        }

        return Resolve(sourceValueMm).Magnification;
    }

    public static double GetViewScale(double overlayMagnification)
    {
        var token = NormalizeMagnification(overlayMagnification);
        return token switch
        {
            100 => 60.8,
            200 => 122.7,
            300 => 183.0,
            400 => 246.0,
            _ => 60.8
        };
    }

    private static (double Magnification, string CalibrationMicrons) Resolve(
        double sourceValueMm)
    {
        if (sourceValueMm <= 0.3403) return (400.0, "200.4");
        if (sourceValueMm <= 0.4572) return (300.0, "399.6");
        if (sourceValueMm <= 0.6908) return (200.0, "700");
        return (100.0, "700");
    }

    private static int NormalizeMagnification(double overlayMagnification)
    {
        if (!double.IsFinite(overlayMagnification) || overlayMagnification <= 0.0)
            return 100;

        return overlayMagnification < 10.0
            ? (int)Math.Round(overlayMagnification * 100.0)
            : (int)Math.Round(overlayMagnification);
    }
}
