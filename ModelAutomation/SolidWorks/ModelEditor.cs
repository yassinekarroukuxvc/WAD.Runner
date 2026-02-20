// ModelAutomation/SolidWorks/ModelEditor.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application; // Logger

using DomDimKey = WAD.Runner.DataManagement.Domain.Dimensions.DimensionKey;
using DomWedgeData = WAD.Runner.DataManagement.Domain.Wedge.WedgeData;

using SwDim = SolidWorks.Interop.sldworks.Dimension;

namespace WAD.Runner.ModelAutomation.SolidWorks
{
    /// <summary>
    /// Minimal SolidWorks Part editor for ModelAutomation.
    /// IMPORTANT:
    /// - No implicit rebuilds in methods (except RebuildOnce()).
    /// - Orchestrator owns the single rebuild at the end.
    /// </summary>
    public sealed class ModelEditor : IDisposable
    {
        private readonly SldWorks _sw;
        private ModelDoc2? _model;
        private string _partPath = "";
        private int _err, _warn;

        private FeatureToggleBatch? _toggles;

        public ModelEditor(SldWorks swApp)
        {
            _sw = swApp ?? throw new ArgumentNullException(nameof(swApp));
        }

        public ModelDoc2 Model => _model ?? throw new InvalidOperationException("No active part loaded.");
        public string PartPath => _partPath;

        public void OpenPart(string partPath)
        {
            if (string.IsNullOrWhiteSpace(partPath))
                throw new ArgumentNullException(nameof(partPath));

            var full = Path.GetFullPath(partPath);
            if (!File.Exists(full))
                throw new FileNotFoundException("Part not found.", full);

            // Ensure not read-only
            var attrs = File.GetAttributes(full);
            if ((attrs & FileAttributes.ReadOnly) != 0)
            {
                Logger.Warn($"[ModelEditor] Clearing read-only attribute: {full}");
                File.SetAttributes(full, attrs & ~FileAttributes.ReadOnly);
            }

            _err = 0; _warn = 0;

            Logger.Info($"[ModelEditor] OpenPart → '{full}'");
            var doc = _sw.OpenDoc6(
                full,
                (int)swDocumentTypes_e.swDocPART,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                "",
                ref _err,
                ref _warn);

            _model = doc as ModelDoc2;
            if (_model is null)
            {
                var reason = DecodeOpenError(_err);
                Logger.Error($"[ModelEditor] Failed to open part. err={_err} ({reason}), warn={_warn}. Path: {full}");
                throw new InvalidOperationException(
                    $"Failed to open part. err={_err} ({reason}), warn={_warn}\nPath: {full}");
            }

            _partPath = full;

            // Build feature index ONCE for fast toggles.
            _toggles = FeatureToggleBatch.Build(_model);

            Logger.Success($"[ModelEditor] Part opened: {_partPath}");
        }

        public bool ActivateConfiguration(string configName)
        {
            if (string.IsNullOrWhiteSpace(configName))
            {
                Logger.Warn("[ModelEditor] ActivateConfiguration skipped (empty name).");
                return false;
            }

            Logger.Info($"[ModelEditor] ActivateConfiguration → '{configName}'");

            var namesObj = Model.GetConfigurationNames();
            var names = (namesObj as object[] ?? Array.Empty<object>())
                .Select(o => o?.ToString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            var target =
                names.FirstOrDefault(n => string.Equals(n, configName, StringComparison.Ordinal)) ??
                names.FirstOrDefault(n => string.Equals(n, configName, StringComparison.OrdinalIgnoreCase));

            if (target is null)
            {
                Logger.Warn($"[ModelEditor] Config '{configName}' not found; keeping current.");
                Logger.Info($"[ModelEditor] Available configs: {string.Join(", ", names)}");
                return false;
            }

            Model.ShowConfiguration2(target);
            Logger.Success($"[ModelEditor] Activated configuration: {target}");
            return true;
        }


        /// <summary>
        /// Import equations from external equation file into the model.
        /// NO rebuild here.
        /// </summary>
        public void ImportEquationsFromFile(string equationFilePath)
        {
            if (string.IsNullOrWhiteSpace(equationFilePath) || !File.Exists(equationFilePath))
                throw new FileNotFoundException("Equations file not found.", equationFilePath);

            Logger.Info($"[ModelEditor] ImportEquationsFromFile → '{equationFilePath}'");

            var eq = Model.GetEquationMgr();
            bool prevAutoSolve = eq.AutomaticSolveOrder;
            bool prevAutoRebuild = eq.AutomaticRebuild;

            try
            {
                // Keep consistent and predictable during import
                eq.AutomaticSolveOrder = true;

                // IMPORTANT: do not allow SW to rebuild during import
                eq.AutomaticRebuild = false;

                eq.FilePath = equationFilePath;

                var ok = eq.UpdateValuesFromExternalEquationFile();
                Logger.Info($"[ModelEditor] UpdateValuesFromExternalEquationFile() → {ok}");

                // NO rebuild here
            }
            finally
            {
                eq.AutomaticSolveOrder = prevAutoSolve;
                eq.AutomaticRebuild = prevAutoRebuild;
            }
        }

        /// <summary>
        /// Sets "Engraving" custom property (no rebuild).
        /// </summary>
        public void SetEngraving(string? text)
        {
            Logger.Info($"[ModelEditor] SetEngraving → '{text ?? "(null)"}'");
            var mgr = Model.Extension.get_CustomPropertyManager("");
            var rc = mgr.Set2("Engraving", text ?? "");
            Logger.Info($"[ModelEditor] Engraving Set2 result: {rc}");
        }

        /// <summary>
        /// Batch apply feature toggles using the pre-built FeatureToggleBatch index.
        /// NO rebuild here.
        /// </summary>
        public FeatureToggleBatch.ToggleResult ApplyFeatureToggles(
            IEnumerable<string>? suppress,
            IEnumerable<string>? unsuppress,
            swInConfigurationOpts_e scope = swInConfigurationOpts_e.swThisConfiguration)
        {
            if (_toggles is null)
                _toggles = FeatureToggleBatch.Build(Model);

            Logger.Info("[ModelEditor] ApplyFeatureToggles (batch)");
            return _toggles.Apply(suppress, unsuppress, scope);
        }

        /// <summary>
        /// Apply length tolerances for the given dimension keys (no rebuild).
        /// Uses same strategy as your PartEditor: search all owners and probe shortName@owner.
        /// </summary>
        public void ApplyLengthTolerances(DomWedgeData wedge, IEnumerable<DomDimKey> keys)
        {
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));
            if (keys is null) return;

            var keysList = keys.Select(k => k.Value).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
            if (keysList.Count == 0)
            {
                Logger.Info("[ModelEditor] ApplyLengthTolerances → no keys");
                return;
            }

            Logger.Info($"[ModelEditor] ApplyLengthTolerances → [{string.Join(", ", keysList)}]");

            var owners = GetAllFeatureAndSketchNames(Model);
            Logger.Info($"[ModelEditor] Owners discovered: {owners.Count}");

            foreach (var shortName in keysList)
            {
                try
                {
                    if (!wedge.Dimensions.TryGetValue(DomDimKey.From(shortName), out var d) || d is null)
                    {
                        Logger.Warn($"[ModelEditor] Tolerance input missing key '{shortName}'.");
                        continue;
                    }

                    // Only for mm length tolerances (same as your filtering upstream)
                    double upper_m = (double)d.Tol.Upper.AsMm() / 1000.0;
                    double lower_m = (double)d.Tol.Lower.AsMm() / 1000.0;

                    if (!TryGetDimensionByShortName(Model, shortName, owners, out var swDim) || swDim is null)
                    {
                        Logger.Warn($"[ModelEditor] Could not locate Dimension for '{shortName}' (owner unknown).");
                        continue;
                    }

                    var tol = swDim.Tolerance;
                    if (tol is null)
                    {
                        Logger.Warn($"[ModelEditor] Tolerance object null for '{shortName}'.");
                        continue;
                    }

                    tol.Type = (Math.Abs(upper_m - lower_m) > 1e-12)
                        ? (int)swTolType_e.swTolBILAT
                        : (int)swTolType_e.swTolSYMMETRIC;

                    tol.SetValues(-lower_m, upper_m);
                    Logger.Success($"[ModelEditor] Tolerance applied '{shortName}' → +{upper_m:G6} / -{lower_m:G6} m.");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[ModelEditor] ApplyLengthTolerances failed for '{shortName}': {ex.Message}");
                }
            }

            // NO rebuild here
        }

        /// <summary>
        /// The ONLY rebuild method. Call once at the end of the workflow.
        /// </summary>
        public void RebuildOnce()
        {
            Logger.Info("[ModelEditor] RebuildOnce");
            Model.EditRebuild3();
            Model.ForceRebuild3(false);
            Model.GraphicsRedraw2();
            Logger.Success("[ModelEditor] Rebuild complete.");
        }

        public void Save()
        {
            Logger.Info("[ModelEditor] Save");
            Model.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref _err, ref _warn);
            Logger.Success($"[ModelEditor] Saved (err={_err}, warn={_warn})");
        }

        public void Close()
        {
            if (string.IsNullOrWhiteSpace(_partPath))
                return;

            Logger.Info($"[ModelEditor] Close → '{_partPath}'");
            try
            {
                _sw.CloseDoc(_partPath);
                Logger.Success("[ModelEditor] Closed.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[ModelEditor] Close exception: {ex.Message}");
            }
            finally
            {
                _model = null;
                _toggles = null;
                _partPath = "";
            }
        }

        public void Dispose()
        {
            try { Close(); } catch { /* ignore */ }
        }

        private static string DecodeOpenError(int err)
            => err switch
            {
                1 => "Generic/unknown open error",
                2 => "File could not be opened (check path/permissions/unsupported version/already locked)",
                3 => "File not found",
                4 => "Invalid file or version",
                _ => "Unspecified"
            };

        private static List<string> GetAllFeatureAndSketchNames(ModelDoc2 model)
        {
            var names = new List<string>();
            var part = (PartDoc)model;
            var f = (Feature)part.FirstFeature();

            while (f != null)
            {
                if (!string.IsNullOrWhiteSpace(f.Name))
                    names.Add(f.Name);

                var sub = (Feature)f.GetFirstSubFeature();
                while (sub != null)
                {
                    if (!string.IsNullOrWhiteSpace(sub.Name))
                        names.Add(sub.Name);
                    sub = (Feature)sub.GetNextSubFeature();
                }

                f = (Feature)f.GetNextFeature();
            }

            return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool TryGetDimensionByShortName(ModelDoc2 model, string shortName, List<string> owners, out SwDim? swDim)
        {
            foreach (var owner in owners)
            {
                var probe = $"{shortName}@{owner}";
                var dim = model.Parameter(probe) as SwDim;
                if (dim != null)
                {
                    Logger.Info($"[ModelEditor] Resolved '{probe}'");
                    swDim = dim;
                    return true;
                }
            }

            Logger.Warn($"[ModelEditor] Not found for '{shortName}@*'");
            swDim = null;
            return false;
        }
    }
}
