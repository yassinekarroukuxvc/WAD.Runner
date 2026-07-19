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
/// It never deletes by short-name prefix or substring. This prevents a
/// target such as "F" from also deleting FR, FL, FRX or
/// F@ANNOT_FRONT_PLAN.
/// </summary>
public sealed class ExactAnnotationDeletionService
{
    public ExactAnnotationDeletionResult DeleteInView(
        SwModelDoc2 drawingModel,
        string viewName,
        IReadOnlyCollection<string> plannedFullNames,
        string logPrefix)
    {
        if (drawingModel is null)
            throw new ArgumentNullException(nameof(drawingModel));

        if (string.IsNullOrWhiteSpace(viewName))
        {
            throw new ArgumentException(
                "View name is required.",
                nameof(viewName));
        }

        var planned = (plannedFullNames ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (planned.Count == 0)
            return ExactAnnotationDeletionResult.Empty(viewName);

        var view = TryGetDrawingViewByName(
            drawingModel,
            viewName);

        if (view is null)
        {
            Logger.Warn(
                $"[{logPrefix}.ExactDelete] View '{viewName}' " +
                "was not found; no annotations were deleted.");

            return new ExactAnnotationDeletionResult(
                viewName,
                planned,
                Array.Empty<string>(),
                planned,
                Array.Empty<string>());
        }

        var before = Enumerate(view)
            .Select(handle => handle.FullName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var deleted = new List<string>();
        var failed = new List<string>();

        foreach (var target in planned)
        {
            /*
             * Re-enumerate after each deletion because SolidWorks COM
             * objects may become invalid after the annotation collection
             * changes.
             */
            var currentAnnotations = Enumerate(view);

            var candidates = currentAnnotations
                .Where(handle =>
                    string.Equals(
                        handle.FullName,
                        target,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            /*
             * The deletion plan comes from a previous scan. If the exact
             * full name changed slightly, allow equivalent identity
             * matching only when exactly one candidate is found.
             */
            if (candidates.Count == 0)
            {
                candidates = currentAnnotations
                    .Where(handle =>
                        AnnotationNameIdentity.AreEquivalent(
                            handle.FullName,
                            target))
                    .ToList();
            }

            if (candidates.Count != 1)
            {
                failed.Add(target);

                Logger.Warn(
                    $"[{logPrefix}.ExactDelete] Skipped '{target}' " +
                    $"in '{viewName}': expected one matching annotation, " +
                    $"found {candidates.Count}.");

                continue;
            }

            var candidate = candidates[0];

            try
            {
                drawingModel.ClearSelection2(true);

                var selected = candidate.Annotation.Select3(
                    false,
                    null);

                if (!selected)
                {
                    failed.Add(target);

                    Logger.Warn(
                        $"[{logPrefix}.ExactDelete] Could not select " +
                        $"'{candidate.FullName}' in '{viewName}'.");

                    continue;
                }

                /*
                 * DeleteSelection2 acts only on the selected annotation.
                 *
                 * Option 0 avoids deleting absorbed or child features.
                 */
                var extension = drawingModel.Extension;

                var removed =
                    extension is not null &&
                    extension.DeleteSelection2(0);

                if (!removed)
                {
                    failed.Add(target);

                    Logger.Warn(
                        $"[{logPrefix}.ExactDelete] DeleteSelection2 " +
                        $"returned false for '{candidate.FullName}' " +
                        $"in '{viewName}'.");

                    continue;
                }

                deleted.Add(candidate.FullName);

                Logger.Info(
                    $"[{logPrefix}.ExactDelete] Deleted exact dimension " +
                    $"Name='{candidate.DimensionName}' " +
                    $"FullName='{candidate.FullName}' " +
                    $"in view '{viewName}'.");
            }
            catch (Exception ex)
            {
                failed.Add(target);

                Logger.Warn(
                    $"[{logPrefix}.ExactDelete] Failed deleting " +
                    $"'{target}' in '{viewName}': {ex.Message}");
            }
            finally
            {
                try
                {
                    drawingModel.ClearSelection2(true);
                }
                catch
                {
                    // Selection cleanup must not hide the original result.
                }
            }
        }

        var after = Enumerate(view)
            .Select(handle => handle.FullName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var removedActual = before
            .Except(
                after,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        /*
         * Every annotation removed from the view must correspond to an
         * annotation that this service successfully selected and deleted.
         */
        var unexpectedRemoved = removedActual
            .Except(
                deleted,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unexpectedRemoved.Count > 0)
        {
            throw new InvalidOperationException(
                $"Unexpected annotation deletion detected in view " +
                $"'{viewName}': {string.Join(", ", unexpectedRemoved)}");
        }

        /*
         * A target may have been represented by an equivalent full name,
         * so use identity matching when checking whether it remains.
         */
        var stillPresent = planned
            .Where(target =>
                after.Any(actual =>
                    string.Equals(
                        actual,
                        target,
                        StringComparison.OrdinalIgnoreCase) ||
                    AnnotationNameIdentity.AreEquivalent(
                        actual,
                        target)))
            .ToList();

        foreach (var target in stillPresent)
        {
            if (!failed.Contains(
                    target,
                    StringComparer.OrdinalIgnoreCase))
            {
                failed.Add(target);
            }
        }

        return new ExactAnnotationDeletionResult(
            viewName,
            planned,
            removedActual,
            failed
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            unexpectedRemoved);
    }

    private static SwView? TryGetDrawingViewByName(
        SwModelDoc2 drawingModel,
        string viewName)
    {
        if (drawingModel is not SwDrawingDoc drawing)
            return null;

        try
        {
            /*
             * The first SolidWorks drawing view is normally the sheet.
             * Start at GetNextView() to inspect actual drawing views.
             */
            var sheetView = drawing.GetFirstView() as SwView;
            var view = sheetView?.GetNextView() as SwView;

            while (view is not null)
            {
                if (string.Equals(
                        SafeViewName(view),
                        viewName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return view;
                }

                view = view.GetNextView() as SwView;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(
                $"[ExactAnnotationDeletionService] Failed finding view " +
                $"'{viewName}': {ex.Message}");
        }

        return null;
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

        var results = new List<DisplayDimensionHandle>();

        foreach (var item in displayDimensions)
        {
            if (item is not SwDisplayDimension displayDimension)
                continue;

            SwDimension? dimension = null;
            SwAnnotation? annotation = null;

            try
            {
                dimension =
                    displayDimension.GetDimension() as SwDimension;
            }
            catch
            {
                // Ignore malformed dimensions.
            }

            try
            {
                annotation =
                    displayDimension.GetAnnotation() as SwAnnotation;
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
                fullName =
                    dimension.FullName?.Trim() ??
                    string.Empty;
            }
            catch
            {
                continue;
            }

            try
            {
                dimensionName =
                    dimension.Name?.Trim() ??
                    string.Empty;
            }
            catch
            {
                dimensionName =
                    AnnotationNameIdentity.GetDimensionName(fullName);
            }

            if (string.IsNullOrWhiteSpace(fullName))
                continue;

            results.Add(
                new DisplayDimensionHandle(
                    fullName,
                    dimensionName,
                    annotation));
        }

        return results;
    }

    private sealed record DisplayDimensionHandle(
        string FullName,
        string DimensionName,
        SwAnnotation Annotation);
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