
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Validation;

internal static class PgbWedgeValidator
{
    public static void Validate(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues)
    {
        var rules = PgbValidationRuleCatalog.For(wedgeType);

        ValidateRequiredDimensions(
            wedge,
            wedgeType,
            rules.RequiredDimensions,
            issues);

        ValidateAndCleanProperties(
            wedge,
            wedgeType,
            rules.PropertyRules,
            issues);

        ValidateCWithCbrConsistency(
            wedge,
            wedgeType,
            issues);
    }


    private static void ValidateCWithCbrConsistency(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues)
    {
        var footOption =
            WedgePropertyAccessor.ReadNormalizedToken(
                    wedge,
                    "Wed-Foot_Option",
                    "Wed_Foot_Option",
                    "Wed Foot Option",
                    "Wed-Foot Option",
                    "Foot_Option",
                    "Foot Option")
                .Trim()
                .Replace('-', '_')
                .Replace(' ', '_')
                .ToUpperInvariant();

        if (footOption is not ("LW_C" or "SW_C"))
            return;

        var hasCbrl = WedgeDimensionAccess.IsPositive(wedge, "CBRL");
        var hasCbrd = WedgeDimensionAccess.IsPositive(wedge, "CBRD");

        if (!hasCbrl && !hasCbrd)
            return;

        foreach (var key in new[] { "CBRL", "CBRD" })
        {
            if (WedgeDimensionAccess.IsPositive(wedge, key))
                continue;

            var message =
                WedgeDimensionAccess.TryGetDimension(wedge, key, out var dimension)
                    ? $"invalid ({key}={dimension!.Nominal.Value}); must be > 0 when a C foot has CBR data"
                    : "missing; must be > 0 when a C foot has CBR data";

            AddIssue(
                wedge,
                wedgeType,
                issues,
                "PGB C with CBR",
                "C foot with CBR",
                key,
                message);
        }
    }

    private static void ValidateRequiredDimensions(
        WedgeData wedge,
        WedgeType wedgeType,
        IReadOnlyList<string> requiredDimensions,
        List<DimensionValidationIssue> issues)
    {
        foreach (var key in requiredDimensions)
        {
            if (WedgeDimensionAccess.IsPositive(wedge, key))
                continue;

            var message =
                WedgeDimensionAccess.TryGetDimension(
                    wedge,
                    key,
                    out var dimension)
                    ? $"invalid ({key}={dimension!.Nominal.Value}); must be > 0"
                    : "missing; must be > 0";

            AddIssue(
                wedge,
                wedgeType,
                issues,
                "PGB required dimension",
                key,
                key,
                message);
        }
    }

    private static void ValidateAndCleanProperties(
        WedgeData wedge,
        WedgeType wedgeType,
        IReadOnlyList<PgbPropertyValidationRule> propertyRules,
        List<DimensionValidationIssue> issues)
    {
        foreach (var rule in propertyRules)
        {
            var rawValue =
                WedgePropertyAccessor.ReadRaw(
                    wedge,
                    rule.PropertyName);

            var cleanedValue =
                WedgePropertyAccessor.NormalizeDbToken(rawValue);

            if (string.IsNullOrWhiteSpace(cleanedValue))
            {
                AddIssue(
                    wedge,
                    wedgeType,
                    issues,
                    "PGB required field",
                    rule.PropertyName,
                    rule.PropertyName,
                    "missing or empty after database-value cleanup");

                continue;
            }

            var canonicalValue =
                rule.AllowedValues.FirstOrDefault(
                    allowed => string.Equals(
                        allowed,
                        cleanedValue,
                        StringComparison.OrdinalIgnoreCase));

            if (canonicalValue is null)
            {
                AddIssue(
                    wedge,
                    wedgeType,
                    issues,
                    "PGB allowed field value",
                    rule.PropertyName,
                    rule.PropertyName,
                    $"invalid value '{cleanedValue}' " +
                    $"(raw database value: '{FormatRaw(rawValue)}'). " +
                    "Allowed values after cleaning: " +
                    string.Join(", ", rule.AllowedValues));

                continue;
            }

            if (!WedgePropertyAccessor.TrySet(
                    wedge,
                    rule.PropertyName,
                    canonicalValue,
                    out var failureReason))
            {
                AddIssue(
                    wedge,
                    wedgeType,
                    issues,
                    "PGB field cleanup",
                    rule.PropertyName,
                    rule.PropertyName,
                    $"value '{cleanedValue}' is valid, but the cleaned " +
                    $"canonical value '{canonicalValue}' could not be written " +
                    $"back to WedgeData.Properties. {failureReason}");
            }
        }
    }

    private static string FormatRaw(
        string? rawValue)
    {
        if (rawValue is null)
            return "<null>";

        return rawValue
            .Replace("\0", "\\0", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static void AddIssue(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues,
        string requirementType,
        string ruleName,
        string dimension,
        string message)
    {
        issues.Add(
            new DimensionValidationIssue(
                wedge.ArticleNumber,
                wedgeType,
                requirementType,
                ruleName,
                dimension,
                message));
    }
}
