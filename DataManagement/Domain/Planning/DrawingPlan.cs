namespace WAD.Runner.DataManagement.Domain.Planning;

public sealed class DrawingPlan
{
    public IReadOnlyList<DimensionSpec> Dimensions { get; }
    public IReadOnlyList<NoteSpec> Notes { get; }
    public IReadOnlyList<TableSpec> Tables { get; }
    public PlannerDiagnostics Diagnostics { get; }

    public DrawingPlan(
        IReadOnlyList<DimensionSpec> dims,
        IReadOnlyList<NoteSpec> notes,
        IReadOnlyList<TableSpec> tables,
        PlannerDiagnostics diag)
    {
        Dimensions = dims;
        Notes = notes;
        Tables = tables;
        Diagnostics = diag;
    }
}
