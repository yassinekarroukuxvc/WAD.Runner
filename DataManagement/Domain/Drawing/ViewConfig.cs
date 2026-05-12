namespace WAD.Runner.DataManagement.Domain.Drawing;

public sealed class ViewConfig
{
    public double[] PositionMm { get; set; } = new double[] { 0, 0 };
    public double Scale { get; set; } = 1.0;

    public Dictionary<string, double> Params { get; set; } = new();
    public Dictionary<string, bool> Flags { get; set; } = new();
    public Dictionary<string, string?> Metadata { get; set; } = new();
}
