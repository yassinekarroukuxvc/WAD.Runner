using System;
using System.Collections.Generic;
using WAD.Runner.DataManagement.Domain.Wedge;
using static WAD.Runner.DrawingAutomation.Profiles.ProfilePresets;

namespace WAD.Runner.DrawingAutomation.Profiles;

/// <summary>Central registry for all (Subclass × DrawingType) profiles.</summary>
public static class ProfileRegistry
{
    private static readonly Dictionary<DrawingProfileKey, DrawingProfile> _profiles =
        new(EqualityComparer<DrawingProfileKey>.Default)
        {
            // FG
            [new DrawingProfileKey(WedgeSubclass.FG, DrawingType.Production)] = FgProduction(),
            [new DrawingProfileKey(WedgeSubclass.FG, DrawingType.Customer)] = FgCustomer(),
            [new DrawingProfileKey(WedgeSubclass.FG, DrawingType.Overlay)] = FgOverlay(),

            // PGB
            [new DrawingProfileKey(WedgeSubclass.PGB, DrawingType.Production)] = PgbProduction(),
            [new DrawingProfileKey(WedgeSubclass.PGB, DrawingType.Overlay)] = PgbOverlay(),
        };

    public static DrawingProfile Get(WedgeSubclass subclass, DrawingType type)
    {
        var key = new DrawingProfileKey(subclass, type);
        if (_profiles.TryGetValue(key, out var profile)) return profile;
        throw new NotSupportedException($"No drawing profile registered for {subclass}/{type}.");
    }
}
