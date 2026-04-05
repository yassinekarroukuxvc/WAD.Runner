using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.SolidWorks;
using WAD.Runner.DrawingAutomation.Views;

using WAD.Runner.DrawingAutomation.Rules.Common;
using WAD.Runner.DrawingAutomation.Rules.FP;

// ✅ Follow EquationUpdater pattern (typed access to wedge.Dimensions)
using DomDim = WAD.Runner.DataManagement.Domain.Dimensions.Dimension;
using DomUnitKind = WAD.Runner.DataManagement.Domain.Units.UnitKind;

namespace WAD.Runner.DrawingAutomation.Rules.FP
{
    /// <summary>
    /// FP annotation cleanup runner.
    ///
    /// FP uses FP annotation deletion rules.
    /// </summary>
    public sealed class FpAnnotationCleanupRunner : IDrawingCleanupRunner
    {
        public WedgeType AppliesTo => WedgeType.FP;

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

                if (run.WedgeType != WedgeType.FP)
                {
                    Logger.Info("[FP.Cleanup] Skipped (not FP).");
                    return;
                }

                if (ds?.Model is not ModelDoc2 model)
                {
                    Logger.Warn("[FP.Cleanup] ds.Model is null or not ModelDoc2 (skipping).");
                    return;
                }

                var viewNames = new FpAnnotationDeletionRules.ViewNameMap
                {
                    Front = ResolveActualViewName(nameMap, "Front"),
                    Side = ResolveActualViewName(nameMap, "Side"),
                    Top = ResolveActualViewName(nameMap, "Top"),
                    Detail = ResolveActualViewName(nameMap, "Detail"),
                    Section = ResolveActualViewName(nameMap, "Section"),
                };

                var (shank, foot) = ResolveFpShankAndFoot(run.Wedge);
                var rulesDrawingType = ResolveFpRulesDrawingType(run, drawingData);
                var options = BuildFpOptionsFromWedgeDimensions(run.Wedge);

                Logger.Blue($"[FP.Cleanup] Shank={shank}, Foot={foot}");

                var deletions = FpAnnotationDeletionRules.PlanDeletionsFromDrawing(
                    model,
                    rulesDrawingType,
                    shank,
                    foot,
                    options,
                    viewNames,
                    activateEachView: activateEachView);

                if (deletions == null || deletions.Count == 0)
                {
                    Logger.Warn("[FP.Plan] Planned deletions = 0. Dumping existing dims by view for debugging...");
                    FpAnnotationDeletionRules.DumpExistingDisplayDimensionFullNamesFromDrawing(
                        model,
                        viewNames,
                        activateEachView: activateEachView);
                    return;
                }

                FpAnnotationDeletionRules.DumpDeletionPlan("FP Cleanup Runner", deletions, maxPerView: 200);

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

                    Logger.Info($"[FP.Plan] Deleted in view '{viewNameActual}': {deletedInView}/{fullNames.Count} planned.");
                }

                Logger.Info($"[FP.Plan] Total deleted (all views): {totalDeleted}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[FP.Cleanup] Failed (continuing): {ex.Message}");
            }
        }

        // ============================================================
        // Options builder
        // ============================================================

        private static FpAnnotationDeletionRules.Options BuildFpOptionsFromWedgeDimensions(WedgeData wedge)
        {
            bool Pos(string key) => IsDimPositive(wedge, key);

            var hasVwVr = Pos("VR") && Pos("VW");
            var hasW2 = Pos("W2");
            var hasF = Pos("F");
            var hasSlb = Pos("VBL");
            var hasFrBr = Pos("FR") && Pos("BR");
            var hasErd = Pos("ERD");
            var hasK = Pos("K");
            var hasRa2 = Pos("RA2");

            Logger.Blue($"[FP.Options] VW/VR={hasVwVr}, W2={hasW2}, F={hasF}, VBL(SLB)={hasSlb}, FR/BR={hasFrBr}, ERD={hasErd}, K={hasK}, RA2={hasRa2}");

            DumpDim(wedge, "VW");
            DumpDim(wedge, "VR");
            DumpDim(wedge, "W2");
            DumpDim(wedge, "F");
            DumpDim(wedge, "VBL");
            DumpDim(wedge, "FR");
            DumpDim(wedge, "BR");
            DumpDim(wedge, "ERD");
            DumpDim(wedge, "K");
            DumpDim(wedge, "RA2");

            return new FpAnnotationDeletionRules.Options
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

        private static void DumpDim(WedgeData wedge, string key)
        {
            if (!TryGetDim(wedge, key, out var dim) || dim is null)
            {
                Logger.Info($"[FP.OptionsDbg] {key}: (missing)");
                return;
            }

            try
            {
                double v = dim.Nominal.Unit == DomUnitKind.Degree
                    ? (double)dim.Nominal.AsDeg()
                    : (double)dim.Nominal.AsMm();

                Logger.Info($"[FP.OptionsDbg] {key}: {v.ToString("0.#####", CultureInfo.InvariantCulture)} ({dim.Nominal.Unit})");
            }
            catch (Exception ex)
            {
                Logger.Info($"[FP.OptionsDbg] {key}: (unreadable) {ex.Message}");
            }
        }

        private static bool IsDimPositive(WedgeData wedge, string key)
        {
            const double eps = 1e-12;

            if (!TryGetDim(wedge, key, out var dim) || dim is null)
                return false;

            try
            {
                double v = dim.Nominal.Unit == DomUnitKind.Degree
                    ? (double)dim.Nominal.AsDeg()
                    : (double)dim.Nominal.AsMm();

                return v > eps;
            }
            catch
            {
                return false;
            }
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
                var have = NormalizeBaseKey(haveRaw);

                if (have.Equals(want, StringComparison.OrdinalIgnoreCase))
                {
                    dim = kv.Value;
                    return dim is not null;
                }
            }

            return false;
        }

        private static string NormalizeBaseKey(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var s = raw.Trim().ToUpperInvariant();

            var at = s.IndexOf('@');
            if (at >= 0) s = s.Substring(0, at);

            s = s.Replace("-", "_").Replace(" ", "_");
            s = s.Replace("(", "_").Replace(")", "");

            var us = s.IndexOf('_');
            if (us > 0) s = s.Substring(0, us);

            s = new string(s.Where(char.IsLetterOrDigit).ToArray());

            return s;
        }

        // ============================================================
        // DrawingType resolution
        // ============================================================

        private static FpAnnotationDeletionRules.DrawingType ResolveFpRulesDrawingType(DrawingRun run, DrawingData dd)
        {
            if (run?.Wedge?.Subclass == WedgeSubclass.PGB)
                return FpAnnotationDeletionRules.DrawingType.Pgb;

            var dt = dd == null ? string.Empty : dd.DrawingType.ToString();

            if (dt.Equals("Customer", StringComparison.OrdinalIgnoreCase))
                return FpAnnotationDeletionRules.DrawingType.Customer;

            return FpAnnotationDeletionRules.DrawingType.Production;
        }

        private static (FpAnnotationDeletionRules.ShankType Shank, FpAnnotationDeletionRules.FootOption Foot) ResolveFpShankAndFoot(WedgeData wedge)
        {
            var wedType = TryGetPropLoose(wedge, "Wed-Type");
            var footOpt = TryGetPropLoose(wedge, "Wed-Foot_Option");

            Logger.Blue($"FP FOOT OPTION : {footOpt}");

            var shank = ParseFpShankType(wedType);
            var foot = ResolveFpFootOption(wedge, footOpt);

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

        // ============================================================
        // Property helpers
        // ============================================================

        private static string? TryGetPropLoose(WedgeData wedge, string key)
        {
            if (wedge == null || string.IsNullOrWhiteSpace(key))
                return null;

            if (wedge.Properties != null)
            {
                if (wedge.Properties.TryGetValue(key, out var v))
                    return string.IsNullOrWhiteSpace(v) ? null : v.Trim();

                foreach (var kv in wedge.Properties)
                {
                    if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                        return string.IsNullOrWhiteSpace(kv.Value) ? null : kv.Value.Trim();
                }
            }

            return null;
        }

        // ============================================================
        // Shank parsing
        // ============================================================

        private static FpAnnotationDeletionRules.ShankType ParseFpShankType(string? wedType)
        {
            var s = (wedType ?? string.Empty).Trim().ToUpperInvariant();

            if (s.Contains("180") || s.Contains("REV"))
                return FpAnnotationDeletionRules.ShankType.Deg180Rev;

            return FpAnnotationDeletionRules.ShankType.Std;
        }

        // ============================================================
        // Foot option resolution
        // ============================================================

        private static FpAnnotationDeletionRules.FootOption ResolveFpFootOption(WedgeData wedge, string? rawFootOption)
        {
            var s =
                NormalizeToken(rawFootOption) ??
                NormalizeToken(TryGetPropLoose(wedge, "Wed-Foot_Option")) ??
                NormalizeToken(TryGetPropLoose(wedge, "Foot_Option")) ??
                NormalizeToken(TryGetPropLoose(wedge, "FootOption")) ??
                NormalizeToken(TryGetPropLoose(wedge, "foot_option"));

            if (string.IsNullOrWhiteSpace(s))
                return FpAnnotationDeletionRules.FootOption.None;

            FpAnnotationDeletionRules.FootOption baseFoot =
                s switch
                {
                    "SW_C" => FpAnnotationDeletionRules.FootOption.C,
                    "SW_G" => FpAnnotationDeletionRules.FootOption.G,
                    "SW_VG" => FpAnnotationDeletionRules.FootOption.VG,
                    "SW_CG" => FpAnnotationDeletionRules.FootOption.CG,
                    "SW_CC" => FpAnnotationDeletionRules.FootOption.CC,
                    _ => FpAnnotationDeletionRules.FootOption.None
                };

            if (baseFoot == FpAnnotationDeletionRules.FootOption.C && s == "SW_C")
            {
                bool allPositive =
                    IsDimPositive(wedge, "CBRA") &&
                    IsDimPositive(wedge, "CBRL") &&
                    IsDimPositive(wedge, "CBRD");

                if (allPositive)
                {
                    Logger.Info("[FP.Cleanup] Foot rule: raw=SW_C and (CBRA/CBRL/CBRD all > 0) → using C_WITH_CBR.");
                    return FpAnnotationDeletionRules.FootOption.C_WITH_CBR;
                }
            }

            return baseFoot;
        }

        private static string? NormalizeToken(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;

            var t = s.Trim().ToUpperInvariant();

            t = t.Replace("-", "_").Replace(" ", "_");
            t = new string(t.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());

            return string.IsNullOrWhiteSpace(t) ? null : t;
        }
    }
}