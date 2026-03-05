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
/// This parser is tolerant of common real-world shapes for the nominal field:
///   - "0.424"                -> 0.424 mm
///   - "0.424mm"              -> 0.424 mm
///   - "700um" / "700µm"      -> 0.700 mm
///   - "100X|700um"           -> 0.700 mm   (magnification|calibration)
///   - "200x|0.70mm"          -> 0.700 mm
/// If a composite/annotated token is used (e.g. "100X|700um"), a short note
/// is appended to the returned comment to preserve the original token.
///
/// ✅ Also tolerates ProAlpha style tokens appearing in numeric slots:
///   - REF, MIN, MAX, '-', 'N/A'
/// These are treated as "no numeric value" (0) and preserved into the comment.
///
/// ✅ Tolerance sign normalization:
///   - If parsed tolerance is negative, it is converted to positive (absolute value).
///     Example: LTOL = -0.001 => 0.001
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

        // 1) Nominal (mm) with tolerant parsing
        var (nomMm, note) = ParseNominalFlexibleToMm(p[0]);

        // 2) Tolerances (mm), tolerant numeric or empty -> 0
        var lt = ParseOptionalDecimal(p[1]);
        var ut = ParseOptionalDecimal(p[2]);

        // ✅ Normalize negative tolerances => positive
        lt = decimal.Abs(lt);
        ut = decimal.Abs(ut);

        // 3) Comment: merge original + any salvage note
        var cmt = NormalizeComment(p[3]);
        if (!string.IsNullOrWhiteSpace(note))
            cmt = string.IsNullOrWhiteSpace(cmt) ? note : $"{cmt} (source={note})";

        // 4) Preserve style markers if they appear in numeric slots
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

        // Angles remain strict numbers (no unit conversion here by design).
        // If REF/MIN/MAX is present in the nominal slot for an angle, this is invalid.
        var nomDeg = ParseRequiredDecimal(p[0], "nominal (deg)", payload);

        var cmt = NormalizeComment(p[3]);

        // Preserve markers if they appear in tol slots (rare, but harmless)
        cmt = AppendStyleMarkersToCommentIfAny(cmt, p[1], p[2]);

        // Angular tolerances not modeled here; keep zero length tolerance.
        return (Quantity.DegOf(nomDeg), Tolerance.Zero, cmt);
    }

    // ------------------------ helpers ------------------------

    /// <summary>
    /// Expected shape is at least 4 fields: nom;Ltol;Utol;Comment;...
    /// We tolerate extra trailing fields.
    /// </summary>
    private static string[] Split(string payload)
    {
        if (payload is null)
            throw new ArgumentNullException(nameof(payload), "Payload cannot be null.");

        var parts = payload.Split(';');
        if (parts.Length < 4)
            throw new FormatException($"Payload must have at least 4 fields (nom;Ltol;Utol;Comment). Received: \"{payload}\"");

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
    /// - REF/MIN/MAX/-/N/A => 0
    /// - tolerant of comma decimals
    /// </summary>
    private static decimal ParseOptionalDecimal(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return 0m;

        var t = s.Trim();

        // ✅ style markers / non-numeric tokens => treat as "missing numeric"
        if (IsStyleToken(t)) return 0m;

        t = NormalizeNumericToken(t);

        if (decimal.TryParse(t, NumberStyles.Float, CI, out var v))
            return v;

        throw new FormatException($"Invalid numeric token '{t}' in tolerance field.");
    }

    private static string NormalizeNumericToken(string s)
    {
        var t = (s ?? string.Empty).Trim();

        // tolerate comma decimals from some exports/locales
        t = t.Replace(',', '.');

        return t;
    }

    private static bool IsStyleToken(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        var t = s.Trim();

        return t.Equals("REF", StringComparison.OrdinalIgnoreCase)
            || t.Equals("MIN", StringComparison.OrdinalIgnoreCase)
            || t.Equals("MAX", StringComparison.OrdinalIgnoreCase)
            || t.Equals("-", StringComparison.OrdinalIgnoreCase)
            || t.Equals("N/A", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeComment(string s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? AppendStyleMarkersToCommentIfAny(string? comment, params string[] tokens)
    {
        if (tokens is null || tokens.Length == 0) return comment;

        // Keep only "meaningful" markers (skip '-' and 'N/A' since they’re just empties)
        var markers = tokens
            .Where(IsStyleToken)
            .Select(t => (t ?? string.Empty).Trim().ToUpperInvariant())
            .Where(t => t is "REF" or "MIN" or "MAX")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (markers.Count == 0) return comment;

        var joined = string.Join(",", markers);
        return string.IsNullOrWhiteSpace(comment) ? joined : $"{comment} ({joined})";
    }

    /// <summary>
    /// Attempts to parse the nominal token into millimeters with tolerant handling:
    /// - Pure number → mm
    /// - Number + unit (mm/um/µm) → converted to mm
    /// - Composite forms with separators like '|' → attempts to find a unit-bearing part
    /// Returns: (mmValue, originalTokenIfCompositeElseNull)
    /// </summary>
    private static (decimal mm, string? sourceNote) ParseNominalFlexibleToMm(string token)
    {
        var t = token?.Trim() ?? string.Empty;
        if (t.Length == 0)
            throw new FormatException("Nominal is empty.");

        // If nominal is a style marker (REF/MIN/MAX), treat as 0mm and let caller preserve marker in comment.
        if (IsStyleToken(t))
            return (0m, null);

        // Fast path: plain decimal (e.g., "0.424")
        if (decimal.TryParse(NormalizeNumericToken(t), NumberStyles.Float, CI, out var plain))
            return (plain, null);

        // If contains a pipe or other delimiters, try each segment
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

        // Last chance: scan the whole token for first "<number><unit>" pair
        if (TryScanNumberWithUnitToMm(t, out var mmFromScan))
            return (mmFromScan, t);

        throw new FormatException($"Invalid nominal (mm): \"{t}\"");
    }

    /// <summary>
    /// Splits composite tokens like "100X|700um" into ["100X","700um"] and also tries
    /// gentle splits on whitespace and commas.
    /// </summary>
    private static IEnumerable<string> SplitCompositeToken(string s)
    {
        foreach (var byPipe in s.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = byPipe.Trim();
            if (trimmed.Length == 0) continue;

            var secondaries = trimmed.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var sec in secondaries)
                yield return sec.Trim();
        }

        if (!s.Contains('|'))
            yield return s.Trim();
    }

    /// <summary>
    /// Try parse strings like "0.70mm", "700um", "700µm", case-insensitive.
    /// </summary>
    private static bool TryParseUnitValueToMm(string s, out decimal mm)
    {
        mm = 0m;
        if (string.IsNullOrWhiteSpace(s)) return false;

        var lower = s.Trim().ToLowerInvariant();
        lower = lower.Replace("µm", "um");

        if (lower.EndsWith("mm", StringComparison.Ordinal))
        {
            var num = lower.Substring(0, lower.Length - 2).Trim();
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
            var num = lower.Substring(0, lower.Length - 2).Trim();
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
    ///   "100X|700um"  -> 700um -> 0.700 mm
    ///   "200x 0.70mm" -> 0.70mm -> 0.700 mm
    /// </summary>
    private static bool TryScanNumberWithUnitToMm(string s, out decimal mm)
    {
        mm = 0m;
        if (string.IsNullOrWhiteSpace(s)) return false;

        var normalized = s.Replace("µm", "um");

        var rx = new Regex(@"([+-]?\d+(?:\.\d+)?)[ ]*(mm|um)", RegexOptions.IgnoreCase);
        var m = rx.Match(normalized);
        if (!m.Success) return false;

        var number = NormalizeNumericToken(m.Groups[1].Value);
        var unit = m.Groups[2].Value.ToLowerInvariant();

        if (!decimal.TryParse(number, NumberStyles.Float, CI, out var v))
            return false;

        mm = unit == "mm" ? v : v / 1000m;
        return true;
    }
}