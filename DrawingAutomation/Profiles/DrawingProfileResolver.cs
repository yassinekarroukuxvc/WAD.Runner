// DrawingAutomation/Profiles/DrawingProfileResolver.cs
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Profiles;

/// <summary>
/// Single place that maps (WedgeType, WedgeSubclass, DrawingType) → DrawingProfile.
/// </summary>
public static class DrawingProfileResolver
{
    public static DrawingProfile Resolve(WedgeType wedgeType, WedgeSubclass subclass, DrawingType drawingType)
        => wedgeType switch
        {
            WedgeType.CKVD => ProfileRegistry.GetCkvd(subclass, drawingType),
            WedgeType.COB => ProfileRegistry.GetCob(subclass, drawingType),
            WedgeType.UTUS => ProfileRegistry.GetUtus(subclass, drawingType),
            WedgeType.FP => ProfileRegistry.GetFp(subclass, drawingType),
            WedgeType.OSG7 => ProfileRegistry.GetOsg7(subclass, drawingType),
            _ => ProfileRegistry.GetCkvd(subclass, drawingType)
        };

    /// <summary>Convenience overload — reads wedge type and subclass from the run.</summary>
    public static DrawingProfile Resolve(DrawingRun run, DrawingData drawingData)
        => Resolve(run.WedgeType, run.Wedge.Subclass, drawingData.DrawingType);
}
