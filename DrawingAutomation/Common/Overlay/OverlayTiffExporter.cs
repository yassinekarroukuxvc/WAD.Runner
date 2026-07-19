using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Planning;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;

using WAD.Runner.DrawingAutomation.Common;
using WAD.Runner.DrawingAutomation.Metadata;
using WAD.Runner.DrawingAutomation.Overlay;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.SolidWorks;
using WAD.Runner.DrawingAutomation.Tables;
using WAD.Runner.DrawingAutomation.Views;

namespace WAD.Runner.DrawingAutomation.Common.Overlay
{
    public static class OverlayTiffExporter
    {
        public static void ExportOverlayTiff(SldWorks swApp, DrawingService ds, DrawingRun run)
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

                if (!DrawingExecutorCommon.SaveCurrentSheetAsTiff(swApp, ds, tiffPath, 200))
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
