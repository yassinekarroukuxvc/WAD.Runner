using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Wedges.CobLike;

namespace WAD.Runner.DrawingAutomation.Wedges.Cob;

public sealed class CobDrawingModule : CobLikeDrawingModuleBase
{
    public CobDrawingModule()
        : base(WedgeType.COB, "COB")
    {
    }
}
