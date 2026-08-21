using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

namespace WAD.Runner.DrawingAutomation.Wedges._45CK.Annotations;

public sealed class _45CKAnnotationContextResolver :
    IAnnotationWedgeContextResolver
{
    public AnnotationWedgeContext Resolve(
        WedgeData wedge)
    {
        if (wedge is null)
            throw new ArgumentNullException(nameof(wedge));

        var footToken =
            ResolveFootToken(wedge);

        var feedHoleToken =
            ResolveFeedHoleToken(wedge);

        return new AnnotationWedgeContext
        {
            Traits = new AnnotationTraitSet(new[]
            {
                Pair(
                    AnnotationTraitNames.WedType,
                    _45CKAnnotationShankTypes.Std),

                Pair(
                    AnnotationTraitNames.ShankType,
                    _45CKAnnotationShankTypes.Std),

                Pair(
                    AnnotationTraitNames.FootOption,
                    footToken),

                Pair(
                    AnnotationTraitNames.FeedHoleType,
                    feedHoleToken)
            }),

            Sketches = SketchNameSet.Empty
        };
    }

    private static string ResolveFootToken(
        WedgeData wedge)
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
                _45CKAnnotationFootOptions.Vg,

            "LW_CG" or
            "SW_CG" or
            "CG" =>
                _45CKAnnotationFootOptions.Cg,

            _ =>
                _45CKAnnotationFootOptions.Unknown
        };
    }

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
            return _45CKAnnotationFeedHoleTypes.Standard;
        }

        if (token.StartsWith(
                "OVAL",
                StringComparison.OrdinalIgnoreCase))
        {
            return _45CKAnnotationFeedHoleTypes.Oval;
        }

        if (token.StartsWith(
                "SLOT",
                StringComparison.OrdinalIgnoreCase))
        {
            return _45CKAnnotationFeedHoleTypes.Slot;
        }

        return _45CKAnnotationFeedHoleTypes.Unknown;
    }

    private static KeyValuePair<string, string> Pair(
        string key,
        string value)
        => new(key, value);
}