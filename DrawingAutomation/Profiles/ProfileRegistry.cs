using System;
using System.Collections.Generic;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using static WAD.Runner.DrawingAutomation.Profiles.ProfilePresets;

namespace WAD.Runner.DrawingAutomation.Profiles;

public static class ProfileRegistry
{
    private static readonly IReadOnlyDictionary<RegisteredDrawingProfileKey, DrawingProfile> Registry =
        new Dictionary<RegisteredDrawingProfileKey, DrawingProfile>
        {
            [new(WedgeType.CKVD, WedgeSubclass.FG, DrawingType.Production)] = FgProduction(),
            [new(WedgeType.CKVD, WedgeSubclass.FG, DrawingType.Customer)] = FgCustomer(),
            [new(WedgeType.CKVD, WedgeSubclass.FG, DrawingType.Overlay)] = FgOverlay(),
            [new(WedgeType.CKVD, WedgeSubclass.PGB, DrawingType.Production)] = PgbProduction(),
            [new(WedgeType.CKVD, WedgeSubclass.PGB, DrawingType.Overlay)] = PgbOverlay(),

            [new(WedgeType.COB, WedgeSubclass.FG, DrawingType.Production)] = CobFgProduction(),
            [new(WedgeType.COB, WedgeSubclass.FG, DrawingType.Customer)] = CobFgCustomer(),
            [new(WedgeType.COB, WedgeSubclass.FG, DrawingType.Overlay)] = CobFgOverlay(),
            [new(WedgeType.COB, WedgeSubclass.PGB, DrawingType.Production)] = CobPgbProduction(),
            [new(WedgeType.COB, WedgeSubclass.PGB, DrawingType.Overlay)] = CobPgbOverlay(),

            [new(WedgeType.UTUS, WedgeSubclass.FG, DrawingType.Production)] = UtusFgProduction(),
            [new(WedgeType.UTUS, WedgeSubclass.FG, DrawingType.Customer)] = UtusFgCustomer(),
            [new(WedgeType.UTUS, WedgeSubclass.FG, DrawingType.Overlay)] = UtusFgOverlay(),
            [new(WedgeType.UTUS, WedgeSubclass.PGB, DrawingType.Production)] = UtusPgbProduction(),
            [new(WedgeType.UTUS, WedgeSubclass.PGB, DrawingType.Overlay)] = UtusPgbOverlay(),

            [new(WedgeType.FP, WedgeSubclass.FG, DrawingType.Production)] = FpFgProduction(),
            [new(WedgeType.FP, WedgeSubclass.FG, DrawingType.Customer)] = FpFgCustomer(),
            [new(WedgeType.FP, WedgeSubclass.FG, DrawingType.Overlay)] = FpFgOverlay(),
            [new(WedgeType.FP, WedgeSubclass.PGB, DrawingType.Production)] = FpPgbProduction(),
            [new(WedgeType.FP, WedgeSubclass.PGB, DrawingType.Overlay)] = FpPgbOverlay(),

            [new(WedgeType.OSG7, WedgeSubclass.FG, DrawingType.Production)] = Osg7FgProduction(),
            [new(WedgeType.OSG7, WedgeSubclass.FG, DrawingType.Customer)] = Osg7FgCustomer(),
            [new(WedgeType.OSG7, WedgeSubclass.FG, DrawingType.Overlay)] = Osg7FgOverlay(),
            [new(WedgeType.OSG7, WedgeSubclass.PGB, DrawingType.Production)] = Osg7PgbProduction(),
            [new(WedgeType.OSG7, WedgeSubclass.PGB, DrawingType.Overlay)] = Osg7PgbOverlay(),
        };

    public static DrawingProfile Get(WedgeType wedgeType, WedgeSubclass subclass, DrawingType type)
    {
        var key = new RegisteredDrawingProfileKey(wedgeType, subclass, type);
        if (Registry.TryGetValue(key, out var profile))
            return profile;

        throw new NotSupportedException($"No drawing profile registered for {wedgeType}/{subclass}/{type}.");
    }

    public static DrawingProfile Get(WedgeSubclass subclass, DrawingType type)
        => Get(WedgeType.CKVD, subclass, type);

    public static DrawingProfile GetCkvd(WedgeSubclass subclass, DrawingType type)
        => Get(WedgeType.CKVD, subclass, type);

    public static DrawingProfile GetCob(WedgeSubclass subclass, DrawingType type)
        => Get(WedgeType.COB, subclass, type);

    public static DrawingProfile GetUtus(WedgeSubclass subclass, DrawingType type)
        => Get(WedgeType.UTUS, subclass, type);

    public static DrawingProfile GetFp(WedgeSubclass subclass, DrawingType type)
        => Get(WedgeType.FP, subclass, type);

    public static DrawingProfile GetOsg7(WedgeSubclass subclass, DrawingType type)
        => Get(WedgeType.OSG7, subclass, type);
}
