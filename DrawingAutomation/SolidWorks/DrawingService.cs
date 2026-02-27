using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
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

        public sealed record DeleteSheetsResult(
            bool Ok,
            string KeepSheet,
            IReadOnlyList<string> Deleted,
            IReadOnlyList<string> NotDeleted
        );

        /// <summary>
        /// Best-effort "fast mode" to reduce UI/graphics overhead during batch changes.
        /// Uses only safe calls + cached reflection (portable method lookup).
        /// </summary>
        private sealed class FastModeScope : IDisposable
        {
            private readonly SldWorks _swApp;
            private readonly ModelDoc2 _model;
            private readonly ModelDocExtension? _ext;

            private readonly bool _oldCmdInProgress;
            private readonly bool _oldAddToDb;

            public FastModeScope(SldWorks swApp, ModelDoc2 model, ModelDocExtension? ext)
            {
                _swApp = swApp;
                _model = model;
                _ext = ext;

                _oldCmdInProgress = GetCommandInProgressSafe(_swApp);
                _oldAddToDb = GetAddToDbSafe(_model);

                // Apply fast mode (best-effort)
                TrySetCommandInProgress(_swApp, true);
                TrySetAddToDb(_model, true);

                // Optional: suspend graphics + feature tree if SW version supports it
                TrySetGraphicsUpdate(_ext, enabled: false);
                TrySetFeatureTreeUpdate(_model, enabled: false);
            }

            public void Dispose()
            {
                // Restore (best-effort)
                TrySetGraphicsUpdate(_ext, enabled: true);
                TrySetFeatureTreeUpdate(_model, enabled: true);

                TrySetAddToDb(_model, _oldAddToDb);
                TrySetCommandInProgress(_swApp, _oldCmdInProgress);
            }

            private static bool GetCommandInProgressSafe(SldWorks swApp)
            {
                try { return swApp.CommandInProgress; } catch { return false; }
            }

            private static void TrySetCommandInProgress(SldWorks swApp, bool v)
            {
                try { swApp.CommandInProgress = v; } catch { }
            }

            private static bool GetAddToDbSafe(ModelDoc2 model)
            {
                try { return model.GetAddToDB(); } catch { return false; }
            }

            private static void TrySetAddToDb(ModelDoc2 model, bool v)
            {
                try { model.SetAddToDB(v); } catch { }
            }

            // Cached reflection hooks (optional)
            private static MethodInfo? _miEnableGraphicsUpdate; // ModelDocExtension.EnableGraphicsUpdate(bool)
            private static MethodInfo? _miFeatureMgrEnableTree; // ModelDoc2.FeatureManagerEnableFeatureTree(bool)

            private static void TrySetGraphicsUpdate(ModelDocExtension? ext, bool enabled)
            {
                if (ext is null) return;

                try
                {
                    _miEnableGraphicsUpdate ??= FindMethod(
                        ext.GetType(),
                        "EnableGraphicsUpdate",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        new[] { typeof(bool) });

                    _miEnableGraphicsUpdate?.Invoke(ext, new object[] { enabled });
                }
                catch { }
            }

            private static void TrySetFeatureTreeUpdate(ModelDoc2 model, bool enabled)
            {
                try
                {
                    _miFeatureMgrEnableTree ??= FindMethod(
                        ((object)model).GetType(),   // ✅ force .NET GetType()
                        "FeatureManagerEnableFeatureTree",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        new[] { typeof(bool) });

                    _miFeatureMgrEnableTree?.Invoke(model, new object[] { enabled });
                }
                catch { }
            }
        }

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

        // Reflection caches
        private static MethodInfo? _miLayerAdd2;
        private static MethodInfo? _miLayerAdd;
        private static MethodInfo? _miGetReferencedModelName2;
        private static MethodInfo? _miGetReferencedModelName;
        private static MethodInfo? _miDrawingDeleteSheet;
        private static MethodInfo? _miDrawingRemoveSheet;

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
                throw new InvalidOperationException(
                    $"Failed to open drawing. SW error={_error}, warn={_warning}. Path='{_drawingPath}'.");

            // Activate (best-effort). No Thread.Sleep.
            try
            {
                int actErr = 0;
                _swApp.ActivateDoc2(Path.GetFileName(_drawingPath), false, ref actErr);
            }
            catch { }

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

        public void Rebuild(bool redraw = false)
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

            if (redraw)
            {
                try { _model.GraphicsRedraw2(); } catch { }
            }
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
                _drawing.ActivateSheet(sheetName);
                Logger.Info($"Activated sheet: {sheetName}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"ActivateSheet('{sheetName}') failed: {ex.Message}");
            }
        }

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
        /// Delete all sheets except the one you want to keep.
        /// FAST PATH: try direct API first, then selection delete.
        /// Uses FastModeScope to reduce UI overhead.
        /// </summary>
        public DeleteSheetsResult DeleteAllSheetsExcept2(string keepSheetName)
        {
            var deleted = new List<string>();
            var notDeleted = new List<string>();

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

            using var fast = new FastModeScope(_swApp, _model, _modelExt);

            try { _drawing.ActivateSheet(resolvedKeep); } catch { }
            try { _drawing.EditSheet(); } catch { }

            foreach (var sheetName in names)
            {
                if (string.IsNullOrWhiteSpace(sheetName)) continue;
                if (string.Equals(sheetName, resolvedKeep, StringComparison.Ordinal)) continue;

                if (TryDeleteSheetDirect(sheetName))
                {
                    deleted.Add(sheetName);
                    continue;
                }

                if (TryDeleteSheetBySelection(sheetName))
                {
                    deleted.Add(sheetName);
                    continue;
                }

                notDeleted.Add(sheetName);
            }

            try { _drawing.ActivateSheet(resolvedKeep); } catch { }
            try { _model.EditRebuild3(); } catch { }

            var ok = notDeleted.Count == 0;
            return new DeleteSheetsResult(ok, resolvedKeep, deleted, notDeleted);
        }

        public bool DeleteAllSheetsExcept(string keepSheetName)
        {
            var res = DeleteAllSheetsExcept2(keepSheetName);
            return res.Ok;
        }

        private bool TryDeleteSheetDirect(string sheetName)
        {
            try
            {
                if (_drawing is null) return false;

                var t = _drawing.GetType();

                // Cache the reflection once
                _miDrawingDeleteSheet ??= FindMethod(t, "DeleteSheet",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    new[] { typeof(string) });

                if (_miDrawingDeleteSheet != null)
                {
                    _miDrawingDeleteSheet.Invoke(_drawing, new object[] { sheetName });
                    return true;
                }

                _miDrawingRemoveSheet ??= FindMethod(t, "RemoveSheet",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    new[] { typeof(string) });

                if (_miDrawingRemoveSheet != null)
                {
                    _miDrawingRemoveSheet.Invoke(_drawing, new object[] { sheetName });
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool TryDeleteSheetBySelection(string sheetName)
        {
            try
            {
                if (_drawing is null || _model is null) return false;

                _drawing.ActivateSheet(sheetName);
                _model.ClearSelection2(true);

                var selected = _model.Extension.SelectByID2(
                    sheetName, "SHEET", 0, 0, 0, false, 0, null, 0);

                if (!selected)
                    return false;

                return _model.Extension.DeleteSelection2(0);
            }
            catch
            {
                return false;
            }
        }

        // ───────────────────────────────────────────────────────────────────────
        // Metadata hooks (stubs)
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

        public void DrawCalibrationBoxOnSheetFormat(double overlayScaling)
        {
            if (_drawing is null || _model is null)
            {
                Logger.Warn("[CalibrationBox] No drawing is open.");
                return;
            }

            using var fast = new FastModeScope(_swApp, _model, _modelExt);

            var token = NormalizeScalingBucket(overlayScaling);

            var (xIn, yIn, widthIn, heightIn) = token switch
            {
                100 => (2.355, 1.55, 1.68, 1.68),
                200 => (1.500, 0.705, 3.38, 3.38),
                300 => (1.755, 0.955, 2.88, 2.89),
                400 => (2.225, 1.425, 1.94, 1.94),
                _ => (1.755, 0.955, 2.88, 2.89),
            };

            const double IN_TO_M = 0.0254;

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

        public void InsertCalibrationBoxNoteBottomRight(string calibrationValueMicrons, double overlayScaling)
        {
            if (_drawing is null || _model is null)
            {
                Logger.Warn("[CalibrationNote] No drawing is open.");
                return;
            }

            using var fast = new FastModeScope(_swApp, _model, _modelExt);

            var token = NormalizeScalingBucket(overlayScaling);

            var (xIn, yIn, wIn, hIn) = token switch
            {
                100 => (2.355, 1.55, 1.68, 1.68),
                200 => (1.500, 0.705, 3.38, 3.38),
                300 => (1.755, 0.955, 2.88, 2.89),
                400 => (2.225, 1.425, 1.94, 1.94),
                _ => (1.755, 0.955, 2.88, 2.89),
            };

            const double IN_TO_M = 0.0254;
            const double insetX = 0.08;
            const double insetY = 0.06;
            const double charH = 0.0016;

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
                    // Cache reflection once
                    _miLayerAdd2 ??= FindMethod(lm.GetType(), "AddLayer2",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        new[] { typeof(string), typeof(string), typeof(int) });

                    _miLayerAdd ??= FindMethod(lm.GetType(), "AddLayer",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        new[] { typeof(string) });

                    try
                    {
                        if (_miLayerAdd2 != null)
                            _miLayerAdd2.Invoke(lm, new object[] { layerName, "", 0 });
                        else
                            _miLayerAdd?.Invoke(lm, new object[] { layerName });
                    }
                    catch { }

                    layer = lm.GetLayer(layerName) as ILayer;
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
                            _miGetReferencedModelName2 ??= FindMethod(v.GetType(), "GetReferencedModelName2",
                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                                Type.EmptyTypes);

                            if (_miGetReferencedModelName2 != null)
                            {
                                try { p = _miGetReferencedModelName2.Invoke(v, null) as string; } catch { }
                            }
                        }

                        if (string.IsNullOrWhiteSpace(p))
                        {
                            _miGetReferencedModelName ??= FindMethod(v.GetType(), "GetReferencedModelName",
                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                                Type.EmptyTypes);

                            if (_miGetReferencedModelName != null)
                            {
                                try { p = _miGetReferencedModelName.Invoke(v, null) as string; } catch { }
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(p))
                            results.Add(Path.GetFullPath(p));
                    }
                    catch { }

                    v = v.GetNextView() as View;
                }
            }
            catch { }

            return results.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        // ───────────────────────────────────────────────────────────────────────
        // Portable reflection helper (NO 5-arg GetMethod overloads)
        // ───────────────────────────────────────────────────────────────────────

        private static MethodInfo? FindMethod(Type t, string name, BindingFlags flags, Type[] paramTypes)
        {
            try
            {
                var methods = t.GetMethods(flags);
                for (int i = 0; i < methods.Length; i++)
                {
                    var m = methods[i];
                    if (!string.Equals(m.Name, name, StringComparison.Ordinal)) continue;

                    var ps = m.GetParameters();
                    if (ps.Length != paramTypes.Length) continue;

                    bool match = true;
                    for (int p = 0; p < ps.Length; p++)
                    {
                        if (ps[p].ParameterType != paramTypes[p])
                        {
                            match = false;
                            break;
                        }
                    }

                    if (match) return m;
                }
            }
            catch { }

            return null;
        }
    }
}