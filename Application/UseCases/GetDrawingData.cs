using WAD.Runner.Application.Ports;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.Application.UseCases;

/// <summary>
/// Loads CAD-agnostic drawing configuration (views/tables/metadata)
/// for the given DrawingType and subclass.
/// </summary>
public sealed class GetDrawingData
{
    private readonly IDrawingDataSource _source;

    public GetDrawingData(IDrawingDataSource source) => _source = source;

    public Task<DrawingData> ExecuteAsync(DrawingType drawingType, WedgeSubclass subclass, string articleNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(articleNumber))
            throw new ArgumentException("Article number is required.", nameof(articleNumber));

        return _source.LoadAsync(drawingType, subclass, articleNumber.Trim(), ct);
    }
}
