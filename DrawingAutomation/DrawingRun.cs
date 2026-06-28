using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation;


public sealed class DrawingRun
{

    public required string TemplatePartPath { get; init; }
    public required string TemplateDrawingPath { get; init; }


    public required string ModPartPath { get; init; }
    public required string ModDrawingPath { get; init; }
    public required string EquationsPath { get; init; }


    public required WedgeData Wedge { get; init; }


    public string? OutputPdfPath { get; init; }
    public string? OutputTiffPath { get; init; }
    public WedgeType WedgeType { get; init; } = WedgeType.CKVD;
}
