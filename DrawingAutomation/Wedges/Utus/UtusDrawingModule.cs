using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Wedges.CobLike;

namespace WAD.Runner.DrawingAutomation.Wedges.Utus;

public sealed class UtusDrawingModule : CobLikeDrawingModuleBase
{
    public UtusDrawingModule()
        : base(WedgeType.UTUS, "UTUS")
    {
    }
}
