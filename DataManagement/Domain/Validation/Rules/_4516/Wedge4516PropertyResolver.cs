using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Validation.Rules._4516;

/// <summary>
/// Resolves 4516 classification properties from positive dimensions.
///
/// The resolver only infers a value when the corresponding database
/// field is empty. Existing packed values such as LW_C;;;;;;;; are
/// normalized and written back as LW_C.
/// </summary>
internal static class Wedge4516PropertyResolver
{
    private const string FeedHoleProperty =
        "Wed-Feed_H/Slot";

    private const string FootOptionProperty =
        "Wed-Foot_Option";

    public static IReadOnlyList<DimensionValidationIssue>
        ResolveAndApply(
            WedgeData wedge,
            WedgeType wedgeType)
    {
        if (wedge is null)
            throw new ArgumentNullException(nameof(wedge));

        var issues =
            new List<DimensionValidationIssue>();

        ResolveFeedHoleType(
            wedge,
            wedgeType,
            issues);

        ResolveFootOption(
            wedge,
            wedgeType,
            issues);

        return issues;
    }

    private static void ResolveFeedHoleType(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues)
    {
        var existing =
            WedgePropertyAccessor.ReadNormalizedToken(
                wedge,
                FeedHoleProperty,
                "Wed-Feed_H_Slot",
                "Wed Feed H Slot",
                "Feed_H/Slot",
                "Feed H Slot");

        if (!string.IsNullOrWhiteSpace(existing))
        {
            var canonicalExisting =
                CanonicalizeFeedHoleToken(existing);

            TryWriteProperty(
                wedge,
                wedgeType,
                issues,
                FeedHoleProperty,
                canonicalExisting,
                "Existing feed-hole token normalization");

            return;
        }

        var matches =
            new List<ResolvedValue>();

        if (HasAllPositive(wedge, "H"))
        {
            matches.Add(
                new ResolvedValue(
                    "STD",
                    "H"));
        }

        if (HasAllPositive(wedge, "HH", "HW"))
        {
            matches.Add(
                new ResolvedValue(
                    "Oval",
                    "HH + HW"));
        }

        if (HasAllPositive(wedge, "ST", "SW"))
        {
            matches.Add(
                new ResolvedValue(
                    "Slot",
                    "ST + SW"));
        }

        if (matches.Count == 0)
        {
            issues.Add(
                NewIssue(
                    wedge,
                    wedgeType,
                    "4516 Feed Hole Resolution",
                    "Infer Wed-Feed_H/Slot",
                    FeedHoleProperty,
                    "field is empty and no feed-hole rule matched. " +
                    "Expected H for STD, HH and HW for Oval, or " +
                    "ST and SW for Slot."));

            return;
        }

        if (matches.Count > 1)
        {
            issues.Add(
                NewIssue(
                    wedge,
                    wedgeType,
                    "4516 Feed Hole Resolution",
                    "Infer Wed-Feed_H/Slot",
                    FeedHoleProperty,
                    "field is empty but multiple feed-hole rules " +
                    $"matched: {FormatMatches(matches)}. " +
                    "The value was not inferred because the data is " +
                    "ambiguous."));

            return;
        }

        TryWriteProperty(
            wedge,
            wedgeType,
            issues,
            FeedHoleProperty,
            matches[0].Value,
            $"Inferred from {matches[0].Evidence}");
    }

    private static void ResolveFootOption(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues)
    {
        var existing =
            WedgePropertyAccessor.ReadNormalizedToken(
                wedge,
                FootOptionProperty,
                "Wed_Foot_Option",
                "Wed Foot Option",
                "Foot_Option",
                "Foot Option");

        if (!string.IsNullOrWhiteSpace(existing))
        {
            var canonicalExisting =
                existing
                    .Trim()
                    .Replace('-', '_')
                    .Replace(' ', '_')
                    .ToUpperInvariant();

            TryWriteProperty(
                wedge,
                wedgeType,
                issues,
                FootOptionProperty,
                canonicalExisting,
                "Existing foot-option token normalization");

            return;
        }

        var hasCCbr =
            HasAllPositive(
                wedge,
                "ICL",
                "CD",
                "CBRL",
                "CBRD");

        var matches =
            new List<ResolvedValue>();

        // Evaluate the more specific C + CBR case before plain C.
        if (hasCCbr)
        {
            matches.Add(
                new ResolvedValue(
                    "LW_C_CBR",
                    "ICL + CD + CBRL + CBRD"));
        }

        if (HasAllPositive(wedge, "GA", "B", "GD"))
        {
            matches.Add(
                new ResolvedValue(
                    "LW_VG",
                    "GA + B + GD"));
        }

        if (!hasCCbr &&
            HasAllPositive(wedge, "CL", "CD"))
        {
            matches.Add(
                new ResolvedValue(
                    "LW_C",
                    "CL + CD"));
        }

        if (HasAllPositive(wedge, "IGO", "GD"))
        {
            matches.Add(
                new ResolvedValue(
                    "LW_G",
                    "IGO + GD"));
        }

        if (HasAllPositive(wedge, "G", "CGR", "CGD"))
        {
            matches.Add(
                new ResolvedValue(
                    "LW_CC",
                    "G + CGR + CGD"));
        }

        if (matches.Count == 0)
        {
            TryWriteProperty(
                wedge,
                wedgeType,
                issues,
                FootOptionProperty,
                "LW_FLAT",
                "No C, C_CBR, VG, G, or CC rule matched");

            return;
        }

        if (matches.Count > 1)
        {
            issues.Add(
                NewIssue(
                    wedge,
                    wedgeType,
                    "4516 Foot Option Resolution",
                    "Infer Wed-Foot_Option",
                    FootOptionProperty,
                    "field is empty but multiple foot-option rules " +
                    $"matched: {FormatMatches(matches)}. " +
                    "The value was not inferred because the data is " +
                    "ambiguous."));

            return;
        }

        TryWriteProperty(
            wedge,
            wedgeType,
            issues,
            FootOptionProperty,
            matches[0].Value,
            $"Inferred from {matches[0].Evidence}");
    }

    private static bool HasAllPositive(
        WedgeData wedge,
        params string[] dimensions)
    {
        return dimensions.All(
            dimension => WedgeDimensionAccess.IsPositive(
                wedge,
                dimension));
    }

    private static void TryWriteProperty(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues,
        string propertyName,
        string value,
        string ruleDescription)
    {
        if (WedgePropertyAccessor.TrySet(
                wedge,
                propertyName,
                value,
                out var failureReason))
        {
            return;
        }

        issues.Add(
            NewIssue(
                wedge,
                wedgeType,
                "4516 Property Assignment",
                ruleDescription,
                propertyName,
                $"resolved value '{value}', but it could not be " +
                $"written to WedgeData. {failureReason}"));
    }

    private static DimensionValidationIssue NewIssue(
        WedgeData wedge,
        WedgeType wedgeType,
        string requirementType,
        string ruleName,
        string dimension,
        string message)
    {
        return new DimensionValidationIssue(
            wedge.ArticleNumber,
            wedgeType,
            requirementType,
            ruleName,
            dimension,
            message);
    }

    private static string CanonicalizeFeedHoleToken(
        string token)
    {
        var normalized =
            WedgePropertyAccessor.NormalizeDbToken(token)
                .Trim()
                .ToUpperInvariant();

        if (normalized.StartsWith(
                "STD",
                StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(
                "STANDARD",
                StringComparison.OrdinalIgnoreCase))
        {
            return "STD";
        }

        if (normalized.StartsWith(
                "OVAL",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Oval";
        }

        if (normalized.StartsWith(
                "SLOT",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Slot";
        }

        return WedgePropertyAccessor.NormalizeDbToken(token);
    }

    private static string FormatMatches(
        IEnumerable<ResolvedValue> matches)
    {
        return string.Join(
            ", ",
            matches.Select(
                match =>
                    $"{match.Value} ({match.Evidence})"));
    }

    private sealed record ResolvedValue(
        string Value,
        string Evidence);
}