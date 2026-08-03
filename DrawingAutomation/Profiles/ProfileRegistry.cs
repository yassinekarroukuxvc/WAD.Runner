using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Wedges;

namespace WAD.Runner.DrawingAutomation.Profiles;

public static class ProfileRegistry
{
    private static readonly IReadOnlyDictionary<DrawingProfileKey, DrawingProfile> Profiles =
        DrawingWedgeModuleRegistry.GetProfiles().ToDictionary(profile => profile.Key);

    public static DrawingProfile Get(
        WedgeType wedgeType,
        WedgeSubclass subclass,
        DrawingType drawingType)
    {
        var key = new DrawingProfileKey(wedgeType, subclass, drawingType);
        if (Profiles.TryGetValue(key, out var profile))
            return profile;

        if (drawingType == DrawingType.Customer)
        {
            var productionKey = new DrawingProfileKey(
                wedgeType,
                subclass,
                DrawingType.Production);

            if (Profiles.TryGetValue(productionKey, out var productionProfile))
                return productionProfile;
        }

        throw new NotSupportedException(
            $"No drawing profile is registered for {wedgeType}/{subclass}/{drawingType}. " +
            "Add it to the matching IDrawingWedgeModule.");
    }
}
