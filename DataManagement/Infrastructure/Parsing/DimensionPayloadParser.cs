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

        // 2) Tolerances (mm), strict numeric or empty -> 0
        var lt = ParseOptionalDecimal(p[1]);
        var ut = ParseOptionalDecimal(p[2]);

        // 3) Comment: merge original + any salvage note
        var cmt = NormalizeComment(p[3]);
        if (!string.IsNullOrWhiteSpace(note))
            cmt = string.IsNullOrWhiteSpace(cmt) ? note : $"{cmt} (source={note})";

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
        var nomDeg = ParseRequiredDecimal(p[0], "nominal (deg)", payload);
        var cmt = NormalizeComment(p[3]);

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

        // Normalize nulls for safety
        for (int i = 0; i < parts.Length; i++)
            parts[i] = parts[i]?.Trim() ?? string.Empty;

        return parts;
    }

    private static decimal ParseRequiredDecimal(string s, string fieldName, string payloadForError)
    {
        if (!decimal.TryParse(s, NumberStyles.Float, CI, out var v))
            throw new FormatException($"Invalid {fieldName}: \"{s}\" in payload \"{payloadForError}\"");
        return v;
    }

    private static decimal ParseOptionalDecimal(string s)
        => string.IsNullOrWhiteSpace(s) ? 0m : decimal.Parse(s, NumberStyles.Float, CI);

    private static string? NormalizeComment(string s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

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

        // Fast path: plain decimal (e.g., "0.424")
        if (decimal.TryParse(t, NumberStyles.Float, CI, out var plain))
            return (plain, null);

        // If contains a pipe or other delimiters, try each segment
        var candidates = SplitCompositeToken(t);

        foreach (var c in candidates)
        {
            if (TryParseUnitValueToMm(c, out var mm))
                return (mm, t);
            if (decimal.TryParse(c, NumberStyles.Float, CI, out var asMm))
                return (asMm, t);
        }

        // Last chance: scan the whole token for first "<number><unit>" pair
        if (TryScanNumberWithUnitToMm(t, out var mmFromScan))
            return (mmFromScan, t);

        // Give up
        throw new FormatException($"Invalid nominal (mm): \"{t}\"");
    }

    /// <summary>
    /// Splits composite tokens like "100X|700um" into ["100X","700um"] and also tries
    /// gentle splits on whitespace and commas.
    /// </summary>
    private static IEnumerable<string> SplitCompositeToken(string s)
    {
        // Prioritize pipe '|' which is common in "100X|700um"
        foreach (var byPipe in s.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = byPipe.Trim();
            if (trimmed.Length == 0) continue;

            // Further split on commas or spaces if present
            var secondaries = trimmed.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var sec in secondaries)
                yield return sec.Trim();
        }

        // If there was no pipe at all, still return the original once
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
        // Normalize micro symbol
        lower = lower.Replace("µm", "um");

        if (lower.EndsWith("mm", StringComparison.Ordinal))
        {
            var num = lower.Substring(0, lower.Length - 2).Trim();
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

        // number + unit (mm|um), with optional spaces:  ([+-]?\d+(\.\d+)?)[ ]*(mm|um)
        var rx = new Regex(@"([+-]?\d+(?:\.\d+)?)[ ]*(mm|um)", RegexOptions.IgnoreCase);
        var m = rx.Match(normalized);
        if (!m.Success) return false;

        var number = m.Groups[1].Value;
        var unit = m.Groups[2].Value.ToLowerInvariant();

        if (!decimal.TryParse(number, NumberStyles.Float, CI, out var v))
            return false;

        mm = unit == "mm" ? v : v / 1000m;
        return true;
    }
}
