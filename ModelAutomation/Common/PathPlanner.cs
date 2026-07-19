using System;
using System.IO;
using System.Linq;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Common;

public static class PathPlanner
{
    public sealed record Plan(
        string WorkDir,
        string PartPath,
        string EquationsPath,
        string PdfPath,
        string FileBase);

    public static Plan Build(
        string article,
        WedgeSubclass subclass,
        DrawingType drawingType,
        string outputRoot,
        string? fileBase = null)
    {
        var safeArticle = SanitizeFileName(article, "UNKNOWN");
        var baseRoot = string.IsNullOrWhiteSpace(outputRoot)
            ? Path.Combine("Resources", "Out")
            : outputRoot.Trim();

        var subclassSegment = subclass.ToString();
        var drawingTypeSegment = drawingType.ToString();
        var normalizedRoot = Path.GetFullPath(baseRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        var segments = normalizedRoot.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        var alreadyScoped = segments.Length >= 3
                            && segments[^1].Equals(safeArticle, StringComparison.OrdinalIgnoreCase)
                            && segments[^2].Equals(drawingTypeSegment, StringComparison.OrdinalIgnoreCase)
                            && segments[^3].Equals(subclassSegment, StringComparison.OrdinalIgnoreCase);

        var workDir = alreadyScoped
            ? normalizedRoot
            : Path.GetFullPath(Path.Combine(normalizedRoot, subclassSegment, drawingTypeSegment, safeArticle));

        Directory.CreateDirectory(workDir);

        var suffix = ResolveSuffix(subclass, drawingType);
        var defaultFileBase = $"{safeArticle}{suffix}";
        var safeFileBase = SanitizeFileName(fileBase, defaultFileBase);

        return new Plan(
            WorkDir: workDir,
            PartPath: Path.Combine(workDir, $"{safeFileBase}.SLDPRT"),
            EquationsPath: Path.Combine(workDir, "equations.txt"),
            PdfPath: Path.Combine(workDir, $"{safeFileBase}.pdf"),
            FileBase: safeFileBase);
    }

    private static string ResolveSuffix(WedgeSubclass subclass, DrawingType drawingType)
    {
        var isPgb = subclass == WedgeSubclass.PGB;

        return (isPgb, drawingType) switch
        {
            (true, DrawingType.Production) => "D",
            (true, DrawingType.Overlay) => "TF",
            (false, DrawingType.Production) => "P",
            (false, DrawingType.Customer) => "C",
            (false, DrawingType.Overlay) => "TF",
            _ => throw new NotSupportedException(
                $"Unsupported combination: subclass={subclass}, drawingType={drawingType}")
        };
    }

    private static string SanitizeFileName(string? value, string fallback)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var invalid = Path.GetInvalidFileNameChars()
            .Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' })
            .ToHashSet();

        var cleaned = new string(candidate
            .Select(ch => char.IsControl(ch) || invalid.Contains(ch) ? '_' : ch)
            .ToArray())
            .Trim()
            .TrimEnd('.', ' ');

        if (string.IsNullOrWhiteSpace(cleaned) || cleaned is "." or "..")
            return fallback;

        return cleaned;
    }
}
