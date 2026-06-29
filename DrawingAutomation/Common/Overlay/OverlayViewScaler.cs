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
    public static class OverlayViewScaler
    {
        public static void ApplyOverlayViewScales(DrawingService ds, IDictionary<string, string> nameMap, double overlayMag)
        {
            try
            {
                var drawing = ds.Drawing;
                if (drawing == null)
                {
                    Logger.Warn("[Overlay] ApplyOverlayViewScales: drawing is null.");
                    return;
                }

                nameMap.TryGetValue("Front", out var frontName);
                nameMap.TryGetValue("Side", out var sideName);
                nameMap.TryGetValue("Top", out var topName);
                nameMap.TryGetValue("Detail", out var detailName);
                nameMap.TryGetValue("Section", out var sectionName);

                bool IsSame(string? a, string? b) =>
                    !string.IsNullOrWhiteSpace(a) &&
                    !string.IsNullOrWhiteSpace(b) &&
                    string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

                double detailSectionScale = GetOverlayViewScaleFromMagnification(overlayMag);

                object viewObj = drawing.GetFirstView();
                while (viewObj is not null)
                {
                    var view = viewObj as View;
                    if (view == null) break;

                    var vName = view.Name ?? string.Empty;

                    if (IsSame(vName, frontName) || IsSame(vName, sideName) || IsSame(vName, topName))
                    {
                        view.ScaleDecimal = 2;
                        Logger.Info($"[Overlay] Set view '{vName}' scale ≈ 1:1.5 (ScaleDecimal=2).");
                    }

                    if (IsSame(vName, detailName) || IsSame(vName, sectionName))
                    {
                        view.ScaleDecimal = detailSectionScale;
                        Logger.Info($"[Overlay] Set view '{vName}' scale from overlayMag={overlayMag}X → ScaleDecimal={detailSectionScale:0.####}.");
                    }

                    viewObj = view.GetNextView();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Overlay] ApplyOverlayViewScales failed: {ex.Message}");
            }
        }

        public static void TryRepositionAllOverlayViews(
            SldWorks swApp,
            DrawingService ds,
            DrawingRun run,
            IDictionary<string, string> nameMap)
        {
            try
            {
                var overlayMacroPath = GetOverlayMacroPath();

                var isCkvd = run.WedgeType == WedgeType.CKVD;
                var isOsg7 = run.WedgeType == WedgeType.OSG7;
                var isCob = run.WedgeType == WedgeType.COB;
                var isUtUs = run.WedgeType == WedgeType.UTUS;
                var isFp = run.WedgeType == WedgeType.FP;

                var shankType = ResolveShankType(run.Wedge);
                bool hasVw = HasPositiveDimension(run, "VW");
                bool hasVr = HasPositiveDimension(run, "VR");

                var baseRefPointSketchName = run.WedgeType switch
                {
                    WedgeType.CKVD => "ref_point_2",
                    WedgeType.OSG7 => "ref_point",
                    _ => "ref_point_sketch"
                };

                if (shankType == ShankType.Rev180)
                {
                    baseRefPointSketchName = "ref_point_180_DEG_REV_sketch";
                }

                var detailRefPointSketchName = baseRefPointSketchName;
                var sectionRefPointSketchName = baseRefPointSketchName;

                if (!isCkvd && !isOsg7 && hasVw && hasVr)
                {
                    detailRefPointSketchName = "ref_point_non_std_cut_sketch";

                    Logger.Info(
                        "[Overlay] Non-CKVD with VW>0 and VR>0 detected → " +
                        "Detail view will use 'ref_point_non_std_cut_sketch'.");
                }

                double detailYIn = 2.4;
                double sectionYIn = 2.4;

                if (isCob || isUtUs || isFp)
                {
                    if (shankType == ShankType.Rev180)
                    {
                        double tdfMm = GetDimMm(run, "TDF");
                        double tdMm = GetDimMm(run, "TD");

                        if (tdfMm > 0.0 &&
                            tdMm > 0.0 &&
                            !double.IsNaN(tdfMm) && !double.IsInfinity(tdfMm) &&
                            !double.IsNaN(tdMm) && !double.IsInfinity(tdMm))
                        {
                            double overlayMag = ComputeOverlayMagnification(run);
                            double overlayScale = GetOverlayModelViewScaleDecimal(overlayMag);

                            double scaledTdfMm = tdfMm * overlayScale;
                            double scaledTdMm = tdMm * overlayScale;

                            double computedYmm = 60.96 - ((scaledTdfMm - (scaledTdMm / 2.0)) / 2.0);

                            Logger.Blue($"[Overlay] Raw TDF = {tdfMm:0.####} mm");
                            Logger.Blue($"[Overlay] Raw TD  = {tdMm:0.####} mm");
                            Logger.Blue($"[Overlay] OverlayMag = {overlayMag:0.####}X");
                            Logger.Blue($"[Overlay] OverlayScale = {overlayScale:0.####}");
                            Logger.Blue($"[Overlay] Scaled TDF = {scaledTdfMm:0.####} mm");
                            Logger.Blue($"[Overlay] Scaled TD  = {scaledTdMm:0.####} mm");
                            Logger.Blue($"[Overlay] Computed Detail/Section Y = {computedYmm:0.####} mm");

                            if (!double.IsNaN(computedYmm) &&
                                !double.IsInfinity(computedYmm) &&
                                computedYmm > 0.0)
                            {
                                double computedYin = MmToIn(computedYmm);
                                detailYIn = computedYin;
                                sectionYIn = 2.4;

                                Logger.Info(
                                    $"[Overlay] STD shank detected → Detail/Section Y computed from scaled TDF/TD " +
                                    $"(TDF={scaledTdfMm:0.####} mm, TD={scaledTdMm:0.####} mm, scale={overlayScale:0.####}) " +
                                    $"→ Y={computedYmm:0.####} mm ({computedYin:0.####} in).");
                            }
                            else
                            {
                                Logger.Warn(
                                    $"[Overlay] Computed Y was invalid ({computedYmm:0.####} mm). Falling back to 2.4 in.");
                            }
                        }
                        else
                        {
                            Logger.Warn(
                                $"[Overlay] Missing/invalid dimensions for Y calculation. " +
                                $"TDF={tdfMm:0.####} mm, TD={tdMm:0.####} mm. Falling back to 2.4 in.");
                        }
                    }
                    else
                    {
                        double tdfMm = GetDimMm(run, "TDF");
                        double tdMm = GetDimMm(run, "TD");

                        if (tdfMm > 0.0 &&
                            tdMm > 0.0 &&
                            !double.IsNaN(tdfMm) && !double.IsInfinity(tdfMm) &&
                            !double.IsNaN(tdMm) && !double.IsInfinity(tdMm))
                        {
                            double overlayMag = ComputeOverlayMagnification(run);
                            double overlayScale = GetOverlayModelViewScaleDecimal(overlayMag);

                            double scaledTdfMm = tdfMm * overlayScale;
                            double scaledTdMm = tdMm * overlayScale;

                            double computedYmm = 60.96 - ((scaledTdfMm - (scaledTdMm / 2.0)) / 2.0);

                            Logger.Blue($"[Overlay] Raw TDF = {tdfMm:0.####} mm");
                            Logger.Blue($"[Overlay] Raw TD  = {tdMm:0.####} mm");
                            Logger.Blue($"[Overlay] OverlayMag = {overlayMag:0.####}X");
                            Logger.Blue($"[Overlay] OverlayScale = {overlayScale:0.####}");
                            Logger.Blue($"[Overlay] Scaled TDF = {scaledTdfMm:0.####} mm");
                            Logger.Blue($"[Overlay] Scaled TD  = {scaledTdMm:0.####} mm");
                            Logger.Blue($"[Overlay] Computed Detail/Section Y = {computedYmm:0.####} mm");

                            if (!double.IsNaN(computedYmm) &&
                                !double.IsInfinity(computedYmm) &&
                                computedYmm > 0.0)
                            {
                                double computedYin = MmToIn(computedYmm);
                                detailYIn = computedYin;
                                sectionYIn = computedYin;

                                Logger.Info(
                                    $"[Overlay] STD shank detected → Detail/Section Y computed from scaled TDF/TD " +
                                    $"(TDF={scaledTdfMm:0.####} mm, TD={scaledTdMm:0.####} mm, scale={overlayScale:0.####}) " +
                                    $"→ Y={computedYmm:0.####} mm ({computedYin:0.####} in).");
                            }
                            else
                            {
                                Logger.Warn(
                                    $"[Overlay] Computed Y was invalid ({computedYmm:0.####} mm). Falling back to 2.4 in.");
                            }
                        }
                        else
                        {
                            Logger.Warn(
                                $"[Overlay] Missing/invalid dimensions for Y calculation. " +
                                $"TDF={tdfMm:0.####} mm, TD={tdMm:0.####} mm. Falling back to 2.4 in.");
                        }
                    }
                }

                Logger.Info(
                    $"[Overlay] Reposition Detail using sketch '{detailRefPointSketchName}', " +
                    $"Section using sketch '{sectionRefPointSketchName}', wedge type '{run.WedgeType}'.");

                SecondaryViewPlacementService.RunMacroForViewIfAvailable(
                    swApp,
                    macroFile: overlayMacroPath,
                    logicalViewName: "Detail",
                    sketchName: detailRefPointSketchName,
                    xIn: 6.285,
                    yIn: 2.4,
                    logicalToActual: nameMap);

                SecondaryViewPlacementService.RunMacroForViewIfAvailable(
                    swApp,
                    macroFile: overlayMacroPath,
                    logicalViewName: "Section",
                    sketchName: sectionRefPointSketchName,
                    xIn: 3.19,
                    yIn: sectionYIn,
                    logicalToActual: nameMap);

                if (isCkvd)
                {
                    SecondaryViewPlacementService.RunMacroForViewIfAvailable(
                        swApp,
                        macroFile: overlayMacroPath,
                        logicalViewName: "Front",
                        sketchName: baseRefPointSketchName,
                        xIn: 0.4,
                        yIn: 0.3,
                        logicalToActual: nameMap);

                    SecondaryViewPlacementService.RunMacroForViewIfAvailable(
                        swApp,
                        macroFile: overlayMacroPath,
                        logicalViewName: "Side",
                        sketchName: baseRefPointSketchName,
                        xIn: 3.19,
                        yIn: 0,
                        logicalToActual: nameMap);

                    Logger.Info("[Overlay] CKVD detected → Front and Side views were also repositioned.");
                }
                else
                {
                    Logger.Info("[Overlay] Non-CKVD overlay → only Detail and Section views were repositioned.");
                }

                ds.Rebuild();
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Overlay] Reposition-after-scaling step failed (continuing): {ex.Message}");
            }
        }

        public static void DeleteFrontViewIfVrZero(DrawingService ds, IDictionary<string, string> nameMap, LayoutContext ctx)
        {
            try
            {
                double vr = LayoutMath.Dmm(ctx, "VR");

                if (double.IsNaN(vr) || double.IsInfinity(vr))
                {
                    Logger.Info("[Overlay] VR missing/invalid; keeping Front view.");
                    return;
                }

                if (Math.Abs(vr) > 1e-6)
                {
                    Logger.Info($"[Overlay] VR={vr:0.####} mm (non-zero); keeping Front view.");
                    return;
                }

                var model = ds.Model;
                if (model is null)
                {
                    Logger.Warn("[Overlay] DeleteFrontViewIfVrZero: model is null.");
                    return;
                }

                if (!nameMap.TryGetValue("Front", out var frontName) || string.IsNullOrWhiteSpace(frontName))
                    frontName = "Front";

                Logger.Info($"[Overlay] VR=0 → deleting Front view '{frontName}'…");

                model.ClearSelection2(true);

                bool sel = model.Extension.SelectByID2(
                    frontName,
                    "DRAWINGVIEW",
                    0, 0, 0,
                    false,
                    0,
                    null,
                    0);

                if (!sel)
                {
                    Logger.Warn($"[Overlay] Could not select Front view '{frontName}' for deletion.");
                    return;
                }

                bool ok = model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);

                if (ok) Logger.Info($"[Overlay] Front view '{frontName}' deleted because VR=0.");
                else Logger.Warn($"[Overlay] VR=0 but deletion of Front view '{frontName}' failed.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Overlay] DeleteFrontViewIfVrZero failed: {ex.Message}");
            }
        }

        private static double GetOverlayViewScaleFromMagnification(double overlayMag)
        {
            int token = (int)Math.Round(overlayMag);
            return token switch
            {
                100 => 60.8,
                200 => 122.7,
                300 => 183.0,
                400 => 246.0,
                _ => 60.8
            };
        }

        private static string GetOverlayMacroPath()
        {
            var baseDir = AppContext.BaseDirectory ?? string.Empty;

            var candidateOutput = Path.Combine(baseDir, "Resources", "Macros", "OverlayMacro.swp");
            candidateOutput = Path.GetFullPath(candidateOutput);
            if (File.Exists(candidateOutput))
            {
                Logger.Info($"[Overlay] Using macro from output folder: {candidateOutput}");
                return candidateOutput;
            }

            var candidateProject = Path.Combine(baseDir, "..", "..", "..", "Resources", "Macros", "OverlayMacro.swp");
            candidateProject = Path.GetFullPath(candidateProject);
            if (File.Exists(candidateProject))
            {
                Logger.Info($"[Overlay] Using macro from project folder: {candidateProject}");
                return candidateProject;
            }

            Logger.Warn("[Overlay] OverlayMacro.swp not found. " +
                        $"Tried: '{candidateOutput}' and '{candidateProject}'.");

            return candidateOutput;
        }

        private static string GetOverlayMagnificationSourceKey(WedgeType wedgeType)
        {
            return wedgeType is WedgeType.CKVD or WedgeType.OSG7
                ? "FL"
                : "T";
        }

        private static double GetDimMm(DrawingRun run, string key)
        {
            try
            {
                if (run?.Wedge?.Dimensions == null)
                    return double.NaN;

                if (!run.Wedge.Dimensions.TryGetValue(DimensionKey.From(key), out var dim) || dim == null)
                    return double.NaN;

                return Convert.ToDouble(dim.Nominal.Value);
            }
            catch
            {
                return double.NaN;
            }
        }

        private static double MmToIn(double mm) => mm / 25.4;

        private static double ComputeOverlayMagnification(DrawingRun run)
        {
            if (run is null)
                return 100.0;

            string sourceKey = GetOverlayMagnificationSourceKey(run.WedgeType);
            double sourceValueMm = GetDimMm(run, sourceKey);

            if (double.IsNaN(sourceValueMm) || double.IsInfinity(sourceValueMm) || sourceValueMm <= 0.0)
            {
                Logger.Warn(
                    $"[Overlay] Overlay magnification source '{sourceKey}' missing/invalid for wedge type {run.WedgeType}. Using default 100X.");
                return 100.0;
            }

            Logger.Info(
                $"[Overlay] Overlay magnification source '{sourceKey}' = {sourceValueMm:0.#####} mm for wedgeType={run.WedgeType}");

            if (sourceValueMm <= 0.3403) return 400.0;
            if (sourceValueMm <= 0.4572) return 300.0;
            if (sourceValueMm <= 0.6908) return 200.0;
            if (sourceValueMm <= 1.3766) return 100.0;
            return 100.0;
        }

        private static double GetOverlayModelViewScaleDecimal(double overlayMagnification)
        {
            int token = NormalizeScalingToken(overlayMagnification);

            return token switch
            {
                100 => 60.8,
                200 => 122.7,
                300 => 183.0,
                400 => 246.0,
                _ => 60.8
            };
        }

        private static bool HasPositiveDimension(DrawingRun run, string key)
        {
            try
            {
                double valueMm = GetDimMm(run, key);

                return !double.IsNaN(valueMm)
                    && !double.IsInfinity(valueMm)
                    && Math.Abs(valueMm) > 1e-6;
            }
            catch
            {
                return false;
            }
        }

        private static int NormalizeScalingToken(object? overlayScaling)
        {
            if (overlayScaling is null)
                return 100;

            if (double.TryParse(
                    overlayScaling.ToString(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var d))
            {
                if (d < 10.0)
                    return (int)Math.Round(d * 100.0);

                return (int)Math.Round(d);
            }

            var s = overlayScaling.ToString()?.Trim() ?? string.Empty;
            s = s.ToUpperInvariant().Replace(" ", "");

            if (s.StartsWith("X"))
                s = s[1..];

            if (s.EndsWith("X"))
                s = s[..^1];

            return int.TryParse(
                s,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var n)
                ? n
                : 100;
        }

        private static string? GetPropLoose(WedgeData wedge, string key)
        {
            try
            {
                if (wedge?.Properties == null || wedge.Properties.Count == 0)
                    return null;

                if (wedge.Properties.TryGetValue(key, out var exact))
                    return exact;

                var target = NormalizeKey(key);

                foreach (var kv in wedge.Properties)
                {
                    var k = NormalizeKey(kv.Key);
                    if (string.Equals(k, target, StringComparison.OrdinalIgnoreCase))
                        return kv.Value;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeKey(string? k)
        {
            k ??= string.Empty;
            k = k.Trim();
            return k.Replace("-", "").Replace("_", "").Replace(" ", "");
        }

        private static string NormalizeDbToken(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;

            s = s.Trim();
            var semi = s.IndexOf(';');
            if (semi >= 0)
                s = s.Substring(0, semi);

            return s.Trim();
        }

        private static bool EqualsAny(string value, params string[] options)
            => options.Any(o => string.Equals(value, o, StringComparison.OrdinalIgnoreCase));

        private static ShankType ResolveShankType(WedgeData wedge)
        {
            var raw =
                GetPropLoose(wedge, "Wed-Type") ??
                GetPropLoose(wedge, "Wed_Type") ??
                GetPropLoose(wedge, "Wed Type") ??
                GetPropLoose(wedge, "Shank_Type") ??
                GetPropLoose(wedge, "shank_type") ??
                string.Empty;

            raw = NormalizeDbToken(raw);

            if (EqualsAny(raw,
                    "SW_180REV",
                    "SW_180_DEG_REV",
                    "SW_180DEGREV",
                    "180_DEG_REV",
                    "180DEGREV",
                    "180REV",
                    "REV",
                    "REVERSE"))
                return ShankType.Rev180;

            return ShankType.Std;
        }

        private enum ShankType { Std, Rev180 }

    }
}
