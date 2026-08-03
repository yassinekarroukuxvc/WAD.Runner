using System;
using System.Collections.Generic;
using System.Globalization;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using DomDim = WAD.Runner.DataManagement.Domain.Dimensions.Dimension;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

public static class DimensionFactResolver
{
    private const double PositiveEpsilon = 1e-12;

    /// <summary>
    /// Builds annotation-cleanup dimension facts from every dimension available
    /// in the current WedgeData.
    ///
    /// Presence and positivity are intentionally tracked separately:
    /// - Presence answers rules such as "show X only when supplied by the DB".
    /// - Positivity answers rules such as "show VR only when it has a value".
    /// </summary>
    public static DimensionFacts Resolve(
        WedgeData wedge,
        string logPrefix = "AnnotationCleanup")
    {
        var positiveFacts = new Dictionary<string, bool>(
            StringComparer.OrdinalIgnoreCase);

        var presentKeys = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        if (wedge?.Dimensions == null ||
            wedge.Dimensions.Count == 0)
        {
            Logger.Info(
                $"[{logPrefix}.OptionsDbg] " +
                "No wedge dimensions were available.");

            return DimensionFacts.FromPresenceAndBooleans(
                positiveFacts,
                presentKeys);
        }

        foreach (var kv in wedge.Dimensions)
        {
            var rawKey = kv.Key.Value ??
                         kv.Key.ToString() ??
                         string.Empty;

            var normalizedKey = DimensionFacts.Normalize(rawKey);

            if (string.IsNullOrWhiteSpace(normalizedKey))
                continue;

            if (kv.Value is not null)
                presentKeys.Add(normalizedKey);

            var isPositive = IsDimensionPositive(kv.Value);

            // If the same dimension key appears more than once through casing
            // or aliases, keep it positive if any occurrence is positive.
            if (positiveFacts.TryGetValue(
                    normalizedKey,
                    out var existing))
            {
                positiveFacts[normalizedKey] =
                    existing || isPositive;
            }
            else
            {
                positiveFacts[normalizedKey] = isPositive;
            }

            DumpDimension(
                rawKey,
                kv.Value,
                isPositive,
                logPrefix);
        }

        AddLegacyAliases(
            positiveFacts,
            presentKeys,
            logPrefix);

        return DimensionFacts.FromPresenceAndBooleans(
            positiveFacts,
            presentKeys);
    }

    public static bool IsDimensionPositive(
        WedgeData wedge,
        string key)
    {
        if (!TryGetDimension(wedge, key, out var dim) ||
            dim is null)
        {
            return false;
        }

        return IsDimensionPositive(dim);
    }

    public static bool TryGetDimension(
        WedgeData wedge,
        string key,
        out DomDim? dimension)
    {
        dimension = null;

        if (wedge?.Dimensions == null ||
            wedge.Dimensions.Count == 0 ||
            string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var wantedKey = DimensionFacts.Normalize(key);

        foreach (var kv in wedge.Dimensions)
        {
            var rawKey = kv.Key.Value ??
                         kv.Key.ToString() ??
                         string.Empty;

            var currentKey = DimensionFacts.Normalize(rawKey);

            if (!currentKey.Equals(
                    wantedKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            dimension = kv.Value;
            return dimension is not null;
        }

        return false;
    }

    private static bool IsDimensionPositive(DomDim? dim)
    {
        if (dim is null)
            return false;

        try
        {
            var value = ReadNominalValue(dim);
            return value > PositiveEpsilon;
        }
        catch
        {
            return false;
        }
    }

    private static double ReadNominalValue(DomDim dim)
    {
        return dim.Nominal.Unit == UnitKind.Degree
            ? (double)dim.Nominal.AsDeg()
            : (double)dim.Nominal.AsMm();
    }

    private static void AddLegacyAliases(
        IDictionary<string, bool> positiveFacts,
        ISet<string> presentKeys,
        string logPrefix)
    {
        // Legacy behavior:
        // SLB was treated as equivalent to VBL in the annotation cleanup rules.
        // Do not overwrite a real SLB dimension if it already exists.
        if (!positiveFacts.ContainsKey("SLB") &&
            positiveFacts.TryGetValue("VBL", out var hasVbl))
        {
            positiveFacts["SLB"] = hasVbl;

            if (presentKeys.Contains("VBL"))
                presentKeys.Add("SLB");

            Logger.Info(
                $"[{logPrefix}.OptionsDbg] " +
                $"SLB: alias from VBL positive={hasVbl}");
        }
    }

    private static void DumpDimension(
        string key,
        DomDim? dim,
        bool isPositive,
        string logPrefix)
    {
        if (dim is null)
        {
            Logger.Info(
                $"[{logPrefix}.OptionsDbg] {key}: (null)");

            return;
        }

        try
        {
            var value = ReadNominalValue(dim);

            Logger.Info(
                $"[{logPrefix}.OptionsDbg] {key}: " +
                $"{value.ToString("0.#####", CultureInfo.InvariantCulture)} " +
                $"({dim.Nominal.Unit}) " +
                $"present=True positive={isPositive}");
        }
        catch (Exception ex)
        {
            Logger.Info(
                $"[{logPrefix}.OptionsDbg] {key}: " +
                $"(unreadable) {ex.Message}");
        }
    }
}
