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
using WAD.Runner.DrawingAutomation.Core;
using WAD.Runner.DrawingAutomation.Wedges;
using WAD.Runner.DrawingAutomation.Metadata;
using WAD.Runner.DrawingAutomation.Overlay;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.SolidWorks;
using WAD.Runner.DrawingAutomation.Tables;
using WAD.Runner.DrawingAutomation.Views;

namespace WAD.Runner.DrawingAutomation.Overlay
{
    public static class OverlayAnnotationHelper
    {
        public static void TryApplyAnnotationPositions(
            DrawingService ds,
            IDictionary<string, string> nameMap,
            DrawingRun run,
            DrawingData drawingData,
            IEnumerable<AnnotationPositioner.Plan> planned)
        {
            try
            {
                var pos = new AnnotationPositioner(ds, nameMap);
                pos.Apply(run.Wedge, drawingData, planned);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[DimPos/Overlay] Skipped due to error: {ex.Message}");
            }
        }

        public static void TryApplyOverlayMetadata(DrawingService ds, DrawingData drawingData, DrawingRun run)
        {
            try
            {
                MetadataApplier.ApplyOverlay(ds, drawingData, run.Wedge, run.WedgeType);
                ds.Rebuild();
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Overlay] Metadata apply failed (continuing): {ex.Message}");
            }
        }

        public static void TryCleanupZeroDims(
            DrawingService ds,
            IDictionary<string, string> nameMap,
            LayoutContext ctx,
            DrawingData drawingData,
            IReadOnlyList<DimensionSpec> dims,
            DrawingRun run)
        {
            try
            {
                bool hideVrExtremaAnnotations = ShouldHideVrExtremaAnnotations(run);
                AnnotationCleanupService.RemoveZeroDimensionsFromDrawing(
                    ds,
                    nameMap,
                    ctx,
                    drawingData,
                    dims,
                    hideVrExtremaAnnotations: hideVrExtremaAnnotations);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Overlay] Zero-dimension cleanup failed (continuing): {ex.Message}");
            }
        }

        public static void TryCalibrationBoxAndNote(DrawingService ds, double overlayMag, string overlayCalUm)
        {
            try
            {
                ds.DrawCalibrationBoxOnSheetFormat(overlayMag);
                ds.InsertCalibrationBoxNoteBottomRight(overlayCalUm, overlayMag);
                ds.Rebuild();
                ds.ZoomToSheet();
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Overlay] Calibration box/note step failed (continuing): {ex.Message}");
            }
        }

        public static void TryCreateOverlayDimTable(SldWorks swApp, DrawingService ds, DrawingData drawingData, OverlayDrawingPayload overlayData)
        {
            try
            {
                var tableService = new TableService(swApp, ds.Model!);
                if (!TryCreateOverlayDimensionTable(tableService, drawingData, overlayData))
                    Logger.Warn("[Overlay] Dimension table creation skipped or reported failure.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Overlay] Dimension table step failed (continuing): {ex.Message}");
            }
        }

        private static bool TryCreateOverlayDimensionTable(TableService tableService, DrawingData drawingData, OverlayDrawingPayload overlayData)
        {
            if (drawingData.Tables == null ||
                !drawingData.Tables.TryGetValue("DimTable", out var cfg) ||
                cfg == null ||
                cfg.PositionMm == null ||
                cfg.PositionMm.Length < 2)
            {
                Logger.Warn("[Overlay] DimTable config missing or incomplete; cannot place dimension table.");
                return false;
            }

            var pos = cfg.PositionMm;
            var xMm = pos[0];
            var yMm = pos[1];

            double widthMm = 30.0;
            if (cfg.SizeMm != null && cfg.SizeMm.Length >= 1 && cfg.SizeMm[0] > 0)
                widthMm = cfg.SizeMm[0];

            Logger.Info($"[Overlay] Creating overlay dimension table at ({xMm:0.###}, {yMm:0.###}) mm, width={widthMm:0.###} mm.");

            return tableService.CreateOverlayDimensionTableAt(
                overlayData.Dimensions,
                5,
                118,
                widthMm,
                header: "DIMENSIONS");
        }

        public static (IReadOnlyList<DimensionSpec> dims, IReadOnlyList<AnnotationPositioner.Plan> planned) PlanOverlayDimensions(
            LayoutContext ctx,
            WedgeType wedgeType,
            IEnumerable<AnnotationPositioner.Plan>? plannedDimsOverride)
        {
            var diag = new PlannerDiagnostics();
            var dims = DimensionRules.Build(ctx, diag, wedgeType);

            var planned = (plannedDimsOverride ?? dims.Select(d => new AnnotationPositioner.Plan
            {
                Id = d.Id,
                View = d.View,
                Key = d.Key,
                PositionMm = d.PositionMm,
                Nominal = d.Nominal
            })).ToList();

            Logger.Info($"[Overlay] Planned overlay dims count = {planned.Count}");
            return (dims, planned);
        }

        private static bool ShouldHideVrExtremaAnnotations(DrawingRun run)
        {
            if (run?.Wedge == null)
                return false;

            if (!DrawingWedgeModuleRegistry.Get(run.WedgeType).Behavior.HideVrExtremaWhenOverlayCompressed)
                return false;

            double rawMm = ComputeCobLikeRawNonStdCutMm(run.Wedge);

            bool shouldHide = WasOverlayNonStdCutCompressedOrClamped(rawMm, out double effectiveMm);
            if (shouldHide)
            {
                Logger.Warn(
                    $"[Overlay] VR/VRR extrema annotations will be hidden because overlay non_std_cut was compressed/clamped " +
                    $"for {run.WedgeType}: raw={rawMm:0.#####} mm, effective={effectiveMm:0.#####} mm.");
            }

            return shouldHide;
        }

        private static bool WasOverlayNonStdCutCompressedOrClamped(double rawMm, out double effectiveMm)
        {
            effectiveMm = rawMm;

            if (rawMm <= 0.0)
                return false;

            const double OverlayTlMm = 30.0;

            double softCapMm = 0.5;

            double hardCapMm = 0.6;
            const double CompressionFactor = 0.25;

            if (rawMm <= softCapMm)
                return false;

            double compressedMm = softCapMm + ((rawMm - softCapMm) * CompressionFactor);
            effectiveMm = Math.Min(compressedMm, hardCapMm);

            return Math.Abs(effectiveMm - rawMm) > 1e-9;
        }

        private static double ComputeCobLikeRawNonStdCutMm(WedgeData wedge)
        {
            double vrMax = TryGetMaxLikeMm(wedge, explicitMaxKey: "VR_MAX", baseKey: "VR", out var resolvedVrMax)
                ? resolvedVrMax
                : 0.0;

            double vrrMax = TryGetMaxLikeMm(wedge, explicitMaxKey: "VRR_MAX", baseKey: "VRR", out var resolvedVrrMax)
                ? resolvedVrrMax
                : 0.0;


            double clearance = 0.0;

            return vrMax + vrrMax + clearance;
        }

        private static bool TryGetMaxLikeMm(
            WedgeData wedge,
            string explicitMaxKey,
            string baseKey,
            out double value)
        {
            value = 0.0;

            if (TryGetDimMm(wedge, explicitMaxKey, out var explicitMax))
            {
                value = explicitMax;
                return true;
            }

            if (wedge?.Dimensions is null)
                return false;

            if (!wedge.Dimensions.TryGetValue(DimensionKey.From(baseKey), out var dim) || dim is null)
                return false;

            if (dim.Nominal.Unit != UnitKind.Millimeter)
                return false;

            double nominal = (double)dim.Nominal.AsMm();
            double upperTolerance = (double)decimal.Abs(dim.Tol.Upper.AsMm());
            value = nominal + upperTolerance;
            return true;
        }

        private static bool TryGetDimMm(WedgeData wedge, string key, out double value)
            => new DrawingWedgeFacts(wedge).TryGetLengthMm(key, out value);

    }
}
