using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Profiles;

public readonly record struct DrawingProfileKey(
    WedgeType WedgeType,
    WedgeSubclass Subclass,
    DrawingType DrawingType);
