using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Rules.Common;

/// <summary>
/// Per-wedge-type rules that decide which drawing features
/// (sketches, detail circles, section lines) to suppress or unsuppress.
///
/// This is the drawing-side equivalent of IFeatureRuleSet from ModelAutomation.
/// Pure planning only — no SolidWorks calls, no side effects.
/// </summary>
public interface IDrawingFeatureRuleSet
{
    /// <summary>The wedge type this rule set applies to.</summary>
    WedgeType AppliesTo { get; }

    /// <summary>
    /// Returns the names of drawing features to suppress and unsuppress
    /// for the given wedge data and drawing context.
    ///
    /// Names must match the exact SolidWorks FeatureManager name of the
    /// drawing-level feature (sketch, detail circle, section line, etc.).
    ///
    /// Implementations must be pure (no COM calls, no side effects).
    /// </summary>
    DrawingFeaturePlan Build(WedgeData wedge, DrawingType drawingType, WedgeSubclass subclass);
}

/// <summary>
/// The planned suppress/unsuppress sets for a single drawing job.
/// </summary>
public sealed record DrawingFeaturePlan(
    IReadOnlyList<string> Suppress,
    IReadOnlyList<string> Unsuppress)
{
    public static DrawingFeaturePlan Empty { get; } =
        new(System.Array.Empty<string>(), System.Array.Empty<string>());
}