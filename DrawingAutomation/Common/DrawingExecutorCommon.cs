using System;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using WAD.Runner.Application;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Common;

public static class DrawingExecutorCommon
{
    /// <summary>
    /// Copy template → try relink (while closed) → open drawing → (optional) in-session relink → rebuild/zoom.
    /// </summary>
    public static DrawingService InitializeAndRelink(SldWorks swApp, DrawingRun run)
    {
        if (swApp is null) throw new ArgumentNullException(nameof(swApp));
        if (run is null) throw new ArgumentNullException(nameof(run));

        var destDrw = Path.GetFullPath(run.ModDrawingPath);
        var srcDrw = Path.GetFullPath(run.TemplateDrawingPath);
        var newPart = Path.GetFullPath(run.ModPartPath);
        var oldPart = string.IsNullOrWhiteSpace(run.TemplatePartPath)
            ? string.Empty
            : Path.GetFullPath(run.TemplatePartPath);

        // Ensure destination folder exists
        Directory.CreateDirectory(Path.GetDirectoryName(destDrw)!);

        // If a drawing already exists at the destination, delete it first, then copy fresh
        if (File.Exists(destDrw))
        {
            try
            {
                File.SetAttributes(destDrw, FileAttributes.Normal); // avoid read-only issues
            }
            catch { /* ignore */ }

            File.Delete(destDrw);
            Logger.Info($"[Init] Deleted existing destination drawing → '{destDrw}'");
        }

        // Copy template drawing to destination (fresh copy every time)
        File.Copy(srcDrw, destDrw, overwrite: true);
        Logger.Info($"[Init] Copied drawing template → '{destDrw}'");

        if (!File.Exists(destDrw))
            throw new FileNotFoundException("Destination drawing missing after copy.", destDrw);

        if (!File.Exists(newPart))
            Logger.Warn($"[Init] Target part not found yet (relink will still try): {newPart}");

        // 1) Primary relink while CLOSED
        var closedRelinkOk = TryRelinkWhileClosed(swApp, destDrw, oldPart, newPart);

        // 2) Open the drawing
        var ds = new DrawingService(swApp);
        ds.OpenDrawing(destDrw);

        // 3) Optional fallback in-session only if closed relink failed
        if (!closedRelinkOk)
            ds.ReplaceReferencedModel(destDrw, oldPart, newPart);

        ds.Rebuild();
        ds.ZoomToSheet();

        return ds;
    }

    /// <summary>
    /// Save, export PDF (if provided), and close.
    /// </summary>
    public static void FinalizeProduction(SldWorks swApp, DrawingService ds, string? pdfOutputPath = null)
    {
        if (ds is null) return;

        try
        {
            ds.Save();
            if (!string.IsNullOrWhiteSpace(pdfOutputPath))
            {
                try
                {
                    Exporter.SavePdfAllSheets(swApp, ds, Path.GetFullPath(pdfOutputPath!));
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[Finalize] PDF export failed: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Finalize] Save issue: {ex.Message}");
        }
        finally
        {
            try { ds.SaveAndClose(); } catch { }
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    // TIFF EXPORT HELPERS (Sheet size + DPI, similar to your OTHER project)
    // ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Configure SolidWorks TIFF export to use the current sheet size at the given DPI.
    /// Uses the active drawing behind the provided DrawingService.
    /// </summary>
    private static bool ConfigureTiffExportUseSheetSize(SldWorks swApp, DrawingService ds, int dpi)
    {
        try
        {
            var doc = ds.Model as ModelDoc2;
            var drw = ds.Drawing as DrawingDoc;
            if (doc == null || drw == null || doc.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                Logger.Error("[TIFF] No active drawing document. Open/activate a .SLDDRW first.");
                return false;
            }

            // Tell SW to export from the printer pipeline and to use the SHEET size.
            swApp.SetUserPreferenceIntegerValue(
                (int)swUserPreferenceIntegerValue_e.swTiffScreenOrPrintCapture,
                1 /* Print capture */);

            swApp.SetUserPreferenceToggle(
                (int)swUserPreferenceToggle_e.swTiffPrintUseSheetSize,
                true /* Use sheet size */);

            // DPI + safe defaults
            swApp.SetUserPreferenceIntegerValue(
                (int)swUserPreferenceIntegerValue_e.swTiffPrintDPI,
                dpi);

            swApp.SetUserPreferenceIntegerValue(
                (int)swUserPreferenceIntegerValue_e.swTiffImageType,
                (int)swTiffImageType_e.swTiffImageRGB);

            swApp.SetUserPreferenceIntegerValue(
                (int)swUserPreferenceIntegerValue_e.swTiffCompressionScheme,
                (int)swTiffCompressionScheme_e.swTiffPackbitsCompression);

            // Log what we will actually get from this sheet
            var sheet = (Sheet)drw.GetCurrentSheet();
            double w_m = 0, h_m = 0;
            sheet.GetSize(ref w_m, ref h_m);

            double w_in = w_m / 0.0254;
            double h_in = h_m / 0.0254;

            int applied = swApp.GetUserPreferenceIntegerValue(
                (int)swUserPreferenceIntegerValue_e.swTiffPrintDPI);

            Logger.Info(
                $"[TIFF] Using SHEET size: {w_in:F4} × {h_in:F4} in @ {applied} DPI " +
                $"(≈ {Math.Round(w_in * applied)} × {Math.Round(h_in * applied)} px)");

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"[TIFF] ConfigureTiffExportUseSheetSize({dpi}) failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Save the current sheet as TIFF using the sheet size and desired DPI.
    /// </summary>
    private static bool SaveCurrentSheetAsTiffUseSheetSize(SldWorks swApp, DrawingService ds, string outputFullPath, int dpi)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(outputFullPath))
            {
                Logger.Error("[TIFF] Output path is null/empty.");
                return false;
            }

            var doc = ds.Model as ModelDoc2;
            var drw = ds.Drawing as DrawingDoc;
            if (doc == null || drw == null || doc.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                Logger.Error("[TIFF] No active drawing document. Open/activate a .SLDDRW first.");
                return false;
            }

            if (!ConfigureTiffExportUseSheetSize(swApp, ds, dpi))
                return false;

            // Ensure we're in Edit Sheet (not Edit Template)
            drw.EditSheet();
            doc.EditRebuild3();

            int errs = 0, warns = 0;
            bool ok = doc.Extension.SaveAs(
                outputFullPath,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                null,
                ref errs,
                ref warns);

            if (!ok)
            {
                Logger.Error($"[TIFF] SaveAs TIFF failed. Errors={errs}, Warnings={warns}");
                return false;
            }

            // Log final expected pixels from sheet size
            var sheet = (Sheet)drw.GetCurrentSheet();
            double w_m = 0, h_m = 0;
            sheet.GetSize(ref w_m, ref h_m);

            int applied = swApp.GetUserPreferenceIntegerValue(
                (int)swUserPreferenceIntegerValue_e.swTiffPrintDPI);

            double w_in = w_m / 0.0254;
            double h_in = h_m / 0.0254;

            Logger.Success(
                $"[TIFF] Saved: {outputFullPath} " +
                $"(≈ {Math.Round(w_in * applied)} × {Math.Round(h_in * applied)} px @ {applied} DPI)");

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"[TIFF] SaveCurrentSheetAsTiffUseSheetSize({dpi}) failed: {ex.Message}");
            return false;
        }
    }

    // ------------------------ 100 DPI helpers ------------------------

    public static bool ConfigureTiffExportSheetSize100Dpi(SldWorks swApp, DrawingService ds)
        => ConfigureTiffExportUseSheetSize(swApp, ds, 100);

    public static bool SaveCurrentSheetAsTiff100Dpi(SldWorks swApp, DrawingService ds, string outputFullPath)
        => SaveCurrentSheetAsTiffUseSheetSize(swApp, ds, outputFullPath, 100);

    // ------------------------ 200 DPI helpers ------------------------

    public static bool ConfigureTiffExportSheetSize200Dpi(SldWorks swApp, DrawingService ds)
        => ConfigureTiffExportUseSheetSize(swApp, ds, 200);

    public static bool SaveCurrentSheetAsTiff200Dpi(SldWorks swApp, DrawingService ds, string outputFullPath)
        => SaveCurrentSheetAsTiffUseSheetSize(swApp, ds, outputFullPath, 200);

    // ------------------------ Generic helper ------------------------

    /// <summary>
    /// Generic helper if you want to call it with any DPI.
    /// </summary>
    public static bool SaveCurrentSheetAsTiff(SldWorks swApp, DrawingService ds, string outputFullPath, int dpi)
        => SaveCurrentSheetAsTiffUseSheetSize(swApp, ds, outputFullPath, dpi);

    // ───────────────────────────────────────────────────────────────────────

    private static bool TryRelinkWhileClosed(SldWorks swApp, string drawingPath, string oldModelPath, string newModelPath)
    {
        bool success = false;

        try
        {
            if (!File.Exists(drawingPath))
            {
                Logger.Warn($"[Relink/Closed] Drawing not found: {drawingPath}");
                return false;
            }
            if (string.IsNullOrWhiteSpace(newModelPath) || !File.Exists(newModelPath))
            {
                Logger.Warn($"[Relink/Closed] New model not found: {newModelPath}");
                return false;
            }

            // Try with the full old path if we have it
            if (!string.IsNullOrWhiteSpace(oldModelPath) && File.Exists(oldModelPath))
            {
                var ok = swApp.ReplaceReferencedDocument(
                    Path.GetFullPath(drawingPath),
                    Path.GetFullPath(oldModelPath),
                    Path.GetFullPath(newModelPath));

                if (ok)
                {
                    Logger.Info($"[Relink/Closed] Relinked: '{oldModelPath}' → '{newModelPath}'.");
                    return true;
                }
                Logger.Warn($"[Relink/Closed] ReplaceReferencedDocument returned false (old='{oldModelPath}', new='{newModelPath}').");
            }

            // Try filename-based guess using the template part’s filename (if supplied)
            if (!string.IsNullOrWhiteSpace(oldModelPath))
            {
                var guess = Path.GetFileName(oldModelPath);
                if (!string.IsNullOrWhiteSpace(guess))
                {
                    var ok2 = swApp.ReplaceReferencedDocument(
                        Path.GetFullPath(drawingPath),
                        guess,
                        Path.GetFullPath(newModelPath));
                    if (ok2)
                    {
                        Logger.Info($"[Relink/Closed] Relinked by filename guess: '{guess}' → '{newModelPath}'.");
                        return true;
                    }
                }
            }

            // Final nudge: use the new file’s name as key
            var newFile = Path.GetFileName(newModelPath);
            if (!string.IsNullOrWhiteSpace(newFile))
            {
                var ok3 = swApp.ReplaceReferencedDocument(
                    Path.GetFullPath(drawingPath),
                    newFile,
                    Path.GetFullPath(newModelPath));
                if (ok3)
                {
                    Logger.Info($"[Relink/Closed] Relinked using new file key: '{newFile}' → '{newModelPath}'.");
                    success = true;
                }
                else
                {
                    Logger.Warn("[Relink/Closed] All attempts returned false; will try in-session after opening.");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Relink/Closed] Exception: {ex.Message} (will try in-session after opening).");
        }

        return success;
    }
}
