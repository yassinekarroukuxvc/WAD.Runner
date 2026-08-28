using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Core;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

namespace WAD.Runner.DrawingAutomation.Wedges._1001.Annotations;

public sealed class _1001AnnotationContextResolver : IAnnotationWedgeContextResolver
{
    private const double EqualityEpsilonMm = 1e-6;

    public AnnotationWedgeContext Resolve(
        WedgeData wedge)
    {
        if (wedge is null)
            throw new ArgumentNullException(nameof(wedge));

        var facts =
            new DrawingWedgeFacts(wedge);

        var shankToken =
            ResolveShankToken(wedge);

        var footToken =
            ResolveFootToken(
                wedge,
                facts);

        var feedHoleToken =
            ResolveFeedHoleToken(wedge);

        var froEqualsFr =
            ResolveFroEqualsFr(facts)
                ? _1001AnnotationTraitValues.True
                : _1001AnnotationTraitValues.False;

        return new AnnotationWedgeContext
        {
            Traits = new AnnotationTraitSet(new[]
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
            Sketches = SketchNameSet.Empty
        };
    }

    private static string ResolveShankToken(
        WedgeData wedge)
    {
        var token =
            AnnotationTokenNormalizer.Normalize(
                WedgePropertyReader.GetFirstPropLoose(
                    wedge,
                    "Wed-Type",
                    "Wed_Type",
                    "Wed Type",
                    "Wedge-Type",
                    "Wedge_Type",
                    "wedge_type"));

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

    private static string ResolveFootToken(
        WedgeData wedge,
        DrawingWedgeFacts facts)
    {
        var token =
            AnnotationTokenNormalizer.Normalize(
                WedgePropertyReader.GetFirstPropLoose(
                    wedge,
                    "Wed-Foot_Option",
                    "Wed_Foot_Option",
                    "Wed Foot Option",
                    "Wed-Foot Option",
                    "Foot_Option",
                    "Foot Option",
                    "FootOption",
                    "foot_option"));

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

    private static string ResolveFeedHoleToken(
        WedgeData wedge)
    {
        var token =
            AnnotationTokenNormalizer.Normalize(
                WedgePropertyReader.GetFirstPropLoose(
                    wedge,
                    "Wed-Feed_H/Slot",
                    "Wed_Feed_H_Slot",
                    "Wed Feed H Slot",
                    "Wed-Feed H Slot",
                    "Feed_H/Slot",
                    "Feed_H_Slot",
                    "Feed H Slot",
                    "feed_h_slot"));

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