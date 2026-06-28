using System;
using System.Collections.Generic;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Core;

public sealed class WedgeFacts
{
    private const decimal DefaultPositiveEpsilon = 0.000001m;

    public WedgeFacts(WedgeData wedge)
    {
        Wedge = wedge ?? throw new ArgumentNullException(nameof(wedge));
    }

    public WedgeData Wedge { get; }

    public bool HasPositive(string key, decimal eps = DefaultPositiveEpsilon)
        => TryGetNominalValue(key, out var value) && value > eps;

    public bool AreNominalsEqual(string a, string b, decimal eps = DefaultPositiveEpsilon)
        => TryGetNominalValue(a, out var av) && TryGetNominalValue(b, out var bv) && Math.Abs(av - bv) <= eps;

    public decimal NominalOrZero(string key)
        => TryGetNominalValue(key, out var value) ? value : 0m;

    public bool TryGetNominalValue(string key, out decimal value)
    {
        value = 0m;
        if (Wedge.Dimensions is null) return false;
        if (!Wedge.Dimensions.TryGetValue(DimensionKey.From(key), out var dim) || dim is null) return false;
        value = dim.Nominal.Value;
        return true;
    }

    public bool TryGetLengthMm(string key, out decimal mm)
    {
        mm = 0m;
        var dim = Wedge.TryGet(DimensionKey.From(key));
        if (dim is null || dim.Nominal.Unit != UnitKind.Millimeter) return false;
        mm = dim.Nominal.AsMm();
        return true;
    }

    public bool TryGetAngleDeg(string key, out decimal deg)
    {
        deg = 0m;
        var dim = Wedge.TryGet(DimensionKey.From(key));
        if (dim is null || dim.Nominal.Unit != UnitKind.Degree) return false;
        deg = dim.Nominal.AsDeg();
        return true;
    }

    public bool TryGetLengthToleranceMm(string key, out decimal lowerMm, out decimal upperMm)
    {
        lowerMm = 0m;
        upperMm = 0m;
        var dim = Wedge.TryGet(DimensionKey.From(key));
        if (dim is null || dim.Nominal.Unit != UnitKind.Millimeter) return false;
        lowerMm = dim.Tol.Lower.AsMm();
        upperMm = dim.Tol.Upper.AsMm();
        return true;
    }

    public bool TryGetMaxLikeMm(string explicitMaxKey, string baseKey, out decimal value)
    {
        value = 0m;
        if (TryGetLengthMm(explicitMaxKey, out var explicitMax))
        {
            value = explicitMax;
            return true;
        }

        if (!TryGetLengthMm(baseKey, out var nominal)) return false;
        if (!TryGetLengthToleranceMm(baseKey, out _, out var upper)) return false;
        value = nominal + decimal.Abs(upper);
        return true;
    }

    public string Property(params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = GetPropertyLoose(key);
            if (!string.IsNullOrWhiteSpace(value)) return value!;
        }
        return string.Empty;
    }

    public string NormalizedPropertyToken(params string[] keys)
        => NormalizeDbToken(Property(keys));

    public string? GetPropertyLoose(string key)
    {
        try
        {
            if (Wedge.Properties is null || Wedge.Properties.Count == 0) return null;
            if (Wedge.Properties.TryGetValue(key, out var exact)) return exact;

            var target = NormalizeKey(key);
            foreach (var kv in Wedge.Properties)
            {
                if (string.Equals(NormalizeKey(kv.Key), target, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    public static string NormalizeDbToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var s = value.Trim();
        var semi = s.IndexOf(';');
        if (semi >= 0) s = s[..semi];
        return s.Trim();
    }

    public static string NormalizeKey(string? key)
        => (key ?? string.Empty).Trim().Replace("-", "").Replace("_", "").Replace(" ", "");
}
