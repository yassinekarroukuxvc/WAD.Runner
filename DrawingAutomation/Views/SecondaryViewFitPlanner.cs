using System;
using System.Collections.Generic;
using System.Linq;

namespace WAD.Runner.DrawingAutomation.Views;

/// <summary>
/// Pure geometry / scale-choice helper. This class NEVER talks to SolidWorks,
/// changes a view position, or changes the primary-view scale.
/// All rectangles and origins use sheet coordinates in meters.
/// </summary>
internal static class SecondaryViewFitPlanner
{
    private const double GeometryEpsilon = 1e-9;
    private const double ScoreEpsilon = 1e-10;

    internal sealed record Snapshot(
        string Name,
        double ConfiguredScale,
        double ReferenceScale,
        double OriginX,
        double OriginY,
        ViewRect ReferenceRect);

    internal sealed record Candidate(
        double Scale,
        ViewRect Rect);

    internal sealed record Choice(
        IReadOnlyDictionary<string, Candidate> ByView,
        double Score,
        double LowestRetention,
        double DetailRetention);

    /// <summary>
    /// Candidate scales descend from the original configured scale, not from
    /// the last pass's scale. The original configured scale is a HARD cap.
    /// The configured scale itself and the minimum are always included.
    /// The number of candidates is bounded to avoid thousands of COM calls
    /// during the exact-measurement fallback.
    /// </summary>
    internal static IReadOnlyList<double> ScaleLadder(
        double configured,
        double minimum,
        double step,
        int maximumSamples)
    {
        if (!double.IsFinite(configured) || configured <= 0 ||
            !double.IsFinite(minimum) || minimum <= 0 ||
            !double.IsFinite(step) || step <= 0 ||
            maximumSamples < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configured), "Invalid secondary scale ladder policy.");
        }

        var floor = Math.Min(configured, minimum);
        var spacing = Math.Max(step, (configured - floor) / (maximumSamples - 1));
        var list = new List<double> { configured };

        for (var scale = configured - spacing;
             scale > floor + GeometryEpsilon;
             scale -= spacing)
        {
            list.Add(scale);
        }

        if (configured > floor + GeometryEpsilon)
            list.Add(floor);

        return list;
    }

    /// <summary>
    /// Fast *prediction* of a scaled outline. Breakline geometry may not be
    /// linear, so every predicted result MUST be remeasured after a rebuild.
    /// </summary>
    internal static ViewRect Project(Snapshot snapshot, double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0 ||
            !double.IsFinite(snapshot.ReferenceScale) ||
            snapshot.ReferenceScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        var ratio = scale / snapshot.ReferenceScale;
        var r = snapshot.ReferenceRect;

        return new ViewRect(
            snapshot.OriginX + (r.MinX - snapshot.OriginX) * ratio,
            snapshot.OriginY + (r.MinY - snapshot.OriginY) * ratio,
            snapshot.OriginX + (r.MaxX - snapshot.OriginX) * ratio,
            snapshot.OriginY + (r.MaxY - snapshot.OriginY) * ratio);
    }

    /// <summary>
    /// The ONLY hard geometric constraints are sheet X bounds, optional sheet
    /// Y bounds, optional user-explicit size caps, and ACTUAL rectangular
    /// collision with a primary view. There is deliberately NO "right of
    /// primary views" region constraint.
    /// </summary>
    internal static bool IsIndividuallyValid(
        ViewRect rect,
        ViewRect usableSheet,
        IReadOnlyList<ViewRect> primaryRects,
        double gapMeters,
        SecondaryFitPolicy policy)
    {
        if (rect.MinX < usableSheet.MinX - GeometryEpsilon ||
            rect.MaxX > usableSheet.MaxX + GeometryEpsilon)
            return false;

        if (policy.ClampToSheetHeight &&
            (rect.MinY < usableSheet.MinY - GeometryEpsilon ||
             rect.MaxY > usableSheet.MaxY + GeometryEpsilon))
            return false;

        // Default MaxWidthFraction=1.0 adds NO constraint beyond sheet X.
        // Unlike the old algorithm this measures fraction of the whole sheet,
        // not the region to the right of the primary views.
        if (policy.MaxWidthFraction < 1.0 &&
            rect.Width > usableSheet.Width * policy.MaxWidthFraction + GeometryEpsilon)
            return false;

        if (policy.MaxHeightFraction is double heightFraction &&
            rect.Height > usableSheet.Height * heightFraction + GeometryEpsilon)
            return false;

        foreach (var primary in primaryRects)
        {
            if (rect.Inflate(gapMeters).Overlaps(primary))
                return false;
        }

        return true;
    }

    internal static bool TryValidate(
        IReadOnlyDictionary<string, ViewRect> secondaryRects,
        IReadOnlyList<ViewRect> primaryRects,
        ViewRect usableSheet,
        double gapMeters,
        SecondaryFitPolicy policy,
        out string reason)
    {
        reason = string.Empty;

        foreach (var entry in secondaryRects)
        {
            if (!IsIndividuallyValid(
                    entry.Value, usableSheet, primaryRects, gapMeters, policy))
            {
                reason = $"'{entry.Key}' exceeds a horizontal sheet limit, " +
                         "an explicit size cap, or overlaps a primary view";
                return false;
            }
        }

        var entries = secondaryRects.ToArray();
        for (var i = 0; i < entries.Length; i++)
        {
            for (var j = i + 1; j < entries.Length; j++)
            {
                if (entries[i].Value.Inflate(gapMeters)
                    .Overlaps(entries[j].Value))
                {
                    reason = $"'{entries[i].Key}' overlaps '{entries[j].Key}'";
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Search the discrete candidate pairs. With only Detail and Section,
    /// the search is cheap. Rank by preserved (configured) scale, with an
    /// optional per-view importance weight. On ties, prefer balanced scales
    /// and finally a larger Detail scale.
    /// Input candidates are already checked against primary views and sheet.
    /// </summary>
    internal static IReadOnlyList<Choice> RankChoices(
        IReadOnlyDictionary<string, IReadOnlyList<Candidate>> candidates,
        IReadOnlyDictionary<string, double> configured,
        double gapMeters,
        SecondaryFitPolicy policy)
    {
        var names = candidates.Keys.OrderBy(
            x => x, StringComparer.OrdinalIgnoreCase).ToArray();

        if (names.Length is < 1 or > 2)
            throw new InvalidOperationException(
                "Secondary fitting supports one or two views (Detail/Section).");

        var output = new List<Choice>();
        var firstName = names[0];
        var secondName = names.Length == 2 ? names[1] : null;
        var firstCandidates = candidates[firstName];

        foreach (var first in firstCandidates)
        {
            IEnumerable<Candidate?> seconds = secondName is null
                ? new Candidate?[] { null }
                : candidates[secondName].Select(x => (Candidate?)x);

            foreach (var second in seconds)
            {
                if (second is not null &&
                    first.Rect.Inflate(gapMeters).Overlaps(second.Rect))
                    continue;

                var firstRetention = first.Scale / configured[firstName];
                var secondRetention = second is not null
                    ? second.Scale / configured[secondName!]
                    : 1.0;

                var firstWeight = Priority(firstName, policy);
                var secondWeight = secondName is not null
                    ? Priority(secondName, policy)
                    : 0.0;

                var score = firstWeight * firstRetention +
                            secondWeight * secondRetention;

                var detailRetention =
                    IsDetail(firstName) ? firstRetention :
                    secondName is not null && IsDetail(secondName)
                        ? secondRetention
                        : firstRetention;

                var selected = new Dictionary<string, Candidate>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    [firstName] = first
                };

                if (secondName is not null && second is not null)
                    selected[secondName] = second;

                output.Add(new Choice(
                    selected,
                    score,
                    Math.Min(firstRetention, secondRetention),
                    detailRetention));
            }
        }

        return output
            .OrderByDescending(x => Math.Round(x.Score / ScoreEpsilon))
            .ThenByDescending(x => x.LowestRetention)
            .ThenByDescending(x => x.DetailRetention)
            .ToArray();
    }

    private static bool IsDetail(string name) =>
        name.Equals("Detail", StringComparison.OrdinalIgnoreCase);

    private static double Priority(string name, SecondaryFitPolicy policy) =>
        IsDetail(name) ? policy.DetailPriority : policy.SectionPriority;
}
