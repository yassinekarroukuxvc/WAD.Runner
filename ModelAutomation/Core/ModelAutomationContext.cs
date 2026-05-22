using System;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Common;

namespace WAD.Runner.ModelAutomation.Core;

/// <summary>
/// Immutable context passed through planning layers.
/// It is the only object rule/planner code needs; SolidWorks adapters never live here.
/// </summary>
public sealed class ModelAutomationContext
{
    public ModelAutomationContext(ModelJobRequest job, PathPlanner.Plan paths)
    {
        Job = job ?? throw new ArgumentNullException(nameof(job));
        Paths = paths ?? throw new ArgumentNullException(nameof(paths));
        Facts = job.WedgeData is null ? null : new WedgeFacts(job.WedgeData);
    }

    public ModelJobRequest Job { get; }
    public PathPlanner.Plan Paths { get; }
    public WedgeFacts? Facts { get; }

    public WedgeData? Wedge => Job.WedgeData;
    public WedgeType WedgeType => Job.WedgeType;
    public WedgeSubclass Subclass => Job.Subclass;
    public DrawingType DrawingType => Job.DrawingType;

    public bool HasWedgeData => Wedge is not null;
}
