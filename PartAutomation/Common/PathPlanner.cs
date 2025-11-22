// PartAutomation/Common/PathPlanner.cs
using System;
using System.IO;
using WAD.Runner.Application;                    // Logger
using WAD.Runner.DataManagement.Domain.Drawing; // DrawingType
using WAD.Runner.DataManagement.Domain.Wedge;   // WedgeSubclass

namespace WAD.Runner.PartAutomation.Common;

public static class PathPlanner
{
    public sealed record Plan(
        string WorkDir,
        string PartPath,
        string EquationsPath,
        string PdfPath,
        string FileBase
    );

    /// <summary>
    /// Build a normalized working directory under:
    ///   {outputRoot}\{Subclass}\{DrawingType}\{Article}
    /// Idempotent: if <paramref name="outputRoot"/> already ends with those segments,
    /// it will not append them again.
    /// </summary>
    public static Plan Build(
        string article,
        WedgeSubclass subclass,
        DrawingType drawingType,
        string outputRoot,
        string? fileBase = null)
    {
        if (string.IsNullOrWhiteSpace(article))
            throw new ArgumentException("Article is required.", nameof(article));

        Logger.Info($"[PathPlanner] Build start → article={article}, subclass={subclass}, drawingType={drawingType}, outputRoot='{outputRoot}', fileBase='{fileBase ?? "(auto)"}'");

        var baseRoot = string.IsNullOrWhiteSpace(outputRoot)
            ? Path.Combine("Resources", "Out")
            : outputRoot;

        var sub = subclass.ToString();
        var dtype = drawingType.ToString();

        // Normalize and detect whether baseRoot already ends with \{sub}\{dtype}\{article}
        var normalized = Path.GetFullPath(baseRoot.Trim().TrimEnd('\\', '/'));
        string[] segs = normalized.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        bool alreadyScoped =
            segs.Length >= 3
            && segs[^1].Equals(article, StringComparison.OrdinalIgnoreCase)
            && segs[^2].Equals(dtype, StringComparison.OrdinalIgnoreCase)
            && segs[^3].Equals(sub, StringComparison.OrdinalIgnoreCase);

        var workDir = alreadyScoped
            ? normalized
            : Path.GetFullPath(Path.Combine(baseRoot, sub, dtype, article));

        if (!Directory.Exists(workDir))
        {
            Directory.CreateDirectory(workDir);
            Logger.Info($"[PathPlanner] Created work directory: {workDir}");
        }
        else
        {
            Logger.Info($"[PathPlanner] Using existing work directory: {workDir}");
        }

        // File base
        var fb = string.IsNullOrWhiteSpace(fileBase) ? $"{article}P" : fileBase!.Trim();

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
}
