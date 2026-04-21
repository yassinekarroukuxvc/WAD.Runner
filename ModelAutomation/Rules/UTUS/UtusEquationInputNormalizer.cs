using WAD.Runner.ModelAutomation.Rules.CobLike;

namespace WAD.Runner.ModelAutomation.Rules.UTUS;

/// <summary>
/// UTUS uses the shared COB-like equation normalization logic.
/// </summary>
public sealed class UtusEquationInputNormalizer : CobLikeEquationInputNormalizerBase
{
    public UtusEquationInputNormalizer() : base("UtusEquationInputNormalizer")
    {
    }
}
