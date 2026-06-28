using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Profiles;

public static class ProfileRegistry
{
    private static readonly IReadOnlyDictionary<RegisteredDrawingProfileKey, DrawingProfile> Registry =
        DrawingProfileCatalog
            .CreateDefault()
            .GroupBy(x => x.Key)
            .ToDictionary(g => g.Key, g => g.Last().Profile);

    public static DrawingProfile Get(WedgeType wedgeType, WedgeSubclass subclass, DrawingType type)
    {
        var exact = new RegisteredDrawingProfileKey(wedgeType, subclass, type);
        if (Registry.TryGetValue(exact, out var profile))
            return profile;


        if (type == DrawingType.Customer)
        {
            var productionFallback = new RegisteredDrawingProfileKey(wedgeType, subclass, DrawingType.Production);
            if (Registry.TryGetValue(productionFallback, out var productionProfile))
                return productionProfile;
        }

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
