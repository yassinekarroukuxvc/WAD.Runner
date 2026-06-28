using System.Collections.Generic;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Rules.Common;

public interface IDrawingCleanupRunner
{
    WedgeType AppliesTo { get; }

    void TryApply(
        DrawingService ds,
        IDictionary<string, string> nameMap,
        DrawingRun run,
        DrawingData drawingData,
        bool activateEachView = true);
}
