using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;
using WAD.Runner.DrawingAutomation.Wedges.ABT.Annotations;

namespace WAD.Runner.DrawingAutomation.Wedges.AB16.Annotations;

public sealed class Ab16AnnotationContextResolver : IAnnotationWedgeContextResolver
{
    public AnnotationWedgeContext Resolve(WedgeData wedge)
    {
        if (wedge is null)
            throw new ArgumentNullException(nameof(wedge));

        var footToken = ResolveFootToken(wedge);
        var feedHoleToken = ResolveFeedHoleToken(wedge);

        return new AnnotationWedgeContext
        {
            Traits = new AnnotationTraitSet(new[]
            {
                Pair(AnnotationTraitNames.WedType, Ab16AnnotationShankTypes.Std),
                Pair(AnnotationTraitNames.ShankType, Ab16AnnotationShankTypes.Std),
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
            "LW_VG" or "SW_VG" or "VG" => Ab16AnnotationFootOptions.Vg,
            "LW_CG" or "SW_CG" or "CG" => Ab16AnnotationFootOptions.Cg,
            _ => Ab16AnnotationFootOptions.Unknown
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
            return Ab16AnnotationFeedHoleTypes.Standard;
        }

        if (token.StartsWith("OVAL", StringComparison.OrdinalIgnoreCase))
            return Ab16AnnotationFeedHoleTypes.Oval;

        if (token.StartsWith("SLOT", StringComparison.OrdinalIgnoreCase))
            return Ab16AnnotationFeedHoleTypes.Slot;

        return Ab16AnnotationFeedHoleTypes.Unknown;
    }

    private static KeyValuePair<string, string> Pair(string key, string value)
        => new(key, value);
}