namespace WAD.Runner.DataManagement.Infrastructure.Parsing;

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
