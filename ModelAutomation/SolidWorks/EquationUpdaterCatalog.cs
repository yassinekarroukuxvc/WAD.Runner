using System;
using System.Collections.Generic;

namespace WAD.Runner.ModelAutomation.SolidWorks
{
    internal static class EquationUpdaterCatalog
    {
        internal static class EquationNames
        {
            public const string EngravingStart = "EngravingStart";
            public const string OverlayCalibration1 = "overlay_calibration1";
            public const string Scale = "scale";
            public const string FunnelGap = "funnel_gap";

            // Use the real equation LHS everywhere to avoid key / line mismatches.
            public const string NonStdCut = "non_std_cut@ref_point_non_std_cut_sketch";
        }

        /// <summary>
        /// DB-driven dimensions for CKVD. Missing keys are written as 0 to override the template.
        /// </summary>
        public static readonly HashSet<string> CkvdDbDrivenKeys =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "TL","TD","TDF",
                "B","E","ER","F","FL","FX","W","X","GD","GR",
                "FR","BR","FRX","BRX",
                "VR","VW","VRA",
                "TIP",
                "k",
                "SymmetryTolerance",
                "BA","FA","GA","ISA"
            };

        public static readonly HashSet<string> CkvdAngleKeys =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "BA", "FA", "GA", "ISA"
            };

        /// <summary>
        /// DB name → model name aliases.
        /// </summary>
        public static readonly Dictionary<string, string> DbToModelKeyAlias =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["RC"] = "CR"
            };
    }
}
