using System;
using System.Collections.Generic;
using System.Linq;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Execution;

namespace WAD.Runner.ModelAutomation.Rules.CobLike;

/// <summary>
/// Shared feature-toggle planning for COB-like wedges (COB / FP / UTUS).
///
/// Strategy:
/// 1. Build the exact active set for the current case
/// 2. Suppress every other known model item
/// 3. Let concrete rule sets add small wedge-specific adjustments
///
/// The names in this file are aligned to the corrected feature/sketch list
/// provided for the current 3D model.
/// </summary>
public abstract class CobLikeFeatureRulesBase : IFeatureRuleSet
{
    protected abstract string LogPrefix { get; }

    /// <summary>
    /// Hook for wedge-specific behavior.
    ///
    /// active:
    ///   Add any names that must stay unsuppressed for the current wedge type.
    ///
    /// forceSuppress:
    ///   Add any names that must always stay suppressed for the current wedge type,
    ///   even if the base logic would otherwise activate them.
    /// </summary>
    protected virtual void ApplyVariantAdjustments(
        CobLikeRuleFacts facts,
        CobLikeShankType shank,
        FeatureRuleContext context,
        HashSet<string> active,
        HashSet<string> forceSuppress)
    {
    }

    public ModelRuleRunner.FeaturePlan Build(WedgeData wedge, FeatureRuleContext context)
    {
        if (wedge is null) throw new ArgumentNullException(nameof(wedge));
        if (context is null) throw new ArgumentNullException(nameof(context));

        var facts = new CobLikeRuleFacts(wedge);
        var shank = facts.ShankType;

        Logger.Info($"[{LogPrefix}] Build -> start. Subclass={context.Subclass}, DrawingType={context.DrawingType}, Shank={shank}");

        var active = NewNameSet();
        var forceSuppress = NewNameSet();

        AddTlSet(active);

        if (context.Subclass == WedgeSubclass.PGB)
            BuildPgbPlan(shank, context, active);
        else
            BuildFgPlan(facts, shank, context, active);

        ApplyVariantAdjustments(facts, shank, context, active, forceSuppress);
        ApplyOverlayConfigurationOverride(context, active);

        var suppress = GetAllKnownNames();
        suppress.ExceptWith(active);
        suppress.UnionWith(forceSuppress);
        suppress.ExceptWith(active); // unsuppress always wins

        Logger.Success($"[{LogPrefix}] Build -> done. active={active.Count}, suppress={suppress.Count}");

        return new ModelRuleRunner.FeaturePlan(
            Suppress: suppress.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            Unsuppress: active.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private void BuildPgbPlan(
    CobLikeShankType shank,
    FeatureRuleContext context,
    HashSet<string> active)
    {
        Logger.Info($"[{LogPrefix}] Applying PGB rules.");

        AddPgbCoreShankSet(shank, active);

        if (context.DrawingType == DrawingType.Overlay)
        {
            active.Add("ref_point_sketch");
            active.Add("cut_plan_feature");
            AddPgbOverlaySet(shank, active);
        }
    }

    private void BuildFgPlan(
        CobLikeRuleFacts facts,
        CobLikeShankType shank,
        FeatureRuleContext context,
        HashSet<string> active)
    {
        var foot = ResolveFootOption(facts);

        Logger.Info($"[{LogPrefix}] Applying FG rules. Foot={foot}");

        AddFgCoreShankSet(shank, active);

        if (facts.IsDimPositive("FRO"))
            AddFeatureGroup(active, "FRO", shank);

        if (facts.IsDimPositive("VBL"))
            AddFeatureGroup(active, "SLB", shank);

        if (facts.IsDimPositive("VW"))
            AddFeatureGroup(active, "VW", shank);

        if (facts.IsDimPositive("W2"))
            AddFeatureGroup(active, "W2", shank);

        if (facts.IsDimPositive("RA2"))
            AddFeatureGroup(active, "RA2", shank);

        AddFootOptionSet(active, foot, shank);

        if (context.DrawingType == DrawingType.Overlay)
            AddFgOverlaySet(facts, shank, active);
    }

    protected static HashSet<string> NewNameSet()
        => new(StringComparer.OrdinalIgnoreCase);

    private static void AddTlSet(HashSet<string> target)
    {
        target.Add("TL_feature");
        target.Add("TD_sketch");
    }

    private static void AddPgbCoreShankSet(CobLikeShankType shank, HashSet<string> target)
    {
        AddFeatureGroup(target, "TDF", shank);
        AddFeatureGroup(target, "ISA_20", shank);
        AddFeatureGroup(target, "10BA", shank);
    }

    private static void AddFgCoreShankSet(CobLikeShankType shank, HashSet<string> target)
    {
        AddPgbCoreShankSet(shank, target);
        AddFeatureGroup(target, "ERW", shank);
        AddFeatureGroup(target, "H", shank);
        AddFeatureGroup(target, "H_CUT", shank);
        AddFeatureGroup(target, "H_FIX", shank);
        AddFeatureGroup(target, "FUNNEL_FINAL_DIAMETRE", shank);
        AddFeatureGroup(target, "COMBINE", shank);
    }

    private static void AddPgbOverlaySet(CobLikeShankType shank, HashSet<string> target)
    {
        target.Add("cut_feature");
        target.Add("PGB_LEFT_overlay_sketch");

        if (shank == CobLikeShankType.Std)
        {
            target.Add("PGB_STD_FRONT_overlay");
            target.Add("PGB_STD_FRONT_overlay_sketch");
        }
        else
        {
            target.Add("PGB_180_DEG_REV_FRONT_overlay");
            target.Add("PGB_180_DEG_REV_FRONT_overlay_sketch");
        }
    }

    private static void AddFgOverlaySet(CobLikeRuleFacts facts, CobLikeShankType shank, HashSet<string> target)
    {
        target.Add("ref_point_sketch");
        target.Add("ref_point_non_std_cut_sketch");
        target.Add("ref_point_180_DEG_REV_sketch");

        if (facts.HasVr)
        {
            target.Add("non_std_cut_plan_feature");
            target.Add("non_std_cut_feature");
        }
        else
        {
            target.Add("cut_feature");
        }

        if (!facts.HasVw)
        {
            target.Add("FG_LEFT_overlay_sketch");
        }
        else if (facts.HasLargeOverlayVrCase)
        {
            if (facts.AreNominalsEqual("VW", "W"))
                target.Add("VW_LEFT_case_4_overlay_sketch");
            else
                target.Add("VW_LEFT_case_3_overlay_sketch");
        }
        else
        {
            if (facts.AreNominalsEqual("VW", "W"))
                target.Add("VW_LEFT_case_2_overlay_sketch");
            else
                target.Add("VW_LEFT_case_1_overlay_sketch");
        }

        if (facts.HasRa2H)
            target.Add(GetRa2HOverlaySketchName(shank));
        else
            target.Add(GetOverlayFrontSketchName(shank));

        if (facts.IsDimPositive("VBL"))
            target.Add(GetSlbOverlaySketchName(shank));
    }

    protected static void AddFootOptionSet(HashSet<string> target, CobLikeFootOption foot, CobLikeShankType shank)
    {
        switch (foot)
        {
            case CobLikeFootOption.C:
                AddFeatureGroup(target, "C", shank);
                AddFeatureGroup(target, "BR_C", shank);
                AddFeatureGroup(target, "FR_C", shank);
                break;

            case CobLikeFootOption.G:
                AddFeatureGroup(target, "G", shank);
                AddFeatureGroup(target, "BR_G", shank);
                AddFeatureGroup(target, "FR_G", shank);
                break;

            case CobLikeFootOption.VG:
                AddFeatureGroup(target, "VG", shank);
                AddFeatureGroup(target, "BR_VG", shank);
                AddFeatureGroup(target, "FR_VG", shank);
                break;

            case CobLikeFootOption.CC:
                AddFeatureGroup(target, "C", shank);
                AddFeatureGroup(target, "CG", shank);
                AddFeatureGroup(target, "BR_C", shank);
                AddFeatureGroup(target, "FR_C", shank);
                break;

            case CobLikeFootOption.C_WithCbr:
                AddFeatureGroup(target, "C", shank);
                AddFeatureGroup(target, "CBRA", shank);
                AddFeatureGroup(target, "FR_C", shank);
                break;
        }
    }

    protected static void AddFeatureGroup(HashSet<string> target, string baseName, CobLikeShankType shank)
    {
        foreach (var name in BuildNameCandidates(baseName, shank))
            target.Add(name);
    }

    protected static IEnumerable<string> BuildNameCandidates(string baseName, CobLikeShankType shank)
    {
        if (baseName.Equals("FRO", StringComparison.OrdinalIgnoreCase))
        {
            yield return shank == CobLikeShankType.Std
                ? "FRO_STD_feature_1"
                : "FRO_180_DEG_REV_feature_1";
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
            yield return shank == CobLikeShankType.Std
                ? "funnel_final_diametre_STD_feature"
                : "funnel_final_diametre_180_DEG_REV_feature";
            yield break;
        }

        var suffix = shank == CobLikeShankType.Std ? "STD" : "180_DEG_REV";

        yield return $"{baseName}_{suffix}_feature";

        if (HasSketch(baseName))
            yield return $"{baseName}_{suffix}_sketch";
    }

    private static bool HasSketch(string baseName)
        => baseName is
            "TDF" or
            "SLB" or
            "ISA_20" or
            "VW" or
            "W2" or
            "10BA" or
            "RA2" or
            "ERW" or
            "VG" or
            "G" or
            "C" or
            "CG" or
            "CBRA";

    protected static string GetOverlayFrontSketchName(CobLikeShankType shank)
        => shank == CobLikeShankType.Std
            ? "PGB_STD_FRONT_overlay_sketch"
            : "PGB_180_DEG_REV_FRONT_overlay_sketch";

    protected static string GetRa2HOverlaySketchName(CobLikeShankType shank)
        => shank == CobLikeShankType.Std
            ? "RA2H_STD_FRONT_overlay_sketch"
            : "RA2H_180_DEG_REV_FRONT_overlay_sketch";

    protected static string GetSlbOverlaySketchName(CobLikeShankType shank)
        => shank == CobLikeShankType.Std
            ? "SLB_STD_overlay_sketch"
            : "SLB_180_DEG_REV_overlay_sketch";

    protected CobLikeFootOption ResolveFootOption(CobLikeRuleFacts facts)
    {
        var raw =
            CobLikeRuleFacts.GetPropLoose(facts.Wedge, "Wed-Foot_Option") ??
            CobLikeRuleFacts.GetPropLoose(facts.Wedge, "Wed-FootOption") ??
            CobLikeRuleFacts.GetPropLoose(facts.Wedge, "Foot_Option") ??
            CobLikeRuleFacts.GetPropLoose(facts.Wedge, "FootOption") ??
            string.Empty;

        raw = CobLikeRuleFacts.NormalizeDbToken(raw);

        CobLikeFootOption foot;

        if (EqualsAny(raw, "SW_G", "G")) foot = CobLikeFootOption.G;
        else if (EqualsAny(raw, "SW_VG", "VG")) foot = CobLikeFootOption.VG;
        else if (EqualsAny(raw, "SW_CG", "CG", "CC")) foot = CobLikeFootOption.CC;
        else foot = CobLikeFootOption.C;

        if (foot == CobLikeFootOption.C)
        {
            bool hasCbr =
                facts.IsDimPositive("CBRA") &&
                facts.IsDimPositive("CBRD") &&
                facts.IsDimPositive("CBRL");

            if (hasCbr)
                foot = CobLikeFootOption.C_WithCbr;
        }

        return foot;
    }

    protected static bool EqualsAny(string value, params string[] options)
        => options.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));

    private void ApplyOverlayConfigurationOverride(
        FeatureRuleContext context,
        HashSet<string> active)
    {
        if (context.DrawingType != DrawingType.Overlay)
            return;

        var profile = ResolveOverlayCutProfile(context);
        if (string.IsNullOrWhiteSpace(profile))
            return;

        switch (profile)
        {
            case "default_config":
            case "std_cut":
                ForceStandardCutState(active);
                Logger.Info($"[{LogPrefix}] Overlay config/profile '{profile}' -> standard cut state.");
                break;

            case "non_std_cut":
                ForceNonStandardCutState(active);
                Logger.Info($"[{LogPrefix}] Overlay config/profile 'non_std_cut' -> non-standard cut state.");
                break;
        }
    }

    private static string ResolveOverlayCutProfile(FeatureRuleContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.FeatureRuleProfile))
            return context.FeatureRuleProfile.Trim();

        if (string.Equals(context.TargetConfigurationName, "non_std_cut", StringComparison.OrdinalIgnoreCase))
            return "non_std_cut";

        if (string.Equals(context.TargetConfigurationName, "std_cut", StringComparison.OrdinalIgnoreCase))
            return "std_cut";

        if (string.Equals(context.TargetConfigurationName, "Default", StringComparison.OrdinalIgnoreCase))
            return "default_config";

        return null;
    }

    private static void ForceStandardCutState(HashSet<string> active)
    {
        active.Add("cut_feature");
        active.Remove("non_std_cut_plan_feature");
        active.Remove("non_std_cut_feature");
    }

    private static void ForceNonStandardCutState(HashSet<string> active)
    {
        active.Remove("cut_feature");
        active.Add("non_std_cut_plan_feature");
        active.Add("non_std_cut_feature");
    }

    protected static HashSet<string> GetAllKnownNames()
    {
        var all = NewNameSet();

        all.Add("TL_feature");
        all.Add("TD_sketch");

        foreach (var shank in new[] { CobLikeShankType.Std, CobLikeShankType.Rev180 })
        {
            foreach (var baseName in new[]
            {
                "TDF",
                "ENGRAVING",
                "SLB",
                "ISA_20",
                "VW",
                "W2",
                "10BA",
                "RA2",
                "ERW",
                "H",
                "H_CUT",
                "H_FIX",
                "FUNNEL_FINAL_DIAMETRE",
                "ROUND_BR",
                "COMBINE",
                "FRO",
                "VG",
                "BR_VG",
                "FR_VG",
                "G",
                "FR_G",
                "BR_G",
                "C",
                "FR_C",
                "BR_C",
                "CG",
                "CBRA"
            })
            {
                AddFeatureGroup(all, baseName, shank);
            }
        }

        all.Add("ref_point_sketch");
        all.Add("ref_point_non_std_cut_sketch");
        all.Add("ref_point_180_DEG_REV_sketch");
        all.Add("cut_feature");
        all.Add("non_std_cut_plan_feature");
        all.Add("non_std_cut_feature");
        all.Add("PGB_LEFT_overlay_sketch");
        all.Add("PGB_STD_FRONT_overlay_sketch");
        all.Add("PGB_180_DEG_REV_FRONT_overlay_sketch");
        all.Add("FG_LEFT_overlay_sketch");
        all.Add("RA2H_STD_FRONT_overlay_sketch");
        all.Add("RA2H_180_DEG_REV_FRONT_overlay_sketch");
        all.Add("VW_LEFT_case_1_overlay_sketch");
        all.Add("VW_LEFT_case_2_overlay_sketch");
        all.Add("VW_LEFT_case_3_overlay_sketch");
        all.Add("VW_LEFT_case_4_overlay_sketch");
        all.Add("SLB_STD_overlay_sketch");
        all.Add("SLB_180_DEG_REV_overlay_sketch");
        all.Add("cut_plan_feature");
        all.Add("PGB_STD_FRONT_overlay");
        all.Add("PGB_180_DEG_REV_FRONT_overlay");

        return all;
    }
}