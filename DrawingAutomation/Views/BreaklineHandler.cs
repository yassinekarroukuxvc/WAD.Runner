using System;
using System.Collections.Generic;

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
/// Important architectural rules:
///
/// - This class does not rebuild.
/// - This class does not choose workflow order.
/// - This class does not branch by wedge subclass.
/// - Calculations use the CURRENT final/candidate view scale.
/// - Breakline gap ownership stays here.
/// </summary>
public sealed class BreaklineHandler
{
    private const double PositionEpsilon = 1e-9;
    private const double MinimumValidScale = 1e-6;

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

            Logger.Success(
                $"Breakline gap set to " +
                $"{gapSheetMeters:F6} m (sheet).");

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
    /// New wedge-agnostic entry point.
    /// </summary>
    public bool ApplyBreakline(
        string viewName,
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
                $"ApplyBreakline: wedge data is null " +
                $"for view '{viewName}'.");

            return false;
        }

        if (drawData is null)
        {
            Logger.Warn(
                $"ApplyBreakline: drawing data is null " +
                $"for view '{viewName}'.");

            return false;
        }

        /*
         * Breakline gap belongs to this handler.
         *
         * Resolution:
         * 1. view breakline_gap_mm
         * 2. built-in logical-view default
         */
        TryApplyConfiguredGap(
            drawData,
            viewName);

        if (IsDetail(viewName) ||
            IsSection(viewName))
        {
            return SetDetailOrSectionBreakline(
                wedge,
                drawData,
                viewName);
        }

        if (IsFront(viewName) ||
            IsSide(viewName))
        {
            return SetFrontOrSideBreakline(
                wedge,
                drawData,
                viewName);
        }

        Logger.Warn(
            $"ApplyBreakline: unrecognized view key " +
            $"'{viewName}'. " +
            "Expected Front, Side, Detail, or Section.");

        return false;
    }

    /// <summary>
    /// Compatibility overload for existing callers.
    ///
    /// Drawing type and wedge subclass no longer control breakline logic.
    /// </summary>
    [Obsolete(
        "Use ApplyBreakline(string viewName, WedgeData wedge, DrawingData drawData).")]
    public bool ApplyBreakline(
        string viewName,
        DrawingType drawingType,
        WedgeSubclass subclass,
        WedgeData wedge,
        DrawingData drawData)
    {
        return ApplyBreakline(
            viewName,
            wedge,
            drawData);
    }

    private bool SetDetailOrSectionBreakline(
        WedgeData wedge,
        DrawingData drawData,
        string viewName)
    {
        var breakline =
            GetValidatedBreakline();

        if (breakline is null)
            return false;

        var tlMm =
            GetLengthMm(
                wedge,
                "TL");

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
         * Generic global configuration is the fallback.
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
         * Convert to sheet-space with the CURRENT view scale.
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
                $"scale={scale:F6} (sheet m).");
        }

        return ok;
    }

    private bool SetFrontOrSideBreakline(
        WedgeData wedge,
        DrawingData drawData,
        string viewName)
    {
        var breakline =
            GetValidatedBreakline();

        if (breakline is null)
            return false;

        var tlMm =
            GetLengthMm(
                wedge,
                "TL");

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
         * Generic names are preferred.
         *
         * Legacy names are accepted so existing drawing configuration does
         * not have to be migrated immediately.
         *
         * No wedge subclass is consulted.
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

        if (TryGetKMeters(
                wedge,
                tlMm,
                out kMeters))
        {
            Logger.Blue(
                $"[{viewName}] Engraving start " +
                $"(K-Value) = {kMeters:F6} m.");
        }
        else
        {
            kMeters =
                Math.Min(
                    (double)tlMm *
                    fallbackPct /
                    1000.0,
                    tlMeters * 0.5);

            Logger.Info(
                $"[{viewName}] K-Value unavailable; " +
                $"fallback TL * {fallbackPct:P0} = " +
                $"{kMeters:F6} m.");
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
                $"{viewName} Front/Side"))
        {
            Logger.Error(
                $"[{viewName}] Breakline calculation details -> " +
                $"TL={tlMeters:F6} m, " +
                $"K={kMeters:F6} m, " +
                $"scale={scale:F6}, " +
                $"fallbackPct={fallbackPct:F6}, " +
                $"offsetPct={offsetPct:F6}, " +
                $"upperPct={upperPct:F6}.");

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
                $"scale={scale:F6} (sheet m).");
        }

        return ok;
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
        {
            Logger.Warn(
                $"No breakline gap could be resolved " +
                $"for view '{viewName}'.");

            return;
        }

        var gapMeters =
            MmToMeters(
                gapMm.Value);

        if (!SetBreaklineGap(
                gapMeters))
        {
            Logger.Warn(
                $"Failed to apply breakline gap for " +
                $"'{viewName}' ({gapMm.Value:F3} mm).");
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
                $"Configured breakline_gap_mm for " +
                $"'{viewName}' is invalid: {configuredGapMm}. " +
                "Using the default gap.");
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

    private BreakLine? GetValidatedBreakline()
    {
        try
        {
            var reportedCount =
                _view.GetBreakLineCount2(
                    out _);

            if (reportedCount <= 0)
            {
                Logger.Warn(
                    "View has no breaklines to position.");

                return null;
            }

            var rawBreaklines =
                _view.GetBreakLines();

            if (rawBreaklines is null)
            {
                Logger.Warn(
                    $"View reported {reportedCount} breakline(s), " +
                    "but GetBreakLines returned null.");

                return null;
            }

            if (rawBreaklines is not object[] breaklines)
            {
                Logger.Error(
                    "GetBreakLines returned unexpected type: " +
                    rawBreaklines.GetType().FullName);

                return null;
            }

            if (breaklines.Length != 1)
            {
                Logger.Error(
                    "Expected exactly one breakline in the view, " +
                    $"but found {breaklines.Length}. " +
                    "Breakline positioning was skipped.");

                return null;
            }

            if (breaklines[0] is not BreakLine breakline)
            {
                Logger.Error(
                    "The object returned by GetBreakLines could not " +
                    "be converted to BreakLine.");

                return null;
            }

            return breakline;
        }
        catch (Exception ex)
        {
            Logger.Error(
                $"GetValidatedBreakline failed: {ex.Message}");

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
                    $"Dimension '{key}' has unexpected unit " +
                    $"'{dimension.Nominal.Unit}'. " +
                    "Millimeters were expected.");

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
        {
            Logger.Error(
                $"[{context}] Cannot set breakline: " +
                "BreakLine object is null.");

            return false;
        }

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
                    $"breakline positions -> " +
                    $"lower={lower:F6}, upper={upper:F6}.");

                return false;
            }

            /*
             * No rebuild here.
             *
             * The coordinator batches mutations and owns the mandatory
             * regeneration boundary.
             */
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
                $"[{context}] Invalid breakline positions: " +
                $"lower={lower}, upper={upper}. " +
                "Both values must be finite.");

            return false;
        }

        if (Math.Abs(
                upper - lower) <=
            PositionEpsilon)
        {
            Logger.Error(
                $"[{context}] Invalid breakline positions: " +
                $"lower={lower:F9}, upper={upper:F9}. " +
                "The two positions are too close.");

            return false;
        }

        if (lower >= upper)
        {
            Logger.Error(
                $"[{context}] Invalid breakline ordering: " +
                $"lower={lower:F6} must be less than " +
                $"upper={upper:F6}.");

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
                kMm > halfTlMm
                    ? halfTlMm
                    : kMm;

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

            kMeters = 0.0;

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
            var sentinel =
                double.NaN;

            var resolved =
                ResolveGlobalParam(
                    drawData,
                    key,
                    sentinel);

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

        /*
         * Generic front/side defaults shared by all wedge types.
         *
         * Existing fg_* and pgb_* parameter names are still accepted as
         * aliases, but the algorithm itself is wedge-agnostic.
         */
        public const double FrontFallbackPct =
            0.40;

        public const double FrontOffsetPct =
            0.020;

        public const double FrontUpperPct =
            0.050;
    }
}
