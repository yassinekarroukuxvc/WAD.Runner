using System;
using System.Linq;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Core;
using WAD.Runner.ModelAutomation.Equations;

namespace WAD.Runner.ModelAutomation.Rules.CobLike;

public sealed class CobLikeFacts
{
    public CobLikeFacts(WedgeData wedge)
    {
        Facts = new WedgeFacts(wedge);
    }

    public WedgeFacts Facts { get; }
    public WedgeData Wedge => Facts.Wedge;

    public bool HasVw => Facts.HasPositive("VW");
    public bool HasVr => Facts.HasPositive("VR");
    public bool HasVrr => Facts.HasPositive("VRR");
    public bool HasVbl => Facts.HasPositive("VBL");
    public bool HasRa2H => Facts.HasPositive("RA2H");
    public bool HasLargeOverlayVrCase => EquationGeometry.NonStdCutRawMm(Facts) > 0.5m;
    public CobLikeShankType ShankType => ResolveShankType(Facts);

    public bool IsPositive(string key) => Facts.HasPositive(key);
    public bool AreEqual(string a, string b) => Facts.AreNominalsEqual(a, b);

    public static CobLikeShankType ResolveShankType(WedgeFacts facts)
    {
        var raw = facts.NormalizedPropertyToken("Wed-Type", "Wed_Type", "Wed Type", "Shank_Type", "shank_type");
        if (EqualsAny(raw, "SW_180REV", "SW_180_DEG_REV", "SW_180DEGREV", "180_DEG_REV", "180DEGREV", "180REV", "REV", "REVERSE"))
            return CobLikeShankType.Rev180;
        return CobLikeShankType.Std;
    }

    public CobLikeFootOption ResolveFootOption()
    {
        var raw = Facts.NormalizedPropertyToken("Wed-Foot_Option", "Wed-FootOption", "Foot_Option", "FootOption");

        var foot = raw switch
        {
            var x when EqualsAny(x, "SW_G", "G") => CobLikeFootOption.G,
            var x when EqualsAny(x, "SW_VG", "VG") => CobLikeFootOption.VG,
            var x when EqualsAny(x, "SW_CG", "CG", "CC") => CobLikeFootOption.CC,
            _ => CobLikeFootOption.C
        };

        if (foot == CobLikeFootOption.C && IsPositive("CBRA") && IsPositive("CBRD") && IsPositive("CBRL"))
            return CobLikeFootOption.C_WithCbr;

        return foot;
    }

    private static bool EqualsAny(string value, params string[] options)
        => options.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
}
