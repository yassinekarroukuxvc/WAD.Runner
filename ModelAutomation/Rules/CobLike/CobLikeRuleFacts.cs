using System;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Rules.CobLike;

/// <summary>
/// Centralized read-only helper around COB-like wedge data.
/// Keeps repeated parsing / dimension lookups in one place so the rule files
/// stay focused on the actual business rules.
/// </summary>
public sealed class CobLikeRuleFacts
{
    public CobLikeRuleFacts(WedgeData wedge)
    {
        Wedge = wedge ?? throw new ArgumentNullException(nameof(wedge));
    }

    public WedgeData Wedge { get; }

    public bool HasVw => IsDimPositive("VW");
    public bool HasVr => IsDimPositive("VR");
    public bool HasVrr => IsDimPositive("VRR");
    public bool HasVbl => IsDimPositive("VBL");
    public bool HasRa2H => IsDimPositive("RA2H");

    public CobLikeShankType ShankType => ResolveShankType(Wedge);


    public bool HasLargeOverlayVrCase
    {
        get
        {
            const decimal overlayTlMm = 30m;
            //decimal softCapMm = overlayTlMm * 0.012m;
            decimal softCapMm = 0.5m;
            return ComputeCobLikeNonStdCutRawMm() > softCapMm;
        }
    }

    public bool IsDimPositive(string dimKey)
    {
        if (Wedge?.Dimensions is null)
            return false;

        if (!Wedge.Dimensions.TryGetValue(DimensionKey.From(dimKey), out var dim) || dim is null)
            return false;

        return dim.Nominal.Value > 0m;
    }

    public bool AreNominalsEqual(string dimKeyA, string dimKeyB)
        => GetNominalValue(dimKeyA) == GetNominalValue(dimKeyB);

    public decimal GetNominalValue(string dimKey)
    {
        if (Wedge?.Dimensions is null)
            return 0m;

        if (!Wedge.Dimensions.TryGetValue(DimensionKey.From(dimKey), out var dim) || dim is null)
            return 0m;

        return dim.Nominal.Value;
    }

    public bool TryGetNominalValue(string dimKey, out decimal value)
    {
        value = 0m;

        if (Wedge?.Dimensions is null)
            return false;

        if (!Wedge.Dimensions.TryGetValue(DimensionKey.From(dimKey), out var dim) || dim is null)
            return false;

        value = dim.Nominal.Value;
        return true;
    }

    public bool TryGetLengthNominalMm(string dimKey, out decimal nominalMm)
    {
        nominalMm = 0m;

        if (Wedge?.Dimensions is null)
            return false;

        var dim = Wedge.TryGet(DimensionKey.From(dimKey));
        if (dim is null || dim.Nominal.Unit != UnitKind.Millimeter)
            return false;

        nominalMm = dim.Nominal.Value;
        return true;
    }

    public bool TryGetLengthToleranceMm(string dimKey, out decimal lowerMm, out decimal upperMm)
    {
        lowerMm = 0m;
        upperMm = 0m;

        if (Wedge?.Dimensions is null)
            return false;

        var dim = Wedge.TryGet(DimensionKey.From(dimKey));
        if (dim is null || dim.Nominal.Unit != UnitKind.Millimeter)
            return false;

        lowerMm = dim.Tol.Lower.Value;
        upperMm = dim.Tol.Upper.Value;
        return true;
    }

    public bool TryGetNominalMm(string dimKey, out decimal mm)
    {
        mm = 0m;

        if (Wedge?.Dimensions is null)
            return false;

        var dim = Wedge.TryGet(DimensionKey.From(dimKey));
        if (dim is null || !dim.Nominal.IsMm)
            return false;

        mm = dim.Nominal.AsMm();
        return true;
    }

    public bool TryGetMaxLikeMm(string explicitMaxKey, string baseKey, out decimal value)
    {
        value = 0m;

        if (TryGetNominalMm(explicitMaxKey, out var explicitMax))
        {
            value = explicitMax;
            return true;
        }

        if (!TryGetLengthNominalMm(baseKey, out var nominalMm))
            return false;

        if (!TryGetLengthToleranceMm(baseKey, out _, out var upperMm))
            return false;

        value = nominalMm + upperMm;
        return true;
    }

    public decimal ComputeCobLikeNonStdCutRawMm()
    {
        const decimal extraClearanceFactor = 0.20m;

        decimal vrMax = TryGetMaxLikeMm("VR_MAX", "VR", out var resolvedVrMax)
            ? resolvedVrMax
            : 0m;

        decimal vrrMax = TryGetMaxLikeMm("VRR_MAX", "VRR", out var resolvedVrrMax)
            ? resolvedVrrMax
            : 0m;

        //decimal clearance = vrMax * extraClearanceFactor;
        decimal clearance = 0;
        return vrMax + vrrMax + clearance;
    }

    public bool TryGetNominalDeg(string dimKey, out decimal deg)
    {
        deg = 0m;

        if (Wedge?.Dimensions is null)
            return false;

        var dim = Wedge.TryGet(DimensionKey.From(dimKey));
        if (dim is null || !dim.Nominal.IsDeg)
            return false;

        deg = dim.Nominal.AsDeg();
        return true;
    }

    public static CobLikeShankType ResolveShankType(WedgeData wedge)
    {
        var raw =
            GetPropLoose(wedge, "Wed-Type") ??
            GetPropLoose(wedge, "Wed_Type") ??
            GetPropLoose(wedge, "Wed Type") ??
            GetPropLoose(wedge, "Shank_Type") ??
            GetPropLoose(wedge, "shank_type") ??
            string.Empty;

        raw = NormalizeDbToken(raw);

        if (EqualsAny(raw,
                "SW_180REV",
                "SW_180_DEG_REV",
                "SW_180DEGREV",
                "180_DEG_REV",
                "180DEGREV",
                "180REV",
                "REV",
                "REVERSE"))
            return CobLikeShankType.Rev180;

        return CobLikeShankType.Std;
    }

    public static string? GetPropLoose(WedgeData wedge, string key)
    {
        try
        {
            if (wedge?.Properties is null || wedge.Properties.Count == 0)
                return null;

            if (wedge.Properties.TryGetValue(key, out var exact))
                return exact;

            var target = NormalizeKey(key);

            foreach (var kv in wedge.Properties)
            {
                var current = NormalizeKey(kv.Key);
                if (string.Equals(current, target, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public static string NormalizeDbToken(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return string.Empty;

        s = s.Trim();

        var semi = s.IndexOf(';');
        if (semi >= 0)
            s = s.Substring(0, semi);

        return s.Trim();
    }

    public static string NormalizeKey(string? key)
    {
        key ??= string.Empty;
        key = key.Trim();
        return key.Replace("-", "").Replace("_", "").Replace(" ", "");
    }

    private static bool EqualsAny(string value, params string[] options)
    {
        for (int i = 0; i < options.Length; i++)
        {
            if (string.Equals(value, options[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
