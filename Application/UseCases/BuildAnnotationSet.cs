using WAD.Runner.Application.Ports;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Planning;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.Application.UseCases;

/// <summary>
/// Builds a CAD-agnostic annotation set (dimensions, notes, tables)
/// from the domain data sources.  This corresponds to the old
/// “plan-drawing” phase before SolidWorks execution.
/// </summary>
public sealed class BuildAnnotationSet
{
    private readonly IWedgeDataSource _wedgeSrc;
    private readonly IDrawingDataSource _drawSrc;

    public BuildAnnotationSet(IWedgeDataSource wedgeSrc, IDrawingDataSource drawSrc)
    {
        _wedgeSrc = wedgeSrc;
        _drawSrc = drawSrc;
    }

    public async Task<(IReadOnlyList<DimensionSpec> Dims,
                      IReadOnlyList<NoteSpec> Notes,
                      IReadOnlyList<TableSpec> Tables,
                      PlannerDiagnostics Diag)>
        ExecuteAsync(string article, WedgeSubclass subclass, DrawingType dtype, CancellationToken ct)
    {
        // 1. Load data from sources
        var wedge = await _wedgeSrc.LoadAsync(article, subclass, ct);
        var drawing = await _drawSrc.LoadAsync(dtype, subclass, article, ct);

        // 2. Build context and diagnostics
        var ctx = new LayoutContext(wedge, drawing);
        var diag = new PlannerDiagnostics();

        // 3. Build dimensions using our new DimensionRules API
        Logger.Info("[BuildSet] BEFORE DimensionRules.Build");
        var dims = DimensionRules.Build(ctx, diag);
        Logger.Info($"[BuildSet] AFTER  DimensionRules.Build → dims={dims.Count}");

        // 4. Example: derive simple notes (you’ll extend in Step 20)
        var notes = new List<NoteSpec>();
        if (drawing.Metadata.TryGetValue("OverlayCalibrationMicron", out var cal)
            && !string.IsNullOrWhiteSpace(cal)
            && drawing.Tables.TryGetValue("HowToOrder", out var tbl))
        {
            notes.Add(new NoteSpec
            {
                Id = "OverlayCalibration",
                PositionMm = new[] { tbl.PositionMm[0], tbl.PositionMm[1] - 10 },
                Text = $"Calibration: {cal} µm"
            });
        }

        // 5. Translate tables from drawing config into TableSpecs
        var tables = drawing.Tables.Select(kv => new TableSpec
        {
            Id = kv.Key,
            PositionMm = kv.Value.PositionMm,
            SizeMm = kv.Value.SizeMm
        }).ToList();

        // 6. Return the bundle
        return (dims, notes, tables, diag);
    }
}
