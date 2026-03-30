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
        wedgeType = WedgeType.CKVD;

        var s = SanitizeRaw(raw);
        if (string.IsNullOrWhiteSpace(s))
            return false;

        var normalized = s.Trim().ToUpperInvariant()
            .Replace(" ", "")
            .Replace("-", "")
            .Replace("_", "")
            .Replace("/", "");

        wedgeType = normalized switch
        {
            "COB" => WedgeType.COB,
            "CKVD" => WedgeType.CKVD,
            "UT" => WedgeType.UTUS,
            "US" => WedgeType.UTUS,
            "OSG7" => WedgeType.OSG7,
            _ => WedgeType.CKVD
        };

        return normalized is "COB" or "CKVD" or "UT" or "US" or "OSG7";
    }
}