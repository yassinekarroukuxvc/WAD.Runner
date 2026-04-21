using System;
using System.Collections.Generic;
using System.Linq;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Common;
using WAD.Runner.ModelAutomation.Execution;

namespace WAD.Runner.ModelAutomation.Rules.CobLike;

/// <summary>
/// Shared feature-toggle planning for COB-like wedges (COB / FP / UTUS).
///
/// The common behavior lives here:
/// - shank selection
/// - foot-option planning
/// - FG vs PGB split
/// - overlay sketch planning
/// - per-configuration cut-state override
///
/// Concrete classes stay small and only define the real differences.
/// </summary>
public abstract class CobLikeFeatureRulesBase : IFeatureRuleSet
{
    protected abstract string LogPrefix { get; }
    protected abstract string Pgb180RevConfigurationHint { get; }

    /// <summary>
    /// COB uses VR+VW driven non-standard cut planning during overlay.
    /// FP and UTUS keep the simpler overlay plan.
    /// </summary>
    protected virtual bool SupportsOverlayNonStandardCutPlanning => false;

    /// <summary>
    /// COB also suppresses non-standard cut features when not in overlay.
    /// FP and UTUS suppress only the standard cut features outside overlay.
    /// </summary>
    protected virtual bool SuppressNonStandardCutFeaturesOutsideOverlay => false;

    public ModelRuleRunner.FeaturePlan Build(WedgeData wedge, FeatureRuleContext context)
    {
        if (wedge is null) throw new ArgumentNullException(nameof(wedge));
        if (context is null) throw new ArgumentNullException(nameof(context));

        Logger.Info($"[{LogPrefix}] Build → start");

        var facts = new CobLikeRuleFacts(wedge);
        var shank = facts.ShankType;

        if (context.Subclass == WedgeSubclass.PGB)
            return BuildPgbPlan(facts, shank, context);

        return BuildFgPlan(facts, shank, context);
    }

    private ModelRuleRunner.FeaturePlan BuildPgbPlan(
        CobLikeRuleFacts facts,
        CobLikeShankType shank,
        FeatureRuleContext context)
    {
        Logger.Info($"[{LogPrefix}] Subclass=PGB → applying PGB shank-only rules. Shank={shank}");

        var suppress = NewNameSet();
        var unsuppress = NewNameSet();

        BuildPgbMandatoryPlan(shank, suppress, unsuppress);
        ApplyVariantAdjustments(shank, suppress, unsuppress);

        foreach (var nm in BuildNameCandidatesWithSketches("FRO", CobLikeShankType.Std))
            suppress.Add(nm);

        foreach (var nm in BuildNameCandidatesWithSketches("FRO", CobLikeShankType.Rev180))
            suppress.Add(nm);

        if (context.DrawingType == DrawingType.Overlay)
        {
            Logger.Info($"[{LogPrefix}] Subclass=PGB + Overlay → applying overlay template feature toggles.");
            BuildOverlayPlan(facts, shank, suppress, unsuppress, leftOverlaySketch: "PGB_LEFT_overlay_sketch");
            Logger.Info($"[{LogPrefix}] PGB Overlay rule → suppress FRO (STD + 180_DEG_REV).");
        }

        EnforceCutFeaturesByDrawingType(context.DrawingType, suppress, unsuppress);
        ApplyOverlayConfigurationOverride(context, suppress, unsuppress);

        return FinalizePlan(suppress, unsuppress, scope: "PGB");
    }

    private ModelRuleRunner.FeaturePlan BuildFgPlan(
        CobLikeRuleFacts facts,
        CobLikeShankType shank,
        FeatureRuleContext context)
    {
        var suppress = NewNameSet();
        var unsuppress = NewNameSet();
        var foot = ResolveFootOption(facts);

        unsuppress.Add("TL_feature");

        if (context.DrawingType is DrawingType.Production or DrawingType.Customer)
        {
            var engraving = TryGetEngravingName();
            if (!string.IsNullOrWhiteSpace(engraving))
                unsuppress.Add(engraving);
        }

        if (context.DrawingType == DrawingType.Overlay)
        {
            Logger.Info($"[{LogPrefix}] Subclass=FG + Overlay → applying FG overlay template feature toggles.");

            suppress.Add("PGB_LEFT_overlay_sketch");
            unsuppress.Remove("PGB_LEFT_overlay_sketch");

            unsuppress.Add("FG_LEFT_overlay_sketch");
            suppress.Remove("FG_LEFT_overlay_sketch");

            BuildOverlayPlan(facts, shank, suppress, unsuppress, leftOverlaySketch: "FG_LEFT_overlay_sketch");
        }

        Logger.Info($"[{LogPrefix}] Parsed → Subclass=FG, Shank={shank}, Foot={foot}");

        BuildFeaturePlanAlignedWithSpec(facts, shank, foot, suppress, unsuppress);
        ApplyVariantAdjustments(shank, suppress, unsuppress);

        EnforceCutFeaturesByDrawingType(context.DrawingType, suppress, unsuppress);
        ApplyOverlayConfigurationOverride(context, suppress, unsuppress);

        return FinalizePlan(suppress, unsuppress, scope: "FG");
    }

    protected virtual void ApplyVariantAdjustments(
        CobLikeShankType shank,
        HashSet<string> suppress,
        HashSet<string> unsuppress)
    {
    }

    private ModelRuleRunner.FeaturePlan FinalizePlan(
        HashSet<string> suppress,
        HashSet<string> unsuppress,
        string scope)
    {
        suppress.RemoveWhere(nm => unsuppress.Contains(nm));

        Logger.Success($"[{LogPrefix}] Build({scope}) → done. unsuppress={unsuppress.Count}, suppress={suppress.Count}");

        return new ModelRuleRunner.FeaturePlan(
            Suppress: suppress.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            Unsuppress: unsuppress.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    protected static HashSet<string> NewNameSet()
        => new(StringComparer.OrdinalIgnoreCase);

    protected void EnforceCutFeaturesByDrawingType(
    DrawingType drawingType,
    HashSet<string> suppress,
    HashSet<string> unsuppress)
    {
        if (drawingType == DrawingType.Overlay)
            return;

        suppress.Add("cut_feature");
        suppress.Add("cut_plan_feature");
        suppress.Add("non_std_cut_feature");
        suppress.Add("non_std_cut_plan_feature");

        unsuppress.Remove("cut_feature");
        unsuppress.Remove("cut_plan_feature");
        unsuppress.Remove("non_std_cut_feature");
        unsuppress.Remove("non_std_cut_plan_feature");

        Logger.Info($"[{LogPrefix}] Non-Overlay ({drawingType}) → force suppress: cut_feature, cut_plan_feature, non_std_cut_feature, non_std_cut_plan_feature");
    }

    private void BuildPgbMandatoryPlan(
        CobLikeShankType shank,
        HashSet<string> suppress,
        HashSet<string> unsuppress)
    {
        unsuppress.Add("TL_feature");
        unsuppress.Add("part_axis");

        var opposite = shank == CobLikeShankType.Std ? CobLikeShankType.Rev180 : CobLikeShankType.Std;

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

        if (shank == CobLikeShankType.Rev180)
            Logger.Info($"[{LogPrefix}] PGB 180_DEG_REV hint: configuration name expected = {Pgb180RevConfigurationHint}");
    }

    private void BuildOverlayPlan(
        CobLikeRuleFacts facts,
        CobLikeShankType shank,
        HashSet<string> suppress,
        HashSet<string> unsuppress,
        string leftOverlaySketch)
    {
        if (!Enum.IsDefined(typeof(CobLikeShankType), shank))
            shank = CobLikeShankType.Std;

        unsuppress.Add("ref_point_sketch");
        unsuppress.Add("cut_plan_feature");
        unsuppress.Add("cut_feature");

        bool vrPositive = facts.HasVr;
        bool vwPositive = facts.HasVw;
        bool ra2Positive = facts.IsDimPositive("RA2");
        bool ra2hPositive = facts.HasRa2H;
        bool slbEnabled = ResolveOptionalEnabled(facts, "SLB");
        bool isStd = shank == CobLikeShankType.Std;

        if (SupportsOverlayNonStandardCutPlanning)
        {
            if (vrPositive && vwPositive)
            {
                suppress.Add("cut_feature");
                unsuppress.Remove("cut_feature");

                unsuppress.Add("non_std_cut_plan_feature");
                unsuppress.Add("non_std_cut_feature");
                suppress.Remove("non_std_cut_plan_feature");
                suppress.Remove("non_std_cut_feature");

                Logger.Info($"[{LogPrefix}] Overlay rule: VR > 0 and VW > 0 → suppress cut_feature and unsuppress non_std_cut_plan_feature.");
            }
            else
            {
                suppress.Add("non_std_cut_plan_feature");
                suppress.Add("non_std_cut_feature");
                unsuppress.Remove("non_std_cut_plan_feature");
                unsuppress.Remove("non_std_cut_feature");
            }
        }

        if (!string.IsNullOrWhiteSpace(leftOverlaySketch))
        {
            if (vrPositive)
            {
                suppress.Add(leftOverlaySketch);
                unsuppress.Remove(leftOverlaySketch);

                Logger.Info($"[{LogPrefix}] Overlay rule: VR > 0 → suppress '{leftOverlaySketch}'.");
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

            Logger.Info($"[{LogPrefix}] Overlay rule: RA2 > 0 → suppress PGB_STD_FRONT_overlay_sketch.");
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

            Logger.Info($"[{LogPrefix}] Overlay rule: RA2H > 0 → unsuppress {(isStd ? Ra2hStdFront : Ra2hRevFront)}.");
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
            bool vwEqualsW = facts.AreNominalsEqual("VW", "W");

            if (vwEqualsW)
            {
                unsuppress.Add(VwLeftCase2);
                suppress.Add(VwLeftCase1);
                Logger.Info($"[{LogPrefix}] Overlay rule: VR > 0 and VW == W → unsuppress VW_LEFT_case_2_overlay_sketch.");
            }
            else
            {
                unsuppress.Add(VwLeftCase1);
                suppress.Add(VwLeftCase2);
                Logger.Info($"[{LogPrefix}] Overlay rule: VR > 0 and VW != W → unsuppress VW_LEFT_case_1_overlay_sketch.");
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

                foreach (var nm in BuildNameCandidatesWithSketches("10BA", CobLikeShankType.Std))
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

                foreach (var nm in BuildNameCandidatesWithSketches("10BA", CobLikeShankType.Rev180))
                {
                    suppress.Add(nm);
                    unsuppress.Remove(nm);
                }
            }

            Logger.Info($"[{LogPrefix}] Overlay rule: SLB enabled → unsuppress {(isStd ? SlbStdOverlay : SlbRevOverlay)}, suppress matching front overlay sketch and 10BA.");
        }
        else
        {
            suppress.Add(SlbStdOverlay);
            suppress.Add(SlbRevOverlay);
        }
    }

    protected static string BuildAnnotationName(string baseName, CobLikeShankType shank)
        => $"{baseName}_{BuildSuffix(shank)}_annotation";

    private void BuildFeaturePlanAlignedWithSpec(
        CobLikeRuleFacts facts,
        CobLikeShankType shank,
        CobLikeFootOption foot,
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

        bool baIsZero = IsDimZero(facts, "BA");
        if (baIsZero)
            Logger.Info($"[{LogPrefix}] Business rule triggered: BA == 0 → suppress 10BA (STD + 180_DEG_REV) and force-enable SLB.");

        var opposite = shank == CobLikeShankType.Std ? CobLikeShankType.Rev180 : CobLikeShankType.Std;

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
            foreach (var nm in BuildNameCandidatesWithSketches("10BA", CobLikeShankType.Std))
                suppress.Add(nm);

            foreach (var nm in BuildNameCandidatesWithSketches("10BA", CobLikeShankType.Rev180))
                suppress.Add(nm);

            unsuppress.RemoveWhere(nm => nm.StartsWith("10BA_", StringComparison.OrdinalIgnoreCase));
        }

        foreach (var opt in optionalBases)
        {
            bool enabled = ResolveOptionalEnabled(facts, opt);

            if (baIsZero && opt.Equals("SLB", StringComparison.OrdinalIgnoreCase))
                enabled = true;

            Logger.Info($"[{LogPrefix}] Optional '{opt}' enabled={enabled}");

            foreach (var nm in BuildNameCandidatesWithSketches(opt, shank))
            {
                if (enabled) unsuppress.Add(nm);
                else suppress.Add(nm);
            }
        }
    }

    protected static IEnumerable<string> ExpandForShank(IEnumerable<string> bases, CobLikeShankType shank)
    {
        foreach (var b in bases)
            foreach (var nm in BuildNameCandidatesWithSketches(b, shank))
                yield return nm;
    }

    protected static IEnumerable<string> ExpandFootForShank(CobLikeFootOption foot, CobLikeShankType shank)
    {
        return foot switch
        {
            CobLikeFootOption.C => ExpandForShank(new[] { "C", "BR_C", "FR_C" }, shank),
            CobLikeFootOption.G => ExpandForShank(new[] { "G", "BR_G", "FR_G" }, shank),
            CobLikeFootOption.VG => ExpandForShank(new[] { "VG", "BR_VG", "FR_VG" }, shank),
            CobLikeFootOption.CC => ExpandForShank(new[] { "C", "CG", "BR_C", "FR_C" }, shank),
            CobLikeFootOption.C_WithCbr => ExpandForShank(new[] { "C", "CBRA", "FR_C" }, shank),
            _ => Array.Empty<string>()
        };
    }

    protected static string BuildSuffix(CobLikeShankType shank)
        => shank == CobLikeShankType.Std ? "STD" : "180_DEG_REV";

    protected static string BuildFeatureName(string baseName, CobLikeShankType shank)
        => $"{baseName}_{BuildSuffix(shank)}_feature";

    protected static IEnumerable<string> BuildNameCandidatesWithSketches(string baseName, CobLikeShankType shank)
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

        if (baseName.Equals("RA2", StringComparison.OrdinalIgnoreCase) && shank == CobLikeShankType.Rev180)
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

    protected static IEnumerable<string> BuildHMandatoryCandidates(CobLikeShankType shank)
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

    protected static bool ResolveOptionalEnabled(CobLikeRuleFacts facts, string featureKey)
    {
        if (facts is null) return false;

        if (featureKey.Equals("SLB", StringComparison.OrdinalIgnoreCase))
            return IsDimEnabled(facts, "VBL");

        return IsDimEnabled(facts, featureKey);
    }

    protected static bool IsDimEnabled(CobLikeRuleFacts facts, string dimKey)
        => facts.TryGetNominalValue(dimKey, out var value) && value != 0m;

    protected static bool IsDimZero(CobLikeRuleFacts facts, string dimKey)
        => facts.TryGetNominalValue(dimKey, out var value) && value == 0m;

    protected CobLikeFootOption ResolveFootOption(CobLikeRuleFacts facts)
    {
        var raw =
            CobLikeRuleFacts.GetPropLoose(facts.Wedge, "Wed-Foot_Option") ??
            CobLikeRuleFacts.GetPropLoose(facts.Wedge, "Wed-FootOption") ??
            CobLikeRuleFacts.GetPropLoose(facts.Wedge, "Foot_Option") ??
            CobLikeRuleFacts.GetPropLoose(facts.Wedge, "foot_option") ??
            CobLikeRuleFacts.GetPropLoose(facts.Wedge, "FootOption") ??
            string.Empty;

        raw = CobLikeRuleFacts.NormalizeDbToken(raw);

        CobLikeFootOption baseFoot;

        if (EqualsAny(raw, "SW_G", "G")) baseFoot = CobLikeFootOption.G;
        else if (EqualsAny(raw, "SW_VG", "VG")) baseFoot = CobLikeFootOption.VG;
        else if (EqualsAny(raw, "SW_CG", "CG", "CC")) baseFoot = CobLikeFootOption.CC;
        else baseFoot = CobLikeFootOption.C;

        if (baseFoot == CobLikeFootOption.C)
        {
            bool allPositive =
                facts.IsDimPositive("CBRA") &&
                facts.IsDimPositive("CBRD") &&
                facts.IsDimPositive("CBRL");

            if (allPositive)
            {
                Logger.Info($"[{LogPrefix}] Foot rule: base=C and (CBRA/CBRD/CBRL all > 0) → using C_WithCbr.");
                return CobLikeFootOption.C_WithCbr;
            }
        }

        return baseFoot;
    }

    protected static bool EqualsAny(string value, params string[] options)
        => options.Any(o => string.Equals(value, o, StringComparison.OrdinalIgnoreCase));

    protected static string TryGetEngravingName()
    {
        try { return SwNames.Engraving; }
        catch { return "Engraving"; }
    }

    protected void ApplyOverlayConfigurationOverride(
        FeatureRuleContext context,
        HashSet<string> suppress,
        HashSet<string> unsuppress)
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
                ForceStandardCutState(suppress, unsuppress);
                Logger.Info($"[{LogPrefix}] Overlay config/profile '{profile}' → standard cut state.");
                break;

            case "non_std_cut":
                ForceNonStandardCutState(suppress, unsuppress);
                Logger.Info($"[{LogPrefix}] Overlay config/profile 'non_std_cut' → non-standard cut state.");
                break;
        }
    }

    protected static string? ResolveOverlayCutProfile(FeatureRuleContext context)
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

    protected static void ForceStandardCutState(HashSet<string> suppress, HashSet<string> unsuppress)
    {
        unsuppress.Add("cut_plan_feature");
        unsuppress.Add("cut_feature");
        suppress.Remove("cut_plan_feature");
        suppress.Remove("cut_feature");

        suppress.Add("non_std_cut_plan_feature");
        suppress.Add("non_std_cut_feature");
        unsuppress.Remove("non_std_cut_plan_feature");
        unsuppress.Remove("non_std_cut_feature");
    }

    protected static void ForceNonStandardCutState(HashSet<string> suppress, HashSet<string> unsuppress)
    {
        unsuppress.Add("cut_plan_feature");
        suppress.Remove("cut_plan_feature");

        suppress.Add("cut_feature");
        unsuppress.Remove("cut_feature");

        unsuppress.Add("non_std_cut_plan_feature");
        unsuppress.Add("non_std_cut_feature");
        suppress.Remove("non_std_cut_plan_feature");
        suppress.Remove("non_std_cut_feature");
    }
}
