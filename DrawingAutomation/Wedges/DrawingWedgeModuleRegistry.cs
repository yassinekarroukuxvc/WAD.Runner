using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;
using WAD.Runner.DrawingAutomation.Wedges._4516;
using WAD.Runner.DrawingAutomation.Wedges.ABT;
using WAD.Runner.DrawingAutomation.Wedges.AB16;
using WAD.Runner.DrawingAutomation.Wedges.Ckvd;
using WAD.Runner.DrawingAutomation.Wedges.Cob;
using WAD.Runner.DrawingAutomation.Wedges.Fp;
using WAD.Runner.DrawingAutomation.Wedges.Osg7;
using WAD.Runner.DrawingAutomation.Wedges.Utus;
using WAD.Runner.DrawingAutomation.Wedges._45CK;

namespace WAD.Runner.DrawingAutomation.Wedges;

public static class DrawingWedgeModuleRegistry
{
    private static readonly IReadOnlyDictionary<WedgeType, IDrawingWedgeModule> Modules =
        BuildModules();

    public static IReadOnlyList<WedgeType> SupportedWedgeTypes { get; } =
        Modules.Keys.ToArray();

    public static IDrawingWedgeModule Get(WedgeType wedgeType)
    {
        if (Modules.TryGetValue(wedgeType, out var module))
            return module;

        throw new NotSupportedException(
            $"No DrawingAutomation wedge module is registered for '{wedgeType}'. " +
            "Add one implementation of IDrawingWedgeModule.");
    }

    public static IEnumerable<DrawingProfile> GetProfiles()
        => Modules.Values.SelectMany(module => module.Profiles);

    public static IEnumerable<IAnnotationRuleCatalog> GetAnnotationCatalogs()
        => Modules.Values
            .SelectMany(module => module.AnnotationCatalogs)
            .GroupBy(catalog => catalog.Profile)
            .Select(group => group.First());

    private static IReadOnlyDictionary<WedgeType, IDrawingWedgeModule> BuildModules()
    {
        IDrawingWedgeModule[] modules =
        {
            new _4516DrawingModule(),
            new AbtDrawingModule(),
            new Ab16DrawingModule(),
            new CkvdDrawingModule(),
            new CobDrawingModule(),
            new FpDrawingModule(),
            new UtusDrawingModule(),
            new Osg7DrawingModule(),
            new _45CKDrawingModule(),
        };

        var duplicateTypes = modules
            .GroupBy(module => module.WedgeType)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateTypes.Length > 0)
        {
            throw new InvalidOperationException(
                "Duplicate DrawingAutomation wedge modules: " +
                string.Join(", ", duplicateTypes));
        }

        foreach (var module in modules)
            Validate(module);

        return modules.ToDictionary(module => module.WedgeType);
    }

    private static void Validate(IDrawingWedgeModule module)
    {
        if (module.Behavior is null)
            throw new InvalidOperationException($"{module.WedgeType} has no drawing behavior.");

        if (module.OverlayPositioningRule is null)
            throw new InvalidOperationException($"{module.WedgeType} has no overlay positioning rule.");

        if (module.Profiles is null || module.Profiles.Count == 0)
            throw new InvalidOperationException($"{module.WedgeType} has no drawing profiles.");

        if (module.AnnotationCatalogs is null || module.AnnotationCatalogs.Count == 0)
            throw new InvalidOperationException($"{module.WedgeType} has no annotation catalogs.");

        if (module.AnnotationContextResolver is null)
            throw new InvalidOperationException($"{module.WedgeType} has no annotation context resolver.");

        var duplicateProfileKeys = module.Profiles
            .GroupBy(profile => profile.Key)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateProfileKeys.Length > 0)
        {
            throw new InvalidOperationException(
                $"{module.WedgeType} contains duplicate drawing profiles: " +
                string.Join(", ", duplicateProfileKeys));
        }

        foreach (var profile in module.Profiles)
        {
            if (profile.Key.WedgeType != module.WedgeType)
            {
                throw new InvalidOperationException(
                    $"Profile '{profile.ProfileName}' belongs to {profile.Key.WedgeType}, " +
                    $"but is registered by the {module.WedgeType} module.");
            }

            var annotationProfile = module.ResolveAnnotationProfile(
                profile.Key.Subclass,
                profile.Key.DrawingType);

            if (!module.AnnotationCatalogs.Any(catalog => catalog.Profile == annotationProfile))
            {
                throw new InvalidOperationException(
                    $"Profile '{profile.ProfileName}' resolves to annotation profile " +
                    $"'{annotationProfile}', but the {module.WedgeType} module does not provide it.");
            }

            ValidateProfile(profile);
        }
    }

    private static void ValidateProfile(DrawingProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.ProfileName))
            throw new InvalidOperationException($"Profile '{profile.Key}' has no name.");

        if (profile.SheetSelector is null)
            throw new InvalidOperationException($"Profile '{profile.Key}' has no sheet selector.");

        if (profile.BreaklineViews is null)
            throw new InvalidOperationException($"Profile '{profile.Key}' has no breakline policy.");

        if (profile.Scale is null ||
            profile.Scale.Step <= 0.0 ||
            profile.Scale.MinScale <= 0.0 ||
            profile.Scale.MaxScale < profile.Scale.MinScale ||
            profile.Scale.FillRatioHeight <= 0.0 ||
            profile.Scale.FillRatioHeight > 1.0)
        {
            throw new InvalidOperationException($"Profile '{profile.Key}' has an invalid scale policy.");
        }
    }
}