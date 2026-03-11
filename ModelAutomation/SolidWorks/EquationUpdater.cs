// ModelAutomation/SolidWorks/EquationUpdater.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
using DomWedgeType = WAD.Runner.DataManagement.Domain.Wedge.WedgeType;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.SolidWorks
{
    public static class EquationUpdater
    {
        private static readonly Regex LineRx =
            new(@"^\s*""(?<key>[^""]+)""\s*=.*$", RegexOptions.Compiled);

        private static string F(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

        // --------------------------------------------------------------------
        // CKVD policy:
        // - CKVD writes provided zeros (writeZeros=true)
        // - AND CKVD treats missing DB-driven dims as 0 (override template)
        // --------------------------------------------------------------------

        /// <summary>
        /// CKVD: keys that are expected to be DB-driven base dimensions.
        /// If missing from effectiveDims => overwrite template value with 0.
        /// </summary>
        private static readonly HashSet<string> CkvdDbDrivenKeys =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // --- base mm dims ---
                "TL","TD","TDF",
                "B","E","ER","F","FL","FX","W","X","GD","GR",
                "FR","BR","FRX","BRX",
                "VR","VW","VRA",
                "TIP",
                "k",
                "SymmetryTolerance",

                // --- base angle dims ---
                "BA","FA","GA","ISA"
            };

        /// <summary>
        /// CKVD: keys that are angle-based (deg). Used when we need to emit 0 for missing keys.
        /// </summary>
        private static readonly HashSet<string> CkvdAngleKeys =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "BA","FA","GA","ISA"
            };

        // --------------------------------------------------------------------
        // Public API (wedgeType-aware, deterministic)
        // --------------------------------------------------------------------

        public static void UpdateEquationFile(
            string equationFilePath,
            DomWedgeData wedge,
            DomWedgeType wedgeType,
            DomDrawingType drawingType)
        {
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));
            UpdateEquationFile(equationFilePath, wedge.Dimensions, wedge, wedgeType, drawingType);
        }

        /// <summary>
        /// Writes equations.txt so that:
        /// - For every key present in effectiveDims, we WRITE/UPDATE its equation line.
        /// - CKVD: writeZeros=true, so 0 values are written (override template).
        /// - Others: writeZeros=false, so 0 values DO NOT override template (keep existing line).
        /// - CKVD: missing DB-driven keys => set to 0 (override template).
        /// - Overlay vars are enforced.
        /// - COB-only: compute + upsert funnel_gap.
        /// </summary>
        public static void UpdateEquationFile(
            string equationFilePath,
            IReadOnlyDictionary<DomDimKey, DomDim> effectiveDims,
            DomWedgeData wedge,
            DomWedgeType wedgeType,
            DomDrawingType drawingType)
        {
            if (string.IsNullOrWhiteSpace(equationFilePath) || !File.Exists(equationFilePath))
                throw new FileNotFoundException("Equation file not found.", equationFilePath);

            if (wedge is null) throw new ArgumentNullException(nameof(wedge));
            if (effectiveDims is null) throw new ArgumentNullException(nameof(effectiveDims));

            DumpEffectiveDims("EquationUpdater.UpdateEquationFile", effectiveDims);

            bool writeZeros = ShouldWriteZeroDims(wedgeType);
            bool missingAsZero = ShouldTreatMissingAsZero(wedgeType);

            Logger.Info(
                $"[ModelAutomation.EquationUpdater] UpdateEquationFile → '{equationFilePath}', drawingType={drawingType}, wedgeType={wedgeType}, writeZeros={writeZeros}, missingAsZero={missingAsZero}");

            var encoding = GetFileEncoding(equationFilePath);
            var raw = File.ReadAllText(equationFilePath, encoding);
            var newline = DetectNewline(raw);

            var lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
            var output = new List<string>(lines.Count + 64);

            var byKey = effectiveDims.ToDictionary(
                kv => kv.Key.Value, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            var providedKeys = new HashSet<string>(byKey.Keys, StringComparer.OrdinalIgnoreCase);

            var angleKeys = new HashSet<string>(
                byKey.Where(kv => kv.Value.Nominal.Unit == DomUnitKind.Degree)
                     .Select(kv => kv.Key),
                StringComparer.OrdinalIgnoreCase);

            var zeroProvidedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                overlayMag = ComputeOverlayMagnification(wedge, wedgeType);
                overlayScale = GetOverlayModelViewScaleDecimal(overlayMag);
                overlayMagStr = overlayMag.ToString("0.#####", CultureInfo.InvariantCulture);

                Logger.Info(
                    $"[ModelAutomation.EquationUpdater] Overlay magnification resolved to {overlayMagStr} for wedgeType={wedgeType}");
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

                if (byKey.TryGetValue(key, out var dim))
                {
                    if (!writeZeros && zeroProvidedKeys.Contains(key))
                    {
                        output.Add(line);
                        continue;
                    }

                    WriteDim(output, key, dim, angleKeys.Contains(key));
                    rewritten++;
                    continue;
                }

                if (missingAsZero && CkvdDbDrivenKeys.Contains(key) && !providedKeys.Contains(key))
                {
                    output.Add(MakeZeroLinePreservingUnit(key, line, CkvdAngleKeys.Contains(key)));
                    rewritten++;
                    Logger.Info($"[ModelAutomation.EquationUpdater] CKVD missing key -> zero: {key}");
                    continue;
                }

                output.Add(line);
            }

            int appended = 0;

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

            if (missingAsZero)
            {
                foreach (var key in CkvdDbDrivenKeys)
                {
                    if (!LineExists(output, key))
                    {
                        var isAngle = CkvdAngleKeys.Contains(key);
                        output.Add($"\"{key}\" = 0{(isAngle ? "deg" : "mm")}");
                        appended++;
                        Logger.Info($"[ModelAutomation.EquationUpdater] CKVD missing line appended as zero: {key}");
                    }
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

            // --------------------------- COB funnel_gap ---------------------------
            if (wedgeType == DomWedgeType.COB || wedgeType == DomWedgeType.UTUS)
            {
                double funnelGapMm = ComputeFunnelGapMm(wedge);

                ReplaceOrAppend(output, "funnel_gap",
                    $"\"funnel_gap\" = {F(funnelGapMm)}mm");

                Logger.Info($"[ModelAutomation.EquationUpdater] {wedgeType} funnel_gap computed = {funnelGapMm} mm");
            }

            Logger.Info($"[ModelAutomation.EquationUpdater] Rewritten={rewritten}, Appended={appended}");
            File.WriteAllText(equationFilePath, string.Join(newline, output), encoding);
            Logger.Success($"[ModelAutomation.EquationUpdater] Equation file updated: {equationFilePath}");
        }

        /// <summary>
        /// Direct upsert into model EquationMgr (fallback/alternate).
        /// IMPORTANT: no rebuild here. Orchestrator will do the single rebuild at the end.
        ///
        /// BEHAVIOR:
        /// - CKVD: If a provided dim is zero -> DO override (write 0 into the model).
        /// - Others: If a provided dim is zero -> DO NOT override existing equation in the model.
        /// - CKVD: If a DB-driven dim is missing from provided dims, we do not touch it here.
        /// - Special keys (EngravingStart / overlay vars) are still enforced.
        /// - COB: funnel_gap is enforced when the COB input set is present.
        /// </summary>
        public static void UpsertEquationsInModel(
            ModelDoc2 model,
            IReadOnlyDictionary<DomDimKey, DomDim> effectiveDims,
            DomWedgeData wedge,
            DomWedgeType wedgeType,
            DomDrawingType drawingType,
            bool rebuild = false)
        {
            if (model is null) throw new ArgumentNullException(nameof(model));
            if (effectiveDims is null) throw new ArgumentNullException(nameof(effectiveDims));
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));

            bool writeZeros = ShouldWriteZeroDims(wedgeType);

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
                overlayMag = ComputeOverlayMagnification(wedge, wedgeType);
                overlayScale = GetOverlayModelViewScaleDecimal(overlayMag);
                overlayMagStr = overlayMag.ToString("0.#####", CultureInfo.InvariantCulture);

                Logger.Info(
                    $"[ModelAutomation.EquationUpdater] Overlay magnification resolved (model) to {overlayMagStr} for wedgeType={wedgeType}");
            }

            var engravingLine = BuildEngravingStartLine(wedge);

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
                    skippedZero++;
                    continue;
                }

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

            // --------------------------- COB funnel_gap ---------------------------
            var byKey = effectiveDims.ToDictionary(kv => kv.Key.Value, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            if (wedgeType == DomWedgeType.COB || wedgeType == DomWedgeType.UTUS)
            {
                double funnelGapMm = ComputeFunnelGapMm(wedge);
                UpsertEquation(mgr, byNameIndex, "funnel_gap", $"\"funnel_gap\" = {F(funnelGapMm)}mm");
                upserted++;

                Logger.Info($"[ModelAutomation.EquationUpdater] COB funnel_gap computed (model) = {funnelGapMm} mm");
            }

            if (rebuild)
            {
                model.EditRebuild3();
            }

            Logger.Success(
                $"[ModelAutomation.EquationUpdater] UpsertEquationsInModel → upserted={upserted}, skippedZeroOrUnreadable={skippedZero}, rebuild={rebuild}, wedgeType={wedgeType}, writeZeros={writeZeros}");
        }

        // --------------------------------------------------------------------
        // Policy
        // --------------------------------------------------------------------

        private static bool ShouldWriteZeroDims(DomWedgeType wedgeType)
            => wedgeType == DomWedgeType.CKVD;

        private static bool ShouldTreatMissingAsZero(DomWedgeType wedgeType)
            => wedgeType == DomWedgeType.CKVD;

        // ------------------------- helpers -------------------------

        private static string MakeZeroLinePreservingUnit(string key, string existingLine, bool isAngleKeyFallback)
        {
            string unit =
                existingLine.IndexOf("deg", StringComparison.OrdinalIgnoreCase) >= 0 ? "deg" :
                existingLine.IndexOf("in", StringComparison.OrdinalIgnoreCase) >= 0 ? "in" :
                existingLine.IndexOf("mm", StringComparison.OrdinalIgnoreCase) >= 0 ? "mm" :
                (isAngleKeyFallback ? "deg" : "mm");

            return $"\"{key}\" = 0{unit}";
        }

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
        /// CKVD uses FL for overlay magnification.
        /// All other wedge types use T for now.
        /// </summary>
        private static double ComputeOverlayMagnification(DomWedgeData wedge, DomWedgeType wedgeType)
        {
            return wedgeType == DomWedgeType.CKVD
                ? ComputeOverlayMagnificationFromDimension(wedge, "FL", wedgeType)
                : ComputeOverlayMagnificationFromDimension(wedge, "T", wedgeType);
        }

        private static double ComputeOverlayMagnificationFromDimension(
            DomWedgeData wedge,
            string dimensionKey,
            DomWedgeType wedgeType)
        {
            const double defaultMag = 100.0;

            if (wedge?.Dimensions is null)
                return defaultMag;

            if (!wedge.Dimensions.TryGetValue(DomDimKey.From(dimensionKey), out var dim) ||
                dim is null ||
                dim.Nominal.Unit != DomUnitKind.Millimeter)
            {
                Logger.Warn(
                    $"[ModelAutomation.EquationUpdater] Overlay magnification source '{dimensionKey}' missing or not mm for wedgeType={wedgeType}. Using default {defaultMag}.");
                return defaultMag;
            }

            double value = (double)dim.Nominal.AsMm();
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0.0)
            {
                Logger.Warn(
                    $"[ModelAutomation.EquationUpdater] Overlay magnification source '{dimensionKey}' invalid ({value}) for wedgeType={wedgeType}. Using default {defaultMag}.");
                return defaultMag;
            }

            Logger.Info(
                $"[ModelAutomation.EquationUpdater] Overlay magnification source '{dimensionKey}' = {value.ToString("0.#####", CultureInfo.InvariantCulture)}mm for wedgeType={wedgeType}");

            if (value <= 0.3403) return 400;
            if (value <= 0.4572) return 300;
            if (value <= 0.6908) return 200;
            if (value <= 1.3766) return 100;
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

        // --------------------------- COB funnel_gap ---------------------------

        private static bool IsCobWedge(Dictionary<string, DomDim> byKey)
        {
            return byKey.ContainsKey("FNO") &&
                   byKey.ContainsKey("FNA") &&
                   byKey.ContainsKey("BA") &&
                   byKey.ContainsKey("RA") &&
                   byKey.ContainsKey("FND") &&
                   byKey.ContainsKey("H");
        }

        private static double ComputeFunnelGapMm(DomWedgeData wedge)
        {
            const double DefaultGapMm = 0.0003;

            if (!TryGetMm(wedge, "FNO", out var fno) || fno <= 0)
                return DefaultGapMm;

            if (!TryGetDeg(wedge, "FNA", out var fna) ||
                !TryGetDeg(wedge, "BA", out var ba) ||
                !TryGetDeg(wedge, "RA", out var ra) ||
                !TryGetMm(wedge, "FND", out var fnd) ||
                !TryGetMm(wedge, "H", out var h))
                return DefaultGapMm;

            double alpha = (fna / 2.0) * Math.PI / 180.0;
            double k = (ba + ra) * Math.PI / 180.0;

            double t2 = Math.Tan(alpha) * Math.Tan(alpha) * Math.Tan(k) * Math.Tan(k);
            double frac = (1 - t2) / (1 + t2);

            double inside = fnd * frac - h;
            double denom = 2.0 * Math.Sin(alpha);
            if (Math.Abs(denom) < 1e-12) return DefaultGapMm;

            Logger.Blue($"Funnel Gap = {inside / denom}");
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

        // --------------------------- diagnostics ---------------------------

        private static void DumpEffectiveDims(
            string tag,
            IReadOnlyDictionary<DomDimKey, DomDim> effectiveDims)
        {
            if (effectiveDims is null)
            {
                Logger.Warn($"[{tag}] effectiveDims = <null>");
                return;
            }

            Logger.Info($"[{tag}] effectiveDims.Count = {effectiveDims.Count}");

            foreach (var kv in effectiveDims.OrderBy(k => k.Key.Value, StringComparer.OrdinalIgnoreCase))
            {
                var key = kv.Key.Value ?? "<null-key>";
                var dim = kv.Value;

                if (dim is null)
                {
                    Logger.Warn($"[{tag}] {key} = <null-dim>");
                    continue;
                }

                try
                {
                    var unit = dim.Nominal.Unit;
                    double nominal = unit == DomUnitKind.Degree
                        ? (double)dim.Nominal.AsDeg()
                        : (double)dim.Nominal.AsMm();

                    var unitStr = unit == DomUnitKind.Degree ? "deg" : "mm";
                    Logger.Info($"[{tag}] {key} = {nominal.ToString("0.#####", CultureInfo.InvariantCulture)}{unitStr}");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[{tag}] {key} = <failed to read nominal> : {ex.Message}");
                }
            }
        }
    }
}