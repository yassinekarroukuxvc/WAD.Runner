// ModelAutomation/SolidWorks/EquationUpdater.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;

using DomDim = WAD.Runner.DataManagement.Domain.Dimensions.Dimension;
using DomDimKey = WAD.Runner.DataManagement.Domain.Dimensions.DimensionKey;
using DomWedgeData = WAD.Runner.DataManagement.Domain.Wedge.WedgeData;
using DomDrawingType = WAD.Runner.DataManagement.Domain.Wedge.DrawingType;
using DomUnitKind = WAD.Runner.DataManagement.Domain.Units.UnitKind;

namespace WAD.Runner.ModelAutomation.SolidWorks
{
    public static class EquationUpdater
    {
        private static readonly Regex LineRx =
            new(@"^\s*""(?<key>[^""]+)""\s*=.*$", RegexOptions.Compiled);

        private static string F(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

        public static void UpdateEquationFile(string equationFilePath, DomWedgeData wedge, DomDrawingType drawingType)
        {
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));
            UpdateEquationFile(equationFilePath, wedge.Dimensions, wedge, drawingType);
        }

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

            Logger.Info($"[ModelAutomation.EquationUpdater] UpdateEquationFile → '{equationFilePath}', drawingType={drawingType}");

            var encoding = GetFileEncoding(equationFilePath);
            var raw = File.ReadAllText(equationFilePath, encoding);
            var newline = DetectNewline(raw);

            var lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
            var output = new List<string>(lines.Count + 32);

            // Dimensions coming from caller (effective dims)
            var byKey = effectiveDims.ToDictionary(
                kv => kv.Key.Value, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            var angleKeys = new HashSet<string>(
                byKey.Where(kv => kv.Value.Nominal.Unit == DomUnitKind.Degree)
                     .Select(kv => kv.Key),
                StringComparer.OrdinalIgnoreCase);

            // CKVD RULE:
            // - CKVD should WRITE 0 values into equations when provided.
            // - COB (and others) should KEEP template value when provided dim is 0.
            bool writeZeros = ShouldWriteZeroDims(wedge);

            // Keys in our provided dimension list that are effectively "do not override"
            // if their nominal is zero (keep equation file value as-is)
            var zeroProvidedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Only build this set when we are in "keep template on zero" mode.
            if (!writeZeros)
            {
                foreach (var (k, dim) in byKey)
                {
                    try
                    {
                        var v = dim.Nominal.Unit == DomUnitKind.Degree
                            ? (double)dim.Nominal.AsDeg()
                            : (double)dim.Nominal.AsMm();

                        if (Math.Abs(v) < 1e-12)
                            zeroProvidedKeys.Add(k);
                    }
                    catch
                    {
                        // If we can't read it reliably, treat as "don't override"
                        zeroProvidedKeys.Add(k);
                    }
                }
            }

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

            foreach (var line in lines)
            {
                var m = LineRx.Match(line);
                if (!m.Success)
                {
                    output.Add(line);
                    continue;
                }

                var key = m.Groups["key"].Value;

                // Always-managed special keys
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

                if (isOverlay && key.Equals("TL", StringComparison.OrdinalIgnoreCase))
                {
                    output.Add($"\"TL\" = {F(30)}mm");
                    rewritten++;
                    continue;
                }

                // RULE:
                // - If key exists in provided dims AND:
                //     - writeZeros == true  => ALWAYS override (including 0)
                //     - writeZeros == false => override only when non-zero; if zero => keep existing line
                // - If key does NOT exist in provided dims => keep existing line
                if (byKey.TryGetValue(key, out var dim))
                {
                    if (!writeZeros && zeroProvidedKeys.Contains(key))
                    {
                        // keep equation file's value (original line)
                        output.Add(line);
                        continue;
                    }

                    WriteDim(output, key, dim, angleKeys.Contains(key));
                    rewritten++;
                    continue;
                }

                // Not in provided dims -> keep equation file line as-is
                output.Add(line);
            }

            int appended = 0;

            // Append provided dims that don't exist in the file yet.
            // - CKVD (writeZeros=true): appends even if value == 0.
            // - Others: skips appending zero provided dims.
            foreach (var (key, dim) in byKey)
            {
                if (!writeZeros && zeroProvidedKeys.Contains(key))
                    continue;

                if (!LineExists(output, key))
                {
                    WriteDim(output, key, dim, angleKeys.Contains(key));
                    appended++;
                }
            }

            if (!engravingTouched && !LineExists(output, "EngravingStart"))
            {
                output.Add(engravingLine);
                appended++;
            }

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

            Logger.Info($"[ModelAutomation.EquationUpdater] Rewritten={rewritten}, Appended={appended}");
            File.WriteAllText(equationFilePath, string.Join(newline, output), encoding);
            Logger.Success($"[ModelAutomation.EquationUpdater] Equation file updated: {equationFilePath}");
        }

        /// <summary>
        /// Direct upsert into model EquationMgr (fallback/alternate).
        /// IMPORTANT: no rebuild here. Orchestrator will do the single rebuild at the end.
        ///
        /// UPDATED BEHAVIOR:
        /// - CKVD: If a provided dim is zero -> DO override (write 0 into the model).
        /// - Others: If a provided dim is zero -> DO NOT override existing equation in the model (keep it).
        /// - If a dim does not exist in provided dims -> it is untouched (keeps model equation).
        /// - Special keys (EngravingStart / overlay vars) are still enforced.
        /// </summary>
        public static void UpsertEquationsInModel(
            ModelDoc2 model,
            IReadOnlyDictionary<DomDimKey, DomDim> effectiveDims,
            DomWedgeData wedge,
            DomDrawingType drawingType,
            bool rebuild = false)
        {
            if (model is null) throw new ArgumentNullException(nameof(model));
            if (effectiveDims is null) throw new ArgumentNullException(nameof(effectiveDims));
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));

            var mgr = (EquationMgr)model.GetEquationMgr();
            if (mgr is null)
                throw new InvalidOperationException("EquationMgr is null.");

            var byNameIndex = BuildEquationIndex(mgr);

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

            // CKVD writes zeros; others keep template/model for zeros.
            bool writeZeros = ShouldWriteZeroDims(wedge);

            int upserted = 0;
            int skippedZero = 0;

            foreach (var (keyObj, dim) in effectiveDims)
            {
                var key = keyObj.Value;

                if (string.Equals(key, "EngravingStart", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool isAngle = dim.Nominal.Unit == DomUnitKind.Degree;

                double val;
                try
                {
                    val = (double)(isAngle ? dim.Nominal.AsDeg() : dim.Nominal.AsMm());
                }
                catch
                {
                    // If we can't resolve it, don't override.
                    skippedZero++;
                    continue;
                }

                // RULE:
                // - Others: if provided value is zero -> keep existing equation (do not upsert)
                // - CKVD: allow writing zero
                if (!writeZeros && Math.Abs(val) < 1e-12)
                {
                    skippedZero++;
                    continue;
                }

                string rhs = isAngle ? $"{F(val)}deg" : $"{F(val)}mm";
                string eqText = $"\"{key}\" = {rhs}";

                UpsertEquation(mgr, byNameIndex, key, eqText);
                upserted++;
            }

            // Always enforce these
            UpsertEquation(mgr, byNameIndex, "EngravingStart", engravingLine);
            upserted++;

            if (isOverlay)
            {
                UpsertEquation(mgr, byNameIndex, "overlay_calibration1", $"\"overlay_calibration1\" = {overlayMagStr}");
                upserted++;

                UpsertEquation(mgr, byNameIndex, "scale", $"\"scale\" = {F(overlayScale)}");
                upserted++;

                UpsertEquation(mgr, byNameIndex, "TL", $"\"TL\" = {F(30)}mm");
                upserted++;
            }

            if (rebuild)
            {
                model.EditRebuild3();
            }

            Logger.Success(
                $"[ModelAutomation.EquationUpdater] UpsertEquationsInModel → upserted={upserted}, skippedZeroOrUnreadable={skippedZero}, rebuild={rebuild}");
        }

        // ------------------------- helpers -------------------------

        private static void UpsertEquation(EquationMgr mgr, Dictionary<string, int> index, string key, string equationText)
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
                    Logger.Warn($"[ModelAutomation.EquationUpdater] Failed to set Equation[{i}] for '{key}': {ex.Message}");
                }
            }

            try
            {
                _ = mgr.Add3(
                    -1,
                    equationText,
                    true,
                    (int)swInConfigurationOpts_e.swThisConfiguration,
                    null);

                index[key] = mgr.GetCount() - 1;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[ModelAutomation.EquationUpdater] Failed to add equation for '{key}': {ex.Message}");
            }
        }

        private static Dictionary<string, int> BuildEquationIndex(EquationMgr mgr)
        {
            int count = mgr.GetCount();
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < count; i++)
            {
                var eq = mgr.Equation[i] ?? string.Empty;
                var lhs = ExtractLhsName(eq);
                if (string.IsNullOrWhiteSpace(lhs)) continue;

                if (!map.ContainsKey(lhs))
                    map.Add(lhs, i);
            }

            return map;
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

        // ----------------------------------------------------------
        // CKVD vs COB behavior switch
        // ----------------------------------------------------------

        /// <summary>
        /// Returns true when the wedge family/type is CKVD, meaning:
        /// - Provided zero dims should be WRITTEN as 0 into equations/model.
        /// For other wedge types (e.g., COB):
        /// - Provided zero dims should NOT override the template/model values.
        ///
        /// Implementation uses reflection to avoid binding to a specific property name
        /// (WedgeType / Type / Family etc.) and to keep this file robust across domain changes.
        /// </summary>
        private static bool ShouldWriteZeroDims(DomWedgeData wedge)
        {
            if (wedge is null) return false;

            // Try common property names. If your domain has a definitive property,
            // you can replace this with a direct check for maximum clarity/perf.
            var wt =
                TryGetStringProp(wedge, "WedgeType") ??
                TryGetStringProp(wedge, "Type") ??
                TryGetStringProp(wedge, "Family") ??
                TryGetStringProp(wedge, "WedgeFamily");

            return string.Equals(wt, "CKVD", StringComparison.OrdinalIgnoreCase);
        }

        private static string? TryGetStringProp(object obj, string propName)
        {
            try
            {
                var p = obj.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.Public);
                if (p is null) return null;

                var v = p.GetValue(obj);
                if (v is null) return null;

                // If it's an enum, ToString() gives "CKVD" etc.
                return v.ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}