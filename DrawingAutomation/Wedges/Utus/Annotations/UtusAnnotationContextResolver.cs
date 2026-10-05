using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Core;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

namespace WAD.Runner.DrawingAutomation.Wedges.Utus.Annotations;

public sealed class UtusAnnotationContextResolver
    : IAnnotationWedgeContextResolver
{
    private const double EqualityEpsilonMm = 1e-6;

    public AnnotationWedgeContext Resolve(
        WedgeData wedge)
    {
        if (wedge is null)
            throw new ArgumentNullException(nameof(wedge));

        return wedge.Subclass switch
        {
            WedgeSubclass.PGB =>
                ResolvePgbContext(wedge),

            WedgeSubclass.FG =>
                ResolveFgContext(wedge),

            _ =>
                throw new InvalidOperationException(
                    $"Unsupported UTUS subclass '{wedge.Subclass}' " +
                    "for annotation context resolution.")
        };
    }

    // ================================================================
    // PGB
    // ================================================================

    /// <summary>
    /// PGB annotation context.
    ///
    /// PGB rules:
    /// - shank comes ONLY from PGB-Type
    /// - no foot-option property
    /// - no feed-hole property
    /// </summary>
    private static AnnotationWedgeContext ResolvePgbContext(
        WedgeData wedge)
    {
        var facts =
            new DrawingWedgeFacts(wedge);

        var shankToken =
            ResolvePgbShankToken(
                wedge);

        var froEqualsFr =
            ResolveFroEqualsFr(facts)
                ? UtusAnnotationTraitValues.True
                : UtusAnnotationTraitValues.False;

        return new AnnotationWedgeContext
        {
            Traits =
                new AnnotationTraitSet(
                    new[]
                    {
                        Pair(
                            AnnotationTraitNames.WedType,
                            shankToken),

                        Pair(
                            AnnotationTraitNames.ShankType,
                            shankToken),

                        // PGB has no foot option.
                        Pair(
                            AnnotationTraitNames.FootOption,
                            UtusAnnotationFootOptions.FlatOrUnknown),

                        // PGB has no feed hole.
                        Pair(
                            AnnotationTraitNames.FeedHoleType,
                            UtusAnnotationFeedHoleTypes.Unknown),

                        Pair(
                            UtusAnnotationTraitNames.FroEqualsFr,
                            froEqualsFr)
                    }),

            Sketches =
                SketchNameSet.Empty
        };
    }

    // ================================================================
    // FG
    // ================================================================

    /// <summary>
    /// FG annotation context.
    ///
    /// FG rules:
    /// - shank comes ONLY from Wed-Type
    /// - foot option comes from Wed-Foot_Option
    /// - feed hole comes from Wed-Feed_H/Slot
    /// </summary>
    private static AnnotationWedgeContext ResolveFgContext(
        WedgeData wedge)
    {
        var facts =
            new DrawingWedgeFacts(wedge);

        var shankToken =
            ResolveFgShankToken(
                wedge);

        var footToken =
            ResolveFgFootToken(
                wedge,
                facts);

        var feedHoleToken =
            ResolveFgFeedHoleToken(
                wedge);

        var froEqualsFr =
            ResolveFroEqualsFr(facts)
                ? UtusAnnotationTraitValues.True
                : UtusAnnotationTraitValues.False;

        return new AnnotationWedgeContext
        {
            Traits =
                new AnnotationTraitSet(
                    new[]
                    {
                        Pair(
                            AnnotationTraitNames.WedType,
                            shankToken),

                        Pair(
                            AnnotationTraitNames.ShankType,
                            shankToken),

                        Pair(
                            AnnotationTraitNames.FootOption,
                            footToken),

                        Pair(
                            AnnotationTraitNames.FeedHoleType,
                            feedHoleToken),

                        Pair(
                            UtusAnnotationTraitNames.FroEqualsFr,
                            froEqualsFr)
                    }),

            Sketches =
                SketchNameSet.Empty
        };
    }

    // ================================================================
    // PGB SHANK
    // ================================================================

    private static string ResolvePgbShankToken(
        WedgeData wedge)
    {
        var token =
            AnnotationTokenNormalizer.Normalize(
                WedgePropertyReader.GetFirstPgbPropLoose(
                    wedge,
                    "PGB-Type",
                    "PGB_Type",
                    "PGB Type"));

        return token switch
        {
            "SW_STD" or
            "STD" =>
                UtusAnnotationShankTypes.Std,

            "SW_180REV" or
            "SW_180_REV" or
            "180REV" or
            "180_REV" =>
                UtusAnnotationShankTypes.Rev,

            _ =>
                token
        };
    }

    // ================================================================
    // FG SHANK
    // ================================================================

    private static string ResolveFgShankToken(
        WedgeData wedge)
    {
        var token =
            AnnotationTokenNormalizer.Normalize(
                WedgePropertyReader.GetFirstFgPropLoose(
                    wedge,
                    "Wed-Type",
                    "Wed_Type",
                    "Wed Type"));

        return token switch
        {
            "SW_STD" or
            "STD" =>
                UtusAnnotationShankTypes.Std,

            "SW_180REV" or
            "SW_180_REV" or
            "180REV" or
            "180_REV" =>
                UtusAnnotationShankTypes.Rev,

            _ =>
                token
        };
    }

    // ================================================================
    // FG FOOT OPTION
    // ================================================================

    private static string ResolveFgFootToken(
        WedgeData wedge,
        DrawingWedgeFacts facts)
    {
        var token =
            AnnotationTokenNormalizer.Normalize(
                WedgePropertyReader.GetFgFootOptionProp(
                    wedge));

        return token switch
        {
            "LW_VG" or
            "SW_VG" or
            "VG" =>
                UtusAnnotationFootOptions.Vg,

            "LW_G" or
            "SW_G" or
            "G" =>
                UtusAnnotationFootOptions.G,

            "LW_C" or
            "SW_C" or
            "C" =>
                HasCbr(facts)
                    ? UtusAnnotationFootOptions.CWithCbr
                    : UtusAnnotationFootOptions.C,

            _ =>
                UtusAnnotationFootOptions.FlatOrUnknown
        };
    }

    private static bool HasCbr(
        DrawingWedgeFacts facts)
        => facts.HasPositiveLength("CBRL") &&
           facts.HasPositiveLength("CBRD");

    // ================================================================
    // FG FEED HOLE
    // ================================================================

    private static string ResolveFgFeedHoleToken(
        WedgeData wedge)
    {
        var token =
            AnnotationTokenNormalizer.Normalize(
                WedgePropertyReader.GetFgFeedHoleProp(
                    wedge));

        if (token.StartsWith(
                "STD",
                StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith(
                "STANDARD",
                StringComparison.OrdinalIgnoreCase))
        {
            return UtusAnnotationFeedHoleTypes.Standard;
        }

        if (token.StartsWith(
                "OVAL",
                StringComparison.OrdinalIgnoreCase))
        {
            return UtusAnnotationFeedHoleTypes.Oval;
        }

        if (token.StartsWith(
                "SLOT",
                StringComparison.OrdinalIgnoreCase))
        {
            return UtusAnnotationFeedHoleTypes.Slot;
        }

        return UtusAnnotationFeedHoleTypes.Unknown;
    }

    // ================================================================
    // COMMON
    // ================================================================

    private static bool ResolveFroEqualsFr(
        DrawingWedgeFacts facts)
    {
        if (!facts.TryGetLengthMm(
                "FRO",
                out var froMm) ||
            !facts.TryGetLengthMm(
                "FR",
                out var frMm))
        {
            return false;
        }

        return Math.Abs(
                   froMm - frMm) <=
               EqualityEpsilonMm;
    }

    private static KeyValuePair<string, string> Pair(
        string key,
        string value)
        => new(
            key,
            value);
}