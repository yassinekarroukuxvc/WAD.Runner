// DrawingAutomation/Rules/COB/CobAnnotationCleanupRunner.cs
using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.SolidWorks;
using WAD.Runner.DrawingAutomation.Views; // AnnotationCleanupService

using WAD.Runner.DrawingAutomation.Rules.Common;

namespace WAD.Runner.DrawingAutomation.Rules.COB
{
    /// <summary>
    /// COB annotation cleanup runner (delete-by-fullname plan).
    ///
    /// - Uses CobAnnotationDeletionRules.PlanDeletionsFromDrawing(...)
    /// - Applies deletions via AnnotationCleanupService.RemoveDimensionsByFullNamesInView(...)
    ///
    /// Designed to be called near the end of the drawing pipeline, before export.
    /// </summary>
    public sealed class CobAnnotationCleanupRunner : IDrawingCleanupRunner
    {
        public WedgeType AppliesTo => WedgeType.COB;

        public void TryApply(
            DrawingService ds,
            IDictionary<string, string> nameMap,
            DrawingRun run,
            DrawingData drawingData,
            bool activateEachView = true)
        {
            try
            {
                if (run == null) throw new ArgumentNullException(nameof(run));
                if (drawingData == null) throw new ArgumentNullException(nameof(drawingData));

                if (run.WedgeType != WedgeType.COB)
                {
                    Logger.Info("[COB.Cleanup] Skipped (not COB).");
                    return;
                }

                if (ds?.Model is not ModelDoc2 model)
                {
                    Logger.Warn("[COB.Cleanup] ds.Model is null or not ModelDoc2 (skipping).");
                    return;
                }

                var viewNames = new CobAnnotationDeletionRules.ViewNameMap
                {
                    Front = ResolveActualViewName(nameMap, "Front"),
                    Side = ResolveActualViewName(nameMap, "Side"),
                    Top = ResolveActualViewName(nameMap, "Top"),
                    Detail = ResolveActualViewName(nameMap, "Detail"),
                    Section = ResolveActualViewName(nameMap, "Section"),
                };

                var (shank, foot) = ResolveCobShankAndFoot(run.Wedge);
                var rulesDrawingType = ResolveCobRulesDrawingType(run, drawingData);

                // ✅ Options inferred from WedgeData DIMENSIONS by VALUE (>0)
                var options = BuildCobOptionsFromWedgeDimensions(run.Wedge);

                var deletions = CobAnnotationDeletionRules.PlanDeletionsFromDrawing(
                    model,
                    rulesDrawingType,
                    shank,
                    foot,
                    options,
                    viewNames,
                    activateEachView: activateEachView);

                if (deletions == null || deletions.Count == 0)
                {
                    Logger.Warn("[COB.Plan] Planned deletions = 0. Dumping existing dims by view for debugging...");
                    CobAnnotationDeletionRules.DumpExistingDisplayDimensionFullNamesFromDrawing(model, viewNames, activateEachView: activateEachView);
                    return;
                }

                CobAnnotationDeletionRules.DumpDeletionPlan("COB Cleanup Runner", deletions, maxPerView: 200);

                int totalDeleted = 0;

                foreach (var g in deletions.GroupBy(d => d.ViewName, StringComparer.OrdinalIgnoreCase))
                {
                    var viewNameActual = g.Key;
                    var fullNames = g.Select(x => x.AnnotationFullName).ToList();

                    var deletedInView = AnnotationCleanupService.RemoveDimensionsByFullNamesInView(
                        ds,
                        nameMap,
                        logicalViewName: viewNameActual,
                        fullNames: fullNames);

                    totalDeleted += deletedInView;

                    Logger.Info($"[COB.Plan] Deleted in view '{viewNameActual}': {deletedInView}/{fullNames.Count} planned.");
                }

                Logger.Info($"[COB.Plan] Total deleted (all views): {totalDeleted}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[COB.Cleanup] Failed (continuing): {ex.Message}");
            }
        }

        // ============================================================
        // Options builder (VALUE-BASED: >0 checks)
        // ============================================================

        private static CobAnnotationDeletionRules.Options BuildCobOptionsFromWedgeDimensions(WedgeData wedge)
        {
            static object? FindDimObject(WedgeData w, string key)
            {
                if (w?.Dimensions == null || w.Dimensions.Count == 0) return null;

                foreach (var kv in w.Dimensions)
                {
                    var kStr = kv.Key.ToString();
                    if (string.Equals(kStr, key, StringComparison.OrdinalIgnoreCase))
                        return kv.Value;
                }

                return null;
            }

            static double TryGetNominalAsDouble(object? dimObj)
            {
                if (dimObj == null) return 0.0;

                static double ReadNumericProp(object o, string propName)
                {
                    var p = o.GetType().GetProperty(propName);
                    if (p == null) return double.NaN;

                    object? v;
                    try { v = p.GetValue(o); }
                    catch { return double.NaN; }

                    if (v == null) return double.NaN;

                    try
                    {
                        return v switch
                        {
                            double d => d,
                            float f => f,
                            decimal m => (double)m,
                            int i => i,
                            long l => l,
                            short s => s,
                            _ => Convert.ToDouble(v)
                        };
                    }
                    catch
                    {
                        return double.NaN;
                    }
                }

                var candidates = new[]
                {
                    "Mm", "mm",
                    "Deg", "deg",
                    "ValueMm", "ValueMM",
                    "NominalMm", "NominalMM",
                    "Value", "Nominal"
                };

                foreach (var name in candidates)
                {
                    var x = ReadNumericProp(dimObj, name);
                    if (!double.IsNaN(x))
                        return x;
                }

                try
                {
                    var s = dimObj.ToString() ?? string.Empty;
                    var buf = new string(s.Where(c => char.IsDigit(c) || c == '.' || c == '-' || c == ',').ToArray());
                    if (double.TryParse(buf.Replace(',', '.'),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var parsed))
                        return parsed;
                }
                catch { }

                return 0.0;
            }

            static bool IsPositive(WedgeData w, string key)
            {
                var dim = FindDimObject(w, key);
                var val = TryGetNominalAsDouble(dim);
                return val > 0.0;
            }

            // Rules requested:
            // - HasVwVr true if VR and VW > 0
            // - HasW2 true if W2 > 0
            // - HasF true if F > 0
            // - HasSlb true if VBL > 0
            // - HasFrBr true if FR and BR > 0
            // - HasErd true if ERD > 0
            // - HasK true if K > 0
            // - HasRa2 true if RA2 > 0

            var hasVwVr = IsPositive(wedge, "VR") && IsPositive(wedge, "VW");
            var hasW2 = IsPositive(wedge, "W2");
            var hasF = IsPositive(wedge, "F");
            var hasSlb = IsPositive(wedge, "VBL");
            var hasFrBr = IsPositive(wedge, "FR") && IsPositive(wedge, "BR");
            var hasErd = IsPositive(wedge, "ERD");
            var hasK = IsPositive(wedge, "K");
            var hasRa2 = IsPositive(wedge, "RA2");

            Logger.Info($"[COB.Options] VW/VR={hasVwVr}, W2={hasW2}, F={hasF}, VBL(SLB)={hasSlb}, FR/BR={hasFrBr}, ERD={hasErd}, K={hasK}, RA2={hasRa2}");

            return new CobAnnotationDeletionRules.Options
            {
                HasVwVr = hasVwVr,
                HasW2 = hasW2,
                HasSlb = hasSlb,
                HasRa2 = hasRa2,

                HasFrBr = hasFrBr,
                HasF = hasF,

                HasK = hasK,
                KAnnotationFullName = null,

                HasErd = hasErd,
                ErdAnnotationFullName = null
            };
        }

        private static CobAnnotationDeletionRules.DrawingType ResolveCobRulesDrawingType(DrawingRun run, DrawingData dd)
        {
            if (run?.Wedge?.Subclass == WedgeSubclass.PGB)
                return CobAnnotationDeletionRules.DrawingType.Pgb;

            var dt = dd == null ? string.Empty : dd.DrawingType.ToString();

            if (dt.Equals("Customer", StringComparison.OrdinalIgnoreCase))
                return CobAnnotationDeletionRules.DrawingType.Customer;

            return CobAnnotationDeletionRules.DrawingType.Production;
        }

        private static (CobAnnotationDeletionRules.ShankType Shank, CobAnnotationDeletionRules.FootOption Foot) ResolveCobShankAndFoot(WedgeData wedge)
        {
            var wedType = TryGetProp(wedge, "Wed-Type");
            var footOpt = TryGetProp(wedge, "Wed-Foot_Option");

            var shank = ParseCobShankType(wedType);
            var foot = ParseCobFootOption(footOpt);

            return (shank, foot);
        }

        private static string ResolveActualViewName(IDictionary<string, string> nameMap, string logical)
        {
            if (nameMap != null &&
                nameMap.TryGetValue(logical, out var mapped) &&
                !string.IsNullOrWhiteSpace(mapped))
                return mapped;

            return logical;
        }

        private static string? TryGetProp(WedgeData wedge, string key)
        {
            if (wedge?.Properties == null) return null;

            if (wedge.Properties.TryGetValue(key, out var v))
                return string.IsNullOrWhiteSpace(v) ? null : v.Trim();

            foreach (var kv in wedge.Properties)
            {
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                    return string.IsNullOrWhiteSpace(kv.Value) ? null : kv.Value.Trim();
            }

            return null;
        }

        private static CobAnnotationDeletionRules.ShankType ParseCobShankType(string? wedType)
        {
            var s = (wedType ?? string.Empty).Trim().ToUpperInvariant();
            if (s.Contains("180") || s.Contains("REV"))
                return CobAnnotationDeletionRules.ShankType.Deg180Rev;

            return CobAnnotationDeletionRules.ShankType.Std;
        }

        private static CobAnnotationDeletionRules.FootOption ParseCobFootOption(string? footOption)
        {
            var s = (footOption ?? string.Empty).Trim().ToUpperInvariant();
            s = s.Replace("-", "_").Replace(" ", "_");

            if (string.IsNullOrWhiteSpace(s))
                return CobAnnotationDeletionRules.FootOption.None;

            if (s == "C") return CobAnnotationDeletionRules.FootOption.C;
            if (s == "G") return CobAnnotationDeletionRules.FootOption.G;
            if (s == "VG") return CobAnnotationDeletionRules.FootOption.VG;
            if (s == "CG") return CobAnnotationDeletionRules.FootOption.CG;
            if (s == "CC") return CobAnnotationDeletionRules.FootOption.CC;

            if (s.Contains("CBR"))
                return CobAnnotationDeletionRules.FootOption.C_WITH_CBR;

            return CobAnnotationDeletionRules.FootOption.None;
        }
    }
}