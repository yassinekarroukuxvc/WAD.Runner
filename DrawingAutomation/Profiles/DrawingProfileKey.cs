using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Profiles;


public readonly record struct DrawingProfileKey(WedgeSubclass Subclass, DrawingType Type);
