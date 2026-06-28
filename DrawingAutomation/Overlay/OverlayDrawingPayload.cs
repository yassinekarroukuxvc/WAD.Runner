
using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Overlay;


public sealed record OverlayDrawingPayload(
    string ArticleNumber,
    WedgeSubclass Subclass,
    DrawingType DrawingType,
    string DrawingDescription,
    string? CoiningText,
    IReadOnlyList<OverlayDimensionRow> Dimensions);


public sealed record OverlayDimensionRow(
    string Key,
    Quantity Nominal,
    Quantity TolLower,
    Quantity TolUpper,
    bool IsZeroTolerance,
    bool IsAngle,
    string? Comment);


public interface IOverlayDrawingDataBuilder
{


    OverlayDrawingPayload Build(WedgeData wedge, DrawingData drawing);
}

public sealed class OverlayDrawingDataBuilder : IOverlayDrawingDataBuilder
{


    public OverlayDrawingPayload Build(WedgeData wedge, DrawingData drawing)
        => BuildInternal(wedge, drawing, keysFilter: null);


    public OverlayDrawingPayload Build(
        WedgeData wedge,
        DrawingData drawing,
        IEnumerable<string> keysFilter)
        => BuildInternal(wedge, drawing, keysFilter ?? throw new ArgumentNullException(nameof(keysFilter)));


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


    private static string ResolveDrawingDescription(WedgeData wedge, DrawingData drawing)
    {

        if (TryGetProperty(wedge, "article_description", out var desc) &&
            !string.IsNullOrWhiteSpace(desc))
        {
            return desc;
        }


        if (drawing.Metadata is not null &&
            drawing.Metadata.TryGetValue("SheetTitle", out var sheetTitle) &&
            !string.IsNullOrWhiteSpace(sheetTitle))
        {
            return sheetTitle;
        }


        return wedge.ArticleNumber;
    }

    private static string? ResolveCoiningText(WedgeData wedge, DrawingData drawing)
    {


        if (TryGetProperty(wedge, "Wed-Coining", out var coining) &&
            !string.IsNullOrWhiteSpace(coining))
        {
            return coining;
        }


        if (TryGetProperty(wedge, "Wed-Notes", out var notes) &&
            !string.IsNullOrWhiteSpace(notes))
        {
            return notes;
        }


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
            var dimKey = kvp.Key;
            var dim = kvp.Value;

            if (dim is null)
                continue;


            var key = dimKey.ToString();


            if (allowedKeys != null && !allowedKeys.Contains(key))
                continue;

            var nominal = dim.Nominal;


            if (IsZeroNominal(nominal))
                continue;

            var tol = dim.Tol;


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


    private static bool IsZeroNominal(Quantity q)
    {
        if (q.IsMm)
            return q.AsMm() == 0m;
        if (q.IsDeg)
            return q.AsDeg() == 0m;
        return q.Value == 0m;
    }


    private static Quantity ZeroLike(Quantity source)
        => new(0m, source.Unit);

    private static bool TryGetProperty(WedgeData wedge, string key, out string? value)
    {
        value = null;

        if (wedge.Properties is null)
            return false;


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
