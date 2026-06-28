using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using WAD.Runner.Application;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Common;

public static class Exporter
{
    public static void SavePdfAllSheets(SldWorks app, DrawingService ds, string outputPath)
    {
        if (app is null) throw new ArgumentNullException(nameof(app));
        if (ds is null) throw new ArgumentNullException(nameof(ds));
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentNullException(nameof(outputPath));
        if (ds.Model is null || ds.Drawing is null) throw new InvalidOperationException("No active drawing.");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var model = ds.Model;
        var drawing = ds.Drawing;
        var ext = model.Extension;

        var wrappers = GetAllSheetWrappers(drawing);
        if (wrappers.Length == 0)
        {
            Logger.Warn("[PDF] No sheets found to export.");
            return;
        }

        var pdfData = (ExportPdfData)app.GetExportFileData((int)swExportDataFileType_e.swExportPdfData);
        pdfData.SetSheets((int)swExportDataSheetsToExport_e.swExportData_ExportSpecifiedSheets, wrappers);
        pdfData.ViewPdfAfterSaving = false;

        bool prevColor = app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swPDFExportInColor);
        bool prevHiQ = app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swPDFExportShadedEdgesHighQuality);
        bool prevLines = app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swPDFExportUseCurrentPrintLineWeights);

        app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swPDFExportInColor, false);
        app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swPDFExportShadedEdgesHighQuality, true);
        app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swPDFExportUseCurrentPrintLineWeights, true);

        try
        {
            int errs = 0, warns = 0;
            bool ok = ext.SaveAs3(
                outputPath,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                pdfData,
                null,
                ref errs,
                ref warns);

            if (!ok)
            {
                Logger.Warn($"[PDF] Export failed. SW error={errs}, warn={warns}");
            }
            else
            {
                Logger.Success($"[PDF] Saved → '{outputPath}'");
            }
        }
        finally
        {
            app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swPDFExportInColor, prevColor);
            app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swPDFExportShadedEdgesHighQuality, prevHiQ);
            app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swPDFExportUseCurrentPrintLineWeights, prevLines);
        }
    }

    private static DispatchWrapper[] GetAllSheetWrappers(DrawingDoc drawing)
    {
        try
        {
            var namesObj = drawing.GetSheetNames() as object[];
            string[] names;

            if (namesObj is null)
            {
                dynamic dd = drawing;
                object[] alt = dd.GetSheetNames();
                names = Array.ConvertAll(alt, o => o?.ToString() ?? "");
            }
            else
            {
                names = Array.ConvertAll(namesObj, o => o?.ToString() ?? "");
            }

            var list = new System.Collections.Generic.List<DispatchWrapper>();
            foreach (var n in names)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                try
                {
                    drawing.ActivateSheet(n);
                    if (drawing.GetCurrentSheet() is Sheet sheet)
                        list.Add(new DispatchWrapper(sheet));
                }
                catch {  }
            }
            return list.ToArray();
        }
        catch
        {
            return Array.Empty<DispatchWrapper>();
        }
    }
}
