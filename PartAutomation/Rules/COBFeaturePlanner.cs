// PartAutomation/Rules/COBFeaturePlanner.cs
using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.PartAutomation.SolidWorks;

namespace WAD.Runner.PartAutomation.Rules
{
    /// <summary>
    /// Macro-aligned planner + executor for COB:
    /// - Computes deterministic ON/OFF sets (single plan)
    /// - Applies OFF then ON (macro order) using PartEditor.ApplyFeaturePlan (feature-index + skip-if-same)
    /// - Emits SuppressedGroups (optional) to help EquationUpsert skip unreferenced vars for disabled options
    ///
    /// IMPORTANT:
    /// This is a replacement for COBRules.Apply(...) and should be called by your PartAutomationService.
    ///
    /// Macro alignment note:
    /// - This class should NOT force a rebuild. The caller should decide the rebuild point.
    /// </summary>
    public static class COBFeaturePlanner
    {
        // Public API ----------------------------------------------------------

        public static FeatureTogglePlan Build(WedgeData wedge, DrawingType drawingType)
        {
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));

            var shank = ResolveShankType(wedge);
            var foot = ResolveFootOption(wedge);

            // Optionals based on presence/enabled flags (same as your old COBRules)
            bool hasSlb = ResolveOptionalEnabled(wedge, "SLB");
            bool hasVw = ResolveOptionalEnabled(wedge, "VW");
            bool hasW2 = ResolveOptionalEnabled(wedge, "W2");
            bool hasRa2 = ResolveOptionalEnabled(wedge, "RA2");

            bool froEqualsFr = AreDimensionsEqualMm(wedge, "FRO", "FR");

            var b = FeatureTogglePlan.Create()
                .Note($"COB shank={shank}, foot={foot}, overlay={(drawingType == DrawingType.Overlay)}")
                .Note($"Optionals: SLB={hasSlb}, VW={hasVw}, W2={hasW2}, RA2={hasRa2}")
                .Note($"Rule: FRO==FR → FR_* forced OFF = {froEqualsFr}");

            // SuppressedGroups for EquationUpsert skip-add behavior (best effort)
            if (!hasSlb) b.SuppressedGroup("SLB", true);
            if (!hasVw) b.SuppressedGroup("VW", true);
            if (!hasW2) b.SuppressedGroup("W2", true);
            if (!hasRa2) b.SuppressedGroup("RA2", true);

            if (froEqualsFr)
                b.SuppressedGroup("FR", true);

            BuildCobPlan(b, shank, foot, hasSlb, hasVw, hasW2, hasRa2, froEqualsFr);

            return b.Build().Canonicalize();
        }

        /// <summary>
        /// Applies the COB plan using the optimized macro-aligned PartEditor.ApplyFeaturePlan.
        /// This method intentionally does NOT call Rebuild(); caller controls rebuild timing.
        /// </summary>
        public static FeatureTogglePlan Apply(PartEditor part, WedgeData wedge, DrawingType drawingType)
        {
            if (part is null) throw new ArgumentNullException(nameof(part));
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));

            Logger.Info("[COBFeaturePlanner] Apply → start");

            // Engraving toggle stays outside feature plan (macro flow: set props/toggles first)
            if (drawingType == DrawingType.Production || drawingType == DrawingType.Customer)
            {
                Logger.Info("[COBFeaturePlanner] Non-overlay drawing → apply engraving toggle.");
                BasicPartRules.ApplyEngravingToggle(part);
            }
            else
            {
                Logger.Info("[COBFeaturePlanner] Overlay drawing → no engraving sketch change.");
            }

            var plan = Build(wedge, drawingType);

            if (plan.Notes.Count > 0)
                Logger.Info("[COBFeaturePlanner] " + string.Join(" | ", plan.Notes));

            // Use the macro-aligned bulk apply:
            // - refresh feature index
            // - disable feature tree updates
            // - OFF pass then ON pass
            // - skip suppression calls when already correct
            part.ApplyFeaturePlan(plan, log: msg => Logger.Info("[COBFeaturePlanner] " + msg));

            Logger.Success("[COBFeaturePlanner] Apply → done (no rebuild here)");
            return plan;
        }

        // Planning -----------------------------------------------------------

        private static void BuildCobPlan(
            FeatureTogglePlan.Builder b,
            CobShankType shank,
            CobFootOption foot,
            bool hasSlb,
            bool hasVw,
            bool hasW2,
            bool hasRa2,
            bool froEqualsFr)
        {
            // Mandatory feature BASE names (suffix later)
            var mandatoryBases = new[]
            {
                "TDF",
                "ISA_20",
                "10BA",
                "FRO",
                "ERW",
                "H",
                "funnel_final_diametre",
                "ROUND_BR",
                "COMBINE",
            };

            // Optional feature BASE names
            var optionalBases = new[] { "SLB", "VW", "W2", "RA2" };

            // Foot + fillets BASE names
            var footAndFilletBases_All = new[]
            {
                "C", "G", "VG", "CG", "CBRA",
                "BR_C", "FR_C",
                "BR_G", "FR_G",
                "BR_VG", "FR_VG"
            };

            // Expand all candidates for this shank (including typo variants)
            var allFoot = ExpandForShank(footAndFilletBases_All, shank).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Also include STD vs REV mandatory universe because we may need to switch
            var allMandatoryStd = ExpandForShank(mandatoryBases, CobShankType.Std).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var allMandatoryRev = ExpandForShank(mandatoryBases, CobShankType.Rev180).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var allOptional = ExpandForShank(optionalBases, shank).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Always keep TL ON (common / unsuffixed)
            b.On("TL_feature");

            // --- Shank mandatory switching ---
            if (shank == CobShankType.Std)
            {
                b.Off(allMandatoryRev);
                b.On(allMandatoryStd);
            }
            else
            {
                b.Off(allMandatoryStd);
                b.On(allMandatoryRev);
            }

            // --- Foot selection ---
            var desiredFoot = ExpandFootForShank(foot, shank).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Macro explicitness: everything foot-related OFF first
            b.Off(allFoot);

            // Then ON the desired set, with FR suppression rule enforced
            foreach (var nm in desiredFoot)
            {
                if (froEqualsFr && nm.StartsWith("FR_", StringComparison.OrdinalIgnoreCase))
                    continue;

                b.On(nm);
            }

            // Hard-force OFF FR_* in case desiredFoot contained any (or prior job left them ON)
            if (froEqualsFr)
            {
                foreach (var frBase in new[] { "FR_C", "FR_G", "FR_VG" })
                    b.Off(BuildFeatureNameCandidates(frBase, shank));
            }

            // --- Optional features ---
            // OFF all optionals first, then enable selected ones
            b.Off(allOptional);

            if (hasSlb) b.On(BuildFeatureNameCandidates("SLB", shank));
            if (hasVw) b.On(BuildFeatureNameCandidates("VW", shank));
            if (hasW2) b.On(BuildFeatureNameCandidates("W2", shank));
            if (hasRa2) b.On(BuildFeatureNameCandidates("RA2", shank));
        }

        // Name expansion helpers --------------------------------------------

        private static IEnumerable<string> ExpandForShank(IEnumerable<string> bases, CobShankType shank)
        {
            foreach (var b in bases)
            {
                foreach (var nm in BuildFeatureNameCandidates(b, shank))
                    yield return nm;
            }
        }

        private static IEnumerable<string> ExpandFootForShank(CobFootOption foot, CobShankType shank)
        {
            return foot switch
            {
                CobFootOption.C =>
                    ExpandForShank(new[] { "C", "BR_C", "FR_C" }, shank),

                CobFootOption.G =>
                    ExpandForShank(new[] { "G", "BR_G", "FR_G" }, shank),

                CobFootOption.VG =>
                    ExpandForShank(new[] { "VG", "BR_VG", "FR_VG" }, shank),

                CobFootOption.CC =>
                    ExpandForShank(new[] { "C", "CG", "BR_C", "FR_C" }, shank),

                CobFootOption.C_WithCbr =>
                    // Special: C + CBRA + FR_C, but NOT BR_C
                    ExpandForShank(new[] { "C", "CBRA", "FR_C" }, shank),

                _ => Array.Empty<string>()
            };
        }

        private static string BuildFeatureName(string baseName, CobShankType shank)
        {
            var suffix = shank == CobShankType.Std ? "STD" : "180_DEG_REV";
            return $"{baseName}_{suffix}_feature";
        }

        private static IEnumerable<string> BuildFeatureNameCandidates(string baseName, CobShankType shank)
        {
            yield return BuildFeatureName(baseName, shank);

            // Spec typo variant in 180 list: RA2_180_DEF_REV_feature
            if (shank == CobShankType.Rev180 && baseName.Equals("RA2", StringComparison.OrdinalIgnoreCase))
                yield return "RA2_180_DEF_REV_feature";
        }

        // WedgeData parsing --------------------------------------------------

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

        private static CobFootOption ResolveFootOption(WedgeData wedge)
        {
            var raw =
                GetPropLoose(wedge, "Wed-Foot_Option") ??
                GetPropLoose(wedge, "Wed-FootOption") ??
                GetPropLoose(wedge, "Foot_Option") ??
                GetPropLoose(wedge, "foot_option") ??
                GetPropLoose(wedge, "FootOption") ??
                string.Empty;

            raw = NormalizeDbToken(raw);

            if (EqualsAny(raw, "SW_G", "G")) return CobFootOption.G;
            if (EqualsAny(raw, "SW_VG", "VG")) return CobFootOption.VG;
            if (EqualsAny(raw, "SW_CG", "CG", "CC")) return CobFootOption.CC;
            if (EqualsAny(raw, "SW_F", "F", "CBR", "C_CBR")) return CobFootOption.C_WithCbr;

            return CobFootOption.C;
        }

        private static bool ResolveOptionalEnabled(WedgeData wedge, string key)
        {
            var prop =
                GetPropLoose(wedge, key) ??
                GetPropLoose(wedge, $"{key}_enabled") ??
                GetPropLoose(wedge, $"{key}_Enabled") ??
                GetPropLoose(wedge, $"has_{key}") ??
                GetPropLoose(wedge, $"Has_{key}");

            if (!string.IsNullOrWhiteSpace(prop))
                return ParseBoolLoose(NormalizeDbToken(prop));

            // fallback: dimension exists and nominal > 0
            if (wedge.Dimensions != null && wedge.Dimensions.TryGetValue(new DimensionKey(key), out var dim))
            {
                if (dim.Nominal.Unit == UnitKind.Millimeter) return dim.Nominal.AsMm() > 0m;
                if (dim.Nominal.Unit == UnitKind.Degree) return dim.Nominal.AsDeg() > 0m;
            }

            return false;
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

        private static bool ParseBoolLoose(string s)
        {
            s = (s ?? string.Empty).Trim();
            return EqualsAny(s, "1", "true", "yes", "y", "on", "enabled", "enable");
        }

        private static bool EqualsAny(string value, params string[] options)
            => options.Any(o => string.Equals(value, o, StringComparison.OrdinalIgnoreCase));

        private enum CobShankType { Std, Rev180 }

        private enum CobFootOption
        {
            C,
            G,
            VG,
            CC,
            C_WithCbr
        }

        // FR suppression helpers --------------------------------------------

        private static bool AreDimensionsEqualMm(WedgeData wedge, string k1, string k2, decimal tolMm = 0.000001m)
        {
            if (!TryGetNominalMm(wedge, k1, out var a)) return false;
            if (!TryGetNominalMm(wedge, k2, out var b)) return false;
            return Math.Abs(a - b) <= tolMm;
        }

        private static bool TryGetNominalMm(WedgeData wedge, string key, out decimal mm)
        {
            mm = 0m;
            if (wedge?.Dimensions == null) return false;

            if (!wedge.Dimensions.TryGetValue(new DimensionKey(key), out var dim))
                return false;

            if (dim.Nominal.Unit != UnitKind.Millimeter) return false;

            mm = dim.Nominal.AsMm();
            return true;
        }
    }
}
