using System.Collections.Generic;

using WAD.Runner.DrawingAutomation.Profiles.Modules;

namespace WAD.Runner.DrawingAutomation.Profiles;

public static class DrawingProfileCatalog
{
    public static IEnumerable<DrawingProfileRegistration> CreateDefault()
    {
        foreach (var module in CreateDefaultModules())
        foreach (var profile in module.CreateProfiles())
            yield return profile;
    }

    private static IEnumerable<IDrawingProfileModule> CreateDefaultModules()
    {
        yield return new CkvdDrawingProfiles();
        yield return new CobDrawingProfiles();
        yield return new UtusDrawingProfiles();
        yield return new FpDrawingProfiles();
        yield return new Osg7DrawingProfiles();
    }
}
