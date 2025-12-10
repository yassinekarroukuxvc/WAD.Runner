// PartAutomation/Rules/COBRules.cs
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.PartAutomation.SolidWorks;

namespace WAD.Runner.PartAutomation.Rules;

public static class COBRules
{
    /// <summary>
    /// COB post-rules orchestration.
    /// Currently: engraving toggle for non-overlay drawings only.
    /// Add COB-specific behaviors here later.
    /// </summary>
    public static void Apply(PartEditor part, WedgeData wedge, DrawingType drawingType)
    {
        Logger.Info("[COBRules] Apply → start");

        // Example policy: same engraving behavior as CKVD.
        if (drawingType == DrawingType.Production || drawingType == DrawingType.Customer)
        {
            Logger.Info("[COBRules] Non-overlay drawing → apply engraving toggle.");
            BasicPartRules.ApplyEngravingToggle(part);
        }
        else
        {
            Logger.Info("[COBRules] Overlay drawing → no engraving sketch change (for now).");
        }

        // TODO: add real COB-specific rules here:
        // e.g., CobSpecificRules.ApplySomething(part, wedge, drawingType);

        part.Rebuild();
        Logger.Success("[COBRules] Apply → done.");
    }
}
