using System;
using System.Globalization;

using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.ModelAutomation.Common;

using DomDim =
    WAD.Runner.DataManagement.Domain.Dimensions.Dimension;

namespace WAD.Runner.ModelAutomation.Equations;

internal static class EquationFormatting
{
    private const string NumericFormat = "0.##########";

    public static string Number(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Equation values must be finite.");
        }

        return value.ToString(
            NumericFormat,
            CultureInfo.InvariantCulture);
    }

    public static string Number(decimal value)
        => value.ToString(
            NumericFormat,
            CultureInfo.InvariantCulture);

    public static string DimensionLine(
        string key,
        DomDim dimension)
    {
        if (dimension is null)
            throw new ArgumentNullException(nameof(dimension));

        return dimension.Nominal.Unit switch
        {
            UnitKind.Degree =>
                Line(
                    key,
                    dimension.Nominal.AsDeg(),
                    "deg"),

            UnitKind.Millimeter =>
                LengthLineFromMillimeters(
                    key,
                    dimension.Nominal.AsMm()),

            _ => throw new InvalidOperationException(
                $"Unsupported equation dimension unit " +
                $"'{dimension.Nominal.Unit}' for '{key}'.")
        };
    }

    public static string LengthLineFromMillimeters(
        string key,
        decimal millimeters)
        => Line(
            key,
            ModelLengthUnits.MillimetersToInches(
                millimeters),
            "in");

    public static string LengthLineFromMillimeters(
        string key,
        double millimeters)
        => Line(
            key,
            ModelLengthUnits.MillimetersToInches(
                millimeters),
            "in");

    public static string Line(
        string key,
        double value,
        string unit = "")
        => $"\"{key}\" = {Number(value)}{unit}";

    public static string Line(
        string key,
        decimal value,
        string unit = "")
        => $"\"{key}\" = {Number(value)}{unit}";
}
