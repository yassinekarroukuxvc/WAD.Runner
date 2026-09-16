using System;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Core;

/// <summary>
/// Centralized, unit-aware access to wedge dimensions and properties.
/// Keep low-level database/model reads here so every rule family interprets data consistently.
/// </summary>
public sealed class WedgeFacts
{
    public const decimal DefaultPositiveEpsilon = 0.000001m;

    public WedgeFacts(
        WedgeData wedge,
        WedgeSubclass? subclass = null)
    {
        Wedge = wedge ?? throw new ArgumentNullException(nameof(wedge));
        Subclass = subclass;
    }

    public WedgeData Wedge { get; }

    /// <summary>
    /// Subclass associated with this fact set when known.
    ///
    /// This matters for specification properties:
    /// FG uses Wed-* specification properties; PGB ModelAutomation uses only PGB-Type when required.
    /// </summary>
    public WedgeSubclass? Subclass { get; }

    public bool HasPositive(string key, decimal eps = DefaultPositiveEpsilon)
        => TryGetNominalValue(key, out var value) && value > eps;

    public bool AreNominalsEqual(string a, string b, decimal eps = DefaultPositiveEpsilon)
        => TryGetNominalValue(a, out var av)
           && TryGetNominalValue(b, out var bv)
           && decimal.Abs(av - bv) <= eps;

    public decimal NominalOrZero(string key)
        => TryGetNominalValue(key, out var value) ? value : 0m;

    public bool TryGetNominalValue(string key, out decimal value)
    {
        value = 0m;
        if (!TryGetDimension(key, out var dim)) return false;
        value = dim.Nominal.Value;
        return true;
    }

    public bool TryGetLengthMm(string key, out decimal mm)
    {
        mm = 0m;
        if (!TryGetDimension(key, out var dim) || dim.Nominal.Unit != UnitKind.Millimeter)
            return false;

        mm = dim.Nominal.AsMm();
        return true;
    }

    public bool TryGetAngleDeg(string key, out decimal deg)
    {
        deg = 0m;
        if (!TryGetDimension(key, out var dim) || dim.Nominal.Unit != UnitKind.Degree)
            return false;

        deg = dim.Nominal.AsDeg();
        return true;
    }

    /// <summary>
    /// Returns the signed lower/upper angular tolerance values exactly as stored
    /// in the domain model.
    /// </summary>
    public bool TryGetAngleToleranceDeg(string key, out decimal lowerDeg, out decimal upperDeg)
    {
        lowerDeg = 0m;
        upperDeg = 0m;

        if (!TryGetDimension(key, out var dim) ||
            dim.Nominal.Unit != UnitKind.Degree)
        {
            return false;
        }

        /*
         * A tolerance delta belongs to its parent dimension semantically.
         *
         * Some DataManagement inputs currently create angular tolerance
         * quantities with a non-degree unit tag even though their numeric
         * values are degree deltas. Calling AsDeg() on those quantities
         * throws "Quantity is not in degrees".
         *
         * Preserve the normal unit-aware path when the tolerance is tagged
         * correctly. Otherwise, because the parent nominal is confirmed to
         * be an angle, safely interpret the stored raw tolerance value as a
         * degree delta.
         */
        lowerDeg = ReadAngleToleranceDegrees(dim.Tol.Lower);
        upperDeg = ReadAngleToleranceDegrees(dim.Tol.Upper);
        return true;
    }

    private static decimal ReadAngleToleranceDegrees(Quantity tolerance)
    {
        if (tolerance.Unit == UnitKind.Degree)
            return tolerance.AsDeg();

        return tolerance.Value;
    }

    /// <summary>
    /// Returns positive lower/upper angular tolerance magnitudes.
    /// </summary>
    public bool TryGetAngleToleranceMagnitudesDeg(
        string key,
        out decimal lowerAbsDeg,
        out decimal upperAbsDeg)
    {
        lowerAbsDeg = 0m;
        upperAbsDeg = 0m;

        if (!TryGetAngleToleranceDeg(key, out var lowerDeg, out var upperDeg))
            return false;

        lowerAbsDeg = decimal.Abs(lowerDeg);
        upperAbsDeg = decimal.Abs(upperDeg);
        return true;
    }

    /// <summary>
    /// Returns the mathematical minimum and maximum angular values.
    /// </summary>
    public bool TryGetAngleBoundsDeg(string key, out decimal minDeg, out decimal maxDeg)
    {
        minDeg = 0m;
        maxDeg = 0m;

        if (!TryGetAngleDeg(key, out var nominalDeg))
            return false;

        if (!TryGetAngleToleranceMagnitudesDeg(key, out var lowerAbsDeg, out var upperAbsDeg))
            return false;

        minDeg = nominalDeg - lowerAbsDeg;
        maxDeg = nominalDeg + upperAbsDeg;
        return true;
    }

    /// <summary>
    /// Returns the signed lower/upper tolerance values exactly as stored in the domain model.
    /// </summary>
    public bool TryGetLengthToleranceMm(string key, out decimal lowerMm, out decimal upperMm)
    {
        lowerMm = 0m;
        upperMm = 0m;

        if (!TryGetDimension(key, out var dim) || dim.Nominal.Unit != UnitKind.Millimeter)
            return false;

        lowerMm = dim.Tol.Lower.AsMm();
        upperMm = dim.Tol.Upper.AsMm();
        return true;
    }

    /// <summary>
    /// Returns positive lower/upper tolerance magnitudes. This is the preferred form for
    /// overlay tolerance-zone dimensions and min/max calculations.
    /// </summary>
    public bool TryGetLengthToleranceMagnitudesMm(string key, out decimal lowerAbsMm, out decimal upperAbsMm)
    {
        lowerAbsMm = 0m;
        upperAbsMm = 0m;

        if (!TryGetLengthToleranceMm(key, out var lowerMm, out var upperMm))
            return false;

        lowerAbsMm = decimal.Abs(lowerMm);
        upperAbsMm = decimal.Abs(upperMm);
        return true;
    }

    public bool TryGetLengthBoundsMm(string key, out decimal minMm, out decimal maxMm)
    {
        minMm = 0m;
        maxMm = 0m;

        if (!TryGetLengthMm(key, out var nominalMm))
            return false;

        if (!TryGetLengthToleranceMagnitudesMm(key, out var lowerAbsMm, out var upperAbsMm))
            return false;

        minMm = nominalMm - lowerAbsMm;
        maxMm = nominalMm + upperAbsMm;
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

        if (!TryGetLengthMm(baseKey, out var nominal))
            return false;

        if (!TryGetLengthToleranceMagnitudesMm(baseKey, out _, out var upperAbs))
            return false;

        value = nominal + upperAbs;
        return true;
    }

    public string Property(params string[] keys)
    {
        if (keys is null)
            return string.Empty;

        /*
         * MODEL-AUTOMATION SUBCLASS PROPERTY CONTRACT
         * -------------------------------------------
         * FG may use Wed-* specification properties.
         * PGB has no feed-hole or foot-option property. Within ModelAutomation,
         * the only PGB specification property that may be consumed is PGB-Type.
         *
         * Therefore a PGB run never translates arbitrary Wed-* keys into PGB-*.
         * Only a type request (for example Wed-Type/PGB-Type) resolves to PGB-Type.
         */
        if (Subclass == WedgeSubclass.PGB)
        {
            foreach (var key in keys)
            {
                if (!IsTypePropertyKey(key))
                    continue;

                var value = GetPropertyLoose("PGB-Type");
                if (!string.IsNullOrWhiteSpace(value))
                    return value!;
            }

            return string.Empty;
        }

        foreach (var key in keys)
        {
            var value = GetPropertyLoose(key);
            if (!string.IsNullOrWhiteSpace(value))
                return value!;
        }

        return string.Empty;
    }

    public string NormalizedPropertyToken(params string[] keys)
        => NormalizeDbToken(Property(keys));

    /// <summary>
    /// Reads a type property using the subclass-specific namespace.
    /// PGB may consume only PGB-Type. FG reads Wed-Type (or another supplied
    /// Wed-* property when this helper is used by legacy FG code) plus aliases.
    /// Non-type PGB property requests intentionally resolve to an empty token.
    /// </summary>
    public string NormalizedSubclassPropertyToken(
        string pgbKey,
        string wedKey,
        params string[] fgAliases)
    {
        if (Subclass == WedgeSubclass.PGB)
        {
            if (!IsTypePropertyKey(pgbKey) || !IsTypePropertyKey(wedKey))
                return string.Empty;

            return NormalizeDbToken(GetPropertyLoose("PGB-Type"));
        }

        var value = GetPropertyLoose(wedKey);
        if (!string.IsNullOrWhiteSpace(value))
            return NormalizeDbToken(value);

        if (fgAliases is not null)
        {
            foreach (var alias in fgAliases)
            {
                value = GetPropertyLoose(alias);
                if (!string.IsNullOrWhiteSpace(value))
                    return NormalizeDbToken(value);
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Returns the effective database property name for diagnostics.
    /// Only type is subclass-mapped in ModelAutomation: PGB + Wed-Type => PGB-Type.
    /// </summary>
    public string EffectivePropertyName(string key)
    {
        if (Subclass == WedgeSubclass.PGB && IsTypePropertyKey(key))
            return "PGB-Type";

        return key;
    }

    public string? GetPropertyLoose(string key)
    {
        if (Wedge.Properties is null || Wedge.Properties.Count == 0)
            return null;

        if (Wedge.Properties.TryGetValue(key, out var exact))
            return exact;

        var target = NormalizeKey(key);
        foreach (var kv in Wedge.Properties)
        {
            if (string.Equals(NormalizeKey(kv.Key), target, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }

        return null;
    }

    private static bool IsTypePropertyKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        var normalized = NormalizeKey(key);
        return string.Equals(normalized, "WEDTYPE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "PGBTYPE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "WEDGETYPE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "SHANKTYPE", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeDbToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var token = value.Trim();
        var separatorIndex = token.IndexOf(';');
        if (separatorIndex >= 0)
            token = token[..separatorIndex];

        return token.Trim();
    }

    public static string NormalizeKey(string? key)
        => (key ?? string.Empty)
            .Trim()
            .Replace("-", string.Empty)
            .Replace("_", string.Empty)
            .Replace(" ", string.Empty);

    private bool TryGetDimension(string key, out Dimension dim)
    {
        dim = null!;
        if (string.IsNullOrWhiteSpace(key) || Wedge.Dimensions is null)
            return false;

        if (!Wedge.Dimensions.TryGetValue(DimensionKey.From(key), out var resolved) || resolved is null)
            return false;

        dim = resolved;
        return true;
    }
}