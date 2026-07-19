using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Core;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

public static class WedgePropertyReader
{
    public static string? GetPropLoose(WedgeData wedge, string key)
        => wedge is null
            ? null
            : new DrawingWedgeFacts(wedge).GetProperty(key);
}
