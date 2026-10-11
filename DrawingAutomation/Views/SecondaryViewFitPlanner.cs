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
    /// The strict left boundary for ALL secondary view outlines is the
    /// rightmost PRIMARY view edge + the required inter-view gap.
    ///
    /// Front/Side/Top stay wherever the primary layout put them; this
    /// method only READS their rectangles. Never move a secondary view to
    /// satisfy this boundary: it must be satisfied by scale reduction alone.
    /// </summary>
    internal static double GetRequiredSecondaryMinX(
        ViewRect usableSheet,
        IReadOnlyList<ViewRect> primaryRects,
        double gapMeters)
    {
        if (usableSheet is null)
            throw new ArgumentNullException(nameof(usableSheet));
        if (primaryRects is null)
            throw new ArgumentNullException(nameof(primaryRects));
        if (!double.IsFinite(gapMeters) || gapMeters < 0.0)
            throw new ArgumentOutOfRangeException(nameof(gapMeters));

        var requiredMinX = usableSheet.MinX;

        foreach (var primary in primaryRects)
        {
            if (primary is null)
                throw new InvalidOperationException(
                    "Cannot determine right-side boundary: primary outline missing.");

            requiredMinX = Math.Max(
                requiredMinX,
                primary.MaxX + gapMeters);
        }

        return requiredMinX;
    }

    /// <summary>
    /// A valid secondary outline is COMPLETELY to the right of ALL primary
    /// outlines, inside the horizontal sheet limits, clear of actual primary
    /// rectangles, and compliant with optional explicit size/height caps.
    /// Its vertical position can extend beyond the sheet by default.
    ///
    /// In particular, vertical separation does NOT exempt a secondary
    /// from the strict "right of Front/Side/Top" requirement.
    /// </summary>
    internal static bool IsIndividuallyValid(
        ViewRect rect,
        ViewRect usableSheet,
        IReadOnlyList<ViewRect> primaryRects,
        double gapMeters,
        SecondaryFitPolicy policy)
    {
        var minSecondaryX = GetRequiredSecondaryMinX(
            usableSheet, primaryRects, gapMeters);

        // Hard rule: even a vertically separated Detail/Section must NOT
        // extend left into the primary views' horizontal range.
        if (rect.MinX < minSecondaryX - GeometryEpsilon)
            return false;

        if (rect.MinX < usableSheet.MinX - GeometryEpsilon ||
            rect.MaxX > usableSheet.MaxX + GeometryEpsilon)
            return false;

        if (policy.ClampToSheetHeight &&
            (rect.MinY < usableSheet.MinY - GeometryEpsilon ||
             rect.MaxY > usableSheet.MaxY + GeometryEpsilon))
            return false;

        // With a nondefault size cap, compare width to the usable RIGHT
        // region (not to the whole sheet). With the default cap 1.0,
        // only the actual right-side and sheet-X boundaries apply.
        if (policy.MaxWidthFraction < 1.0 &&
            rect.Width > (usableSheet.MaxX - minSecondaryX) *
                         policy.MaxWidthFraction + GeometryEpsilon)
            return false;

        if (policy.MaxHeightFraction is double heightFraction &&
            rect.Height > usableSheet.Height * heightFraction + GeometryEpsilon)
            return false;

        // Defensive collision check against actual primary geometry.
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
        var rightBoundary = GetRequiredSecondaryMinX(
            usableSheet, primaryRects, gapMeters);

        foreach (var entry in secondaryRects)
        {
            var rect = entry.Value;

            if (rect.MinX < rightBoundary - GeometryEpsilon)
            {
                reason = $"'{entry.Key}' left edge at " +
                         $"{rect.MinX * 1000.0:0.###} mm is left of the " +
                         $"required secondary boundary " +
                         $"{rightBoundary * 1000.0:0.###} mm " +
                         "(rightmost Front/Side/Top + gap)";
                return false;
            }

            if (!IsIndividuallyValid(
                    rect, usableSheet, primaryRects, gapMeters, policy))
            {
                reason = $"'{entry.Key}' exceeds the horizontal sheet " +
                         "limits, an explicit secondary size cap, or " +
                         "overlaps a primary view";
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
