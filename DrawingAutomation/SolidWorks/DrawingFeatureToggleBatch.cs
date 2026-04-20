// DrawingAutomation/SolidWorks/DrawingFeatureToggleBatch.cs
using System;
using System.Collections.Generic;
using System.Linq;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;

namespace WAD.Runner.DrawingAutomation.SolidWorks;

/// <summary>
/// Suppresses and unsuppresses named features in a drawing's FeatureManager tree.
///
/// In SolidWorks drawings, the FeatureManager contains sketch features, detail
/// circles, section lines, and other drawing-specific features — distinct from
/// the part features. This class targets those drawing-level features by their
/// exact FeatureManager name.
///
/// Typical drawing feature names:
///   "DetailCircle1"        — the sketch circle that drives a detail view
///   "SectionLine1"         — the sketch line that drives a section view
///   "Sketch3"              — any sheet-level annotation sketch
///   "CalibrationBox"       — a named sketch drawn on the overlay sheet
///
/// Usage:
///   var batch = DrawingFeatureToggleBatch.Build(drawingService);
///   batch.Apply(suppress: new[]{"DetailCircle1"}, unsuppress: new[]{"SectionLine1"});
///
/// IMPORTANT: No rebuild is performed here. The caller must call ds.Rebuild()
/// after applying toggles, exactly as the model-side orchestrator does.
/// </summary>
public sealed class DrawingFeatureToggleBatch
{
    private readonly ModelDoc2 _model;
    private readonly Dictionary<string, Feature> _index;

    private DrawingFeatureToggleBatch(ModelDoc2 model, Dictionary<string, Feature> index)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _index = index;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Build
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Walks the drawing document's FeatureManager and indexes every feature
    /// (including sub-features) by name. Call once per opened drawing.
    /// </summary>
    public static DrawingFeatureToggleBatch Build(DrawingService ds)
    {
        if (ds is null) throw new ArgumentNullException(nameof(ds));

        var model = ds.Model
            ?? throw new InvalidOperationException("DrawingService has no active model.");

        var index = new Dictionary<string, Feature>(StringComparer.OrdinalIgnoreCase);

        var f = model.FirstFeature() as Feature;
        while (f != null)
        {
            TryAdd(index, f);

            // Sub-features (e.g. sketches nested inside a section-line feature)
            var sub = f.GetFirstSubFeature() as Feature;
            while (sub != null)
            {
                TryAdd(index, sub);
                sub = sub.GetNextSubFeature() as Feature;
            }

            f = f.GetNextFeature() as Feature;
        }

        Logger.Info($"[DrawingFeatureToggleBatch] Index built → {index.Count} features.");
        return new DrawingFeatureToggleBatch(model, index);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Apply
    // ─────────────────────────────────────────────────────────────────────

    public sealed class ToggleResult
    {
        public List<string> Suppressed { get; } = new();
        public List<string> Unsuppressed { get; } = new();
        public List<string> SkippedAlreadyCorrect { get; } = new();
        public List<string> Missing { get; } = new();
        public Dictionary<string, string> Failed { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Applies suppress/unsuppress to named drawing features.
    /// Unsuppress wins when a name appears in both lists.
    /// No rebuild is performed — call ds.Rebuild() after this returns.
    /// </summary>
    public ToggleResult Apply(
        IEnumerable<string>? suppress,
        IEnumerable<string>? unsuppress)
    {
        var res = new ToggleResult();

        var supSet = Normalize(suppress);
        var unsupSet = Normalize(unsuppress);

        // Unsuppress wins on conflict
        supSet.ExceptWith(unsupSet);

        Logger.Info($"[DrawingFeatureToggleBatch] Apply → suppress={supSet.Count}, unsuppress={unsupSet.Count}");

        // Unsuppress first (safer order: reveal before hiding)
        foreach (var name in unsupSet)
            Toggle(name, targetSuppressed: false, res);

        foreach (var name in supSet)
            Toggle(name, targetSuppressed: true, res);

        Logger.Info(
            $"[DrawingFeatureToggleBatch] Done → " +
            $"suppressed={res.Suppressed.Count}, unsuppressed={res.Unsuppressed.Count}, " +
            $"skipped={res.SkippedAlreadyCorrect.Count}, missing={res.Missing.Count}, failed={res.Failed.Count}");

        if (res.Missing.Count > 0)
            Logger.Warn("[DrawingFeatureToggleBatch] Missing: " + string.Join(", ", res.Missing));

        if (res.Failed.Count > 0)
            Logger.Warn("[DrawingFeatureToggleBatch] Failed: " +
                string.Join(", ", res.Failed.Select(kv => $"{kv.Key} → {kv.Value}")));

        return res;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Core toggle
    // ─────────────────────────────────────────────────────────────────────

    private void Toggle(string name, bool targetSuppressed, ToggleResult res)
    {
        if (!_index.TryGetValue(name, out var feature))
        {
            res.Missing.Add(name);
            return;
        }

        // Read current suppression state. IsSuppressed2 returns a VARIANT —
        // decode the same way as the model-side FeatureToggleBatch.
        if (TryReadSuppressed(feature, out bool currentlySuppressed)
            && currentlySuppressed == targetSuppressed)
        {
            res.SkippedAlreadyCorrect.Add(name);
            return;
        }

        int action = targetSuppressed
            ? (int)swFeatureSuppressionAction_e.swSuppressFeature
            : (int)swFeatureSuppressionAction_e.swUnSuppressFeature;

        try
        {
            // swAllConfiguration: drawing features typically only have one
            // configuration ("Default"), so this scope is safe for all cases.
            feature.SetSuppression2(
                action,
                (int)swInConfigurationOpts_e.swAllConfiguration,
                null);

            if (targetSuppressed) res.Suppressed.Add(name);
            else res.Unsuppressed.Add(name);
        }
        catch (Exception ex)
        {
            res.Failed[name] = ex.Message;
            Logger.Warn($"[DrawingFeatureToggleBatch] SetSuppression2 failed for '{name}': {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private static bool TryReadSuppressed(Feature feature, out bool suppressed)
    {
        suppressed = false;
        try
        {
            // IsSuppressed2 returns a VARIANT — can be bool, int, short, or arrays of those.
            object? raw = feature.IsSuppressed2(
                (int)swInConfigurationOpts_e.swThisConfiguration, null);

            return TryDecodeSuppressedVariant(raw, out suppressed);
        }
        catch
        {
            return false; // Unknown state — proceed with SetSuppression2 anyway
        }
    }

    private static bool TryDecodeSuppressedVariant(object? raw, out bool suppressed)
    {
        suppressed = false;
        if (raw is null) return false;

        switch (raw)
        {
            case bool b: suppressed = b; return true;
            case int i: suppressed = i != 0; return true;
            case short s: suppressed = s != 0; return true;
            case long l: suppressed = l != 0; return true;
        }

        if (raw is Array arr && arr.Length > 0)
        {
            var first = arr.GetValue(0);
            if (first is Array nested && nested.Length > 0)
                first = nested.GetValue(0);

            switch (first)
            {
                case bool bb: suppressed = bb; return true;
                case int ii: suppressed = ii != 0; return true;
                case short ss: suppressed = ss != 0; return true;
                case long ll: suppressed = ll != 0; return true;
            }
        }

        return false;
    }

    private static void TryAdd(Dictionary<string, Feature> index, Feature f)
    {
        try
        {
            var name = f?.Name;
            if (!string.IsNullOrWhiteSpace(name) && !index.ContainsKey(name))
                index[name] = f;
        }
        catch { /* corrupted COM object — skip */ }
    }

    private static HashSet<string> Normalize(IEnumerable<string>? names)
        => new HashSet<string>(
            (names ?? Enumerable.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim()),
            StringComparer.OrdinalIgnoreCase);
}
