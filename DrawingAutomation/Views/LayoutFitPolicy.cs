namespace WAD.Runner.DrawingAutomation.Views;

/// <summary>
/// Tunables for keeping drawing views inside the sheet and
/// free of overlaps. Distances are in millimeters.
/// </summary>
public sealed record LayoutFitPolicy(
    double SheetMarginMm = 5.0,
    double ViewGapMm = 4.0,
    double SecondaryScaleStep = 0.5,
    double SecondaryMinScale = 1.0,
    int MaxSecondaryIterations = 40)
{
    public static readonly LayoutFitPolicy Default = new LayoutFitPolicy();
}