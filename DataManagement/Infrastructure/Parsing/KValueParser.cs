using System.Globalization;
using WAD.Runner.DataManagement.Domain.Units;

namespace WAD.Runner.DataManagement.Infrastructure.Parsing;

public static class KValueParser
{
    private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    public static (Quantity kMm, string? comment) Parse(string payload)
    {
        if (payload is null)
            throw new ArgumentNullException(nameof(payload), "K-Value payload cannot be null.");

        var parts = payload.Split(';');
        if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
            throw new FormatException($"K-Value payload missing numeric value: \"{payload}\"");

        if (!decimal.TryParse(parts[0].Trim(), NumberStyles.Float, CI, out var k))
            throw new FormatException($"Invalid K-Value number \"{parts[0]}\" in payload \"{payload}\"");

        var comment = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1])
            ? parts[1].Trim()
            : null;

        return (Quantity.MmOf(k), comment);
    }

    public static bool TryParse(string payload, out Quantity kMm, out string? comment)
    {
        kMm = default;
        comment = null;
        try
        {
            (kMm, comment) = Parse(payload);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
