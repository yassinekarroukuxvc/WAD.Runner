using System;
using System.Collections.Generic;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DataManagement.Domain.Drawing;
using static WAD.Runner.DrawingAutomation.Profiles.ProfilePresets;

namespace WAD.Runner.DrawingAutomation.Profiles;

/// <summary>
/// Central registry for (Subclass × DrawingType) drawing profiles.
///
/// IMPORTANT:
/// - The default Get(...) method returns **CKVD** profiles (backwards compatible).
/// - COB profiles are exposed via GetCob(...), using the Cob* presets.
/// </summary>
public static class ProfileRegistry
{
    // CKVD registry (what your code was already using)
    private static readonly Dictionary<DrawingProfileKey, DrawingProfile> _ckvdProfiles =
        new(EqualityComparer<DrawingProfileKey>.Default)
        {
            // FG (CKVD)
            [new DrawingProfileKey(WedgeSubclass.FG, DrawingType.Production)] = FgProduction(),
            [new DrawingProfileKey(WedgeSubclass.FG, DrawingType.Customer)] = FgCustomer(),
            [new DrawingProfileKey(WedgeSubclass.FG, DrawingType.Overlay)] = FgOverlay(),

            // PGB (CKVD)
            [new DrawingProfileKey(WedgeSubclass.PGB, DrawingType.Production)] = PgbProduction(),
            [new DrawingProfileKey(WedgeSubclass.PGB, DrawingType.Overlay)] = PgbOverlay(),
        };

    /// <summary>
    /// Backwards-compatible accessor: returns **CKVD** profiles.
    /// Existing CKVD executors should keep using this.
    /// </summary>
    public static DrawingProfile Get(WedgeSubclass subclass, DrawingType type)
        => GetCkvd(subclass, type);

    /// <summary>
    /// Explicit CKVD accessor (same as Get, but named).
    /// </summary>
    public static DrawingProfile GetCkvd(WedgeSubclass subclass, DrawingType type)
    {
        var key = new DrawingProfileKey(subclass, type);
        if (_ckvdProfiles.TryGetValue(key, out var profile)) return profile;
        throw new NotSupportedException($"No CKVD drawing profile registered for {subclass}/{type}.");
    }

    /// <summary>
    /// COB profile accessor.
    /// Uses the Cob* presets defined in ProfilePresets.
    /// Call this from COB drawing executors.
    /// </summary>
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
}
