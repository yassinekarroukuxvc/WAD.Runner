using System.Globalization;
using System.Text.RegularExpressions;

using WAD.Runner.DataManagement.Domain.Units;

namespace WAD.Runner.DataManagement.Infrastructure.Parsing;

/// <summary>
/// Parses semicolon-separated payloads coming from Spec2 rows:
///   Length/Angle: "nom;Ltol;Utol;Comment;;;;"
/// All numeric fields are expressed in millimeters for lengths.
/// Angles use the same payload shape but are interpreted in degrees and
/// carry zero length tolerance in the domain.
///
/// Hardened for noisy production data:
/// - Accepts plain numerics and unit-bearing numerics (mm / um / µm)
/// - Accepts composite nominal tokens like "100X|700um"
/// - Accepts style / non-numeric markers in numeric slots:
///     REF, REFERENCE, MIN, MIN., MINIMUM, MAX, MAX., MAXIMUM,
///     NA, N/A, N.A., NONE, NULL, TBD, -, --
/// - Accepts inequality-like forms:
///     <MIN, >MAX, <=MIN, >=MAX
/// - Tolerates punctuation/noise around style tokens
/// - Preserves meaningful style markers into the returned comment
/// - Optional tolerance slots never throw for known non-numeric tokens
/// - Negative tolerances are normalized to positive values
/// </summary>
public static class DimensionPayloadParser
{
    private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    /// <summary>
    /// Parse a LENGTH row. Returns nominal in mm, tolerance in mm, optional comment.
    /// Missing or empty tolerances default to 0.
    /// </summary>
    public static (Quantity nominalMm, Tolerance tolMm, string? comment) ParseLengthRow(string payload)
    {
        var p = Split(payload);

        var (nomMm, note) = ParseNominalFlexibleToMm(p[0]);

        var lt = ParseOptionalDecimal(p[1]);
        var ut = ParseOptionalDecimal(p[2]);

        lt = decimal.Abs(lt);
        ut = decimal.Abs(ut);

        var cmt = NormalizeComment(p[3]);

        if (!string.IsNullOrWhiteSpace(note))
            cmt = string.IsNullOrWhiteSpace(cmt) ? note : $"{cmt} (source={note})";

        cmt = AppendStyleMarkersToCommentIfAny(cmt, p[0], p[1], p[2]);

        return (Quantity.MmOf(nomMm), Tolerance.Mm(lt, ut), cmt);
    }

    /// <summary>
    /// Parse an ANGLE row. Returns nominal in degrees and ZERO length tolerance.
    /// Any numeric values in Ltol/Utol are ignored at this stage.
    /// </summary>
    public static (Quantity nominalDeg, Tolerance tolMm, string? comment) ParseAngleRow(string payload)
    {
        var p = Split(payload);

        var nomToken = p[0];

        if (IsStyleToken(nomToken))
            throw new FormatException($"Angle nominal cannot be a style token: \"{nomToken}\" in payload \"{payload}\"");

        var nomDeg = ParseRequiredDecimal(nomToken, "nominal (deg)", payload);

        var cmt = NormalizeComment(p[3]);
        cmt = AppendStyleMarkersToCommentIfAny(cmt, p[1], p[2]);

        return (Quantity.DegOf(nomDeg), Tolerance.Zero, cmt);
    }

    // ---------------------------------------------------------------------
    // Core helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// Expected shape is at least 4 fields: nom;Ltol;Utol;Comment;...
    /// Extra trailing fields are tolerated.
    /// </summary>
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

    /// <summary>
    /// Optional numeric fields:
    /// - empty => 0
    /// - known style tokens => 0
    /// - tolerant of comma decimals
    /// - does not throw for recognized noisy DB placeholders
    /// </summary>
    private static decimal ParseOptionalDecimal(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return 0m;

        var t = s.Trim();

        if (IsStyleToken(t))
            return 0m;

        t = NormalizeNumericToken(t);

        if (decimal.TryParse(t, NumberStyles.Float, CI, out var v))
            return v;

        throw new FormatException($"Invalid numeric token '{t}' in tolerance field.");
    }

    private static string NormalizeNumericToken(string s)
    {
        var t = (s ?? string.Empty).Trim();

        t = t.Replace(',', '.');

        return t;
    }

    /// <summary>
    /// Aggressively normalizes style-like tokens from noisy DB exports.
    ///
    /// Examples:
    ///   "MIN."      -> "MIN"
    ///   " minimum " -> "MINIMUM"
    ///   "N.A."      -> "NA"
    ///   "<=MAX."    -> "MAX"
    ///   "--"        -> "-"
    ///   "REF:"      -> "REF"
    /// </summary>
    private static string NormalizeStyleToken(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return string.Empty;

        var t = s.Trim().ToUpperInvariant();

        // normalize unicode variants / spacing
        t = t.Replace("µ", "U");
        t = Regex.Replace(t, @"\s+", string.Empty);

        // strip leading comparison / decoration chars
        t = t.TrimStart('<', '>', '=', '~');

        // remove trailing punctuation / separators
        t = t.TrimEnd('.', ':', ';', ',', '_');

        // collapse repeated dashes
        t = Regex.Replace(t, @"^-+$", "-");

        // normalize common forms
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

    // ---------------------------------------------------------------------
    // Nominal parsing
    // ---------------------------------------------------------------------

    /// <summary>
    /// Attempts to parse the nominal token into millimeters with tolerant handling:
    /// - Pure number -> mm
    /// - Number + unit (mm/um/µm) -> converted to mm
    /// - Composite forms with separators like '|'
    /// - Known style tokens -> 0mm
    /// Returns: (mmValue, originalTokenIfCompositeElseNull)
    /// </summary>
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

    /// <summary>
    /// Splits composite tokens like "100X|700um" into candidates and also tries
    /// gentle splits on whitespace, commas, and slashes.
    /// </summary>
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

    /// <summary>
    /// Try parse strings like:
    ///   "0.70mm", "700um", "700µm"
    /// Also tolerates wrapping noise like:
    ///   "(700um)", "[0.70mm]"
    /// </summary>
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

    /// <summary>
    /// Scans a free-form string for the first number+unit pair (mm/um/µm).
    /// Examples:
    ///   "100X|700um"   -> 700um  -> 0.700 mm
    ///   "200x 0.70mm"  -> 0.70mm -> 0.700 mm
    ///   "MAG=100X 700um" -> 700um -> 0.700 mm
    /// </summary>
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