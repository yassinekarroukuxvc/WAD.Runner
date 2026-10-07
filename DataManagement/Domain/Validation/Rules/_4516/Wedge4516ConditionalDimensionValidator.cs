using System;
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Validation.Rules._4516;

/// <summary>
/// Validates 4516 FG dimensions that depend on Wed-Feed_H/Slot and Wed-Foot_Option.
///
/// The database fields are authoritative:
/// - Feed-hole type comes from Wed-Feed_H/Slot.
/// - Foot option comes from Wed-Foot_Option.
/// - C with CBR is NOT a separate database foot-option token.
///   It is C with CBRL/CBRD populated.
/// </summary>
internal static class Wedge4516ConditionalDimensionValidator
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

        // These conditional rules apply only to FG.
        if (wedge.Subclass != WedgeSubclass.FG)
            return;

        ValidateFeedHoleDimensions(wedge, wedgeType, issues);
        ValidateFootOptionDimensions(wedge, wedgeType, issues);
        ValidateFootProfileInputs(wedge, wedgeType, issues);
    }

    // ============================================================
    // Feed hole
    // ============================================================

    private static void ValidateFeedHoleDimensions(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues)
    {
        var raw = WedgePropertyAccessor.ReadNormalizedToken(
            wedge,
            FeedHoleProperty,
            "Wed-Feed_H_Slot",
            "Wed Feed H Slot",
            "Wed-Feed H Slot",
            "Feed_H/Slot",
            "Feed_H_Slot",
            "Feed H Slot");

        var feedHoleType = NormalizeFeedHoleToken(raw);

        switch (feedHoleType)
        {
            case "STD":
                RequireAllPositive(
                    wedge,
                    wedgeType,
                    issues,
                    "4516 Feed Hole Validation",
                    $"{FeedHoleProperty} = STD",
                    new[] { "H" });
                break;

            case "OVAL":
                RequireAllPositive(
                    wedge,
                    wedgeType,
                    issues,
                    "4516 Feed Hole Validation",
                    $"{FeedHoleProperty} = Oval",
                    new[] { "HH", "HW" });
                break;

            case "SLOT":
                RequireAllPositive(
                    wedge,
                    wedgeType,
                    issues,
                    "4516 Feed Hole Validation",
                    $"{FeedHoleProperty} = Slot",
                    new[] { "ST", "SW" });
                break;

            case "":
                AddPropertyIssue(
                    wedge,
                    wedgeType,
                    issues,
                    "4516 Feed Hole Validation",
                    "Feed-hole type is required",
                    FeedHoleProperty,
                    "field is empty. Expected STD, Oval or Slot.");
                break;

            default:
                AddPropertyIssue(
                    wedge,
                    wedgeType,
                    issues,
                    "4516 Feed Hole Validation",
                    "Supported feed-hole type",
                    FeedHoleProperty,
                    $"unsupported value '{raw}'. Expected STD, Oval or Slot.");
                break;
        }
    }

    // ============================================================
    // Foot option
    // ============================================================

    private static void ValidateFootOptionDimensions(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues)
    {
        var raw = WedgePropertyAccessor.ReadNormalizedToken(
            wedge,
            FootOptionProperty,
            "Wed_Foot_Option",
            "Wed Foot Option",
            "Wed-Foot Option",
            "Foot_Option",
            "Foot Option");

        var footOption = NormalizeFootOptionToken(raw);

        switch (footOption)
        {
            // ----------------------------------------------------
            // VG
            // ----------------------------------------------------
            case "VG":
            case "LW_VG":
            case "SW_VG":
                RequireAllPositive(
                    wedge,
                    wedgeType,
                    issues,
                    "4516 Foot Option Validation",
                    $"{FootOptionProperty} = {footOption}",
                    new[] { "GA", "B", "GD" });
                break;

            // ----------------------------------------------------
            // C / C with CBR
            //
            // C with CBR is still stored as C.
            //
            // C:
            //   CL > 0
            //   CD > 0
            //
            // C with CBR:
            //   CL > 0
            //   CD > 0
            //   CBRL > 0
            //   CBRD > 0
            //
            // If neither CBR dimension exists, this is normal C.
            // If either one exists, both are required.
            // ----------------------------------------------------
            case "C":
            case "LW_C":
            case "SW_C":
                RequireAllPositive(
                    wedge,
                    wedgeType,
                    issues,
                    "4516 Foot Option Validation",
                    $"{FootOptionProperty} = {footOption}",
                    new[] { "CL", "CD" });

                ValidateCFootCbrDimensions(
                    wedge,
                    wedgeType,
                    issues);
                break;

            // ----------------------------------------------------
            // G
            // ----------------------------------------------------
            case "G":
            case "LW_G":
            case "SW_G":
                RequireAllPositive(
                    wedge,
                    wedgeType,
                    issues,
                    "4516 Foot Option Validation",
                    $"{FootOptionProperty} = {footOption}",
                    new[] { "GO", "GD" });
                break;

            // ----------------------------------------------------
            // CG
            //
            // This is the rule that previously lived under CC.
            // ----------------------------------------------------
            case "CG":
            case "LW_CG":
            case "SW_CG":
                RequireAllPositive(
                    wedge,
                    wedgeType,
                    issues,
                    "4516 Foot Option Validation",
                    $"{FootOptionProperty} = {footOption}",
                    new[] { "G", "CGR", "CGD" });
                break;

            // ----------------------------------------------------
            // F
            //
            // F replaces the old FLAT option.
            // There are currently no additional conditional
            // dimensions to validate here.
            // ----------------------------------------------------
            case "F":
            case "LW_F":
            case "SW_F":
                break;

            // ----------------------------------------------------
            // CC
            //
            // CC is valid only when BOTH CBR dimensions exist.
            // ----------------------------------------------------
            case "CC":
            case "LW_CC":
            case "SW_CC":
                RequireAllPositive(
                    wedge,
                    wedgeType,
                    issues,
                    "4516 Foot Option Validation",
                    $"{FootOptionProperty} = {footOption}",
                    new[] { "CBRL", "CBRD" });
                break;

            // ----------------------------------------------------
            // Missing foot option
            // ----------------------------------------------------
            case "":
                AddPropertyIssue(
                    wedge,
                    wedgeType,
                    issues,
                    "4516 Foot Option Validation",
                    "Foot option is required",
                    FootOptionProperty,
                    "field is empty. Expected VG, C, G, CG, F or CC.");
                break;

            // ----------------------------------------------------
            // Unsupported foot option
            // ----------------------------------------------------
            default:
                AddPropertyIssue(
                    wedge,
                    wedgeType,
                    issues,
                    "4516 Foot Option Validation",
                    "Supported foot option",
                    FootOptionProperty,
                    $"unsupported value '{raw}'. " +
                    "Expected VG, C, G, CG, F or CC " +
                    "(LW_... / SW_... variants are also supported).");
                break;
        }
    }

    /// <summary>
    /// Determines whether a C foot is actually C-with-CBR.
    ///
    /// C-with-CBR is not represented by another foot-option token.
    /// It is identified from CBRL/CBRD.
    ///
    /// - CBRL <= 0 and CBRD <= 0 => normal C
    /// - Either one > 0            => both must be > 0
    /// - Both > 0                  => valid C with CBR
    /// </summary>
    private static void ValidateCFootCbrDimensions(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues)
    {
        var hasCbrl = WedgeDimensionAccess.IsPositive(wedge, "CBRL");
        var hasCbrd = WedgeDimensionAccess.IsPositive(wedge, "CBRD");

        // Neither CBR dimension is active:
        // this is simply a normal C foot.
        if (!hasCbrl && !hasCbrd)
            return;

        // If one is populated, both are mandatory.
        RequireAllPositive(
            wedge,
            wedgeType,
            issues,
            "4516 CBR Validation",
            "C foot with CBR",
            new[] { "CBRL", "CBRD" });
    }

    // ============================================================
    // 4516 foot-profile inputs
    // ============================================================

    private static void ValidateFootProfileInputs(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues)
    {
        /*
         * IMPORTANT:
         *
         * Do not validate the final FRX/BRX geometry here.
         *
         * This validator does not know the drawing type and therefore
         * cannot know the effective equation values. For an overlay the
         * equation planner may replace, for example:
         *
         *     FL -> FL_MAX
         *     GD -> GD_MIN
         *     CD -> CD_MIN
         *
         * The authoritative geometry validation is therefore performed
         * inside _4516EquationPlanner AFTER all equation overrides have
         * been applied to the effective-value tracker and BEFORE FRX/BRX
         * are written to the SolidWorks equation file.
         *
         * Here we only validate that the DB/reference inputs required to
         * perform that later calculation are available in the expected
         * units. F is allowed to be zero; it is a calculation/reference
         * input and is not sent to SolidWorks.
         */
        RequireFootProfileLengthInput(
            wedge,
            wedgeType,
            issues,
            "FL",
            "FL is required for the 4516 FRX/BRX calculation.");

        RequireFootProfileLengthInput(
            wedge,
            wedgeType,
            issues,
            "F",
            "F is required in the database for the 4516 FRX/BRX calculation. " +
            "F is a reference/calculation input and is not sent to SolidWorks.");

        RequireFootProfileLengthInput(
            wedge,
            wedgeType,
            issues,
            "FR",
            "FR is required for the 4516 FRX calculation.");

        RequireFootProfileLengthInput(
            wedge,
            wedgeType,
            issues,
            "BR",
            "BR is required for the 4516 BRX calculation.");

        /*
         * Preserve the current 4516 convention:
         * missing FTA is allowed and the equation planner uses 0 deg.
         */
    }

    private static void RequireFootProfileLengthInput(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues,
        string dimensionKey,
        string message)
    {
        if (TryGetNominalLengthMm(
                wedge,
                dimensionKey,
                out _))
        {
            return;
        }

        AddFootProfileIssue(
            wedge,
            wedgeType,
            issues,
            dimensionKey,
            message);
    }

    private static bool TryGetNominalLengthMm(
        WedgeData wedge,
        string dimensionKey,
        out decimal millimeters)
    {
        millimeters = 0m;

        if (!WedgeDimensionAccess.TryGetDimension(
                wedge,
                dimensionKey,
                out var dimension) ||
            dimension is null ||
            dimension.Nominal.Unit != UnitKind.Millimeter)
        {
            return false;
        }

        millimeters =
            dimension.Nominal.AsMm();

        return true;
    }

    private static void AddFootProfileIssue(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues,
        string dimensionKey,
        string message)
    {
        Logger.Warn(
            "[4516 Foot Profile Validation] Rejected -> " +
            $"article={wedge.ArticleNumber}, " +
            $"dimension={dimensionKey}, " +
            $"reason={message}");

        issues.Add(
            new DimensionValidationIssue(
                wedge.ArticleNumber,
                wedgeType,
                "4516 Foot Profile Validation",
                "FRX / BRX calculation inputs",
                dimensionKey,
                message));
    }

    // ============================================================
    // Normalization
    // ============================================================

    private static string NormalizeFeedHoleToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var token = WedgePropertyAccessor.NormalizeDbToken(raw)
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

    private static string NormalizeFootOptionToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var token = WedgePropertyAccessor.NormalizeDbToken(raw)
            .Trim()
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

    // ============================================================
    // Validation helpers
    // ============================================================

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