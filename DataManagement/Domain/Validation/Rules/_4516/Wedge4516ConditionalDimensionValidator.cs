using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Validation.Rules._4516;

/// <summary>
/// Validates the dimensions required by the selected 4516
/// feed-hole type and foot option.
/// </summary>
internal static class Wedge4516ConditionalDimensionValidator
{
    private const string FeedHoleProperty =
        "Wed-Feed_H/Slot";

    private const string FootOptionProperty =
        "Wed-Foot_Option";

    public static void Validate(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues)
    {
        if (wedge is null)
            throw new ArgumentNullException(nameof(wedge));

        if (issues is null)
            throw new ArgumentNullException(nameof(issues));

        ValidateFeedHoleDimensions(
            wedge,
            wedgeType,
            issues);

        ValidateFootOptionDimensions(
            wedge,
            wedgeType,
            issues);
    }

    // ================================================================
    // FEED-HOLE VALIDATION
    // ================================================================

    private static void ValidateFeedHoleDimensions(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues)
    {
        var raw =
            WedgePropertyAccessor.ReadNormalizedToken(
                wedge,
                FeedHoleProperty,
                "Wed-Feed_H_Slot",
                "Wed Feed H Slot",
                "Wed-Feed H Slot",
                "Feed_H/Slot",
                "Feed_H_Slot",
                "Feed H Slot");

        var feedHoleType =
            NormalizeFeedHoleToken(raw);

        switch (feedHoleType)
        {
            case "STD":
                RequireAllPositive(
                    wedge,
                    wedgeType,
                    issues,
                    requirementType:
                        "4516 Feed Hole Validation",
                    ruleName:
                        "Wed-Feed_H/Slot = STD",
                    dimensions:
                        new[] { "H" });

                break;

            case "OVAL":
                RequireAllPositive(
                    wedge,
                    wedgeType,
                    issues,
                    requirementType:
                        "4516 Feed Hole Validation",
                    ruleName:
                        "Wed-Feed_H/Slot = Oval",
                    dimensions:
                        new[] { "HH", "HW" });

                break;

            case "SLOT":
                RequireAllPositive(
                    wedge,
                    wedgeType,
                    issues,
                    requirementType:
                        "4516 Feed Hole Validation",
                    ruleName:
                        "Wed-Feed_H/Slot = Slot",
                    dimensions:
                        new[] { "ST", "SW" });

                break;

            case "":
                AddPropertyIssue(
                    wedge,
                    wedgeType,
                    issues,
                    requirementType:
                        "4516 Feed Hole Validation",
                    ruleName:
                        "Resolve feed-hole type",
                    propertyName:
                        FeedHoleProperty,
                    message:
                        "field is empty. It must resolve to " +
                        "STD, Oval or Slot before dimension validation.");

                break;

            default:
                AddPropertyIssue(
                    wedge,
                    wedgeType,
                    issues,
                    requirementType:
                        "4516 Feed Hole Validation",
                    ruleName:
                        "Supported feed-hole type",
                    propertyName:
                        FeedHoleProperty,
                    message:
                        $"unsupported value '{raw}'. Expected " +
                        "STD(Round), STD, Oval or Slot.");

                break;
        }
    }

    private static string NormalizeFeedHoleToken(
        string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var token =
            WedgePropertyAccessor.NormalizeDbToken(raw)
                .Trim()
                .ToUpperInvariant();

        /*
         * Supported examples:
         *
         * STD
         * STD(Round)
         * STD (Round)
         * Standard
         */
        if (token.StartsWith(
                "STD",
                StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith(
                "STANDARD",
                StringComparison.OrdinalIgnoreCase))
        {
            return "STD";
        }

        if (token.StartsWith(
                "OVAL",
                StringComparison.OrdinalIgnoreCase))
        {
            return "OVAL";
        }

        if (token.StartsWith(
                "SLOT",
                StringComparison.OrdinalIgnoreCase))
        {
            return "SLOT";
        }

        return token;
    }

    // ================================================================
    // FOOT-OPTION VALIDATION
    // ================================================================

    private static void ValidateFootOptionDimensions(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues)
    {
        var raw =
            WedgePropertyAccessor.ReadNormalizedToken(
                wedge,
                FootOptionProperty,
                "Wed_Foot_Option",
                "Wed Foot Option",
                "Wed-Foot Option",
                "Foot_Option",
                "Foot Option");

        var footOption =
            NormalizeFootOptionToken(raw);

        switch (footOption)
        {
            case "LW_VG":
            case "SW_VG":
                RequireAllPositive(
                    wedge,
                    wedgeType,
                    issues,
                    requirementType:
                        "4516 Foot Option Validation",
                    ruleName:
                        $"{FootOptionProperty} = {footOption}",
                    dimensions:
                        new[] { "GA", "B", "GD" });

                break;

            case "LW_C":
            case "SW_C":
                RequireAllPositive(
                    wedge,
                    wedgeType,
                    issues,
                    requirementType:
                        "4516 Foot Option Validation",
                    ruleName:
                        $"{FootOptionProperty} = {footOption}",
                    dimensions:
                        new[] { "CL", "CD" });

                break;

            case "LW_C_CBR":
            case "SW_C_CBR":
                RequireAllPositive(
                    wedge,
                    wedgeType,
                    issues,
                    requirementType:
                        "4516 Foot Option Validation",
                    ruleName:
                        $"{FootOptionProperty} = {footOption}",
                    dimensions:
                        new[]
                        {
                            "ICL",
                            "CD",
                            "CBRL",
                            "CBRD"
                        });

                break;

            case "LW_G":
            case "SW_G":
                RequireAllPositive(
                    wedge,
                    wedgeType,
                    issues,
                    requirementType:
                        "4516 Foot Option Validation",
                    ruleName:
                        $"{FootOptionProperty} = {footOption}",
                    dimensions:
                        new[] { "GO", "GD" });

                break;

            case "LW_CC":
            case "SW_CC":
                RequireAllPositive(
                    wedge,
                    wedgeType,
                    issues,
                    requirementType:
                        "4516 Foot Option Validation",
                    ruleName:
                        $"{FootOptionProperty} = {footOption}",
                    dimensions:
                        new[] { "G", "CGR", "CGD" });

                break;

            case "LW_FLAT":
            case "SW_FLAT":
                RequireAllPositive(
                    wedge,
                    wedgeType,
                    issues,
                    requirementType:
                        "4516 Foot Option Validation",
                    ruleName:
                        $"{FootOptionProperty} = {footOption}",
                    dimensions:
                        new[] { "W" });

                break;

            case "":
                AddPropertyIssue(
                    wedge,
                    wedgeType,
                    issues,
                    requirementType:
                        "4516 Foot Option Validation",
                    ruleName:
                        "Resolve foot option",
                    propertyName:
                        FootOptionProperty,
                    message:
                        "field is empty. The 4516 property resolver " +
                        "must populate the foot option before validation.");

                break;

            default:
                AddPropertyIssue(
                    wedge,
                    wedgeType,
                    issues,
                    requirementType:
                        "4516 Foot Option Validation",
                    ruleName:
                        "Supported foot option",
                    propertyName:
                        FootOptionProperty,
                    message:
                        $"unsupported value '{raw}'. Expected LW_VG, " +
                        "LW_C, LW_C_CBR, LW_G, LW_CC or LW_FLAT.");

                break;
        }
    }

    private static string NormalizeFootOptionToken(
        string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var token =
            WedgePropertyAccessor.NormalizeDbToken(raw)
                .Trim()
                .Replace('-', '_')
                .Replace(' ', '_')
                .Trim('_')
                .ToUpperInvariant();

        while (token.Contains(
                   "__",
                   StringComparison.Ordinal))
        {
            token =
                token.Replace(
                    "__",
                    "_",
                    StringComparison.Ordinal);
        }

        return token;
    }

    // ================================================================
    // REQUIRED-DIMENSION VALIDATION
    // ================================================================

    private static void RequireAllPositive(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues,
        string requirementType,
        string ruleName,
        IReadOnlyList<string> dimensions)
    {
        foreach (var dimensionKey in dimensions)
        {
            if (WedgeDimensionAccess.IsPositive(
                    wedge,
                    dimensionKey))
            {
                continue;
            }

            issues.Add(
                new DimensionValidationIssue(
                    wedge.ArticleNumber,
                    wedgeType,
                    requirementType,
                    ruleName,
                    dimensionKey,
                    BuildMissingOrInvalidMessage(
                        wedge,
                        dimensionKey,
                        ruleName)));
        }
    }

    private static string BuildMissingOrInvalidMessage(
        WedgeData wedge,
        string dimensionKey,
        string ruleName)
    {
        if (!WedgeDimensionAccess.TryGetDimension(
                wedge,
                dimensionKey,
                out var dimension) ||
            dimension is null)
        {
            return
                $"missing; '{dimensionKey}' must be present and > 0 " +
                $"because [{ruleName}] is selected.";
        }

        return
            $"invalid ({dimensionKey}={dimension.Nominal.Value}); " +
            $"'{dimensionKey}' must be > 0 because " +
            $"[{ruleName}] is selected.";
    }

    private static void AddPropertyIssue(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues,
        string requirementType,
        string ruleName,
        string propertyName,
        string message)
    {
        issues.Add(
            new DimensionValidationIssue(
                wedge.ArticleNumber,
                wedgeType,
                requirementType,
                ruleName,
                propertyName,
                message));
    }
}