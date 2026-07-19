using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Tables;

public static class DimensionTableKeyFilter
{
    private readonly record struct FilterKey(
        WedgeType WedgeType,
        WedgeSubclass Subclass,
        DrawingType DrawingType);

    private static readonly IReadOnlyDictionary<FilterKey, string[]> Rules = BuildRules();

    public static HashSet<string>? GetAllowedKeys(
        WedgeType wedgeType,
        DrawingType drawingType,
        WedgeSubclass subclass)
    {
        var key = new FilterKey(wedgeType, subclass, drawingType);
        return Rules.TryGetValue(key, out var allowed)
            ? new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase)
            : null;
    }

    private static IReadOnlyDictionary<FilterKey, string[]> BuildRules()
    {
        var rules = new Dictionary<FilterKey, string[]>();

        RegisterCobLike(rules, WedgeType.COB);
        RegisterCobLike(rules, WedgeType.UTUS);
        RegisterCobLike(rules, WedgeType.FP);
        RegisterCkvd(rules);
        RegisterOsg7(rules);
        return rules;
    }

    private static void RegisterCobLike(
        IDictionary<FilterKey, string[]> rules,
        WedgeType wedgeType)
    {
        Add(
            rules,
            wedgeType,
            WedgeSubclass.FG,
            DrawingType.Production,
            "T", "F", "FD", "FL", "H", "VBL",
            "RC", "CD", "GR", "GD", "B", "BF", "G", "VR",
            "W", "VW", "FR", "BR", "ERW", "TL", "TD", "TDF",
            "VFL", "Y");

        Add(
            rules,
            wedgeType,
            WedgeSubclass.FG,
            DrawingType.Customer,
            "T", "H", "VBL", "B", "BF", "G", "VR",
            "W", "VW", "W2", "FR", "BR", "TD", "TDF", "TL");

        Add(
            rules,
            wedgeType,
            WedgeSubclass.FG,
            DrawingType.Overlay,
            "TL", "TD", "TDF", "W", "ISA", "K", "RA", "T", "BA");

        Add(
            rules,
            wedgeType,
            WedgeSubclass.PGB,
            DrawingType.Production,
            "W", "ISA", "T", "FD");

        Add(
            rules,
            wedgeType,
            WedgeSubclass.PGB,
            DrawingType.Overlay,
            "TL", "TD", "TDF", "W", "ISA", "K", "BA");
    }

    private static void RegisterCkvd(IDictionary<FilterKey, string[]> rules)
    {
        Add(
            rules,
            WedgeType.CKVD,
            WedgeSubclass.FG,
            DrawingType.Production,
            "FL", "F", "B", "GD", "GR", "VR", "W", "VW",
            "FR", "BR", "FX", "X", "E");

        Add(
            rules,
            WedgeType.CKVD,
            WedgeSubclass.FG,
            DrawingType.Customer,
            "FL", "F", "B", "GD", "GR", "VR", "W", "VW",
            "FR", "BR", "FX", "X", "E");

        Add(
            rules,
            WedgeType.CKVD,
            WedgeSubclass.FG,
            DrawingType.Overlay,
            "TL", "TD", "TDF", "W", "ISA", "K", "BA");

        Add(
            rules,
            WedgeType.CKVD,
            WedgeSubclass.PGB,
            DrawingType.Production,
            "W", "ISA", "T", "FL");

        Add(
            rules,
            WedgeType.CKVD,
            WedgeSubclass.PGB,
            DrawingType.Overlay,
            "TL", "TD", "TDF", "W", "ISA", "K", "BA");
    }

    private static void RegisterOsg7(IDictionary<FilterKey, string[]> rules)
    {
        Add(
            rules,
            WedgeType.OSG7,
            WedgeSubclass.FG,
            DrawingType.Production,
            "FL", "F", "B", "GD", "GR", "VR", "W", "VW",
            "FR", "BR", "FX", "X", "FRX","BRX","VFL","TD","TDF","TL");

        Add(
            rules,
            WedgeType.OSG7,
            WedgeSubclass.FG,
            DrawingType.Customer,
            "FL", "F", "B", "GD", "GR", "VR", "W", "VW",
            "FR", "BR", "FX", "X", "FRX", "BRX", "VFL", "TD", "TDF", "TL");

        Add(
            rules,
            WedgeType.OSG7,
            WedgeSubclass.FG,
            DrawingType.Overlay,
            "TL", "TD", "TDF", "W", "ISA", "K", "BA","FA","VFL", "TD", "TDF", "TL");

        Add(
            rules,
            WedgeType.OSG7,
            WedgeSubclass.PGB,
            DrawingType.Production,
            "W", "ISA", "T", "FL", "TD", "TDF", "TL");

        Add(
            rules,
            WedgeType.OSG7,
            WedgeSubclass.PGB,
            DrawingType.Overlay,
            "TL", "TD", "TDF", "W", "ISA", "K", "BA", "TD", "TDF", "TL");
    }

    private static void Add(
        IDictionary<FilterKey, string[]> rules,
        WedgeType wedgeType,
        WedgeSubclass subclass,
        DrawingType drawingType,
        params string[] keys)
    {
        var filterKey = new FilterKey(wedgeType, subclass, drawingType);
        if (rules.ContainsKey(filterKey))
            throw new InvalidOperationException($"Duplicate dimension-table filter: {filterKey}.");

        rules[filterKey] = keys;
    }
}
