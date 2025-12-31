// PartAutomation/Rules/OSG7Rules.cs
using System;
using System.Collections.Generic;
using System.Linq;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.PartAutomation.SolidWorks;

namespace WAD.Runner.PartAutomation.Rules;

public static class OSG7Rules
{
    private const double Eps = 1e-6;

    public static void Apply(PartEditor part, WedgeData wedge, DrawingType drawingType)
    {
        Logger.Info("[OSG7Rules] Apply → start");
        Logger.Info($"[OSG7Rules] Subclass={wedge.Subclass}, DrawingType={drawingType}");

        var typeAns = GetTypeAns(wedge);
        var overlayEnabled = drawingType == DrawingType.Overlay;

        var useVr = DecideVrOption(wedge);
        var useVfl = DecideVflOption(wedge);
        var hasTip = DecideTipOption(wedge);

        Logger.Info($"[OSG7Rules] Mode: {typeAns} | overlay={overlayEnabled} | useVR={useVr} | useVFL={useVfl} | TIP={hasTip}");

        ApplyFeatureStates(part, typeAns, overlayEnabled, useVr, useVfl, hasTip);

        part.Rebuild();
        Logger.Success("[OSG7Rules] Apply → done.");
    }

    private static string GetTypeAns(WedgeData wedge)
        => wedge.Subclass == WedgeSubclass.PGB ? "PGB" : "FG";

    private static bool DecideVrOption(WedgeData wedge)
        => new[] { "VW", "VR", "VRR", "VRA" }.Any(k => HasNonZeroNominal(wedge, k));

    private static bool DecideVflOption(WedgeData wedge)
        => new[] { "VFL", "VFLR" }.Any(k => HasNonZeroNominal(wedge, k));

    private static bool DecideTipOption(WedgeData wedge)
        => HasNonZeroNominal(wedge, "TIP");

    private static bool HasNonZeroNominal(WedgeData wedge, string key)
    {
        if (!wedge.Dimensions.TryGetValue(new DimensionKey(key), out var dim) || dim is null)
            return false;

        var val = dim.Nominal.Value;
        return Math.Abs((double)val) > Eps;
    }

    private static void ApplyFeatureStates(
        PartEditor part,
        string typeAns,
        bool overlayEnabled,
        bool useVr,
        bool useVfl,
        bool hasTip)
    {
        Logger.Info("[OSG7Rules] ApplyFeatureStates → start");

        var coreFeatures = new[]
        {
            "TD_sketch",
            "TL_feature",
            "TDF_sketch", "TDF_feature",
            "ISA_sketch", "ISA_feature",
            "VR_sketch",
            "STD_shank_sketch", "STD_shank_feature",
            "G_groove_sketch", "G_groove_feature"
        };

        var vflFeatures = new[] { "VFL_sketch", "VFL_feature" };
        var vrFeatures = new[] { "VR_sketch", "VR_feature" };

        var frbrStdFeatures = new[] { "FR_BR_STD_sketch", "FR_BR_STD_feature", "FR_BR_STD_cut_feature" };
        var frbrVflFeatures = new[] { "FR_BR_STD_VFL_sketch", "FR_BR_STD_VFL_feature", "FR_BR_STD_VFL_cut_feature" };

        var flStdFeatures = new[] { "FL_STD_sketch" };
        var flVflFeatures = new[] { "FL_STD_VFL_sketch" };

        var tipFeatures = new[] { "TIP_sketch" };

        var overlayFeatures = new[] { "ref_point", "H_cut_plan", "H_cut_feature" };
        var overlaySketchesAll = new[]
        {
            "FG_FL_STD_overlay_sketch",
            "FG_FL_STD_VFL_overlay_sketch",
            "FG_B_STD_overlay_sketch",
            "FG_B_STD_VR_overlay_sketch"
        };

        var pgbOverlaySketches = new[] { "PGB_FL_overlay_sketch", "PGB_W_overlay_sketch" };

        var pgbCoreFeatures = new[]
        {
            "TD_sketch",
            "TL_feature",
            "TDF_sketch", "TDF_feature",
            "ISA_sketch", "ISA_feature",
            "STD_shank_sketch", "STD_shank_feature"
        };

        var offSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var onSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddAll(offSet, vflFeatures);
        AddAll(offSet, vrFeatures);
        AddAll(offSet, frbrStdFeatures);
        AddAll(offSet, frbrVflFeatures);
        AddAll(offSet, flStdFeatures);
        AddAll(offSet, flVflFeatures);
        AddAll(offSet, tipFeatures);
        AddAll(offSet, overlayFeatures);
        AddAll(offSet, overlaySketchesAll);
        AddAll(offSet, pgbOverlaySketches);

        if (string.Equals(typeAns, "PGB", StringComparison.OrdinalIgnoreCase))
        {
            AddAll(offSet, new[] { "G_groove_feature", "G_groove_sketch" });

            var pgbOn = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddAll(pgbOn, pgbCoreFeatures);

            if (overlayEnabled)
            {
                AddAll(pgbOn, overlayFeatures);
                AddAll(pgbOn, pgbOverlaySketches);
                Logger.Info("[OSG7Rules] PGB overlay enabled; unsuppress overlay features + PGB overlay sketches.");
            }

            foreach (var nm in pgbOn) offSet.Remove(nm);

            ApplyToggles(part, offSet, suppress: true);
            ApplyToggles(part, pgbOn, suppress: false);

            Logger.Success("[OSG7Rules] ApplyFeatureStates → done. (PGB mode)");
            return;
        }

        AddAll(onSet, coreFeatures);

        if (useVr) AddAll(onSet, vrFeatures);
        if (useVfl) AddAll(onSet, vflFeatures);

        AddAll(onSet, useVfl ? frbrVflFeatures : frbrStdFeatures);
        AddAll(onSet, useVfl ? flVflFeatures : flStdFeatures);

        if (hasTip) AddAll(onSet, tipFeatures);

        if (overlayEnabled)
        {
            AddAll(onSet, overlayFeatures);

            var enabledOverlays = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (useVfl)
            {
                enabledOverlays.Add("FG_FL_STD_VFL_overlay_sketch");
            }
            else if (useVr)
            {
                enabledOverlays.Add("FG_B_STD_VR_overlay_sketch");
            }
            else
            {
                enabledOverlays.Add("FG_FL_STD_overlay_sketch");
                enabledOverlays.Add("FG_B_STD_overlay_sketch");
            }

            AddAll(onSet, enabledOverlays);

            Logger.Info("[OSG7Rules] FG overlay enabled; selected overlay sketch(es): " +
                        (enabledOverlays.Count == 0 ? "(none)" : string.Join(", ", enabledOverlays)));
        }

        foreach (var nm in onSet) offSet.Remove(nm);

        ApplyToggles(part, offSet, suppress: true);
        ApplyToggles(part, onSet, suppress: false);

        Logger.Success("[OSG7Rules] ApplyFeatureStates → done. (FG mode)");
    }

    private static void ApplyToggles(PartEditor part, IEnumerable<string> featureNames, bool suppress)
    {
        foreach (var f in featureNames.Where(s => !string.IsNullOrWhiteSpace(s)))
            SuppressFeatureSafe(part, f, suppress);
    }

    private static void SuppressFeatureSafe(PartEditor part, string featureName, bool suppress)
    {
        try
        {
            part.SuppressFeature(featureName, suppress);
            Logger.Info($"[OSG7Rules] Feature {(suppress ? "SUPPRESS" : "UNSUPPRESS")} → {featureName}");
        }
        catch (Exception ex)
        {
            Logger.Warn($"[OSG7Rules] Feature toggle failed for '{featureName}'. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void AddAll(HashSet<string> set, IEnumerable<string> items)
    {
        foreach (var s in items)
        {
            if (!string.IsNullOrWhiteSpace(s))
                set.Add(s);
        }
    }
}
