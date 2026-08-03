using System;
using System.Collections.Generic;

namespace WAD.Runner.ModelAutomation.Equations;

internal static class EquationCatalog
{
    internal static class Names
    {
        public const string EngravingStart = "EngravingStart";
        public const string OverlayCalibration1 = "overlay_calibration1";
        public const string Scale = "scale";
        public const string FunnelGap = "funnel_gap";
        public const string NonStdCut = "non_std_cut@ref_point_non_std_cut_sketch";
    }

    public static readonly IReadOnlyDictionary<string, string> DbToModelAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["RC"] = "CR"
        };

    public static readonly IReadOnlyCollection<string> CkvdDbDrivenKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TL","TD","TDF",
            "B","E","ER","F","FL","FX","W","X","GD","GR",
            "FR","BR","FRX","BRX",
            "VR","VW","VRR","VRA",
            "TIP",
            "k",
            "SymmetryTolerance",
            "BA","FA","GA","ISA"
        };

    public static readonly IReadOnlyCollection<string> CkvdAngleKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "BA", "FA", "GA", "ISA"
        };
}
