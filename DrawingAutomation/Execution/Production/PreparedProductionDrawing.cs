using System.Collections.Generic;

using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Execution.Production;

internal sealed record PreparedProductionDrawing(
    DrawingService DrawingService,
    IDictionary<string, string> ViewNames);
