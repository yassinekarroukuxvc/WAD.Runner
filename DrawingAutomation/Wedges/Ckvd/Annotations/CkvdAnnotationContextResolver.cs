using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

namespace WAD.Runner.DrawingAutomation.Wedges.Ckvd.Annotations;

public sealed class CkvdAnnotationContextResolver :
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
                    $"Unsupported CKVD subclass '{wedge.Subclass}' " +
                    "for annotation context resolution.")
        };
    }

    // ================================================================
    // PGB CONTEXT
    // ================================================================

    /// <summary>
    /// PGB CKVD:
    ///     Style -> PGB-Type only.
    ///
    /// Expected:
    ///     LW_STYLE_A_CKVD
    ///     LW_STYLE_B_CKVD
    ///
    /// No fallback to Wed-Type is allowed.
    /// </summary>
    private static AnnotationWedgeContext ResolvePgbContext(
        WedgeData wedge)
    {
        var styleToken =
            ResolvePgbStyleToken(
                wedge);

        ValidateStyleToken(
            styleToken,
            propertyName: "PGB-Type");

        return BuildContext(
            styleToken);
    }

    // ================================================================
    // FG CONTEXT
    // ================================================================

    /// <summary>
    /// FG CKVD:
    ///     Style -> Wed-Type only.
    ///
    /// Expected:
    ///     LW_STYLE_A_CKVD
    ///     LW_STYLE_B_CKVD
    ///
    /// No fallback to PGB-Type is allowed.
    /// </summary>
    private static AnnotationWedgeContext ResolveFgContext(
        WedgeData wedge)
    {
        var styleToken =
            ResolveFgStyleToken(
                wedge);

        ValidateStyleToken(
            styleToken,
            propertyName: "Wed-Type");

        return BuildContext(
            styleToken);
    }

    // ================================================================
    // PGB STYLE
    // ================================================================

    private static string ResolvePgbStyleToken(
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
    // FG STYLE
    // ================================================================

    private static string ResolveFgStyleToken(
        WedgeData wedge)
    {
        return AnnotationTokenNormalizer.Normalize(
            WedgePropertyReader.GetFirstFgPropLoose(
                wedge,
                "Wed-Type",
                "Wed_Type",
                "Wed Type",

                // Existing CKVD FG aliases retained.
                "Shank_Type",
                "shank_type"));
    }

    // ================================================================
    // VALIDATION
    // ================================================================

    private static void ValidateStyleToken(
        string styleToken,
        string propertyName)
    {
        if (string.Equals(
                styleToken,
                CkvdAnnotationStyles.StyleA,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(
                styleToken,
                CkvdAnnotationStyles.StyleB,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Unable to resolve the CKVD annotation style from '{propertyName}'. " +
            "Expected 'LW_STYLE_A_CKVD' or 'LW_STYLE_B_CKVD', " +
            $"but received '{Display(styleToken)}'.");
    }

    // ================================================================
    // CONTEXT
    // ================================================================

    private static AnnotationWedgeContext BuildContext(
        string styleToken)
    {
        return new AnnotationWedgeContext
        {
            Traits =
                new AnnotationTraitSet(
                    new[]
                    {
                        new KeyValuePair<string, string>(
                            AnnotationTraitNames.WedType,
                            styleToken)
                    }),

            Sketches =
                SketchNameSet.Empty
        };
    }

    // ================================================================
    // HELPERS
    // ================================================================

    private static string Display(
        string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "<missing>"
            : value;
}