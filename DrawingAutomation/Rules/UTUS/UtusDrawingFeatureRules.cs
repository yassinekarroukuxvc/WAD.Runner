using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Rules.Common;
using WAD.Runner.Application;

namespace WAD.Runner.DrawingAutomation.Rules.UTUS;

/// <summary>
/// UTUS drawing feature rules.
/// Add suppress/unsuppress logic here as you identify the SW feature names.
/// </summary>
public sealed class UtusDrawingFeatureRules : IDrawingFeatureRuleSet
{
    public WedgeType AppliesTo => WedgeType.UTUS;

    public DrawingFeaturePlan Build(WedgeData wedge, DrawingType drawingType, WedgeSubclass subclass)
    {
        Logger.Info($"[UtusDrawingFeatureRules] Build → subclass={subclass}, drawingType={drawingType}");
        // ── Add UTUS-specific rules here ──
        return DrawingFeaturePlan.Empty;
    }
}
