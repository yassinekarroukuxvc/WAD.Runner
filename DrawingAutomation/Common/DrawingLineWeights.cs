using System;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;

namespace WAD.Runner.DrawingAutomation.Common
{
    /// <summary>
    /// Print line weights for production/customer drawings (document "Line Font" settings).
    ///
    /// Two-weight drafting convention (2:1):
    ///     Thick (0.35 mm) -> visible edges, section lines, emphasized section outline
    ///     Thin  (0.18 mm) -> dimensions/leaders, crosshatch, hidden, tangent, sketch,
    ///                        construction, break and detail lines
    ///
    /// The mm values are SolidWorks' printer line weights; the PDF export uses them because
    /// <see cref="Exporter"/> enables "use current print line weights".
    /// </summary>
    public static class DrawingLineWeights
    {
        private static readonly (swUserPreferenceIntegerValue_e Setting, swLineWeights_e Weight)[] Standard =
        {
            (swUserPreferenceIntegerValue_e.swLineFontVisibleEdgesThickness, swLineWeights_e.swLW_THICK),
            (swUserPreferenceIntegerValue_e.swLineFontSectionLineThickness, swLineWeights_e.swLW_THICK),
            (swUserPreferenceIntegerValue_e.swLineFontEmphasizedSectionThickness, swLineWeights_e.swLW_THICK),

            (swUserPreferenceIntegerValue_e.swLineFontDimensionsThickness, swLineWeights_e.swLW_THIN),
            (swUserPreferenceIntegerValue_e.swLineFontCrosshatchThickness, swLineWeights_e.swLW_THIN),
            (swUserPreferenceIntegerValue_e.swLineFontHiddenEdgesThickness, swLineWeights_e.swLW_THIN),
            (swUserPreferenceIntegerValue_e.swLineFontTangentEdgesThickness, swLineWeights_e.swLW_THIN),
            (swUserPreferenceIntegerValue_e.swLineFontSketchCurvesThickness, swLineWeights_e.swLW_THIN),
            (swUserPreferenceIntegerValue_e.swLineFontConstructionCurvesThickness, swLineWeights_e.swLW_THIN),
            (swUserPreferenceIntegerValue_e.swLineFontBreakLineThickness, swLineWeights_e.swLW_THIN),
            (swUserPreferenceIntegerValue_e.swLineFontDetailCircleThickness, swLineWeights_e.swLW_THIN),
            (swUserPreferenceIntegerValue_e.swLineFontDetailBorderThickness, swLineWeights_e.swLW_THIN),
        };

        public static void ApplyPrintStandard(ModelDoc2? model)
        {
            if (model is null)
                return;

            var ext = model.Extension;
            var failed = 0;

            foreach (var (setting, weight) in Standard)
            {
                try
                {
                    if (!ext.SetUserPreferenceInteger(
                            (int)setting,
                            (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified,
                            (int)weight))
                    {
                        failed++;
                        Logger.Warn($"[LineWeights] {setting} -> {weight} was not accepted.");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    Logger.Warn($"[LineWeights] {setting} failed: {ex.Message}");
                }
            }

            Logger.Info(
                $"[LineWeights] Applied print line weights (visible/section=thick, annotations/hatch=thin), " +
                $"failed={failed}.");
        }
    }
}
