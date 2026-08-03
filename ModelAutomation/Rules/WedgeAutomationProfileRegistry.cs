using System;
using System.Collections.Generic;
using System.Linq;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Equations;
using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.Rules.CKVD;
using WAD.Runner.ModelAutomation.Rules.COB;
using WAD.Runner.ModelAutomation.Rules.CobLike;
using WAD.Runner.ModelAutomation.Rules.FP;
using WAD.Runner.ModelAutomation.Rules.OSG7;
using WAD.Runner.ModelAutomation.Rules.UTUS;
using WAD.Runner.ModelAutomation.Tolerances;

namespace WAD.Runner.ModelAutomation.Rules;

/// <summary>
/// The single registration point for wedge automation behavior.
/// Adding a wedge type should require one profile entry plus its rule implementations.
/// </summary>
public static class WedgeAutomationProfileRegistry
{
    private static readonly IReadOnlyDictionary<WedgeType, WedgeAutomationProfile> Profiles = BuildProfiles();

    private static readonly WedgeAutomationProfile Default = new(
        default,
        "Default",
        new DefaultConfigurationRules(),
        new DefaultFeatureRules(),
        new StandardEquationPlanner(),
        new NoOpToleranceRules(),
        Array.Empty<string>());

    public static IReadOnlyCollection<WedgeType> SupportedWedgeTypes { get; } =
        Profiles.Keys.OrderBy(type => type.ToString(), StringComparer.OrdinalIgnoreCase).ToArray();

    public static bool TryGet(WedgeType wedgeType, out WedgeAutomationProfile profile)
    {
        if (Profiles.TryGetValue(wedgeType, out var resolved))
        {
            profile = resolved;
            return true;
        }

        profile = null!;
        return false;
    }

    public static WedgeAutomationProfile For(WedgeType wedgeType)
    {
        if (TryGet(wedgeType, out var profile))
            return profile;

        Logger.Warn(
            $"[WedgeAutomationProfileRegistry] No profile registered for '{wedgeType}'. " +
            "Using the safe default profile with no feature/tolerance rules.");
        return Default;
    }

    private static IReadOnlyDictionary<WedgeType, WedgeAutomationProfile> BuildProfiles()
    {
        var profiles = new Dictionary<WedgeType, WedgeAutomationProfile>();

        Add(profiles, new WedgeAutomationProfile(
            WedgeType.CKVD,
            "CKVD",
            new CkvdConfigurationRules(),
            new CkvdFeatureRules(),
            new CkvdEquationPlanner(WedgeType.CKVD),
            new CkvdToleranceRules(),
            Array.Empty<string>()));

        Add(profiles, CreateCobLikeProfile(
            WedgeType.COB,
            "COB",
            new CobConfigurationRules(),
            new CobFeatureRules(),
            new CobToleranceRules()));

        Add(profiles, CreateCobLikeProfile(
            WedgeType.FP,
            "FP",
            new FpConfigurationRules(),
            new FpFeatureRules(),
            new FpToleranceRules()));

        Add(profiles, CreateCobLikeProfile(
            WedgeType.UTUS,
            "UTUS",
            new UtusConfigurationRules(),
            new UtusFeatureRules(),
            new UtusToleranceRules()));

        Add(profiles, new WedgeAutomationProfile(
            WedgeType.OSG7,
            "OSG7",
            new DefaultConfigurationRules(),
            new Osg7FeatureRules(),
            new Osg7EquationPlanner(),
            new Osg7ToleranceRules(),
            Array.Empty<string>()));

        return profiles;
    }

    private static WedgeAutomationProfile CreateCobLikeProfile(
        WedgeType wedgeType,
        string name,
        IModelConfigurationRules configurationRules,
        IFeatureRuleSet featureRules,
        IToleranceRuleSet toleranceRules)
        => new(
            wedgeType,
            name,
            configurationRules,
            featureRules,
            new CobLikeEquationPlanner(wedgeType),
            toleranceRules,
            CobLikePostRebuildSuppressions.All);

    private static void Add(
        IDictionary<WedgeType, WedgeAutomationProfile> profiles,
        WedgeAutomationProfile profile)
    {
        if (!profiles.TryAdd(profile.WedgeType, profile))
            throw new InvalidOperationException($"Duplicate automation profile for wedge type '{profile.WedgeType}'.");
    }
}
