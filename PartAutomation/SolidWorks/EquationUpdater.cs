using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

// Domain aliases (YOUR models)
using DomDim = WAD.Runner.DataManagement.Domain.Dimensions.Dimension;
using DomDimKey = WAD.Runner.DataManagement.Domain.Dimensions.DimensionKey;
using DomWedgeData = WAD.Runner.DataManagement.Domain.Wedge.WedgeData;
using DomDrawingType = WAD.Runner.DataManagement.Domain.Wedge.DrawingType;
using DomUnitKind = WAD.Runner.DataManagement.Domain.Units.UnitKind;

// SW aliases (minimal)
using SwModelDoc2 = SolidWorks.Interop.sldworks.ModelDoc2;
using SwEquationMgr = SolidWorks.Interop.sldworks.EquationMgr;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application; // Logger

namespace WAD.Runner.PartAutomation.SolidWorks
{
    /// <summary>
    /// Updates equations.txt from Domain WedgeData; ensures equations exist in the open part.
    /// Keeps to YOUR domain types (no reference-project dependencies).
    /// Also updates overlay_calibration1 and scale for Overlay drawings, computed from FL.
    /// </summary>
    public static class EquationUpdater
    {
        private static readonly Regex LineRx =
            new(@"^\s*""(?<key>[^""]+)""\s*=.*$", RegexOptions.Compiled);

        private static string F(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

        public static void UpdateEquationFile(string equationFilePath, DomWedgeData wedge, DomDrawingType drawingType)
        {
            Logger.Info($"[EquationUpdater] UpdateEquationFile → path='{equationFilePath}', drawingType={drawingType}");
            if (string.IsNullOrWhiteSpace(equationFilePath) || !File.Exists(equationFilePath))
            {
                Logger.Error($"[EquationUpdater] Equation file not found: {equationFilePath}");
                throw new FileNotFoundException("Equation file not found.", equationFilePath);
            }
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));

            var encoding = GetFileEncoding(equationFilePath);
            Logger.Info($"[EquationUpdater] Detected encoding: {encoding.EncodingName}");
            var raw = File.ReadAllText(equationFilePath, encoding);
            var newline = DetectNewline(raw);

            var lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
            Logger.Info($"[EquationUpdater] Existing lines: {lines.Count}");

            var output = new List<string>(lines.Count + 32);

            // Build lookups from YOUR domain data
            var byKey = wedge.Dimensions.ToDictionary(
                kv => kv.Key.Value, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            Logger.Info($"[EquationUpdater] Wedge dimension keys: {byKey.Count}");

            var angleKeys = new HashSet<string>(
                byKey.Where(kv => kv.Value.Nominal.Unit == DomUnitKind.Degree)
                     .Select(kv => kv.Key),
                StringComparer.OrdinalIgnoreCase);

            Logger.Info($"[EquationUpdater] Angle keys detected: {string.Join(", ", angleKeys)}");

            var engravingLine = BuildEngravingStartLine(wedge);
            Logger.Info($"[EquationUpdater] EngravingStart line → {engravingLine}");

            // ── Overlay mapping (local only, no changes to WedgeData) ──
            bool isOverlay = drawingType == DomDrawingType.Overlay;
            double overlayMag = 100.0;
            double overlayScale = 60.8;
            string overlayMagStr = "100";

            if (isOverlay)
            {
                overlayMag = ComputeOverlayMagnificationFromFl(wedge);
                overlayScale = GetOverlayModelViewScaleDecimal(overlayMag);
                overlayMagStr = overlayMag.ToString("0.#####", CultureInfo.InvariantCulture);

                Logger.Info($"[EquationUpdater] Overlay FL-based → mag={overlayMag}X, scale={overlayScale}");
            }

            bool engravingTouched = false;
            bool overlayCalTouched = false;
            bool scaleTouched = false;
            int rewritten = 0;

            // Rewrite known lines
            foreach (var line in lines)
            {
                var m = LineRx.Match(line);
                if (!m.Success)
                {
                    output.Add(line);
                    continue;
                }

                var key = m.Groups["key"].Value;

                // EngravingStart
                if (key.Equals("EngravingStart", StringComparison.OrdinalIgnoreCase))
                {
                    output.Add(engravingLine);
                    engravingTouched = true;
                    rewritten++;
                    continue;
                }

                // Overlay-specific fields (only when Overlay drawing)
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

                // Dimension value from domain
                if (byKey.TryGetValue(key, out var dim))
                {
                    WriteDim(output, key, dim, angleKeys.Contains(key));
                    rewritten++;
                    continue;
                }

                output.Add(line); // leave as-is
            }

            // Ensure all data keys exist
            int appended = 0;
            foreach (var (key, dim) in byKey)
            {
                if (!LineExists(output, key))
                {
                    WriteDim(output, key, dim, angleKeys.Contains(key));
                    appended++;
                }
            }

            // Ensure EngravingStart exists
            if (!engravingTouched && !LineExists(output, "EngravingStart"))
            {
                output.Add(engravingLine);
                appended++;
            }

            // Ensure overlay_calibration1 + scale for Overlay drawings
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

            Logger.Info($"[EquationUpdater] Rewritten lines: {rewritten}, Appended lines: {appended}");
            File.WriteAllText(equationFilePath, string.Join(newline, output), encoding);
            Logger.Success($"[EquationUpdater] Equation file updated: {equationFilePath}");
        }

        public static void EnsureAllEquationsExist(SwModelDoc2 model, DomWedgeData wedge)
        {
            Logger.Info("[EquationUpdater] EnsureAllEquationsExist → start");
            if (model is null || wedge is null)
            {
                Logger.Warn("[EquationUpdater] Model or WedgeData is null; skipping.");
                return;
            }

            var mgr = (SwEquationMgr)model.GetEquationMgr();
            if (mgr is null)
            {
                Logger.Warn("[EquationUpdater] EquationMgr is null; skipping.");
                return;
            }

            int count = mgr.GetCount();
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < count; i++)
            {
                var eq = mgr.Equation[i] ?? string.Empty;
                var lhs = ExtractLhsName(eq);
                if (!string.IsNullOrWhiteSpace(lhs)) existing.Add(lhs);
            }
            Logger.Info($"[EquationUpdater] Existing equation variables in model: {existing.Count}");

            int added = 0;
            foreach (var (keyObj, dim) in wedge.Dimensions)
            {
                var key = keyObj.Value;
                if (string.Equals(key, "EngravingStart", StringComparison.OrdinalIgnoreCase))
                    continue; // file-driven

                if (existing.Contains(key)) continue;

                bool isAngle = dim.Nominal.Unit == DomUnitKind.Degree;
                double val = (double)(isAngle ? dim.Nominal.AsDeg() : dim.Nominal.AsMm());
                string eq = isAngle
                    ? $"\"{key}\" = {F(val)}deg"
                    : $"\"{key}\" = {F(val)}mm";

                try
                {
                    _ = mgr.Add3(
                        -1,
                        eq,
                        true,
                        (int)swInConfigurationOpts_e.swThisConfiguration, // scope: this configuration
                        null
                    );
                    added++;
                    Logger.Info($"[EquationUpdater] Added equation: {eq}");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[EquationUpdater] Failed to add equation for '{key}': {ex.Message}");
                }
            }

            model.EditRebuild3();
            Logger.Success($"[EquationUpdater] EnsureAllEquationsExist → added={added}, totalExistingNow≈{existing.Count + added}");
        }

        // ---- helpers ----

        private static void WriteDim(List<string> sink, string key, DomDim dim, bool isAngle)
        {
            double v = (double)(isAngle ? dim.Nominal.AsDeg() : dim.Nominal.AsMm());
            string unit = isAngle ? "deg" : "mm";
            var line = $"\"{key}\" = {F(v)}{unit}";
            sink.Add(line);
            Logger.Blue($"[EquationUpdater] Emit line → {line}");
        }

        private static string BuildEngravingStartLine(DomWedgeData wedge)
        {
            double engrMm = 0.0;

            if (wedge.KValue is not null)
            {
                engrMm = (double)wedge.KValue.ValueMm.AsMm();
            }
            else if (wedge.Dimensions.TryGetValue(DomDimKey.From("TL"), out var tl)
                     && tl is not null
                     && tl.Nominal.Unit == DomUnitKind.Millimeter)
            {
                engrMm = (double)tl.Nominal.AsMm() * 0.40;
            }

            return $"\"EngravingStart\" = {F(engrMm)}mm";
        }

        /// <summary>
        /// FL-based overlay magnification, local to this updater.
        /// Thresholds: 100/200/300/400X as in the OTHER project.
        /// </summary>
        private static double ComputeOverlayMagnificationFromFl(DomWedgeData wedge)
        {
            const double defaultMag = 100.0;

            if (wedge is null || wedge.Dimensions is null)
                return defaultMag;

            if (!wedge.Dimensions.TryGetValue(DomDimKey.From("FL"), out var flDim) ||
                flDim is null ||
                flDim.Nominal.Unit != DomUnitKind.Millimeter)
            {
                Logger.Warn("[EquationUpdater] FL missing or not in mm; using default overlay mag=100X.");
                return defaultMag;
            }

            double fl = (double)flDim.Nominal.AsMm();
            if (double.IsNaN(fl) || double.IsInfinity(fl) || fl <= 0.0)
            {
                Logger.Warn($"[EquationUpdater] FL invalid ({fl}); using default overlay mag=100X.");
                return defaultMag;
            }

            double mag;
            if (fl <= 0.3403) { mag = 400; }
            else if (fl <= 0.4572) { mag = 300; }
            else if (fl <= 0.6908) { mag = 200; }
            else if (fl <= 1.3766) { mag = 100; }
            else { mag = 100; }

            Logger.Info($"[EquationUpdater] FL={fl:0.####} mm → overlay mag={mag}X.");
            return mag;
        }

        /// <summary>
        /// Maps overlay magnification (100/200/300/400) to the "scale" value:
        /// 100→60.8, 200→122.7, 300→183, 400→246. Default=60.8.
        /// </summary>
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
                if (d < 10.0) return (int)Math.Round(d * 100.0); // 1..4 → 100..400
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
    }
}
