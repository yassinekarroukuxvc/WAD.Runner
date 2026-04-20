using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Rules.Common;

namespace WAD.Runner.DrawingAutomation.Rules.FP;

/// <summary>
/// FP drawing feature rules.
/// Add suppress/unsuppress logic here as you identify the SW feature names.
/// </summary>
public sealed class FpDrawingFeatureRules : IDrawingFeatureRuleSet
{
    public WedgeType AppliesTo => WedgeType.FP;

    public DrawingFeaturePlan Build(WedgeData wedge, DrawingType drawingType, WedgeSubclass subclass)
    {
        Logger.Info($"[FpDrawingFeatureRules] Build → subclass={subclass}, drawingType={drawingType}");
        // ── Add FP-specific rules here ──
        return DrawingFeaturePlan.Empty;
    }
}
