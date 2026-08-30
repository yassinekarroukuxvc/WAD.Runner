using System;
using System.Collections.Generic;
using System.Linq;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Equations;
using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.Rules._4516;
using WAD.Runner.ModelAutomation.Rules._45CK;
using WAD.Runner.ModelAutomation.Rules._1001;
using WAD.Runner.ModelAutomation.Rules.AB16;
using WAD.Runner.ModelAutomation.Rules.ABT;
using WAD.Runner.ModelAutomation.Rules.CKVD;
using WAD.Runner.ModelAutomation.Rules.COB;
using WAD.Runner.ModelAutomation.Rules.CobLike;
using WAD.Runner.ModelAutomation.Rules.FP;
using WAD.Runner.ModelAutomation.Rules.M;
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
    private static readonly IReadOnlyDictionary<WedgeType, WedgeAutomationProfile> Profiles =
        BuildProfiles();

    private static readonly WedgeAutomationProfile Default = new(
        default,
        "Default",
        new DefaultConfigurationRules(),
        new DefaultFeatureRules(),
        new StandardEquationPlanner(),
        new NoOpToleranceRules(),
        Array.Empty<string>());

    public static IReadOnlyCollection<WedgeType> SupportedWedgeTypes { get; } =
        Profiles.Keys
            .OrderBy(
                type => type.ToString(),
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static bool TryGet(
        WedgeType wedgeType,
        out WedgeAutomationProfile profile)
    {
        if (Profiles.TryGetValue(
                wedgeType,
                out var resolved))
        {
            profile =
                resolved;

            return true;
        }

        profile =
            null!;

        return false;
    }

    public static WedgeAutomationProfile For(
        WedgeType wedgeType)
    {
        if (TryGet(
                wedgeType,
                out var profile))
        {
            return profile;
        }

        Logger.Warn(
            $"[WedgeAutomationProfileRegistry] No profile registered for '{wedgeType}'. " +
            "Using the safe default profile with no feature/tolerance rules.");

        return Default;
    }

    private static IReadOnlyDictionary<WedgeType, WedgeAutomationProfile>
        BuildProfiles()
    {
        var profiles =
            new Dictionary<WedgeType, WedgeAutomationProfile>();

        Add(
            profiles,
            new WedgeAutomationProfile(
                WedgeType.CKVD,
                "CKVD",
                new CkvdConfigurationRules(),
                new CkvdFeatureRules(),
                new CkvdEquationPlanner(WedgeType.CKVD),
                new CkvdToleranceRules(),
                Array.Empty<string>()));

        Add(
            profiles,
            new WedgeAutomationProfile(
                WedgeType._4516,
                "_4516",
                new _4516ConfigurationRules(),
                new _4516FeatureRules(),
                new _4516EquationPlanner(WedgeType._4516),
                new _4516ToleranceRules(),
                Array.Empty<string>()));

        Add(
            profiles,
            new WedgeAutomationProfile(
                WedgeType.COB,
                "COB",
                new CobConfigurationRules(),
                new CobFeatureRules(),
                new CobEquationPlanner(),
                new CobToleranceRules(),
                Array.Empty<string>()));

        Add(
            profiles,
            new WedgeAutomationProfile(
                WedgeType.FP,
                "FP",
                new FpConfigurationRules(),
                new FpFeatureRules(),
                new FpEquationPlanner(),
                new FpToleranceRules(),
                Array.Empty<string>()));

        Add(
            profiles,
            new WedgeAutomationProfile(
                WedgeType.UTUS,
                "UTUS",
                new UtusConfigurationRules(),
                new UtusFeatureRules(),
                new UtusEquationPlanner(),
                new UtusToleranceRules(),
                Array.Empty<string>()));

        Add(
            profiles,
            new WedgeAutomationProfile(
                WedgeType.OSG7,
                "OSG7",
                new DefaultConfigurationRules(),
                new Osg7FeatureRules(),
                new Osg7EquationPlanner(),
                new Osg7ToleranceRules(),
                Array.Empty<string>()));

        Add(
            profiles,
            new WedgeAutomationProfile(
                WedgeType.ABT,
                "ABT",
                new AbtConfigurationRules(),
                new AbtFeatureRules(),
                new AbtEquationPlanner(WedgeType.ABT),
                new AbtToleranceRules(),
                Array.Empty<string>()));

        Add(
            profiles,
            new WedgeAutomationProfile(
                WedgeType.AB16,
                "AB16",
                new Ab16ConfigurationRules(),
                new Ab16FeatureRules(),
                new Ab16EquationPlanner(),
                new Ab16ToleranceRules(),
                Array.Empty<string>()));

        Add(
            profiles,
            new WedgeAutomationProfile(
                WedgeType._45CK,
                "_45CK",
                new _45CKConfigurationRules(),
                new _45CKFeatureRules(),
                new _45CKEquationPlanner(),
                new _45CKToleranceRules(),
                Array.Empty<string>()));

        Add(
            profiles,
            new WedgeAutomationProfile(
                WedgeType.M,
                "M",
                new MConfigurationRules(),
                new MFeatureRules(),
                new MEquationPlanner(),
                new MToleranceRules(),
                Array.Empty<string>()));

        Add(
            profiles,
            new WedgeAutomationProfile(
                WedgeType._1001,
                "_1001",
                new _1001ConfigurationRules(),
                new _1001FeatureRules(),
                new _1001EquationPlanner(),
                new _1001ToleranceRules(),
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
        if (!profiles.TryAdd(
                profile.WedgeType,
                profile))
        {
            throw new InvalidOperationException(
                $"Duplicate automation profile for wedge type '{profile.WedgeType}'.");
        }
    }
}