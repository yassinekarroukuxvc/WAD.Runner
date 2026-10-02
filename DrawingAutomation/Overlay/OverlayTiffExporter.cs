using System;
using System.IO;

using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DrawingAutomation.Common;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Overlay
{
    public static class OverlayTiffExporter
    {
        private const int DefaultDpi = 200;
        private const bool DefaultMonochrome = false;

        public static void ExportOverlayTiff(
            SldWorks swApp,
            DrawingService ds,
            DrawingRun run,
            int dpi = DefaultDpi,
            bool monochrome = DefaultMonochrome)
        {
            try
            {
                ds.Rebuild();
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

                if (!DrawingExecutorCommon.SaveCurrentSheetAsTiff(swApp, ds, tiffPath, dpi, monochrome))
                    Logger.Warn("[Overlay] TIFF export reported failure; see logs above.");
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
    }
}