// ModelAutomation/Rules/DefaultFeatureRules.cs
using System;
using System.Collections.Generic;

using WAD.Runner.Application; // Logger
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

using WAD.Runner.ModelAutomation.Execution; // IFeatureRuleSet + FeaturePlan

namespace WAD.Runner.ModelAutomation.Rules
{
    /// <summary>
    /// Fallback feature rules for unknown wedge types.
    /// Policy (matches your old behavior):
    /// - Production/Customer: engraving ON (unsuppressed)
    /// - Overlay: do nothing
    /// </summary>
    public sealed class DefaultFeatureRules : IFeatureRuleSet
    {
        public ModelRuleRunner.FeaturePlan Build(WedgeData wedge, DrawingType drawingType, WedgeSubclass subclass)
        {
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));

            var suppress = new List<string>();
            var unsuppress = new List<string>();

            Logger.Info($"[DefaultFeatureRules] Build → subclass={wedge.Subclass}, drawingType={drawingType}");

            if (drawingType is DrawingType.Production or DrawingType.Customer)
            {
                // Prefer SwNames if you copied it into ModelAutomation.Common
                var engravingName = TryGetEngravingName();

                unsuppress.Add(engravingName);
                Logger.Info($"[DefaultFeatureRules] Non-overlay → unsuppress engraving sketch '{engravingName}'");
            }
            else
            {
                Logger.Info("[DefaultFeatureRules] Overlay → no toggles (keep template state).");
            }

            return new ModelRuleRunner.FeaturePlan(suppress, unsuppress);
        }

        private static string TryGetEngravingName()
        {
            try
            {
                return WAD.Runner.ModelAutomation.Common.SwNames.Engraving;
            }
            catch
            {
                // Fallback literal name (change if your real sketch/feature name differs)
                return "Engraving";
            }
        }
    }
}
