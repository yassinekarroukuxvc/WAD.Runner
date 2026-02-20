// PartAutomation/SolidWorks/PartEditor.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

using Sw = SolidWorks.Interop.sldworks;
using SwConst = SolidWorks.Interop.swconst;
using SwDim = SolidWorks.Interop.sldworks.Dimension;
using SwPart = SolidWorks.Interop.sldworks.PartDoc;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Wedge;

using WAD.Runner.PartAutomation.Rules; // FeatureTogglePlan
using WAD.Runner.PartAutomation.SolidWorks.Interop;

namespace WAD.Runner.PartAutomation.SolidWorks;

public sealed class PartEditor
{
    private readonly Sw.SldWorks _sw;
    private Sw.ModelDoc2? _model;
    private Sw.ModelDocExtension? _ext;
    private string _partPath = "";
    private int _err = 0, _warn = 0;

    // Case-insensitive feature map (mirrors VBA dictionary)
    private IReadOnlyDictionary<string, Feature>? _featMap;

    // Optional: keep the full result (if FeatureIndex.Result contains more metadata)
    private FeatureIndex.Result? _featIndex;

    public PartEditor(Sw.SldWorks sw) => _sw = sw;

    public Sw.ModelDoc2 Model => _model ?? throw new InvalidOperationException("No active part loaded.");

    public void Open(string partPath)
    {
        Logger.Info($"[PartEditor] Open → '{partPath}'");
        if (string.IsNullOrWhiteSpace(partPath))
            throw new ArgumentNullException(nameof(partPath));

        var full = Path.GetFullPath(partPath);
        if (!File.Exists(full))
            throw new FileNotFoundException("Part not found.", full);

        var attrs = File.GetAttributes(full);
        if ((attrs & FileAttributes.ReadOnly) != 0)
        {
            Logger.Warn($"[PartEditor] Clearing read-only attribute: {full}");
            File.SetAttributes(full, attrs & ~FileAttributes.ReadOnly);
        }

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
                // OpenDocSpec is COM; keep it simple and safe
                var specObj = _sw.GetOpenDocSpec(full);
                if (specObj != null)
                {
                    var specType = specObj.GetType();
                    specType.InvokeMember("DocumentType", BindingFlags.SetProperty, null, specObj, new object[] { (int)SwConst.swDocumentTypes_e.swDocPART });
                    specType.InvokeMember("Silent", BindingFlags.SetProperty, null, specObj, new object[] { true });
                    specType.InvokeMember("ReadOnly", BindingFlags.SetProperty, null, specObj, new object[] { false });
                    specType.InvokeMember("LightWeight", BindingFlags.SetProperty, null, specObj, new object[] { false });

                    doc = _sw.OpenDoc7(specObj);
                    Logger.Info($"[PartEditor] OpenDoc7 returned {(doc is null ? "null" : "a document")}.");
                }
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

        RefreshFeatureIndex();
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
        try
        {
            _sw.CloseDoc(_partPath);
            Logger.Success("[PartEditor] Closed.");
        }
        catch (Exception ex)
        {
            Logger.Warn($"[PartEditor] Close exception: {ex.Message}");
        }
        finally
        {
            _featIndex = null;
            _featMap = null;
            _ext = null;
            _model = null;
            _partPath = "";
        }
    }

    public void Rebuild()
    {
        Logger.Info("[PartEditor] Rebuild");
        Model.EditRebuild3();
        Model.ForceRebuild3(false);
        Model.GraphicsRedraw2();
    }

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

            Rebuild();
            RefreshFeatureIndex();
        }
        catch (Exception ex)
        {
            Logger.Warn($"[PartEditor] ActivateConfiguration failed: {ex.Message}");
            try
            {
                _model.ShowConfiguration2(configName);
                Logger.Success($"[PartEditor] Activated configuration (fallback): {configName}");
                Rebuild();
                RefreshFeatureIndex();
            }
            catch
            {
                Logger.Error("[PartEditor] Fallback activation failed.");
            }
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
        string prevPath = eq.FilePath;

        try
        {
            eq.AutomaticSolveOrder = true;
            eq.AutomaticRebuild = true;

            // NOTE: This sets the active part's EquationMgr link to this path.
            // If you don't want the generated part to remain linked to the template file,
            // you must pass a job-local copied equations.txt path here (caller responsibility).
            eq.FilePath = equationFilePath;

            var ok = eq.UpdateValuesFromExternalEquationFile();
            Logger.Info($"[PartEditor] UpdateValuesFromExternalEquationFile() → {ok}");

            Rebuild();
            RefreshFeatureIndex(); // equations can drive suppression / feature state indirectly
            Logger.Success("[PartEditor] Equations updated and rebuild completed.");
        }
        finally
        {
            eq.AutomaticSolveOrder = prevAutoSolve;
            eq.AutomaticRebuild = prevAutoRebuild;

            // If you want to restore the previous link, keep this. If you want to keep the new one, remove.
            // Keeping restore here makes PartEditor "non-destructive" w.r.t. prior linkage.
            try { eq.FilePath = prevPath; } catch { }
        }
    }

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
        Logger.Success($"[PartEditor] Dimension set: {fullName}");
    }

    public void SetOrAddGlobalVarMm(string varName, double mm)
    {
        if (string.IsNullOrWhiteSpace(varName))
            throw new ArgumentException("varName is required.", nameof(varName));

        var rhs = $"{FormatInvariant(mm)}mm";
        SetOrAddGlobalVarRaw(varName, rhs);
    }

    public void SetOrAddGlobalVarDeg(string varName, double deg)
    {
        if (string.IsNullOrWhiteSpace(varName))
            throw new ArgumentException("varName is required.", nameof(varName));

        var rhs = $"{FormatInvariant(deg)}deg";
        SetOrAddGlobalVarRaw(varName, rhs);
    }

    public void SetOrAddGlobalVarRaw(string varName, string rhsWithUnits)
    {
        if (string.IsNullOrWhiteSpace(varName))
            throw new ArgumentException("varName is required.", nameof(varName));
        if (string.IsNullOrWhiteSpace(rhsWithUnits))
            throw new ArgumentException("rhsWithUnits is required.", nameof(rhsWithUnits));

        var eqMgr = (Sw.EquationMgr)Model.GetEquationMgr();

        var eq = $"\"{varName}\"={rhsWithUnits}";
        var idx = FindEquationIndex(eqMgr, varName);

        if (idx >= 0)
        {
            eqMgr.Equation[idx] = eq;
            Logger.Info($"[PartEditor] GlobalVar updated: {eq}");
            return;
        }

        SafeAddEquation(eqMgr, eq);
        Logger.Info($"[PartEditor] GlobalVar added: {eq}");
    }

    private static int FindEquationIndex(Sw.EquationMgr eqMgr, string varName)
    {
        var needle = $"\"{varName}\"=";

        for (int i = 0; i < eqMgr.GetCount(); i++)
        {
            var s = (eqMgr.Equation[i] ?? string.Empty).Replace(" ", string.Empty);
            if (s.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static void SafeAddEquation(object eqMgr, string eq)
    {
        try
        {
            eqMgr.GetType().InvokeMember(
                "Add3",
                BindingFlags.InvokeMethod,
                null,
                eqMgr,
                new object[] { -1, eq, true, (int)swInConfigurationOpts_e.swThisConfiguration, null });
            return;
        }
        catch { }

        try
        {
            eqMgr.GetType().InvokeMember(
                "Add2",
                BindingFlags.InvokeMethod,
                null,
                eqMgr,
                new object[] { -1, eq, true });
            return;
        }
        catch { }

        eqMgr.GetType().InvokeMember(
            "Add",
            BindingFlags.InvokeMethod,
            null,
            eqMgr,
            new object[] { eq });
    }

    private static string FormatInvariant(double d)
    {
        var s = d.ToString("0.###############", CultureInfo.InvariantCulture);
        if (s.EndsWith(".", StringComparison.Ordinal)) s = s[..^1];
        return s;
    }

    // --------------------------- Feature Tree control (modified: no dynamic) ---------------------------

    private static readonly ConcurrentDictionary<Type, (PropertyInfo? PropEnable, MethodInfo? MethEnable, MethodInfo? MethWindow)> _fmCache = new();

    public void DisableFeatureTree()
    {
        try
        {
            var fmObj = Model.FeatureManager;
            if (fmObj is null) return;

            var meta = _fmCache.GetOrAdd(fmObj.GetType(), ResolveFeatureManagerMeta);

            // Try property first, then methods (PIA differences)
            try { meta.PropEnable?.SetValue(fmObj, false); } catch { }
            try { meta.MethEnable?.Invoke(fmObj, new object?[] { false }); } catch { }
            try { meta.MethWindow?.Invoke(fmObj, new object?[] { false }); } catch { }

            Logger.Info("[PartEditor] Feature tree disabled.");
        }
        catch (Exception ex)
        {
            Logger.Warn($"[PartEditor] DisableFeatureTree failed (ignored): {ex.Message}");
        }
    }

    public void EnableFeatureTree()
    {
        try
        {
            var fmObj = Model.FeatureManager;
            if (fmObj is null) return;

            var meta = _fmCache.GetOrAdd(fmObj.GetType(), ResolveFeatureManagerMeta);

            try { meta.PropEnable?.SetValue(fmObj, true); } catch { }
            try { meta.MethEnable?.Invoke(fmObj, new object?[] { true }); } catch { }
            try { meta.MethWindow?.Invoke(fmObj, new object?[] { true }); } catch { }

            Logger.Info("[PartEditor] Feature tree enabled.");
        }
        catch (Exception ex)
        {
            Logger.Warn($"[PartEditor] EnableFeatureTree failed (ignored): {ex.Message}");
        }
    }

    private static (PropertyInfo? PropEnable, MethodInfo? MethEnable, MethodInfo? MethWindow) ResolveFeatureManagerMeta(Type t)
    {
        var prop = t.GetProperty("EnableFeatureTree", BindingFlags.Instance | BindingFlags.Public);
        var methEnable = t.GetMethod("EnableFeatureTree", BindingFlags.Instance | BindingFlags.Public, binder: null, types: new[] { typeof(bool) }, modifiers: null);
        var methWindow = t.GetMethod("EnableFeatureTreeWindow", BindingFlags.Instance | BindingFlags.Public, binder: null, types: new[] { typeof(bool) }, modifiers: null);
        return (prop, methEnable, methWindow);
    }

    // --------------------------- Feature indexing + suppression (VBA-style) ---------------------------

    public void RefreshFeatureIndex()
    {
        try
        {
            // ✅ FIX: FeatureIndex.Build returns FeatureIndex.Result
            _featIndex = FeatureIndex.Build(Model);

            // ✅ FIX: assign the dictionary map
            _featMap = _featIndex.Map;

            Logger.Info($"[PartEditor] FeatureIndex built: {_featMap.Count} entries.");
        }
        catch (Exception ex)
        {
            _featIndex = null;
            _featMap = null;
            Logger.Warn($"[PartEditor] FeatureIndex build failed: {ex.Message}");
        }
    }

    public void ApplyFeaturePlan(FeatureTogglePlan plan, Action<string>? log = null)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));

        RefreshFeatureIndex();

        if (_featMap == null)
        {
            log?.Invoke("[PartEditor] ApplyFeaturePlan: feature index missing; cannot apply plan.");
            return;
        }

        DisableFeatureTree();

        var scoped = new RebuildGuard(Model);
        try
        {
            log?.Invoke($"[PartEditor] ApplyFeaturePlan → OFF={plan.Off.Count}, ON={plan.On.Count}");

            FeatureSuppression.Apply(_featMap, plan.Off, suppress: true, log: log);
            FeatureSuppression.Apply(_featMap, plan.On, suppress: false, log: log);
        }
        finally
        {
            scoped.Dispose();
            EnableFeatureTree();
        }
    }

    public void SuppressFeature(string name, bool suppress)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        if (_featMap == null) RefreshFeatureIndex();

        var key = name.Trim();
        if (_featMap == null || !_featMap.TryGetValue(key, out var feat) || feat is null)
        {
            Logger.Warn($"[PartEditor] Feature not found: {name}");
            return;
        }

        // Use ONLY the fast method (no TryIsSuppressed anywhere)
        var cfgOpt = (int)swInConfigurationOpts_e.swThisConfiguration;
        if (FeatureSuppression.TryIsSuppressedFast(feat, cfgOpt, out var isSuppressed) && isSuppressed == suppress)
            return;

        int action = suppress
            ? (int)swFeatureSuppressionAction_e.swSuppressFeature
            : (int)swFeatureSuppressionAction_e.swUnSuppressFeature;

        feat.SetSuppression2(action, cfgOpt, null);

        Logger.Info($"[PartEditor] {(suppress ? "SUPPRESS" : "UNSUPPRESS")} → {name}");
    }

    public void SuppressSketch(string name, bool suppress)
        => SuppressFeature(name, suppress);

    public bool TrySuppressFeatureIfNeeded(string featureName, bool suppress)
    {
        if (string.IsNullOrWhiteSpace(featureName)) return false;

        if (_featMap == null) RefreshFeatureIndex();

        var key = featureName.Trim();
        if (_featMap == null || !_featMap.TryGetValue(key, out var feat) || feat is null)
        {
            Logger.Warn($"[PartEditor] TrySuppressFeatureIfNeeded: feature not found '{featureName}'");
            return false;
        }

        var cfgOpt = (int)swInConfigurationOpts_e.swThisConfiguration;
        if (FeatureSuppression.TryIsSuppressedFast(feat, cfgOpt, out var isSuppressed) && isSuppressed == suppress)
            return false;

        int action = suppress
            ? (int)swFeatureSuppressionAction_e.swSuppressFeature
            : (int)swFeatureSuppressionAction_e.swUnSuppressFeature;

        feat.SetSuppression2(action, cfgOpt, null);
        return true;
    }

    private sealed class RebuildGuard : IDisposable
    {
        private readonly Sw.ModelDoc2 _m;
        private bool _prevAddToDb;
        private bool _prevDisplayWhenAdded;
        private bool _hasPrevAddToDb;
        private bool _hasPrevDisplayWhenAdded;

        public RebuildGuard(Sw.ModelDoc2 m)
        {
            _m = m;

            try { _prevAddToDb = _m.GetAddToDB(); _hasPrevAddToDb = true; } catch { _hasPrevAddToDb = false; }
            try { _prevDisplayWhenAdded = _m.GetDisplayWhenAdded(); _hasPrevDisplayWhenAdded = true; } catch { _hasPrevDisplayWhenAdded = false; }

            try { _m.SetAddToDB(true); } catch { }
            try { _m.SetDisplayWhenAdded(false); } catch { }
        }

        public void Dispose()
        {
            try { if (_hasPrevAddToDb) _m.SetAddToDB(_prevAddToDb); } catch { }
            try { if (_hasPrevDisplayWhenAdded) _m.SetDisplayWhenAdded(_prevDisplayWhenAdded); } catch { }
        }
    }

    // ------------------------------------- Tolerances ---------------------------------------

    public void ApplyLengthTolerances(WedgeData wedge, IEnumerable<DimensionKey> keys)
    {
        var shortNames = keys?.Select(k => k.Value).ToList() ?? new List<string>();
        Logger.Info($"[PartEditor] ApplyLengthTolerances(ref) → [{string.Join(", ", shortNames)}]");

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

                double upper_m = (double)d.Tol.Upper.AsMm() / 1000.0;
                double lower_m = (double)d.Tol.Lower.AsMm() / 1000.0;

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

                tol.SetValues(-lower_m, upper_m);

                Logger.Success($"[ApplyTolerances] Applied to '{shortName}' → +{upper_m:G6} / -{lower_m:G6} m.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[ApplyTolerances] Failed for '{shortName}': {ex.Message}");
            }
        }
    }

    private static List<string> GetAllFeatureAndSketchNames(Sw.ModelDoc2 model)
    {
        var names = new List<string>();
        var part = (SwPart)model;
        var f = part.FirstFeature() as Feature;

        while (f != null)
        {
            if (!string.IsNullOrWhiteSpace(f.Name))
                names.Add(f.Name);

            var sub = f.GetFirstSubFeature() as Feature;
            while (sub != null)
            {
                if (!string.IsNullOrWhiteSpace(sub.Name))
                    names.Add(sub.Name);
                sub = sub.GetNextSubFeature() as Feature;
            }

            f = f.GetNextFeature() as Feature;
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

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
}
