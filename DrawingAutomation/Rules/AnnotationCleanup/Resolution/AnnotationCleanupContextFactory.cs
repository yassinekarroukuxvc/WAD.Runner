using System;
using System.Collections.Generic;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

public sealed class AnnotationCleanupContextFactory
{
    public AnnotationCleanupContext Create(
        DrawingRun run,
        DrawingData drawingData,
        IDictionary<string, string>? viewNameMap,
        string logPrefix)
    {
        if (run is null)
            throw new ArgumentNullException(nameof(run));

        if (drawingData is null)
            throw new ArgumentNullException(nameof(drawingData));

        var wedge = run.Wedge;
        var profile = AnnotationCleanupProfileResolver.Resolve(run, drawingData);
        var shank = ShankTypeResolver.Resolve(wedge);
        var foot = FootOptionResolver.Resolve(wedge);
        var dimensions = DimensionFactResolver.Resolve(wedge, logPrefix);
        var viewNames = ViewNameResolver.Resolve(viewNameMap);
        var sketches = SketchNameResolver.Resolve(shank);
        var wedTypeToken = ResolveWedTypeToken(wedge);

        ValidateCkvdWedType(
            profile,
            wedTypeToken);

        Logger.Blue(
            $"[{logPrefix}.Resolve] " +
            $"Profile={profile}, " +
            $"Shank={shank}, " +
            $"Foot={foot}, " +
            $"Wed-Type={wedTypeToken}");

        return new AnnotationCleanupContext
        {
            Profile = profile,
            Shank = shank,
            Foot = foot,
            Dimensions = dimensions,
            ViewNames = viewNames,
            Sketches = sketches,
            WedTypeToken = wedTypeToken,
            KAnnotationFullName = null,
            ErdAnnotationFullName = null
        };
    }

    private static string ResolveWedTypeToken(WedgeData wedge)
        => NormalizeToken(
            WedgePropertyReader.GetFirstPropLoose(
                wedge,
                "Wed-Type",
                "Wed_Type",
                "Wed Type",
                "Shank_Type",
                "shank_type"));

    private static void ValidateCkvdWedType(
        AnnotationCleanupProfile profile,
        string wedTypeToken)
    {
        if (!IsCkvdProfile(profile))
            return;

        if (string.Equals(
                wedTypeToken,
                "LW_STYLE_A_CKVD",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                wedTypeToken,
                "LW_STYLE_B_CKVD",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidOperationException(
            "Unable to resolve the CKVD annotation style from Wed-Type. " +
            "Expected 'LW_STYLE_A_CKVD' or 'LW_STYLE_B_CKVD', " +
            $"but received '{wedTypeToken}'.");
    }

    private static bool IsCkvdProfile(
        AnnotationCleanupProfile profile)
        => profile is
            AnnotationCleanupProfile.CkvdFgProduction or
            AnnotationCleanupProfile.CkvdFgCustomer or
            AnnotationCleanupProfile.CkvdFgOverlay or
            AnnotationCleanupProfile.CkvdPgbProduction or
            AnnotationCleanupProfile.CkvdPgbOverlay;

    private static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var token = value
            .Trim()
            .Trim('\0');

        // Database fields can be returned as packed values, for example:
        // LW_STYLE_B_CKVD;;;;;;;;;;
        // Only the first semicolon-delimited field is the Wed-Type token.
        var separatorIndex = token.IndexOf(';');
        if (separatorIndex >= 0)
            token = token[..separatorIndex];

        return token
            .Trim()
            .Trim('\0')
            .ToUpperInvariant();
    }
}