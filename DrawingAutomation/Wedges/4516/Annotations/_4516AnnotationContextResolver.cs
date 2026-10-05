using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

namespace WAD.Runner.DrawingAutomation.Wedges._4516.Annotations;

public sealed class _4516AnnotationContextResolver :
    IAnnotationWedgeContextResolver
{
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
                    $"Unsupported 4516 subclass '{wedge.Subclass}' " +
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
    ///     Type/Shank -> PGB-Type only.
    ///     Foot       -> Not applicable.
    ///     Feed hole  -> Not applicable.
    ///
    /// PGB must never use Wed-Type, Wed-Foot_Option
    /// or Wed-Feed_H/Slot.
    /// </summary>
    private static AnnotationWedgeContext ResolvePgbContext(
        WedgeData wedge)
    {
        var shankToken =
            ResolvePgbShankToken(
                wedge);

        var effectiveShankToken =
            string.IsNullOrWhiteSpace(shankToken)
                ? _4516AnnotationShankTypes.StandardHole
                : shankToken;

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
                            effectiveShankToken),

                        // PGB has no foot option.
                        Pair(
                            AnnotationTraitNames.FootOption,
                            _4516AnnotationFootOptions.FlatOrUnknown),

                        // PGB has no feed hole.
                        Pair(
                            AnnotationTraitNames.FeedHoleType,
                            _4516AnnotationFeedHoleTypes.Unknown)
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
    ///     Type/Shank -> Wed-Type.
    ///     Foot       -> Wed-Foot_Option.
    ///     Feed hole  -> Wed-Feed_H/Slot.
    /// </summary>
    private static AnnotationWedgeContext ResolveFgContext(
        WedgeData wedge)
    {
        var shankToken =
            ResolveFgShankToken(
                wedge);

        var effectiveShankToken =
            string.IsNullOrWhiteSpace(shankToken)
                ? _4516AnnotationShankTypes.StandardHole
                : shankToken;

        var footToken =
            ResolveFgFootToken(
                wedge);

        var feedHoleToken =
            ResolveFgFeedHoleToken(
                wedge);

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
                            effectiveShankToken),

                        Pair(
                            AnnotationTraitNames.FootOption,
                            footToken),

                        Pair(
                            AnnotationTraitNames.FeedHoleType,
                            feedHoleToken)
                    }),

            Sketches =
                SketchNameSet.Empty
        };
    }

    // ================================================================
    // PGB SHANK / TYPE
    // ================================================================

    /// <summary>
    /// Resolves the PGB shank/type using ONLY PGB-Type.
    ///
    /// There is deliberately no fallback to:
    ///     Wed-Type
    ///     Wedge-Type
    ///     wedge_type
    ///
    /// This prevents the general wedge family value "4516"
    /// from being interpreted as the shank/type trait.
    /// </summary>
    private static string ResolvePgbShankToken(
        WedgeData wedge)
    {
        return AnnotationTokenNormalizer.Normalize(
            WedgePropertyReader.GetFirstPgbPropLoose(
                wedge,
                "PGB-Type",
                "PGB_Type",
                "PGB Type"));
    }

    // ================================================================
    // FG SHANK / TYPE
    // ================================================================

    /// <summary>
    /// Resolves the FG shank/type using only FG properties.
    ///
    /// Shank_Type aliases are retained because they were already
    /// supported by the 4516 annotation resolver.
    ///
    /// Generic Wedge-Type / wedge_type are intentionally excluded.
    /// </summary>
    private static string ResolveFgShankToken(
        WedgeData wedge)
    {
        return AnnotationTokenNormalizer.Normalize(
            WedgePropertyReader.GetFirstFgPropLoose(
                wedge,
                "Wed-Type",
                "Wed_Type",
                "Wed Type",
                "Shank_Type",
                "shank_type"));
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
        WedgeData wedge)
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
                _4516AnnotationFootOptions.Vg,

            "LW_G" or
            "SW_G" or
            "G" =>
                _4516AnnotationFootOptions.G,

            "LW_C" or
            "SW_C" or
            "C" =>
                _4516AnnotationFootOptions.C,

            "LW_C_CBR" or
            "SW_C_CBR" =>
                _4516AnnotationFootOptions.CWithCbr,

            "LW_CC" or
            "SW_CC" or
            "CC" =>
                _4516AnnotationFootOptions.Cc,

            _ =>
                _4516AnnotationFootOptions.FlatOrUnknown
        };
    }

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
            return _4516AnnotationFeedHoleTypes.StandardRound;
        }

        if (token.StartsWith(
                "OVAL",
                StringComparison.OrdinalIgnoreCase))
        {
            return _4516AnnotationFeedHoleTypes.Oval;
        }

        if (token.StartsWith(
                "SLOT",
                StringComparison.OrdinalIgnoreCase))
        {
            return _4516AnnotationFeedHoleTypes.Slot;
        }

        return _4516AnnotationFeedHoleTypes.Unknown;
    }

    // ================================================================
    // COMMON
    // ================================================================

    private static KeyValuePair<string, string> Pair(
        string key,
        string value)
        => new(
            key,
            value);
}