using WAD.Runner.ModelAutomation.Rules.CobLike;

namespace WAD.Runner.ModelAutomation.Rules.COB;

/// <summary>
/// COB uses the shared COB-like feature planning with the full overlay
/// standard/non-standard cut behavior.
/// </summary>
public sealed class CobFeatureRules : CobLikeFeatureRulesBase
{
    protected override string LogPrefix => "CobFeatureRules";
    protected override string Pgb180RevConfigurationHint => "COB_180_DEG_REV_PGB";
    protected override bool SupportsOverlayNonStandardCutPlanning => true;
    protected override bool SuppressNonStandardCutFeaturesOutsideOverlay => true;
}
