using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Validation.Rules.OSG7;

internal static class Osg7CrossDimensionValidator
{
    private const decimal DimensionComparisonTolerance =
        0.000001m;

    public static void Validate(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues)
    {
        if (!WedgeDimensionAccess.TryGetPositiveValue(
                wedge,
                "FRX",
                out var frx) ||
            !WedgeDimensionAccess.TryGetPositiveValue(
                wedge,
                "BRX",
                out var brx) ||
            !WedgeDimensionAccess.TryGetPositiveValue(
                wedge,
                "FL",
                out var fl) ||
            !WedgeDimensionAccess.TryGetPositiveValue(
                wedge,
                "F",
                out var f))
        {
            return;
        }

        var expectedBrx = fl - f - frx;
        var expectedFrx = fl - f - brx;

        var brxIsValid =
            AreApproximatelyEqual(brx, expectedBrx);

        var frxIsValid =
            AreApproximatelyEqual(frx, expectedFrx);

        if (brxIsValid && frxIsValid)
            return;

        var failedRules = new List<string>();

        if (!brxIsValid)
        {
            failedRules.Add(
                $"BRX is {brx}, but FL - F - FRX = " +
                $"{fl} - {f} - {frx} = {expectedBrx}");
        }

        if (!frxIsValid)
        {
            failedRules.Add(
                $"FRX is {frx}, but FL - F - BRX = " +
                $"{fl} - {f} - {brx} = {expectedFrx}");
        }

        issues.Add(
            new DimensionValidationIssue(
                wedge.ArticleNumber,
                wedgeType,
                "OSG7 Cross-Dimension Validation",
                "FRX / BRX relationship",
                "FRX, BRX",
                "invalid FRX/BRX relationship. When both FRX and " +
                "BRX are > 0, BRX = FL - F - FRX and " +
                "FRX = FL - F - BRX must both be satisfied. " +
                $"Actual values: FL={fl}, F={f}, " +
                $"FRX={frx}, BRX={brx}. " +
                $"Details: {string.Join("; ", failedRules)}."));
    }

    private static bool AreApproximatelyEqual(
        decimal actual,
        decimal expected)
    {
        return Math.Abs(actual - expected)
               <= DimensionComparisonTolerance;
    }
}
