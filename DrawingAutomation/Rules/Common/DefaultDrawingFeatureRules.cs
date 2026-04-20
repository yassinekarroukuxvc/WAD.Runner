using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.Application;

namespace WAD.Runner.DrawingAutomation.Rules.Common;

/// <summary>
/// Safe no-op fallback for unimplemented or future wedge types.
/// Returns an empty plan — no drawing features are touched.
/// </summary>
public sealed class DefaultDrawingFeatureRules : IDrawingFeatureRuleSet
{
    public WedgeType AppliesTo => WedgeType.OSG7; // placeholder; used as fallback for unknown types

    public DrawingFeaturePlan Build(WedgeData wedge, DrawingType drawingType, WedgeSubclass subclass)
    {
        Logger.Info("[DefaultDrawingFeatureRules] No drawing feature rules defined — returning empty plan.");
        return DrawingFeaturePlan.Empty;
    }
}