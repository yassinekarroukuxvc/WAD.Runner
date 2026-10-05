using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Core;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

namespace WAD.Runner.DrawingAutomation.Wedges.M.Annotations;

public sealed class MAnnotationContextResolver :
    IAnnotationWedgeContextResolver
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
                    $"Unsupported M subclass '{wedge.Subclass}' " +
                    "for annotation context resolution.")
        };
    }

    // ================================================================
    // PGB CONTEXT
    // ================================================================

    /// <summary>
    /// PGB annotation context.
    ///
    /// PGB:
    ///     Shank      -> PGB-Type only.
    ///     Foot       -> Not applicable.
    ///     Feed hole  -> Not applicable.
    ///
    /// PGB must never resolve these traits from:
    ///     Wed-Type
    ///     Wed-Foot_Option
    ///     Wed-Feed_H/Slot
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
                ? MAnnotationTraitValues.True
                : MAnnotationTraitValues.False;

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
                            MAnnotationFootOptions.FlatOrUnknown),

                        // PGB has no feed hole.
                        Pair(
                            AnnotationTraitNames.FeedHoleType,
                            MAnnotationFeedHoleTypes.Unknown),

                        Pair(
                            MAnnotationTraitNames.FroEqualsFr,
                            froEqualsFr)
                    }),

            Sketches =
                SketchNameSet.Empty
        };
    }

    // ================================================================
    // FG CONTEXT
    // ================================================================

    /// <summary>
    /// FG annotation context.
    ///
    /// FG:
    ///     Shank      -> Wed-Type.
    ///     Foot       -> Wed-Foot_Option.
    ///     Feed hole  -> Wed-Feed_H/Slot.
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
                ? MAnnotationTraitValues.True
                : MAnnotationTraitValues.False;

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
                            MAnnotationTraitNames.FroEqualsFr,
                            froEqualsFr)
                    }),

            Sketches =
                SketchNameSet.Empty
        };
    }

    // ================================================================
    // PGB SHANK
    // ================================================================

    /// <summary>
    /// Resolves PGB shank orientation using ONLY PGB-Type.
    ///
    /// No fallback to:
    ///     Wed-Type
    ///     Wedge-Type
    ///     Wedge_Type
    ///     wedge_type
    /// </summary>
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
                MAnnotationShankTypes.Std,

            "SW_180REV" or
            "SW_180_REV" or
            "180REV" or
            "180_REV" =>
                MAnnotationShankTypes.Rev,

            _ =>
                token
        };
    }

    // ================================================================
    // FG SHANK
    // ================================================================

    /// <summary>
    /// Resolves FG shank orientation using ONLY Wed-Type.
    ///
    /// Wedge-Type / wedge_type are deliberately excluded because
    /// they identify the wedge family "M", not the STD/REV shank.
    /// </summary>
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
                MAnnotationShankTypes.Std,

            "SW_180REV" or
            "SW_180_REV" or
            "180REV" or
            "180_REV" =>
                MAnnotationShankTypes.Rev,

            _ =>
                token
        };
    }

    // ================================================================
    // FG FOOT OPTION
    // ================================================================

    /// <summary>
    /// Resolves the FG foot option.
    ///
    /// PGB never calls this method.
    ///
    /// M supports:
    ///     C
    ///     C with CBR
    ///     G
    ///     VG
    ///     F
    ///     CC
    /// </summary>
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
                MAnnotationFootOptions.Vg,

            "LW_G" or
            "SW_G" or
            "G" =>
                MAnnotationFootOptions.G,

            "LW_C" or
            "SW_C" or
            "C" =>
                HasCbr(facts)
                    ? MAnnotationFootOptions.CWithCbr
                    : MAnnotationFootOptions.C,

            "LW_F" or
            "SW_F" or
            "F" =>
                MAnnotationFootOptions.F,

            "LW_CC" or
            "SW_CC" or
            "CC" =>
                MAnnotationFootOptions.Cc,

            _ =>
                MAnnotationFootOptions.FlatOrUnknown
        };
    }

    /// <summary>
    /// There is no separate C-with-CBR foot-option token.
    ///
    /// A normal C foot becomes C-with-CBR when both
    /// CBRL and CBRD are positive.
    /// </summary>
    private static bool HasCbr(
        DrawingWedgeFacts facts)
        => facts.HasPositiveLength("CBRL") &&
           facts.HasPositiveLength("CBRD");

    // ================================================================
    // FG FEED HOLE
    // ================================================================

    /// <summary>
    /// Resolves the FG feed-hole type.
    ///
    /// PGB never calls this method.
    /// </summary>
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
            return MAnnotationFeedHoleTypes.Standard;
        }

        if (token.StartsWith(
                "OVAL",
                StringComparison.OrdinalIgnoreCase))
        {
            return MAnnotationFeedHoleTypes.Oval;
        }

        if (token.StartsWith(
                "SLOT",
                StringComparison.OrdinalIgnoreCase))
        {
            return MAnnotationFeedHoleTypes.Slot;
        }

        return MAnnotationFeedHoleTypes.Unknown;
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
                   froMm -
                   frMm) <=
               EqualityEpsilonMm;
    }

    private static KeyValuePair<string, string> Pair(
        string key,
        string value)
        => new(
            key,
            value);
}