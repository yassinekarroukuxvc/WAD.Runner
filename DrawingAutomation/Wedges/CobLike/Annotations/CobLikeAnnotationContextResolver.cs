using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

namespace WAD.Runner.DrawingAutomation.Wedges.CobLike.Annotations;

public sealed class CobLikeAnnotationContextResolver :
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

        var shankToken = ResolveShankToken(wedTypeToken);
        var footToken = ResolveFootToken(wedge);

        return new AnnotationWedgeContext
        {
            Traits = new AnnotationTraitSet(new[]
            {
                Pair(AnnotationTraitNames.WedType, wedTypeToken),
                Pair(AnnotationTraitNames.ShankType, shankToken),
                Pair(AnnotationTraitNames.FootOption, footToken)
            }),
            Sketches = ResolveSketches(shankToken)
        };
    }

    private static string ResolveShankToken(string wedTypeToken)
        => wedTypeToken.Contains("180", StringComparison.OrdinalIgnoreCase) ||
           wedTypeToken.Contains("REV", StringComparison.OrdinalIgnoreCase)
            ? CobLikeAnnotationShankTypes.Reverse180
            : CobLikeAnnotationShankTypes.Standard;

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

        var resolved = token switch
        {
            "LW_C" or "SW_C" =>
                CobLikeAnnotationFootOptions.C,

            "LW_G" or "SW_G" =>
                CobLikeAnnotationFootOptions.G,

            "LW_VG" or "SW_VG" =>
                CobLikeAnnotationFootOptions.VG,

            "LW_CG" or "SW_CG" =>
                CobLikeAnnotationFootOptions.CG,

            "LW_CC" or "SW_CC" =>
                CobLikeAnnotationFootOptions.CC,

            "LW_C_CBR" or "SW_C_CBR" =>
                CobLikeAnnotationFootOptions.CWithCbr,

            _ =>
                CobLikeAnnotationFootOptions.None
        };

        if (resolved == CobLikeAnnotationFootOptions.C &&
            DimensionFactResolver.IsDimensionPositive(wedge, "CBRA") &&
            DimensionFactResolver.IsDimensionPositive(wedge, "CBRL") &&
            DimensionFactResolver.IsDimensionPositive(wedge, "CBRD"))
        {
            return CobLikeAnnotationFootOptions.CWithCbr;
        }

        return resolved;
    }

    private static SketchNameSet ResolveSketches(string shankToken)
    {
        if (string.Equals(
                shankToken,
                CobLikeAnnotationShankTypes.Reverse180,
                StringComparison.OrdinalIgnoreCase))
        {
            return new SketchNameSet
            {
                FrontSketch = "ANNOT_180_DEG_REV_FRONT_sketch",
                TopSketch = "ANNOT_180_DEG_REV_TOP_sketch",
                FrBrSketch = "ANNOT_FR_BR_180_DEG_REV_FRONT_sketch"
            };
        }

        return new SketchNameSet
        {
            FrontSketch = "ANNOT_STD_FRONT_sketch",
            TopSketch = "ANNOT_STD_TOP_sketch",
            FrBrSketch = "ANNOT_FR_BR_STD_FRONT_sketch"
        };
    }

    private static KeyValuePair<string, string> Pair(
        string key,
        string value)
        => new(key, value);
}
