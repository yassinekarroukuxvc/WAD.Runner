using System;
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Tolerances;

namespace WAD.Runner.ModelAutomation.Rules.FP
{
    /// <summary>
    /// FP tolerances planning (pure logic).
    ///
    /// Domain contract:
    /// - Lengths nominal in mm, angles in deg.
    /// - Tolerances are always mm and stored on Dimension.Tol (Lower/Upper).
    ///
    /// Current scope:
    /// - FP Overlay: push W / FD / T tolerances into overlay sketch parameters.
    /// - When RA2H > 0: push T / RA2H / FL tolerances into the RA2H shank-specific sketch.
    /// - When SLB is enabled (VBL > 0): push T / FL tolerances into the SLB shank-specific sketch.
    /// - When VR > 0 (VW feature active):
    ///   - Compute VR_MAX/MIN (VR_NOM ± VR_UTOL/LTOL) and VRR_MAX/MIN (VRR_NOM ± VRR_UTOL/LTOL).
    ///   - If VW == W → push VW tolerances into VW_LEFT_case_2_overlay_sketch.
    ///   - Else        → push W, VW tolerances + computed VR/VRR bounds into VW_LEFT_case_1_overlay_sketch.
    /// - Target sketch names depend on subclass for the LEFT sketch:
    ///   - FG  -> FG_LEFT_overlay_sketch
    ///   - PGB -> PGB_LEFT_overlay_sketch
    /// </summary>
    public sealed class FpToleranceRules : IToleranceRuleSet
    {
        public TolerancePlan Build(WedgeData wedge, DrawingType drawingType, WedgeSubclass subclass)
        {
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));

            if (drawingType != DrawingType.Overlay)
                return TolerancePlan.Empty;

            var shank = ResolveShankType(wedge);
            var updates = new List<ToleranceUpdate>();

            var prefix = subclass == WedgeSubclass.FG ? "FG" : "PGB";
            bool isStd = shank == CobShankType.Std;
            string frontSketch = isStd ? "PGB_STD_FRONT_overlay_sketch" : "PGB_180_DEG_REV_FRONT_overlay_sketch";

            // ----------------------------------------------------------------
            // W tolerances → LEFT overlay sketch (always, both subclasses)
            // ----------------------------------------------------------------
            AddTolPairMm(updates, wedge, dimKey: "W",
                utolTarget: $"W_UTOL@{prefix}_LEFT_overlay_sketch",
                ltolTarget: $"W_LTOL@{prefix}_LEFT_overlay_sketch");

            // ----------------------------------------------------------------
            // FD + T tolerances → shank-matching FRONT overlay sketch (always)
            // ----------------------------------------------------------------
            AddTolPairMm(updates, wedge, dimKey: "FD",
                utolTarget: $"FD_UTOL@{frontSketch}",
                ltolTarget: $"FD_LTOL@{frontSketch}");

            AddTolPairMm(updates, wedge, dimKey: "T",
                utolTarget: $"T_UTOL@{frontSketch}",
                ltolTarget: $"T_LTOL@{frontSketch}");

            // ----------------------------------------------------------------
            // RA2H sketch tolerances (when RA2H > 0)
            // ----------------------------------------------------------------
            bool ra2hPositive = IsDimPositive(wedge, "RA2H");

            if (ra2hPositive)
            {
                string ra2hSketch = isStd
                    ? "RA2H_STD_FRONT_overlay_sketch"
                    : "RA2H_180_DEG_REV_FRONT_overlay_sketch";

                AddTolPairMm(updates, wedge, dimKey: "FL",
                    utolTarget: $"FL_UTOL@{ra2hSketch}",
                    ltolTarget: $"FL_LTOL@{ra2hSketch}");

                AddTolPairMm(updates, wedge, dimKey: "RA2H",
                    utolTarget: $"RA2H_UTOL@{ra2hSketch}",
                    ltolTarget: $"RA2H_LTOL@{ra2hSketch}");

                AddTolPairMm(updates, wedge, dimKey: "T",
                    utolTarget: $"T_UTOL@{ra2hSketch}",
                    ltolTarget: $"T_LTOL@{ra2hSketch}");

                Logger.Info($"[FpToleranceRules] RA2H > 0 → planned tolerances for {ra2hSketch}.");
            }

            // ----------------------------------------------------------------
            // SLB sketch tolerances (when VBL > 0)
            // ----------------------------------------------------------------
            bool slbEnabled = IsDimPositive(wedge, "VBL");

            if (slbEnabled)
            {
                string slbSketch = isStd
                    ? "SLB_STD_overlay_sketch"
                    : "SLB_180_DEG_REV_overlay_sketch";

                AddTolPairMm(updates, wedge, dimKey: "FL",
                    utolTarget: $"FL_UTOL@{slbSketch}",
                    ltolTarget: $"FL_LTOL@{slbSketch}");

                AddTolPairMm(updates, wedge, dimKey: "T",
                    utolTarget: $"T_UTOL@{slbSketch}",
                    ltolTarget: $"T_LTOL@{slbSketch}");

                Logger.Info($"[FpToleranceRules] SLB enabled (VBL > 0) → planned tolerances for {slbSketch}.");
            }

            // ----------------------------------------------------------------
            // VW sketch tolerances (when VR > 0)
            // Case 2 (VW == W): VW_UTOL, VW_LTOL → VW_LEFT_case_2_overlay_sketch
            // Case 1 (VW != W): W_UTOL, W_LTOL, VW_UTOL, VW_LTOL + computed
            //                   VR_MAX/MIN, VRR_MAX/MIN → VW_LEFT_case_1_overlay_sketch
            // ----------------------------------------------------------------
            bool vrPositive = IsDimPositive(wedge, "VR");

            if (vrPositive)
            {
                bool vwEqualsW = IsDimNominalEqual(wedge, "VW", wedge, "W");

                if (vwEqualsW)
                {
                    // Case 2: only VW tolerances needed
                    AddTolPairMm(updates, wedge, dimKey: "VW",
                        utolTarget: "VW_UTOL@VW_LEFT_case_2_overlay_sketch",
                        ltolTarget: "VW_LTOL@VW_LEFT_case_2_overlay_sketch");

                    Logger.Info("[FpToleranceRules] VW case 2 (VW == W) → planned VW tolerances for VW_LEFT_case_2_overlay_sketch.");
                }
                else
                {
                    // Case 1: W tolerances, VW tolerances, and computed VR/VRR bounds
                    const string Case1Sketch = "VW_LEFT_case_1_overlay_sketch";

                    AddTolPairMm(updates, wedge, dimKey: "W",
                        utolTarget: $"W_UTOL@{Case1Sketch}",
                        ltolTarget: $"W_LTOL@{Case1Sketch}");

                    AddTolPairMm(updates, wedge, dimKey: "VW",
                        utolTarget: $"VW_UTOL@{Case1Sketch}",
                        ltolTarget: $"VW_LTOL@{Case1Sketch}");

                    AddComputedBoundsMm(updates, wedge, nomKey: "VR",
                        maxTarget: $"VR_MAX@{Case1Sketch}",
                        minTarget: $"VR_MIN@{Case1Sketch}");

                    AddComputedBoundsMm(updates, wedge, nomKey: "VRR",
                        maxTarget: $"VRR_MAX@{Case1Sketch}",
                        minTarget: $"VRR_MIN@{Case1Sketch}");

                    Logger.Info($"[FpToleranceRules] VW case 1 (VW != W) → planned W, VW, VR bounds, VRR bounds for {Case1Sketch}.");
                }
            }

            Logger.Info($"[FpToleranceRules] {subclass} Overlay → planned updates={updates.Count} (Shank={shank})");
            return updates.Count == 0 ? TolerancePlan.Empty : new TolerancePlan(updates);
        }

        // ----------------------------------------------------------------
        // Tolerance helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Adds (UTOL, LTOL) updates from wedge dimension tolerances (always mm).
        /// </summary>
        private static void AddTolPairMm(
            List<ToleranceUpdate> updates,
            WedgeData wedge,
            string dimKey,
            string utolTarget,
            string ltolTarget)
        {
            if (!TryGetLengthToleranceMm(wedge, dimKey, out var ltolMm, out var utolMm))
            {
                Logger.Warn($"[FpToleranceRules] Missing/invalid tolerance for '{dimKey}' (skipping: {ltolTarget}, {utolTarget})");
                return;
            }

            updates.Add(new ToleranceUpdate(utolTarget, utolMm, ToleranceUnit.LengthMm));
            updates.Add(new ToleranceUpdate(ltolTarget, ltolMm, ToleranceUnit.LengthMm));

            Logger.Info($"[FpToleranceRules] {dimKey}: LTOL={ltolMm}mm → {ltolTarget}, UTOL={utolMm}mm → {utolTarget}");
        }

        /// <summary>
        /// Computes NOM + UTOL (max bound) and NOM - LTOL (min bound) for a dimension
        /// and pushes them as individual scalar updates.
        /// This is used for VR and VRR in the VW case-1 overlay sketch.
        /// </summary>
        private static void AddComputedBoundsMm(
            List<ToleranceUpdate> updates,
            WedgeData wedge,
            string nomKey,
            string maxTarget,
            string minTarget)
        {
            if (wedge?.Dimensions is null)
            {
                Logger.Warn($"[FpToleranceRules] Cannot compute bounds for '{nomKey}': dimensions null.");
                return;
            }

            var key = DimensionKey.From(nomKey);
            var dim = wedge.TryGet(key);

            if (dim is null)
            {
                Logger.Warn($"[FpToleranceRules] Missing dimension '{nomKey}' for bound computation (skipping: {maxTarget}, {minTarget}).");
                return;
            }

            if (dim.Nominal.Unit != UnitKind.Millimeter)
            {
                Logger.Warn($"[FpToleranceRules] '{nomKey}' nominal unit is {dim.Nominal.Unit} (expected Millimeter).");
                return;
            }

            decimal nom = dim.Nominal.Value;
            decimal utol = dim.Tol.Upper.Value;
            decimal ltol = dim.Tol.Lower.Value;

            decimal maxVal = nom + utol;
            decimal minVal = nom - ltol;

            updates.Add(new ToleranceUpdate(maxTarget, maxVal, ToleranceUnit.LengthMm));
            updates.Add(new ToleranceUpdate(minTarget, minVal, ToleranceUnit.LengthMm));

            Logger.Info($"[FpToleranceRules] {nomKey}: NOM={nom}, UTOL={utol}, LTOL={ltol} → MAX={maxVal} → {maxTarget}, MIN={minVal} → {minTarget}");
        }

        /// <summary>
        /// Extracts length tolerances in mm from the domain model.
        /// Ensures the dimension is a length (nominal mm) because tolerances are length-only.
        /// </summary>
        private static bool TryGetLengthToleranceMm(WedgeData wedge, string dimKey, out decimal lowerMm, out decimal upperMm)
        {
            lowerMm = 0m;
            upperMm = 0m;

            if (wedge?.Dimensions is null) return false;

            var key = DimensionKey.From(dimKey);
            var dim = wedge.TryGet(key);
            if (dim is null)
                return false;

            if (dim.Nominal.Unit != UnitKind.Millimeter)
            {
                Logger.Warn($"[FpToleranceRules] '{dimKey}' nominal unit is {dim.Nominal.Unit} (expected Millimeter).");
                return false;
            }

            lowerMm = dim.Tol.Lower.Value;
            upperMm = dim.Tol.Upper.Value;

            return true;
        }

        // ----------------------------------------------------------------
        // Dimension comparison helper
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns true when the nominal values of two named dimensions are equal.
        /// Treats a missing dimension as 0.
        /// </summary>
        private static bool IsDimNominalEqual(WedgeData wedgeA, string dimKeyA, WedgeData wedgeB, string dimKeyB)
        {
            decimal GetNominal(WedgeData w, string key)
            {
                if (w?.Dimensions is null) return 0m;
                if (!w.Dimensions.TryGetValue(DimensionKey.From(key), out var d) || d is null)
                    return 0m;
                return d.Nominal.Value;
            }

            return GetNominal(wedgeA, dimKeyA) == GetNominal(wedgeB, dimKeyB);
        }

        private static bool IsDimPositive(WedgeData wedge, string dimKey)
        {
            if (wedge?.Dimensions is null) return false;
            if (!wedge.Dimensions.TryGetValue(DimensionKey.From(dimKey), out var dim) || dim is null)
                return false;

            return dim.Nominal.Value > 0m;
        }

        // ----------------------------------------------------------------
        // Shank resolve
        // ----------------------------------------------------------------
        private static CobShankType ResolveShankType(WedgeData wedge)
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
                return CobShankType.Rev180;

            return CobShankType.Std;
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

        private static bool EqualsAny(string value, params string[] options)
        {
            for (int i = 0; i < options.Length; i++)
                if (string.Equals(value, options[i], StringComparison.OrdinalIgnoreCase))
                    return true;

            return false;
        }

        private enum CobShankType { Std, Rev180 }
    }
}