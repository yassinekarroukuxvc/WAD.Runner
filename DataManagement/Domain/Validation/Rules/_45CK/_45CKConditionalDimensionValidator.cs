using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Validation.Rules._45CK;

/// <summary>
/// Validates the dimensions required by the selected 45CK
/// feed-hole type and foot option.
/// </summary>
internal static class _45CKConditionalDimensionValidator
{
    private const string FeedHoleProperty = "Wed-Feed_H/Slot";
    private const string FootOptionProperty = "Wed-Foot_Option";

    public static void Validate(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues)
    {
        if (wedge is null)
            throw new ArgumentNullException(nameof(wedge));

        if (issues is null)
            throw new ArgumentNullException(nameof(issues));

        if (wedge.Subclass != WedgeSubclass.FG)
            return;

        ValidateFeedHoleDimensions(
            wedge,
            wedgeType,
            issues);

        ValidateFootOptionDimensions(
            wedge,
            wedgeType,
            issues);
    }

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
                    "45CK Feed Hole Validation",
                    $"{FeedHoleProperty} = STD",
                    new[]
                    {
                        "H"
                    });
                break;

            case "OVAL":
                RequireAllPositive(
                    wedge,
                    wedgeType,
                    issues,
                    "45CK Feed Hole Validation",
                    $"{FeedHoleProperty} = Oval",
                    new[]
                    {
                        "HH",
                        "HW"
                    });
                break;

            case "SLOT":
                RequireAllPositive(
                    wedge,
                    wedgeType,
                    issues,
                    "45CK Feed Hole Validation",
                    $"{FeedHoleProperty} = Slot",
                    new[]
                    {
                        "ST",
                        "SW"
                    });
                break;

            case "":
                AddPropertyIssue(
                    wedge,
                    wedgeType,
                    issues,
                    "45CK Feed Hole Validation",
                    "Feed-hole type is required",
                    FeedHoleProperty,
                    "field is empty. Expected STD, Oval or Slot.");
                break;

            default:
                AddPropertyIssue(
                    wedge,
                    wedgeType,
                    issues,
                    "45CK Feed Hole Validation",
                    "Supported feed-hole type",
                    FeedHoleProperty,
                    $"unsupported value '{raw}'. Expected STD, STD(Round), Oval or Slot.");
                break;
        }
    }

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
                RequireAllPositive(
                    wedge,
                    wedgeType,
                    issues,
                    "45CK Foot Option Validation",
                    $"{FootOptionProperty} = {footOption}",
                    new[]
                    {
                        "GA",
                        "GD",
                        "B"
                    });
                break;

            case "LW_CG":
                RequireAllPositive(
                    wedge,
                    wedgeType,
                    issues,
                    "45CK Foot Option Validation",
                    $"{FootOptionProperty} = {footOption}",
                    new[]
                    {
                        "CGO",
                        "CGR",
                        "G"
                    });
                break;

            case "":
                AddPropertyIssue(
                    wedge,
                    wedgeType,
                    issues,
                    "45CK Foot Option Validation",
                    "Foot option is required",
                    FootOptionProperty,
                    "field is empty. Expected LW_VG or LW_CG.");
                break;

            default:
                AddPropertyIssue(
                    wedge,
                    wedgeType,
                    issues,
                    "45CK Foot Option Validation",
                    "Supported foot option",
                    FootOptionProperty,
                    $"unsupported value '{raw}'. Expected LW_VG or LW_CG.");
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
            $"'{dimensionKey}' must be > 0 because [{ruleName}] is selected.";
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