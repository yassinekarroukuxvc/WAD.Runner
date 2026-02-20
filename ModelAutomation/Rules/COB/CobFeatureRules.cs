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
    /// Minimal logging:
    /// - parsed shank + foot
    /// - optionals enabled/disabled
    /// - business rule trigger
    /// - final counts
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

            var optionalBases = new[] { "SLB", "VW", "W2", "RA2" };

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

            // Opposite shank OFF
            foreach (var b in mandatoryBases)
                foreach (var nm in BuildFeatureNameCandidates(b, opposite))
                    suppress.Add(nm);

            foreach (var b in footAndFilletBases_All)
                foreach (var nm in BuildFeatureNameCandidates(b, opposite))
                    suppress.Add(nm);

            foreach (var b in optionalBases)
                foreach (var nm in BuildFeatureNameCandidates(b, opposite))
                    suppress.Add(nm);

            // Mandatory ON for selected shank
            foreach (var b in mandatoryBases)
                foreach (var nm in BuildFeatureNameCandidates(b, shank))
                    unsuppress.Add(nm);

            // Foot logic: OFF all, then ON only selected
            foreach (var nm in ExpandForShank(footAndFilletBases_All, shank))
                suppress.Add(nm);

            foreach (var nm in ExpandFootForShank(foot, shank))
                unsuppress.Add(nm);

            // Apply business rule
            if (froEqualsFr)
            {
                foreach (var frBase in new[] { "FR_C", "FR_G", "FR_VG" })
                    foreach (var nm in BuildFeatureNameCandidates(frBase, shank))
                        suppress.Add(nm);

                unsuppress.RemoveWhere(nm => nm.StartsWith("FR_", StringComparison.OrdinalIgnoreCase));
            }

            // Optionals
            foreach (var opt in optionalBases)
            {
                bool enabled = ResolveOptionalEnabled(wedge, opt);
                Logger.Info($"[CobFeatureRules] Optional '{opt}' enabled={enabled}");

                foreach (var nm in BuildFeatureNameCandidates(opt, shank))
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
                foreach (var nm in BuildFeatureNameCandidates(b, shank))
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
                CobFootOption.C_WithCbr => ExpandForShank(new[] { "C", "CBRA", "FR_C" }, shank),
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

            // RA2 reverse name variant (template/spec inconsistency)
            if (baseName.Equals("RA2", StringComparison.OrdinalIgnoreCase) && shank == CobShankType.Rev180)
            {
                yield return "RA2_180_DEF_REV_feature"; // spec typo variant
                yield return "RA2_180_DEG_REV_feature"; // defensive
            }
        }

        // --------------------------------------------
        // Optional enablement rules
        // --------------------------------------------
        private static bool ResolveOptionalEnabled(WedgeData wedge, string key)
        {
            if (wedge is null) return false;

            // SLB is property-driven
            if (key.Equals("SLB", StringComparison.OrdinalIgnoreCase))
            {
                var prop =
                    GetPropLoose(wedge, "SLB") ??
                    GetPropLoose(wedge, "SLB_enabled") ??
                    GetPropLoose(wedge, "SLB_Enabled") ??
                    GetPropLoose(wedge, "has_SLB") ??
                    GetPropLoose(wedge, "Has_SLB");

                return !string.IsNullOrWhiteSpace(prop) && ParseBoolLoose(NormalizeDbToken(prop));
            }

            // VW/W2/RA2 are dimension-driven: nominal != 0 => ON
            if (!wedge.Dimensions.TryGetValue(DimensionKey.From(key), out var dim) || dim is null)
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
