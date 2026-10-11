using System;
using System.IO;
using System.Linq;

using System.Collections.Generic;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;
using WAD.Runner.DrawingAutomation.Common;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Overlay
{
    public static class OverlayTiffExporter
    {
        private const int DefaultDpi = 200;
        private const bool DefaultMonochrome = false;
        private const double OverlayLineWeightFactor = 0.5;

        public static void ExportOverlayTiff(
            SldWorks swApp,
            DrawingService ds,
            DrawingRun run,
            int dpi = DefaultDpi,
            bool monochrome = DefaultMonochrome,
            Action<DrawingService>? afterTextStyle = null)
        {
            try
            {
                // Step logs: an intermittent SolidWorks hang occurs somewhere in these steps.
                Logger.Info("[Overlay/TIFF] Removing legacy coining notes...");
                RemoveTinyLegacyCoiningNotes(ds);
                Logger.Info("[Overlay/TIFF] Rebuild...");
                ds.Rebuild();
                Logger.Info("[Overlay/TIFF] Applying text style...");
                DrawingTextStyle.ApplyToAnnotations(ds.Model as ModelDoc2);
                Logger.Info("[Overlay/TIFF] Applying reference layout...");
                afterTextStyle?.Invoke(ds);
                Logger.Info("[Overlay/TIFF] Rebuild + save...");
                ds.Rebuild(redraw: true);
                ds.Save();

                string tiffPath;
                if (!string.IsNullOrWhiteSpace(run.OutputTiffPath))
                {
                    tiffPath = Path.GetFullPath(run.OutputTiffPath);
                }
                else
                {
                    var basePath = !string.IsNullOrWhiteSpace(run.OutputPdfPath)
                        ? Path.GetFullPath(run.OutputPdfPath)
                        : Path.GetFullPath(run.ModDrawingPath);

                    tiffPath = Path.ChangeExtension(basePath, ".tif");
                }

                var outputDirectory = Path.GetDirectoryName(tiffPath);
                if (!string.IsNullOrWhiteSpace(outputDirectory))
                    Directory.CreateDirectory(outputDirectory);

                var restoreLineWeights = ScalePrintLineWeights(swApp, ds.Model as ModelDoc2, OverlayLineWeightFactor);
                bool exported;
                try
                {
                    exported = DrawingExecutorCommon.SaveCurrentSheetAsTiff(swApp, ds, tiffPath, dpi, monochrome,
                        supersample: 4, outputWidth: 1280, outputHeight: 960);
                }
                finally
                {
                    restoreLineWeights();
                }

                if (!exported)
                    Logger.Warn("[Overlay] TIFF export reported failure; see logs above.");
                else
                    MaskOutsideBorder(tiffPath, dpi);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Overlay] TIFF export step failed (continuing to close): {ex.Message}");
            }
            finally
            {
                try { ds.SaveAndClose(); } catch { }
            }
        }

        /// <summary>
        /// Overlay lines are drawn at half the normal print line weight. The TIFF is a print
        /// capture, so it uses the printer line weights (thin ... thick6); they are scaled for the
        /// export only and restored afterwards, so the saved drawing keeps its settings.
        /// </summary>
        private static Action ScalePrintLineWeights(SldWorks swApp, ModelDoc2? model, double factor)
        {
            var settings = new[]
            {
                swUserPreferenceDoubleValue_e.swPageSetupPrinterThinLineWeight,
                swUserPreferenceDoubleValue_e.swPageSetupPrinterNormalLineWeight,
                swUserPreferenceDoubleValue_e.swPageSetupPrinterThickLineWeight,
                swUserPreferenceDoubleValue_e.swPageSetupPrinterThick2LineWeight,
                swUserPreferenceDoubleValue_e.swPageSetupPrinterThick3LineWeight,
                swUserPreferenceDoubleValue_e.swPageSetupPrinterThick4LineWeight,
                swUserPreferenceDoubleValue_e.swPageSetupPrinterThick5LineWeight,
                swUserPreferenceDoubleValue_e.swPageSetupPrinterThick6LineWeight,
            };
            const int noOption = (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified;
            var restore = new List<Action>();

            foreach (var setting in settings)
            {
                var id = (int)setting;
                try
                {
                    var app = swApp.GetUserPreferenceDoubleValue(id);
                    if (app > 0 && swApp.SetUserPreferenceDoubleValue(id, app * factor))
                        restore.Add(() => swApp.SetUserPreferenceDoubleValue(id, app));

                    if (model is not null)
                    {
                        var doc = model.Extension.GetUserPreferenceDouble(id, noOption);
                        if (doc > 0 && model.Extension.SetUserPreferenceDouble(id, noOption, doc * factor))
                            restore.Add(() => model.Extension.SetUserPreferenceDouble(id, noOption, doc));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[Overlay] Line weight {setting} not scaled: {ex.Message}");
                }
            }

            Logger.Info($"[Overlay] Print line weights scaled x{factor} for the TIFF ({restore.Count} values).");
            return () =>
            {
                foreach (var action in restore)
                {
                    try { action(); } catch { }
                }
            };
        }

        /// <summary>
        /// Whitens everything outside the blue sheet border, so geometry that runs past the frame
        /// does not show. The image keeps its 1280 x 960 size and scale (no crop or resample), so
        /// the overlay magnification is unchanged.
        /// </summary>
        private static void MaskOutsideBorder(string tiffPath, int dpi)
        {
            // Read through a memory copy: GDI+ cannot save over a file it still has open.
            System.Drawing.Bitmap image;
            using (var stream = new MemoryStream(File.ReadAllBytes(tiffPath)))
            using (var loaded = new System.Drawing.Bitmap(stream))
            {
                image = new System.Drawing.Bitmap(loaded.Width, loaded.Height,
                    System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                // Same resolution on both, so the copy is pixel for pixel (no rescale).
                image.SetResolution(loaded.HorizontalResolution, loaded.VerticalResolution);
                using var g = System.Drawing.Graphics.FromImage(image);
                g.DrawImageUnscaled(loaded, 0, 0);
            }

            using (image)
            {
                var w = image.Width;
                var h = image.Height;
                var rect = new System.Drawing.Rectangle(0, 0, w, h);
                var data = image.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite,
                    System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                try
                {
                    var stride = data.Stride;
                    var pixels = new byte[stride * h];
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

                    // Border lines are the outermost columns/rows drawn over most of their length. Any
                    // colour counts: where the part's cut face lies on the border it covers the blue.
                    bool IsDrawn(int x, int y)
                    {
                        var i = y * stride + x * 3; // BGR
                        return Math.Min(pixels[i], Math.Min(pixels[i + 1], pixels[i + 2])) < 200;
                    }

                    int left = -1, right = -1, top = -1, bottom = -1;
                    for (var x = 0; x < w; x++)
                    {
                        var count = 0;
                        for (var y = 0; y < h; y++) if (IsDrawn(x, y)) count++;
                        if (count < h * 0.6) continue;
                        if (left < 0) left = x;
                        right = x;
                    }
                    for (var y = 0; y < h; y++)
                    {
                        var count = 0;
                        for (var x = 0; x < w; x++) if (IsDrawn(x, y)) count++;
                        if (count < w * 0.6) continue;
                        if (top < 0) top = y;
                        bottom = y;
                    }

                    if (left < 0 || top < 0 || right - left < w / 2 || bottom - top < h / 2)
                    {
                        Logger.Warn("[Overlay] TIFF border not found; outside of the border left unchanged.");
                        return;
                    }

                    for (var y = 0; y < h; y++)
                    for (var x = 0; x < w; x++)
                    {
                        if (x >= left && x <= right && y >= top && y <= bottom) continue;
                        var i = y * stride + x * 3;
                        pixels[i] = pixels[i + 1] = pixels[i + 2] = 255;
                    }

                    System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
                    Logger.Info($"[Overlay] TIFF masked outside border x={left}..{right}, y={top}..{bottom} px.");
                }
                finally
                {
                    image.UnlockBits(data);
                }

                image.SetResolution(dpi, dpi);
                var tiffCodec = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
                    .First(c => c.MimeType == "image/tiff");
                using var encoderParams = new System.Drawing.Imaging.EncoderParameters(1);
                encoderParams.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                    System.Drawing.Imaging.Encoder.Compression,
                    (long)System.Drawing.Imaging.EncoderValue.CompressionLZW);
                var tempPath = tiffPath + ".masked.tmp";
                image.Save(tempPath, tiffCodec, encoderParams);
                File.Move(tempPath, tiffPath, overwrite: true);
            }
        }

        private static void RemoveTinyLegacyCoiningNotes(DrawingService ds)
        {
            if (ds.Model is not ModelDoc2 model || ds.Drawing is not DrawingDoc drawing) return;
            model.ClearSelection2(true);
            var selected = 0;
            try
            {
                for (var view = drawing.GetFirstView() as View; view is not null; view = view.GetNextView() as View)
                {
                    if (view.GetAnnotations() is not object[] items) continue;
                    foreach (var item in items)
                    {
                        if (item is not Annotation ann || ann.GetSpecificAnnotation() is not Note note) continue;
                        var text = (note.GetText() ?? "").Replace("\r", "").Trim();
                        if (text != "FOR COINING USE\n  221-02051-MA" ||
                            ann.GetTextFormat(0) is not TextFormat format || format.CharHeight >= 0.0001) continue;
                        if (ann.Select3(true, null)) selected++;
                    }
                }
                if (selected > 0)
                {
                    if (!model.Extension.DeleteSelection2(0))
                        throw new InvalidOperationException("Could not remove tiny legacy coining notes.");
                    Logger.Info($"[Overlay] Removed {selected} tiny legacy template coining notes.");
                }
            }
            finally { model.ClearSelection2(true); }
        }
    }
}
