using System;
using System.IO;

namespace WAD.Runner.DrawingAutomation.Core;

public static class DrawingRunValidator
{
    public static void Validate(DrawingRun run)
    {
        if (run is null) throw new ArgumentNullException(nameof(run));
        if (run.Wedge is null) throw new ArgumentException("DrawingRun.Wedge is required.", nameof(run));

        RequireExistingFile(run.TemplatePartPath, nameof(run.TemplatePartPath));
        RequireExistingFile(run.TemplateDrawingPath, nameof(run.TemplateDrawingPath));
        RequirePath(run.ModPartPath, nameof(run.ModPartPath));
        RequirePath(run.ModDrawingPath, nameof(run.ModDrawingPath));
        RequirePath(run.EquationsPath, nameof(run.EquationsPath));

        var templateDrawing = Path.GetFullPath(run.TemplateDrawingPath);
        var outputDrawing = Path.GetFullPath(run.ModDrawingPath);

        if (string.Equals(templateDrawing, outputDrawing, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "TemplateDrawingPath and ModDrawingPath must be different. " +
                "Drawing automation must never overwrite its template.");
        }
    }

    public static void EnsureGeneratedPartExists(DrawingRun run)
    {
        if (run is null) throw new ArgumentNullException(nameof(run));

        var generatedPart = Path.GetFullPath(run.ModPartPath);
        if (!File.Exists(generatedPart))
        {
            throw new FileNotFoundException(
                "The model phase completed without producing the part required by drawing automation.",
                generatedPart);
        }
    }

    private static void RequireExistingFile(string path, string propertyName)
    {
        RequirePath(path, propertyName);

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"{propertyName} was not found.", fullPath);
    }

    private static void RequirePath(string path, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException($"{propertyName} is required.", propertyName);
    }
}
