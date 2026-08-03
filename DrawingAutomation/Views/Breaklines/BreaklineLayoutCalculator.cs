using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Core;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.Wedges;

namespace WAD.Runner.DrawingAutomation.Views.Breaklines;

public sealed class BreaklineLayoutCalculator
{
    private const double PositionEpsilon = 1e-9;

    public bool TryCalculate(
        string logicalView,
        WedgeData wedge,
        DrawingData drawingData,
        double viewScale,
        DrawingWedgeBehavior behavior,
        out BreaklineLayout layout,
        out string error)
    {
        layout = null!;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(logicalView))
        {
            error = "The logical view name is empty.";
            return false;
        }

        if (wedge is null)
        {
            error = "Wedge data is null.";
            return false;
        }

        if (drawingData is null)
        {
            error = "Drawing data is null.";
            return false;
        }

        if (behavior is null)
        {
            error = "Wedge drawing behavior is null.";
            return false;
        }

        if (!double.IsFinite(viewScale) || viewScale <= 1e-6)
        {
            error = $"The current view scale '{viewScale}' is invalid.";
            return false;
        }

        var facts = new DrawingWedgeFacts(wedge);
        var tlMm = behavior.BreaklineTlOverrideMm.HasValue
            ? (double)behavior.BreaklineTlOverrideMm.Value
            : facts.GetLengthMmOrNaN("TL");

        if (!double.IsFinite(tlMm) || tlMm <= 0.0)
        {
            error = "TL is missing or invalid.";
            return false;
        }

        var gapMm = ResolveBreaklineGapMm(drawingData, logicalView);
        var gapSheetMeters = MmToMeters(gapMm);

        double lower;
        double upper;

        if (IsSecondary(logicalView))
        {
            var insetMm = ResolveViewParam(
                drawingData,
                logicalView,
                "detail_inset_mm",
                ResolveGlobalParam(
                    drawingData,
                    "detail_inset_mm",
                    Defaults.DetailInsetMm));

            var halfSpanSheetMeters = MmToMeters(tlMm * 0.5) * viewScale;
            lower = -halfSpanSheetMeters + MmToMeters(insetMm);
            upper = halfSpanSheetMeters;
        }
        else if (IsPrimaryBreaklineView(logicalView))
        {
            var fallbackPct = ResolveGlobalParamAliases(
                drawingData,
                Defaults.FrontFallbackPct,
                "front_fallback_pct",
                "fg_fallback_pct",
                "pgb_fallback_pct");

            var offsetPct = ResolveGlobalParamAliases(
                drawingData,
                Defaults.FrontOffsetPct,
                "front_offset_pct",
                "fg_front_offset_pct",
                "pgb_front_offset_pct");

            var upperPct = ResolveGlobalParamAliases(
                drawingData,
                Defaults.FrontUpperPct,
                "front_upper_pct",
                "fg_front_upper_pct",
                "pgb_front_upper_pct");

            var tlMeters = MmToMeters(tlMm);
            var kMeters = TryGetKMeters(wedge, tlMm, out var resolvedK)
                ? resolvedK
                : Math.Min(MmToMeters(tlMm * fallbackPct), tlMeters * 0.5);

            lower =
                (tlMeters * 0.5 - kMeters + tlMeters * offsetPct) *
                viewScale;

            upper =
                (tlMeters * 0.5 - tlMeters * upperPct) *
                viewScale;
        }
        else
        {
            error = $"'{logicalView}' is not a supported breakline view.";
            return false;
        }

        if (!ValidatePositions(lower, upper, out error))
            return false;

        layout = new BreaklineLayout(
            gapSheetMeters,
            lower,
            upper,
            viewScale);

        return true;
    }

    private static double ResolveBreaklineGapMm(
        DrawingData drawingData,
        string logicalView)
    {
        if (TryGetViewParameters(drawingData, logicalView, out var parameters) &&
            TryGetParameter(parameters, "breakline_gap_mm", out var configuredGapMm) &&
            double.IsFinite(configuredGapMm) &&
            configuredGapMm > 0.0)
        {
            return configuredGapMm;
        }

        return IsSecondary(logicalView)
            ? Defaults.DetailSectionBreaklineGapMm
            : Defaults.FrontSideBreaklineGapMm;
    }

    private static bool TryGetKMeters(
        WedgeData wedge,
        double tlMm,
        out double kMeters)
    {
        kMeters = 0.0;

        try
        {
            var kValue = wedge.KValue;
            if (kValue is null)
                return false;

            var kMm = (double)kValue.ValueMm.Value;
            if (!double.IsFinite(kMm) || kMm <= 0.0)
                return false;

            kMeters = MmToMeters(Math.Min(kMm, tlMm * 0.5));
            return kMeters > 0.0;
        }
        catch
        {
            return false;
        }
    }

    private static double ResolveViewParam(
        DrawingData drawingData,
        string logicalView,
        string key,
        double fallback)
    {
        if (!TryGetViewParameters(drawingData, logicalView, out var parameters) ||
            !TryGetParameter(parameters, key, out var value) ||
            !double.IsFinite(value) ||
            value <= 0.0)
        {
            return fallback;
        }

        return value;
    }

    private static double ResolveGlobalParam(
        DrawingData drawingData,
        string key,
        double fallback)
    {
        if (drawingData.Views is null)
            return fallback;

        foreach (var viewConfig in drawingData.Views.Values)
        {
            if (viewConfig?.Params is null ||
                !TryGetParameter(viewConfig.Params, key, out var value) ||
                !double.IsFinite(value) ||
                value < 0.0)
            {
                continue;
            }

            return value;
        }

        return fallback;
    }

    private static double ResolveGlobalParamAliases(
        DrawingData drawingData,
        double fallback,
        params string[] keys)
    {
        foreach (var key in keys ?? Array.Empty<string>())
        {
            var resolved = ResolveGlobalParam(drawingData, key, double.NaN);
            if (double.IsFinite(resolved))
                return resolved;
        }

        return fallback;
    }

    private static bool TryGetViewParameters(
        DrawingData drawingData,
        string logicalView,
        out IReadOnlyDictionary<string, double> parameters)
    {
        parameters = null!;

        if (drawingData.Views is null)
            return false;

        foreach (var pair in drawingData.Views)
        {
            if (!string.Equals(
                    pair.Key,
                    logicalView,
                    StringComparison.OrdinalIgnoreCase) ||
                pair.Value?.Params is null)
            {
                continue;
            }

            parameters = pair.Value.Params;
            return true;
        }

        return false;
    }

    private static bool TryGetParameter(
        IReadOnlyDictionary<string, double> parameters,
        string key,
        out double value)
    {
        value = 0.0;

        foreach (var pair in parameters)
        {
            if (!string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                continue;

            value = pair.Value;
            return true;
        }

        return false;
    }

    private static bool ValidatePositions(
        double lower,
        double upper,
        out string error)
    {
        if (!double.IsFinite(lower) || !double.IsFinite(upper))
        {
            error = "Breakline positions are not finite.";
            return false;
        }

        if (lower >= upper)
        {
            error = $"Invalid breakline ordering: lower={lower:F6}, upper={upper:F6}.";
            return false;
        }

        if (Math.Abs(upper - lower) <= PositionEpsilon)
        {
            error = "Breakline positions are too close together.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool IsPrimaryBreaklineView(string logicalView)
        => string.Equals(logicalView, DrawingViewNames.Front, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(logicalView, DrawingViewNames.Side, StringComparison.OrdinalIgnoreCase);

    private static bool IsSecondary(string logicalView)
        => string.Equals(logicalView, DrawingViewNames.Detail, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(logicalView, DrawingViewNames.Section, StringComparison.OrdinalIgnoreCase);

    private static double MmToMeters(double millimeters)
        => millimeters / 1000.0;

    private static class Defaults
    {
        public const double FrontSideBreaklineGapMm = 2.0;
        public const double DetailSectionBreaklineGapMm = 50.0;
        public const double DetailInsetMm = 40.0;
        public const double FrontFallbackPct = 0.40;
        public const double FrontOffsetPct = 0.020;
        public const double FrontUpperPct = 0.050;
    }
}
