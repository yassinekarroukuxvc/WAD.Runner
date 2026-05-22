using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Profiles.Modules;

public sealed class UtusDrawingProfiles : IDrawingProfileModule
{
    public IEnumerable<DrawingProfileRegistration> CreateProfiles()
    {
        yield return Register(WedgeSubclass.FG, DrawingType.Production, ProfilePresets.UtusFgProduction());
        yield return Register(WedgeSubclass.FG, DrawingType.Customer, ProfilePresets.UtusFgCustomer());
        yield return Register(WedgeSubclass.FG, DrawingType.Overlay, ProfilePresets.UtusFgOverlay());
        yield return Register(WedgeSubclass.PGB, DrawingType.Production, ProfilePresets.UtusPgbProduction());
        yield return Register(WedgeSubclass.PGB, DrawingType.Overlay, ProfilePresets.UtusPgbOverlay());
    }

    private static DrawingProfileRegistration Register(WedgeSubclass subclass, DrawingType drawingType, DrawingProfile profile)
        => new(new RegisteredDrawingProfileKey(WedgeType.UTUS, subclass, drawingType), profile);
}
