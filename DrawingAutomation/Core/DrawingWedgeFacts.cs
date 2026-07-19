using System;
using System.Linq;

using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Core;

public enum DrawingShankType
{
    Standard,
    Reverse180
}

/// <summary>
/// Small, shared reader for drawing rules that need wedge dimensions or properties.
/// It keeps unit conversion and loose database-property matching consistent.
/// </summary>
public sealed class DrawingWedgeFacts
{
    private const double PositiveEpsilonMm = 1e-6;

    public DrawingWedgeFacts(WedgeData wedge)
    {
        Wedge = wedge ?? throw new ArgumentNullException(nameof(wedge));
    }

    public WedgeData Wedge { get; }

    public DrawingShankType ShankType
    {
        get
        {
            var raw = GetProperty(
                "Wed-Type",
                "Wed_Type",
                "Wed Type",
                "Shank_Type",
                "shank_type");

            var token = NormalizeDatabaseToken(raw);
            return token.Contains("180", StringComparison.OrdinalIgnoreCase) ||
                   token.Contains("REV", StringComparison.OrdinalIgnoreCase) ||
                   token.Contains("REVERSE", StringComparison.OrdinalIgnoreCase)
                ? DrawingShankType.Reverse180
                : DrawingShankType.Standard;
        }
    }

    public bool HasPositiveLength(string key)
        => TryGetLengthMm(key, out var valueMm) &&
           Math.Abs(valueMm) > PositiveEpsilonMm;

    public double GetLengthMmOrNaN(string key)
        => TryGetLengthMm(key, out var valueMm)
            ? valueMm
            : double.NaN;

    public bool TryGetLengthMm(string key, out double valueMm)
    {
        valueMm = 0.0;

        if (string.IsNullOrWhiteSpace(key) || Wedge.Dimensions is null)
            return false;

        if (!Wedge.Dimensions.TryGetValue(DimensionKey.From(key), out var dimension) ||
            dimension is null)
        {
            dimension = Wedge.Dimensions
                .FirstOrDefault(pair => string.Equals(
                    pair.Key.Value,
                    key,
                    StringComparison.OrdinalIgnoreCase))
                .Value;
        }

        if (dimension is null || dimension.Nominal.Unit != UnitKind.Millimeter)
            return false;

        try
        {
            valueMm = (double)dimension.Nominal.AsMm();
            return double.IsFinite(valueMm);
        }
        catch
        {
            valueMm = 0.0;
            return false;
        }
    }

    public string? GetProperty(params string[] keys)
    {
        if (Wedge.Properties is null || Wedge.Properties.Count == 0 || keys is null)
            return null;

        foreach (var key in keys.Where(key => !string.IsNullOrWhiteSpace(key)))
        {
            if (Wedge.Properties.TryGetValue(key, out var exact) &&
                !string.IsNullOrWhiteSpace(exact))
            {
                return exact.Trim();
            }

            var normalizedWanted = NormalizeKey(key);
            foreach (var pair in Wedge.Properties)
            {
                if (string.Equals(
                        NormalizeKey(pair.Key),
                        normalizedWanted,
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(pair.Value))
                {
                    return pair.Value.Trim();
                }
            }
        }

        return null;
    }

    private static string NormalizeKey(string? value)
        => (value ?? string.Empty)
            .Trim()
            .Replace("-", string.Empty)
            .Replace("_", string.Empty)
            .Replace(" ", string.Empty);

    private static string NormalizeDatabaseToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var token = value.Trim();
        var separator = token.IndexOf(';');
        if (separator >= 0)
            token = token[..separator];

        return token.Trim();
    }
}
