using System;
using System.Collections.Generic;

using WAD.Runner.DrawingAutomation.Profiles;

namespace WAD.Runner.DrawingAutomation.Core;

/// <summary>
/// Converts logical drawing view names used by rules (Front, Side, Top, Detail, Section)
/// into actual SolidWorks template view names.
/// </summary>
public static class DrawingViewNameMap
{
    public static IDictionary<string, string> FromProfile(DrawingProfile profile)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));

        var v = profile.Views;
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Front"] = v.Front,
            ["Side"] = v.Side,
            ["Top"] = v.Top,
            ["Detail"] = v.Detail,
            ["Section"] = v.Section
        };
    }
}
