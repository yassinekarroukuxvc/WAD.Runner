// DrawingAutomation/Rules/Common/BaseAnnotationCleanupRunner.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.SolidWorks;
using WAD.Runner.DrawingAutomation.Views;

using Ann = WAD.Runner.DrawingAutomation.Rules.Common.SharedAnnotationDeletionRules;
using DomDim = WAD.Runner.DataManagement.Domain.Dimensions.Dimension;
using DomDimKey = WAD.Runner.DataManagement.Domain.Dimensions.DimensionKey;
using DomUnitKind = WAD.Runner.DataManagement.Domain.Units.UnitKind;
namespace WAD.Runner.DrawingAutomation.Rules.Common;

/// <summary>
/// Base class for annotation cleanup runners (COB, UTUS, FP, and future types).
///
/// Previously each runner was a copy-paste of ~250 lines of identical code:
///   TryGetDim, IsDimPositive, NormalizeBaseKey, TryGetPropLoose,
///   ParseShankType, ResolveFootOption, NormalizeToken, ResolveActualViewName.
///
/// Now those live here once. Subclasses provide:
///   - LogPrefix       : e.g. "COB", "UTUS", "FP"
///   - AppliesTo       : the WedgeType this runner handles
///   - PlanDeletions() : calls into their specific (or shared) rules class
/// </summary>
public abstract class BaseAnnotationCleanupRunner : IDrawingCleanupRunner
{
    public abstract WedgeType AppliesTo { get; }
    protected abstract string LogPrefix { get; }

    /// <summary>
    /// Called by TryApply after context is resolved.
    /// Subclasses call into their rules class to get the deletion plan.
    /// </summary>
    protected abstract IReadOnlyList<SharedAnnotationDeletionRules.DeletionTarget> PlanDeletions(
        ModelDoc2 model,
        SharedAnnotationDeletionRules.DrawingType drawingType,
        SharedAnnotationDeletionRules.ShankType shank,
        SharedAnnotationDeletionRules.FootOption foot,
        SharedAnnotationDeletionRules.Options options,
        SharedAnnotationDeletionRules.ViewNameMap viewNames,
        bool activateEachView);

    /// <summary>
    /// Called when deletions == 0 for debugging. Override to call DumpExisting on your rules class.
    /// </summary>
    protected virtual void DumpExistingForDebug(
        ModelDoc2 model,
        SharedAnnotationDeletionRules.ViewNameMap viewNames,
        bool activateEachView)
    {
        SharedAnnotationDeletionRules.DumpExistingDimensionNames(
            LogPrefix, model, viewNames, activateEachView);
    }

    public void TryApply(
        DrawingService ds,
        IDictionary<string, string> nameMap,
        DrawingRun run,
        DrawingData drawingData,
        bool activateEachView = true)
    {
        try
        {
            if (run is null) throw new ArgumentNullException(nameof(run));
            if (drawingData is null) throw new ArgumentNullException(nameof(drawingData));

            if (run.WedgeType != AppliesTo)
            {
                Logger.Info($"[{LogPrefix}.Cleanup] Skipped (not {AppliesTo}).");
                return;
            }

            if (ds?.Model is not ModelDoc2 model)
            {
                Logger.Warn($"[{LogPrefix}.Cleanup] ds.Model is null or not ModelDoc2 (skipping).");
                return;
            }

            var viewNames = BuildViewNameMap(nameMap);
            var (shank, foot) = ResolveShankAndFoot(run.Wedge);
            var drawingType = ResolveDrawingType(run, drawingData);
            var options = BuildOptions(run.Wedge);

            Logger.Blue($"[{LogPrefix}.Cleanup] DrawingType={drawingType}, Shank={shank}, Foot={foot}");

            var deletions = PlanDeletions(model, drawingType, shank, foot, options, viewNames, activateEachView);

            if (deletions == null || deletions.Count == 0)
            {
                Logger.Warn($"[{LogPrefix}.Plan] Planned deletions = 0 — dumping existing dims for debugging…");
                DumpExistingForDebug(model, viewNames, activateEachView);
                return;
            }

            SharedAnnotationDeletionRules.DumpDeletionPlan(LogPrefix, $"{LogPrefix} Cleanup Runner", deletions);

            int totalDeleted = 0;

            foreach (var g in deletions.GroupBy(d => d.ViewName, StringComparer.OrdinalIgnoreCase))
            {
                var viewNameActual = g.Key;
                var fullNames = g.Select(x => x.AnnotationFullName).ToList();

                int deletedInView = AnnotationCleanupService.RemoveDimensionsByFullNamesInView(
                    ds, nameMap, logicalViewName: viewNameActual, fullNames: fullNames);

                totalDeleted += deletedInView;
                Logger.Info($"[{LogPrefix}.Plan] Deleted in view '{viewNameActual}': {deletedInView}/{fullNames.Count} planned.");
            }

            Logger.Info($"[{LogPrefix}.Plan] Total deleted (all views): {totalDeleted}");
        }
        catch (Exception ex)
        {
            Logger.Warn($"[{LogPrefix}.Cleanup] Failed (continuing): {ex.Message}");
        }
    }

    // ── Context resolution ────────────────────────────────────────────

    protected static SharedAnnotationDeletionRules.ViewNameMap BuildViewNameMap(IDictionary<string, string> nameMap)
        => new()
        {
            Front = Resolve(nameMap, "Front"),
            Side = Resolve(nameMap, "Side"),
            Top = Resolve(nameMap, "Top"),
            Detail = Resolve(nameMap, "Detail"),
            Section = Resolve(nameMap, "Section"),
        };

    private static string Resolve(IDictionary<string, string> nameMap, string logical)
    {
        if (nameMap != null && nameMap.TryGetValue(logical, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
            return mapped;
        return logical;
    }

    protected static SharedAnnotationDeletionRules.DrawingType ResolveDrawingType(DrawingRun run, DrawingData dd)
    {
        if (run?.Wedge?.Subclass == WedgeSubclass.PGB)
            return SharedAnnotationDeletionRules.DrawingType.Pgb;

        var dt = dd == null ? string.Empty : dd.DrawingType.ToString();

        return dt.Equals("Customer", StringComparison.OrdinalIgnoreCase)
            ? SharedAnnotationDeletionRules.DrawingType.Customer
            : SharedAnnotationDeletionRules.DrawingType.Production;
    }

    protected (SharedAnnotationDeletionRules.ShankType Shank, SharedAnnotationDeletionRules.FootOption Foot)
        ResolveShankAndFoot(WedgeData wedge)
    {
        var wedType = GetPropLoose(wedge, "Wed-Type");
        var footOpt = GetPropLoose(wedge, "Wed-Foot_Option");

        Logger.Blue($"[{LogPrefix}.Resolve] Wed-Type={wedType ?? "(null)"}, Wed-Foot_Option={footOpt ?? "(null)"}");

        return (ParseShankType(wedType), ResolveFootOption(wedge, footOpt));
    }

    // ── Options builder ───────────────────────────────────────────────

    protected SharedAnnotationDeletionRules.Options BuildOptions(WedgeData wedge)
    {
        bool Pos(string key) => IsDimPositive(wedge, key);

        var hasVwVr = Pos("VR") && Pos("VW");
        var hasSlb = Pos("VBL");
        var hasW2 = Pos("W2");
        var hasGa = Pos("GA");
        var hasCd = Pos("CD");
        var hasGd = Pos("GD");
        var hasGr = Pos("GR");
        var hasB = Pos("B");
        var hasRa2 = Pos("RA2");
        var hasErd = Pos("ERD");
        var hasFrBr = Pos("FR") && Pos("BR");
        var hasF = Pos("F");
        var hasG = Pos("G");
        var hasCgr = Pos("CGR");
        var hasCgd = Pos("CGD");
        var hasCbra = Pos("CBRA");
        var hasCbrl = Pos("CBRL");

        Logger.Blue(
            $"[{LogPrefix}.Options] VW/VR={hasVwVr}, VBL={hasSlb}, " +
            $"W2={hasW2}, GA={hasGa}, CD={hasCd}, GD={hasGd}, GR={hasGr}, B={hasB}, " +
            $"RA2={hasRa2}, ERD={hasErd}, FR/BR={hasFrBr}, " +
            $"F={hasF}, G={hasG}, CGR={hasCgr}, CGD={hasCgd}, CBRA={hasCbra}, CBRL={hasCbrl}");

        // Diagnostic dim dump
        foreach (var key in new[] {
            "VW","VR","VBL","W2","GA","CD","GD","GR","B",
            "RA2","ERD","FR","BR","F","G","CGR","CGD","CBRA","CBRL" })
            DumpDim(wedge, key);

        return new SharedAnnotationDeletionRules.Options
        {
            HasVwVr = hasVwVr,
            HasSlb = hasSlb,
            HasW2 = hasW2,
            HasGa = hasGa,
            HasCd = hasCd,
            HasGd = hasGd,
            HasGr = hasGr,
            HasB = hasB,
            HasRa2 = hasRa2,
            HasErd = hasErd,
            HasFrBr = hasFrBr,
            HasF = hasF,
            HasG = hasG,
            HasCgr = hasCgr,
            HasCgd = hasCgd,
            HasCbra = hasCbra,
            HasCbrl = hasCbrl,
        };
    }

    // ── Dim helpers ───────────────────────────────────────────────────

    protected static bool IsDimPositive(WedgeData wedge, string key)
    {
        if (!TryGetDim(wedge, key, out var dim) || dim is null) return false;
        try
        {
            double v = dim.Nominal.Unit == UnitKind.Degree
                ? (double)dim.Nominal.AsDeg()
                : (double)dim.Nominal.AsMm();
            return v > 1e-12;
        }
        catch { return false; }
    }

    private void DumpDim(WedgeData wedge, string key)
    {
        if (!TryGetDim(wedge, key, out var dim) || dim is null)
        {
            Logger.Info($"[{LogPrefix}.OptionsDbg] {key}: (missing)");
            return;
        }
        try
        {
            double v = dim.Nominal.Unit == UnitKind.Degree
                ? (double)dim.Nominal.AsDeg()
                : (double)dim.Nominal.AsMm();
            Logger.Info($"[{LogPrefix}.OptionsDbg] {key}: {v.ToString("0.#####", CultureInfo.InvariantCulture)} ({dim.Nominal.Unit})");
        }
        catch (Exception ex) { Logger.Info($"[{LogPrefix}.OptionsDbg] {key}: (unreadable) {ex.Message}"); }
    }

    private static bool TryGetDim(WedgeData wedge, string key, out DomDim? dim)
    {
        dim = null;
        if (wedge?.Dimensions == null || wedge.Dimensions.Count == 0 || string.IsNullOrWhiteSpace(key))
            return false;

        var want = NormalizeBaseKey(key);
        foreach (var kv in wedge.Dimensions)
        {
            var haveRaw = kv.Key.Value ?? kv.Key.ToString() ?? string.Empty;
            if (NormalizeBaseKey(haveRaw).Equals(want, StringComparison.OrdinalIgnoreCase))
            {
                dim = kv.Value;
                return dim is not null;
            }
        }
        return false;
    }

    private static string NormalizeBaseKey(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var s = raw.Trim().ToUpperInvariant();
        var at = s.IndexOf('@');
        if (at >= 0) s = s.Substring(0, at);
        s = s.Replace("-", "_").Replace(" ", "_").Replace("(", "_").Replace(")", "");
        return new string(s.Where(char.IsLetterOrDigit).ToArray());
    }

    // ── Property helpers ──────────────────────────────────────────────

    protected static string? GetPropLoose(WedgeData wedge, string key)
    {
        if (wedge == null || string.IsNullOrWhiteSpace(key)) return null;
        if (wedge.Properties == null) return null;

        if (wedge.Properties.TryGetValue(key, out var v))
            return string.IsNullOrWhiteSpace(v) ? null : v.Trim();

        foreach (var kv in wedge.Properties)
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(kv.Value) ? null : kv.Value.Trim();

        return null;
    }

    // ── Shank + foot resolution ───────────────────────────────────────

    protected static SharedAnnotationDeletionRules.ShankType ParseShankType(string? wedType)
    {
        var s = (wedType ?? string.Empty).Trim().ToUpperInvariant();
        return (s.Contains("180") || s.Contains("REV"))
            ? SharedAnnotationDeletionRules.ShankType.Deg180Rev
            : SharedAnnotationDeletionRules.ShankType.Std;
    }

    protected static SharedAnnotationDeletionRules.FootOption ResolveFootOption(WedgeData wedge, string? rawFoot)
    {
        var s = NormalizeToken(rawFoot)
             ?? NormalizeToken(GetPropLoose(wedge, "Wed-Foot_Option"))
             ?? NormalizeToken(GetPropLoose(wedge, "Foot_Option"))
             ?? NormalizeToken(GetPropLoose(wedge, "FootOption"))
             ?? NormalizeToken(GetPropLoose(wedge, "foot_option"));

        if (string.IsNullOrWhiteSpace(s))
            return SharedAnnotationDeletionRules.FootOption.None;

        var baseFoot = s switch
        {
            "SW_C" => SharedAnnotationDeletionRules.FootOption.C,
            "SW_G" => SharedAnnotationDeletionRules.FootOption.G,
            "SW_VG" => SharedAnnotationDeletionRules.FootOption.VG,
            "SW_CG" => SharedAnnotationDeletionRules.FootOption.CG,
            "SW_CC" => SharedAnnotationDeletionRules.FootOption.CC,
            _ => SharedAnnotationDeletionRules.FootOption.None
        };

        if (baseFoot == SharedAnnotationDeletionRules.FootOption.C)
        {
            bool allCbr = IsDimPositive(wedge, "CBRA") &&
                          IsDimPositive(wedge, "CBRL") &&
                          IsDimPositive(wedge, "CBRD");
            if (allCbr)
                return SharedAnnotationDeletionRules.FootOption.C_WITH_CBR;
        }

        return baseFoot;
    }

    private static string? NormalizeToken(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim().ToUpperInvariant().Replace("-", "_").Replace(" ", "_");
        t = new string(t.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        return string.IsNullOrWhiteSpace(t) ? null : t;
    }
}
