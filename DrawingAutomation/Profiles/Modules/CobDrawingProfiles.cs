using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Profiles.Modules;

public sealed class CobDrawingProfiles : IDrawingProfileModule
{
    public IEnumerable<DrawingProfileRegistration> CreateProfiles()
    {
        yield return Register(WedgeSubclass.FG, DrawingType.Production, ProfilePresets.CobFgProduction());
        yield return Register(WedgeSubclass.FG, DrawingType.Customer, ProfilePresets.CobFgCustomer());
        yield return Register(WedgeSubclass.FG, DrawingType.Overlay, ProfilePresets.CobFgOverlay());
        yield return Register(WedgeSubclass.PGB, DrawingType.Production, ProfilePresets.CobPgbProduction());
        yield return Register(WedgeSubclass.PGB, DrawingType.Overlay, ProfilePresets.CobPgbOverlay());
    }

    private static DrawingProfileRegistration Register(WedgeSubclass subclass, DrawingType drawingType, DrawingProfile profile)
        => new(new RegisteredDrawingProfileKey(WedgeType.COB, subclass, drawingType), profile);
}
