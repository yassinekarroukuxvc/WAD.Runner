namespace WAD.Runner.DataManagement.Domain.Drawing;

public sealed class TableConfig
{
    public double[] PositionMm { get; set; } = new double[] { 0, 0 };
    public double[]? SizeMm { get; set; }
    public Dictionary<string, string?> Metadata { get; set; } = new();
    public Dictionary<string, double> Params { get; set; } = new();
    public Dictionary<string, bool> Flags { get; set; } = new();
}
