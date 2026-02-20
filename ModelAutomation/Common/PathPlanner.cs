using System;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Common;

public static class PathPlanner
{
    public sealed record Plan(
        string WorkDir,
        string PartPath,
        string EquationsPath,
        string PdfPath,
        string FileBase
    );

    public static Plan Build(
        string article,
        WedgeSubclass subclass,
        DrawingType drawingType,
        string outputRoot,
        string? fileBase = null)
    {
        if (string.IsNullOrWhiteSpace(article))
            throw new ArgumentException("Article is required.", nameof(article));

        Logger.Info($"[PathPlanner] Build start → article={article}, subclass={subclass}, drawingType={drawingType}");

        var baseRoot = string.IsNullOrWhiteSpace(outputRoot)
            ? Path.Combine("Resources", "Out")
            : outputRoot;

        var sub = subclass.ToString();
        var dtype = drawingType.ToString();

        var normalized = Path.GetFullPath(baseRoot.Trim().TrimEnd('\\', '/'));
        var segs = normalized.Split(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);

        bool alreadyScoped =
            segs.Length >= 3 &&
            segs[^1].Equals(article, StringComparison.OrdinalIgnoreCase) &&
            segs[^2].Equals(dtype, StringComparison.OrdinalIgnoreCase) &&
            segs[^3].Equals(sub, StringComparison.OrdinalIgnoreCase);

        var workDir = alreadyScoped
            ? normalized
            : Path.GetFullPath(Path.Combine(baseRoot, sub, dtype, article));

        Directory.CreateDirectory(workDir);

        // -----------------------------
        // File base naming logic
        // -----------------------------
        var suffix = ResolveSuffix(subclass, drawingType);
        var fb = string.IsNullOrWhiteSpace(fileBase)
            ? $"{article}{suffix}"
            : fileBase.Trim();

        var partPath = Path.Combine(workDir, $"{fb}.SLDPRT");
        var equationsPath = Path.Combine(workDir, "equations.txt");
        var pdfPath = Path.Combine(workDir, $"{fb}.pdf");

        Logger.Info("[PathPlanner] Planned paths:");
        Logger.Info($"  • Part      : {partPath}");
        Logger.Info($"  • Equations : {equationsPath}");
        Logger.Info($"  • PDF       : {pdfPath}");
        Logger.Info($"[PathPlanner] Build complete → baseName={fb}");

        return new Plan(
            WorkDir: workDir,
            PartPath: partPath,
            EquationsPath: equationsPath,
            PdfPath: pdfPath,
            FileBase: fb
        );
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
}
