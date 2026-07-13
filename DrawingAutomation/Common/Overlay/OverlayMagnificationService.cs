using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Planning;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;

using WAD.Runner.DrawingAutomation.Common;
using WAD.Runner.DrawingAutomation.Metadata;
using WAD.Runner.DrawingAutomation.Overlay;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.SolidWorks;
using WAD.Runner.DrawingAutomation.Tables;
using WAD.Runner.DrawingAutomation.Views;

namespace WAD.Runner.DrawingAutomation.Common.Overlay
{
    public static class OverlayMagnificationService
    {
        private static readonly string[] CobLikeOverlayDimensionKeys =
        {
            "W", "FD", "T", "VBL", "VBLR", "VW", "VR", "VRR", "RA2H", "RA", "RA2",
            "TL", "TD", "TDF", "CGR", "G", "CGD", "FRO", "CR", "RC", "CD", "GR", "GD",
            "B", "MB", "H", "FNO", "FL", "ERL", "ERD", "CBRL", "CBRD", "FLC", "CL",
            "MI", "Y", "ERW", "FLER", "MFL", "BF", "FR", "CBL","HA","FNA"
        };

        private static readonly string[] CkvdOverlayDimensionKeys =
        {
            "FL", "FR", "F", "W", "BR", "GD", "GR", "B", "E", "FX", "X", "TD", "TDF", "TL"
        };

        private static readonly string[] DefaultOverlayDimensionKeys =
        {
            "FL", "FR", "F", "W", "BR", "GD", "GR", "B", "E", "FX", "X"
        };

        public static string[] DefaultOverlayDimKeys(WedgeType wedgeType)
        {
            var keys = wedgeType switch
            {
                WedgeType.COB or WedgeType.UTUS or WedgeType.FP => CobLikeOverlayDimensionKeys,
                WedgeType.CKVD or WedgeType.OSG7 => CkvdOverlayDimensionKeys,
                _ => DefaultOverlayDimensionKeys
            };

            return keys.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static (LayoutContext ctx, double overlayMag, string overlayCalUm) ComputeOverlayMagCal(
            DrawingRun run,
            DrawingData drawingData)
        {
            if (run is null) throw new ArgumentNullException(nameof(run));
            if (drawingData is null) throw new ArgumentNullException(nameof(drawingData));

            var ctx = new LayoutContext(run.Wedge, drawingData);

            string sourceKey = GetOverlayMagnificationSourceKey(run.WedgeType);
            double sourceValueMm = LayoutMath.Dmm(ctx, sourceKey);

            if (double.IsNaN(sourceValueMm) || double.IsInfinity(sourceValueMm) || sourceValueMm <= 0.0)
            {
                Logger.Error($"[Overlay] {sourceKey} missing/invalid for wedge type {run.WedgeType}; using fallback mag=100, cal=700 µm.");
                return (ctx, 100.0, "700");
            }

            double mag;
            string calibUm;

            if (sourceValueMm <= 0.3403) { mag = 400; calibUm = "200.4"; }
            else if (sourceValueMm <= 0.4572) { mag = 300; calibUm = "399.6"; }
            else if (sourceValueMm <= 0.6908) { mag = 200; calibUm = "700"; }
            else if (sourceValueMm <= 1.3766) { mag = 100; calibUm = "700"; }
            else { mag = 100; calibUm = "700"; }

            Logger.Info($"[Overlay] {sourceKey}={sourceValueMm:0.####} mm → mag={mag}X, calib={calibUm} µm, wedgeType={run.WedgeType}.");
            return (ctx, mag, calibUm);
        }

        private static string GetOverlayMagnificationSourceKey(WedgeType wedgeType)
        {
            return wedgeType is WedgeType.CKVD or WedgeType.OSG7
                ? "FL"
                : "T";
        }

    }
}
