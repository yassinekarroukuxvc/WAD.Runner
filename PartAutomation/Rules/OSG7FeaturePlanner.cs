// PartAutomation/Rules/OSG7FeaturePlanner.cs
using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.PartAutomation.SolidWorks;

namespace WAD.Runner.PartAutomation.Rules
{
    /// <summary>
    /// Macro-aligned planner + executor for OSG7:
    /// - Computes which features/sketches must be ON vs OFF (single deterministic plan)
    /// - Applies OFF then ON (macro style)
    /// - Emits SuppressedGroups so EquationUpsert can skip adding unreferenced vars for disabled options
    ///
    /// This replaces the old OSG7Rules "toggle inline" style and makes it easier to test + extend.
    /// </summary>
    public static class OSG7FeaturePlanner
    {
        private const double Eps = 1e-6;

        // Public API ----------------------------------------------------------

        /// <summary>
        /// Build the toggle plan only (no SolidWorks calls).
        /// </summary>
        public static FeatureTogglePlan Build(WedgeData wedge, DrawingType drawingType)
        {
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));

            var mode = GetTypeAns(wedge);              // "FG" or "PGB"
            var overlayEnabled = drawingType == DrawingType.Overlay;

            var useVr = DecideVrOption(wedge);
            var useVfl = DecideVflOption(wedge);
            var hasTip = DecideTipOption(wedge);

            var b = FeatureTogglePlan.Create()
                .Note($"OSG7 mode={mode}, overlay={overlayEnabled}, useVR={useVr}, useVFL={useVfl}, TIP={hasTip}");

            // If option is disabled, mark that group as suppressed => EquationUpsert may skip ADDs
            // for unreferenced vars with that base token.
            if (!useVr) b.SuppressedGroup("VR", true);
            if (!useVfl) b.SuppressedGroup("VFL", true);
            if (!hasTip) b.SuppressedGroup("TIP", true);

            if (string.Equals(mode, "PGB", StringComparison.OrdinalIgnoreCase))
                return BuildPgbPlan(b, overlayEnabled);

            return BuildFgPlan(b, overlayEnabled, useVr, useVfl, hasTip);
        }

        /// <summary>
        /// Build + apply in macro order: OFF first, then ON, then one rebuild.
        /// </summary>
        public static FeatureTogglePlan Apply(PartEditor part, WedgeData wedge, DrawingType drawingType)
        {
            if (part is null) throw new ArgumentNullException(nameof(part));
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));

            Logger.Info("[OSG7FeaturePlanner] Apply → start");

            var plan = Build(wedge, drawingType).Canonicalize();

            if (plan.Notes.Count > 0)
                Logger.Info("[OSG7FeaturePlanner] " + string.Join(" | ", plan.Notes));

            ApplyPlan(part, plan);

            part.Rebuild();
            Logger.Success("[OSG7FeaturePlanner] Apply → done");
            return plan;
        }

        // Planning -----------------------------------------------------------

        private static FeatureTogglePlan BuildPgbPlan(FeatureTogglePlan.Builder b, bool overlayEnabled)
        {
            // NOTE:
            // This mirrors your previous OSG7Rules PGB branch:
            // - G groove is OFF
            // - PGB core is ON
            // - If overlay => overlay features + PGB overlay sketches ON

            var pgbCore = new[]
            {
                "TD_sketch",
                "TL_feature",
                "TDF_sketch", "TDF_feature",
                "ISA_sketch", "ISA_feature",
                "STD_shank_sketch", "STD_shank_feature"
            };

            var overlayFeatures = new[] { "ref_point", "H_cut_plan", "H_cut_feature" };
            var pgbOverlaySketches = new[] { "PGB_FL_overlay_sketch", "PGB_W_overlay_sketch" };

            // OFF baseline for PGB: explicitly kill G groove if it exists
            b.Off("G_groove_feature", "G_groove_sketch");

            // ON core
            b.On(pgbCore);

            if (overlayEnabled)
            {
                b.On(overlayFeatures);
                b.On(pgbOverlaySketches);
                b.Note("PGB overlay enabled → overlay features + PGB overlay sketches ON");
            }
            else
            {
                // When not overlay, make sure overlay stuff is OFF (macro explicitness)
                b.Off(overlayFeatures);
                b.Off(pgbOverlaySketches);
            }

            return b.Build();
        }

        private static FeatureTogglePlan BuildFgPlan(
            FeatureTogglePlan.Builder b,
            bool overlayEnabled,
            bool useVr,
            bool useVfl,
            bool hasTip)
        {
            // NOTE:
            // This mirrors your previous OSG7Rules FG branch, but expressed as a plan.

            var core = new[]
            {
                "TD_sketch",
                "TL_feature",
                "TDF_sketch", "TDF_feature",
                "ISA_sketch", "ISA_feature",
                "VR_sketch",
                "STD_shank_sketch", "STD_shank_feature",
                "G_groove_sketch", "G_groove_feature"
            };

            var vfl = new[] { "VFL_sketch", "VFL_feature" };
            var vr = new[] { "VR_sketch", "VR_feature" };

            var frbrStd = new[] { "FR_BR_STD_sketch", "FR_BR_STD_feature", "FR_BR_STD_cut_feature" };
            var frbrVfl = new[] { "FR_BR_STD_VFL_sketch", "FR_BR_STD_VFL_feature", "FR_BR_STD_VFL_cut_feature" };

            var flStd = new[] { "FL_STD_sketch" };
            var flVfl = new[] { "FL_STD_VFL_sketch" };

            var tip = new[] { "TIP_sketch" };

            var overlayFeatures = new[] { "ref_point", "H_cut_plan", "H_cut_feature" };

            // All possible overlay sketches (FG)
            var overlaySketchesAll = new[]
            {
                "FG_FL_STD_overlay_sketch",
                "FG_FL_STD_VFL_overlay_sketch",
                "FG_B_STD_overlay_sketch",
                "FG_B_STD_VR_overlay_sketch"
            };

            // Macro style: start by pushing the known universe to OFF, then select what must be ON.
            b.Off(vfl);
            b.Off(vr);
            b.Off(frbrStd);
            b.Off(frbrVfl);
            b.Off(flStd);
            b.Off(flVfl);
            b.Off(tip);
            b.Off(overlayFeatures);
            b.Off(overlaySketchesAll);

            // Always ON core (FG)
            b.On(core);

            if (useVr) b.On(vr);
            if (useVfl) b.On(vfl);

            // Choose FR/BR + FL set based on VFL
            if (useVfl)
            {
                b.On(frbrVfl);
                b.On(flVfl);
            }
            else
            {
                b.On(frbrStd);
                b.On(flStd);
            }

            if (hasTip)
                b.On(tip);

            if (overlayEnabled)
            {
                b.On(overlayFeatures);

                // Select overlay sketch(es) exactly like your previous logic:
                // - If VFL => FG_FL_STD_VFL_overlay_sketch
                // - Else if VR => FG_B_STD_VR_overlay_sketch
                // - Else => FG_FL_STD_overlay_sketch + FG_B_STD_overlay_sketch
                if (useVfl)
                {
                    b.On("FG_FL_STD_VFL_overlay_sketch");
                    b.Note("FG overlay: VFL path → FG_FL_STD_VFL_overlay_sketch");
                }
                else if (useVr)
                {
                    b.On("FG_B_STD_VR_overlay_sketch");
                    b.Note("FG overlay: VR path → FG_B_STD_VR_overlay_sketch");
                }
                else
                {
                    b.On("FG_FL_STD_overlay_sketch", "FG_B_STD_overlay_sketch");
                    b.Note("FG overlay: default path → FG_FL_STD_overlay_sketch + FG_B_STD_overlay_sketch");
                }
            }

            return b.Build();
        }

        // Execution ----------------------------------------------------------

        private static void ApplyPlan(PartEditor part, FeatureTogglePlan plan)
        {
            // Macro order matters: suppress first to prevent suppressed-child evaluation weirdness,
            // then unsuppress what you need.
            Logger.Info($"[OSG7FeaturePlanner] ApplyPlan → OFF={plan.Off.Count}, ON={plan.On.Count}");

            // OFF
            foreach (var name in plan.Off)
                ToggleSafe(part, name, suppress: true);

            // ON
            foreach (var name in plan.On)
                ToggleSafe(part, name, suppress: false);
        }

        private static void ToggleSafe(PartEditor part, string featureName, bool suppress)
        {
            if (string.IsNullOrWhiteSpace(featureName))
                return;

            try
            {
                // Use the fast "IfNeeded" path to avoid unnecessary COM calls
                // (same principle as the VBA macro / FeatureSuppression helper).
                bool touched = part.TrySuppressFeatureIfNeeded(featureName, suppress);
                if (touched)
                    Logger.Info($"[OSG7FeaturePlanner] {(suppress ? "SUPPRESS" : "UNSUPPRESS")} → {featureName}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[OSG7FeaturePlanner] Toggle failed for '{featureName}'. {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Option decisions ---------------------------------------------------

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
            if (wedge?.Dimensions == null) return false;

            if (!wedge.Dimensions.TryGetValue(new DimensionKey(key), out var dim) || dim is null)
                return false;

            // We only care "present and non-zero" as a macro switch
            var v = (double)dim.Nominal.Value;
            return Math.Abs(v) > Eps;
        }
    }
}
