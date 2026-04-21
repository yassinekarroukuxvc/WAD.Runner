using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Profiles;

/// <summary>
/// Registry key: WedgeType × Subclass × DrawingType.
/// </summary>
public readonly record struct RegisteredDrawingProfileKey(
    WedgeType WedgeType,
    WedgeSubclass Subclass,
    DrawingType Type);
