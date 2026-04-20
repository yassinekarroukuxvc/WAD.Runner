using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Rules.Common;

namespace WAD.Runner.DrawingAutomation.Rules.COB;

/// <summary>
/// COB drawing feature toggle planning.
///
/// IMPORTANT:
/// The drawing uses the same SolidWorks feature/sketch names as the part,
/// so this rule set intentionally reuses the same naming conventions and
/// planning style as CobFeatureRules from model automation.
///
/// This file only builds a plan:
/// - Suppress[]
/// - Unsuppress[]
///
/// The caller/editor can then apply the plan against the drawing document.
/// </summary>
public sealed class CobDrawingFeatureRules : IDrawingFeatureRuleSet
{
    public WedgeType AppliesTo => WedgeType.COB;

    public DrawingFeaturePlan Build(WedgeData wedge, DrawingType drawingType, WedgeSubclass subclass)
    {
        if (wedge is null) throw new ArgumentNullException(nameof(wedge));

        Logger.Info($"[CobDrawingFeatureRules] Build → subclass={subclass}, drawingType={drawingType}");

        var shank = ResolveShankType(wedge);

        // ------------------------------------------------------------
        // PGB rules
        // ------------------------------------------------------------
        if (subclass == WedgeSubclass.PGB)
        {
            var suppress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unsuppress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Logger.Info($"[CobDrawingFeatureRules] Subclass=PGB → applying PGB shank-only rules. Shank={shank}");

            BuildPgbPlan(shank, suppress, unsuppress);

            foreach (var nm in BuildNameCandidatesWithSketches("FRO", CobShankType.Std))
                suppress.Add(nm);

            foreach (var nm in BuildNameCandidatesWithSketches("FRO", CobShankType.Rev180))
                suppress.Add(nm);

            if (drawingType == DrawingType.Overlay)
            {
                Logger.Info("[CobDrawingFeatureRules] Subclass=PGB + Overlay → applying overlay template feature toggles.");
                BuildOverlayPlan(wedge, shank, suppress, unsuppress, "PGB_LEFT_overlay_sketch");
            }

            EnforceCutFeaturesByDrawingType(drawingType, suppress, unsuppress);

            suppress.RemoveWhere(nm => unsuppress.Contains(nm));

            return new DrawingFeaturePlan(
                suppress.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                unsuppress.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
        }

        // ------------------------------------------------------------
        // FG rules
        // ------------------------------------------------------------
        var fgSuppress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fgUnsuppress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var foot = ResolveFootOption(wedge);

        // same baseline as part rules
        fgUnsuppress.Add("TL_feature");

        if (drawingType is DrawingType.Production or DrawingType.Customer)
        {
            var engraving = TryGetEngravingName();
            if (!string.IsNullOrWhiteSpace(engraving))
                fgUnsuppress.Add(engraving);
        }

        if (drawingType == DrawingType.Overlay)
        {
            Logger.Info("[CobDrawingFeatureRules] Subclass=FG + Overlay → applying FG overlay template feature toggles.");

            // prevent leakage from PGB overlay sketch
            fgSuppress.Add("PGB_LEFT_overlay_sketch");
            fgUnsuppress.Remove("PGB_LEFT_overlay_sketch");

            // ensure FG overlay sketch is the active one
            fgUnsuppress.Add("FG_LEFT_overlay_sketch");
            fgSuppress.Remove("FG_LEFT_overlay_sketch");

            BuildOverlayPlan(wedge, shank, fgSuppress, fgUnsuppress, "FG_LEFT_overlay_sketch");
        }

        Logger.Info($"[CobDrawingFeatureRules] Parsed → Subclass=FG, Shank={shank}, Foot={foot}");

        BuildFeaturePlanAlignedWithSpec(wedge, shank, foot, fgSuppress, fgUnsuppress);

        EnforceCutFeaturesByDrawingType(drawingType, fgSuppress, fgUnsuppress);

        fgSuppress.RemoveWhere(nm => fgUnsuppress.Contains(nm));

        return new DrawingFeaturePlan(
            fgSuppress.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            fgUnsuppress.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    // -------------------------------------------------------------------------
    // Shared enforcement
    // -------------------------------------------------------------------------
    private static void EnforceCutFeaturesByDrawingType(
        DrawingType drawingType,
        HashSet<string> suppress,
        HashSet<string> unsuppress)
    {
        if (drawingType == DrawingType.Overlay)
            return;

        suppress.Add("cut_feature");
        suppress.Add("cut_plan_feature");
        suppress.Add("non_std_cut_plan_feature");
        suppress.Add("non_std_cut_feature");

        unsuppress.Remove("cut_feature");
        unsuppress.Remove("cut_plan_feature");
        unsuppress.Remove("non_std_cut_plan_feature");
        unsuppress.Remove("non_std_cut_feature");
    }

    // -------------------------------------------------------------------------
    // PGB planning
    // -------------------------------------------------------------------------
    private static void BuildPgbPlan(
        CobShankType shank,
        HashSet<string> suppress,
        HashSet<string> unsuppress)
    {
        unsuppress.Add("TL_feature");
        unsuppress.Add("part_axis");

        var opposite = shank == CobShankType.Std ? CobShankType.Rev180 : CobShankType.Std;

        foreach (var nm in BuildNameCandidatesWithSketches("TDF", shank))
            unsuppress.Add(nm);

        foreach (var nm in BuildNameCandidatesWithSketches("ISA_20", shank))
            unsuppress.Add(nm);

        foreach (var nm in BuildNameCandidatesWithSketches("10BA", shank))
            unsuppress.Add(nm);

        unsuppress.Add(BuildAnnotationName("10BA", shank));

        foreach (var nm in BuildNameCandidatesWithSketches("TDF", opposite))
            suppress.Add(nm);

        foreach (var nm in BuildNameCandidatesWithSketches("ISA_20", opposite))
            suppress.Add(nm);

        foreach (var nm in BuildNameCandidatesWithSketches("10BA", opposite))
            suppress.Add(nm);

        suppress.Add(BuildAnnotationName("10BA", opposite));
    }

    // -------------------------------------------------------------------------
    // Overlay planning
    // -------------------------------------------------------------------------
    private static void BuildOverlayPlan(
        WedgeData wedge,
        CobShankType shank,
        HashSet<string> suppress,
        HashSet<string> unsuppress,
        string leftOverlaySketch)
    {
        if (!Enum.IsDefined(typeof(CobShankType), shank))
            shank = CobShankType.Std;

        unsuppress.Add("ref_point_sketch");
        unsuppress.Add("cut_plan_feature");
        unsuppress.Add("cut_feature");

        bool vrPositive = IsDimPositive(wedge, "VR");
        bool vwPositive = IsDimPositive(wedge, "VW");
        bool ra2Positive = IsDimPositive(wedge, "RA2");
        bool ra2hPositive = IsDimPositive(wedge, "RA2H");
        bool slbEnabled = ResolveOptionalEnabled(wedge, "SLB"); // VBL > 0
        bool isStd = shank == CobShankType.Std;

        if (vrPositive && vwPositive)
        {
            suppress.Add("cut_feature");
            unsuppress.Remove("cut_feature");

            unsuppress.Add("non_std_cut_plan_feature");
            unsuppress.Add("non_std_cut_feature");
            suppress.Remove("non_std_cut_plan_feature");
            suppress.Remove("non_std_cut_feature");
        }
        else
        {
            suppress.Add("non_std_cut_plan_feature");
            suppress.Add("non_std_cut_feature");
            unsuppress.Remove("non_std_cut_plan_feature");
            unsuppress.Remove("non_std_cut_feature");
        }

        if (!string.IsNullOrWhiteSpace(leftOverlaySketch))
        {
            if (vrPositive)
            {
                suppress.Add(leftOverlaySketch);
                unsuppress.Remove(leftOverlaySketch);
            }
            else
            {
                unsuppress.Add(leftOverlaySketch);
            }
        }

        const string StdFront = "PGB_STD_FRONT_overlay_sketch";
        const string RevFront = "PGB_180_DEG_REV_FRONT_overlay_sketch";

        if (isStd)
        {
            unsuppress.Add(StdFront);
            suppress.Add(RevFront);
        }
        else
        {
            unsuppress.Add(RevFront);
            suppress.Add(StdFront);
        }

        if (ra2Positive)
        {
            suppress.Add(StdFront);
            unsuppress.Remove(StdFront);
        }

        const string Ra2hStdFront = "RA2H_STD_FRONT_overlay_sketch";
        const string Ra2hRevFront = "RA2H_180_DEG_REV_FRONT_overlay_sketch";

        if (ra2hPositive)
        {
            if (isStd)
            {
                unsuppress.Add(Ra2hStdFront);
                suppress.Add(Ra2hRevFront);
            }
            else
            {
                unsuppress.Add(Ra2hRevFront);
                suppress.Add(Ra2hStdFront);
            }
        }
        else
        {
            suppress.Add(Ra2hStdFront);
            suppress.Add(Ra2hRevFront);
        }

        const string VwLeftCase1 = "VW_LEFT_case_1_overlay_sketch";
        const string VwLeftCase2 = "VW_LEFT_case_2_overlay_sketch";

        if (vrPositive)
        {
            bool vwEqualsW = IsDimEqualTo(wedge, "VW", wedge, "W");

            if (vwEqualsW)
            {
                unsuppress.Add(VwLeftCase2);
                suppress.Add(VwLeftCase1);
            }
            else
            {
                unsuppress.Add(VwLeftCase1);
                suppress.Add(VwLeftCase2);
            }
        }
        else
        {
            suppress.Add(VwLeftCase1);
            suppress.Add(VwLeftCase2);
        }

        const string SlbStdOverlay = "SLB_STD_overlay_sketch";
        const string SlbRevOverlay = "SLB_180_DEG_REV_overlay_sketch";

        if (slbEnabled)
        {
            if (isStd)
            {
                unsuppress.Add(SlbStdOverlay);
                suppress.Add(SlbRevOverlay);

                suppress.Add(StdFront);
                unsuppress.Remove(StdFront);

                foreach (var nm in BuildNameCandidatesWithSketches("10BA", CobShankType.Std))
                {
                    suppress.Add(nm);
                    unsuppress.Remove(nm);
                }
            }
            else
            {
                unsuppress.Add(SlbRevOverlay);
                suppress.Add(SlbStdOverlay);

                suppress.Add(RevFront);
                unsuppress.Remove(RevFront);

                foreach (var nm in BuildNameCandidatesWithSketches("10BA", CobShankType.Rev180))
                {
                    suppress.Add(nm);
                    unsuppress.Remove(nm);
                }
            }
        }
        else
        {
            suppress.Add(SlbStdOverlay);
            suppress.Add(SlbRevOverlay);
        }
    }

    // -------------------------------------------------------------------------
    // FG/spec-aligned planning
    // -------------------------------------------------------------------------
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

        var footAndFilletBasesAll = new[]
        {
            "C", "G", "VG", "CG", "CBRA",
            "BR_C", "FR_C",
            "BR_G", "FR_G",
            "BR_VG", "FR_VG"
        };

        bool baIsZero = IsDimZero(wedge, "BA");

        var opposite = shank == CobShankType.Std ? CobShankType.Rev180 : CobShankType.Std;

        foreach (var b in mandatoryBases)
            foreach (var nm in BuildNameCandidatesWithSketches(b, opposite))
                suppress.Add(nm);

        foreach (var b in footAndFilletBasesAll)
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

        foreach (var nm in ExpandForShank(footAndFilletBasesAll, shank))
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

            foreach (var nm in BuildNameCandidatesWithSketches(opt, shank))
            {
                if (enabled) unsuppress.Add(nm);
                else suppress.Add(nm);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Name helpers
    // -------------------------------------------------------------------------
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

    private static string BuildAnnotationName(string baseName, CobShankType shank)
        => $"{baseName}_{BuildSuffix(shank)}_annotation";

    private static IEnumerable<string> BuildNameCandidatesWithSketches(string baseName, CobShankType shank)
    {
        yield return BuildFeatureName(baseName, shank);

        var suffix = BuildSuffix(shank);
        yield return $"{baseName}_{suffix}_sketch";
        yield return $"{baseName}_{suffix}_Sketch";
        yield return $"{baseName}_{suffix}_SKETCH";

        if (baseName.Equals("FRO", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"FRO_{suffix}_feature_1";
            yield return $"FRO_{suffix}_feature_2";
        }

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

        yield return $"H_{suffix}_feature";
        yield return $"H_{suffix}_cut_feature";
        yield return $"H_{suffix}_fix_feature";

        yield return $"H_{suffix}_sketch";
        yield return $"H_{suffix}_Sketch";
        yield return $"H_{suffix}_SKETCH";

        yield return $"H_{suffix}_cut_sketch";
        yield return $"H_{suffix}_cut_Sketch";
        yield return $"H_{suffix}_cut_SKETCH";
    }

    // -------------------------------------------------------------------------
    // Optional / dimensions
    // -------------------------------------------------------------------------
    private static bool ResolveOptionalEnabled(WedgeData wedge, string featureKey)
    {
        if (wedge is null) return false;

        if (featureKey.Equals("SLB", StringComparison.OrdinalIgnoreCase))
            return IsDimEnabled(wedge, "VBL");

        return IsDimEnabled(wedge, featureKey);
    }

    private static bool IsDimEnabled(WedgeData wedge, string dimKey)
    {
        if (wedge?.Dimensions is null) return false;
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

    private static bool IsDimPositive(WedgeData wedge, string dimKey)
    {
        if (wedge?.Dimensions is null) return false;
        if (!wedge.Dimensions.TryGetValue(DimensionKey.From(dimKey), out var dim) || dim is null)
            return false;

        return dim.Nominal.Value > 0m;
    }

    private static bool IsDimEqualTo(WedgeData wedgeA, string dimKeyA, WedgeData wedgeB, string dimKeyB)
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

    // -------------------------------------------------------------------------
    // Wedge parsing
    // -------------------------------------------------------------------------
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
                return CobFootOption.C_WithCbr;
        }

        return baseFoot;
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

    private static string TryGetEngravingName() => "Engraving";

    private enum CobShankType
    {
        Std,
        Rev180
    }

    private enum CobFootOption
    {
        C,
        G,
        VG,
        CC,
        C_WithCbr
    }
}