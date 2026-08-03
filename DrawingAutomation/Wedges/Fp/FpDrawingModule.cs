using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Wedges.CobLike;

namespace WAD.Runner.DrawingAutomation.Wedges.Fp;

public sealed class FpDrawingModule : CobLikeDrawingModuleBase
{
    public FpDrawingModule()
        : base(WedgeType.FP, "FP")
    {
    }
}
