// ModelAutomation/Rules/CKVD/CkvdToleranceRules.cs
using System;
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Tolerances;

namespace WAD.Runner.ModelAutomation.Rules.CKVD
{
    /// <summary>
    /// CKVD tolerances planning (pure logic).
    ///
    /// Domain contract:
    /// - Lengths nominal in mm, angles in deg.
    /// - Tolerances are always mm and stored on Dimension.Tol (Lower/Upper).
    ///
    /// Scope (mirrors current ModelEditor.ApplyCkvdDerivedDimensions behavior):
    /// 1) Derived "min/max" parameters from VR tolerance:
    ///    - VR_MIN@FG_Wed_VW = VR_nom - |LTOL(VR)|
    ///    - VR_MAX@FG_Wed_VW = VR_nom + |UTOL(VR)|
    ///
    /// 2) Sketch tolerance parameters for selected keys.
    ///    Most sketches use:
    ///      - UTOL@Sketch / LTOL@Sketch
    ///    BUT VW is special in your CKVD template:
    ///      - VW_UTOL@FG_Wed_VW / VW_LTOL@FG_Wed_VW
    ///
    /// Notes:
    /// - All updates are expressed in millimeters (ToleranceUnit.LengthMm),
    ///   because the tolerance applier layer can convert mm → meters when writing to SW parameters.
    /// - Lower/Upper are normalized to positive magnitudes for sketch params.
    /// </summary>
    public sealed class CkvdToleranceRules : IToleranceRuleSet
    {
        public TolerancePlan Build(WedgeData wedge, DrawingType drawingType, WedgeSubclass subclass)
        {
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));

            var updates = new List<ToleranceUpdate>();

            // ------------------------------------------------------------
            // 1) Derived VR_MIN / VR_MAX
            // ------------------------------------------------------------
            AddVrMinMaxMetersAsMmUpdates(
                updates,
                wedge,
                nominalKey: "VR",
                vrMinTarget: "VR_MIN@FG_Wed_VW",
                vrMaxTarget: "VR_MAX@FG_Wed_VW");

            // ------------------------------------------------------------
            // 2) Sketch tolerance params (UTOL/LTOL) per mapping
            // ------------------------------------------------------------
            foreach (var rule in GetTolSketchRules(subclass))
            {
                AddTolPairMmAbs(
                    updates,
                    wedge,
                    dimKey: rule.DimKey,
                    utolTarget: rule.UtolTarget,
                    ltolTarget: rule.LtolTarget);
            }

            Logger.Info($"[CkvdToleranceRules] Planned updates={updates.Count} (Subclass={subclass}, DrawingType={drawingType})");
            return updates.Count == 0 ? TolerancePlan.Empty : new TolerancePlan(updates);
        }

        // ============================================================
        // Rule helpers
        // ============================================================

        private static void AddTolPairMmAbs(
            List<ToleranceUpdate> updates,
            WedgeData wedge,
            string dimKey,
            string utolTarget,
            string ltolTarget)
        {
            if (!TryGetLengthToleranceMm(wedge, dimKey, out var lowerMm, out var upperMm))
            {
                Logger.Warn($"[CkvdToleranceRules] Missing/invalid tolerance for '{dimKey}' (skipping: {ltolTarget}, {utolTarget})");
                return;
            }

            var lAbs = decimal.Abs(lowerMm);
            var uAbs = decimal.Abs(upperMm);

            updates.Add(new ToleranceUpdate(utolTarget, uAbs, ToleranceUnit.LengthMm));
            updates.Add(new ToleranceUpdate(ltolTarget, lAbs, ToleranceUnit.LengthMm));

            Logger.Info($"[CkvdToleranceRules] {dimKey}: |LTOL|={lAbs}mm → {ltolTarget}, |UTOL|={uAbs}mm → {utolTarget}");
        }

        private static void AddVrMinMaxMetersAsMmUpdates(
            List<ToleranceUpdate> updates,
            WedgeData wedge,
            string nominalKey,
            string vrMinTarget,
            string vrMaxTarget)
        {
            if (!TryGetLengthNominalMm(wedge, nominalKey, out var nomMm))
            {
                Logger.Warn($"[CkvdToleranceRules] Missing/invalid nominal for '{nominalKey}' (skipping {vrMinTarget}/{vrMaxTarget}).");
                return;
            }

            if (!TryGetLengthToleranceMm(wedge, nominalKey, out var lowerMm, out var upperMm))
            {
                Logger.Warn($"[CkvdToleranceRules] Missing/invalid tolerance for '{nominalKey}' (skipping {vrMinTarget}/{vrMaxTarget}).");
                return;
            }

            var lAbs = decimal.Abs(lowerMm);
            var uAbs = decimal.Abs(upperMm);

            var minMm = nomMm - lAbs;
            var maxMm = nomMm + uAbs;

            updates.Add(new ToleranceUpdate(vrMinTarget, minMm, ToleranceUnit.LengthMm));
            updates.Add(new ToleranceUpdate(vrMaxTarget, maxMm, ToleranceUnit.LengthMm));

            Logger.Info($"[CkvdToleranceRules] {nominalKey}: NOM={nomMm}mm, |LTOL|={lAbs}mm, |UTOL|={uAbs}mm → " +
                        $"{vrMinTarget}={minMm}mm, {vrMaxTarget}={maxMm}mm");
        }

        // ============================================================
        // Domain extraction helpers
        // ============================================================

        private static bool TryGetLengthNominalMm(WedgeData wedge, string dimKey, out decimal nominalMm)
        {
            nominalMm = 0m;

            if (wedge?.Dimensions is null) return false;

            var key = DimensionKey.From(dimKey);
            var dim = wedge.TryGet(key);
            if (dim is null) return false;

            if (dim.Nominal.Unit != UnitKind.Millimeter)
            {
                Logger.Warn($"[CkvdToleranceRules] '{dimKey}' nominal unit is {dim.Nominal.Unit} (expected Millimeter).");
                return false;
            }

            nominalMm = dim.Nominal.Value;
            return true;
        }

        private static bool TryGetLengthToleranceMm(WedgeData wedge, string dimKey, out decimal lowerMm, out decimal upperMm)
        {
            lowerMm = 0m;
            upperMm = 0m;

            if (wedge?.Dimensions is null) return false;

            var key = DimensionKey.From(dimKey);
            var dim = wedge.TryGet(key);
            if (dim is null) return false;

            if (dim.Nominal.Unit != UnitKind.Millimeter)
            {
                Logger.Warn($"[CkvdToleranceRules] '{dimKey}' nominal unit is {dim.Nominal.Unit} (expected Millimeter).");
                return false;
            }

            lowerMm = dim.Tol.Lower.Value;
            upperMm = dim.Tol.Upper.Value;

            return true;
        }

        // ============================================================
        // Mapping rules (pure config)
        // ============================================================

        private sealed record TolSketchRule(string DimKey, string UtolTarget, string LtolTarget);

        private static IReadOnlyList<TolSketchRule> GetTolSketchRules(WedgeSubclass subclass)
        {
            // PGB: standard UTOL/LTOL
            if (subclass == WedgeSubclass.PGB)
            {
                return new List<TolSketchRule>
                {
                    new("FL", "UTOL@PGB_Wed_FL", "LTOL@PGB_Wed_FL"),
                    new("W",  "UTOL@PGB_Wed_W",  "LTOL@PGB_Wed_W"),
                };
            }

            // FG: mostly standard UTOL/LTOL, but VW is special
            return new List<TolSketchRule>
            {
                new("FL", "UTOL@FG_Wed_FL", "LTOL@FG_Wed_FL"),
                new("W",  "UTOL@FG_Wed_W",  "LTOL@FG_Wed_W"),
                new("B",  "UTOL@FG_Wed_B",  "LTOL@FG_Wed_B"),

                // ✅ Special case you requested:
                // VW uses VW_UTOL/VW_LTOL parameter names on FG_Wed_VW
                new("VW", "VW_UTOL@FG_Wed_VW", "VW_LTOL@FG_Wed_VW"),

                // VR remains standard UTOL/LTOL on its sketch
                new("VR", "UTOL@FG_Wed_VR", "LTOL@FG_Wed_VR"),
            };
        }
    }
}