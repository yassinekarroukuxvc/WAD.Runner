using WAD.Runner.ModelAutomation.Rules.CobLike;

namespace WAD.Runner.ModelAutomation.Rules.FP;

/// <summary>
/// FP uses the shared COB-like equation normalization logic.
/// </summary>
public sealed class FpEquationInputNormalizer : CobLikeEquationInputNormalizerBase
{
    public FpEquationInputNormalizer() : base("FpEquationInputNormalizer")
    {
    }
}
