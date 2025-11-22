// DrawingAutomation/Overlay/OverlayDrawingDataBuilder.cs
using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.DataManagement.Domain.Drawing;      // DrawingData, DrawingType
using WAD.Runner.DataManagement.Domain.Dimensions;   // DimensionKey, Dimension
using WAD.Runner.DataManagement.Domain.Units;        // Quantity
using WAD.Runner.DataManagement.Domain.Wedge;        // WedgeData, WedgeSubclass

namespace WAD.Runner.DrawingAutomation.Overlay;

/// <summary>
/// Lightweight DTO used by Overlay drawing executors.
/// Contains only the data that Overlay needs: description, coining text,
/// and the list of dimensions to feed into the dimension table.
/// </summary>
public sealed record OverlayDrawingPayload(
    string ArticleNumber,
    WedgeSubclass Subclass,
    DrawingType DrawingType,
    string DrawingDescription,
    string? CoiningText,
    IReadOnlyList<OverlayDimensionRow> Dimensions);

/// <summary>Single dimension row for the Overlay dimension table.
/// Uses domain types (Quantity) so formatting stays in one place
/// (table/note formatters).
/// </summary>
public sealed record OverlayDimensionRow(
    string Key,
    Quantity Nominal,
    Quantity TolLower,
    Quantity TolUpper,
    bool IsZeroTolerance,
    bool IsAngle,
    string? Comment);

/// <summary>
/// Builds OverlayDrawingPayload from WedgeData + DrawingData.
/// This is intentionally small and Overlay-specific so we can later
/// extend it per wedge type (CKVD, COB, PGB...) without touching executors.
/// </summary>
public interface IOverlayDrawingDataBuilder
{
    /// <summary>
    /// Build payload with all non-zero dimensions.
    /// </summary>
    OverlayDrawingPayload Build(WedgeData wedge, DrawingData drawing);
}

public sealed class OverlayDrawingDataBuilder : IOverlayDrawingDataBuilder
{
    /// <summary>
    /// Default implementation: include all non-zero dimensions.
    /// </summary>
    public OverlayDrawingPayload Build(WedgeData wedge, DrawingData drawing)
        => BuildInternal(wedge, drawing, keysFilter: null);

    /// <summary>
    /// Overload: build payload but include only the specified dimension keys
    /// (case-insensitive). Example: new[] { "FL", "FR", "ER" }.
    /// </summary>
    public OverlayDrawingPayload Build(
        WedgeData wedge,
        DrawingData drawing,
        IEnumerable<string> keysFilter)
        => BuildInternal(wedge, drawing, keysFilter ?? throw new ArgumentNullException(nameof(keysFilter)));

    // ---- CORE IMPLEMENTATION ------------------------------------------------

    private static OverlayDrawingPayload BuildInternal(
        WedgeData wedge,
        DrawingData drawing,
        IEnumerable<string>? keysFilter)
    {
        if (wedge is null) throw new ArgumentNullException(nameof(wedge));
        if (drawing is null) throw new ArgumentNullException(nameof(drawing));

        var description = ResolveDrawingDescription(wedge, drawing);
        var coiningText = ResolveCoiningText(wedge, drawing);

        HashSet<string>? allowedKeys = null;
        if (keysFilter != null)
            allowedKeys = new HashSet<string>(keysFilter, StringComparer.OrdinalIgnoreCase);

        var dims = BuildDimensionRows(wedge, allowedKeys).ToList();

        return new OverlayDrawingPayload(
            ArticleNumber: wedge.ArticleNumber,
            Subclass: wedge.Subclass,
            DrawingType: drawing.DrawingType,
            DrawingDescription: description,
            CoiningText: coiningText,
            Dimensions: dims);
    }

    // ---- PRIVATE HELPERS ----------------------------------------------------

    private static string ResolveDrawingDescription(WedgeData wedge, DrawingData drawing)
    {
        // 1) Prefer article_description from WedgeData.Properties (as in your JSON)
        if (TryGetProperty(wedge, "article_description", out var desc) &&
            !string.IsNullOrWhiteSpace(desc))
        {
            return desc;
        }

        // 2) Fallback to DrawingData.Metadata["SheetTitle"], e.g. "FG Overlay Drawing"
        if (drawing.Metadata is not null &&
            drawing.Metadata.TryGetValue("SheetTitle", out var sheetTitle) &&
            !string.IsNullOrWhiteSpace(sheetTitle))
        {
            return sheetTitle;
        }

        // 3) Last fallback: article number (never null)
        return wedge.ArticleNumber;
    }

    private static string? ResolveCoiningText(WedgeData wedge, DrawingData drawing)
    {
        // TODO: once Wed-Spec1 has a "coining" row mapped, adapt this key if needed.
        // For now, we assume it will eventually land in WedgeData.Properties["Wed-Coining"].

        if (TryGetProperty(wedge, "Wed-Coining", out var coining) &&
            !string.IsNullOrWhiteSpace(coining))
        {
            return coining;
        }

        // Temporary fallback: Wed-Notes (often used for machining/coining hints).
        if (TryGetProperty(wedge, "Wed-Notes", out var notes) &&
            !string.IsNullOrWhiteSpace(notes))
        {
            return notes;
        }

        // If we ever decide to store coining in DrawingData.Metadata, we can add
        // another fallback here, e.g. "CoiningText" or similar.

        return null;
    }

    private static IEnumerable<OverlayDimensionRow> BuildDimensionRows(
        WedgeData wedge,
        HashSet<string>? allowedKeys)
    {
        if (wedge.Dimensions is null)
            yield break;

        foreach (var kvp in wedge.Dimensions
                                 .OrderBy(d => d.Key.ToString(), StringComparer.Ordinal))
        {
            var dimKey = kvp.Key;   // DimensionKey
            var dim = kvp.Value;    // Dimension domain object

            if (dim is null)
                continue;

            // Convert DimensionKey → string for the table (B, BA, TL, etc.)
            var key = dimKey.ToString();

            // If a filter-list is provided, enforce it (case-insensitive).
            if (allowedKeys != null && !allowedKeys.Contains(key))
                continue;

            var nominal = dim.Nominal;

            // Skip zero-valued dimensions (both mm and deg)
            if (IsZeroNominal(nominal))
                continue;

            var tol = dim.Tol;

            // Nominal.IsDeg tells us if it's an angle (so you can center or style differently)
            var isAngle = nominal.IsDeg;

            var tolLower = tol?.Lower ?? ZeroLike(nominal);
            var tolUpper = tol?.Upper ?? ZeroLike(nominal);

            var isZeroTol = tol?.IsZero ?? (tolLower.Value == 0 && tolUpper.Value == 0);

            yield return new OverlayDimensionRow(
                Key: key,
                Nominal: nominal,
                TolLower: tolLower,
                TolUpper: tolUpper,
                IsZeroTolerance: isZeroTol,
                IsAngle: isAngle,
                Comment: dim.Comment);
        }
    }

    /// <summary>
    /// Returns true if nominal is exactly zero (mm or deg).
    /// </summary>
    private static bool IsZeroNominal(Quantity q)
    {
        if (q.IsMm)
            return q.AsMm() == 0m;
        if (q.IsDeg)
            return q.AsDeg() == 0m;
        return q.Value == 0m;
    }

    /// <summary>
    /// Builds a zero-valued quantity with the same unit as the source (mm or deg).
    /// </summary>
    private static Quantity ZeroLike(Quantity source)
        => new(0m, source.Unit);

    private static bool TryGetProperty(WedgeData wedge, string key, out string? value)
    {
        value = null;

        if (wedge.Properties is null)
            return false;

        // Case-insensitive lookup is safer because DB / mapping might vary.
        foreach (var kvp in wedge.Properties)
        {
            if (kvp.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                value = kvp.Value;
                return true;
            }
        }

        return false;
    }
}
