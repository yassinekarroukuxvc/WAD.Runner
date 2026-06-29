using System;
using System.Collections.Generic;
using System.Linq;

namespace WAD.Runner.ModelAutomation.Rules.CobLike;

/// <summary>
/// Single source of truth for every SolidWorks feature/sketch name that WAD manages
/// for COB-like wedge types (COB, FP).
///
/// Naming convention in the 3D model:
///   {BASE}_{SHANK}_feature      e.g. TDF_STD_feature, TDF_180_DEG_REV_feature
///   {BASE}_{SHANK}_sketch       e.g. TDF_STD_sketch  (only for bases that have a sketch)
///
/// Special cases handled below:
///   H, H_CUT, H_FIX, FUNNEL — non-standard naming, see FeatureNames().
///   FRO                      — STD uses "FRO_STD_feature", REV uses "FRO_180_DEG_REV_feature".
///   ERW (model features)     — suppressed for PGB; activated for FG per shank.
///   Overlay sketches         — managed separately; never part of STD/REV feature groups.
/// </summary>
public static class CobLikeFeatureCatalog
{
    // -------------------------------------------------------------------------
    // Shank suffix helpers
    // -------------------------------------------------------------------------

    public static string Suffix(CobLikeShankType shank)
        => shank == CobLikeShankType.Std ? "STD" : "180_DEG_REV";

    // -------------------------------------------------------------------------
    // Feature name builders
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the feature (and optional sketch) names for a given base name and shank.
    /// Covers all special-case naming in the 3D model.
    /// </summary>
    public static IEnumerable<string> FeatureNames(string baseName, CobLikeShankType shank)
    {
        var sfx = Suffix(shank);

        switch (baseName.ToUpperInvariant())
        {
            // --- H family: feature + sketch ---
            case "H":
                yield return $"H_{sfx}_feature";
                yield return $"H_{sfx}_sketch";
                yield break;

            case "H_CUT":
                yield return $"H_{sfx}_cut_feature";
                yield return $"H_{sfx}_cut_sketch";
                yield break;

            case "H_FIX":
                yield return $"H_{sfx}_fix_feature";
                yield break;

            // --- Funnel: feature only (sketch name in model doesn't follow pattern) ---
            case "FUNNEL":
                yield return $"funnel_final_diametre_{sfx}_feature";
                yield break;

            // --- FRO: follows the standard pattern but feature-only (no sketch) ---
            case "FRO":
                yield return $"FRO_{sfx}_feature";
                yield break;

            // --- Default: {BASE}_{SHANK}_feature [+ {BASE}_{SHANK}_sketch if applicable] ---
            default:
                yield return $"{baseName}_{sfx}_feature";
                if (HasSketch(baseName))
                    yield return $"{baseName}_{sfx}_sketch";
                yield break;
        }
    }

    /// <summary>
    /// Returns feature names for several base names at once (same shank).
    /// </summary>
    public static IEnumerable<string> FeatureNames(CobLikeShankType shank, params string[] baseNames)
        => baseNames.SelectMany(b => FeatureNames(b, shank));

    /// <summary>
    /// True for base names whose model feature also contains a sketch child that WAD manages.
    /// </summary>
    private static bool HasSketch(string baseName)
        => baseName.ToUpperInvariant() is
            "TDF" or "ENGRAVING" or "SLB" or "ISA_20" or "VW" or "W2" or
            "10BA" or "RA2" or "ERW" or "VG" or "G" or "C" or "CG" or "CBRA";

    // -------------------------------------------------------------------------
    // Core feature sets activated for each drawing subclass
    // -------------------------------------------------------------------------

    /// <summary>Always-on features, regardless of subclass or drawing type.</summary>
    public static IEnumerable<string> GlobalCore()
    {
        yield return "TL_feature";
        yield return "TD_sketch";
    }

    /// <summary>Base features shared by both PGB and FG.</summary>
    public static IEnumerable<string> SharedCore(CobLikeShankType shank)
        => FeatureNames(shank, "TDF", "ISA_20", "10BA");

    /// <summary>Additional features activated for FG (on top of SharedCore).</summary>
    public static IEnumerable<string> FgOnly(CobLikeShankType shank)
        => FeatureNames(shank, "ERW", "H", "H_CUT", "H_FIX", "FUNNEL", "COMBINE");

    /// <summary>Full PGB feature set.</summary>
    public static IEnumerable<string> PgbCore(CobLikeShankType shank)
        => SharedCore(shank);

    /// <summary>Full FG feature set.</summary>
    public static IEnumerable<string> FgCore(CobLikeShankType shank)
        => SharedCore(shank).Concat(FgOnly(shank));

    // -------------------------------------------------------------------------
    // ERW suppression (PGB forces both ERW variants off)
    // -------------------------------------------------------------------------

    public static IEnumerable<string> PgbErwSuppressions()
        => FeatureNames(CobLikeShankType.Std, "ERW")
           .Concat(FeatureNames(CobLikeShankType.Rev180, "ERW"));

    // -------------------------------------------------------------------------
    // Foot option feature groups
    // -------------------------------------------------------------------------

    public static IEnumerable<string> FootFeatures(CobLikeFootOption foot, CobLikeShankType shank)
        => foot switch
        {
            CobLikeFootOption.G => FeatureNames(shank, "G", "BR_G", "FR_G"),
            CobLikeFootOption.VG => FeatureNames(shank, "VG", "BR_VG", "FR_VG"),
            CobLikeFootOption.CC => FeatureNames(shank, "C", "CG", "BR_C", "FR_C"),
            CobLikeFootOption.C_WithCbr => FeatureNames(shank, "C", "CBRA", "FR_C"),
            _  /* C (default) */        => FeatureNames(shank, "C", "BR_C", "FR_C"),
        };

    // =========================================================================
    // OVERLAY SKETCHES
    // =========================================================================

    // -------------------------------------------------------------------------
    // Common overlay features (always activated for overlays)
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // PGB overlay feature
    // -------------------------------------------------------------------------

    public static string PgbFrontOverlayFeature(CobLikeShankType shank)
        => shank == CobLikeShankType.Std
            ? "PGB_STD_FRONT_overlay"
            : "PGB_180_DEG_REV_FRONT_overlay";

    // -------------------------------------------------------------------------
    // Front overlay sketches (mutually exclusive — exactly one shown at a time)
    // -------------------------------------------------------------------------

    /// <summary>Standard front sketch (no RA2H, no SLB).</summary>
    public static string FrontSketch(CobLikeShankType shank)
        => shank == CobLikeShankType.Std
            ? "PGB_STD_FRONT_overlay_sketch"
            : "PGB_180_DEG_REV_FRONT_overlay_sketch";

    /// <summary>Front sketch when RA2H > 0 but no SLB.</summary>
    public static string Ra2HSketch(CobLikeShankType shank)
        => shank == CobLikeShankType.Std
            ? "RA2H_STD_FRONT_overlay_sketch"
            : "RA2H_180_DEG_REV_FRONT_overlay_sketch";

    /// <summary>Front sketch when SLB active but no RA2H.</summary>
    public static string SlbSketch(CobLikeShankType shank)
        => shank == CobLikeShankType.Std
            ? "SLB_STD_overlay_sketch"
            : "SLB_180_DEG_REV_overlay_sketch";

    /// <summary>Front sketch when both RA2H > 0 and SLB active.</summary>
    public static string Ra2HSlbSketch(CobLikeShankType shank)
        => shank == CobLikeShankType.Std
            ? "RA2H_SLB_STD_FRONT_overlay_sketch"
            : "RA2H_SLB_180_DEG_REV_FRONT_overlay_sketch";

    /// <summary>All four front overlay sketch names for a given shank (used to deactivate the unused ones).</summary>
    public static IEnumerable<string> AllFrontSketches(CobLikeShankType shank)
    {
        yield return FrontSketch(shank);
        yield return Ra2HSketch(shank);
        yield return SlbSketch(shank);
        yield return Ra2HSlbSketch(shank);
    }

    // -------------------------------------------------------------------------
    // Left overlay sketches (VW cases — mutually exclusive)
    // -------------------------------------------------------------------------

    public const string LeftSketchPgb = "PGB_LEFT_overlay_sketch";
    public const string LeftSketchFg = "FG_LEFT_overlay_sketch";
    public const string VwLeftCase1 = "VW_LEFT_case_1_overlay_sketch";
    public const string VwLeftCase2 = "VW_LEFT_case_2_overlay_sketch";
    public const string VwLeftCase3 = "VW_LEFT_case_3_overlay_sketch";
    public const string VwLeftCase4 = "VW_LEFT_case_4_overlay_sketch";

    // -------------------------------------------------------------------------
    // ERW overlay sketches (FG only — mutually exclusive per shank)
    // -------------------------------------------------------------------------

    public static string ErwOverlaySketch(CobLikeShankType shank)
        => shank == CobLikeShankType.Std
            ? "ERW_STD_overlay_sketch"
            : "ERW_180_DEG_REV_overlay_sketch";

    public static IEnumerable<string> AllErwOverlaySketches()
    {
        yield return "ERW_STD_overlay_sketch";
        yield return "ERW_180_DEG_REV_overlay_sketch";
    }

    // -------------------------------------------------------------------------
    // Foot-width overlay sketches (FG only — exactly one shown at a time)
    //
    // Nine sketches: {C|VG|G}_FOOT_{W|VW|W2}_overlay_sketch
    // Selection: foot option → prefix; smallest positive value of W/VW/W2 → suffix.
    // -------------------------------------------------------------------------

    public static string FootWidthSketch(CobLikeFootOption foot, decimal w, decimal vw, decimal w2)
    {
        var prefix = foot switch
        {
            CobLikeFootOption.G => "G",
            CobLikeFootOption.VG => "VG",
            _ => "C"   // C, C_WithCbr, CC
        };

        // Start with W as baseline; only consider VW/W2 when they are positive.
        var suffix = "W";
        var min = w;

        if (vw > 0m && vw < min) { min = vw; suffix = "VW"; }
        if (w2 > 0m && w2 < min) { suffix = "W2"; }

        return $"{prefix}_FOOT_{suffix}_overlay_sketch";
    }

    public static IEnumerable<string> AllFootWidthSketches()
    {
        foreach (var foot in new[] { "C", "VG", "G" })
            foreach (var dim in new[] { "W", "VW", "W2" })
                yield return $"{foot}_FOOT_{dim}_overlay_sketch";
    }

    // =========================================================================
    // AllManagedNames — every name WAD will explicitly suppress or activate.
    // The plan builder suppresses anything known but not activated.
    // =========================================================================

    public static IEnumerable<string> AllManagedNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(IEnumerable<string> src) { foreach (var n in src) names.Add(n); }

        // Global
        Add(GlobalCore());

        // All shank-specific feature groups (both shanks)
        foreach (var shank in new[] { CobLikeShankType.Std, CobLikeShankType.Rev180 })
        {
            Add(FeatureNames(shank,
                "TDF", "ENGRAVING", "SLB", "ISA_20", "VW", "W2", "10BA", "RA2", "ERW",
                "H", "H_CUT", "H_FIX", "FUNNEL", "ROUND_BR", "COMBINE", "FRO",
                "VG", "BR_VG", "FR_VG",
                "G", "BR_G", "FR_G",
                "C", "BR_C", "FR_C", "CG", "CBRA"));
        }

        // Overlay: common + cut profiles
        Add(OverlayCommon());
        Add(OverlayCutStandard());
        Add(OverlayCutNonStandard());

        // Overlay: PGB front features
        names.Add(PgbFrontOverlayFeature(CobLikeShankType.Std));
        names.Add(PgbFrontOverlayFeature(CobLikeShankType.Rev180));

        // Overlay: all front sketches (both shanks)
        foreach (var shank in new[] { CobLikeShankType.Std, CobLikeShankType.Rev180 })
            Add(AllFrontSketches(shank));

        // Overlay: left sketches
        names.Add(LeftSketchPgb);
        names.Add(LeftSketchFg);
        names.Add(VwLeftCase1);
        names.Add(VwLeftCase2);
        names.Add(VwLeftCase3);
        names.Add(VwLeftCase4);

        // Overlay: ERW sketches
        Add(AllErwOverlaySketches());

        // Overlay: foot-width sketches (9 combinations)
        Add(AllFootWidthSketches());

        return names;
    }
}