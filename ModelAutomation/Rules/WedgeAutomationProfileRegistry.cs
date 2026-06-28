using System;
using System.Collections.Generic;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Equations;
using WAD.Runner.ModelAutomation.Rules.CKVD;
using WAD.Runner.ModelAutomation.Rules.COB;
using WAD.Runner.ModelAutomation.Rules.FP;
using WAD.Runner.ModelAutomation.Rules.OSG7;
using WAD.Runner.ModelAutomation.Rules.UTUS;
using WAD.Runner.ModelAutomation.Tolerances;
using WAD.Runner.ModelAutomation.Rules.CobLike;

namespace WAD.Runner.ModelAutomation.Rules;

public static class WedgeAutomationProfileRegistry
{
    private static readonly IReadOnlyDictionary<WedgeType, WedgeAutomationProfile> Profiles =
        new Dictionary<WedgeType, WedgeAutomationProfile>
        {
            [WedgeType.CKVD] = new(
                WedgeType.CKVD,
                "CKVD",
                new CkvdConfigurationRules(),
                new CkvdFeatureRules(),
                new CkvdEquationPlanner(),
                new CkvdToleranceRules(),
                Array.Empty<string>()),

            [WedgeType.COB] = new(
                WedgeType.COB,
                "COB",
                new CobConfigurationRules(),
                new CobFeatureRules(),
                new CobLikeEquationPlanner(WedgeType.COB),
                new CobToleranceRules(),
                CobLikePostRebuildSuppressions.All),

            [WedgeType.FP] = new(
                WedgeType.FP,
                "FP",
                new FpConfigurationRules(),
                new FpFeatureRules(),
                new CobLikeEquationPlanner(WedgeType.FP),
                new FpToleranceRules(),
                CobLikePostRebuildSuppressions.All),

            [WedgeType.UTUS] = new(
                WedgeType.UTUS,
                "UTUS",
                new UtusConfigurationRules(),
                new UtusFeatureRules(),
                new CobLikeEquationPlanner(WedgeType.UTUS),
                new UtusToleranceRules(),
                CobLikePostRebuildSuppressions.All),

            [WedgeType.OSG7] = new(
                WedgeType.OSG7,
                "OSG7",
                new DefaultConfigurationRules(),
                new Osg7FeatureRules(),
                new Osg7EquationPlanner(),
                new Osg7ToleranceRules(),
                Array.Empty<string>())
        };

    private static readonly WedgeAutomationProfile Default = new(
        default,
        "Default",
        new DefaultConfigurationRules(),
        new DefaultFeatureRules(),
        new StandardEquationPlanner(),
        new NoOpToleranceRules(),
        Array.Empty<string>());

    public static WedgeAutomationProfile For(WedgeType wedgeType)
        => Profiles.TryGetValue(wedgeType, out var profile) ? profile : Default;
}
