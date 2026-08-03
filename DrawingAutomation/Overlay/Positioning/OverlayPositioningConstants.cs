namespace WAD.Runner.DrawingAutomation.Overlay.Positioning;

public static class OverlayReferencePointNames
{
    public const string Reverse180 = "ref_point_180_DEG_REV_sketch";
    public const string GenericNonStandardCut = "ref_point_non_std_cut_sketch";
    public const string CkvdStandard = "ref_point";
    public const string CkvdNonStandardCut = "ref_point_non_std_cut";
    public const string CkvdStyleA = "ref_point_a";
    public const string CkvdStyleB = "ref_point_b";
}

public static class OverlayPositioningDefaults
{
    public const double FrontXIn = 0.4;
    public const double FrontYIn = 0.3;
    public const double SideXIn = 3.19;
    public const double SideYIn = 0.0;
    public const double DetailXIn = 6.285;
    public const double DetailYIn = 2.4;
    public const double SectionXIn = 3.19;
    public const double SectionYIn = 2.4;
}

public static class OverlayViewScaleDefaults
{
    public const double PrimaryViewScale = 2.0;
}

public static class OverlayPositioningConstants
{
    public const double DetailSectionBaselineMm = 60.96;
    public const double MillimetersPerInch = 25.4;
    public const double ZeroTolerance = 1e-6;
}
