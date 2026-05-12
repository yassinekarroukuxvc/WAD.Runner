using System.Globalization;
using System.Text.RegularExpressions;
using WAD.Runner.DataManagement.Domain.Units;

namespace WAD.Runner.DataManagement.Infrastructure.Parsing;

public static class DimensionPayloadParser
{
    private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    public static (Quantity nominalMm, Tolerance tolMm, string? comment) ParseLengthRow(string payload)
    {
        var p = Split(payload);

        var (nomMm, note) = ParseNominalFlexibleToMm(p[0]);

        var lt = ParseOptionalDecimalOrDefaultRef(p[1], out var ltMarker);
        var ut = ParseOptionalDecimalOrDefaultRef(p[2], out var utMarker);

        lt = decimal.Abs(lt);
        ut = decimal.Abs(ut);

        var cmt = NormalizeComment(p[3]);

        if (!string.IsNullOrWhiteSpace(note))
            cmt = string.IsNullOrWhiteSpace(cmt) ? note : $"{cmt} (source={note})";

        cmt = AppendStyleMarkersToCommentIfAny(cmt, p[0]);
        cmt = AppendCanonicalMarkersToCommentIfAny(cmt, ltMarker, utMarker);

        return (Quantity.MmOf(nomMm), Tolerance.Mm(lt, ut), cmt);
    }

    public static (Quantity nominalDeg, Tolerance tolMm, string? comment) ParseAngleRow(string payload)
    {
        var p = Split(payload);

        var nomToken = p[0];

        if (IsStyleToken(nomToken))
            throw new FormatException($"Angle nominal cannot be a style token: \"{nomToken}\" in payload \"{payload}\"");

        var nomDeg = ParseRequiredDecimal(nomToken, "nominal (deg)", payload);

        var _ = ParseOptionalDecimalOrDefaultRef(p[1], out var ltMarker);
        var __ = ParseOptionalDecimalOrDefaultRef(p[2], out var utMarker);

        var cmt = NormalizeComment(p[3]);
        cmt = AppendCanonicalMarkersToCommentIfAny(cmt, ltMarker, utMarker);

        return (Quantity.DegOf(nomDeg), Tolerance.Zero, cmt);
    }

    private static string[] Split(string payload)
    {
        if (payload is null)
            throw new ArgumentNullException(nameof(payload), "Payload cannot be null.");

        var parts = payload.Split(';');
        if (parts.Length < 4)
            throw new FormatException(
                $"Payload must have at least 4 fields (nom;Ltol;Utol;Comment). Received: \"{payload}\"");

        for (int i = 0; i < parts.Length; i++)
            parts[i] = parts[i]?.Trim() ?? string.Empty;

        return parts;
    }

    private static decimal ParseRequiredDecimal(string s, string fieldName, string payloadForError)
    {
        var t = NormalizeNumericToken(s);

        if (!decimal.TryParse(t, NumberStyles.Float, CI, out var v))
            throw new FormatException($"Invalid {fieldName}: \"{s}\" in payload \"{payloadForError}\"");

        return v;
    }

    private static decimal ParseOptionalDecimalOrDefaultRef(string s, out string? styleMarker)
    {
        styleMarker = null;

        if (string.IsNullOrWhiteSpace(s))
            return 0m;

        var t = s.Trim();

        if (IsStyleToken(t))
        {
            styleMarker = CanonicalStyleToken(t);
            return 0m;
        }

        t = NormalizeNumericToken(t);

        if (decimal.TryParse(t, NumberStyles.Float, CI, out var v))
            return v;

        styleMarker = "REF";
        return 0m;
    }

    private static string NormalizeNumericToken(string s)
    {
        var t = (s ?? string.Empty).Trim();

        t = t.Replace(',', '.');

        return t;
    }

    private static string NormalizeStyleToken(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return string.Empty;

        var t = s.Trim().ToUpperInvariant();

        t = t.Replace("µ", "U");
        t = Regex.Replace(t, @"\s+", string.Empty);

        t = t.TrimStart('<', '>', '=', '~');

        t = t.TrimEnd('.', ':', ';', ',', '_');

        t = Regex.Replace(t, @"^-+$", "-");

        t = t.Replace("N/A", "NA");
        t = t.Replace("N.A", "NA");
        t = t.Replace("N.A.", "NA");

        return t;
    }

    private static bool IsStyleToken(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return false;

        var t = NormalizeStyleToken(s);

        return t is
            "REF" or
            "REFERENCE" or
            "MIN" or
            "MINIMUM" or
            "MAX" or
            "MAXIMUM" or
            "NA" or
            "NONE" or
            "NULL" or
            "TBD" or
            "-" or
            "NOTAPPLICABLE";
    }

    private static string CanonicalStyleToken(string s)
    {
        var t = NormalizeStyleToken(s);

        return t switch
        {
            "REFERENCE" => "REF",
            "MINIMUM" => "MIN",
            "MAXIMUM" => "MAX",
            "NOTAPPLICABLE" => "N/A",
            "NA" => "N/A",
            "NONE" => "NONE",
            "NULL" => "NULL",
            "TBD" => "TBD",
            "-" => "-",
            _ => t
        };
    }

    private static string? NormalizeComment(string s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? AppendStyleMarkersToCommentIfAny(string? comment, params string[] tokens)
    {
        if (tokens is null || tokens.Length == 0)
            return comment;

        var markers = tokens
            .Where(IsStyleToken)
            .Select(CanonicalStyleToken)
            .Where(t => t is "REF" or "MIN" or "MAX" or "N/A" or "NONE" or "NULL" or "TBD")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (markers.Count == 0)
            return comment;

        var joined = string.Join(",", markers);
        return string.IsNullOrWhiteSpace(comment) ? joined : $"{comment} ({joined})";
    }

    private static string? AppendCanonicalMarkersToCommentIfAny(string? comment, params string?[] markers)
    {
        if (markers is null || markers.Length == 0)
            return comment;

        var filtered = markers
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m!.Trim())
            .Where(t => t is "REF" or "MIN" or "MAX" or "N/A" or "NONE" or "NULL" or "TBD")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (filtered.Count == 0)
            return comment;

        var joined = string.Join(",", filtered);
        return string.IsNullOrWhiteSpace(comment) ? joined : $"{comment} ({joined})";
    }

    private static (decimal mm, string? sourceNote) ParseNominalFlexibleToMm(string token)
    {
        var t = token?.Trim() ?? string.Empty;
        if (t.Length == 0)
            throw new FormatException("Nominal is empty.");

        if (IsStyleToken(t))
            return (0m, null);

        if (decimal.TryParse(NormalizeNumericToken(t), NumberStyles.Float, CI, out var plain))
            return (plain, null);

        var candidates = SplitCompositeToken(t);

        foreach (var c in candidates)
        {
            if (IsStyleToken(c))
                return (0m, t);

            if (TryParseUnitValueToMm(c, out var mm))
                return (mm, t);

            if (decimal.TryParse(NormalizeNumericToken(c), NumberStyles.Float, CI, out var asMm))
                return (asMm, t);
        }

        if (TryScanNumberWithUnitToMm(t, out var mmFromScan))
            return (mmFromScan, t);

        throw new FormatException($"Invalid nominal (mm): \"{t}\"");
    }

    private static IEnumerable<string> SplitCompositeToken(string s)
    {
        foreach (var primary in s.Split(new[] { '|', '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = primary.Trim();
            if (trimmed.Length == 0)
                continue;

            var secondaries = trimmed.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var sec in secondaries)
                yield return sec.Trim();
        }

        if (!s.Contains('|') && !s.Contains('/'))
            yield return s.Trim();
    }

    private static bool TryParseUnitValueToMm(string s, out decimal mm)
    {
        mm = 0m;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        var lower = s.Trim().ToLowerInvariant();
        lower = lower.Replace("µm", "um");
        lower = lower.Trim('(', ')', '[', ']', '{', '}');

        if (lower.EndsWith("mm", StringComparison.Ordinal))
        {
            var num = lower[..^2].Trim();
            num = NormalizeNumericToken(num);

            if (decimal.TryParse(num, NumberStyles.Float, CI, out var v))
            {
                mm = v;
                return true;
            }

            return false;
        }

        if (lower.EndsWith("um", StringComparison.Ordinal))
        {
            var num = lower[..^2].Trim();
            num = NormalizeNumericToken(num);

            if (decimal.TryParse(num, NumberStyles.Float, CI, out var v))
            {
                mm = v / 1000m;
                return true;
            }

            return false;
        }

        return false;
    }

    private static bool TryScanNumberWithUnitToMm(string s, out decimal mm)
    {
        mm = 0m;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        var normalized = s.Replace("µm", "um");

        var rx = new Regex(@"([+-]?\d+(?:\.\d+)?)[ ]*(mm|um)", RegexOptions.IgnoreCase);
        var m = rx.Match(normalized);
        if (!m.Success)
            return false;

        var number = NormalizeNumericToken(m.Groups[1].Value);
        var unit = m.Groups[2].Value.ToLowerInvariant();

        if (!decimal.TryParse(number, NumberStyles.Float, CI, out var v))
            return false;

        mm = unit == "mm" ? v : v / 1000m;
        return true;
    }
}
