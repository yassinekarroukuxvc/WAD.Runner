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
        public static DrawingService InitializeAndRelink(
            SldWorks swApp,
            DrawingRun run,
            bool rebuildAfterOpen = false,
            bool zoomAfterOpen = false)
        {
            if (swApp is null) throw new ArgumentNullException(nameof(swApp));
            if (run is null) throw new ArgumentNullException(nameof(run));

            var destinationDrawing = Path.GetFullPath(run.ModDrawingPath);
            var templateDrawing = Path.GetFullPath(run.TemplateDrawingPath);
            var generatedPart = Path.GetFullPath(run.ModPartPath);
            var templatePart = string.IsNullOrWhiteSpace(run.TemplatePartPath)
                ? string.Empty
                : Path.GetFullPath(run.TemplatePartPath);

            CopyDrawingTemplate(templateDrawing, destinationDrawing);

            if (!File.Exists(generatedPart))
            {
                Logger.Warn(
                    $"[Init] Target part not found yet (relink will still try): {generatedPart}");
            }

            var closedRelinkOk = TryRelinkWhileClosed(
                swApp,
                destinationDrawing,
                templatePart,
                generatedPart);

            var drawingService = new DrawingService(swApp);

            try
            {
                drawingService.OpenDrawing(destinationDrawing, rebuildAfterOpen: false);

                if (!closedRelinkOk)
                {
                    drawingService.ReplaceReferencedModel(
                        destinationDrawing,
                        templatePart,
                        generatedPart);
                }

                if (rebuildAfterOpen)
                    drawingService.Rebuild(redraw: false);

                if (zoomAfterOpen)
                    drawingService.ZoomToSheet();

                return drawingService;
            }
            catch
            {
                drawingService.Close();
                throw;
            }
        }

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

        private static void CopyDrawingTemplate(
            string templateDrawing,
            string destinationDrawing)
        {
            if (!File.Exists(templateDrawing))
                throw new FileNotFoundException("Drawing template was not found.", templateDrawing);

            if (string.Equals(
                    templateDrawing,
                    destinationDrawing,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The destination drawing cannot be the same file as the drawing template.");
            }

            var destinationDirectory = Path.GetDirectoryName(destinationDrawing)
                ?? throw new InvalidOperationException(
                    $"Invalid destination drawing path: '{destinationDrawing}'");

            Directory.CreateDirectory(destinationDirectory);

            var temporaryDrawing = Path.Combine(
                destinationDirectory,
                $".{Path.GetFileName(destinationDrawing)}.{Guid.NewGuid():N}.tmp");

            try
            {
                File.Copy(templateDrawing, temporaryDrawing, overwrite: true);

                if (File.Exists(destinationDrawing))
                {
                    try { File.SetAttributes(destinationDrawing, FileAttributes.Normal); }
                    catch (Exception ex)
                    {
                        Logger.Warn(
                            $"[Init] Could not normalize existing drawing attributes: {ex.Message}");
                    }
                }

                File.Move(temporaryDrawing, destinationDrawing, overwrite: true);
                Logger.Info($"[Init] Copied drawing template → '{destinationDrawing}'");
            }
            finally
            {
                if (File.Exists(temporaryDrawing))
                {
                    try { File.Delete(temporaryDrawing); }
                    catch { }
                }
            }

            if (!File.Exists(destinationDrawing))
            {
                throw new FileNotFoundException(
                    "Destination drawing is missing after template copy.",
                    destinationDrawing);
            }
        }

        private static bool ConfigureTiffExport(SldWorks swApp, DrawingService ds, int dpi, bool monochrome)
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

                if (monochrome)
                {
                    // 1-bit line art + Group 4 fax compression: crisp, tiny files. Best at >= 400 DPI.
                    swApp.SetUserPreferenceIntegerValue(
                        (int)swUserPreferenceIntegerValue_e.swTiffImageType,
                        (int)swTiffImageType_e.swTiffImageBlackAndWhite);

                    swApp.SetUserPreferenceIntegerValue(
                        (int)swUserPreferenceIntegerValue_e.swTiffCompressionScheme,
                        (int)swTiffCompressionScheme_e.swTiffGroup4FaxCompression);
                }
                else
                {
                    swApp.SetUserPreferenceIntegerValue(
                        (int)swUserPreferenceIntegerValue_e.swTiffImageType,
                        (int)swTiffImageType_e.swTiffImageRGB);

                    swApp.SetUserPreferenceIntegerValue(
                        (int)swUserPreferenceIntegerValue_e.swTiffCompressionScheme,
                        (int)swTiffCompressionScheme_e.swTiffPackbitsCompression);
                }

                var sheet = (Sheet)drw.GetCurrentSheet();
                double w_m = 0, h_m = 0;
                sheet.GetSize(ref w_m, ref h_m);

                double w_in = w_m / 0.0254;
                double h_in = h_m / 0.0254;

                int applied = swApp.GetUserPreferenceIntegerValue(
                    (int)swUserPreferenceIntegerValue_e.swTiffPrintDPI);

                Logger.Info(
                    $"[TIFF] Using SHEET size: {w_in:F4} × {h_in:F4} in @ {applied} DPI " +
                    $"(≈ {Math.Round(w_in * applied)} × {Math.Round(h_in * applied)} px), " +
                    $"mode={(monochrome ? "1-bit/G4" : "RGB/PackBits")}");

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"[TIFF] ConfigureTiffExport({dpi}) failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Finds the "color" member of swPageSetupDrawingColor_e at runtime (name contains "Color",
        /// but not "Black" or "Automatic") and logs all members so the real names are visible.
        /// Returns null if no suitable member exists.
        /// </summary>
        private static int? ResolveColorModeValue()
        {
            int? result = null;
            var members = new List<string>();

            foreach (swPageSetupDrawingColor_e value in Enum.GetValues(typeof(swPageSetupDrawingColor_e)))
            {
                var name = value.ToString();
                members.Add($"{name}={(int)value}");

                if (result.HasValue) continue;

                bool isColor = name.IndexOf("Color", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isBlack = name.IndexOf("Black", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isAuto = name.IndexOf("Automatic", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isColor && !isBlack && !isAuto)
                    result = (int)value;
            }

            Logger.Info($"[TIFF] swPageSetupDrawingColor_e members: {string.Join(", ", members)}");

            if (!result.HasValue)
                Logger.Warn("[TIFF] No color member found in swPageSetupDrawingColor_e; skipping page setup color change.");

            return result;
        }

        /// <summary>
        /// Sets Page Setup > Drawing color to the color mode on the document-level and
        /// app-level page setup objects (DrawingColor is not available at sheet level).
        /// Returns the original values so they can be restored after the export.
        /// </summary>
        private static (int? Doc, int? App) SetPrintColor(SldWorks swApp, ModelDoc2 doc)
        {
            int? originalDoc = null;
            int? originalApp = null;

            var colorValue = ResolveColorModeValue();
            if (!colorValue.HasValue)
                return (null, null);

            try
            {
                if (doc.PageSetup is IPageSetup docSetup)
                {
                    originalDoc = docSetup.DrawingColor;
                    docSetup.DrawingColor = colorValue.Value;
                    Logger.Info($"[TIFF] Doc page setup drawing color: {originalDoc} -> {docSetup.DrawingColor}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[TIFF] Could not set doc-level drawing color: {ex.Message}");
            }

            try
            {
                if (doc.Extension.AppPageSetup is IPageSetup appSetup)
                {
                    originalApp = appSetup.DrawingColor;
                    appSetup.DrawingColor = colorValue.Value;
                    Logger.Info($"[TIFF] App page setup drawing color: {originalApp} -> {appSetup.DrawingColor}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[TIFF] Could not set app-level drawing color: {ex.Message}");
            }

            return (originalDoc, originalApp);
        }

        private static void RestorePrintColor(SldWorks swApp, ModelDoc2 doc, (int? Doc, int? App) original)
        {
            try
            {
                if (original.Doc.HasValue && doc.PageSetup is IPageSetup docSetup)
                    docSetup.DrawingColor = original.Doc.Value;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[TIFF] Could not restore doc-level drawing color: {ex.Message}");
            }

            try
            {
                if (original.App.HasValue && doc.Extension.AppPageSetup is IPageSetup appSetup)
                    appSetup.DrawingColor = original.App.Value;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[TIFF] Could not restore app-level drawing color: {ex.Message}");
            }
        }

        private static bool SaveCurrentSheetAsTiffUseSheetSize(
            SldWorks swApp,
            DrawingService ds,
            string outputFullPath,
            int dpi,
            bool monochrome)
        {
            ModelDoc2? colorDoc = null;
            (int? Doc, int? App) originalColor = (null, null);

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

                if (!ConfigureTiffExport(swApp, ds, dpi, monochrome))
                    return false;

                // Keep the drawing's original colors: force the color mode for RGB exports.
                if (!monochrome)
                {
                    colorDoc = doc;
                    originalColor = SetPrintColor(swApp, doc);
                }

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
            finally
            {
                if (colorDoc != null)
                    RestorePrintColor(swApp, colorDoc, originalColor);
            }
        }

        public static bool SaveCurrentSheetAsTiff(
            SldWorks swApp,
            DrawingService ds,
            string outputFullPath,
            int dpi,
            bool monochrome = false,
            int supersample = 2)
        {
            if (monochrome || supersample <= 1)
                return SaveCurrentSheetAsTiffUseSheetSize(swApp, ds, outputFullPath, dpi, monochrome);

            return SaveCurrentSheetAsTiffSupersampled(swApp, ds, outputFullPath, dpi, supersample);
        }

        private static bool SaveCurrentSheetAsTiffSupersampled(
            SldWorks swApp,
            DrawingService ds,
            string outputFullPath,
            int targetDpi,
            int factor)
        {
            string? tempPath = null;
            try
            {
                var drw = ds.Drawing as DrawingDoc;
                if (drw == null)
                {
                    Logger.Error("[TIFF] No active drawing document.");
                    return false;
                }

                // Exact pixel size a direct export at targetDpi would produce.
                var sheet = (Sheet)drw.GetCurrentSheet();
                double w_m = 0, h_m = 0;
                sheet.GetSize(ref w_m, ref h_m);
                int targetW = (int)Math.Round(w_m / 0.0254 * targetDpi);
                int targetH = (int)Math.Round(h_m / 0.0254 * targetDpi);

                var dir = Path.GetDirectoryName(outputFullPath) ?? Path.GetTempPath();
                tempPath = Path.Combine(dir, $".hires.{Guid.NewGuid():N}.tif");

                Logger.Info($"[TIFF] Supersampling: rendering at {targetDpi * factor} DPI, " +
                            $"downsampling to {targetW} × {targetH} px @ {targetDpi} DPI");

                if (!SaveCurrentSheetAsTiffUseSheetSize(swApp, ds, tempPath, targetDpi * factor, false))
                    return false;

                DownsampleTiff(tempPath, outputFullPath, targetW, targetH, targetDpi);

                Logger.Success($"[TIFF] Saved (supersampled {factor}x): {outputFullPath}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"[TIFF] Supersampled export failed: {ex.Message}");
                return false;
            }
            finally
            {
                if (tempPath != null && File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }

        private static void DownsampleTiff(
            string sourcePath,
            string destinationPath,
            int targetWidth,
            int targetHeight,
            int dpi)
        {
            using (var src = new System.Drawing.Bitmap(sourcePath))
            using (var dst = new System.Drawing.Bitmap(
                       targetWidth,
                       targetHeight,
                       System.Drawing.Imaging.PixelFormat.Format24bppRgb))
            {
                dst.SetResolution(dpi, dpi);

                using (var g = System.Drawing.Graphics.FromImage(dst))
                {
                    g.Clear(System.Drawing.Color.White);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;

                    // TileFlipXY avoids a faint border artifact at the image edges.
                    using (var attrs = new System.Drawing.Imaging.ImageAttributes())
                    {
                        attrs.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
                        g.DrawImage(
                            src,
                            new System.Drawing.Rectangle(0, 0, targetWidth, targetHeight),
                            0, 0, src.Width, src.Height,
                            System.Drawing.GraphicsUnit.Pixel,
                            attrs);
                    }
                }

                var tiffCodec = System.Drawing.Imaging.ImageCodecInfo
                    .GetImageEncoders()
                    .First(c => c.MimeType == "image/tiff");

                using (var encoderParams = new System.Drawing.Imaging.EncoderParameters(1))
                {
                    encoderParams.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                        System.Drawing.Imaging.Encoder.Compression,
                        (long)System.Drawing.Imaging.EncoderValue.CompressionLZW);

                    dst.Save(destinationPath, tiffCodec, encoderParams);
                }
            }
        }

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