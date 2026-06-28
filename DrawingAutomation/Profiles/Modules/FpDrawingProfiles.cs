using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Profiles.Modules;

public sealed class FpDrawingProfiles : IDrawingProfileModule
{
    public IEnumerable<DrawingProfileRegistration> CreateProfiles()
    {
        yield return Register(WedgeSubclass.FG, DrawingType.Production, ProfilePresets.FpFgProduction());
        yield return Register(WedgeSubclass.FG, DrawingType.Customer, ProfilePresets.FpFgCustomer());
        yield return Register(WedgeSubclass.FG, DrawingType.Overlay, ProfilePresets.FpFgOverlay());
        yield return Register(WedgeSubclass.PGB, DrawingType.Production, ProfilePresets.FpPgbProduction());
        yield return Register(WedgeSubclass.PGB, DrawingType.Overlay, ProfilePresets.FpPgbOverlay());
    }

    private static DrawingProfileRegistration Register(WedgeSubclass subclass, DrawingType drawingType, DrawingProfile profile)
        => new(new RegisteredDrawingProfileKey(WedgeType.FP, subclass, drawingType), profile);
}
