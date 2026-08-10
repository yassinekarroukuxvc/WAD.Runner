using System;

using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Wedge;

using DomDim =
    WAD.Runner.DataManagement.Domain.Dimensions.Dimension;

namespace WAD.Runner.DataManagement.Domain.Validation;

internal static class WedgeDimensionAccess
{
    public static bool IsPositive(
        WedgeData wedge,
        string key)
    {
        return TryGetDimension(
                   wedge,
                   key,
                   out var dimension) &&
               dimension!.Nominal.Value > 0m;
    }

    public static bool TryGetPositiveValue(
        WedgeData wedge,
        string key,
        out decimal value)
    {
        value = 0m;

        if (!TryGetDimension(
                wedge,
                key,
                out var dimension))
        {
            return false;
        }

        value = dimension!.Nominal.Value;
        return value > 0m;
    }

    public static bool TryGetDimension(
        WedgeData wedge,
        string key,
        out DomDim? dimension)
    {
        dimension = null;

        if (wedge is null ||
            string.IsNullOrWhiteSpace(key) ||
            wedge.Dimensions is null ||
            wedge.Dimensions.Count == 0)
        {
            return false;
        }

        var target = DimensionKey.From(key);

        if (wedge.Dimensions.TryGetValue(
                target,
                out var exact))
        {
            dimension = exact;
            return dimension is not null;
        }

        foreach (var pair in wedge.Dimensions)
        {
            if (!string.Equals(
                    pair.Key.Value,
                    key,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            dimension = pair.Value;
            return dimension is not null;
        }

        return false;
    }
}
