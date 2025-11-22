// PartAutomation/SolidWorks/PartEditor.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Sw = SolidWorks.Interop.sldworks;
using SwConst = SolidWorks.Interop.swconst;
using SwDim = SolidWorks.Interop.sldworks.Dimension;
using SwFeat = SolidWorks.Interop.sldworks.Feature;
using SwPart = SolidWorks.Interop.sldworks.PartDoc;
using WAD.Runner.Application;                         // Logger
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.PartAutomation.SolidWorks;

public sealed class PartEditor
{
    private readonly Sw.SldWorks _sw;
    private Sw.ModelDoc2? _model;
    private Sw.ModelDocExtension? _ext;
    private string _partPath = "";
    private int _err = 0, _warn = 0;

    public PartEditor(Sw.SldWorks sw) => _sw = sw;
    public Sw.ModelDoc2 Model => _model ?? throw new InvalidOperationException("No active part loaded.");

    // ----------------------------- Open / Close / Save / Rebuild -----------------------------

    public void Open(string partPath)
    {
        Logger.Info($"[PartEditor] Open → '{partPath}'");
        if (string.IsNullOrWhiteSpace(partPath))
            throw new ArgumentNullException(nameof(partPath));

        var full = Path.GetFullPath(partPath);
        if (!File.Exists(full))
            throw new FileNotFoundException("Part not found.", full);

        // Ensure not read-only (copied files sometimes inherit RO)
        var attrs = File.GetAttributes(full);
        if ((attrs & FileAttributes.ReadOnly) != 0)
        {
            Logger.Warn($"[PartEditor] Clearing read-only attribute: {full}");
            File.SetAttributes(full, attrs & ~FileAttributes.ReadOnly);
        }

        // Try OpenDoc6 first
        _err = 0; _warn = 0;
        var doc = _sw.OpenDoc6(
            full,
            (int)SwConst.swDocumentTypes_e.swDocPART,
            (int)(SwConst.swOpenDocOptions_e.swOpenDocOptions_Silent),
            "",
            ref _err,
            ref _warn);

        if (doc == null)
        {
            Logger.Warn($"[PartEditor] OpenDoc6 returned null (err={_err}, warn={_warn}). Trying OpenDoc7...");
            try
            {
                dynamic spec = _sw.GetOpenDocSpec(full);
                spec.DocumentType = (int)SwConst.swDocumentTypes_e.swDocPART;
                spec.Silent = true;
                spec.ReadOnly = false;
                spec.LightWeight = false;
                doc = _sw.OpenDoc7(spec);
                Logger.Info($"[PartEditor] OpenDoc7 returned {(doc is null ? "null" : "a document")}.");
            }
            catch (Exception ex)
            {
                Logger.Error($"[PartEditor] OpenDoc7 exception: {ex.Message}");
            }
        }

        _model = doc as Sw.ModelDoc2;
        if (_model == null)
        {
            var reason = DecodeOpenError(_err);
            Logger.Error($"[PartEditor] Failed to open part. err={_err} ({reason}), warn={_warn}. Path: {full}");
            throw new InvalidOperationException($"Failed to open part. err={_err} ({reason}), warn={_warn}\nPath: {full}");
        }

        _partPath = full;
        _ext = _model.Extension;
        Logger.Success($"[PartEditor] Part opened: {_partPath}");
        Rebuild();
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

    public void Save()
    {
        Logger.Info("[PartEditor] Save");
        Model.Save3((int)SwConst.swSaveAsOptions_e.swSaveAsOptions_Silent, ref _err, ref _warn);
        Logger.Success($"[PartEditor] Saved (err={_err}, warn={_warn})");
    }

    public void Close()
    {
        Logger.Info($"[PartEditor] Close → '{(_partPath ?? "(null)")}'");
        try { _sw.CloseDoc(_partPath); Logger.Success("[PartEditor] Closed."); }
        catch (Exception ex) { Logger.Warn($"[PartEditor] Close exception: {ex.Message}"); }
    }

    public void Rebuild()
    {
        Logger.Info("[PartEditor] Rebuild");
        Model.EditRebuild3();
    }

    // --------------------------------- Config & Equations -----------------------------------

    public void ActivateConfiguration(string configName)
    {
        Logger.Info($"[PartEditor] ActivateConfiguration → '{configName}'");
        if (string.IsNullOrWhiteSpace(configName) || _model is null)
        {
            Logger.Warn("[PartEditor] ActivateConfiguration skipped (no model or empty name).");
            return;
        }

        try
        {
            var namesObj = _model.GetConfigurationNames();
            var names = (namesObj as object[] ?? Array.Empty<object>())
                        .Select(o => o?.ToString())
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToList();

            Logger.Info($"[PartEditor] Available configurations: {string.Join(", ", names)}");

            var target =
                names.FirstOrDefault(n => string.Equals(n, configName, StringComparison.Ordinal)) ??
                names.FirstOrDefault(n => string.Equals(n, configName, StringComparison.OrdinalIgnoreCase));

            if (target is null)
            {
                Logger.Warn($"[PartEditor] Config '{configName}' not found; keeping current.");
                return;
            }

            _model.ShowConfiguration2(target);
            Logger.Success($"[PartEditor] Activated configuration: {target}");
            // Rebuild();  // optional
        }
        catch (Exception ex)
        {
            Logger.Warn($"[PartEditor] ActivateConfiguration fast-path failed: {ex.Message}. Trying ShowConfiguration2...");
            try { _model.ShowConfiguration2(configName); Logger.Success($"[PartEditor] Activated configuration (fallback): {configName}"); Rebuild(); }
            catch { Logger.Error("[PartEditor] Fallback activation failed."); }
        }
    }

    public void UpdateEquations(string equationFilePath)
    {
        Logger.Info($"[PartEditor] UpdateEquations → '{equationFilePath}'");
        if (!File.Exists(equationFilePath))
        {
            Logger.Error($"[PartEditor] Equations file not found: {equationFilePath}");
            throw new FileNotFoundException("Equations file not found.", equationFilePath);
        }

        var eq = Model.GetEquationMgr();

        bool prevAutoSolve = eq.AutomaticSolveOrder;
        bool prevAutoRebuild = eq.AutomaticRebuild;
        try
        {
            eq.AutomaticSolveOrder = true;
            eq.AutomaticRebuild = true;
            eq.FilePath = equationFilePath;

            var ok = eq.UpdateValuesFromExternalEquationFile();
            Logger.Info($"[PartEditor] UpdateValuesFromExternalEquationFile() → {ok}");

            // Nudge rebuild after import (helps SOLIDWORKS pick up changes reliably)
            Logger.Info("[PartEditor] Rebuilding after equations import…");
            Model.EditRebuild3();
            Model.ForceRebuild3(false);
            Model.EditRebuild3();
            Model.GraphicsRedraw2();

            Logger.Success("[PartEditor] Equations updated and rebuild completed.");
        }
        finally
        {
            eq.AutomaticSolveOrder = prevAutoSolve;
            eq.AutomaticRebuild = prevAutoRebuild;
        }
    }

    // --------------------------------- Properties & Values ----------------------------------

    public void SetEngraving(string? text)
    {
        Logger.Info($"[PartEditor] SetEngraving → '{text ?? "(null)"}'");
        var mgr = Model.Extension.get_CustomPropertyManager("");
        var rc = mgr.Set2("Engraving", text ?? "");
        Logger.Info($"[PartEditor] Engraving Set2 result: {rc}");
    }

    public void SetDimensionMeters(string fullName, double meters)
    {
        Logger.Info($"[PartEditor] SetDimensionMeters → {fullName} = {meters} m");
        var dim = Model.Parameter(fullName) as SwDim;
        if (dim == null)
        {
            Logger.Warn($"[PartEditor] Dimension not found: {fullName}");
            return;
        }
        dim.SystemValue = meters;
        // Rebuild();
        Logger.Success($"[PartEditor] Dimension set: {fullName}");
    }

    // -------------------------------- Suppress Feature / Sketch -----------------------------

    public void SuppressFeature(string name, bool suppress)
    {
        Logger.Info($"[PartEditor] SuppressFeature → name='{name}', suppress={suppress}");
        if (_ext!.SelectByID2(name, "BODYFEATURE", 0, 0, 0, false, 0, null, 0))
        {
            if (suppress) Model.EditSuppress2();
            else Model.EditUnsuppress2();
            Model.ClearSelection2(true);
            // Rebuild();
            Logger.Success($"[PartEditor] Feature {(suppress ? "suppressed" : "unsuppressed")}: {name}");
        }
        else
        {
            Logger.Warn($"[PartEditor] Feature not selectable: {name}");
        }
    }

    public void SuppressSketch(string name, bool suppress)
    {
        Logger.Info($"[PartEditor] SuppressSketch → name='{name}', suppress={suppress}");
        var feat = FindFirstFeatureByExact(name);
        if (feat == null)
        {
            Logger.Warn($"[PartEditor] Sketch/feature not found: {name}");
            return;
        }

        feat.SetSuppression2(
            suppress ? (int)SwConst.swFeatureSuppressionAction_e.swSuppressFeature
                     : (int)SwConst.swFeatureSuppressionAction_e.swUnSuppressFeature,
            (int)SwConst.swInConfigurationOpts_e.swThisConfiguration,
            null);

        // Rebuild();
        Logger.Success($"[PartEditor] Sketch {(suppress ? "suppressed" : "unsuppressed")}: {name}");
    }

    // ------------------------------------- Tolerances ---------------------------------------

    /// <summary>
    /// Reference-style tolerance setter: resolves "KEY@OWNER" and uses swDim.Tolerance.SetValues(-lower, upper).
    /// </summary>
    public void ApplyLengthTolerances(WedgeData wedge, IEnumerable<DimensionKey> keys)
    {
        var shortNames = keys?.Select(k => k.Value).ToList() ?? new List<string>();
        Logger.Info($"[PartEditor] ApplyLengthTolerances(ref) → [{string.Join(", ", shortNames)}]");

        // Discover owners at runtime (features + sub-features including sketches)
        var allOwners = GetAllFeatureAndSketchNames(Model);
        Logger.Info($"[PartEditor] Owners discovered: {allOwners.Count}");

        foreach (var shortName in shortNames)
        {
            try
            {
                if (!wedge.Dimensions.TryGetValue(DimensionKey.From(shortName), out var d) || d is null)
                {
                    Logger.Warn($"[ApplyTolerances] Input dimensions missing key '{shortName}'.");
                    continue;
                }

                // mm → meters (SW internal unit)
                double upper_m = (double)d.Tol.Upper.AsMm() / 1000.0;
                double lower_m = (double)d.Tol.Lower.AsMm() / 1000.0;

                // Probe KEY@OWNER across discovered owners
                if (!TryGetDimensionByShortName(Model, shortName, allOwners, out var swDim) || swDim is null)
                {
                    Logger.Warn($"[ApplyTolerances] Could not locate Dimension for '{shortName}' (owner unknown).");
                    continue;
                }

                var tol = swDim.Tolerance;
                if (tol == null)
                {
                    Logger.Warn($"[ApplyTolerances] Tolerance object null for '{shortName}'.");
                    continue;
                }

                tol.Type = (Math.Abs(upper_m - lower_m) > 1e-12)
                    ? (int)SwConst.swTolType_e.swTolBILAT
                    : (int)SwConst.swTolType_e.swTolSYMMETRIC;

                // SolidWorks expects SetValues(minusLower, plusUpper)
                tol.SetValues(-lower_m, upper_m);

                Logger.Success($"[ApplyTolerances] Applied to '{shortName}' → +{upper_m:G6} / -{lower_m:G6} m.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[ApplyTolerances] Failed for '{shortName}': {ex.Message}");
            }
        }

        // Optional: single rebuild after batch
        // Rebuild();
    }

    // ------------------------------------ Helpers -------------------------------------------

    /// <summary>
    /// Enumerates all feature and sub-feature names (owners) in the active part,
    /// including sketches, to probe KEY@OWNER reliably.
    /// </summary>
    private static List<string> GetAllFeatureAndSketchNames(Sw.ModelDoc2 model)
    {
        var names = new List<string>();
        var part = (SwPart)model;
        var f = (SwFeat)part.FirstFeature();

        while (f != null)
        {
            if (!string.IsNullOrWhiteSpace(f.Name))
                names.Add(f.Name);

            var sub = (SwFeat)f.GetFirstSubFeature();
            while (sub != null)
            {
                if (!string.IsNullOrWhiteSpace(sub.Name))
                    names.Add(sub.Name);
                sub = (SwFeat)sub.GetNextSubFeature();
            }

            f = (SwFeat)f.GetNextFeature();
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Tries to resolve a SolidWorks Dimension by probing "shortName@owner" across discovered owners.
    /// </summary>
    private static bool TryGetDimensionByShortName(Sw.ModelDoc2 model, string shortName, List<string> owners, out SwDim? swDim)
    {
        foreach (var owner in owners)
        {
            var probe = $"{shortName}@{owner}";
            var dim = model.Parameter(probe) as SwDim;
            if (dim != null)
            {
                Logger.Info($"[ApplyTolerances] Resolved '{probe}'");
                swDim = dim;
                return true;
            }
        }

        Logger.Warn($"[ApplyTolerances] Not found for '{shortName}@*'");
        swDim = null;
        return false;
    }

    private List<string> GetAllOwners()
    {
        // kept for compatibility with some logs/flows; equivalent to GetAllFeatureAndSketchNames(Model)
        var names = GetAllFeatureAndSketchNames(Model);
        Logger.Info($"[PartEditor] GetAllOwners → {names.Count} names collected.");
        return names;
    }

    private SwDim? FindDimension(string shortName, List<string> owners)
    {
        foreach (var owner in owners)
        {
            var probe = $"{shortName}@{owner}";
            var dim = Model.Parameter(probe) as SwDim;
            if (dim != null)
            {
                Logger.Info($"[PartEditor] FindDimension → found '{probe}'");
                return dim;
            }
        }
        Logger.Warn($"[PartEditor] FindDimension → not found for '{shortName}@*'");
        return null;
    }

    private SwFeat? FindFirstFeatureByExact(string name)
    {
        var part = (SwPart)Model;
        var f = (SwFeat)part.FirstFeature();

        while (f != null)
        {
            if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info($"[PartEditor] FindFirstFeatureByExact → found '{name}' (top-level)");
                return f;
            }

            var sub = (SwFeat)f.GetFirstSubFeature();
            while (sub != null)
            {
                if (string.Equals(sub.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Info($"[PartEditor] FindFirstFeatureByExact → found '{name}' (sub-feature)");
                    return sub;
                }
                sub = (SwFeat)sub.GetNextSubFeature();
            }

            f = (SwFeat)f.GetNextFeature();
        }
        Logger.Warn($"[PartEditor] FindFirstFeatureByExact → '{name}' not found.");
        return null;
    }
}
