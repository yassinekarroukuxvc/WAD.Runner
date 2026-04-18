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

namespace WAD.Runner.ModelAutomation.SolidWorks
{
    public static class EquationUpdater
    {
        private static readonly Regex LineRx =
            new(@"^\s*""(?<key>[^""]+)""\s*=.*$", RegexOptions.Compiled);

        private static string F(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

        // ─────────────────────────────────────────────────────────────────────
        // Write policy
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Controls how zero and missing dimension values are treated when writing the equation file.
        /// CKVD differs from all other wedge types: it must write zeros to override the template.
        /// </summary>
        private sealed record WritePolicy(bool WriteZeros, bool MissingDbKeysAsZero)
        {
            public static WritePolicy For(DomWedgeType wedgeType)
            {
                bool isCkvd = wedgeType == DomWedgeType.CKVD;
                return new WritePolicy(WriteZeros: isCkvd, MissingDbKeysAsZero: isCkvd);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // CKVD-specific key sets
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// DB-driven dimensions for CKVD. Missing keys are written as 0 to override the template.
        /// </summary>
        private static readonly HashSet<string> CkvdDbDrivenKeys =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "TL","TD","TDF",
                "B","E","ER","F","FL","FX","W","X","GD","GR",
                "FR","BR","FRX","BRX",
                "VR","VW","VRA",
                "TIP",
                "k",
                "SymmetryTolerance",
                "BA","FA","GA","ISA"
            };

        private static readonly HashSet<string> CkvdAngleKeys =
            new(StringComparer.OrdinalIgnoreCase) { "BA", "FA", "GA", "ISA" };

        // ─────────────────────────────────────────────────────────────────────
        // Key alias map (DB name → model name)
        // ─────────────────────────────────────────────────────────────────────

        private static readonly Dictionary<string, string> DbToModelKeyAlias =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "RC", "CR" }
            };

        // ─────────────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────────────

        public static void UpdateEquationFile(
            string equationFilePath,
            DomWedgeData wedge,
            DomWedgeType wedgeType,
            DomDrawingType drawingType)
        {
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));
            UpdateEquationFile(equationFilePath, wedge.Dimensions, wedge, wedgeType, drawingType);
        }

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

            var policy = WritePolicy.For(wedgeType);
            var byKey = BuildByKey(effectiveDims);
            var special = BuildSpecialKeyMap(wedge, wedgeType, drawingType);

            Logger.Info(
                $"[EquationUpdater] UpdateEquationFile → '{equationFilePath}', " +
                $"drawingType={drawingType}, wedgeType={wedgeType}, " +
                $"writeZeros={policy.WriteZeros}, missingAsZero={policy.MissingDbKeysAsZero}");

            var encoding = GetFileEncoding(equationFilePath);
            var raw = File.ReadAllText(equationFilePath, encoding);
            var newline = DetectNewline(raw);
            var lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
            var output = new List<string>(lines.Count + 64);

            var zeroProvidedKeys = policy.WriteZeros
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : CollectZeroKeys(byKey);

            int rewritten = 0;

            // ── Pass 1: rewrite existing lines ───────────────────────────────
            foreach (var line in lines)
            {
                var m = LineRx.Match(line);
                if (!m.Success) { output.Add(line); continue; }

                var key = m.Groups["key"].Value;

                // Special keys always win
                if (special.TryGetValue(key, out var specialLine))
                {
                    output.Add(specialLine);
                    rewritten++;
                    continue;
                }

                // DB-driven dim
                if (byKey.TryGetValue(key, out var dim))
                {
                    if (zeroProvidedKeys.Contains(key))
                    {
                        output.Add(line); // keep template value for zeros
                        continue;
                    }

                    WriteDim(output, key, dim);
                    rewritten++;
                    continue;
                }

                // CKVD: missing DB-driven keys → zero
                if (policy.MissingDbKeysAsZero && CkvdDbDrivenKeys.Contains(key))
                {
                    output.Add(MakeZeroLine(key, line));
                    rewritten++;
                    Logger.Info($"[EquationUpdater] CKVD missing key → zero: {key}");
                    continue;
                }

                output.Add(line);
            }

            // ── Pass 2: append anything not already in the file ──────────────
            int appended = 0;

            foreach (var (key, line) in special)
                if (!LineExists(output, key)) { output.Add(line); appended++; }

            foreach (var (key, dim) in byKey)
            {
                if (zeroProvidedKeys.Contains(key)) continue;
                if (!LineExists(output, key)) { WriteDim(output, key, dim); appended++; }
            }

            if (policy.MissingDbKeysAsZero)
            {
                foreach (var key in CkvdDbDrivenKeys)
                {
                    if (LineExists(output, key)) continue;
                    output.Add($"\"{key}\" = 0{(CkvdAngleKeys.Contains(key) ? "deg" : "mm")}");
                    appended++;
                    Logger.Info($"[EquationUpdater] CKVD missing line appended as zero: {key}");
                }
            }

            Logger.Info($"[EquationUpdater] Rewritten={rewritten}, Appended={appended}");
            File.WriteAllText(equationFilePath, string.Join(newline, output), encoding);
            Logger.Success($"[EquationUpdater] Equation file updated: {equationFilePath}");
        }

        /// <summary>
        /// Direct upsert into model EquationMgr (fallback / alternate path).
        /// No rebuild here — orchestrator owns the single rebuild at the end.
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

            var policy = WritePolicy.For(wedgeType);
            var byKey = BuildByKey(effectiveDims);
            var special = BuildSpecialKeyMap(wedge, wedgeType, drawingType);

            var mgr = (EquationMgr)model.GetEquationMgr()
                ?? throw new InvalidOperationException("EquationMgr is null.");

            var index = BuildEquationIndex(mgr);
            int upserted = 0;
            int skippedZero = 0;

            // ── DB-driven dims ────────────────────────────────────────────────
            foreach (var (key, dim) in byKey)
            {
                if (string.Equals(key, "EngravingStart", StringComparison.OrdinalIgnoreCase))
                    continue; // handled via special map

                bool isAngle = dim.Nominal.Unit == DomUnitKind.Degree;
                double val;
                try { val = (double)(isAngle ? dim.Nominal.AsDeg() : dim.Nominal.AsMm()); }
                catch { skippedZero++; continue; }

                if (!policy.WriteZeros && Math.Abs(val) < 1e-12)
                { skippedZero++; continue; }

                UpsertEquation(mgr, index, key, $"\"{key}\" = {(isAngle ? $"{F(val)}deg" : $"{F(val)}mm")}");
                upserted++;
            }

            // ── Special keys ──────────────────────────────────────────────────
            foreach (var (key, line) in special)
            {
                UpsertEquation(mgr, index, key, line);
                upserted++;
            }

            if (rebuild) model.EditRebuild3();

            Logger.Success(
                $"[EquationUpdater] UpsertEquationsInModel → upserted={upserted}, " +
                $"skippedZero={skippedZero}, rebuild={rebuild}, " +
                $"wedgeType={wedgeType}, writeZeros={policy.WriteZeros}");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Special key map
        //
        // Builds every "always-managed" key → equation line for this job.
        // Both public methods consume this; the logic lives in exactly one place.
        // ─────────────────────────────────────────────────────────────────────

        private static Dictionary<string, string> BuildSpecialKeyMap(
            DomWedgeData wedge,
            DomWedgeType wedgeType,
            DomDrawingType drawingType)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Engraving start — always present
            map["EngravingStart"] = BuildEngravingStartLine(wedge);

            // Overlay-only keys
            if (drawingType == DomDrawingType.Overlay)
            {
                double mag = ComputeOverlayMagnification(wedge, wedgeType);
                double scale = GetOverlayModelViewScaleDecimal(mag);
                string magStr = F(mag);

                Logger.Info($"[EquationUpdater] Overlay magnification resolved to {magStr} for wedgeType={wedgeType}");

                map["overlay_calibration1"] = $"\"overlay_calibration1\" = {magStr}";
                map["scale"] = $"\"scale\" = {F(scale)}";
                map["TL"] = $"\"TL\" = {F(30)}mm";
            }

            // funnel_gap — non-CKVD types; already in effectiveDims from the normalizer,
            // but EquationUpdater recomputes it here as the authoritative file-level value.
            if (wedgeType != DomWedgeType.CKVD)
            {
                double gapMm = ComputeFunnelGapMm(wedge);
                map["funnel_gap"] = $"\"funnel_gap\" = {F(gapMm)}mm";
                Logger.Info($"[EquationUpdater] {wedgeType} funnel_gap = {F(gapMm)} mm");
            }

            // non_std_cut — COB / UTUS / FP
            if (wedgeType is DomWedgeType.COB or DomWedgeType.UTUS or DomWedgeType.FP)
            {
                double cutMm = ComputeNonStdCutMm(wedge);
                // The key in the equation file is the full "param@sketch" form
                map["non_std_cut"] = $"\"non_std_cut@ref_point_non_std_cut_sketch\" = {F(cutMm)}mm";
                Logger.Info($"[EquationUpdater] {wedgeType} non_std_cut = {F(cutMm)} mm");
            }

            return map;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Domain computations
        // ─────────────────────────────────────────────────────────────────────

        private static double ComputeFunnelGapMm(DomWedgeData wedge)
        {
            const double DefaultMm = 0.00762; // 0.0003 inch

            if (!TryGetMm(wedge, "FNO", out var fno) || fno <= 0.0) return DefaultMm;
            if (!TryGetDeg(wedge, "FNA", out var fna)) return DefaultMm;
            if (!TryGetDeg(wedge, "BA", out var ba)) return DefaultMm;
            if (!TryGetDeg(wedge, "RA", out var ra)) return DefaultMm;
            if (!TryGetMm(wedge, "H", out var h)) return DefaultMm;

            double alpha = (fna / 2.0) * Math.PI / 180.0;
            double k = (ba + ra) * Math.PI / 180.0;
            double sinAlpha = Math.Sin(alpha);

            if (Math.Abs(sinAlpha) < 1e-12) return DefaultMm;

            double tanA = Math.Tan(alpha);
            double tanK = Math.Tan(k);
            double denom = 1.0 + (tanA * tanK);

            if (Math.Abs(denom) < 1e-12) return DefaultMm;

            double frac = (1.0 - (tanA * tanA) * (tanK * tanK)) / denom;
            double bracket = (fno * frac) - h;
            double fg = bracket / (2.0 * sinAlpha);

            if (double.IsNaN(fg) || double.IsInfinity(fg) || fg <= 0.0) return DefaultMm;

            return fg;
        }

        /// <summary>
        /// non_std_cut must always be strictly greater than VR_MAX + VRR_MAX so the cut
        /// covers the worst-case tolerance-expanded groove width.
        ///
        /// Resolution order:
        /// 1. Use explicit VR_MAX / VRR_MAX if present in wedge dimensions.
        /// 2. Otherwise derive from VR / VRR as NOM + UTOL.
        /// 3. Otherwise fall back to 0.
        /// </summary>
        private static double ComputeNonStdCutMm(DomWedgeData wedge)
        {
            const double MarginMm = 0.01;

            double vrMax = TryGetMaxLikeMm(wedge, explicitMaxKey: "VR_MAX", baseKey: "VR", out var vrSource)
                ? vrSource
                : 0.0;

            double vrrMax = TryGetMaxLikeMm(wedge, explicitMaxKey: "VRR_MAX", baseKey: "VRR", out var vrrSource)
                ? vrrSource
                : 0.0;

            double result = vrMax + vrrMax + vrMax/5;

            Logger.Info(
                $"[EquationUpdater] non_std_cut = VR_MAX({F(vrMax)}) + VRR_MAX({F(vrrMax)}) + margin({F(MarginMm)}) = {F(result)} mm");

            return result;
        }

        /// <summary>
        /// Tries to resolve a max-bound length in mm.
        /// Priority:
        /// - explicit max dimension key (e.g. VR_MAX)
        /// - derived as base nominal + upper tolerance (e.g. VR + VR_UTOL)
        /// </summary>
        private static bool TryGetMaxLikeMm(
            DomWedgeData wedge,
            string explicitMaxKey,
            string baseKey,
            out double value)
        {
            value = 0.0;

            if (TryGetMm(wedge, explicitMaxKey, out var explicitMax))
            {
                value = explicitMax;
                Logger.Info($"[EquationUpdater] Using explicit {explicitMaxKey} = {F(value)} mm");
                return true;
            }

            if (wedge?.Dimensions is null)
                return false;

            if (!wedge.Dimensions.TryGetValue(DomDimKey.From(baseKey), out var dim) || dim is null)
                return false;

            if (dim.Nominal.Unit != DomUnitKind.Millimeter)
                return false;

            double nominal = (double)dim.Nominal.AsMm();
            double upperTol = (double)dim.Tol.Upper.Value;

            value = nominal + upperTol;

            Logger.Info(
                $"[EquationUpdater] Derived {explicitMaxKey} from {baseKey}: NOM({F(nominal)}) + UTOL({F(upperTol)}) = {F(value)} mm");

            return true;
        }

        private static string BuildEngravingStartLine(DomWedgeData wedge)
        {
            double engrMm = 0.0;

            if (wedge.KValue is not null)
            {
                engrMm = (double)wedge.KValue.ValueMm.AsMm();
            }
            else if (wedge.Dimensions.TryGetValue(DomDimKey.From("TL"), out var tl)
                     && tl?.Nominal.Unit == DomUnitKind.Millimeter)
            {
                engrMm = (double)tl.Nominal.AsMm() * 0.40;
            }

            return $"\"EngravingStart\" = {F(engrMm)}mm";
        }

        // ─────────────────────────────────────────────────────────────────────
        // Overlay magnification
        // ─────────────────────────────────────────────────────────────────────

        private static double ComputeOverlayMagnification(DomWedgeData wedge, DomWedgeType wedgeType)
        {
            // CKVD uses FL; all others use T
            string dimKey = wedgeType == DomWedgeType.CKVD ? "FL" : "T";
            return ComputeOverlayMagnificationFromDimension(wedge, dimKey, wedgeType);
        }

        private static double ComputeOverlayMagnificationFromDimension(
            DomWedgeData wedge, string dimKey, DomWedgeType wedgeType)
        {
            const double Default = 100.0;

            if (!TryGetMm(wedge, dimKey, out var value) || value <= 0.0)
            {
                Logger.Warn(
                    $"[EquationUpdater] Overlay mag source '{dimKey}' missing/invalid for {wedgeType}. " +
                    $"Using default {Default}.");
                return Default;
            }

            Logger.Info($"[EquationUpdater] Overlay mag source '{dimKey}' = {F(value)} mm for {wedgeType}");

            if (value <= 0.3403) return 400;
            if (value <= 0.4572) return 300;
            if (value <= 0.6908) return 200;
            return 100;
        }

        private static double GetOverlayModelViewScaleDecimal(double mag)
            => (int)Math.Round(mag) switch
            {
                400 => 246.0,
                300 => 183.0,
                200 => 122.7,
                _ => 60.8
            };

        // ─────────────────────────────────────────────────────────────────────
        // Key alias resolution
        // ─────────────────────────────────────────────────────────────────────

        private static Dictionary<string, DomDim> BuildByKey(
            IReadOnlyDictionary<DomDimKey, DomDim> effectiveDims)
        {
            var result = new Dictionary<string, DomDim>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in effectiveDims)
            {
                string key = kv.Key.Value;

                if (DbToModelKeyAlias.TryGetValue(key, out var alias))
                {
                    key = alias;
                    Logger.Info($"[EquationUpdater] Key alias: '{kv.Key.Value}' → '{alias}'");
                }

                result[key] = kv.Value; // last writer wins
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // File helpers
        // ─────────────────────────────────────────────────────────────────────

        private static HashSet<string> CollectZeroKeys(Dictionary<string, DomDim> byKey)
        {
            var zeros = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (k, dim) in byKey)
            {
                try
                {
                    double v = dim.Nominal.Unit == DomUnitKind.Degree
                        ? (double)dim.Nominal.AsDeg()
                        : (double)dim.Nominal.AsMm();

                    if (Math.Abs(v) < 1e-12) zeros.Add(k);
                }
                catch { zeros.Add(k); }
            }

            return zeros;
        }

        private static void WriteDim(List<string> sink, string key, DomDim dim)
        {
            bool isAngle = dim.Nominal.Unit == DomUnitKind.Degree;
            double v = (double)(isAngle ? dim.Nominal.AsDeg() : dim.Nominal.AsMm());
            sink.Add($"\"{key}\" = {F(v)}{(isAngle ? "deg" : "mm")}");
        }

        /// <summary>Preserves the unit (deg/mm/in) of the existing line when zeroing a CKVD key.</summary>
        private static string MakeZeroLine(string key, string existingLine)
        {
            string unit =
                existingLine.IndexOf("deg", StringComparison.OrdinalIgnoreCase) >= 0 ? "deg" :
                existingLine.IndexOf("mm", StringComparison.OrdinalIgnoreCase) >= 0 ? "mm" :
                existingLine.IndexOf("in", StringComparison.OrdinalIgnoreCase) >= 0 ? "in" :
                (CkvdAngleKeys.Contains(key) ? "deg" : "mm");

            return $"\"{key}\" = 0{unit}";
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

        // ─────────────────────────────────────────────────────────────────────
        // EquationMgr helpers
        // ─────────────────────────────────────────────────────────────────────

        private static void UpsertEquation(
            EquationMgr mgr, Dictionary<string, int> index, string key, string equationText)
        {
            if (index.TryGetValue(key, out var i))
            {
                try { mgr.Equation[i] = equationText; return; }
                catch (Exception ex)
                {
                    Logger.Warn($"[EquationUpdater] Failed to set Equation[{i}] for '{key}': {ex.Message}");
                }
            }

            try
            {
                _ = mgr.Add3(-1, equationText, true,
                    (int)swInConfigurationOpts_e.swThisConfiguration, null);
                index[key] = mgr.GetCount() - 1;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[EquationUpdater] Failed to add equation for '{key}': {ex.Message}");
            }
        }

        private static Dictionary<string, int> BuildEquationIndex(EquationMgr mgr)
        {
            int count = mgr.GetCount();
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < count; i++)
            {
                var lhs = ExtractLhsName(mgr.Equation[i] ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(lhs) && !map.ContainsKey(lhs))
                    map.Add(lhs, i);
            }

            return map;
        }

        private static string ExtractLhsName(string equation)
        {
            int eqIdx = equation.IndexOf('=');
            string lhs = (eqIdx >= 0 ? equation[..eqIdx] : equation).Trim();
            if (lhs.StartsWith("\"") && lhs.EndsWith("\"") && lhs.Length >= 2)
                lhs = lhs[1..^1];
            return lhs;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Dimension read helpers
        // ─────────────────────────────────────────────────────────────────────

        private static bool TryGetMm(DomWedgeData wedge, string key, out double value)
        {
            value = 0;
            if (wedge?.Dimensions is null) return false;
            if (!wedge.Dimensions.TryGetValue(DomDimKey.From(key), out var dim) || dim is null) return false;
            if (dim.Nominal.Unit != DomUnitKind.Millimeter) return false;
            value = (double)dim.Nominal.AsMm();
            return true;
        }

        private static bool TryGetDeg(DomWedgeData wedge, string key, out double value)
        {
            value = 0;
            if (wedge?.Dimensions is null) return false;
            if (!wedge.Dimensions.TryGetValue(DomDimKey.From(key), out var dim) || dim is null) return false;
            if (dim.Nominal.Unit != DomUnitKind.Degree) return false;
            value = (double)dim.Nominal.AsDeg();
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Diagnostics
        // ─────────────────────────────────────────────────────────────────────

        private static void DumpEffectiveDims(
            string tag, IReadOnlyDictionary<DomDimKey, DomDim> effectiveDims)
        {
            if (effectiveDims is null) { Logger.Warn($"[{tag}] effectiveDims = <null>"); return; }

            Logger.Info($"[{tag}] effectiveDims.Count = {effectiveDims.Count}");

            foreach (var kv in effectiveDims.OrderBy(k => k.Key.Value, StringComparer.OrdinalIgnoreCase))
            {
                var key = kv.Key.Value ?? "<null-key>";
                var dim = kv.Value;
                if (dim is null) { Logger.Warn($"[{tag}] {key} = <null-dim>"); continue; }

                try
                {
                    bool deg = dim.Nominal.Unit == DomUnitKind.Degree;
                    double v = deg ? (double)dim.Nominal.AsDeg() : (double)dim.Nominal.AsMm();
                    Logger.Info($"[{tag}] {key} = {F(v)}{(deg ? "deg" : "mm")}");
                }
                catch (Exception ex) { Logger.Warn($"[{tag}] {key} = <read error>: {ex.Message}"); }
            }
        }
    }
}