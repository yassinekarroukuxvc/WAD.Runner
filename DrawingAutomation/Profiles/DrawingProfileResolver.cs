using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Profiles;

/// <summary>
/// Single place that maps (WedgeType, WedgeSubclass, DrawingType) → DrawingProfile.
/// </summary>
public static class DrawingProfileResolver
{
    public static DrawingProfile Resolve(WedgeType wedgeType, WedgeSubclass subclass, DrawingType drawingType)
        => ProfileRegistry.Get(wedgeType, subclass, drawingType);

    public static DrawingProfile Resolve(DrawingRun run, DrawingData drawingData)
        => Resolve(run.WedgeType, run.Wedge.Subclass, drawingData.DrawingType);
}
