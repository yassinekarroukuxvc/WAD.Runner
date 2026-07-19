using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Profiles;

public static class ProfileRegistry
{
    private static readonly IReadOnlyDictionary<RegisteredDrawingProfileKey, DrawingProfile> Registry =
        BuildRegistry();

    public static DrawingProfile Get(
        WedgeType wedgeType,
        WedgeSubclass subclass,
        DrawingType type)
    {
        var exact = new RegisteredDrawingProfileKey(wedgeType, subclass, type);
        if (Registry.TryGetValue(exact, out var profile))
            return profile;

        if (type == DrawingType.Customer)
        {
            var productionFallback = new RegisteredDrawingProfileKey(
                wedgeType,
                subclass,
                DrawingType.Production);

            if (Registry.TryGetValue(productionFallback, out var productionProfile))
                return productionProfile;
        }

        throw new NotSupportedException(
            $"No drawing profile is registered for {wedgeType}/{subclass}/{type}. " +
            "Add the profile to the matching IDrawingProfileModule.");
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

    private static IReadOnlyDictionary<RegisteredDrawingProfileKey, DrawingProfile> BuildRegistry()
    {
        var registry = new Dictionary<RegisteredDrawingProfileKey, DrawingProfile>();

        foreach (var registration in DrawingProfileCatalog.CreateDefault())
        {
            if (registration.Profile is null)
                throw new InvalidOperationException(
                    $"Drawing profile '{registration.Key}' is null.");

            ValidateProfile(registration.Profile, registration.Key);

            var expectedProfileKey = new DrawingProfileKey(
                registration.Key.Subclass,
                registration.Key.Type);

            if (registration.Profile.Key != expectedProfileKey)
            {
                throw new InvalidOperationException(
                    $"Drawing profile key mismatch for '{registration.Key}'. " +
                    $"The profile contains '{registration.Profile.Key}'.");
            }

            if (!registry.TryAdd(registration.Key, registration.Profile))
            {
                throw new InvalidOperationException(
                    $"Duplicate drawing profile key: {registration.Key}. " +
                    "Each (WedgeType, Subclass, DrawingType) must be registered exactly once.");
            }
        }

        return registry;
    }

    private static void ValidateProfile(
        DrawingProfile profile,
        RegisteredDrawingProfileKey registrationKey)
    {
        if (string.IsNullOrWhiteSpace(profile.ProfileName))
            throw new InvalidOperationException($"Profile name is missing for '{registrationKey}'.");

        if (profile.SheetSelector is null ||
            profile.UseBreaklinesForView is null ||
            profile.ScaleForView is null)
        {
            throw new InvalidOperationException($"Profile delegates are incomplete for '{registrationKey}'.");
        }

        if (profile.ViewsOrder is null || profile.ViewsOrder.Count == 0)
            throw new InvalidOperationException($"View order is empty for '{registrationKey}'.");

        if (profile.Scale is null)
            throw new InvalidOperationException($"Scale policy is missing for '{registrationKey}'.");

        if (profile.Scale.Step <= 0.0 ||
            profile.Scale.MinScale <= 0.0 ||
            profile.Scale.MaxScale < profile.Scale.MinScale ||
            profile.Scale.FillRatioHeight <= 0.0)
        {
            throw new InvalidOperationException($"Scale policy is invalid for '{registrationKey}'.");
        }
    }

}
