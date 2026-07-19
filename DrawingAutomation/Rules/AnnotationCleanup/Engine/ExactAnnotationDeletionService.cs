using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using WAD.Runner.Application;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

// Prevent collision with namespaces named Annotation inside the project.
using SwAnnotation = SolidWorks.Interop.sldworks.Annotation;
using SwDimension = SolidWorks.Interop.sldworks.Dimension;
using SwDisplayDimension = SolidWorks.Interop.sldworks.DisplayDimension;
using SwDrawingDoc = SolidWorks.Interop.sldworks.DrawingDoc;
using SwModelDoc2 = SolidWorks.Interop.sldworks.ModelDoc2;
using SwView = SolidWorks.Interop.sldworks.View;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Engine;

/// <summary>
/// Deletes only the exact DisplayDimension objects that were included in
/// the cleanup plan.
///
/// Performance strategy:
/// 1. Traverse the drawing views once and resolve every planned target.
/// 2. Enumerate each affected view's DisplayDimensions once before delete.
/// 3. Select every resolved annotation across every affected view.
/// 4. Call DeleteSelection2 exactly once for the entire cleanup batch.
/// 5. Re-enumerate each affected view once to verify the result.
///
/// This avoids the old O(targets x annotations) COM pattern that re-read a
/// view and deleted one annotation at a time.
/// </summary>
public sealed class ExactAnnotationDeletionService
{
    /// <summary>
    /// Deletes all planned annotations across all drawing views as one
    /// SolidWorks selection/deletion batch.
    /// </summary>
    public IReadOnlyList<ExactAnnotationDeletionResult> DeleteBatch(
        SwModelDoc2 drawingModel,
        IReadOnlyCollection<AnnotationDeletionTarget> plannedTargets,
        string logPrefix)
    {
        if (drawingModel is null)
            throw new ArgumentNullException(nameof(drawingModel));

        var plannedByView = NormalizePlan(plannedTargets);

        if (plannedByView.Count == 0)
            return Array.Empty<ExactAnnotationDeletionResult>();

        if (drawingModel is not SwDrawingDoc drawing)
        {
            return plannedByView
                .Select(kv => new ExactAnnotationDeletionResult(
                    kv.Key,
                    kv.Value,
                    Array.Empty<string>(),
                    kv.Value,
                    Array.Empty<string>()))
                .ToList();
        }

        var viewMap = BuildDrawingViewMap(drawing);
        var snapshots = new Dictionary<string, ViewSnapshot>(
            StringComparer.OrdinalIgnoreCase);
        var resolved = new List<ResolvedTarget>();
        var failedByView = new Dictionary<string, HashSet<string>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in plannedByView)
        {
            var viewName = entry.Key;
            var planned = entry.Value;

            if (!viewMap.TryGetValue(viewName, out var view))
            {
                Logger.Warn(
                    $"[{logPrefix}.ExactDelete] View '{viewName}' " +
                    "was not found; no annotations were deleted.");

                AddFailures(failedByView, viewName, planned);
                continue;
            }

            // One COM enumeration per affected view before deletion.
            var handles = Enumerate(view);
            snapshots[viewName] = new ViewSnapshot(view, handles);

            foreach (var target in planned)
            {
                var candidates = handles
                    .Where(handle => string.Equals(
                        handle.FullName,
                        target,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Only use identity fallback when there is no exact match.
                if (candidates.Count == 0)
                {
                    candidates = handles
                        .Where(handle => AnnotationNameIdentity.AreEquivalent(
                            handle.FullName,
                            target))
                        .ToList();
                }

                if (candidates.Count != 1)
                {
                    AddFailure(failedByView, viewName, target);

                    Logger.Warn(
                        $"[{logPrefix}.ExactDelete] Skipped '{target}' " +
                        $"in '{viewName}': expected one matching annotation, " +
                        $"found {candidates.Count}.");

                    continue;
                }

                resolved.Add(new ResolvedTarget(
                    viewName,
                    target,
                    candidates[0]));
            }
        }

        // A defensive guard against selecting the same concrete annotation
        // more than once through two equivalent planned identities.
        resolved = RemoveDuplicateResolvedTargets(
            resolved,
            failedByView,
            logPrefix);

        var selected = SelectAll(
            drawingModel,
            resolved,
            failedByView,
            logPrefix);

        if (selected.Count > 0)
        {
            var extension = drawingModel.Extension;
            var deleteReturnedTrue = false;

            try
            {
                // One deletion call for every selected annotation in every view.
                deleteReturnedTrue =
                    extension is not null &&
                    extension.DeleteSelection2(0);
            }
            catch (Exception ex)
            {
                Logger.Warn(
                    $"[{logPrefix}.ExactDelete] Batch DeleteSelection2 " +
                    $"failed: {ex.Message}");
            }
            finally
            {
                SafeClearSelection(drawingModel);
            }

            if (!deleteReturnedTrue)
            {
                Logger.Warn(
                    $"[{logPrefix}.ExactDelete] Batch DeleteSelection2 " +
                    "returned false. The post-delete audit will determine " +
                    "which selected annotations, if any, were actually removed.");
            }
        }
        else
        {
            SafeClearSelection(drawingModel);
        }

        var selectedNamesByView = selected
            .GroupBy(x => x.ViewName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new HashSet<string>(
                    group.Select(x => x.Handle.FullName),
                    StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        var results = new List<ExactAnnotationDeletionResult>();

        foreach (var entry in plannedByView)
        {
            var viewName = entry.Key;
            var planned = entry.Value;

            if (!snapshots.TryGetValue(viewName, out var snapshot))
            {
                var missingViewFailed = GetFailures(
                    failedByView,
                    viewName,
                    planned);

                results.Add(new ExactAnnotationDeletionResult(
                    viewName,
                    planned,
                    Array.Empty<string>(),
                    missingViewFailed,
                    Array.Empty<string>()));

                continue;
            }

            var before = snapshot.Handles
                .Select(handle => handle.FullName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // One COM enumeration per affected view after deletion.
            var after = Enumerate(snapshot.View)
                .Select(handle => handle.FullName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var removedActual = before
                .Except(after, StringComparer.OrdinalIgnoreCase)
                .ToList();

            selectedNamesByView.TryGetValue(
                viewName,
                out var selectedNames);

            selectedNames ??= new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            var unexpectedRemoved = removedActual
                .Where(removed => !selectedNames.Contains(removed))
                .ToList();

            if (unexpectedRemoved.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Unexpected annotation deletion detected in view " +
                    $"'{viewName}': {string.Join(", ", unexpectedRemoved)}");
            }

            var failed = GetFailures(
                failedByView,
                viewName,
                Array.Empty<string>())
                .ToList();

            // A target may have been represented by an equivalent full name.
            // Anything still present after the batch is considered failed.
            foreach (var target in planned)
            {
                var stillPresent = after.Any(actual =>
                    string.Equals(
                        actual,
                        target,
                        StringComparison.OrdinalIgnoreCase) ||
                    AnnotationNameIdentity.AreEquivalent(actual, target));

                if (stillPresent &&
                    !failed.Contains(target, StringComparer.OrdinalIgnoreCase))
                {
                    failed.Add(target);
                }
            }

            foreach (var removed in removedActual)
            {
                Logger.Info(
                    $"[{logPrefix}.ExactDelete] Batch deleted exact " +
                    $"dimension FullName='{removed}' in view '{viewName}'.");
            }

            results.Add(new ExactAnnotationDeletionResult(
                viewName,
                planned,
                removedActual,
                failed
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                unexpectedRemoved));
        }

        Logger.Info(
            $"[{logPrefix}.ExactDelete] Batch complete: " +
            $"planned={plannedByView.Sum(x => x.Value.Count)}, " +
            $"resolved={resolved.Count}, selected={selected.Count}, " +
            $"deleted={results.Sum(x => x.DeletedCount)}, " +
            $"DeleteSelection2 calls={(selected.Count > 0 ? 1 : 0)}.");

        return results;
    }

    /// <summary>
    /// Compatibility wrapper for callers that still delete a single view.
    /// The implementation still uses the optimized batch path internally.
    /// </summary>
    public ExactAnnotationDeletionResult DeleteInView(
        SwModelDoc2 drawingModel,
        string viewName,
        IReadOnlyCollection<string> plannedFullNames,
        string logPrefix)
    {
        if (string.IsNullOrWhiteSpace(viewName))
        {
            throw new ArgumentException(
                "View name is required.",
                nameof(viewName));
        }

        var targets = (plannedFullNames ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => new AnnotationDeletionTarget(
                viewName.Trim(),
                x.Trim()))
            .ToList();

        if (targets.Count == 0)
            return ExactAnnotationDeletionResult.Empty(viewName);

        return DeleteBatch(
                drawingModel,
                targets,
                logPrefix)
            .FirstOrDefault() ??
            ExactAnnotationDeletionResult.Empty(viewName);
    }

    private static Dictionary<string, IReadOnlyCollection<string>> NormalizePlan(
        IReadOnlyCollection<AnnotationDeletionTarget> plannedTargets)
    {
        return (plannedTargets ?? Array.Empty<AnnotationDeletionTarget>())
            .Where(target =>
                target is not null &&
                !string.IsNullOrWhiteSpace(target.ViewName) &&
                !string.IsNullOrWhiteSpace(target.AnnotationFullName))
            .GroupBy(
                target => target.ViewName.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyCollection<string>)group
                    .Select(target => target.AnnotationFullName.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, SwView> BuildDrawingViewMap(
        SwDrawingDoc drawing)
    {
        var result = new Dictionary<string, SwView>(
            StringComparer.OrdinalIgnoreCase);

        try
        {
            // The first view is the sheet. Actual drawing views follow it.
            var sheetView = drawing.GetFirstView() as SwView;
            var view = sheetView?.GetNextView() as SwView;

            while (view is not null)
            {
                var name = SafeViewName(view);

                if (!string.IsNullOrWhiteSpace(name) &&
                    !result.ContainsKey(name))
                {
                    result.Add(name, view);
                }

                view = view.GetNextView() as SwView;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(
                $"[ExactAnnotationDeletionService] Failed enumerating " +
                $"drawing views: {ex.Message}");
        }

        return result;
    }

    private static List<ResolvedTarget> RemoveDuplicateResolvedTargets(
        IReadOnlyCollection<ResolvedTarget> resolved,
        IDictionary<string, HashSet<string>> failedByView,
        string logPrefix)
    {
        var unique = new List<ResolvedTarget>();

        foreach (var group in resolved.GroupBy(
                     x => x.ViewName + "||" + x.Handle.FullName,
                     StringComparer.OrdinalIgnoreCase))
        {
            var entries = group.ToList();

            if (entries.Count == 1)
            {
                unique.Add(entries[0]);
                continue;
            }

            foreach (var entry in entries)
                AddFailure(failedByView, entry.ViewName, entry.PlannedFullName);

            Logger.Warn(
                $"[{logPrefix}.ExactDelete] Multiple planned targets resolved " +
                $"to the same concrete annotation '{entries[0].Handle.FullName}' " +
                $"in view '{entries[0].ViewName}'. The duplicate mapping was " +
                "skipped for safety.");
        }

        return unique;
    }

    private static List<ResolvedTarget> SelectAll(
        SwModelDoc2 drawingModel,
        IReadOnlyCollection<ResolvedTarget> resolved,
        IDictionary<string, HashSet<string>> failedByView,
        string logPrefix)
    {
        var candidates = resolved.ToList();

        if (candidates.Count == 0)
            return new List<ResolvedTarget>();

        SafeClearSelection(drawingModel);

        var extension = drawingModel.Extension;

        // Fast path: one COM call selects the entire annotation array.
        if (extension is not null)
        {
            try
            {
                var objects = candidates
                    .Select(x => (object)x.Handle.Annotation)
                    .ToArray();

                var selectedCount = extension.MultiSelect2(
                    objects,
                    false,
                    null);

                if (selectedCount == candidates.Count)
                {
                    Logger.Info(
                        $"[{logPrefix}.ExactDelete] MultiSelect2 selected " +
                        $"all {selectedCount} annotations in one call.");

                    return candidates;
                }

                Logger.Warn(
                    $"[{logPrefix}.ExactDelete] MultiSelect2 selected " +
                    $"{selectedCount}/{candidates.Count} annotations. " +
                    "Retrying selection with Annotation.Select3 while " +
                    "preserving a single batch delete operation.");
            }
            catch (Exception ex)
            {
                Logger.Warn(
                    $"[{logPrefix}.ExactDelete] MultiSelect2 was not usable: " +
                    $"{ex.Message}. Falling back to Annotation.Select3.");
            }
        }

        // Compatibility fallback. This is still far cheaper than the old
        // implementation because there is no re-enumeration or deletion
        // between selections; DeleteSelection2 is still called only once.
        SafeClearSelection(drawingModel);

        var selected = new List<ResolvedTarget>();

        foreach (var candidate in candidates)
        {
            try
            {
                var append = selected.Count > 0;
                var wasSelected = candidate.Handle.Annotation.Select3(
                    append,
                    null);

                if (wasSelected)
                {
                    selected.Add(candidate);
                    continue;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(
                    $"[{logPrefix}.ExactDelete] Could not select " +
                    $"'{candidate.Handle.FullName}' in " +
                    $"'{candidate.ViewName}': {ex.Message}");
            }

            AddFailure(
                failedByView,
                candidate.ViewName,
                candidate.PlannedFullName);
        }

        return selected;
    }

    private static string SafeViewName(SwView view)
    {
        try
        {
            return view.Name?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static IReadOnlyList<DisplayDimensionHandle> Enumerate(
        SwView view)
    {
        object? raw;

        try
        {
            raw = view.GetDisplayDimensions();
        }
        catch
        {
            return Array.Empty<DisplayDimensionHandle>();
        }

        if (raw is not object[] displayDimensions ||
            displayDimensions.Length == 0)
        {
            return Array.Empty<DisplayDimensionHandle>();
        }

        var results = new List<DisplayDimensionHandle>(
            displayDimensions.Length);

        foreach (var item in displayDimensions)
        {
            if (item is not SwDisplayDimension displayDimension)
                continue;

            SwDimension? dimension = null;
            SwAnnotation? annotation = null;

            try
            {
                dimension = displayDimension.GetDimension() as SwDimension;
            }
            catch
            {
                // Ignore malformed dimensions.
            }

            try
            {
                annotation = displayDimension.GetAnnotation() as SwAnnotation;
            }
            catch
            {
                // Ignore annotations that cannot be retrieved.
            }

            if (dimension is null || annotation is null)
                continue;

            string fullName;
            string dimensionName;

            try
            {
                fullName = dimension.FullName?.Trim() ?? string.Empty;
            }
            catch
            {
                continue;
            }

            try
            {
                dimensionName = dimension.Name?.Trim() ?? string.Empty;
            }
            catch
            {
                dimensionName =
                    AnnotationNameIdentity.GetDimensionName(fullName);
            }

            if (string.IsNullOrWhiteSpace(fullName))
                continue;

            results.Add(new DisplayDimensionHandle(
                fullName,
                dimensionName,
                annotation));
        }

        return results;
    }

    private static void SafeClearSelection(SwModelDoc2 drawingModel)
    {
        try
        {
            drawingModel.ClearSelection2(true);
        }
        catch
        {
            // Selection cleanup is best-effort and must not mask the result.
        }
    }

    private static void AddFailure(
        IDictionary<string, HashSet<string>> failedByView,
        string viewName,
        string target)
    {
        if (!failedByView.TryGetValue(viewName, out var failures))
        {
            failures = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            failedByView[viewName] = failures;
        }

        failures.Add(target);
    }

    private static void AddFailures(
        IDictionary<string, HashSet<string>> failedByView,
        string viewName,
        IEnumerable<string> targets)
    {
        foreach (var target in targets)
            AddFailure(failedByView, viewName, target);
    }

    private static IReadOnlyCollection<string> GetFailures(
        IReadOnlyDictionary<string, HashSet<string>> failedByView,
        string viewName,
        IEnumerable<string> fallback)
    {
        if (failedByView.TryGetValue(viewName, out var failures))
            return failures.ToList();

        return fallback
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed record DisplayDimensionHandle(
        string FullName,
        string DimensionName,
        SwAnnotation Annotation);

    private sealed record ViewSnapshot(
        SwView View,
        IReadOnlyList<DisplayDimensionHandle> Handles);

    private sealed record ResolvedTarget(
        string ViewName,
        string PlannedFullName,
        DisplayDimensionHandle Handle);
}

public sealed record ExactAnnotationDeletionResult(
    string ViewName,
    IReadOnlyCollection<string> Planned,
    IReadOnlyCollection<string> Deleted,
    IReadOnlyCollection<string> Failed,
    IReadOnlyCollection<string> UnexpectedDeleted)
{
    public int DeletedCount => Deleted.Count;

    public bool IsSafe =>
        UnexpectedDeleted.Count == 0;

    public static ExactAnnotationDeletionResult Empty(
        string viewName)
    {
        return new ExactAnnotationDeletionResult(
            viewName,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>());
    }
}
