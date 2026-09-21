using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Validation;

internal sealed class PgbPropertyValidationRule
{
    public string PropertyName { get; }

    public IReadOnlyList<string> AllowedValues { get; }

    public PgbPropertyValidationRule(
        string propertyName,
        params string[] allowedValues)
    {
        PropertyName = propertyName;
        AllowedValues = allowedValues ?? Array.Empty<string>();
    }
}

internal sealed class PgbValidationRuleSet
{
    public static PgbValidationRuleSet Empty { get; }
        = new();

    public IReadOnlyList<string> RequiredDimensions { get; init; }
        = Array.Empty<string>();

    public IReadOnlyList<PgbPropertyValidationRule> PropertyRules { get; init; }
        = Array.Empty<PgbPropertyValidationRule>();
}

internal static class PgbValidationRuleCatalog
{
    private static readonly string[] StandardDimensions =
    {
        "TL",
        "TD",
        "TDF",
        "W",
        "ISA",
        "FL",
        "RA",
        "BA",
        "T"
    };

    private static readonly string[] ShortDimensions =
    {
        "TL",
        "TD",
        "TDF",
        "W",
        "ISA",
        "FL",
        "RA",
        "BA"
    };

    // PGB uses PGB-Type. Wed-Type is reserved for FG.
    private static readonly PgbPropertyValidationRule SwTypeRule =
        Property(
            "PGB-Type",
            "SW_180REV",
            "SW_STD");

    private static readonly PgbPropertyValidationRule CkvdTypeRule =
        Property(
            "PGB-Type",
            "LW_STYLE_B_CKVD",
            "LW_STYLE_A_CKVD");

    public static PgbValidationRuleSet For(
        WedgeType wedgeType)
    {
        return wedgeType switch
        {
            WedgeType.CKVD =>
                Rules(
                    ShortDimensions,
                    CkvdTypeRule),

            WedgeType._45CK =>
                Rules(StandardDimensions),

            WedgeType.AB16 =>
                Rules(StandardDimensions),

            WedgeType.ABT =>
                Rules(
                    StandardDimensions,
                    SwTypeRule),

            WedgeType.M =>
                Rules(
                    StandardDimensions,
                    SwTypeRule),

            WedgeType._4516 =>
                Rules(StandardDimensions),

            WedgeType._1001 =>
                Rules(
                    StandardDimensions,
                    SwTypeRule),

            WedgeType.COB =>
                Rules(
                    StandardDimensions,
                    SwTypeRule),

            WedgeType.UTUS =>
                Rules(
                    StandardDimensions,
                    SwTypeRule),

            WedgeType.FP =>
                Rules(
                    StandardDimensions,
                    SwTypeRule),

            WedgeType.OSG7 =>
                Rules(ShortDimensions),

            _ => PgbValidationRuleSet.Empty
        };
    }

    private static PgbValidationRuleSet Rules(
        IReadOnlyList<string> requiredDimensions,
        params PgbPropertyValidationRule[] propertyRules)
    {
        return new PgbValidationRuleSet
        {
            RequiredDimensions = requiredDimensions,
            PropertyRules = propertyRules ?? Array.Empty<PgbPropertyValidationRule>()
        };
    }

    private static PgbPropertyValidationRule Property(
        string propertyName,
        params string[] allowedValues)
        => new(propertyName, allowedValues);
}
