using System.Globalization;
using WAD.Runner.DataManagement.Domain.Units;
using DomDim = WAD.Runner.DataManagement.Domain.Dimensions.Dimension;

namespace WAD.Runner.ModelAutomation.Equations;

internal static class EquationFormatting
{
    public static string Number(double value) => value.ToString("0.#####", CultureInfo.InvariantCulture);

    public static string DimensionLine(string key, DomDim dim)
    {
        bool isAngle = dim.Nominal.Unit == UnitKind.Degree;
        double value = (double)(isAngle ? dim.Nominal.AsDeg() : dim.Nominal.AsMm());
        return $"\"{key}\" = {Number(value)}{(isAngle ? "deg" : "mm")}";
    }

    public static string Line(string key, double value, string unit = "")
        => $"\"{key}\" = {Number(value)}{unit}";
}
