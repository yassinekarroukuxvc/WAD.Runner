using System.Linq;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

public static class FootOptionResolver
{
    public static FootOption Resolve(WedgeData wedge)
    {
        var token = NormalizeToken(WedgePropertyReader.GetPropLoose(wedge, "Wed-Foot_Option"))
                 ?? NormalizeToken(WedgePropertyReader.GetPropLoose(wedge, "Foot_Option"))
                 ?? NormalizeToken(WedgePropertyReader.GetPropLoose(wedge, "FootOption"))
                 ?? NormalizeToken(WedgePropertyReader.GetPropLoose(wedge, "foot_option"));

        if (string.IsNullOrWhiteSpace(token))
            return FootOption.None;

        var baseFoot = token switch
        {
            "SW_C" => FootOption.C,
            "SW_G" => FootOption.G,
            "SW_VG" => FootOption.VG,
            "SW_CG" => FootOption.CG,
            "SW_CC" => FootOption.CC,
            _ => FootOption.None
        };

        if (baseFoot == FootOption.C &&
            DimensionFactResolver.IsDimensionPositive(wedge, "CBRA") &&
            DimensionFactResolver.IsDimensionPositive(wedge, "CBRL") &&
            DimensionFactResolver.IsDimensionPositive(wedge, "CBRD"))
        {
            return FootOption.C_WITH_CBR;
        }

        return baseFoot;
    }

    private static string? NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var s = value.Trim().ToUpperInvariant().Replace("-", "_").Replace(" ", "_");
        s = new string(s.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
