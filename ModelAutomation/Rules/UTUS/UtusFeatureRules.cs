// ModelAutomation/Rules/UTUS/UtusFeatureRules.cs
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
    /// UTUS feature toggle planning (NO SolidWorks calls, NO rebuild).
    ///
    /// Same base behavior as COB, except:
    /// - ROUND_BR is always suppressed for both STD and 180_DEG_REV.
    ///
    /// FG:
    /// - Apply full UTUS feature planning logic (mandatory + optional + foot rules).
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
    /// - If VR > 0 in overlay, always suppress the LEFT overlay sketch
    ///   (FG_LEFT_overlay_sketch / PGB_LEFT_overlay_sketch).
    ///
    /// Non-overlay rule:
    /// - If drawingType is NOT Overlay: force suppress "cut_feature" and "cut_plan_feature"
    ///   (and remove them from unsuppress if they were added).
    ///
    /// Additional UTUS rule:
    /// - When shank type is STD, force suppress H_180_DEG_REV_feature
    ///   (and related sketch variants) to prevent leakage.
    /// </summary>
    public sealed class UtusFeatureRules : IFeatureRuleSet
    {
        public ModelRuleRunner.FeaturePlan Build(WedgeData wedge, DrawingType drawingType, WedgeSubclass subclass)
        {
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));

            Logger.Info("[UtusFeatureRules] Build → start");

            var shank = ResolveShankType(wedge);

            // ------------------------------------------------------------
            // PGB rules
            // ------------------------------------------------------------
            if (subclass == WedgeSubclass.PGB)
            {
                Logger.Info($"[UtusFeatureRules] Subclass=PGB → applying PGB shank-only rules. Shank={shank}");

                var suppress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var unsuppress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // PGB mandatory shank plan
                BuildPgbPlan(shank, suppress, unsuppress);

                // PGB Overlay template rules
                if (drawingType == DrawingType.Overlay)
                {
                    Logger.Info("[UtusFeatureRules] Subclass=PGB + Overlay → applying overlay template feature toggles.");
                    BuildOverlayPlan(wedge, shank, suppress, unsuppress, "PGB_LEFT_overlay_sketch");
                }

                // UTUS special rule: ROUND_BR always suppressed for both shanks
                ForceSuppressRoundBrAllShanks(suppress, unsuppress);

                // UTUS special rule: when STD, force suppress H_180_DEG_REV_feature
                ForceSuppressH180DegRevWhenStd(shank, suppress, unsuppress);

                // If NOT overlay → force suppress cut features
                EnforceCutFeaturesByDrawingType(drawingType, suppress, unsuppress);

                // Unsuppress wins
                suppress.RemoveWhere(nm => unsuppress.Contains(nm));

                Logger.Success($"[UtusFeatureRules] Build(PGB) → done. unsuppress={unsuppress.Count}, suppress={suppress.Count}");

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
                Logger.Info("[UtusFeatureRules] Subclass=FG + Overlay → applying FG overlay template feature toggles.");
                BuildOverlayPlan(wedge, shank, fgSuppress, fgUnsuppress, "FG_LEFT_overlay_sketch");
            }

            Logger.Info($"[UtusFeatureRules] Parsed → Subclass=FG, Shank={shank}, Foot={foot}");

            BuildFeaturePlanAlignedWithSpec(wedge, shank, foot, fgSuppress, fgUnsuppress);

            // UTUS special rule: ROUND_BR always suppressed for both shanks
            ForceSuppressRoundBrAllShanks(fgSuppress, fgUnsuppress);

            // UTUS special rule: when STD, force suppress H_180_DEG_REV_feature
            ForceSuppressH180DegRevWhenStd(shank, fgSuppress, fgUnsuppress);

            // If NOT overlay → force suppress cut features
            EnforceCutFeaturesByDrawingType(drawingType, fgSuppress, fgUnsuppress);

            // Unsuppress wins
            fgSuppress.RemoveWhere(nm => fgUnsuppress.Contains(nm));

            Logger.Success($"[UtusFeatureRules] Build(FG) → done. unsuppress={fgUnsuppress.Count}, suppress={fgSuppress.Count}");

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

            Logger.Info($"[UtusFeatureRules] Non-Overlay ({drawingType}) → force suppress: cut_feature, cut_plan_feature");
        }

        // --------------------------------------------
        // UTUS special rules
        // --------------------------------------------
        private static void ForceSuppressRoundBrAllShanks(
            HashSet<string> suppress,
            HashSet<string> unsuppress)
        {
            foreach (var nm in BuildNameCandidatesWithSketches("ROUND_BR", UtusShankType.Std))
                suppress.Add(nm);

            foreach (var nm in BuildNameCandidatesWithSketches("ROUND_BR", UtusShankType.Rev180))
                suppress.Add(nm);

            unsuppress.RemoveWhere(nm =>
                nm.StartsWith("ROUND_BR_", StringComparison.OrdinalIgnoreCase));

            Logger.Info("[UtusFeatureRules] Special rule applied: ROUND_BR forced suppressed for STD and 180_DEG_REV.");
        }

        private static void ForceSuppressH180DegRevWhenStd(
            UtusShankType shank,
            HashSet<string> suppress,
            HashSet<string> unsuppress)
        {
            if (shank != UtusShankType.Std)
                return;

            suppress.Add("H_180_DEG_REV_feature");
            suppress.Add("H_180_DEG_REV_sketch");
            suppress.Add("H_180_DEG_REV_Sketch");
            suppress.Add("H_180_DEG_REV_SKETCH");

            unsuppress.Remove("H_180_DEG_REV_feature");
            unsuppress.Remove("H_180_DEG_REV_sketch");
            unsuppress.Remove("H_180_DEG_REV_Sketch");
            unsuppress.Remove("H_180_DEG_REV_SKETCH");

            Logger.Info("[UtusFeatureRules] Special rule applied: STD shank → force suppress H_180_DEG_REV_feature and related sketch variants.");
        }

        // --------------------------------------------
        // PGB planning (shank-only mandatory)
        // --------------------------------------------
        private static void BuildPgbPlan(
            UtusShankType shank,
            HashSet<string> suppress,
            HashSet<string> unsuppress)
        {
            // Common (no suffix)
            unsuppress.Add("TL_feature");
            unsuppress.Add("part_axis");

            var opposite = shank == UtusShankType.Std ? UtusShankType.Rev180 : UtusShankType.Std;

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

            if (shank == UtusShankType.Rev180)
                Logger.Info("[UtusFeatureRules] PGB 180_DEG_REV hint: configuration name expected = UTUS_180_DEG_REV_PGB");
        }

        // --------------------------------------------
        // Shared overlay plan for PGB + FG
        // --------------------------------------------
        private static void BuildOverlayPlan(
            WedgeData wedge,
            UtusShankType shank,
            HashSet<string> suppress,
            HashSet<string> unsuppress,
            string leftOverlaySketch)
        {
            if (!Enum.IsDefined(typeof(UtusShankType), shank))
                shank = UtusShankType.Std;

            // Base overlay prep (always)
            unsuppress.Add("ref_point_sketch");
            unsuppress.Add("cut_plan_feature");
            unsuppress.Add("cut_feature");

            bool hasVr = IsDimPositive(wedge, "VR");

            // New rule:
            // If VR > 0 in overlay, always suppress LEFT overlay sketch.
            if (!string.IsNullOrWhiteSpace(leftOverlaySketch))
            {
                if (hasVr)
                {
                    suppress.Add(leftOverlaySketch);
                    unsuppress.Remove(leftOverlaySketch);

                    Logger.Info($"[UtusFeatureRules] Overlay rule: VR > 0 → force suppress LEFT overlay sketch '{leftOverlaySketch}'.");
                }
                else
                {
                    unsuppress.Add(leftOverlaySketch);
                }
            }

            // Same real names for both FG and PGB templates
            const string StdFront = "PGB_STD_FRONT_overlay_sketch";
            const string RevFront = "PGB_180_DEG_REV_FRONT_overlay_sketch";

            if (shank == UtusShankType.Std)
            {
                unsuppress.Add(StdFront);
                suppress.Add(RevFront);
            }
            else
            {
                unsuppress.Add(RevFront);
                suppress.Add(StdFront);
            }
        }

        private static string BuildAnnotationName(string baseName, UtusShankType shank)
            => $"{baseName}_{BuildSuffix(shank)}_annotation";

        // --------------------------------------------
        // SPEC-ALIGNED planning (FG / default)
        // --------------------------------------------
        private static void BuildFeaturePlanAlignedWithSpec(
            WedgeData wedge,
            UtusShankType shank,
            UtusFootOption foot,
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
                Logger.Info("[UtusFeatureRules] Business rule triggered: BA == 0 → suppress 10BA (STD + 180_DEG_REV) and force-enable SLB.");

            var opposite = shank == UtusShankType.Std ? UtusShankType.Rev180 : UtusShankType.Std;

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
                foreach (var nm in BuildNameCandidatesWithSketches("10BA", UtusShankType.Std))
                    suppress.Add(nm);

                foreach (var nm in BuildNameCandidatesWithSketches("10BA", UtusShankType.Rev180))
                    suppress.Add(nm);

                unsuppress.RemoveWhere(nm => nm.StartsWith("10BA_", StringComparison.OrdinalIgnoreCase));
            }

            foreach (var opt in optionalBases)
            {
                bool enabled = ResolveOptionalEnabled(wedge, opt);

                if (baIsZero && opt.Equals("SLB", StringComparison.OrdinalIgnoreCase))
                    enabled = true;

                Logger.Info($"[UtusFeatureRules] Optional '{opt}' enabled={enabled}");

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
        private static IEnumerable<string> ExpandForShank(IEnumerable<string> bases, UtusShankType shank)
        {
            foreach (var b in bases)
                foreach (var nm in BuildNameCandidatesWithSketches(b, shank))
                    yield return nm;
        }

        private static IEnumerable<string> ExpandFootForShank(UtusFootOption foot, UtusShankType shank)
        {
            return foot switch
            {
                UtusFootOption.C => ExpandForShank(new[] { "C", "BR_C", "FR_C" }, shank),
                UtusFootOption.G => ExpandForShank(new[] { "G", "BR_G", "FR_G" }, shank),
                UtusFootOption.VG => ExpandForShank(new[] { "VG", "BR_VG", "FR_VG" }, shank),
                UtusFootOption.CC => ExpandForShank(new[] { "C", "CG", "BR_C", "FR_C" }, shank),
                UtusFootOption.C_WithCbr => ExpandForShank(new[] { "C", "CBRA", "FR_C" }, shank),
                _ => Array.Empty<string>()
            };
        }

        private static string BuildSuffix(UtusShankType shank)
            => shank == UtusShankType.Std ? "STD" : "180_DEG_REV";

        private static string BuildFeatureName(string baseName, UtusShankType shank)
            => $"{baseName}_{BuildSuffix(shank)}_feature";

        private static IEnumerable<string> BuildNameCandidatesWithSketches(string baseName, UtusShankType shank)
        {
            yield return BuildFeatureName(baseName, shank);

            var suffix = BuildSuffix(shank);
            yield return $"{baseName}_{suffix}_sketch";
            yield return $"{baseName}_{suffix}_Sketch";
            yield return $"{baseName}_{suffix}_SKETCH";

            if (baseName.Equals("RA2", StringComparison.OrdinalIgnoreCase) && shank == UtusShankType.Rev180)
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

        private static IEnumerable<string> BuildHMandatoryCandidates(UtusShankType shank)
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
        private static UtusShankType ResolveShankType(WedgeData wedge)
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
                return UtusShankType.Rev180;

            return UtusShankType.Std;
        }

        private static UtusFootOption ResolveFootOption(WedgeData wedge)
        {
            var raw =
                GetPropLoose(wedge, "Wed-Foot_Option") ??
                GetPropLoose(wedge, "Wed-FootOption") ??
                GetPropLoose(wedge, "Foot_Option") ??
                GetPropLoose(wedge, "foot_option") ??
                GetPropLoose(wedge, "FootOption") ??
                string.Empty;

            raw = NormalizeDbToken(raw);

            UtusFootOption baseFoot;

            if (EqualsAny(raw, "SW_G", "G")) baseFoot = UtusFootOption.G;
            else if (EqualsAny(raw, "SW_VG", "VG")) baseFoot = UtusFootOption.VG;
            else if (EqualsAny(raw, "SW_CG", "CG", "CC")) baseFoot = UtusFootOption.CC;
            else baseFoot = UtusFootOption.C;

            if (baseFoot == UtusFootOption.C)
            {
                bool allPositive =
                    IsDimPositive(wedge, "CBRA") &&
                    IsDimPositive(wedge, "CBRD") &&
                    IsDimPositive(wedge, "CBRL");

                if (allPositive)
                {
                    Logger.Info("[UtusFeatureRules] Foot rule: base=C and (CBRA/CBRD/CBRL all > 0) → using C_WithCbr.");
                    return UtusFootOption.C_WithCbr;
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

        private enum UtusShankType { Std, Rev180 }

        private enum UtusFootOption
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