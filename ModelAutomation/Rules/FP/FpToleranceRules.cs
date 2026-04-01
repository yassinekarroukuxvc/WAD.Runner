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
    /// - Target sketch names depend on subclass:
    ///   - FG  -> FG_...
    ///   - PGB -> PGB_...
    /// </summary>
    public sealed class FpToleranceRules : IToleranceRuleSet
    {
        public TolerancePlan Build(WedgeData wedge, DrawingType drawingType, WedgeSubclass subclass)
        {
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));

            if (drawingType != DrawingType.Overlay)
                return TolerancePlan.Empty;

            var shank = ResolveShankType(wedge); // STD vs 180_DEG_REV
            var updates = new List<ToleranceUpdate>();

            var prefix = subclass == WedgeSubclass.FG ? "FG" : "PGB";

            // Common for both shanks
            AddTolPairMm(updates, wedge, dimKey: "W",
                utolTarget: $"W_UTOL@{prefix}_LEFT_overlay_sketch",
                ltolTarget: $"W_LTOL@{prefix}_LEFT_overlay_sketch");

            if (shank == CobShankType.Std)
            {
                AddTolPairMm(updates, wedge, dimKey: "FD",
                    utolTarget: $"FD_UTOL@PGB_STD_FRONT_overlay_sketch",
                    ltolTarget: $"FD_LTOL@PGB_STD_FRONT_overlay_sketch");

                AddTolPairMm(updates, wedge, dimKey: "T",
                    utolTarget: $"T_UTOL@PGB_STD_FRONT_overlay_sketch",
                    ltolTarget: $"T_LTOL@PGB_STD_FRONT_overlay_sketch");
            }
            else
            {
                AddTolPairMm(updates, wedge, dimKey: "FD",
                    utolTarget: $"FD_UTOL@PGB_180_DEG_REV_FRONT_overlay_sketch",
                    ltolTarget: $"FD_LTOL@PGB_180_DEG_REV_FRONT_overlay_sketch");

                AddTolPairMm(updates, wedge, dimKey: "T",
                    utolTarget: $"T_UTOL@PGB_180_DEG_REV_FRONT_overlay_sketch",
                    ltolTarget: $"T_LTOL@PGB_180_DEG_REV_FRONT_overlay_sketch");
            }

            Logger.Info($"[FpToleranceRules] {subclass} Overlay → planned updates={updates.Count} (Shank={shank})");
            return updates.Count == 0 ? TolerancePlan.Empty : new TolerancePlan(updates);
        }

        /// <summary>
        /// Adds (UTOL, LTOL) updates from wedge dimension tolerances.
        /// Tolerances are always mm in the domain model.
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
        /// Extracts length tolerances in mm from the domain model:
        /// Dimension.Tol.Lower/Upper are Quantity(Millimeter).
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

        // ------------------------------------------------------------
        // Shank resolve
        // ------------------------------------------------------------
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