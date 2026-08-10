using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

namespace WAD.Runner.DrawingAutomation.Wedges.Ckvd.Annotations;

public sealed class CkvdAnnotationContextResolver :
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

        if (!string.Equals(
                wedTypeToken,
                CkvdAnnotationStyles.StyleA,
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                wedTypeToken,
                CkvdAnnotationStyles.StyleB,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Unable to resolve the CKVD annotation style from Wed-Type. " +
                "Expected 'LW_STYLE_A_CKVD' or 'LW_STYLE_B_CKVD', " +
                $"but received '{Display(wedTypeToken)}'.");
        }

        return new AnnotationWedgeContext
        {
            Traits = new AnnotationTraitSet(new[]
            {
                new KeyValuePair<string, string>(
                    AnnotationTraitNames.WedType,
                    wedTypeToken)
            }),
            Sketches = SketchNameSet.Empty
        };
    }

    private static string Display(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "<missing>"
            : value;
}
