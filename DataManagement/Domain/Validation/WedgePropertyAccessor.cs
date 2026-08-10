using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Validation;

internal static class WedgePropertyAccessor
{
    public static string ReadNormalizedToken(
        WedgeData wedge,
        params string[] aliases)
    {
        var raw = ReadRaw(wedge, aliases);
        return NormalizeDbToken(raw);
    }

    public static string? ReadRaw(
        WedgeData wedge,
        params string[] aliases)
    {
        if (wedge?.Properties is null ||
            wedge.Properties.Count == 0 ||
            aliases is null)
        {
            return null;
        }

        foreach (var alias in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias))
                continue;

            if (wedge.Properties.TryGetValue(
                    alias,
                    out var exact))
            {
                return exact;
            }

            var wanted = NormalizeKey(alias);

            foreach (var pair in wedge.Properties)
            {
                if (string.Equals(
                        NormalizeKey(pair.Key),
                        wanted,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value;
                }
            }
        }

        return null;
    }

    public static bool TrySet(
        WedgeData wedge,
        string canonicalKey,
        string value,
        out string failureReason)
    {
        failureReason = string.Empty;

        if (wedge is null)
        {
            failureReason = "WedgeData is null.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(canonicalKey))
        {
            failureReason = "The property key is empty.";
            return false;
        }

        if (wedge.Properties is not
            IDictionary<string, string> mutableProperties)
        {
            failureReason =
                "WedgeData.Properties is null or read-only. " +
                "The inferred value cannot be written back.";

            return false;
        }

        var wanted = NormalizeKey(canonicalKey);

        var existingKey = mutableProperties.Keys
            .FirstOrDefault(
                key => string.Equals(
                    NormalizeKey(key),
                    wanted,
                    StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(existingKey))
        {
            mutableProperties[existingKey] = value;
            return true;
        }

        mutableProperties[canonicalKey] = value;
        return true;
    }

    public static string NormalizeDbToken(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var token = value
            .Trim()
            .Trim('\0');

        var separatorIndex = token.IndexOf(';');

        if (separatorIndex >= 0)
            token = token[..separatorIndex];

        return token.Trim();
    }

    private static string NormalizeKey(
        string? key)
    {
        return (key ?? string.Empty)
            .Trim()
            .Replace("-", string.Empty)
            .Replace("_", string.Empty)
            .Replace(" ", string.Empty)
            .Replace("/", string.Empty);
    }
}
