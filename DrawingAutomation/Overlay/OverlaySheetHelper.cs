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
using WAD.Runner.DrawingAutomation.Core;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Overlay
{
    public static class OverlaySheetHelper
    {
        public static DrawingService OpenRelinkAndPrepareOverlaySheet(
            SldWorks swApp,
            DrawingRun run,
            DrawingData drawingData,
            out IDictionary<string, string> nameMap)
        {
            if (swApp is null) throw new ArgumentNullException(nameof(swApp));
            if (run is null) throw new ArgumentNullException(nameof(run));
            if (drawingData is null) throw new ArgumentNullException(nameof(drawingData));

            var profile = DrawingProfileResolver.Resolve(run, drawingData);

            Logger.Info("[Open] Open + relink overlay drawing…");
            var ds = DrawingExecutorCommon.InitializeAndRelink(swApp, run);

            try
            {
                Logger.Info("[Sheet] Activate overlay sheet via profile…");
                var availableSheets = ds.GetSheetNames();
                var sheetName = profile.SheetSelector(availableSheets);

                TryActivateSheet(ds, sheetName);

                Logger.Info("[Sheet] Delete non-target sheets…");
                DeleteAllSheetsExcept(ds, sheetName);

                ds.ZoomToSheet();

                nameMap = profile.Views.ToLogicalMap();
                return ds;
            }
            catch
            {
                ds.Close();
                throw;
            }
        }

        public static void TryActivateSheet(DrawingService ds, string sheetName)
        {
            try
            {
                var drawing = ds.Drawing ?? throw new InvalidOperationException("No active drawing.");
                drawing.ActivateSheet(sheetName);
                Logger.Info($"Activated sheet: {sheetName}");
                ds.Rebuild();
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to activate sheet '{sheetName}': {ex.Message}");
            }
        }

        public static void DeleteAllSheetsExcept(DrawingService ds, string sheetName)
        {
            var del = ds.DeleteAllSheetsExcept2(sheetName);
            if (!del.Ok)
            {
                if (del.NotDeleted.Count > 0)
                    Logger.Warn($"Some sheets could not be deleted: {string.Join(", ", del.NotDeleted)}");
                else
                    Logger.Warn("DeleteAllSheetsExcept encountered an error (continuing).");
            }
            else if (del.Deleted.Count > 0)
            {
                Logger.Info($"Deleted sheets: {string.Join(", ", del.Deleted)}");
            }
        }

    }
}
