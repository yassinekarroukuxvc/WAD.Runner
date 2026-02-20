// PartAutomation/SolidWorks/EquationUpdater.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

using DomDim = WAD.Runner.DataManagement.Domain.Dimensions.Dimension;
using DomDimKey = WAD.Runner.DataManagement.Domain.Dimensions.DimensionKey;
using DomWedgeData = WAD.Runner.DataManagement.Domain.Wedge.WedgeData;
using DomDrawingType = WAD.Runner.DataManagement.Domain.Wedge.DrawingType;
using DomUnitKind = WAD.Runner.DataManagement.Domain.Units.UnitKind;

using SwModelDoc2 = SolidWorks.Interop.sldworks.ModelDoc2;
using SwEquationMgr = SolidWorks.Interop.sldworks.EquationMgr;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;

namespace WAD.Runner.PartAutomation.SolidWorks
{
    public static class EquationUpdater
    {
        private static readonly Regex LineRx =
            new(@"^\s*""(?<key>[^""]+)""\s*=.*$", RegexOptions.Compiled);

        private static string F(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

        /// <summary>
        /// Keys that should always exist in equation files (even if 0).
        /// </summary>
        private static readonly HashSet<string> AlwaysKeepDimensionKeys =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "TL",
                "TD",
                "TDF",
                "ISA",
                "ISA_20",
                "H"
            };

        public static void UpdateEquationFile(string equationFilePath, DomWedgeData wedge, DomDrawingType drawingType)
        {
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));
            UpdateEquationFile(equationFilePath, wedge.Dimensions, wedge, drawingType);
        }

        /// <summary>
        /// Writes equations.txt so that:
        /// - For every dimension key present in effectiveDims, we WRITE/UPDATE its equation line.
        ///   If nominal is 0, we explicitly write "0mm" or "0deg".
        /// - We do NOT remove dimension equations because they are zero.
        /// - We still update EngravingStart and overlay fields.
        /// - COB-only: compute and upsert funnel_gap (DEFAULT funnel gap is in mm).
        /// </summary>
        public static void UpdateEquationFile(
            string equationFilePath,
            IReadOnlyDictionary<DomDimKey, DomDim> effectiveDims,
            DomWedgeData wedge,
            DomDrawingType drawingType)
        {
            if (string.IsNullOrWhiteSpace(equationFilePath) || !File.Exists(equationFilePath))
                throw new FileNotFoundException("Equation file not found.", equationFilePath);

            if (wedge is null) throw new ArgumentNullException(nameof(wedge));
            if (effectiveDims is null) throw new ArgumentNullException(nameof(effectiveDims));

            Logger.Info($"[EquationUpdater] UpdateEquationFile → '{equationFilePath}', drawingType={drawingType}");

            var encoding = GetFileEncoding(equationFilePath);
            var raw = File.ReadAllText(equationFilePath, encoding);
            var newline = DetectNewline(raw);

            var lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
            var output = new List<string>(lines.Count + 64);

            // Key lookup from effectiveDims
            var byKey = effectiveDims.ToDictionary(
                kv => kv.Key.Value, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            // Angle keys
            var angleKeys = new HashSet<string>(
                byKey.Where(kv => kv.Value?.Nominal.Unit == DomUnitKind.Degree)
                     .Select(kv => kv.Key),
                StringComparer.OrdinalIgnoreCase);

            // Special computed lines
            var engravingLine = BuildEngravingStartLine(wedge);

            bool isOverlay = drawingType == DomDrawingType.Overlay;
            double overlayMag = 100.0;
            double overlayScale = 60.8;
            string overlayMagStr = "100";

            if (isOverlay)
            {
                overlayMag = ComputeOverlayMagnificationFromFl(wedge);
                overlayScale = GetOverlayModelViewScaleDecimal(overlayMag);
                overlayMagStr = overlayMag.ToString("0.#####", CultureInfo.InvariantCulture);
            }

            bool engravingTouched = false;
            bool overlayCalTouched = false;
            bool scaleTouched = false;

            int rewritten = 0;

            // Rewrite existing lines
            foreach (var line in lines)
            {
                var m = LineRx.Match(line);
                if (!m.Success)
                {
                    output.Add(line);
                    continue;
                }

                var key = m.Groups["key"].Value;

                // Always rewrite these:
                if (key.Equals("EngravingStart", StringComparison.OrdinalIgnoreCase))
                {
                    output.Add(engravingLine);
                    engravingTouched = true;
                    rewritten++;
                    continue;
                }

                if (isOverlay && key.Equals("overlay_calibration1", StringComparison.OrdinalIgnoreCase))
                {
                    output.Add($"\"overlay_calibration1\" = {overlayMagStr}");
                    overlayCalTouched = true;
                    rewritten++;
                    continue;
                }

                if (isOverlay && key.Equals("scale", StringComparison.OrdinalIgnoreCase))
                {
                    output.Add($"\"scale\" = {F(overlayScale)}");
                    scaleTouched = true;
                    rewritten++;
                    continue;
                }

                // Existing overlay TL override behavior
                if (isOverlay && key.Equals("TL", StringComparison.OrdinalIgnoreCase))
                {
                    output.Add($"\"TL\" = {F(30)}mm");
                    rewritten++;
                    continue;
                }

                // Dimension found? Rewrite it (INCLUDING 0 values)
                if (byKey.TryGetValue(key, out var dim) && dim is not null)
                {
                    WriteDim(output, key, dim, angleKeys.Contains(key));
                    rewritten++;
                    continue;
                }

                // Keep unknown equation lines as-is
                output.Add(line);
            }

            // Append missing dimension lines (INCLUDING 0 values)
            int appended = 0;

            foreach (var (key, dim) in byKey)
            {
                if (dim is null) continue;

                if (!LineExists(output, key))
                {
                    WriteDim(output, key, dim, angleKeys.Contains(key));
                    appended++;
                }
            }

            // Append EngravingStart if missing
            if (!engravingTouched && !LineExists(output, "EngravingStart"))
            {
                output.Add(engravingLine);
                appended++;
            }

            // Append overlay lines if missing
            if (isOverlay)
            {
                if (!overlayCalTouched && !LineExists(output, "overlay_calibration1"))
                {
                    output.Add($"\"overlay_calibration1\" = {overlayMagStr}");
                    appended++;
                }

                if (!scaleTouched && !LineExists(output, "scale"))
                {
                    output.Add($"\"scale\" = {F(overlayScale)}");
                    appended++;
                }
            }

            // COB-only funnel_gap (default funnel gap is in mm)
            if (IsCobWedge(byKey))
            {
                double funnelGapMm = ComputeCobFunnelGapMm(wedge);

                ReplaceOrAppend(output, "funnel_gap",
                    $"\"funnel_gap\" = {F(funnelGapMm)}mm");

                Logger.Info($"[EquationUpdater] COB funnel_gap computed = {funnelGapMm} mm");
            }

            Logger.Info($"[EquationUpdater] Rewritten={rewritten}, Appended={appended}");
            File.WriteAllText(equationFilePath, string.Join(newline, output), encoding);
            Logger.Success($"[EquationUpdater] Equation file updated: {equationFilePath}");
        }

        public static void EnsureAllEquationsExist(SwModelDoc2 model, DomWedgeData wedge)
        {
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));
            EnsureAllEquationsExist(model, wedge.Dimensions);
        }

        /// <summary>
        /// Ensures every key in effectiveDims exists in EquationMgr.
        /// IMPORTANT: 0-value dimensions are NOT skipped.
        /// Uses a reflection-safe Add3/Add2/Add fallback to avoid interop signature mismatches.
        /// </summary>
        public static void EnsureAllEquationsExist(SwModelDoc2 model, IReadOnlyDictionary<DomDimKey, DomDim> effectiveDims)
        {
            if (model is null) return;
            if (effectiveDims is null) return;

            var mgr = model.GetEquationMgr() as SwEquationMgr;
            if (mgr is null) return;

            var existing = GetExistingEquationNames(mgr);

            int added = 0;

            foreach (var (keyObj, dim) in effectiveDims)
            {
                var key = keyObj.Value;
                if (string.IsNullOrWhiteSpace(key) || dim is null)
                    continue;

                if (string.Equals(key, "EngravingStart", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (existing.Contains(key))
                    continue;

                bool isAngle = dim.Nominal.Unit == DomUnitKind.Degree;
                double val = (double)(isAngle ? dim.Nominal.AsDeg() : dim.Nominal.AsMm());

                string eq = isAngle
                    ? $"\"{key}\" = {F(val)}deg"
                    : $"\"{key}\" = {F(val)}mm";

                if (TryAddEquation(mgr, eq))
                {
                    existing.Add(key);
                    added++;
                }
                else
                {
                    Logger.Warn($"[EquationUpdater] Add failed for '{key}' (no suitable Add method succeeded).");
                }
            }

            model.EditRebuild3();
            Logger.Success($"[EquationUpdater] EnsureAllEquationsExist → added={added}");
        }

        /// <summary>
        /// Macro-like mode: push variables directly into the model's EquationMgr without using an equation file.
        /// IMPORTANT: 0-value dimensions are NOT skipped.
        /// </summary>
        public static void UpsertEquationsInModel(
            SwModelDoc2 model,
            IReadOnlyDictionary<DomDimKey, DomDim> effectiveDims,
            DomWedgeData wedge,
            DomDrawingType drawingType)
        {
            if (model is null) throw new ArgumentNullException(nameof(model));
            if (effectiveDims is null) throw new ArgumentNullException(nameof(effectiveDims));
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));

            var mgr = model.GetEquationMgr() as SwEquationMgr;
            if (mgr is null)
                throw new InvalidOperationException("EquationMgr is null.");

            var index = BuildEquationIndex(mgr);

            bool isOverlay = drawingType == DomDrawingType.Overlay;

            double overlayMag = 100.0;
            double overlayScale = 60.8;
            string overlayMagStr = "100";

            if (isOverlay)
            {
                overlayMag = ComputeOverlayMagnificationFromFl(wedge);
                overlayScale = GetOverlayModelViewScaleDecimal(overlayMag);
                overlayMagStr = overlayMag.ToString("0.#####", CultureInfo.InvariantCulture);
            }

            var engravingLine = BuildEngravingStartLine(wedge);

            int upserted = 0;

            foreach (var (keyObj, dim) in effectiveDims)
            {
                var key = keyObj.Value;
                if (string.IsNullOrWhiteSpace(key) || dim is null)
                    continue;

                if (string.Equals(key, "EngravingStart", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool isAngle = dim.Nominal.Unit == DomUnitKind.Degree;
                double val = (double)(isAngle ? dim.Nominal.AsDeg() : dim.Nominal.AsMm());

                string rhs = isAngle ? $"{F(val)}deg" : $"{F(val)}mm";
                string eqText = $"\"{key}\" = {rhs}";

                UpsertEquation(mgr, index, key, eqText);
                upserted++;
            }

            UpsertEquation(mgr, index, "EngravingStart", engravingLine);
            upserted++;

            if (isOverlay)
            {
                UpsertEquation(mgr, index, "overlay_calibration1", $"\"overlay_calibration1\" = {overlayMagStr}");
                upserted++;

                UpsertEquation(mgr, index, "scale", $"\"scale\" = {F(overlayScale)}");
                upserted++;

                UpsertEquation(mgr, index, "TL", $"\"TL\" = {F(30)}mm");
                upserted++;
            }

            model.EditRebuild3();
            Logger.Success($"[EquationUpdater] UpsertEquationsInModel → upserted={upserted}");
        }

        // --------------------------- COB funnel_gap ---------------------------

        private static bool IsCobWedge(Dictionary<string, DomDim> byKey)
        {
            // Loose presence check for the COB funnel-gap math
            return byKey.ContainsKey("FNO") &&
                   byKey.ContainsKey("FNA") &&
                   byKey.ContainsKey("BA") &&
                   byKey.ContainsKey("RA") &&
                   byKey.ContainsKey("FND") &&
                   byKey.ContainsKey("H");
        }

        /// <summary>
        /// Computes COB funnel_gap in millimeters.
        /// Default funnel gap is in mm.
        /// </summary>
        private static double ComputeCobFunnelGapMm(DomWedgeData wedge)
        {
            // DEFAULT funnel gap (mm)
            const double DefaultGapMm = 0.0003;

            // If FNO is missing/0 => return default
            if (!TryGetMm(wedge, "FNO", out var fno) || fno <= 0)
                return DefaultGapMm;

            if (!TryGetDeg(wedge, "FNA", out var fna) ||
                !TryGetDeg(wedge, "BA", out var ba) ||
                !TryGetDeg(wedge, "RA", out var ra) ||
                !TryGetMm(wedge, "FND", out var fnd) ||
                !TryGetMm(wedge, "H", out var h))
                return DefaultGapMm;

            // alpha = FNA / 2
            double alpha = (fna / 2.0) * Math.PI / 180.0;

            // k = BA + RA
            double k = (ba + ra) * Math.PI / 180.0;

            // frac = (1 - tan^2(alpha)*tan^2(k)) / (1 + tan^2(alpha)*tan^2(k))
            double t2 = Math.Tan(alpha) * Math.Tan(alpha) * Math.Tan(k) * Math.Tan(k);
            double frac = (1 - t2) / (1 + t2);

            // gap = (FND * frac - H) / (2*sin(alpha))
            double inside = fnd * frac - h;
            double denom = 2.0 * Math.Sin(alpha);
            if (Math.Abs(denom) < 1e-12) return DefaultGapMm;

            return inside / denom;
        }

        private static bool TryGetMm(DomWedgeData wedge, string key, out double value)
        {
            value = 0;
            if (wedge?.Dimensions is null) return false;

            if (!wedge.Dimensions.TryGetValue(DomDimKey.From(key), out var dim) || dim is null)
                return false;

            if (dim.Nominal.Unit != DomUnitKind.Millimeter)
                return false;

            value = (double)dim.Nominal.AsMm();
            return true;
        }

        private static bool TryGetDeg(DomWedgeData wedge, string key, out double value)
        {
            value = 0;
            if (wedge?.Dimensions is null) return false;

            if (!wedge.Dimensions.TryGetValue(DomDimKey.From(key), out var dim) || dim is null)
                return false;

            if (dim.Nominal.Unit != DomUnitKind.Degree)
                return false;

            value = (double)dim.Nominal.AsDeg();
            return true;
        }

        // --------------------------- Model EquationMgr helpers ---------------------------

        private static HashSet<string> GetExistingEquationNames(SwEquationMgr mgr)
        {
            int count = mgr.GetCount();
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < count; i++)
            {
                var eq = mgr.Equation[i] ?? string.Empty;
                var lhs = ExtractLhsName(eq);
                if (!string.IsNullOrWhiteSpace(lhs))
                    existing.Add(lhs);
            }

            return existing;
        }

        /// <summary>
        /// Tries EquationMgr.Add3 (newer), then Add2, then Add (older).
        /// This avoids "DISP_E_UNKNOWNNAME"/signature mismatch in some interop builds.
        /// </summary>
        private static bool TryAddEquation(object mgr, string equationText)
        {
            try
            {
                // Typical signature:
                // Add3(int Index, string Equation, bool AddToDatabase, int ConfigOption, object ConfigNames)
                mgr.GetType().InvokeMember(
                    "Add3",
                    BindingFlags.InvokeMethod,
                    null,
                    mgr,
                    new object[] { -1, equationText, true, (int)swInConfigurationOpts_e.swThisConfiguration, null });
                return true;
            }
            catch { /* ignore */ }

            try
            {
                // Some builds expose Add2(int Index, string Equation, bool AddToDatabase)
                mgr.GetType().InvokeMember(
                    "Add2",
                    BindingFlags.InvokeMethod,
                    null,
                    mgr,
                    new object[] { -1, equationText, true });
                return true;
            }
            catch { /* ignore */ }

            try
            {
                // Oldest: Add(string Equation)
                mgr.GetType().InvokeMember(
                    "Add",
                    BindingFlags.InvokeMethod,
                    null,
                    mgr,
                    new object[] { equationText });
                return true;
            }
            catch { /* ignore */ }

            return false;
        }

        private static void UpsertEquation(SwEquationMgr mgr, Dictionary<string, int> index, string key, string equationText)
        {
            if (index.TryGetValue(key, out var i))
            {
                try
                {
                    mgr.Equation[i] = equationText;
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[EquationUpdater] Failed to set Equation[{i}] for '{key}': {ex.Message}");
                }
            }

            if (!TryAddEquation(mgr, equationText))
            {
                Logger.Warn($"[EquationUpdater] Failed to add equation for '{key}'.");
                return;
            }

            // Rebuild index entry (best effort)
            try
            {
                index[key] = mgr.GetCount() - 1;
            }
            catch
            {
                // ignore
            }
        }

        private static Dictionary<string, int> BuildEquationIndex(SwEquationMgr mgr)
        {
            int count = mgr.GetCount();
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < count; i++)
            {
                var eq = mgr.Equation[i] ?? string.Empty;
                var lhs = ExtractLhsName(eq);

                if (string.IsNullOrWhiteSpace(lhs))
                    continue;

                if (!map.ContainsKey(lhs))
                    map.Add(lhs, i);
            }

            return map;
        }

        // --------------------------- Equation-file helpers ---------------------------

        private static void ReplaceOrAppend(List<string> lines, string key, string newLine)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                var m = LineRx.Match(lines[i]);
                if (m.Success && m.Groups["key"].Value.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = newLine;
                    return;
                }
            }

            lines.Add(newLine);
        }

        private static void WriteDim(List<string> sink, string key, DomDim dim, bool isAngle)
        {
            double v = (double)(isAngle ? dim.Nominal.AsDeg() : dim.Nominal.AsMm());
            string unit = isAngle ? "deg" : "mm";
            sink.Add($"\"{key}\" = {F(v)}{unit}");
        }

        private static string BuildEngravingStartLine(DomWedgeData wedge)
        {
            double engrMm = 0.0;

            if (wedge?.KValue is not null)
            {
                engrMm = (double)wedge.KValue.ValueMm.AsMm();
            }
            else if (wedge?.Dimensions is not null &&
                     wedge.Dimensions.TryGetValue(DomDimKey.From("TL"), out var tl) &&
                     tl is not null &&
                     tl.Nominal.Unit == DomUnitKind.Millimeter)
            {
                engrMm = (double)tl.Nominal.AsMm() * 0.40;
            }

            return $"\"EngravingStart\" = {F(engrMm)}mm";
        }

        private static double ComputeOverlayMagnificationFromFl(DomWedgeData wedge)
        {
            const double defaultMag = 100.0;

            if (wedge?.Dimensions is null)
                return defaultMag;

            if (!wedge.Dimensions.TryGetValue(DomDimKey.From("FL"), out var flDim) ||
                flDim is null ||
                flDim.Nominal.Unit != DomUnitKind.Millimeter)
            {
                return defaultMag;
            }

            double fl = (double)flDim.Nominal.AsMm();
            if (double.IsNaN(fl) || double.IsInfinity(fl) || fl <= 0.0)
                return defaultMag;

            if (fl <= 0.3403) return 400;
            if (fl <= 0.4572) return 300;
            if (fl <= 0.6908) return 200;
            if (fl <= 1.3766) return 100;
            return 100;
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

        private static int NormalizeScalingToken(object? overlayScaling)
        {
            if (overlayScaling is null) return 100;

            if (double.TryParse(overlayScaling.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            {
                if (d < 10.0) return (int)Math.Round(d * 100.0);
                return (int)Math.Round(d);
            }

            var s = overlayScaling.ToString()?.Trim() ?? "";
            s = s.ToUpperInvariant().Replace(" ", "");
            if (s.StartsWith("X")) s = s[1..];
            if (s.EndsWith("X")) s = s[..^1];
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 100;
        }

        private static bool LineExists(List<string> lines, string key)
        {
            foreach (var line in lines)
            {
                var m = LineRx.Match(line);
                if (m.Success && m.Groups["key"].Value.Equals(key, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static Encoding GetFileEncoding(string path)
        {
            using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
            if (reader.Peek() >= 0) _ = reader.Read();
            return reader.CurrentEncoding;
        }

        private static string DetectNewline(string content)
        {
            if (content.Contains("\r\n")) return "\r\n";
            if (content.Contains('\n')) return "\n";
            return "\r\n";
        }

        private static string ExtractLhsName(string equation)
        {
            int eqIdx = equation.IndexOf('=');
            string lhs = (eqIdx >= 0 ? equation[..eqIdx] : equation).Trim();
            if (lhs.StartsWith("\"") && lhs.EndsWith("\"") && lhs.Length >= 2)
                lhs = lhs[1..^1];
            return lhs;
        }

        /// <summary>
        /// Best/simple behavior for your current requirement:
        /// ALWAYS write equations for provided dimensions (including 0).
        /// Kept for compatibility (some callers may still use it).
        /// </summary>
        private static bool ShouldWriteDimensionEquation(string key, DomDim dim)
        {
            if (string.IsNullOrWhiteSpace(key) || dim is null)
                return false;

            if (AlwaysKeepDimensionKeys.Contains(key))
                return true;

            var unit = dim.Nominal.Unit;
            if (unit == DomUnitKind.Millimeter || unit == DomUnitKind.Degree)
                return true;

            return true;
        }
    }
}
