// ModelAutomation/Rules/CkvdFeatureRules.cs
using System;
using System.Collections.Generic;

using WAD.Runner.Application; // Logger
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

using WAD.Runner.ModelAutomation.Execution; // IFeatureRuleSet + FeaturePlan
using WAD.Runner.ModelAutomation.Common;    // SwNames (if you already copied it), otherwise use literal name

namespace WAD.Runner.ModelAutomation.Rules
{
    /// <summary>
    /// CKVD feature toggle planning (NO SolidWorks calls, NO rebuild).
    /// Matches current behavior:
    /// - Production/Customer: engraving sketch ON (unsuppressed)
    /// - Overlay: no engraving change
    /// </summary>
    public sealed class CkvdFeatureRules : IFeatureRuleSet
    {
        public ModelRuleRunner.FeaturePlan Build(WedgeData wedge, DrawingType drawingType)
        {
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));

            var suppress = new List<string>();
            var unsuppress = new List<string>();

            Logger.Info($"[CkvdFeatureRules] Build → subclass={wedge.Subclass}, drawingType={drawingType}");

            // Your current CKVD rules apply engraving toggle only for non-overlay drawings.
            if (drawingType is DrawingType.Production or DrawingType.Customer)
            {
                // Engraving sketch must be ON
                var engravingSketchName = ResolveEngravingSketchName();
                unsuppress.Add(engravingSketchName);

                Logger.Info($"[CkvdFeatureRules] Non-overlay → unsuppress engraving sketch '{engravingSketchName}'");
            }
            else
            {
                Logger.Info("[CkvdFeatureRules] Overlay → no engraving toggle (keep template state).");
            }

            // NOTE:
            // CKVD TIP guard and VW/W toggle are currently implemented as logic that calls SuppressSketch directly.
            // We will move those into this file later if you want feature toggles to be fully centralized here.
            // For now, we keep CKVD minimal and faithful to what you already rely on.

            return new ModelRuleRunner.FeaturePlan(suppress, unsuppress);
        }

        private static string ResolveEngravingSketchName()
        {
            // If you already have SwNames.Engraving in ModelAutomation.Common, use it.
            // Otherwise fallback to the literal name used in your template.
            try
            {
                return SwNames.Engraving;
            }
            catch
            {
                return "Engraving"; // fallback literal (adjust to your real sketch/feature name if different)
            }
        }
    }
}
