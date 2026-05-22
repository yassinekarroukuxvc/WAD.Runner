using System;
using System.Collections.Generic;
using System.Linq;

namespace WAD.Runner.ModelAutomation.Rules.CobLike;

/// <summary>
/// Central name catalog for COB-like 3D template items.
/// Feature planners deal in logical groups; physical SolidWorks names live here.
/// </summary>
public static class CobLikeFeatureCatalog
{
    public static IEnumerable<string> GlobalCore()
    {
        yield return "TL_feature";
        yield return "TD_sketch";
    }

    public static IEnumerable<string> PgbCore(CobLikeShankType shank)
        => Groups(shank, "TDF", "ISA_20", "10BA");

    public static IEnumerable<string> FgCore(CobLikeShankType shank)
        => PgbCore(shank)
            .Concat(Groups(shank, "ERW", "H", "H_CUT", "H_FIX", "FUNNEL_FINAL_DIAMETRE", "COMBINE"));

    public static IEnumerable<string> Group(CobLikeShankType shank, string baseName)
        => BuildNameCandidates(baseName, shank);

    public static IEnumerable<string> Groups(CobLikeShankType shank, params string[] baseNames)
        => baseNames.SelectMany(x => Group(shank, x));

    public static IEnumerable<string> PgbForcedSuppressions()
        => Groups(CobLikeShankType.Std, "ERW").Concat(Groups(CobLikeShankType.Rev180, "ERW"));

    public static IEnumerable<string> OverlayCommon()
    {
        yield return "ref_point_sketch";
        yield return "ref_point_non_std_cut_sketch";
        yield return "ref_point_180_DEG_REV_sketch";
    }

    public static IEnumerable<string> OverlayCutStandard()
    {
        yield return "cut_plan_feature";
        yield return "cut_feature";
    }

    public static IEnumerable<string> OverlayCutNonStandard()
    {
        yield return "non_std_cut_plan_feature";
        yield return "non_std_cut_feature";
    }

    public static string FrontOverlaySketch(CobLikeShankType shank)
        => shank == CobLikeShankType.Std ? "PGB_STD_FRONT_overlay_sketch" : "PGB_180_DEG_REV_FRONT_overlay_sketch";

    public static string Ra2HOverlaySketch(CobLikeShankType shank)
        => shank == CobLikeShankType.Std ? "RA2H_STD_FRONT_overlay_sketch" : "RA2H_180_DEG_REV_FRONT_overlay_sketch";

    public static string SlbOverlaySketch(CobLikeShankType shank)
        => shank == CobLikeShankType.Std ? "SLB_STD_overlay_sketch" : "SLB_180_DEG_REV_overlay_sketch";

    public static string FrontOverlayFeature(CobLikeShankType shank)
        => shank == CobLikeShankType.Std ? "PGB_STD_FRONT_overlay" : "PGB_180_DEG_REV_FRONT_overlay";

    public static IEnumerable<string> AllManagedNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in GlobalCore()) names.Add(n);

        foreach (var shank in new[] { CobLikeShankType.Std, CobLikeShankType.Rev180 })
        {
            foreach (var baseName in new[]
            {
                "TDF","ENGRAVING","SLB","ISA_20","VW","W2","10BA","RA2","ERW",
                "H","H_CUT","H_FIX","FUNNEL_FINAL_DIAMETRE","ROUND_BR","COMBINE","FRO",
                "VG","BR_VG","FR_VG","G","FR_G","BR_G","C","FR_C","BR_C","CG","CBRA"
            })
                foreach (var n in Group(shank, baseName)) names.Add(n);
        }

        foreach (var n in OverlayCommon()) names.Add(n);
        foreach (var n in OverlayCutStandard()) names.Add(n);
        foreach (var n in OverlayCutNonStandard()) names.Add(n);

        foreach (var n in new[]
        {
            "PGB_LEFT_overlay_sketch", "FG_LEFT_overlay_sketch",
            "PGB_STD_FRONT_overlay_sketch", "PGB_180_DEG_REV_FRONT_overlay_sketch",
            "RA2H_STD_FRONT_overlay_sketch", "RA2H_180_DEG_REV_FRONT_overlay_sketch",
            "VW_LEFT_case_1_overlay_sketch", "VW_LEFT_case_2_overlay_sketch",
            "VW_LEFT_case_3_overlay_sketch", "VW_LEFT_case_4_overlay_sketch",
            "SLB_STD_overlay_sketch", "SLB_180_DEG_REV_overlay_sketch",
            "PGB_STD_FRONT_overlay", "PGB_180_DEG_REV_FRONT_overlay"
        }) names.Add(n);

        return names;
    }

    private static IEnumerable<string> BuildNameCandidates(string baseName, CobLikeShankType shank)
    {
        if (baseName.Equals("FRO", StringComparison.OrdinalIgnoreCase))
        {
            yield return shank == CobLikeShankType.Std ? "FRO_STD_feature_1" : "FRO_180_DEG_REV_feature_1";
            yield break;
        }

        if (baseName.Equals("H", StringComparison.OrdinalIgnoreCase))
        {
            yield return shank == CobLikeShankType.Std ? "H_STD_feature" : "H_180_DEG_REV_feature";
            yield return shank == CobLikeShankType.Std ? "H_STD_sketch" : "H_180_DEG_REV_sketch";
            yield break;
        }

        if (baseName.Equals("H_CUT", StringComparison.OrdinalIgnoreCase))
        {
            yield return shank == CobLikeShankType.Std ? "H_STD_cut_feature" : "H_180_DEG_REV_cut_feature";
            yield return shank == CobLikeShankType.Std ? "H_STD_cut_sketch" : "H_180_DEG_REV_cut_sketch";
            yield break;
        }

        if (baseName.Equals("H_FIX", StringComparison.OrdinalIgnoreCase))
        {
            yield return shank == CobLikeShankType.Std ? "H_STD_fix_feature" : "H_180_DEG_REV_fix_feature";
            yield break;
        }

        if (baseName.Equals("FUNNEL_FINAL_DIAMETRE", StringComparison.OrdinalIgnoreCase))
        {
            yield return shank == CobLikeShankType.Std ? "funnel_final_diametre_STD_feature" : "funnel_final_diametre_180_DEG_REV_feature";
            yield break;
        }

        var suffix = shank == CobLikeShankType.Std ? "STD" : "180_DEG_REV";
        yield return $"{baseName}_{suffix}_feature";
        if (HasSketch(baseName)) yield return $"{baseName}_{suffix}_sketch";
    }

    private static bool HasSketch(string baseName)
        => baseName is "TDF" or "SLB" or "ISA_20" or "VW" or "W2" or "10BA" or "RA2" or "ERW" or "VG" or "G" or "C" or "CG" or "CBRA";
}
