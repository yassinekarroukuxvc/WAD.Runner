using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

namespace WAD.Runner.DrawingAutomation.Wedges._4516.Annotations;

public sealed class _4516AnnotationContextResolver :
    IAnnotationWedgeContextResolver
{
    public AnnotationWedgeContext Resolve(WedgeData wedge)
    {
        if (wedge is null)
            throw new ArgumentNullException(nameof(wedge));

        var wedTypeToken = AnnotationTokenNormalizer.Normalize(
            WedgePropertyReader.GetFirstPropLoose(
                wedge,
                "Wed-Type",
                "Wed_Type",
                "Wed Type",
                "Shank_Type",
                "shank_type"));

        var footToken = ResolveFootToken(wedge);
        var feedHoleToken = ResolveFeedHoleToken(wedge);

        return new AnnotationWedgeContext
        {
            Traits = new AnnotationTraitSet(new[]
            {
                Pair(AnnotationTraitNames.WedType, wedTypeToken),
                Pair(
                    AnnotationTraitNames.ShankType,
                    string.IsNullOrWhiteSpace(wedTypeToken)
                        ? _4516AnnotationShankTypes.StandardHole
                        : wedTypeToken),
                Pair(AnnotationTraitNames.FootOption, footToken),
                Pair(AnnotationTraitNames.FeedHoleType, feedHoleToken)
            }),
            Sketches = SketchNameSet.Empty
        };
    }

    private static string ResolveFootToken(WedgeData wedge)
    {
        var token = AnnotationTokenNormalizer.Normalize(
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
            "LW_VG" or "SW_VG" =>
                _4516AnnotationFootOptions.Vg,

            "LW_G" or "SW_G" =>
                _4516AnnotationFootOptions.G,

            "LW_C" or "SW_C" =>
                _4516AnnotationFootOptions.C,

            "LW_C_CBR" or "SW_C_CBR" =>
                _4516AnnotationFootOptions.CWithCbr,

            "LW_CC" or "SW_CC" =>
                _4516AnnotationFootOptions.Cc,

            _ =>
                _4516AnnotationFootOptions.FlatOrUnknown
        };
    }

    private static string ResolveFeedHoleToken(WedgeData wedge)
    {
        var token = AnnotationTokenNormalizer.Normalize(
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

        if (token.StartsWith("STD", StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith("STANDARD", StringComparison.OrdinalIgnoreCase))
        {
            return _4516AnnotationFeedHoleTypes.StandardRound;
        }

        if (token.StartsWith("OVAL", StringComparison.OrdinalIgnoreCase))
            return _4516AnnotationFeedHoleTypes.Oval;

        if (token.StartsWith("SLOT", StringComparison.OrdinalIgnoreCase))
            return _4516AnnotationFeedHoleTypes.Slot;

        return _4516AnnotationFeedHoleTypes.Unknown;
    }

    private static KeyValuePair<string, string> Pair(
        string key,
        string value)
        => new(key, value);
}
