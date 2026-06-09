using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Common
{
    public static class DrawingExecutorCommon
    {
        /// <summary>
        /// Copy template → try relink while closed → open drawing → optional in-session relink.
        /// Rebuild/zoom are optional because they are expensive and should only happen when needed.
        /// </summary>
        public static DrawingService InitializeAndRelink(
            SldWorks swApp,
            DrawingRun run,
            bool rebuildAfterOpen = false,
            bool zoomAfterOpen = false)
        {
            if (swApp is null) throw new ArgumentNullException(nameof(swApp));
            if (run is null) throw new ArgumentNullException(nameof(run));

            var destDrw = Path.GetFullPath(run.ModDrawingPath);
            var srcDrw = Path.GetFullPath(run.TemplateDrawingPath);
            var newPart = Path.GetFullPath(run.ModPartPath);
            var oldPart = string.IsNullOrWhiteSpace(run.TemplatePartPath)
                ? string.Empty
                : Path.GetFullPath(run.TemplatePartPath);

            var destDir = Path.GetDirectoryName(destDrw)
                ?? throw new InvalidOperationException($"Invalid destination drawing path: '{destDrw}'");

            Directory.CreateDirectory(destDir);

            if (File.Exists(destDrw))
            {
                try { File.SetAttributes(destDrw, FileAttributes.Normal); } catch { }
                File.Delete(destDrw);
                Logger.Info($"[Init] Deleted existing destination drawing → '{destDrw}'");
            }

            File.Copy(srcDrw, destDrw, overwrite: true);
            Logger.Info($"[Init] Copied drawing template → '{destDrw}'");

            if (!File.Exists(destDrw))
                throw new FileNotFoundException("Destination drawing missing after copy.", destDrw);

            if (!File.Exists(newPart))
                Logger.Warn($"[Init] Target part not found yet (relink will still try): {newPart}");

            var closedRelinkOk = TryRelinkWhileClosed(swApp, destDrw, oldPart, newPart);

            var ds = new DrawingService(swApp);
            ds.OpenDrawing(destDrw, rebuildAfterOpen: false);

            if (!closedRelinkOk)
                ds.ReplaceReferencedModel(destDrw, oldPart, newPart);

            if (rebuildAfterOpen)
                ds.Rebuild(redraw: false);

            if (zoomAfterOpen)
                ds.ZoomToSheet();

            return ds;
        }

        /// <summary>
        /// Save, export PDF if requested, then close.
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
                        Exporter.SavePdfAllSheets(swApp, ds, Path.GetFullPath(pdfOutputPath));
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
                try { ds.Close(); } catch { }
            }
        }

        // ───────────────────────────────────────────────────────────────────────
        // TIFF EXPORT HELPERS
        // ───────────────────────────────────────────────────────────────────────

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

                swApp.SetUserPreferenceIntegerValue(
                    (int)swUserPreferenceIntegerValue_e.swTiffScreenOrPrintCapture,
                    1);

                swApp.SetUserPreferenceToggle(
                    (int)swUserPreferenceToggle_e.swTiffPrintUseSheetSize,
                    true);

                swApp.SetUserPreferenceIntegerValue(
                    (int)swUserPreferenceIntegerValue_e.swTiffPrintDPI,
                    dpi);

                swApp.SetUserPreferenceIntegerValue(
                    (int)swUserPreferenceIntegerValue_e.swTiffImageType,
                    (int)swTiffImageType_e.swTiffImageRGB);

                swApp.SetUserPreferenceIntegerValue(
                    (int)swUserPreferenceIntegerValue_e.swTiffCompressionScheme,
                    (int)swTiffCompressionScheme_e.swTiffPackbitsCompression);

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

        private static bool SaveCurrentSheetAsTiffUseSheetSize(
            SldWorks swApp,
            DrawingService ds,
            string outputFullPath,
            int dpi)
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

                ds.RunInFastMode(() =>
                {
                    try { drw.EditSheet(); } catch { }
                    try { doc.EditRebuild3(); } catch { }
                });

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

        public static bool ConfigureTiffExportSheetSize100Dpi(SldWorks swApp, DrawingService ds)
            => ConfigureTiffExportUseSheetSize(swApp, ds, 100);

        public static bool SaveCurrentSheetAsTiff100Dpi(SldWorks swApp, DrawingService ds, string outputFullPath)
            => SaveCurrentSheetAsTiffUseSheetSize(swApp, ds, outputFullPath, 100);

        public static bool ConfigureTiffExportSheetSize200Dpi(SldWorks swApp, DrawingService ds)
            => ConfigureTiffExportUseSheetSize(swApp, ds, 200);

        public static bool SaveCurrentSheetAsTiff200Dpi(SldWorks swApp, DrawingService ds, string outputFullPath)
            => SaveCurrentSheetAsTiffUseSheetSize(swApp, ds, outputFullPath, 200);

        public static bool SaveCurrentSheetAsTiff(SldWorks swApp, DrawingService ds, string outputFullPath, int dpi)
            => SaveCurrentSheetAsTiffUseSheetSize(swApp, ds, outputFullPath, dpi);

        // ───────────────────────────────────────────────────────────────────────

        private static bool TryRelinkWhileClosed(
            SldWorks swApp,
            string drawingPath,
            string oldModelPath,
            string newModelPath)
        {
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

                drawingPath = Path.GetFullPath(drawingPath);
                newModelPath = Path.GetFullPath(newModelPath);

                foreach (var candidate in BuildRelinkCandidates(oldModelPath, newModelPath))
                {
                    var ok = swApp.ReplaceReferencedDocument(drawingPath, candidate, newModelPath);
                    if (ok)
                    {
                        Logger.Info($"[Relink/Closed] Relinked '{candidate}' -> '{newModelPath}'.");
                        return true;
                    }

                    Logger.Warn($"[Relink/Closed] ReplaceReferencedDocument returned false for key '{candidate}'.");
                }

                Logger.Warn("[Relink/Closed] All attempts returned false; will try in-session after opening.");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Relink/Closed] Exception: {ex.Message} (will try in-session after opening).");
                return false;
            }
        }

        private static IEnumerable<string> BuildRelinkCandidates(string oldModelPath, string newModelPath)
        {
            var candidates = new List<string>();

            void Add(string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                if (candidates.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase))) return;
                candidates.Add(value);
            }

            if (!string.IsNullOrWhiteSpace(oldModelPath))
            {
                if (File.Exists(oldModelPath))
                    Add(Path.GetFullPath(oldModelPath));

                Add(Path.GetFileName(oldModelPath));
            }

            Add(Path.GetFileName(newModelPath));
            return candidates;
        }
    }
}
