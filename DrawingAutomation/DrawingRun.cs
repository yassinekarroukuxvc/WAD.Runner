using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation;

/// <summary>
/// Input context for a single drawing run (FG → Production in Step 1).
/// Keeps executors clean and parameter lists short.
/// </summary>
public sealed class DrawingRun
{
    // Template sources
    public required string TemplatePartPath { get; init; }
    public required string TemplateDrawingPath { get; init; }

    // Working copies (destinations)
    public required string ModPartPath { get; init; }
    public required string ModDrawingPath { get; init; }
    public required string EquationsPath { get; init; }

    // Domain data (used by part automation and, later, drawing steps)
    public required WedgeData Wedge { get; init; }

    // Optional outputs for later steps (export)
    public string? OutputPdfPath { get; init; }
    public string? OutputTiffPath { get; init; }
}
