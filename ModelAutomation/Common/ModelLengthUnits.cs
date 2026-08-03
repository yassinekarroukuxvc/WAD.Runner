using System;

namespace WAD.Runner.ModelAutomation.Common;

/// <summary>
/// Central length-unit boundary for model automation.
///
/// Database and domain length values remain in millimeters.
/// SolidWorks equation files are written in inches.
/// SolidWorks API length values are always supplied in system meters.
/// </summary>
internal static class ModelLengthUnits
{
    public const decimal MillimetersPerInch = 25.4m;
    public const decimal MillimetersPerMeter = 1000m;

    public static decimal MillimetersToInches(
        decimal millimeters)
        => millimeters / MillimetersPerInch;

    public static double MillimetersToInches(
        double millimeters)
        => millimeters / (double)MillimetersPerInch;

    public static double MillimetersToSystemMeters(
        decimal millimeters)
        => (double)(millimeters / MillimetersPerMeter);

    public static double DegreesToSystemRadians(
        decimal degrees)
        => (double)(degrees * (decimal)Math.PI / 180m);
}
