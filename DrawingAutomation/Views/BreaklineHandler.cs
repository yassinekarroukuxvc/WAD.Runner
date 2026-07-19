using System;

using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Views;

/// <summary>
/// Handles breakline geometry for one SolidWorks drawing view.
///
/// Responsibilities:
///
/// - Resolve and apply the breakline gap.
/// - Calculate breakline positions using the current view scale.
/// - Apply breakline positions.
/// - Use an effective TL of 63.5 mm for OSG7 breakline calculations.
///
/// This class does not rebuild the drawing and does not control
/// workflow ordering.
/// </summary>
public sealed class BreaklineHandler
{
    private const double PositionEpsilon = 1e-9;
    private const double MinimumValidScale = 1e-6;

    /*
     * OSG7-specific effective TL.
     *
     * This value is used ONLY for breakline calculations.
     * It does not modify the database, WedgeData, equation file,
     * or SolidWorks model TL.
     */
    private const decimal Osg7BreaklineTlMm = 63.5m;

    private readonly View _view;
    private readonly ModelDoc2 _model;

    public BreaklineHandler(
        View swView,
        ModelDoc2 model)
    {
        _view =
            swView
            ?? throw new ArgumentNullException(
                nameof(swView));

        _model =
            model
            ?? throw new ArgumentNullException(
                nameof(model));
    }

    public bool SetBreaklineGap(
        double gapSheetMeters)
    {
        if (!IsReady())
            return false;

        if (!double.IsFinite(gapSheetMeters) ||
            gapSheetMeters <= 0.0)
        {
            Logger.Warn(
                $"Invalid breakline gap: {gapSheetMeters}. " +
                "The gap must be a finite positive value.");

            return false;
        }

        try
        {
            _view.BreakLineGap =
                gapSheetMeters;

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(
                $"SetBreaklineGap failed: {ex.Message}");

            return false;
        }
    }

    /// <summary>
    /// Applies the configured breakline geometry for a logical view.
    /// </summary>
    public bool ApplyBreakline(
        string viewName,
        WedgeType wedgeType,
        WedgeData wedge,
        DrawingData drawData)
    {
        if (!IsReady())
            return false;

        if (string.IsNullOrWhiteSpace(viewName))
        {
            Logger.Warn(
                "ApplyBreakline received an empty view name.");

            return false;
        }

        if (wedge is null)
        {
            Logger.Warn(
                $"Wedge data is null for view '{viewName}'.");

            return false;
        }

        if (drawData is null)
        {
            Logger.Warn(
                $"Drawing data is null for view '{viewName}'.");

            return false;
        }

        TryApplyConfiguredGap(
            drawData,
            viewName);

        if (IsDetail(viewName) ||
            IsSection(viewName))
        {
            return SetDetailOrSectionBreakline(
                wedgeType,
                wedge,
                drawData,
                viewName);
        }

        if (IsFront(viewName) ||
            IsSide(viewName))
        {
            return SetFrontOrSideBreakline(
                wedgeType,
                wedge,
                drawData,
                viewName);
        }

        Logger.Warn(
            $"Unrecognized breakline view '{viewName}'.");

        return false;
    }

    private bool SetDetailOrSectionBreakline(
        WedgeType wedgeType,
        WedgeData wedge,
        DrawingData drawData,
        string viewName)
    {
        var breakline =
            GetValidatedBreakline(
                viewName);

        if (breakline is null)
            return false;

        var tlMm =
            ResolveBreaklineTlMm(
                wedgeType,
                wedge);

        if (tlMm <= 0m)
        {
            Logger.Warn(
                $"TL is missing or invalid for " +
                $"'{viewName}' breakline.");

            return false;
        }

        var tlMeters =
            MmToMeters(
                tlMm);

        var scale =
            SafeScale();

        /*
         * detail_inset_mm is sheet-space.
         *
         * View-level configuration wins.
         * Global configuration is the fallback.
         */
        var insetMm =
            ResolveViewParam(
                drawData,
                viewName,
                "detail_inset_mm",
                ResolveGlobalParam(
                    drawData,
                    "detail_inset_mm",
                    Defaults.DetailInsetMm));

        var insetSheetMeters =
            MmToMeters(
                insetMm);

        /*
         * TL is model-space.
         * Convert it to sheet-space using the current view scale.
         */
        var halfSpanSheetMeters =
            (tlMeters * 0.5) *
            scale;

        var lower =
            -halfSpanSheetMeters +
            insetSheetMeters;

        var upper =
            halfSpanSheetMeters;

        if (!ValidatePositions(
                lower,
                upper,
                viewName))
        {
            return false;
        }

        var ok =
            TrySetBreakline(
                breakline,
                lower,
                upper,
                viewName);

        if (ok)
        {
            Logger.Success(
                $"[{viewName}] breakline positioned -> " +
                $"lower={lower:F6}, upper={upper:F6}, " +
                $"scale={scale:F6}.");
        }

        return ok;
    }

    private bool SetFrontOrSideBreakline(
        WedgeType wedgeType,
        WedgeData wedge,
        DrawingData drawData,
        string viewName)
    {
        var breakline =
            GetValidatedBreakline(
                viewName);

        if (breakline is null)
            return false;

        var tlMm =
            ResolveBreaklineTlMm(
                wedgeType,
                wedge);

        if (tlMm <= 0m)
        {
            Logger.Warn(
                $"TL is missing or invalid for " +
                $"'{viewName}' breakline.");

            return false;
        }

        var tlMeters =
            MmToMeters(
                tlMm);

        var scale =
            SafeScale();

        /*
         * Generic parameter names are preferred.
         *
         * Legacy FG/PGB parameter names remain supported as aliases.
         */
        var fallbackPct =
            ResolveGlobalParamAliases(
                drawData,
                Defaults.FrontFallbackPct,
                "front_fallback_pct",
                "fg_fallback_pct",
                "pgb_fallback_pct");

        var offsetPct =
            ResolveGlobalParamAliases(
                drawData,
                Defaults.FrontOffsetPct,
                "front_offset_pct",
                "fg_front_offset_pct",
                "pgb_front_offset_pct");

        var upperPct =
            ResolveGlobalParamAliases(
                drawData,
                Defaults.FrontUpperPct,
                "front_upper_pct",
                "fg_front_upper_pct",
                "pgb_front_upper_pct");

        double kMeters;

        if (!TryGetKMeters(
                wedge,
                tlMm,
                out kMeters))
        {
            kMeters =
                Math.Min(
                    (double)tlMm *
                    fallbackPct /
                    1000.0,
                    tlMeters * 0.5);
        }

        var lower =
            (
                tlMeters * 0.5
                - kMeters
                + tlMeters * offsetPct
            ) * scale;

        var upper =
            (
                tlMeters * 0.5
                - tlMeters * upperPct
            ) * scale;

        if (!ValidatePositions(
                lower,
                upper,
                viewName))
        {
            return false;
        }

        var ok =
            TrySetBreakline(
                breakline,
                lower,
                upper,
                viewName);

        if (ok)
        {
            Logger.Success(
                $"[{viewName}] breakline -> " +
                $"lower={lower:F6}, upper={upper:F6}, " +
                $"scale={scale:F6}.");
        }

        return ok;
    }

    /// <summary>
    /// Resolves the TL used specifically for breakline calculations.
    ///
    /// OSG7:
    ///     TL = 63.5 mm
    ///
    /// All other wedge types:
    ///     Actual TL from WedgeData.
    /// </summary>
    private static decimal ResolveBreaklineTlMm(
        WedgeType wedgeType,
        WedgeData wedge)
    {
        if (wedgeType == WedgeType.OSG7)
            return Osg7BreaklineTlMm;

        return GetLengthMm(
            wedge,
            "TL");
    }

    private bool IsReady()
    {
        return
            _view is not null
            && _model is not null;
    }

    private static bool IsFront(
        string value)
    {
        return string.Equals(
            value,
            "Front",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSide(
        string value)
    {
        return string.Equals(
            value,
            "Side",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDetail(
        string value)
    {
        return string.Equals(
            value,
            "Detail",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSection(
        string value)
    {
        return string.Equals(
            value,
            "Section",
            StringComparison.OrdinalIgnoreCase);
    }

    private void TryApplyConfiguredGap(
        DrawingData drawData,
        string viewName)
    {
        var gapMm =
            ResolveBreaklineGapMm(
                drawData,
                viewName);

        if (!gapMm.HasValue)
            return;

        var gapMeters =
            MmToMeters(
                gapMm.Value);

        if (!SetBreaklineGap(
                gapMeters))
        {
            Logger.Warn(
                $"[{viewName}] Failed to apply breakline gap " +
                $"({gapMm.Value:F3} mm).");
        }
    }

    private static double? ResolveBreaklineGapMm(
        DrawingData drawData,
        string viewName)
    {
        if (TryGetViewParams(
                drawData,
                viewName,
                out var parameters)
            &&
            TryGetParam(
                parameters,
                "breakline_gap_mm",
                out var configuredGapMm))
        {
            if (double.IsFinite(configuredGapMm) &&
                configuredGapMm > 0.0)
            {
                return configuredGapMm;
            }

            Logger.Warn(
                $"Invalid breakline_gap_mm for '{viewName}': " +
                $"{configuredGapMm}. Using default.");
        }

        return GetDefaultBreaklineGapMm(
            viewName);
    }

    private static double? GetDefaultBreaklineGapMm(
        string viewName)
    {
        if (IsFront(viewName) ||
            IsSide(viewName))
        {
            return Defaults.FrontSideBreaklineGapMm;
        }

        if (IsDetail(viewName) ||
            IsSection(viewName))
        {
            return Defaults.DetailSectionBreaklineGapMm;
        }

        return null;
    }

    private BreakLine? GetValidatedBreakline(
        string context)
    {
        try
        {
            var reportedCount =
                _view.GetBreakLineCount2(
                    out _);

            if (reportedCount <= 0)
            {
                Logger.Warn(
                    $"[{context}] View has no breakline.");

                return null;
            }

            var rawBreaklines =
                _view.GetBreakLines();

            if (rawBreaklines is null)
            {
                Logger.Warn(
                    $"[{context}] GetBreakLines returned null.");

                return null;
            }

            if (rawBreaklines is not object[] breaklines)
            {
                Logger.Error(
                    $"[{context}] GetBreakLines returned an " +
                    "unexpected object type.");

                return null;
            }

            if (breaklines.Length != 1)
            {
                Logger.Error(
                    $"[{context}] Expected exactly one breakline, " +
                    $"but found {breaklines.Length}.");

                return null;
            }

            if (breaklines[0] is not BreakLine breakline)
            {
                Logger.Error(
                    $"[{context}] Could not resolve BreakLine object.");

                return null;
            }

            return breakline;
        }
        catch (Exception ex)
        {
            Logger.Error(
                $"[{context}] Failed to get breakline: " +
                ex.Message);

            return null;
        }
    }

    private static decimal GetLengthMm(
        WedgeData wedge,
        string key)
    {
        if (wedge is null ||
            string.IsNullOrWhiteSpace(key))
        {
            return 0m;
        }

        try
        {
            var dimension =
                wedge.TryGet(
                    DimensionKey.From(
                        key));

            if (dimension is null)
            {
                Logger.Warn(
                    $"Dimension '{key}' was not found.");

                return 0m;
            }

            if (dimension.Nominal.Unit !=
                UnitKind.Millimeter)
            {
                Logger.Warn(
                    $"Dimension '{key}' must be in millimeters.");

                return 0m;
            }

            var value =
                dimension.Nominal.Value;

            if (value <= 0m)
            {
                Logger.Warn(
                    $"Dimension '{key}' has invalid value " +
                    $"{value} mm.");

                return 0m;
            }

            return value;
        }
        catch (Exception ex)
        {
            Logger.Error(
                $"Failed to read dimension '{key}': " +
                ex.Message);

            return 0m;
        }
    }

    private static double MmToMeters(
        decimal millimeters)
    {
        return
            (double)(millimeters / 1000m);
    }

    private static double MmToMeters(
        double millimeters)
    {
        return
            millimeters / 1000.0;
    }

    private double SafeScale()
    {
        try
        {
            var scale =
                _view.ScaleDecimal;

            if (!double.IsFinite(scale) ||
                scale <= MinimumValidScale)
            {
                Logger.Warn(
                    $"Invalid drawing view scale '{scale}'. " +
                    "Using fallback scale 1.0.");

                return 1.0;
            }

            return scale;
        }
        catch (Exception ex)
        {
            Logger.Warn(
                $"Could not read drawing view scale: " +
                $"{ex.Message}. Using fallback scale 1.0.");

            return 1.0;
        }
    }

    private static bool TrySetBreakline(
        BreakLine breakline,
        double lower,
        double upper,
        string context)
    {
        if (breakline is null)
            return false;

        if (!ValidatePositions(
                lower,
                upper,
                context))
        {
            return false;
        }

        try
        {
            var result =
                breakline.SetPosition(
                    lower,
                    upper);

            if (!result)
            {
                Logger.Error(
                    $"[{context}] SolidWorks rejected " +
                    "the breakline positions.");

                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(
                $"[{context}] SetPosition failed: " +
                ex.Message);

            return false;
        }
    }

    private static bool ValidatePositions(
        double lower,
        double upper,
        string context)
    {
        if (!double.IsFinite(lower) ||
            !double.IsFinite(upper))
        {
            Logger.Error(
                $"[{context}] Breakline positions are not finite.");

            return false;
        }

        if (Math.Abs(
                upper - lower) <=
            PositionEpsilon)
        {
            Logger.Error(
                $"[{context}] Breakline positions are too close.");

            return false;
        }

        if (lower >= upper)
        {
            Logger.Error(
                $"[{context}] Invalid breakline ordering: " +
                $"lower={lower:F6}, upper={upper:F6}.");

            return false;
        }

        return true;
    }

    private static bool TryGetKMeters(
        WedgeData wedge,
        decimal tlMm,
        out double kMeters)
    {
        kMeters = 0.0;

        if (wedge is null ||
            tlMm <= 0m)
        {
            return false;
        }

        try
        {
            var kValue =
                wedge.KValue;

            if (kValue is null)
                return false;

            var kMm =
                kValue.ValueMm.Value;

            if (kMm <= 0m)
                return false;

            var halfTlMm =
                tlMm * 0.5m;

            var clampedMm =
                Math.Min(
                    kMm,
                    halfTlMm);

            kMeters =
                MmToMeters(
                    clampedMm);

            return
                double.IsFinite(kMeters)
                && kMeters > 0.0;
        }
        catch (Exception ex)
        {
            Logger.Warn(
                $"Unable to resolve K-Value: " +
                ex.Message);

            return false;
        }
    }

    private static bool TryGetParam(
        IReadOnlyDictionary<string, double> source,
        string key,
        out double value)
    {
        value = 0.0;

        if (source is null ||
            string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        foreach (var pair in source)
        {
            if (!string.Equals(
                    pair.Key,
                    key,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value =
                pair.Value;

            return true;
        }

        return false;
    }

    private static bool TryGetViewParams(
        DrawingData drawData,
        string viewName,
        out IReadOnlyDictionary<string, double> parameters)
    {
        parameters = null!;

        if (drawData?.Views is null ||
            string.IsNullOrWhiteSpace(viewName))
        {
            return false;
        }

        foreach (var pair in drawData.Views)
        {
            if (!string.Equals(
                    pair.Key,
                    viewName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (pair.Value?.Params is null)
                return false;

            parameters =
                pair.Value.Params;

            return true;
        }

        return false;
    }

    private static double ResolveViewParam(
        DrawingData drawData,
        string viewName,
        string key,
        double fallback)
    {
        if (!TryGetViewParams(
                drawData,
                viewName,
                out var parameters))
        {
            return fallback;
        }

        if (!TryGetParam(
                parameters,
                key,
                out var value))
        {
            return fallback;
        }

        if (!double.IsFinite(value) ||
            value <= 0.0)
        {
            return fallback;
        }

        return value;
    }

    private static double ResolveGlobalParam(
        DrawingData drawData,
        string key,
        double fallback)
    {
        if (drawData?.Views is null)
            return fallback;

        foreach (var viewConfig in
                 drawData.Views.Values)
        {
            if (viewConfig?.Params is null)
                continue;

            if (!TryGetParam(
                    viewConfig.Params,
                    key,
                    out var value))
            {
                continue;
            }

            if (!double.IsFinite(value) ||
                value < 0.0)
            {
                continue;
            }

            return value;
        }

        return fallback;
    }

    private static double ResolveGlobalParamAliases(
        DrawingData drawData,
        double fallback,
        params string[] keys)
    {
        if (keys is null ||
            keys.Length == 0)
        {
            return fallback;
        }

        foreach (var key in keys)
        {
            var resolved =
                ResolveGlobalParam(
                    drawData,
                    key,
                    double.NaN);

            if (double.IsFinite(resolved))
                return resolved;
        }

        return fallback;
    }

    private static class Defaults
    {
        public const double FrontSideBreaklineGapMm =
            2.0;

        public const double DetailSectionBreaklineGapMm =
            50.0;

        public const double DetailInsetMm =
            40.0;

        public const double FrontFallbackPct =
            0.40;

        public const double FrontOffsetPct =
            0.020;

        public const double FrontUpperPct =
            0.050;
    }
}

