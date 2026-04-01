using System;
using System.Collections.Generic;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DataManagement.Domain.Drawing;
using static WAD.Runner.DrawingAutomation.Profiles.ProfilePresets;

namespace WAD.Runner.DrawingAutomation.Profiles;

public static class ProfileRegistry
{
    private static readonly Dictionary<DrawingProfileKey, DrawingProfile> _ckvdProfiles =
        new(EqualityComparer<DrawingProfileKey>.Default)
        {
            [new DrawingProfileKey(WedgeSubclass.FG, DrawingType.Production)] = FgProduction(),
            [new DrawingProfileKey(WedgeSubclass.FG, DrawingType.Customer)] = FgCustomer(),
            [new DrawingProfileKey(WedgeSubclass.FG, DrawingType.Overlay)] = FgOverlay(),

            [new DrawingProfileKey(WedgeSubclass.PGB, DrawingType.Production)] = PgbProduction(),
            [new DrawingProfileKey(WedgeSubclass.PGB, DrawingType.Overlay)] = PgbOverlay(),
        };

    public static DrawingProfile Get(WedgeSubclass subclass, DrawingType type)
        => GetCkvd(subclass, type);

    public static DrawingProfile GetCkvd(WedgeSubclass subclass, DrawingType type)
    {
        var key = new DrawingProfileKey(subclass, type);
        if (_ckvdProfiles.TryGetValue(key, out var profile)) return profile;
        throw new NotSupportedException($"No CKVD drawing profile registered for {subclass}/{type}.");
    }

    public static DrawingProfile GetCob(WedgeSubclass subclass, DrawingType type)
        => (subclass, type) switch
        {
            (WedgeSubclass.FG, DrawingType.Production) => CobFgProduction(),
            (WedgeSubclass.FG, DrawingType.Customer) => CobFgCustomer(),
            (WedgeSubclass.FG, DrawingType.Overlay) => CobFgOverlay(),

            (WedgeSubclass.PGB, DrawingType.Production) => CobPgbProduction(),
            (WedgeSubclass.PGB, DrawingType.Overlay) => CobPgbOverlay(),

            _ => throw new NotSupportedException($"No COB drawing profile registered for {subclass}/{type}.")
        };
    public static DrawingProfile GetUtus(WedgeSubclass subclass, DrawingType type)
        => (subclass, type) switch
        {
            (WedgeSubclass.FG, DrawingType.Production) => UtusFgProduction(),
            (WedgeSubclass.FG, DrawingType.Customer) => UtusFgCustomer(),
            (WedgeSubclass.FG, DrawingType.Overlay) => UtusFgOverlay(),

            (WedgeSubclass.PGB, DrawingType.Production) => UtusPgbProduction(),
            (WedgeSubclass.PGB, DrawingType.Overlay) => UtusPgbOverlay(),

            _ => throw new NotSupportedException($"No UTUS drawing profile registered for {subclass}/{type}.")
        };

    public static DrawingProfile GetFp(WedgeSubclass subclass, DrawingType type)
        => (subclass, type) switch
        {
            (WedgeSubclass.FG, DrawingType.Production) => FpFgProduction(),
            (WedgeSubclass.FG, DrawingType.Customer) => FpFgCustomer(),
            (WedgeSubclass.FG, DrawingType.Overlay) => FpFgOverlay(),

            (WedgeSubclass.PGB, DrawingType.Production) => FpPgbProduction(),
            (WedgeSubclass.PGB, DrawingType.Overlay) => FpPgbOverlay(),

            _ => throw new NotSupportedException($"No FP drawing profile registered for {subclass}/{type}.")
        };

    public static DrawingProfile GetOsg7(WedgeSubclass subclass, DrawingType type)
        => (subclass, type) switch
        {
            (WedgeSubclass.FG, DrawingType.Production) => Osg7FgProduction(),
            (WedgeSubclass.FG, DrawingType.Customer) => Osg7FgCustomer(),
            (WedgeSubclass.FG, DrawingType.Overlay) => Osg7FgOverlay(),

            (WedgeSubclass.PGB, DrawingType.Production) => Osg7PgbProduction(),
            (WedgeSubclass.PGB, DrawingType.Overlay) => Osg7PgbOverlay(),

            _ => throw new NotSupportedException($"No OSG7 drawing profile registered for {subclass}/{type}.")
        };
}
