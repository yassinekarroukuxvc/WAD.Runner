namespace WAD.Runner.DataManagement.Domain.Drawing;

/// <summary>
/// Logical table placement and optional width/height in mm.
/// </summary>
public sealed class TableConfig
{
    public double[] PositionMm { get; set; } = new double[] { 0, 0 };
    public double[]? SizeMm { get; set; }              // optional [w,h]
    public Dictionary<string, string?> Metadata { get; set; } = new(); // e.g., title, font, etc.
    public Dictionary<string, double> Params { get; set; } = new();    // e.g., rowHeight, padding
    public Dictionary<string, bool> Flags { get; set; } = new();     // toggles
}
