// PartAutomation/Rules/COBRules.cs
using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.PartAutomation.Common;
using WAD.Runner.PartAutomation.SolidWorks;

namespace WAD.Runner.PartAutomation.Rules;

public static class COBRules
{
    // You said the template is now:
    // - TL_feature ON
    // - Mandatory STD shank features ON
    // - Everything else OFF
    //
    // We take advantage of that baseline to avoid massive OFF-set toggling.

    public static void Apply(PartEditor part, WedgeData wedge, DrawingType drawingType)
    {
        Logger.Info("[COBRules] Apply → start");

        if (drawingType == DrawingType.Production || drawingType == DrawingType.Customer)
        {
            Logger.Info("[COBRules] Non-overlay drawing → apply engraving toggle.");
            BasicPartRules.ApplyEngravingToggle(part);
        }
        else
        {
            Logger.Info("[COBRules] Overlay drawing → no engraving sketch change (for now).");
        }

        var shank = ResolveShankType(wedge);
        var foot = ResolveFootOption(wedge);

        Logger.Info($"[COBRules] Parsed → Shank={shank}, Foot={foot}");

        ApplyFeatureStatesFast(part, wedge, shank, foot);

        // Do NOT rebuild multiple times inside; keep 1 rebuild at the end.
        part.Rebuild();
        Logger.Success("[COBRules] Apply → done.");
    }

    // --------------------------------------------
    // FAST delta toggles (baseline-aware)
    // --------------------------------------------
    private static void ApplyFeatureStatesFast(PartEditor part, WedgeData wedge, CobShankType shank, CobFootOption foot)
    {
        Logger.Info("[COBRules] ApplyFeatureStatesFast → start");

        // Mandatory features (bases) that must be ON for the selected shank.
        // TL is common (no suffix).
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

        // Optionals (bases)
        var optionalBases = new[] { "SLB", "VW", "W2", "RA2" };

        // Foot + fillets (bases)
        // NOTE: CG is only meaningful for CC; CBRA is only for C_WithCbr
        var footAndFilletBases_All = new[]
        {
            "C", "G", "VG", "CG", "CBRA",
            "BR_C", "FR_C",
            "BR_G", "FR_G",
            "BR_VG", "FR_VG"
        };

        // Always keep TL ON (common feature).
        ToggleIfNeeded(part, "TL_feature", suppress: false);

        // ---------------------------------------------------------
        // RULE: If FRO == FR ⇒ suppress every FR_* feature
        // ---------------------------------------------------------
        bool froEqualsFr = AreDimensionsEqualMm(wedge, "FRO", "FR");
        if (froEqualsFr)
        {
            Logger.Info("[COBRules] Rule triggered: FRO == FR → forcing OFF all FR_* features.");
        }

        // 1) Ensure correct shank mandatory set
        // Baseline assumption:
        // - STD mandatory is already ON when shank == Std
        // - Everything else is OFF
        //
        // So:
        // - If Std → do basically nothing for mandatory
        // - If Rev180 → swap: suppress STD mandatory and unsuppress REV mandatory
        if (shank == CobShankType.Std)
        {
            Logger.Info("[COBRules] Shank=STD → baseline already has mandatory STD ON. Skipping mandatory toggles.");
        }
        else
        {
            Logger.Info("[COBRules] Shank=180_DEG_REV → switching mandatory features from STD → 180_DEG_REV.");

            // Suppress STD mandatory
            foreach (var b in mandatoryBases)
            {
                foreach (var nm in BuildFeatureNameCandidates(b, CobShankType.Std))
                    ToggleIfNeeded(part, nm, suppress: true);
            }

            // Unsuppress 180 mandatory
            foreach (var b in mandatoryBases)
            {
                foreach (var nm in BuildFeatureNameCandidates(b, CobShankType.Rev180))
                    ToggleIfNeeded(part, nm, suppress: false);
            }
        }

        // 2) Foot selection (delta)
        // We only need to ensure:
        // - foot-related features not needed are suppressed
        // - the chosen ones are unsuppressed
        //
        // Since baseline is "everything OFF", this becomes:
        // - Unsuppress chosen foot set
        // - Suppress the other foot sets ONLY if currently ON (e.g., when switching between jobs)
        var desiredFoot = ExpandFootForShank(foot, shank).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allFoot = ExpandForShank(footAndFilletBases_All, shank).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var nm in allFoot)
        {
            bool shouldBeOn = desiredFoot.Contains(nm);

            // Enforce FR suppression if rule triggered
            if (froEqualsFr && nm.StartsWith("FR_", StringComparison.OrdinalIgnoreCase))
                shouldBeOn = false;

            ToggleIfNeeded(part, nm, suppress: !shouldBeOn);
        }

        // Extra hard-force OFF (covers leftovers from prior runs)
        if (froEqualsFr)
        {
            foreach (var frBase in new[] { "FR_C", "FR_G", "FR_VG" })
            {
                foreach (var nm in BuildFeatureNameCandidates(frBase, shank))
                    ToggleIfNeeded(part, nm, suppress: true);
            }
        }

        // 3) Optional features (delta)
        // For each optional base:
        // - if enabled → unsuppress for selected shank
        // - else → suppress for selected shank (only if it ended up ON from previous run)
        foreach (var opt in optionalBases)
        {
            bool enabled = ResolveOptionalEnabled(wedge, opt);

            foreach (var nm in BuildFeatureNameCandidates(opt, shank))
                ToggleIfNeeded(part, nm, suppress: !enabled);

            Logger.Info($"[COBRules] Optional '{opt}' enabled={enabled}");
        }

        Logger.Success("[COBRules] ApplyFeatureStatesFast → done.");
    }

    /// <summary>
    /// IMPORTANT: This should avoid calling suppression if the state is already correct.
    /// That is where most of the speed-up comes from.
    ///
    /// If you don't yet have an "IfNeeded" API on PartEditor, replace this with part.SuppressFeature(...)
    /// temporarily, but you'll lose most of the speed benefit.
    /// </summary>
    private static void ToggleIfNeeded(PartEditor part, string featureName, bool suppress)
    {
        if (string.IsNullOrWhiteSpace(featureName))
            return;

        try
        {
            if (part.TrySuppressFeatureIfNeeded(featureName, suppress))
            {
                Logger.Info($"[COBRules] {(suppress ? "SUPPRESS" : "UNSUPPRESS")} → {featureName}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"[COBRules] Toggle failed for '{featureName}'. {ex.GetType().Name}: {ex.Message}");
        }
    }

    // --------------------------------------------
    // Name expansion helpers
    // --------------------------------------------
    private static IEnumerable<string> ExpandForShank(IEnumerable<string> bases, CobShankType shank)
    {
        foreach (var b in bases)
        {
            foreach (var nm in BuildFeatureNameCandidates(b, shank))
                yield return nm;
        }
    }

    private static IEnumerable<string> ExpandFootForShank(CobFootOption foot, CobShankType shank)
    {
        return foot switch
        {
            CobFootOption.C =>
                ExpandForShank(new[] { "C", "BR_C", "FR_C" }, shank),

            CobFootOption.G =>
                ExpandForShank(new[] { "G", "BR_G", "FR_G" }, shank),

            CobFootOption.VG =>
                ExpandForShank(new[] { "VG", "BR_VG", "FR_VG" }, shank),

            CobFootOption.CC =>
                ExpandForShank(new[] { "C", "CG", "BR_C", "FR_C" }, shank),

            CobFootOption.C_WithCbr =>
                // Special: C + CBRA + FR_C, but NOT BR_C
                ExpandForShank(new[] { "C", "CBRA", "FR_C" }, shank),

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

        // Spec typo variant in 180 list: RA2_180_DEF_REV_feature
        if (shank == CobShankType.Rev180 && baseName.Equals("RA2", StringComparison.OrdinalIgnoreCase))
            yield return "RA2_180_DEF_REV_feature";
    }

    // --------------------------------------------
    // WedgeData parsing
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
        Logger.Info($"[COBRules] ResolveShankType raw='{raw}'");

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
        Logger.Info($"[COBRules] ResolveFootOption raw='{raw}'");

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

    private static bool ResolveOptionalEnabled(WedgeData wedge, string key)
    {
        var prop =
            GetPropLoose(wedge, key) ??
            GetPropLoose(wedge, $"{key}_enabled") ??
            GetPropLoose(wedge, $"{key}_Enabled") ??
            GetPropLoose(wedge, $"has_{key}") ??
            GetPropLoose(wedge, $"Has_{key}");

        if (!string.IsNullOrWhiteSpace(prop))
            return ParseBoolLoose(NormalizeDbToken(prop));

        // fallback: dimension exists and nominal > 0
        if (wedge.Dimensions != null && wedge.Dimensions.TryGetValue(new DimensionKey(key), out var dim))
        {
            if (dim.Nominal.IsMm) return dim.Nominal.AsMm() > 0m;
            if (dim.Nominal.IsDeg) return dim.Nominal.AsDeg() > 0m;
        }

        return false;
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
    // FR suppression helpers
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

        if (!wedge.Dimensions.TryGetValue(new DimensionKey(key), out var dim))
            return false;

        if (!dim.Nominal.IsMm) return false;

        mm = dim.Nominal.AsMm();
        return true;
    }
}
