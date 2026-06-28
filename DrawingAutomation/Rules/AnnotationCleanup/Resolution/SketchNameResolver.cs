using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

public static class SketchNameResolver
{
    public static SketchNameSet Resolve(ShankType shank)
    {
        return shank == ShankType.Std
            ? new SketchNameSet
            {
                FrontSketch = "ANNOT_STD_FRONT_sketch",
                TopSketch = "ANNOT_STD_TOP_sketch",
                FrBrSketch = "ANNOT_FR_BR_STD_FRONT_sketch"
            }
            : new SketchNameSet
            {
                FrontSketch = "ANNOT_180_DEG_REV_FRONT_sketch",
                TopSketch = "ANNOT_180_DEG_REV_TOP_sketch",
                FrBrSketch = "ANNOT_FR_BR_180_DEG_REV_FRONT_sketch"
            };
    }
}
