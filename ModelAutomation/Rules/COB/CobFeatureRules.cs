// ModelAutomation/Rules/COB/CobFeatureRules.cs
using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

using WAD.Runner.ModelAutomation.Execution; // IFeatureRuleSet + FeaturePlan
using WAD.Runner.ModelAutomation.Common;    // SwNames (optional)

namespace WAD.Runner.ModelAutomation.Rules
{
    /// <summary>
    /// COB feature toggle planning (NO SolidWorks calls, NO rebuild).
    ///
    /// PGB FAST MODE:
    /// - Assumes the PGB template/config is already "all suppressed" by default.
    /// - We ONLY UNSUPPRESS the required allowlist (6 items depending on shank).
    /// - (Optional safety) We also suppress the opposite shank's 6 items to prevent leakage.
    /// </summary>
    public sealed class CobFeatureRules : IFeatureRuleSet
    {
        public ModelRuleRunner.FeaturePlan Build(WedgeData wedge, DrawingType drawingType, WedgeSubclass subclass)
        {
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));

            Logger.Info("[CobFeatureRules] Build → start");

            var suppress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unsuppress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var shank = ResolveShankType(wedge);

            // ------------------------------------------------------------
            // COB PGB override: FAST MODE (no massive suppress list)
            // ------------------------------------------------------------
            if (subclass == WedgeSubclass.PGB)
            {
                Logger.Info($"[CobFeatureRules] Parsed → Subclass=PGB, Shank={shank}");

                BuildPgbOnlyPlanFast(shank, suppress, unsuppress);

                // Unsuppress wins
                suppress.RemoveWhere(nm => unsuppress.Contains(nm));

                Logger.Success($"[CobFeatureRules] Build(PGB-fast) → done. unsuppress={unsuppress.Count}, suppress={suppress.Count}");

                return new ModelRuleRunner.FeaturePlan(
                    Suppress: suppress.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                    Unsuppress: unsuppress.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
            }

            // ------------------------------------------------------------
            // Default: FG rules (existing logic)
            // ------------------------------------------------------------

            // TL_feature always active (FG only)
            unsuppress.Add("TL_feature");

            // Engraving toggle (non-overlay only) (FG only)
            if (drawingType is DrawingType.Production or DrawingType.Customer)
            {
                var engraving = TryGetEngravingName();
                if (!string.IsNullOrWhiteSpace(engraving))
                    unsuppress.Add(engraving);
            }

            var foot = ResolveFootOption(wedge);

            Logger.Info($"[CobFeatureRules] Parsed → Subclass=FG, Shank={shank}, Foot={foot}");

            BuildFeaturePlanAlignedWithSpec(wedge, shank, foot, suppress, unsuppress);

            // Unsuppress wins
            suppress.RemoveWhere(nm => unsuppress.Contains(nm));

            Logger.Success($"[CobFeatureRules] Build → done. unsuppress={unsuppress.Count}, suppress={suppress.Count}");

            return new ModelRuleRunner.FeaturePlan(
                Suppress: suppress.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                Unsuppress: unsuppress.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
        }

        // ------------------------------------------------------------
        // PGB-only plan (FAST): only unsuppress allowlist
        // ------------------------------------------------------------
        private static void BuildPgbOnlyPlanFast(
            CobShankType shank,
            HashSet<string> suppress,
            HashSet<string> unsuppress)
        {
            // ✅ UNSUPPRESS ONLY these 6
            var suffix = BuildSuffix(shank);

            unsuppress.Add("TL_feature");
            unsuppress.Add("part_axis");

            unsuppress.Add($"TDF_{suffix}_feature");
            unsuppress.Add($"ISA_20_{suffix}_feature");
            unsuppress.Add($"10BA_{suffix}_feature");
            unsuppress.Add($"10BA_{suffix}_annotation");

            // ✅ Optional safety:
            // suppress the opposite shank's allowlist (only 6 names, cheap)
            var opposite = shank == CobShankType.Std ? CobShankType.Rev180 : CobShankType.Std;
            var oppSuffix = BuildSuffix(opposite);

            suppress.Add($"TDF_{oppSuffix}_feature");
            suppress.Add($"ISA_20_{oppSuffix}_feature");
            suppress.Add($"10BA_{oppSuffix}_feature");
            suppress.Add($"10BA_{oppSuffix}_annotation");

            // If part_axis exists only once (no suffix), don't add it to suppress here.
            // TL_feature should stay ON for both shanks for PGB, so don't suppress it either.
        }

        // --------------------------------------------
        // SPEC-ALIGNED planning (FG / default)
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

            // Extra rule:
            // If BA == 0:
            // - suppress 10BA_STD_feature and 10BA_180_DEG_REV_feature
            // - force-enable SLB feature (selected shank)
            bool baIsZero = IsDimZero(wedge, "BA");
            if (baIsZero)
                Logger.Info("[CobFeatureRules] Business rule triggered: BA == 0 → suppress 10BA (STD + 180_DEG_REV) and force-enable SLB.");

            var opposite = shank == CobShankType.Std ? CobShankType.Rev180 : CobShankType.Std;

            foreach (var b in mandatoryBases)
                foreach (var nm in BuildNameCandidatesWithSketches(b, opposite))
                    suppress.Add(nm);

            foreach (var b in footAndFilletBases_All)
                foreach (var nm in BuildNameCandidatesWithSketches(b, opposite))
                    suppress.Add(nm);

            foreach (var b in optionalBases)
                foreach (var nm in BuildNameCandidatesWithSketches(b, opposite))
                    suppress.Add(nm);

            foreach (var nm in BuildHMandatoryCandidates(opposite))
                suppress.Add(nm);

            foreach (var b in mandatoryBases)
                foreach (var nm in BuildNameCandidatesWithSketches(b, shank))
                    unsuppress.Add(nm);

            foreach (var nm in BuildHMandatoryCandidates(shank))
                unsuppress.Add(nm);

            foreach (var nm in ExpandForShank(footAndFilletBases_All, shank))
                suppress.Add(nm);

            foreach (var nm in ExpandFootForShank(foot, shank))
                unsuppress.Add(nm);

            if (baIsZero)
            {
                foreach (var nm in BuildNameCandidatesWithSketches("10BA", CobShankType.Std))
                    suppress.Add(nm);

                foreach (var nm in BuildNameCandidatesWithSketches("10BA", CobShankType.Rev180))
                    suppress.Add(nm);

                unsuppress.RemoveWhere(nm => nm.StartsWith("10BA_", StringComparison.OrdinalIgnoreCase));
            }

            foreach (var opt in optionalBases)
            {
                bool enabled = ResolveOptionalEnabled(wedge, opt);

                if (baIsZero && opt.Equals("SLB", StringComparison.OrdinalIgnoreCase))
                    enabled = true;

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
                CobFootOption.C_WithCbr => ExpandForShank(new[] { "C", "CBRA", "FR_C" }, shank),
                _ => Array.Empty<string>()
            };
        }

        private static string BuildSuffix(CobShankType shank)
            => shank == CobShankType.Std ? "STD" : "180_DEG_REV";

        private static string BuildFeatureName(string baseName, CobShankType shank)
            => $"{baseName}_{BuildSuffix(shank)}_feature";

        private static IEnumerable<string> BuildNameCandidatesWithSketches(string baseName, CobShankType shank)
        {
            yield return BuildFeatureName(baseName, shank);

            var suffix = BuildSuffix(shank);
            yield return $"{baseName}_{suffix}_sketch";
            yield return $"{baseName}_{suffix}_Sketch";
            yield return $"{baseName}_{suffix}_SKETCH";

            if (baseName.Equals("RA2", StringComparison.OrdinalIgnoreCase) && shank == CobShankType.Rev180)
            {
                yield return "RA2_180_DEF_REV_feature";
                yield return "RA2_180_DEG_REV_feature";

                yield return "RA2_180_DEF_REV_sketch";
                yield return "RA2_180_DEF_REV_Sketch";
                yield return "RA2_180_DEF_REV_SKETCH";

                yield return "RA2_180_DEG_REV_sketch";
                yield return "RA2_180_DEG_REV_Sketch";
                yield return "RA2_180_DEG_REV_SKETCH";
            }
        }

        private static IEnumerable<string> BuildHMandatoryCandidates(CobShankType shank)
        {
            var suffix = BuildSuffix(shank);

            yield return $"H_{suffix}_cut_feature";
            yield return $"H_{suffix}_fix_feature";

            yield return $"H_{suffix}_cut_sketch";
            yield return $"H_{suffix}_cut_Sketch";
            yield return $"H_{suffix}_cut_SKETCH";
        }

        // --------------------------------------------
        // Optional enablement rules (FG only)
        // --------------------------------------------
        private static bool ResolveOptionalEnabled(WedgeData wedge, string featureKey)
        {
            if (wedge is null) return false;

            if (featureKey.Equals("SLB", StringComparison.OrdinalIgnoreCase))
                return IsDimEnabled(wedge, "VBL");

            return IsDimEnabled(wedge, featureKey);
        }

        private static bool IsDimEnabled(WedgeData wedge, string dimKey)
        {
            if (!wedge.Dimensions.TryGetValue(DimensionKey.From(dimKey), out var dim) || dim is null)
                return false;

            return dim.Nominal.Value != 0m;
        }

        private static bool IsDimZero(WedgeData wedge, string dimKey)
        {
            if (wedge?.Dimensions is null) return false;
            if (!wedge.Dimensions.TryGetValue(DimensionKey.From(dimKey), out var dim) || dim is null)
                return false;

            return dim.Nominal.Value == 0m;
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

            CobFootOption baseFoot;

            if (EqualsAny(raw, "SW_G", "G")) baseFoot = CobFootOption.G;
            else if (EqualsAny(raw, "SW_VG", "VG")) baseFoot = CobFootOption.VG;
            else if (EqualsAny(raw, "SW_CG", "CG", "CC")) baseFoot = CobFootOption.CC;
            else baseFoot = CobFootOption.C;

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

        private static string TryGetEngravingName()
        {
            try { return SwNames.Engraving; }
            catch { return "Engraving"; }
        }
    }
}