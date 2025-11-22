using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.PartAutomation.Interfaces;

public interface IPartAutomation
{
    Task<string> RunAsync(
        WedgeData wedge,
        DrawingData drawing,
        PartAutomationOptions opts,
        CancellationToken ct = default);
}