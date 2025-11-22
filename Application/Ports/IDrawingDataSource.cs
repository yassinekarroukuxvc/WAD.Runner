using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.Application.Ports;

/// <summary>
/// Loads drawing configuration (positions, scales, metadata) from any source
/// (JSON file, DB, etc.), independent of SolidWorks.
/// </summary>
public interface IDrawingDataSource
{
    Task<DrawingData> LoadAsync(
        DrawingType drawingType,
        WedgeSubclass subclass,
        string articleNumber,
        CancellationToken ct);
}
