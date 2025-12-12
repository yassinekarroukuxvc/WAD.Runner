using WAD.Runner.Application.Ports;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Planning;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.Application.UseCases;

/// <summary>
/// End-to-end drawing planning use-case.
/// Combines data loading, layout rules, notes, tables, and diagnostics.
/// </summary>
public sealed class PlanDrawing
{
    private readonly IWedgeDataSource _wedgeSrc;
    private readonly IDrawingDataSource _drawSrc;

    public PlanDrawing(IWedgeDataSource wedgeSrc, IDrawingDataSource drawSrc)
    {
        _wedgeSrc = wedgeSrc;
        _drawSrc = drawSrc;
    }

    public async Task<DrawingPlan> ExecuteAsync(
        string article,
        WedgeSubclass subclass,
        DrawingType dtype,
        WedgeType wedgeType,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(article))
            throw new ArgumentException("Article is required.", nameof(article));

        // 1. Load data
        var wedge = await _wedgeSrc.LoadAsync(article, subclass, ct);
        var drawing = await _drawSrc.LoadAsync(dtype, subclass, wedgeType, article.Trim(), ct);

        // 2. Build context & diagnostics
        var ctx = new LayoutContext(wedge, drawing);
        var diag = new PlannerDiagnostics();

        // 3. Build dimensions
        var dims = DimensionRules.Build(ctx, diag);

        // 4. Build notes & tables
        var notes = NoteRules.Build(ctx);
        var tables = TableRules.Build(drawing);

        // 5. Aggregate into a DrawingPlan
        return new DrawingPlan(dims, notes, tables, diag);
    }
}
