using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Rules.CKVD;
using WAD.Runner.ModelAutomation.Rules.COB;
using WAD.Runner.ModelAutomation.Rules.FP;
using WAD.Runner.ModelAutomation.Rules.OSG7;
using WAD.Runner.ModelAutomation.Rules.UTUS;
using WAD.Runner.ModelAutomation.Tolerances;

namespace WAD.Runner.ModelAutomation.Rules;

/// <summary>
/// Single source of truth for wedge-type to rule-set mapping.
/// Add a new wedge type here and the rest of ModelAutomation picks it up.
/// </summary>
public static class WedgeAutomationProfileRegistry
{
    private static readonly WedgeAutomationProfile DefaultProfile = new(
        ConfigurationRules: new DefaultConfigurationRules(),
        FeatureRules: new DefaultFeatureRules(),
        EquationNormalizer: new NoOpEquationInputNormalizer(),
        ToleranceRules: new NoOpToleranceRules());

    public static WedgeAutomationProfile For(WedgeType wedgeType)
        => wedgeType switch
        {
            WedgeType.CKVD => new WedgeAutomationProfile(
                ConfigurationRules: new CkvdConfigurationRules(),
                FeatureRules: new CkvdFeatureRules(),
                EquationNormalizer: new NoOpEquationInputNormalizer(),
                ToleranceRules: new CkvdToleranceRules()),

            WedgeType.COB => new WedgeAutomationProfile(
                ConfigurationRules: new CobConfigurationRules(),
                FeatureRules: new CobFeatureRules(),
                EquationNormalizer: new CobEquationInputNormalizer(),
                ToleranceRules: new CobToleranceRules()),

            WedgeType.UTUS => new WedgeAutomationProfile(
                ConfigurationRules: new UtusConfigurationRules(),
                FeatureRules: new UtusFeatureRules(),
                EquationNormalizer: new UtusEquationInputNormalizer(),
                ToleranceRules: new UtusToleranceRules()),

            WedgeType.FP => new WedgeAutomationProfile(
                ConfigurationRules: new FpConfigurationRules(),
                FeatureRules: new FpFeatureRules(),
                EquationNormalizer: new FpEquationInputNormalizer(),
                ToleranceRules: new FpToleranceRules()),

            WedgeType.OSG7 => new WedgeAutomationProfile(
                ConfigurationRules: new DefaultConfigurationRules(),
                FeatureRules: new DefaultFeatureRules(),
                EquationNormalizer: new Osg7EquationInputNormalizer(),
                ToleranceRules: new NoOpToleranceRules()),

            _ => DefaultProfile
        };
}
