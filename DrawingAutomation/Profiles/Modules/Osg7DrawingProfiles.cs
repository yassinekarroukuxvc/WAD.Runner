using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Profiles.Modules;

public sealed class Osg7DrawingProfiles : IDrawingProfileModule
{
    public IEnumerable<DrawingProfileRegistration> CreateProfiles()
    {
        yield return Register(WedgeSubclass.FG, DrawingType.Production, ProfilePresets.Osg7FgProduction());
        yield return Register(WedgeSubclass.FG, DrawingType.Customer, ProfilePresets.Osg7FgCustomer());
        yield return Register(WedgeSubclass.FG, DrawingType.Overlay, ProfilePresets.Osg7FgOverlay());
        yield return Register(WedgeSubclass.PGB, DrawingType.Production, ProfilePresets.Osg7PgbProduction());
        yield return Register(WedgeSubclass.PGB, DrawingType.Overlay, ProfilePresets.Osg7PgbOverlay());
    }

    private static DrawingProfileRegistration Register(WedgeSubclass subclass, DrawingType drawingType, DrawingProfile profile)
        => new(new RegisteredDrawingProfileKey(WedgeType.OSG7, subclass, drawingType), profile);
}
