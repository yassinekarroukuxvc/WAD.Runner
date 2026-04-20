using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Rules.COB;
using WAD.Runner.DrawingAutomation.Rules.FP;
using WAD.Runner.DrawingAutomation.Rules.CKVD;
using WAD.Runner.DrawingAutomation.Rules.UTUS;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Rules.Common;

/// <summary>
/// Builds and applies drawing feature toggle plans.
///
/// Mirrors the ModelAutomation.ModelRuleRunner pattern:
///   1. Map WedgeType → IDrawingFeatureRuleSet
///   2. Call Build() to get suppress/unsuppress name sets (pure logic)
///   3. Apply via DrawingFeatureToggleBatch (SW calls)
///
/// To add a new wedge type:
///   1. Create Rules/{NewType}/{NewType}DrawingFeatureRules.cs
///   2. Add one line in the switch below.
///   Nothing else changes.
/// </summary>
public static class DrawingFeatureRuleRunner
{
    // ─────────────────────────────────────────────────────────────────────
    // Planning (pure logic, no SW calls)
    // ─────────────────────────────────────────────────────────────────────

    public static DrawingFeaturePlan BuildPlan(
        WedgeType wedgeType,
        WedgeData wedge,
        DrawingType drawingType,
        WedgeSubclass subclass)
    {
        if (wedge is null) throw new ArgumentNullException(nameof(wedge));

        Logger.Info($"[DrawingFeatureRuleRunner] BuildPlan → wedgeType={wedgeType}, subclass={subclass}, drawingType={drawingType}");

        IDrawingFeatureRuleSet rules = wedgeType switch
        {
            WedgeType.CKVD => new CkvdDrawingFeatureRules(),
            WedgeType.COB => new CobDrawingFeatureRules(),
            WedgeType.UTUS => new UtusDrawingFeatureRules(),
            WedgeType.FP => new FpDrawingFeatureRules(),
            _ => new DefaultDrawingFeatureRules()
        };

        var raw = rules.Build(wedge, drawingType, subclass);

        // Normalize: trim, deduplicate, resolve conflicts (unsuppress wins)
        var unsupSet = Normalize(raw.Unsuppress);
        var supSet = Normalize(raw.Suppress);
        supSet.ExceptWith(unsupSet); // unsuppress wins

        var plan = new DrawingFeaturePlan(
            Suppress: supSet.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
            Unsuppress: unsupSet.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList());

        Logger.Info($"[DrawingFeatureRuleRunner] Plan → suppress={plan.Suppress.Count}, unsuppress={plan.Unsuppress.Count}");

        return plan;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Apply (SW calls via DrawingFeatureToggleBatch)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the plan and applies it to the drawing in one call.
    /// Caller must call ds.Rebuild() after this returns.
    /// </summary>
    public static DrawingFeatureToggleBatch.ToggleResult BuildAndApply(
        DrawingService ds,
        WedgeType wedgeType,
        WedgeData wedge,
        DrawingType drawingType,
        WedgeSubclass subclass)
    {
        var plan = BuildPlan(wedgeType, wedge, drawingType, subclass);
        var batch = DrawingFeatureToggleBatch.Build(ds);
        return batch.Apply(plan.Suppress, plan.Unsuppress);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private static HashSet<string> Normalize(IEnumerable<string> names)
        => new HashSet<string>(
            names.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()),
            StringComparer.OrdinalIgnoreCase);
}
