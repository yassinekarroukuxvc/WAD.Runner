using System;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

public static class AnnotationTokenNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var token = value
            .Trim()
            .Trim('\0');

        var separatorIndex = token.IndexOf(';');
        if (separatorIndex >= 0)
            token = token[..separatorIndex];

        token = token
            .Trim()
            .Trim('\0')
            .Replace('-', '_')
            .Replace(' ', '_')
            .Trim('_')
            .ToUpperInvariant();

        while (token.Contains("__", StringComparison.Ordinal))
        {
            token = token.Replace(
                "__",
                "_",
                StringComparison.Ordinal);
        }

        return token;
    }
}
