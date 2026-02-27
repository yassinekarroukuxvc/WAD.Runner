// ModelAutomation/Rules/OSG7/Osg7FeatureRules.cs
using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

using WAD.Runner.ModelAutomation.Execution;

namespace WAD.Runner.ModelAutomation.Rules
{
    /// <summary>
    /// OSG7 feature toggle planning (NO SolidWorks calls, NO rebuild).
    /// Ported from old PartAutomation OSG7Rules.ApplyFeatureStates.
    /// </summary>
    public sealed class Osg7FeatureRules : IFeatureRuleSet
    {
        private const double Eps = 1e-6;

        public ModelRuleRunner.FeaturePlan Build(WedgeData wedge, DrawingType drawingType, WedgeSubclass subclass)
        {
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));

            Logger.Info("[Osg7FeatureRules] Build → start");
            Logger.Info($"[Osg7FeatureRules] Subclass={wedge.Subclass}, DrawingType={drawingType}");

            var typeAns = wedge.Subclass == WedgeSubclass.PGB ? "PGB" : "FG";
            var overlayEnabled = drawingType == DrawingType.Overlay;

            var useVr = new[] { "VW", "VR", "VRR", "VRA" }.Any(k => HasNonZeroNominal(wedge, k));
            var useVfl = new[] { "VFL", "VFLR" }.Any(k => HasNonZeroNominal(wedge, k));
            var hasTip = HasNonZeroNominal(wedge, "TIP");

            Logger.Info($"[Osg7FeatureRules] Mode: {typeAns} | overlay={overlayEnabled} | useVR={useVr} | useVFL={useVfl} | TIP={hasTip}");

            var offSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var onSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ApplyFeatureStates(typeAns, overlayEnabled, useVr, useVfl, hasTip, offSet, onSet);

            // Unsuppress wins
            offSet.RemoveWhere(n => onSet.Contains(n));

            Logger.Success($"[Osg7FeatureRules] Build → done. unsuppress={onSet.Count}, suppress={offSet.Count}");

            return new ModelRuleRunner.FeaturePlan(
                Suppress: offSet.ToArray(),
                Unsuppress: onSet.ToArray());
        }

        private static bool HasNonZeroNominal(WedgeData wedge, string key)
        {
            if (!wedge.Dimensions.TryGetValue(new DimensionKey(key), out var dim) || dim is null)
                return false;

            var val = dim.Nominal.Value;
            return Math.Abs((double)val) > Eps;
        }

        private static void ApplyFeatureStates(
            string typeAns,
            bool overlayEnabled,
            bool useVr,
            bool useVfl,
            bool hasTip,
            HashSet<string> offSet,
            HashSet<string> onSet)
        {
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

            // default off = everything optional + overlay + tip + VR/VFL variants etc.
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
                // In PGB mode, groove features are OFF
                AddAll(offSet, new[] { "G_groove_feature", "G_groove_sketch" });

                var pgbOn = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                AddAll(pgbOn, pgbCoreFeatures);

                if (overlayEnabled)
                {
                    AddAll(pgbOn, overlayFeatures);
                    AddAll(pgbOn, pgbOverlaySketches);
                }

                foreach (var nm in pgbOn) offSet.Remove(nm);
                AddAll(onSet, pgbOn);
                return;
            }

            // FG mode
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

                if (useVfl) enabledOverlays.Add("FG_FL_STD_VFL_overlay_sketch");
                else if (useVr) enabledOverlays.Add("FG_B_STD_VR_overlay_sketch");
                else
                {
                    enabledOverlays.Add("FG_FL_STD_overlay_sketch");
                    enabledOverlays.Add("FG_B_STD_overlay_sketch");
                }

                AddAll(onSet, enabledOverlays);
            }

            foreach (var nm in onSet) offSet.Remove(nm);
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
}
