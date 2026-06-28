using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

public static class ShankTypeResolver
{
    public static ShankType Resolve(WedgeData wedge)
        => Parse(WedgePropertyReader.GetPropLoose(wedge, "Wed-Type"));

    public static ShankType Parse(string? wedType)
    {
        var s = (wedType ?? string.Empty).Trim().ToUpperInvariant();
        return s.Contains("180") || s.Contains("REV")
            ? ShankType.Deg180Rev
            : ShankType.Std;
    }
}
