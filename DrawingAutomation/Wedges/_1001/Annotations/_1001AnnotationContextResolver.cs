using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Core;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

namespace WAD.Runner.DrawingAutomation.Wedges._1001.Annotations;

public sealed class _1001AnnotationContextResolver
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
                    $"Unsupported 1001 subclass '{wedge.Subclass}' " +
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
    /// PGB must never fall back to Wed-Type, Wed-Foot_Option,
    /// Wed-Feed_H/Slot, wedge_type or Wedge-Type.
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
                ? _1001AnnotationTraitValues.True
                : _1001AnnotationTraitValues.False;

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
                            _1001AnnotationFootOptions.FlatOrUnknown),

                        // PGB has no feed hole.
                        Pair(
                            AnnotationTraitNames.FeedHoleType,
                            _1001AnnotationFeedHoleTypes.Unknown),

                        Pair(
                            _1001AnnotationTraitNames.FroEqualsFr,
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
                ? _1001AnnotationTraitValues.True
                : _1001AnnotationTraitValues.False;

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
                            _1001AnnotationTraitNames.FroEqualsFr,
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
    /// Resolves the PGB shank using ONLY PGB-Type.
    ///
    /// No FG fallback is allowed.
    /// No generic wedge_type alias is allowed because wedge_type
    /// identifies the wedge family (1001, 1007, 1300, etc.), not
    /// the STD/REV shank orientation.
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
                _1001AnnotationShankTypes.Std,

            "SW_180REV" or
            "SW_180_REV" or
            "180REV" or
            "180_REV" =>
                _1001AnnotationShankTypes.Rev,

            _ =>
                token
        };
    }

    // ================================================================
    // FG SHANK
    // ================================================================

    /// <summary>
    /// Resolves the FG shank using ONLY Wed-Type.
    ///
    /// Do not add:
    ///     Wedge-Type
    ///     Wedge_Type
    ///     wedge_type
    ///
    /// Those identify the wedge family and can contain values such as
    /// 1001, 1007, 1300 or 1005A rather than SW_STD / SW_180REV.
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
                _1001AnnotationShankTypes.Std,

            "SW_180REV" or
            "SW_180_REV" or
            "180REV" or
            "180_REV" =>
                _1001AnnotationShankTypes.Rev,

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
                _1001AnnotationFootOptions.Vg,

            "LW_G" or
            "SW_G" or
            "G" =>
                _1001AnnotationFootOptions.G,

            "LW_C" or
            "SW_C" or
            "C" =>
                HasCbr(facts)
                    ? _1001AnnotationFootOptions.CWithCbr
                    : _1001AnnotationFootOptions.C,

            "LW_F" or
            "SW_F" or
            "F" =>
                _1001AnnotationFootOptions.F,

            "LW_CC" or
            "SW_CC" or
            "CC" =>
                _1001AnnotationFootOptions.Cc,

            _ =>
                _1001AnnotationFootOptions.FlatOrUnknown
        };
    }

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
            return _1001AnnotationFeedHoleTypes.Standard;
        }

        if (token.StartsWith(
                "OVAL",
                StringComparison.OrdinalIgnoreCase))
        {
            return _1001AnnotationFeedHoleTypes.Oval;
        }

        if (token.StartsWith(
                "SLOT",
                StringComparison.OrdinalIgnoreCase))
        {
            return _1001AnnotationFeedHoleTypes.Slot;
        }

        return _1001AnnotationFeedHoleTypes.Unknown;
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