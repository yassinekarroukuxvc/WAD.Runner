using WAD.Runner.ModelAutomation.Rules.CobLike;

namespace WAD.Runner.ModelAutomation.Rules.UTUS;

/// <summary>
/// UTUS uses the shared COB-like configuration selection logic,
/// but for FG overlay it keeps std_cut even when both VR and VW exist.
/// </summary>
public sealed class UtusConfigurationRules : CobLikeConfigurationRulesBase
{
    public UtusConfigurationRules() : base("UtusConfigRules")
    {
    }

    protected override string ResolveOverlayFgConfig(CobLikeRuleFacts? facts)
    {
        bool hasVw = facts?.HasVw == true;
        bool hasVr = facts?.HasVr == true;

        if (!hasVw && !hasVr)
            return "std_cut";

        // UT/US special case:
        // In overlay, when both VW and VR are present, stay on std_cut.
        if (hasVw && hasVr)
            return "non_std_cut";

        return "Default";
    }
}