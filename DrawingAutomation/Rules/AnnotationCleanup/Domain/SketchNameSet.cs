namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

public sealed class SketchNameSet
{
    public required string FrontSketch { get; init; }
    public required string TopSketch { get; init; }
    public required string FrBrSketch { get; init; }

    // Legacy template typo that exists in the current 180-degree CG/CC rules.
    // Keep it explicit so the behavior is easy to find and does not disappear by accident.
    public string CgDeg180TypoSketch { get; init; } = "ANNOT_180_DEG_REV_FRONT_sketch";
}
