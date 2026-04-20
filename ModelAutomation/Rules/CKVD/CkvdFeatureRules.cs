// ModelAutomation/Rules/CkvdFeatureRules.cs
using System;
using System.Collections.Generic;

using WAD.Runner.Application; // Logger
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;

using WAD.Runner.ModelAutomation.Execution; // IFeatureRuleSet + FeaturePlan
using WAD.Runner.ModelAutomation.Common;    // SwNames (optional)

namespace WAD.Runner.ModelAutomation.Rules
{
    public sealed class CkvdFeatureRules : IFeatureRuleSet
    {
        public ModelRuleRunner.FeaturePlan Build(WedgeData wedge, FeatureRuleContext context)
        {
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));

            var drawingType = context.DrawingType;
            var subclass = context.Subclass;
            var targetConfigurationName = context.TargetConfigurationName;
            var featureRuleProfile = context.FeatureRuleProfile;

            var suppress = new List<string>();
            var unsuppress = new List<string>();

            Logger.Info($"[CkvdFeatureRules] Build → subclass={wedge.Subclass}, drawingType={drawingType}");

            // ============================================================
            // 1) Engraving: ALWAYS SUPPRESSED (feature + sketch)
            // ============================================================
            var engravingFeatureName = ResolveEngravingFeatureName();
            var engravingSketchName = ResolveEngravingSketchName();

            suppress.Add(engravingFeatureName);
            suppress.Add(engravingSketchName);

            Logger.Info($"[CkvdFeatureRules] Always suppress engraving → feature='{engravingFeatureName}', sketch='{engravingSketchName}'");

            // ============================================================
            // 2) Old CKVD FG-only logic
            // ============================================================
            if (wedge.Subclass != WedgeSubclass.FG)
            {
                Logger.Info("[CkvdFeatureRules] Non-FG → skip FG-only TIP/VW-W rules (engraving suppression already applied).");
                return new ModelRuleRunner.FeaturePlan(suppress, unsuppress);
            }

            // 2.1) TIP guard (SketchCrmet)
            ApplyTipGuardPlan(wedge, suppress, unsuppress);

            // 2.2) Overlay VW/W toggle (FG_Wed_W vs FG_Wed_VW)
            ApplyOverlayVwWTogglePlan(wedge, drawingType == DrawingType.Overlay, suppress, unsuppress);

            return new ModelRuleRunner.FeaturePlan(suppress, unsuppress);
        }

        // ============================================================
        // TIP guard: suppress SketchCrmet when TIP==0 (FG only)
        // ============================================================
        private static void ApplyTipGuardPlan(WedgeData wedge, List<string> suppress, List<string> unsuppress)
        {
            if (!TryGetMm(wedge, "TIP", out var tipMm))
            {
                Logger.Blue("[CkvdFeatureRules] TIP not present/mm → TIP guard skipped (template state remains).");
                return;
            }

            var sketchCrmet = ResolveSketchCrmetName();

            if (tipMm == 0m)
            {
                suppress.Add(sketchCrmet);
                Logger.Info($"[CkvdFeatureRules] TIP={tipMm} mm → suppress '{sketchCrmet}'");
            }
            else
            {
                // Old behavior was "suppress: zero"; explicit unsuppress makes this deterministic.
                unsuppress.Add(sketchCrmet);
                Logger.Info($"[CkvdFeatureRules] TIP={tipMm} mm → unsuppress '{sketchCrmet}'");
            }
        }

        // ============================================================
        // Overlay VW/W toggle: if VW≈W -> enable VW sketch, else enable W sketch
        // ============================================================
        private static void ApplyOverlayVwWTogglePlan(WedgeData wedge, bool overlay, List<string> suppress, List<string> unsuppress)
        {
            Logger.Info($"[CkvdFeatureRules] ApplyOverlayVwWTogglePlan → overlay={overlay}");

            if (!overlay)
            {
                Logger.Blue("[CkvdFeatureRules] Not Overlay → skip VW/W toggle.");
                return;
            }

            var sketchW = ResolveSketchFgWedWName();
            var sketchVW = ResolveSketchFgWedVwName();

            var hasVW = TryGetMm(wedge, "VW", out var vwMm);
            var hasW = TryGetMm(wedge, "W", out var wMm);

            if (!(hasVW && hasW))
            {
                Logger.Warn("[CkvdFeatureRules] Missing VW or W (or not mm) → default to W sketch enabled.");
                unsuppress.Add(sketchW);
                suppress.Add(sketchVW);
                return;
            }

            // match old tolerance intent
            var equal = Math.Abs((double)(vwMm - wMm)) <= 0.000001;

            Logger.Info($"[CkvdFeatureRules] VW={vwMm} mm, W={wMm} mm, equal≈{equal}");

            if (equal)
            {
                Logger.Info("[CkvdFeatureRules] VW≈W → enable VW sketch, disable W sketch");
                unsuppress.Add(sketchVW);
                suppress.Add(sketchW);
            }
            else
            {
                Logger.Info("[CkvdFeatureRules] VW≠W → enable W sketch, disable VW sketch");
                unsuppress.Add(sketchW);
                suppress.Add(sketchVW);
            }
        }

        // ============================================================
        // WedgeData helpers
        // ============================================================
        private static bool TryGetMm(WedgeData wedge, string key, out decimal mm)
        {
            mm = 0m;
            if (!TryGetDim(wedge, key, out var d)) return false;
            if (!d.Nominal.IsMm) return false;
            mm = d.Nominal.AsMm();
            return true;
        }

        private static bool TryGetDim(WedgeData wedge, string key, out Dimension d)
        {
            d = default!;
            if (wedge.Dimensions is null) return false;
            return wedge.Dimensions.TryGetValue(new DimensionKey(key), out d);
        }

        // ============================================================
        // Name resolvers (SwNames preferred; literals fallback)
        // ============================================================
        private static string ResolveEngravingFeatureName()
        {
            try { return SwNames.EngravingFeature; } catch { return "Engraving"; }
        }

        private static string ResolveEngravingSketchName()
        {
            try { return SwNames.EngravingSketch; } catch { return "sketch_Engraving"; }
        }

        private static string ResolveSketchCrmetName()
        {
            try { return SwNames.SketchCrmet; } catch { return "Drawing_CRMET"; } // adjust literal if needed
        }

        private static string ResolveSketchFgWedWName()
        {
            try { return SwNames.SketchFgWedW; } catch { return "FG_Wed_W"; } // adjust literal if needed
        }

        private static string ResolveSketchFgWedVwName()
        {
            try { return SwNames.SketchFgWedVW; } catch { return "FG_Wed_VW"; } // adjust literal if needed
        }
    }
}