// ModelAutomation/Rules/COB/CobFeatureRules.cs
using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Wedge;

using WAD.Runner.ModelAutomation.Execution; // IFeatureRuleSet + FeaturePlan
using WAD.Runner.ModelAutomation.Common;    // SwNames (optional)

namespace WAD.Runner.ModelAutomation.Rules
{
    /// <summary>
    /// COB feature toggle planning (NO SolidWorks calls, NO rebuild).
    ///
    /// Remedy A:
    /// - Include matching *_sketch names for many bases to reduce sketch wake-ups after equation import/rebuild.
    ///
    /// NOTE:
    /// - SLB is controlled by dimension "VBL": nominal == 0 => OFF, else ON
    ///
    /// FOOT:
    /// - Base foot selection comes from property (C/G/VG/CC).
    /// - C_WithCbr is selected ONLY when base foot is C AND (CBRA>0 AND CBRD>0 AND CBRL>0).
    ///
    /// H UPDATE:
    /// - H is no longer a single mandatory base feature.
    /// - Mandatory H per shank is now:
    ///     H_<shank>_cut_feature  (with H_<shank>_cut_sketch)
    ///     H_<shank>_fix_feature
    /// - These must always be UNSUPPRESSED for the selected shank, and SUPPRESSED for the opposite shank.
    /// </summary>
    public sealed class CobFeatureRules : IFeatureRuleSet
    {
        public ModelRuleRunner.FeaturePlan Build(WedgeData wedge, DrawingType drawingType)
        {
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));

            Logger.Info("[CobFeatureRules] Build → start");

            var suppress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unsuppress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // TL_feature always active
            unsuppress.Add("TL_feature");

            // Engraving toggle (non-overlay only)
            if (drawingType is DrawingType.Production or DrawingType.Customer)
            {
                var engraving = TryGetEngravingName();
                if (!string.IsNullOrWhiteSpace(engraving))
                    unsuppress.Add(engraving);
            }

            var shank = ResolveShankType(wedge);
            var foot = ResolveFootOption(wedge);

            Logger.Info($"[CobFeatureRules] Parsed → Shank={shank}, Foot={foot}");

            BuildFeaturePlanAlignedWithSpec(wedge, shank, foot, suppress, unsuppress);

            // Unsuppress wins
            suppress.RemoveWhere(nm => unsuppress.Contains(nm));

            Logger.Success($"[CobFeatureRules] Build → done. unsuppress={unsuppress.Count}, suppress={suppress.Count}");

            return new ModelRuleRunner.FeaturePlan(
                Suppress: suppress.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                Unsuppress: unsuppress.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
        }

        // --------------------------------------------
        // SPEC-ALIGNED planning (no SW calls)
        // --------------------------------------------
        private static void BuildFeaturePlanAlignedWithSpec(
            WedgeData wedge,
            CobShankType shank,
            CobFootOption foot,
            HashSet<string> suppress,
            HashSet<string> unsuppress)
        {
            // 4.1 Mandatory features (always active for selected shank)
            // NOTE: "H" removed because it is now split into cut/fix features (handled separately below).
            var mandatoryBases = new[]
            {
                "TDF",
                "ISA_20",
                "10BA",
                "FRO",
                "ERW",
                "funnel_final_diametre",
                "ROUND_BR",
                "COMBINE",
            };

            // 4.3 Independent optional features
            // NOTE: SLB is controlled by dimension VBL (not property).
            var optionalBases = new[] { "SLB", "VW", "W2", "RA2" };

            // 4.2 Foot + fillets (mutually exclusive sets)
            var footAndFilletBases_All = new[]
            {
                "C", "G", "VG", "CG", "CBRA",
                "BR_C", "FR_C",
                "BR_G", "FR_G",
                "BR_VG", "FR_VG"
            };

            // Business rule: If FRO == FR ⇒ suppress every FR_* feature (selected shank)
            bool froEqualsFr = AreDimensionsEqualMm(wedge, "FRO", "FR");
            if (froEqualsFr)
                Logger.Info("[CobFeatureRules] Business rule triggered: FRO == FR → force OFF all FR_* features.");

            // Shank selection
            var opposite = shank == CobShankType.Std ? CobShankType.Rev180 : CobShankType.Std;

            // ---------------------------
            // 5.1 Shank selection logic
            // Step 1: suppress all opposite shank features
            // Step 2: unsuppress mandatory for selected shank
            // ---------------------------

            // Opposite shank OFF (mandatory + foot + optional)
            foreach (var b in mandatoryBases)
                foreach (var nm in BuildNameCandidatesWithSketches(b, opposite))
                    suppress.Add(nm);

            foreach (var b in footAndFilletBases_All)
                foreach (var nm in BuildNameCandidatesWithSketches(b, opposite))
                    suppress.Add(nm);

            foreach (var b in optionalBases)
                foreach (var nm in BuildNameCandidatesWithSketches(b, opposite))
                    suppress.Add(nm);

            // NEW: Opposite shank OFF for the new H cut/fix features
            foreach (var nm in BuildHMandatoryCandidates(opposite))
                suppress.Add(nm);

            // Mandatory ON for selected shank
            foreach (var b in mandatoryBases)
                foreach (var nm in BuildNameCandidatesWithSketches(b, shank))
                    unsuppress.Add(nm);

            // NEW: Mandatory ON for the new H cut/fix features
            foreach (var nm in BuildHMandatoryCandidates(shank))
                unsuppress.Add(nm);

            // ---------------------------
            // 5.2 Foot option selection logic
            // Step 1: suppress ALL foot-related features for selected shank
            // Step 2: unsuppress only features for chosen foot option
            // ---------------------------
            foreach (var nm in ExpandForShank(footAndFilletBases_All, shank))
                suppress.Add(nm);

            foreach (var nm in ExpandFootForShank(foot, shank))
                unsuppress.Add(nm);

            // Apply business rule (FR off)
            if (froEqualsFr)
            {
                foreach (var frBase in new[] { "FR_C", "FR_G", "FR_VG" })
                    foreach (var nm in BuildNameCandidatesWithSketches(frBase, shank))
                        suppress.Add(nm);

                // Remove any FR_* that may have been ON from foot selection (feature OR sketch variants)
                unsuppress.RemoveWhere(nm => nm.StartsWith("FR_", StringComparison.OrdinalIgnoreCase));
            }

            // ---------------------------
            // 5.3 Optional feature toggle logic (selected shank)
            // ---------------------------
            foreach (var opt in optionalBases)
            {
                bool enabled = ResolveOptionalEnabled(wedge, opt);
                Logger.Info($"[CobFeatureRules] Optional '{opt}' enabled={enabled}");

                foreach (var nm in BuildNameCandidatesWithSketches(opt, shank))
                {
                    if (enabled) unsuppress.Add(nm);
                    else suppress.Add(nm);
                }
            }
        }

        // --------------------------------------------
        // Name expansion helpers
        // --------------------------------------------
        private static IEnumerable<string> ExpandForShank(IEnumerable<string> bases, CobShankType shank)
        {
            foreach (var b in bases)
                foreach (var nm in BuildNameCandidatesWithSketches(b, shank))
                    yield return nm;
        }

        private static IEnumerable<string> ExpandFootForShank(CobFootOption foot, CobShankType shank)
        {
            return foot switch
            {
                CobFootOption.C => ExpandForShank(new[] { "C", "BR_C", "FR_C" }, shank),
                CobFootOption.G => ExpandForShank(new[] { "G", "BR_G", "FR_G" }, shank),
                CobFootOption.VG => ExpandForShank(new[] { "VG", "BR_VG", "FR_VG" }, shank),
                CobFootOption.CC => ExpandForShank(new[] { "C", "CG", "BR_C", "FR_C" }, shank),

                // Special: C + CBRA + FR_C (NOT BR_C)
                CobFootOption.C_WithCbr => ExpandForShank(new[] { "C", "CBRA", "FR_C" }, shank),

                _ => Array.Empty<string>()
            };
        }

        private static string BuildSuffix(CobShankType shank)
            => shank == CobShankType.Std ? "STD" : "180_DEG_REV";

        private static string BuildFeatureName(string baseName, CobShankType shank)
            => $"{baseName}_{BuildSuffix(shank)}_feature";

        /// <summary>
        /// Remedy A: return feature + sketch candidates for baseName.
        /// </summary>
        private static IEnumerable<string> BuildNameCandidatesWithSketches(string baseName, CobShankType shank)
        {
            // Primary feature
            yield return BuildFeatureName(baseName, shank);

            // Sketch candidates
            var suffix = BuildSuffix(shank);
            yield return $"{baseName}_{suffix}_sketch";
            yield return $"{baseName}_{suffix}_Sketch";
            yield return $"{baseName}_{suffix}_SKETCH";

            // RA2 reverse name variant (template/spec inconsistency)
            if (baseName.Equals("RA2", StringComparison.OrdinalIgnoreCase) && shank == CobShankType.Rev180)
            {
                // feature variants
                yield return "RA2_180_DEF_REV_feature"; // spec typo variant
                yield return "RA2_180_DEG_REV_feature"; // defensive

                // sketch variants
                yield return "RA2_180_DEF_REV_sketch";
                yield return "RA2_180_DEF_REV_Sketch";
                yield return "RA2_180_DEF_REV_SKETCH";

                yield return "RA2_180_DEG_REV_sketch";
                yield return "RA2_180_DEG_REV_Sketch";
                yield return "RA2_180_DEG_REV_SKETCH";
            }
        }

        /// <summary>
        /// Mandatory H candidates per shank:
        /// - H_<shank>_cut_feature + its sketch
        /// - H_<shank>_fix_feature
        /// </summary>
        private static IEnumerable<string> BuildHMandatoryCandidates(CobShankType shank)
        {
            var suffix = BuildSuffix(shank);

            // Features
            yield return $"H_{suffix}_cut_feature";
            yield return $"H_{suffix}_fix_feature";

            // Sketch for the cut feature (explicitly specified)
            yield return $"H_{suffix}_cut_sketch";
            yield return $"H_{suffix}_cut_Sketch";
            yield return $"H_{suffix}_cut_SKETCH";
        }

        // --------------------------------------------
        // Optional enablement rules
        // --------------------------------------------
        private static bool ResolveOptionalEnabled(WedgeData wedge, string featureKey)
        {
            if (wedge is null) return false;

            // SLB is controlled by dimension VBL
            if (featureKey.Equals("SLB", StringComparison.OrdinalIgnoreCase))
                return IsDimEnabled(wedge, "VBL");

            // VW/W2/RA2 are controlled by their own dimension key
            return IsDimEnabled(wedge, featureKey);
        }

        private static bool IsDimEnabled(WedgeData wedge, string dimKey)
        {
            if (!wedge.Dimensions.TryGetValue(DimensionKey.From(dimKey), out var dim) || dim is null)
                return false;

            return dim.Nominal.Value != 0m;
        }

        // --------------------------------------------
        // WedgeData parsing (shank + foot)
        // --------------------------------------------
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
            // 1) Base foot from property: C / G / VG / CC (default C)
            var raw =
                GetPropLoose(wedge, "Wed-Foot_Option") ??
                GetPropLoose(wedge, "Wed-FootOption") ??
                GetPropLoose(wedge, "Foot_Option") ??
                GetPropLoose(wedge, "foot_option") ??
                GetPropLoose(wedge, "FootOption") ??
                string.Empty;

            raw = NormalizeDbToken(raw);

            CobFootOption baseFoot;

            if (EqualsAny(raw, "SW_G", "G")) baseFoot = CobFootOption.G;
            else if (EqualsAny(raw, "SW_VG", "VG")) baseFoot = CobFootOption.VG;
            else if (EqualsAny(raw, "SW_CG", "CG", "CC")) baseFoot = CobFootOption.CC;
            else baseFoot = CobFootOption.C;

            // 2) C_WithCbr rule:
            // Only when base foot is C AND ALL of CBRA/CBRD/CBRL are > 0
            if (baseFoot == CobFootOption.C)
            {
                bool allPositive =
                    IsDimPositive(wedge, "CBRA") &&
                    IsDimPositive(wedge, "CBRD") &&
                    IsDimPositive(wedge, "CBRL");

                if (allPositive)
                {
                    Logger.Info("[CobFeatureRules] Foot rule: base=C and (CBRA/CBRD/CBRL all > 0) → using C_WithCbr.");
                    return CobFootOption.C_WithCbr;
                }
            }

            return baseFoot;
        }

        private static bool IsDimPositive(WedgeData wedge, string dimKey)
        {
            if (wedge?.Dimensions is null) return false;
            if (!wedge.Dimensions.TryGetValue(DimensionKey.From(dimKey), out var dim) || dim is null)
                return false;

            return dim.Nominal.Value > 0m;
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

        // --------------------------------------------
        // FR suppression helper
        // --------------------------------------------
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

            if (!wedge.Dimensions.TryGetValue(DimensionKey.From(key), out var dim) || dim is null)
                return false;

            if (!dim.Nominal.IsMm) return false;

            mm = dim.Nominal.AsMm();
            return true;
        }

        private static string TryGetEngravingName()
        {
            try { return SwNames.Engraving; }
            catch { return "Engraving"; }
        }
    }
}