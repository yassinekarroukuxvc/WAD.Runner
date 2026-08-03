using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;
using WAD.Runner.ModelAutomation.Common;
using WAD.Runner.DataManagement.Domain.Wedge;

using DomDimKey =
    WAD.Runner.DataManagement.Domain.Dimensions.DimensionKey;

using DomWedgeData =
    WAD.Runner.DataManagement.Domain.Wedge.WedgeData;

using SwDim =
    SolidWorks.Interop.sldworks.Dimension;

namespace WAD.Runner.ModelAutomation.SolidWorks
{
    public sealed class ModelEditor : IDisposable
    {
        private readonly SldWorks _sw;

        private ModelDoc2? _model;
        private string _partPath = "";

        private int _err;
        private int _warn;

        private FeatureToggleBatch? _toggles;

        public ModelEditor(SldWorks swApp)
        {
            _sw = swApp ??
                throw new ArgumentNullException(
                    nameof(swApp));
        }

        public ModelDoc2 Model =>
            _model ??
            throw new InvalidOperationException(
                "No active part loaded.");

        public string PartPath => _partPath;

        public void OpenPart(string partPath)
        {
            if (string.IsNullOrWhiteSpace(partPath))
            {
                throw new ArgumentNullException(
                    nameof(partPath));
            }

            var full = Path.GetFullPath(partPath);

            if (!File.Exists(full))
            {
                throw new FileNotFoundException(
                    "Part not found.",
                    full);
            }

            var attributes = File.GetAttributes(full);

            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                Logger.Warn(
                    "[ModelEditor] Clearing " +
                    $"read-only attribute: {full}");

                File.SetAttributes(
                    full,
                    attributes & ~FileAttributes.ReadOnly);
            }

            _err = 0;
            _warn = 0;

            Logger.Info(
                $"[ModelEditor] OpenPart → '{full}'");

            var document = _sw.OpenDoc6(
                full,
                (int)swDocumentTypes_e.swDocPART,
                (int)swOpenDocOptions_e
                    .swOpenDocOptions_Silent,
                "",
                ref _err,
                ref _warn);

            _model = document as ModelDoc2;

            if (_model is null)
            {
                var reason = DecodeOpenError(_err);

                Logger.Error(
                    "[ModelEditor] Failed to open part. " +
                    $"err={_err} ({reason}), " +
                    $"warn={_warn}. Path: {full}");

                throw new InvalidOperationException(
                    "Failed to open part. " +
                    $"err={_err} ({reason}), " +
                    $"warn={_warn}\nPath: {full}");
            }

            _partPath = full;

            _toggles =
                FeatureToggleBatch.Build(_model);

            Logger.Success(
                $"[ModelEditor] Part opened: {_partPath}");
        }

        public bool ActivateConfiguration(
            string configName)
        {
            if (string.IsNullOrWhiteSpace(configName))
            {
                Logger.Warn(
                    "[ModelEditor] " +
                    "ActivateConfiguration skipped " +
                    "(empty name).");

                return false;
            }

            Logger.Info(
                "[ModelEditor] " +
                $"ActivateConfiguration → '{configName}'");

            var configurationNamesObject =
                Model.GetConfigurationNames();

            var configurationNames =
                (configurationNamesObject as object[] ??
                 Array.Empty<object>())
                .Select(value => value?.ToString())
                .Where(
                    value =>
                        !string.IsNullOrWhiteSpace(value))
                .ToList();

            var target =
                configurationNames.FirstOrDefault(
                    name => string.Equals(
                        name,
                        configName,
                        StringComparison.Ordinal))
                ??
                configurationNames.FirstOrDefault(
                    name => string.Equals(
                        name,
                        configName,
                        StringComparison.OrdinalIgnoreCase));

            if (target is null)
            {
                Logger.Warn(
                    "[ModelEditor] " +
                    $"Config '{configName}' not found; " +
                    "keeping current.");

                Logger.Info(
                    "[ModelEditor] Available configs: " +
                    string.Join(", ", configurationNames));

                return false;
            }

            /*
             * Old configuration handling:
             *
             * We intentionally call ShowConfiguration2
             * without using its Boolean return value.
             *
             * Some SolidWorks documents can return false
             * even though the requested configuration is
             * already active or usable.
             *
             * A found configuration is therefore treated
             * as successfully handled, matching the behavior
             * used before the refactoring.
             */
            Model.ShowConfiguration2(target);

            Logger.Success(
                "[ModelEditor] Activated configuration: " +
                target);

            return true;
        }

        public void ImportEquationsFromFile(
            string equationFilePath)
        {
            if (string.IsNullOrWhiteSpace(
                    equationFilePath) ||
                !File.Exists(equationFilePath))
            {
                throw new FileNotFoundException(
                    "Equations file not found.",
                    equationFilePath);
            }

            Logger.Info(
                "[ModelEditor] " +
                $"ImportEquationsFromFile → " +
                $"'{equationFilePath}'");

            var equationManager =
                Model.GetEquationMgr();

            var previousAutomaticSolveOrder =
                equationManager.AutomaticSolveOrder;

            var previousAutomaticRebuild =
                equationManager.AutomaticRebuild;

            try
            {
                equationManager.AutomaticSolveOrder =
                    true;

                equationManager.AutomaticRebuild =
                    false;

                equationManager.FilePath =
                    equationFilePath;

                var updated =
                    equationManager
                        .UpdateValuesFromExternalEquationFile();

                Logger.Info(
                    "[ModelEditor] " +
                    "UpdateValuesFromExternalEquationFile()" +
                    $" → {updated}");

                if (!updated)
                {
                    throw new InvalidOperationException(
                        "SolidWorks failed to update " +
                        "values from the external " +
                        "equation file.");
                }
            }
            finally
            {
                equationManager.AutomaticSolveOrder =
                    previousAutomaticSolveOrder;

                equationManager.AutomaticRebuild =
                    previousAutomaticRebuild;
            }
        }

        public void SetEngraving(string? text)
        {
            Logger.Info(
                "[ModelEditor] " +
                $"SetEngraving → '{text ?? "(null)"}'");

            var propertyManager =
                Model.Extension
                    .get_CustomPropertyManager("");

            var result =
                propertyManager.Set2(
                    "Engraving",
                    text ?? "");

            Logger.Info(
                "[ModelEditor] " +
                $"Engraving Set2 result: {result}");
        }

        public FeatureToggleBatch.ToggleResult
            ApplyFeatureToggles(
                IEnumerable<string>? suppress,
                IEnumerable<string>? unsuppress,
                swInConfigurationOpts_e scope =
                    swInConfigurationOpts_e
                        .swThisConfiguration)
        {
            if (_toggles is null)
            {
                _toggles =
                    FeatureToggleBatch.Build(Model);
            }

            Logger.Info(
                "[ModelEditor] " +
                "ApplyFeatureToggles (batch)");

            return _toggles.Apply(
                suppress,
                unsuppress,
                scope);
        }

        public void ApplyLengthTolerances(
            DomWedgeData wedge,
            IEnumerable<DomDimKey> keys)
        {
            if (wedge is null)
            {
                throw new ArgumentNullException(
                    nameof(wedge));
            }

            if (keys is null)
                return;

            var keyNames =
                keys
                    .Select(key => key.Value)
                    .Where(
                        value =>
                            !string.IsNullOrWhiteSpace(
                                value))
                    .Distinct()
                    .ToList();

            if (keyNames.Count == 0)
            {
                Logger.Info(
                    "[ModelEditor] " +
                    "ApplyLengthTolerances → no keys");

                return;
            }

            Logger.Info(
                "[ModelEditor] " +
                "ApplyLengthTolerances → [" +
                string.Join(", ", keyNames) +
                "]");

            var owners =
                GetAllFeatureAndSketchNames(Model);

            Logger.Info(
                "[ModelEditor] " +
                $"Owners discovered: {owners.Count}");

            foreach (var shortName in keyNames)
            {
                try
                {
                    if (!wedge.Dimensions.TryGetValue(
                            DomDimKey.From(shortName),
                            out var dimensionData) ||
                        dimensionData is null)
                    {
                        Logger.Warn(
                            "[ModelEditor] " +
                            "Tolerance input missing key " +
                            $"'{shortName}'.");

                        continue;
                    }

                    var upperAbsoluteMeters =
                        ModelLengthUnits
                            .MillimetersToSystemMeters(
                                decimal.Abs(
                                    dimensionData.Tol.Upper.AsMm()));

                    var lowerAbsoluteMeters =
                        ModelLengthUnits
                            .MillimetersToSystemMeters(
                                decimal.Abs(
                                    dimensionData.Tol.Lower.AsMm()));

                    if (!TryGetDimensionByShortName(
                            Model,
                            shortName,
                            owners,
                            out var solidWorksDimension) ||
                        solidWorksDimension is null)
                    {
                        Logger.Warn(
                            "[ModelEditor] " +
                            "Could not locate Dimension for " +
                            $"'{shortName}' " +
                            "(owner unknown).");

                        continue;
                    }

                    var tolerance =
                        solidWorksDimension.Tolerance;

                    if (tolerance is null)
                    {
                        Logger.Warn(
                            "[ModelEditor] " +
                            "Tolerance object null for " +
                            $"'{shortName}'.");

                        continue;
                    }

                    tolerance.Type =
                        Math.Abs(
                            upperAbsoluteMeters -
                            lowerAbsoluteMeters) > 1e-12
                            ? (int)swTolType_e.swTolBILAT
                            : (int)swTolType_e
                                .swTolSYMMETRIC;

                    tolerance.SetValues(
                        -lowerAbsoluteMeters,
                        upperAbsoluteMeters);

                    Logger.Success(
                        "[ModelEditor] " +
                        $"Tolerance applied '{shortName}' " +
                        $"→ +{upperAbsoluteMeters:G6} / " +
                        $"-{lowerAbsoluteMeters:G6} m.");
                }
                catch (Exception ex)
                {
                    Logger.Warn(
                        "[ModelEditor] " +
                        "ApplyLengthTolerances failed " +
                        $"for '{shortName}': {ex.Message}");
                }
            }
        }

        public void RebuildOnce()
        {
            Logger.Info(
                "[ModelEditor] RebuildOnce");

            Model.EditRebuild3();
            Model.ForceRebuild3(false);
            Model.GraphicsRedraw2();

            Logger.Success(
                "[ModelEditor] Rebuild complete.");
        }

        public void Save()
        {
            Logger.Info("[ModelEditor] Save");

            _err = 0;
            _warn = 0;

            var saved = Model.Save3(
                (int)swSaveAsOptions_e
                    .swSaveAsOptions_Silent,
                ref _err,
                ref _warn);

            if (!saved || _err != 0)
            {
                throw new InvalidOperationException(
                    "SolidWorks failed to save the part. " +
                    $"success={saved}, " +
                    $"err={_err}, " +
                    $"warn={_warn}, " +
                    $"path={_partPath}");
            }

            Logger.Success(
                "[ModelEditor] " +
                $"Saved (warn={_warn})");
        }

        public void Close()
        {
            if (string.IsNullOrWhiteSpace(_partPath))
                return;

            Logger.Info(
                $"[ModelEditor] Close → '{_partPath}'");

            try
            {
                var documentTitle =
                    _model?.GetTitle();

                if (string.IsNullOrWhiteSpace(
                        documentTitle))
                {
                    documentTitle =
                        Path.GetFileName(_partPath);
                }

                _sw.CloseDoc(documentTitle);

                Logger.Success(
                    "[ModelEditor] Closed.");
            }
            catch (Exception ex)
            {
                Logger.Warn(
                    "[ModelEditor] Close exception: " +
                    ex.Message);
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
            try
            {
                Close();
            }
            catch
            {
                // Dispose must not hide the original
                // automation exception.
            }
        }

        private static string DecodeOpenError(int error)
        {
            return error switch
            {
                1 =>
                    "Generic/unknown open error",

                2 =>
                    "File could not be opened " +
                    "(check path/permissions/" +
                    "unsupported version/already locked)",

                3 =>
                    "File not found",

                4 =>
                    "Invalid file or version",

                _ =>
                    "Unspecified"
            };
        }

        private static List<string>
            GetAllFeatureAndSketchNames(
                ModelDoc2 model)
        {
            var names = new List<string>();

            var part = (PartDoc)model;
            var feature =
                (Feature)part.FirstFeature();

            while (feature is not null)
            {
                if (!string.IsNullOrWhiteSpace(
                        feature.Name))
                {
                    names.Add(feature.Name);
                }

                var subFeature =
                    (Feature)feature
                        .GetFirstSubFeature();

                while (subFeature is not null)
                {
                    if (!string.IsNullOrWhiteSpace(
                            subFeature.Name))
                    {
                        names.Add(subFeature.Name);
                    }

                    subFeature =
                        (Feature)subFeature
                            .GetNextSubFeature();
                }

                feature =
                    (Feature)feature.GetNextFeature();
            }

            return names
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool TryGetDimensionByShortName(
            ModelDoc2 model,
            string shortName,
            List<string> owners,
            out SwDim? solidWorksDimension)
        {
            foreach (var owner in owners)
            {
                var parameterName =
                    $"{shortName}@{owner}";

                var dimension =
                    model.Parameter(parameterName)
                    as SwDim;

                if (dimension is not null)
                {
                    Logger.Info(
                        "[ModelEditor] " +
                        $"Resolved '{parameterName}'");

                    solidWorksDimension = dimension;
                    return true;
                }
            }

            Logger.Warn(
                "[ModelEditor] " +
                $"Not found for '{shortName}@*'");

            solidWorksDimension = null;
            return false;
        }
    }
}