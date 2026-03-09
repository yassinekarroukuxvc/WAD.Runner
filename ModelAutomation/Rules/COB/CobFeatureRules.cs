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
    /// FG:
    /// - Apply full COB feature planning logic (mandatory + optional + foot rules).
    ///
    /// PGB:
    /// - No foot options, no optional enablement.
    /// - Only shank-type mandatory bases (STD vs 180_DEG_REV).
    /// - We unsuppress the mandatory set for the selected shank and suppress the opposite shank set
    ///   to avoid leakage when templates/configurations are not perfectly "all suppressed".
    ///
    /// Overlay rule:
    /// - Overlay enables cut_feature / cut_plan_feature / ref_point_sketch.
    /// - PGB Overlay uses PGB_LEFT_overlay_sketch.
    /// - FG Overlay uses FG_LEFT_overlay_sketch.
    /// - Both FG and PGB Overlay use the same front overlay sketches:
    ///   PGB_STD_FRONT_overlay_sketch / PGB_180_DEG_REV_FRONT_overlay_sketch.
    ///
    /// Non-overlay rule:
    /// - If drawingType is NOT Overlay: force suppress "cut_feature" and "cut_plan_feature"
    ///   (and remove them from unsuppress if they were added).
    /// </summary>
    public sealed class CobFeatureRules : IFeatureRuleSet
    {
        public ModelRuleRunner.FeaturePlan Build(WedgeData wedge, DrawingType drawingType, WedgeSubclass subclass)
        {
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));

            Logger.Info("[CobFeatureRules] Build → start");

            var shank = ResolveShankType(wedge);

            // ------------------------------------------------------------
            // PGB rules
            // ------------------------------------------------------------
            if (subclass == WedgeSubclass.PGB)
            {
                Logger.Info($"[CobFeatureRules] Subclass=PGB → applying PGB shank-only rules. Shank={shank}");

                var suppress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var unsuppress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // PGB mandatory shank plan
                BuildPgbPlan(shank, suppress, unsuppress);

                // PGB Overlay template rules
                if (drawingType == DrawingType.Overlay)
                {
                    Logger.Info("[CobFeatureRules] Subclass=PGB + Overlay → applying overlay template feature toggles.");
                    BuildOverlayPlan(shank, suppress, unsuppress, "PGB_LEFT_overlay_sketch");
                }

                // If NOT overlay → force suppress cut features
                EnforceCutFeaturesByDrawingType(drawingType, suppress, unsuppress);

                // Unsuppress wins
                suppress.RemoveWhere(nm => unsuppress.Contains(nm));

                Logger.Success($"[CobFeatureRules] Build(PGB) → done. unsuppress={unsuppress.Count}, suppress={suppress.Count}");

                return new ModelRuleRunner.FeaturePlan(
                    Suppress: suppress.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                    Unsuppress: unsuppress.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
            }

            // ------------------------------------------------------------
            // FG rules
            // ------------------------------------------------------------
            var fgSuppress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fgUnsuppress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var foot = ResolveFootOption(wedge);

            // TL_feature always active (FG only)
            fgUnsuppress.Add("TL_feature");

            // Engraving toggle (non-overlay only) (FG only)
            if (drawingType is DrawingType.Production or DrawingType.Customer)
            {
                var engraving = TryGetEngravingName();
                if (!string.IsNullOrWhiteSpace(engraving))
                    fgUnsuppress.Add(engraving);
            }

            // FG Overlay uses same overlay logic as PGB,
            // but with FG_LEFT_overlay_sketch instead of PGB_LEFT_overlay_sketch
            if (drawingType == DrawingType.Overlay)
            {
                Logger.Info("[CobFeatureRules] Subclass=FG + Overlay → applying FG overlay template feature toggles.");
                BuildOverlayPlan(shank, fgSuppress, fgUnsuppress, "FG_LEFT_overlay_sketch");
            }

            Logger.Info($"[CobFeatureRules] Parsed → Subclass=FG, Shank={shank}, Foot={foot}");

            BuildFeaturePlanAlignedWithSpec(wedge, shank, foot, fgSuppress, fgUnsuppress);

            // If NOT overlay → force suppress cut features
            EnforceCutFeaturesByDrawingType(drawingType, fgSuppress, fgUnsuppress);

            // Unsuppress wins
            fgSuppress.RemoveWhere(nm => fgUnsuppress.Contains(nm));

            Logger.Success($"[CobFeatureRules] Build(FG) → done. unsuppress={fgUnsuppress.Count}, suppress={fgSuppress.Count}");

            return new ModelRuleRunner.FeaturePlan(
                Suppress: fgSuppress.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                Unsuppress: fgUnsuppress.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
        }

        // --------------------------------------------
        // DrawingType enforcement for cut features
        // --------------------------------------------
        private static void EnforceCutFeaturesByDrawingType(
            DrawingType drawingType,
            HashSet<string> suppress,
            HashSet<string> unsuppress)
        {
            if (drawingType == DrawingType.Overlay)
                return;

            // Force suppress these when NOT Overlay
            suppress.Add("cut_feature");
            suppress.Add("cut_plan_feature");

            // Safety: if any logic added them to unsuppress, remove
            unsuppress.Remove("cut_feature");
            unsuppress.Remove("cut_plan_feature");

            Logger.Info($"[CobFeatureRules] Non-Overlay ({drawingType}) → force suppress: cut_feature, cut_plan_feature");
        }

        // --------------------------------------------
        // PGB planning (shank-only mandatory)
        // --------------------------------------------
        private static void BuildPgbPlan(
            CobShankType shank,
            HashSet<string> suppress,
            HashSet<string> unsuppress)
        {
            // Common (no suffix)
            unsuppress.Add("TL_feature");
            unsuppress.Add("part_axis");

            var opposite = shank == CobShankType.Std ? CobShankType.Rev180 : CobShankType.Std;

            // Selected shank: force-enable mandatory features (+ sketches)
            foreach (var nm in BuildNameCandidatesWithSketches("TDF", shank))
                unsuppress.Add(nm);

            foreach (var nm in BuildNameCandidatesWithSketches("ISA_20", shank))
                unsuppress.Add(nm);

            foreach (var nm in BuildNameCandidatesWithSketches("10BA", shank))
                unsuppress.Add(nm);

            unsuppress.Add(BuildAnnotationName("10BA", shank));

            // Opposite shank: suppress its mandatory features (+ sketches) to prevent leakage
            foreach (var nm in BuildNameCandidatesWithSketches("TDF", opposite))
                suppress.Add(nm);

            foreach (var nm in BuildNameCandidatesWithSketches("ISA_20", opposite))
                suppress.Add(nm);

            foreach (var nm in BuildNameCandidatesWithSketches("10BA", opposite))
                suppress.Add(nm);

            suppress.Add(BuildAnnotationName("10BA", opposite));

            if (shank == CobShankType.Rev180)
                Logger.Info("[CobFeatureRules] PGB 180_DEG_REV hint: configuration name expected = COB_180_DEG_REV_PGB");
        }

        // --------------------------------------------
        // Shared overlay plan for PGB + FG
        // --------------------------------------------
        private static void BuildOverlayPlan(
            CobShankType shank,
            HashSet<string> suppress,
            HashSet<string> unsuppress,
            string leftOverlaySketch)
        {
            // Default: if shank is unset/invalid, treat as STD
            if (!Enum.IsDefined(typeof(CobShankType), shank))
                shank = CobShankType.Std;

            // Base overlay prep (always)
            unsuppress.Add("ref_point_sketch");
            unsuppress.Add("cut_plan_feature");
            unsuppress.Add("cut_feature");

            // Left overlay sketch depends on subclass/template family
            if (!string.IsNullOrWhiteSpace(leftOverlaySketch))
                unsuppress.Add(leftOverlaySketch);

            // Shank-specific overlay FRONT SKETCH
            // Same real names for both FG and PGB templates
            const string StdFront = "PGB_STD_FRONT_overlay_sketch";
            const string RevFront = "PGB_180_DEG_REV_FRONT_overlay_sketch";

            if (shank == CobShankType.Std)
            {
                unsuppress.Add(StdFront);
                suppress.Add(RevFront); // prevent leakage
            }
            else
            {
                unsuppress.Add(RevFront);
                suppress.Add(StdFront); // prevent leakage
            }
        }

        private static string BuildAnnotationName(string baseName, CobShankType shank)
            => $"{baseName}_{BuildSuffix(shank)}_annotation";

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