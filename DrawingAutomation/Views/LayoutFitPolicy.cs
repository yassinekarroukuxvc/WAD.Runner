namespace WAD.Runner.DrawingAutomation.Views;

/// <summary>
/// Detail/Section retain their configured ORIGIN and their configured scale
/// is a hard MAXIMUM. If they already fit, their scale is not changed.
/// Scale is reduced ONLY when required to place the ENTIRE outline to the
/// right of all Front/Side/Top outlines (+ ViewGapMm), avoid a Detail/Section
/// collision, respect the horizontal sheet bounds, or satisfy an explicitly
/// configured additional cap. Vertical sheet overflow is allowed by default.
/// The original view origins are NEVER moved by the fitting algorithm.
/// </summary>
public sealed record SecondaryFitPolicy(
    bool Enabled = true,
    double MaxWidthFraction = 1.0,
    double? MaxHeightFraction = null,
    bool ClampToSheetHeight = false,
    bool AllowGrowAboveConfigured = false,
    int RefinePasses = 2,
    double DetailPriority = 1.0,
    double SectionPriority = 1.0)
{
    public static readonly SecondaryFitPolicy Default = new();
}

/// <summary>
/// Distances are in millimeters; SolidWorks rectangles are in meters.
/// MaxSecondaryIterations limits the number of exact SolidWorks scale
/// measurements PER SECONDARY VIEW in the fallback. The fast geometric
/// estimate is verified against rebuilt SolidWorks geometry.
/// Primary Front/Side/Top autoscaling is unchanged.
/// </summary>
public sealed record LayoutFitPolicy(
    double SheetMarginMm = 5.0,
    double ViewGapMm = 4.0,
    double SecondaryScaleStep = 0.5,
    double SecondaryMinScale = 1.0,
    int MaxSecondaryIterations = 40,
    SecondaryFitPolicy? SecondaryFitOverride = null)
{
    public static readonly LayoutFitPolicy Default = new();

    public SecondaryFitPolicy SecondaryFit =>
        SecondaryFitOverride ?? SecondaryFitPolicy.Default;
}
