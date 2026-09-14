using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Validation;

/// <summary>
/// Cleans database-backed FG property tokens and validates the FG fields whose
/// allowed values are wedge-type specific.
///
/// Feed-hole and foot-option dimensional requirements remain in the individual
/// wedge conditional validators. This class ensures those validators and model
/// automation receive cleaned, canonical database values.
/// </summary>
internal static class FgWedgePropertyValidator
{
    private const string WedType = "Wed-Type";
    private const string FeedHole = "Wed-Feed_H/Slot";
    private const string FootOption = "Wed-Foot_Option";

    public static void ValidateAndClean(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues)
    {
        if (wedge is null) throw new ArgumentNullException(nameof(wedge));
        if (issues is null) throw new ArgumentNullException(nameof(issues));
        if (wedge.Subclass != WedgeSubclass.FG) return;

        CleanFeedHole(wedge, wedgeType, issues);
        CleanFootOption(wedge, wedgeType, issues);
        ValidateAndCleanWedType(wedge, wedgeType, issues);
    }

    private static void CleanFeedHole(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues)
    {
        var raw = WedgePropertyAccessor.ReadRaw(
            wedge,
            FeedHole,
            "Wed-Feed_H_Slot",
            "Wed Feed H Slot",
            "Wed-Feed H Slot",
            "Feed_H/Slot",
            "Feed_H_Slot",
            "Feed H Slot");

        var cleaned = WedgePropertyAccessor.NormalizeDbToken(raw);
        if (string.IsNullOrWhiteSpace(cleaned)) return;

        var upper = cleaned.Trim().ToUpperInvariant();
        var canonical =
            upper.StartsWith("STD", StringComparison.OrdinalIgnoreCase) ||
            upper.StartsWith("STANDARD", StringComparison.OrdinalIgnoreCase)
                ? "STD"
                : upper.StartsWith("OVAL", StringComparison.OrdinalIgnoreCase)
                    ? "Oval"
                    : upper.StartsWith("SLOT", StringComparison.OrdinalIgnoreCase)
                        ? "Slot"
                        : cleaned.Trim();

        TryWriteCleaned(wedge, wedgeType, issues, FeedHole, canonical);
    }

    private static void CleanFootOption(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues)
    {
        var raw = WedgePropertyAccessor.ReadRaw(
            wedge,
            FootOption,
            "Wed_Foot_Option",
            "Wed Foot Option",
            "Wed-Foot Option",
            "Foot_Option",
            "Foot Option");

        var cleaned = NormalizeStructuredToken(raw);
        if (string.IsNullOrWhiteSpace(cleaned)) return;

        TryWriteCleaned(wedge, wedgeType, issues, FootOption, cleaned);
    }

    private static void ValidateAndCleanWedType(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues)
    {
        string[]? allowed = wedgeType switch
        {
            WedgeType.CKVD => new[]
            {
                "LW_STYLE_B_CKVD",
                "LW_STYLE_A_CKVD"
            },

            WedgeType.COB or
            WedgeType.FP or
            WedgeType.UTUS or
            WedgeType.ABT or
            WedgeType.M or
            WedgeType._1001 => new[]
            {
                "SW_STD",
                "SW_180REV"
            },

            _ => null
        };

        if (allowed is null) return;

        var raw = WedgePropertyAccessor.ReadRaw(
            wedge,
            WedType,
            "Wed_Type",
            "Wed Type",
            "Wedge-Type",
            "Wedge_Type",
            "wedge_type");

        var cleaned = NormalizeStructuredToken(raw);

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            AddIssue(
                wedge,
                wedgeType,
                issues,
                "FG required field",
                WedType,
                "missing or empty after database-value cleanup");
            return;
        }

        var canonical = allowed.FirstOrDefault(value =>
            string.Equals(value, cleaned, StringComparison.OrdinalIgnoreCase));

        if (canonical is null)
        {
            AddIssue(
                wedge,
                wedgeType,
                issues,
                "FG allowed field value",
                WedType,
                $"invalid value '{cleaned}'. Allowed values after cleaning: {string.Join(", ", allowed)}");
            return;
        }

        TryWriteCleaned(wedge, wedgeType, issues, WedType, canonical);
    }

    private static string NormalizeStructuredToken(string? raw)
    {
        var token = WedgePropertyAccessor.NormalizeDbToken(raw);
        if (string.IsNullOrWhiteSpace(token)) return string.Empty;

        token = token
            .Trim()
            .Replace('-', '_')
            .Replace(' ', '_')
            .Trim('_')
            .ToUpperInvariant();

        while (token.Contains("__", StringComparison.Ordinal))
            token = token.Replace("__", "_", StringComparison.Ordinal);

        return token;
    }

    private static void TryWriteCleaned(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues,
        string propertyName,
        string value)
    {
        if (WedgePropertyAccessor.TrySet(
                wedge,
                propertyName,
                value,
                out var failureReason))
        {
            return;
        }

        AddIssue(
            wedge,
            wedgeType,
            issues,
            "FG field cleanup",
            propertyName,
            $"cleaned value '{value}' could not be written back to WedgeData.Properties. {failureReason}");
    }

    private static void AddIssue(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues,
        string requirementType,
        string propertyName,
        string message)
    {
        issues.Add(new DimensionValidationIssue(
            wedge.ArticleNumber,
            wedgeType,
            requirementType,
            propertyName,
            propertyName,
            message));
    }
}
