using System.Collections.Generic;

namespace WAD.Runner.DrawingAutomation.Profiles;


public interface IDrawingProfileModule
{
    IEnumerable<DrawingProfileRegistration> CreateProfiles();
}
