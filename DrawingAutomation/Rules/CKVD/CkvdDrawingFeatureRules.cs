using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Rules.Common;

namespace WAD.Runner.DrawingAutomation.Rules.CKVD
{
    class CkvdDrawingFeatureRules : IDrawingFeatureRuleSet
    {
        public WedgeType AppliesTo => WedgeType.CKVD;

        public DrawingFeaturePlan Build(WedgeData wedge, DrawingType drawingType, WedgeSubclass subclass)
        {
            Logger.Info($"[CkvdDrawingFeatureRules] Build → subclass={subclass}, drawingType={drawingType}");
            // ── Add CKVD-specific rules here ──
            return DrawingFeaturePlan.Empty;
        }
    }
}
