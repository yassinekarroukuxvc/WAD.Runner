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
        "T"
    };

    private static readonly string[] ShortDimensions =
    {
        "TL",
        "TD",
        "TDF",
        "W",
        "ISA",
        "FL"
    };

    private static readonly PgbPropertyValidationRule FeedHoleRule =
        Property(
            "Wed-Feed_H/Slot",
            "STD",
            "Oval",
            "Slot");

    // All PGB foot-option rules accept both LW_... and SW_... forms.
    private static readonly PgbPropertyValidationRule FootVgCgRule =
        Property(
            "Wed-Foot_Option",
            "LW_VG",
            "SW_VG",
            "LW_CG",
            "SW_CG");

    private static readonly PgbPropertyValidationRule FootVgCgCAndGRule =
        Property(
            "Wed-Foot_Option",
            "LW_VG",
            "SW_VG",
            "LW_CG",
            "SW_CG",
            "LW_C",
            "SW_C",
            "LW_G",
            "SW_G");

    private static readonly PgbPropertyValidationRule FootMRule =
        Property(
            "Wed-Foot_Option",
            "LW_VG",
            "SW_VG",
            "LW_CG",
            "SW_CG",
            "LW_C",
            "SW_C",
            "LW_G",
            "SW_G",
            "LW_F",
            "SW_F");

    private static readonly PgbPropertyValidationRule Foot4516Rule =
        Property(
            "Wed-Foot_Option",
            "LW_VG",
            "SW_VG",
            "LW_CG",
            "SW_CG",
            "LW_C",
            "SW_C",
            "LW_G",
            "SW_G",
            "LW_FLAT",
            "SW_FLAT");

    private static readonly PgbPropertyValidationRule FootCobLikeRule =
        Property(
            "Wed-Foot_Option",
            "LW_VG",
            "SW_VG",
            "LW_C",
            "SW_C",
            "LW_G",
            "SW_G");

    private static readonly PgbPropertyValidationRule SwTypeRule =
        Property(
            "Wed-Type",
            "SW_180REV",
            "SW_STD");

    private static readonly PgbPropertyValidationRule CkvdTypeRule =
        Property(
            "Wed-Type",
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
                Rules(
                    StandardDimensions,
                    FeedHoleRule,
                    FootVgCgRule),

            WedgeType.AB16 =>
                Rules(
                    StandardDimensions,
                    FeedHoleRule,
                    FootVgCgRule),

            WedgeType.ABT =>
                Rules(
                    StandardDimensions,
                    FeedHoleRule,
                    FootVgCgCAndGRule,
                    SwTypeRule),

            WedgeType.M =>
                Rules(
                    StandardDimensions,
                    FeedHoleRule,
                    FootMRule,
                    SwTypeRule),

            WedgeType._4516 =>
                Rules(
                    StandardDimensions,
                    FeedHoleRule,
                    Foot4516Rule),

            WedgeType._1001 =>
                Rules(
                    StandardDimensions,
                    FeedHoleRule,
                    FootVgCgCAndGRule,
                    SwTypeRule),

            WedgeType.COB =>
                Rules(
                    StandardDimensions,
                    FeedHoleRule,
                    FootCobLikeRule,
                    SwTypeRule),

            WedgeType.UTUS =>
                Rules(
                    StandardDimensions,
                    FeedHoleRule,
                    FootCobLikeRule,
                    SwTypeRule),

            WedgeType.FP =>
                Rules(
                    StandardDimensions,
                    FeedHoleRule,
                    FootCobLikeRule,
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
