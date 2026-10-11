namespace WAD.Runner.DrawingAutomation.Views;

/// <summary>
/// Detail/Section retain their configured ORIGIN and their configured scale
/// is a hard MAXIMUM. If they already fit, their scale is not changed.
/// Scale is reduced ONLY when the REAL view outlines overlap (including
/// the configured view gap), exceed horizontal sheet limits, or violate an
/// explicit cap. The top/bottom of the sheet are ignored by default.
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
    SecondaryFitPolicy? SecondaryFitOverride = null,
    PackedSecondaryLayoutPolicy? PackedLayoutOverride = null,
    double MinPrimaryGapMm = 13.0,
    double FrontMinLeftMm = 14.0,
    double TopMaxTopMm = 190.6)
{
    public static readonly LayoutFitPolicy Default = new();

    public SecondaryFitPolicy SecondaryFit =>
        SecondaryFitOverride ?? SecondaryFitPolicy.Default;

    public PackedSecondaryLayoutPolicy PackedLayout =>
        PackedLayoutOverride ?? PackedSecondaryLayoutPolicy.Default;
}

/// <summary>
/// Packed Detail/Section layout (drawing_config Metadata "SecondaryLayout" = "Packed").
/// Both views get one common scale, as large as fits the free region right of the primary
/// views, and are placed side by side with <see cref="ViewGapMm"/> between their annotation
/// areas, the pair centred in the region. Y origins are kept, so the feet stay aligned.
///
/// The annotation allowances (mm on paper) reserve room for the callouts that are placed
/// later at fixed paper offsets from the geometry (Detail: GR left, GD/CD right; Section:
/// FR/H/NR column left, 45 deg arcs right). PrimaryRightAllowanceMm covers the primary views'
/// right-hand callouts (the Side view BA angle).
/// </summary>
public sealed record PackedSecondaryLayoutPolicy(
    double ViewGapMm = 6.0,
    double DetailLeftMm = 6.0,
    double DetailRightMm = 14.0,
    double SectionLeftMm = 18.0,
    double SectionRightMm = 24.0,
    double PrimaryRightAllowanceMm = 24.0,
    double CalibrationFraction = 0.5,
    double RetryShrinkFactor = 0.95,
    int MaxAttempts = 3)
{
    public static readonly PackedSecondaryLayoutPolicy Default = new();
}
