using System;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Infrastructure.Parsing;

public static class WedgeStyleParser
{
    public static string? SanitizeRaw(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var s = raw.Trim();

        var semi = s.IndexOf(';');
        if (semi >= 0)
            s = s[..semi];

        s = s.Trim();
        s = s.Trim('.', ',', ':', '-', '_', '/', '\\', '|', ' ');

        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    public static bool TryParseWedgeType(string? raw, out WedgeType wedgeType)
    {
        wedgeType = WedgeType.Unknown;

        var normalized = Normalize(raw);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        wedgeType = normalized switch
        {
            "COB" => WedgeType.COB,
            "CKVD" => WedgeType.CKVD,
            "SKVD" => WedgeType.CKVD,

            "FP" => WedgeType.FP,

            "UT" => WedgeType.UTUS,
            "US" => WedgeType.UTUS,
            "UTUS" => WedgeType.UTUS,
            "COBUT" => WedgeType.UTUS,
            "COBUS" => WedgeType.UTUS,
            "COBUTUS" => WedgeType.UTUS,

            "OSG7" => WedgeType.OSG7,

            _ => WedgeType.Unknown
        };

        return wedgeType != WedgeType.Unknown;
    }

    private static string? Normalize(string? raw)
    {
        var s = SanitizeRaw(raw);
        if (string.IsNullOrWhiteSpace(s))
            return null;

        return s.Trim().ToUpperInvariant()
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace("_", string.Empty)
            .Replace("/", string.Empty)
            .Replace("\\", string.Empty)
            .Replace(".", string.Empty);
    }
}
