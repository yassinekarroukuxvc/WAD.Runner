using System.Collections.Generic;

namespace WAD.Runner.DrawingAutomation.Profiles;

/// <summary>
/// Registers all drawing profiles owned by one wedge family.
/// Adding a new wedge type should mean adding one small module, not editing executors.
/// </summary>
public interface IDrawingProfileModule
{
    IEnumerable<DrawingProfileRegistration> CreateProfiles();
}
