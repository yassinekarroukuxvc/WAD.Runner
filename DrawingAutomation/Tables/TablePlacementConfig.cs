namespace WAD.Runner.DrawingAutomation.Tables;

// Anchor to a sheet corner or near a view. Keep it dead simple.
public sealed class TablePlacementConfig
{
    // One of: "SheetTopLeft", "SheetTopRight", "SheetBottomLeft", "SheetBottomRight", "NearView"
    public string Anchor { get; init; } = "SheetBottomLeft";
    public string? ViewName { get; init; } = null; // used only when Anchor == NearView
    public double OffsetXmm { get; init; } = 10;
    public double OffsetYmm { get; init; } = 10;
    public double ColumnWidthMm { get; init; } = 35;  // default fixed width per column
    public double RowHeightMm { get; init; } = 6;     // simple default height
}
