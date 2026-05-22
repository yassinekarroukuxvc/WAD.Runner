using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Profiles.Modules;

public sealed class CkvdDrawingProfiles : IDrawingProfileModule
{
    public IEnumerable<DrawingProfileRegistration> CreateProfiles()
    {
        yield return Register(WedgeSubclass.FG, DrawingType.Production, ProfilePresets.CkvdFgProduction());
        yield return Register(WedgeSubclass.FG, DrawingType.Customer, ProfilePresets.CkvdFgCustomer());
        yield return Register(WedgeSubclass.FG, DrawingType.Overlay, ProfilePresets.CkvdFgOverlay());
        yield return Register(WedgeSubclass.PGB, DrawingType.Production, ProfilePresets.CkvdPgbProduction());
        yield return Register(WedgeSubclass.PGB, DrawingType.Overlay, ProfilePresets.CkvdPgbOverlay());
    }

    private static DrawingProfileRegistration Register(WedgeSubclass subclass, DrawingType drawingType, DrawingProfile profile)
        => new(new RegisteredDrawingProfileKey(WedgeType.CKVD, subclass, drawingType), profile);
}
