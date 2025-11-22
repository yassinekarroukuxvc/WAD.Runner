using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Profiles;

/// <summary>Dictionary key: Subclass × DrawingType.</summary>
public readonly record struct DrawingProfileKey(WedgeSubclass Subclass, DrawingType Type);
