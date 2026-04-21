using WAD.Runner.ModelAutomation.Rules.CobLike;

namespace WAD.Runner.ModelAutomation.Rules.COB;

/// <summary>
/// COB uses the shared COB-like equation normalization logic.
/// </summary>
public sealed class CobEquationInputNormalizer : CobLikeEquationInputNormalizerBase
{
    public CobEquationInputNormalizer() : base("CobEquationInputNormalizer")
    {
    }
}
