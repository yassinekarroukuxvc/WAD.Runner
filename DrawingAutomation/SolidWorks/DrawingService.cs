using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Reflection;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using WAD.Runner.Application;

namespace WAD.Runner.DrawingAutomation.SolidWorks
{
    public sealed class DrawingService
    {
        // ───────────────────────────────────────────────────────────────────────
        // Nested types
        // ───────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Outcome of a "delete all sheets except" operation.
        /// </summary>
        public sealed record DeleteSheetsResult(
            bool Ok,
            string KeepSheet,
            IReadOnlyList<string> Deleted,
            IReadOnlyList<string> NotDeleted
        );

        // ───────────────────────────────────────────────────────────────────────
        // Fields
        // ───────────────────────────────────────────────────────────────────────

        private readonly SldWorks _swApp;

        private ModelDoc2? _model;
        private DrawingDoc? _drawing;
        private ModelDocExtension? _modelExt;

        private string _drawingPath = string.Empty;
        private int _error;
        private int _warning;

        public DrawingService(SldWorks swApp) => _swApp = swApp ?? throw new ArgumentNullException(nameof(swApp));

        public ModelDoc2? Model => _model;
        public DrawingDoc? Drawing => _drawing;

        // ───────────────────────────────────────────────────────────────────────
        // Open / Relink / Lifecycle
        // ───────────────────────────────────────────────────────────────────────

        public void OpenDrawing(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));

            _drawingPath = Path.GetFullPath(filePath);
            if (!File.Exists(_drawingPath))
                throw new FileNotFoundException($"Drawing file not found at '{_drawingPath}'", _drawingPath);

            _error = _warning = 0;

            var doc = _swApp.OpenDoc6(
                _drawingPath,
                (int)swDocumentTypes_e.swDocDRAWING,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                "",
                ref _error,
                ref _warning);

            if (doc is null)
            {
                throw new InvalidOperationException(
                    $"Failed to open drawing. SW error={_error}, warn={_warning}. Path='{_drawingPath}'.");
            }

            try { int actErr = 0; _swApp.ActivateDoc2(Path.GetFileName(_drawingPath), false, ref actErr); } catch { }
            Thread.Sleep(50);

            _model = doc;
            if (_model.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
                throw new InvalidCastException("Opened document is not a drawing.");

            _drawing = (DrawingDoc)_model;
            _modelExt = _model.Extension;

            try { _model.Lock(); } catch { }

            Logger.Info($"Opened drawing: {Path.GetFileName(_drawingPath)}");
        }

        /// <summary>
        /// Wrapper that calls SW API ReplaceReferencedDocument with discovery fallback.
        /// If 'drawingPath' is known, this works even when no drawing is currently active.
        /// </summary>
        public void ReplaceReferencedModel(string drawingPath, string oldModelPath, string newModelPath)
        {
            if (string.IsNullOrWhiteSpace(drawingPath)) throw new ArgumentNullException(nameof(drawingPath));
            if (string.IsNullOrWhiteSpace(newModelPath)) throw new ArgumentNullException(nameof(newModelPath));

            drawingPath = Path.GetFullPath(drawingPath);
            newModelPath = Path.GetFullPath(newModelPath);

            if (!File.Exists(drawingPath))
            {
                Logger.Warn($"Relink aborted; drawing not found: {drawingPath}");
                return;
            }
            if (!File.Exists(newModelPath))
            {
                Logger.Warn($"Relink aborted; new model not found: {newModelPath}");
                return;
            }

            // 1) Try provided old path if given
            if (!string.IsNullOrWhiteSpace(oldModelPath))
            {
                oldModelPath = Path.GetFullPath(oldModelPath);
                if (_swApp.ReplaceReferencedDocument(drawingPath, oldModelPath, newModelPath))
                {
                    Logger.Info($"Relinked: '{oldModelPath}' → '{newModelPath}'.");
                    return;
                }
                Logger.Warn($"ReplaceReferencedDocument returned false (old='{oldModelPath}', new='{newModelPath}').");
            }

            // 2) Discover actual referenced paths from views
            var refPaths = EnumerateReferencedModelPaths().ToList();
            if (refPaths.Count == 0)
            {
                Logger.Warn("Relink: no referenced model paths discovered; skipping relink.");
                return;
            }

            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(oldModelPath))
                candidates.AddRange(refPaths.Where(p => string.Equals(p, oldModelPath, StringComparison.OrdinalIgnoreCase)));

            var newFile = Path.GetFileName(newModelPath);
            candidates.AddRange(refPaths.Where(p => string.Equals(Path.GetFileName(p), newFile, StringComparison.OrdinalIgnoreCase)));
            candidates.AddRange(refPaths);

            foreach (var oldPath in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (_swApp.ReplaceReferencedDocument(drawingPath, oldPath, newModelPath))
                    {
                        Logger.Info($"Relinked: '{oldPath}' → '{newModelPath}'.");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Relink attempt failed for '{oldPath}': {ex.Message}");
                }
            }

            Logger.Warn("Relink: all attempts failed (drawing may still use original reference).");
        }

        public void ReplaceReferencedModel(string oldModelPath, string newModelPath)
        {
            if (string.IsNullOrEmpty(_drawingPath))
                throw new InvalidOperationException("No drawing is open; call OpenDrawing first.");
            ReplaceReferencedModel(_drawingPath, oldModelPath, newModelPath);
        }

        public void Rebuild()
        {
            if (_model is null) return;

            try
            {
                bool ok = _model.EditRebuild3();
                if (!ok) Logger.Warn("EditRebuild3 returned false (continuing).");
            }
            catch
            {
                try { _model.ForceRebuild3(false); } catch { }
            }

            try { _model.GraphicsRedraw2(); } catch { }
        }

        public void ZoomToSheet()
        {
            if (_model is null) return;

            try { _modelExt?.ViewZoomToSheet(); }
            catch { try { _model.ViewZoomtofit2(); } catch { } }
        }

        public void Save()
        {
            if (_model is null) return;
            _error = _warning = 0;
            _model.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref _error, ref _warning);
            if (_error != 0) Logger.Warn($"Save failed. SW error={_error}, warn={_warning}");
        }

        public void SaveAndClose()
        {
            Save();
            if (!string.IsNullOrEmpty(_drawingPath))
            {
                try { _swApp.CloseDoc(_drawingPath); } catch { }
            }
        }

        public void Unlock()
        {
            try { _model?.UnLock(); } catch { }
        }

        // ───────────────────────────────────────────────────────────────────────
        // Sheet helpers
        // ───────────────────────────────────────────────────────────────────────

        public void ActivateSheet(string sheetName)
        {
            if (_drawing is null) return;
            try
            {
                _drawing.ActivateSheet(sheetName); // some interops return void
                Logger.Info($"Activated sheet: {sheetName}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"ActivateSheet('{sheetName}') failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Safe read of sheet names (filtered, case-insensitive friendly).
        /// </summary>
        public string[] GetSheetNames()
        {
            try
            {
                if (_drawing is null) return Array.Empty<string>();
                var names = (_drawing.GetSheetNames() as string[]) ?? Array.Empty<string>();
                return names.Where(n => !string.IsNullOrWhiteSpace(n)).ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static string? ResolveInsensitive(string[] haystack, string needle)
        {
            if (string.IsNullOrWhiteSpace(needle)) return null;
            return haystack.FirstOrDefault(n =>
                string.Equals(n?.Trim(), needle.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Delete all sheets except the one you want to keep (diagnostic version).
        /// Returns details of what got deleted / not deleted.
        /// </summary>
        public DeleteSheetsResult DeleteAllSheetsExcept2(string keepSheetName)
        {
            var deleted = new List<string>();
            var notDeleted = new List<string>();

            try
            {
                if (_drawing is null || _model is null)
                    return new DeleteSheetsResult(false, keepSheetName, deleted, notDeleted);

                var names = GetSheetNames();
                if (names.Length == 0)
                    return new DeleteSheetsResult(true, keepSheetName, deleted, notDeleted);

                var resolvedKeep = ResolveInsensitive(names, keepSheetName);
                if (string.IsNullOrEmpty(resolvedKeep))
                {
                    Logger.Warn($"Keep sheet '{keepSheetName}' not found. Aborting delete-all-except.");
                    return new DeleteSheetsResult(false, keepSheetName, deleted, names);
                }

                // Activate keep once (some APIs require a non-deleting context active)
                try { _drawing.ActivateSheet(resolvedKeep); } catch { }
                try { _drawing.EditSheet(); } catch { }

                foreach (var sheetName in names)
                {
                    if (string.IsNullOrWhiteSpace(sheetName)) continue;
                    if (string.Equals(sheetName, resolvedKeep, StringComparison.Ordinal)) continue;

                    try
                    {
                        _drawing.ActivateSheet(sheetName);
                        _model.ClearSelection2(true);

                        var selected = _model.Extension.SelectByID2(
                            sheetName, "SHEET", 0, 0, 0, false, 0, null, 0);

                        if (!selected)
                        {
                            Logger.Warn($"Could not select sheet '{sheetName}' for deletion (skipping).");
                            notDeleted.Add(sheetName);
                            continue;
                        }

                        if (!_model.Extension.DeleteSelection2(0))
                        {
                            Logger.Warn($"DeleteSelection2 failed for '{sheetName}'.");
                            notDeleted.Add(sheetName);
                        }
                        else
                        {
                            deleted.Add(sheetName);
                        }
                    }
                    catch
                    {
                        notDeleted.Add(sheetName);
                    }
                }

                // Return to keep
                try { _drawing.ActivateSheet(resolvedKeep); } catch { }
                try { _model.EditRebuild3(); } catch { }

                var ok = notDeleted.Count == 0;
                return new DeleteSheetsResult(ok, resolvedKeep, deleted, notDeleted);
            }
            catch
            {
                return new DeleteSheetsResult(false, keepSheetName, deleted, notDeleted);
            }
        }

        /// <summary>
        /// Back-compat wrapper. Prefer DeleteAllSheetsExcept2 for diagnostics.
        /// (This REPLACES any previous method with the same signature.)
        /// </summary>
        public bool DeleteAllSheetsExcept(string keepSheetName)
        {
            var res = DeleteAllSheetsExcept2(keepSheetName);
            return res.Ok;
        }

        // ───────────────────────────────────────────────────────────────────────
        // Metadata hooks (safe stubs; extend as needed)
        // ───────────────────────────────────────────────────────────────────────

        public void SetSummaryInformation(WAD.Runner.DataManagement.Domain.Drawing.DrawingData dd)
        {
            try { Logger.Info("Summary information hook called."); } catch { }
        }

        public void SetCustomProperties(WAD.Runner.DataManagement.Domain.Drawing.DrawingData dd)
        {
            try
            {
                if (_modelExt is null) return;
                var mgr = _modelExt.CustomPropertyManager[""];
                if (mgr is null) return;
                Logger.Info("Custom properties hook called.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"SetCustomProperties failed: {ex.Message}");
            }
        }

        // ───────────────────────────────────────────────────────────────────────
        // Overlay calibration helpers
        // ───────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Draws the overlay calibration square in the sheet format, using the overlay scaling (100X/200X/300X/400X buckets).
        /// </summary>
        public void DrawCalibrationBoxOnSheetFormat(double overlayScaling)
        {
            if (_drawing is null || _model is null)
            {
                Logger.Warn("[CalibrationBox] No drawing is open.");
                return;
            }

            var token = NormalizeScalingBucket(overlayScaling);

            // (x, y, width, height) in inches
            var (xIn, yIn, widthIn, heightIn) = token switch
            {
                100 => (2.355, 1.55, 1.68, 1.68),
                200 => (1.500, 0.705, 3.38, 3.38),
                300 => (1.755, 0.955, 2.88, 2.89),
                400 => (2.225, 1.425, 1.94, 1.94),
                _ => (1.755, 0.955, 2.88, 2.89),
            };

            const double IN_TO_M = 0.0254;

            // Convert to meters
            var x1 = xIn * IN_TO_M;
            var y1 = yIn * IN_TO_M;
            var x2 = (xIn + widthIn) * IN_TO_M;
            var y2 = y1;
            var x3 = x2;
            var y3 = (yIn + heightIn) * IN_TO_M;
            var x4 = x1;
            var y4 = y3;

            try
            {
                _drawing.EditTemplate();

                // Use a dedicated layer; rename if you have constants somewhere
                const string layerName = "calibration_box";
                EnsureLayerExistsAndVisible(layerName);
                _drawing.SetCurrentLayer(layerName);

                var sm = _model.SketchManager;
                sm.InsertSketch(true);

                var l1 = sm.CreateLine(x1, y1, 0, x2, y2, 0);
                var l2 = sm.CreateLine(x2, y2, 0, x3, y3, 0);
                var l3 = sm.CreateLine(x3, y3, 0, x4, y4, 0);
                var l4 = sm.CreateLine(x4, y4, 0, x1, y1, 0);

                sm.InsertSketch(true);
                _drawing.EditSheet();
                _model.GraphicsRedraw2();

                if (l1 == null || l2 == null || l3 == null || l4 == null)
                    Logger.Warn("[CalibrationBox] One or more CreateLine calls returned null.");
                else
                    Logger.Info($"[CalibrationBox] {token}X box drawn at ({xIn:0.###}, {yIn:0.###}) in, size {widthIn:0.###}×{heightIn:0.###} in.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[CalibrationBox] Failed to draw calibration box: {ex.Message}");
                try { _drawing.EditSheet(); } catch { }
            }
        }

        /// <summary>
        /// Inserts the overlay calibration note (e.g. "5µm") in the bottom-right corner of the calibration box.
        /// </summary>
        public void InsertCalibrationBoxNoteBottomRight(string calibrationValueMicrons, double overlayScaling)
        {
            if (_drawing is null || _model is null)
            {
                Logger.Warn("[CalibrationNote] No drawing is open.");
                return;
            }

            var token = NormalizeScalingBucket(overlayScaling);

            // These must match the box placement above
            var (xIn, yIn, wIn, hIn) = token switch
            {
                100 => (2.355, 1.55, 1.68, 1.68),
                200 => (1.500, 0.705, 3.38, 3.38),
                300 => (1.755, 0.955, 2.88, 2.89),
                400 => (2.225, 1.425, 1.94, 1.94),
                _ => (1.755, 0.955, 2.88, 2.89),
            };

            const double IN_TO_M = 0.0254;
            const double insetX = 0.08;   // inches from right edge
            const double insetY = 0.06;   // inches from bottom edge
            const double charH = 0.0016;  // meters (text height)

            double InToM(double vIn) => vIn * IN_TO_M;

            var posX = InToM(xIn + wIn - insetX);
            var posY = InToM(yIn + insetY) + charH * 0.75;

            try
            {
                _drawing.EditTemplate();
                const string layerName = "calibration_box";
                EnsureLayerExistsAndVisible(layerName);
                _drawing.SetCurrentLayer(layerName);

                var noteObj = _model.InsertNote($"{calibrationValueMicrons}µm");
                if (noteObj is not Note note)
                {
                    Logger.Warn("[CalibrationNote] InsertNote returned null.");
                    return;
                }

                if (note.GetAnnotation() is not Annotation ann)
                {
                    Logger.Warn("[CalibrationNote] Note.GetAnnotation() returned null.");
                    return;
                }

                ann.Layer = layerName;
                ann.SetPosition2(posX, posY, 0.0);

                var tf = (TextFormat)note.GetTextFormat();
                tf.CharHeight = charH;
                tf.TypeFaceName = "Arial";
                tf.Bold = false;
                tf.Italic = false;
                tf.Underline = false;

                try { ann.SetTextFormat(0, false, tf); } catch { }
                try { note.SetTextJustification((int)swTextJustification_e.swTextJustificationRight); } catch { }

                _model.GraphicsRedraw2();
                Logger.Info("[CalibrationNote] Calibration note inserted in calibration box.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[CalibrationNote] Failed to insert calibration note: {ex.Message}");
            }
            finally
            {
                try { _drawing.EditSheet(); } catch { }
            }
        }

        /// <summary>
        /// Normalizes overlayScaling (1.0, 2.0, 300, "300X", ...) into 100/200/300/400.
        /// </summary>
        private static int NormalizeScalingBucket(double overlayScaling)
        {
            var x = overlayScaling < 10.0
                ? (int)Math.Round(overlayScaling * 100.0)
                : (int)Math.Round(overlayScaling);

            return x <= 150 ? 100
                 : x <= 250 ? 200
                 : x <= 350 ? 300
                 : 400;
        }

        private void EnsureLayerExistsAndVisible(string layerName)
        {
            try
            {
                if (_model is null) return;

                var lm = (ILayerMgr)_model.GetLayerManager();
                if (lm is null) return;

                var layer = lm.GetLayer(layerName) as ILayer;
                if (layer is null)
                {
                    try
                    {
                        // Handle both AddLayer2 and AddLayer via reflection (version-safe)
                        var add2 = lm.GetType().GetMethod("AddLayer2", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (add2 != null)
                        {
                            add2.Invoke(lm, new object[] { layerName, "", 0 });
                        }
                        else
                        {
                            var add = lm.GetType().GetMethod("AddLayer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            add?.Invoke(lm, new object[] { layerName });
                        }

                        layer = lm.GetLayer(layerName) as ILayer;
                    }
                    catch { }
                }

                if (layer is not null)
                {
                    try { layer.Visible = true; } catch { }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"EnsureLayerExistsAndVisible('{layerName}') failed: {ex.Message}");
            }
        }

        // ───────────────────────────────────────────────────────────────────────
        // Reference discovery
        // ───────────────────────────────────────────────────────────────────────

        private IEnumerable<string> EnumerateReferencedModelPaths()
        {
            var results = new List<string>();
            try
            {
                var v = _drawing?.GetFirstView() as View;
                while (v is not null)
                {
                    try
                    {
                        string? p = null;

                        try { p = (v.ReferencedDocument as ModelDoc2)?.GetPathName(); } catch { }
                        if (string.IsNullOrWhiteSpace(p))
                        {
                            try { dynamic dv = v; p = dv.GetReferencedModelName2(); } catch { }
                        }
                        if (string.IsNullOrWhiteSpace(p))
                        {
                            try { dynamic dv = v; p = dv.GetReferencedModelName(); } catch { }
                        }

                        if (!string.IsNullOrWhiteSpace(p))
                            results.Add(Path.GetFullPath(p));
                    }
                    catch { /* ignore per-view */ }

                    v = v.GetNextView() as View;
                }
            }
            catch { /* ignore */ }

            return results.Distinct(StringComparer.OrdinalIgnoreCase);
        }
    }
}
