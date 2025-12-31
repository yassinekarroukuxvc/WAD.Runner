// PartAutomation/Rules/BasicPartRules.cs
using System;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;     // DrawingType
using WAD.Runner.DataManagement.Domain.Wedge;      // WedgeData
using WAD.Runner.PartAutomation.Common;
using WAD.Runner.PartAutomation.SolidWorks;

namespace WAD.Runner.PartAutomation.Rules;

public static class BasicPartRules
{
    /// <summary>
    /// CKVD entry point: delegates to CKVDRules.
    /// </summary>
    public static void ApplyCkvdRules(PartEditor part, WedgeData wedge, DrawingType drawingType)
    {
        if (part == null) throw new ArgumentNullException(nameof(part));
        if (wedge == null) throw new ArgumentNullException(nameof(wedge));

        Logger.Info($"[BasicPartRules] ApplyCkvdRules → subclass={wedge.Subclass}, drawingType={drawingType}");
        CKVDRules.Apply(part, wedge, drawingType);
    }

    /// <summary>
    /// COB entry point: delegates to COBRules.
    /// </summary>
    public static void ApplyCobRules(PartEditor part, WedgeData wedge, DrawingType drawingType)
    {
        if (part == null) throw new ArgumentNullException(nameof(part));
        if (wedge == null) throw new ArgumentNullException(nameof(wedge));

        Logger.Info($"[BasicPartRules] ApplyCobRules → subclass={wedge.Subclass}, drawingType={drawingType}");
        COBRules.Apply(part, wedge, drawingType);
    }

    public static void ApplyOsg7Rules(PartEditor part, WedgeData wedge, DrawingType drawingType)
    {
        if (part == null) throw new ArgumentNullException(nameof(part));
        if (wedge == null) throw new ArgumentNullException(nameof(wedge));

        Logger.Info($"[BasicPartRules] ApplyOsg7Rules → subclass={wedge.Subclass}, drawingType={drawingType}");
        OSG7Rules.Apply(part, wedge, drawingType);
    }


    /// <summary>
    /// Common engraving sketch toggle for *non-overlay* drawings.
    /// Wedge-type-specific rule sets can reuse this.
    /// </summary>
    public static void ApplyEngravingToggle(PartEditor part)
    {
        if (part == null) throw new ArgumentNullException(nameof(part));

        Logger.Info("[BasicPartRules] ApplyEngravingToggle → enable engraving for non-overlay drawings");

        // For Production / Customer: engraving ON (unsuppressed)
        const bool suppress = false;

        Logger.Info($"[BasicPartRules] Engraving sketch '{SwNames.Engraving}' suppress={suppress}");
        part.SuppressSketch(SwNames.Engraving, suppress: suppress);

        Logger.Success("[BasicPartRules] Engraving toggle applied.");
    }
}
