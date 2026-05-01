namespace WAD.Runner.DataManagement.Infrastructure.Parsing;

/// <summary>
/// Normalizes article descriptions coming from the production database.
///
/// Some production records store the description as:
///   DESC_TEXT;;;;;
///
/// Others store two descriptions in the same column:
///   DESC1;DESC2;;;
///
/// Business rule:
/// - If one non-empty description exists, display that one.
/// - If two or more non-empty descriptions exist, display the second one.
/// </summary>
public static class ArticleDescriptionParser
{
    public static string? NormalizeForDisplay(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var parts = raw
            .Split(';', StringSplitOptions.None)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (parts.Count == 0)
            return null;

        if (parts.Count >= 2)
            return parts[1];

        return parts[0];
    }
}