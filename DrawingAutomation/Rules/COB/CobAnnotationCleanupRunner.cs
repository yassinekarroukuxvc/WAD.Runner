// DrawingAutomation/Rules/COB/CobAnnotationCleanupRunner.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.SolidWorks;
using WAD.Runner.DrawingAutomation.Views; // AnnotationCleanupService

using WAD.Runner.DrawingAutomation.Rules.Common;

// ✅ Follow EquationUpdater pattern (typed access to wedge.Dimensions)
using DomDim = WAD.Runner.DataManagement.Domain.Dimensions.Dimension;
using DomDimKey = WAD.Runner.DataManagement.Domain.Dimensions.DimensionKey;
using DomUnitKind = WAD.Runner.DataManagement.Domain.Units.UnitKind;

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
                var options = BuildCobOptionsFromWedgeDimensions(run.Wedge);

                Logger.Blue($"[COB.Cleanup] DrawingType={rulesDrawingType}, Shank={shank}, Foot={foot}");

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
                    CobAnnotationDeletionRules.DumpExistingDisplayDimensionFullNamesFromDrawing(
                        model, viewNames, activateEachView: activateEachView);
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

        // ════════════════════════════════════════════════════════════════
        // OPTIONS BUILDER
        // Each flag is independently gated on its own dimension being > 0.
        // ════════════════════════════════════════════════════════════════

        private static CobAnnotationDeletionRules.Options BuildCobOptionsFromWedgeDimensions(WedgeData wedge)
        {
            bool Pos(string key) => IsDimPositive(wedge, key);

            // Front / Side
            var hasVwVr = Pos("VR") && Pos("VW");
            var hasSlb = Pos("VBL");

            // Detail
            var hasW2 = Pos("W2");
            var hasGa = Pos("GA");
            var hasCd = Pos("CD");
            var hasGd = Pos("GD");
            var hasGr = Pos("GR");
            var hasB = Pos("B");

            // Section
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
                $"[COB.Options] " +
                $"VW/VR={hasVwVr}, VBL={hasSlb}, " +
                $"W2={hasW2}, GA={hasGa}, CD={hasCd}, GD={hasGd}, GR={hasGr}, B={hasB}, " +
                $"RA2={hasRa2}, ERD={hasErd}, FR/BR={hasFrBr}, " +
                $"F={hasF}, G={hasG}, CGR={hasCgr}, CGD={hasCgd}, CBRA={hasCbra}, CBRL={hasCbrl}");

            // Extra debug dump
            foreach (var key in new[]
            {
                "VW", "VR", "VBL",
                "W2", "GA", "CD", "GD", "GR", "B",
                "RA2", "ERD", "FR", "BR",
                "F", "G", "CGR", "CGD", "CBRA", "CBRL"
            })
                DumpDim(wedge, key);

            return new CobAnnotationDeletionRules.Options
            {
                // Front / Side
                HasVwVr = hasVwVr,
                HasSlb = hasSlb,

                // Detail
                HasW2 = hasW2,
                HasGa = hasGa,
                HasCd = hasCd,
                HasGd = hasGd,
                HasGr = hasGr,
                HasB = hasB,

                // Section
                HasRa2 = hasRa2,
                HasErd = hasErd,
                HasFrBr = hasFrBr,
                HasF = hasF,
                HasG = hasG,
                HasCgr = hasCgr,
                HasCgd = hasCgd,
                HasCbra = hasCbra,
                HasCbrl = hasCbrl,

                KAnnotationFullName = null,
                ErdAnnotationFullName = null
            };
        }

        // ════════════════════════════════════════════════════════════════
        // DIM HELPERS
        // ════════════════════════════════════════════════════════════════

        private static void DumpDim(WedgeData wedge, string key)
        {
            if (!TryGetDim(wedge, key, out var dim) || dim is null)
            {
                Logger.Info($"[COB.OptionsDbg] {key}: (missing)");
                return;
            }

            try
            {
                double v = dim.Nominal.Unit == DomUnitKind.Degree
                    ? (double)dim.Nominal.AsDeg()
                    : (double)dim.Nominal.AsMm();

                Logger.Info($"[COB.OptionsDbg] {key}: {v.ToString("0.#####", CultureInfo.InvariantCulture)} ({dim.Nominal.Unit})");
            }
            catch (Exception ex)
            {
                Logger.Info($"[COB.OptionsDbg] {key}: (unreadable) {ex.Message}");
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

        /// <summary>
        /// Typed lookup like ModelAutomation.EquationUpdater:
        ///   1. Scan wedge.Dimensions by normalized base key (case-insensitive).
        /// </summary>
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

            // Strip sketch qualifier if present (e.g. "VBL@ANNOT_STD_FRONT_sketch" → "VBL")
            var at = s.IndexOf('@');
            if (at >= 0) s = s.Substring(0, at);

            s = s.Replace("-", "_").Replace(" ", "_");
            s = s.Replace("(", "_").Replace(")", "");

            // Keep only letters/digits
            s = new string(s.Where(char.IsLetterOrDigit).ToArray());

            return s;
        }

        // ════════════════════════════════════════════════════════════════
        // DRAWING TYPE RESOLUTION
        // ════════════════════════════════════════════════════════════════

        private static CobAnnotationDeletionRules.DrawingType ResolveCobRulesDrawingType(
            DrawingRun run, DrawingData dd)
        {
            if (run?.Wedge?.Subclass == WedgeSubclass.PGB)
                return CobAnnotationDeletionRules.DrawingType.Pgb;

            var dt = dd == null ? string.Empty : dd.DrawingType.ToString();

            if (dt.Equals("Customer", StringComparison.OrdinalIgnoreCase))
                return CobAnnotationDeletionRules.DrawingType.Customer;

            return CobAnnotationDeletionRules.DrawingType.Production;
        }

        // ════════════════════════════════════════════════════════════════
        // SHANK + FOOT RESOLUTION
        // ════════════════════════════════════════════════════════════════

        private static (CobAnnotationDeletionRules.ShankType Shank, CobAnnotationDeletionRules.FootOption Foot)
            ResolveCobShankAndFoot(WedgeData wedge)
        {
            var wedType = TryGetPropLoose(wedge, "Wed-Type");
            var footOpt = TryGetPropLoose(wedge, "Wed-Foot_Option");
            Logger.Blue($"[COB.Resolve] Wed-Type={wedType ?? "(null)"}, Wed-Foot_Option={footOpt ?? "(null)"}");

            var shank = ParseCobShankType(wedType);
            var foot = ResolveCobFootOption(wedge, footOpt);

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

        // ════════════════════════════════════════════════════════════════
        // PROPERTY HELPERS
        // ════════════════════════════════════════════════════════════════

        private static string? TryGetPropLoose(WedgeData wedge, string key)
        {
            if (wedge == null || string.IsNullOrWhiteSpace(key)) return null;

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

        // ════════════════════════════════════════════════════════════════
        // SHANK PARSING
        // ════════════════════════════════════════════════════════════════

        private static CobAnnotationDeletionRules.ShankType ParseCobShankType(string? wedType)
        {
            var s = (wedType ?? string.Empty).Trim().ToUpperInvariant();

            if (s.Contains("180") || s.Contains("REV"))
                return CobAnnotationDeletionRules.ShankType.Deg180Rev;

            return CobAnnotationDeletionRules.ShankType.Std;
        }

        // ════════════════════════════════════════════════════════════════
        // FOOT OPTION RESOLUTION
        //
        // Raw value from wedge property → normalized token → FootOption enum.
        // C_WITH_CBR is detected when raw=SW_C and CBRA+CBRL+CBRD all > 0.
        // ════════════════════════════════════════════════════════════════

        private static CobAnnotationDeletionRules.FootOption ResolveCobFootOption(
            WedgeData wedge, string? rawFootOption)
        {
            // Probe multiple possible property key spellings
            var s =
                NormalizeToken(rawFootOption) ??
                NormalizeToken(TryGetPropLoose(wedge, "Wed-Foot_Option")) ??
                NormalizeToken(TryGetPropLoose(wedge, "Foot_Option")) ??
                NormalizeToken(TryGetPropLoose(wedge, "FootOption")) ??
                NormalizeToken(TryGetPropLoose(wedge, "foot_option"));

            if (string.IsNullOrWhiteSpace(s))
                return CobAnnotationDeletionRules.FootOption.None;

            var baseFoot = s switch
            {
                "SW_C" => CobAnnotationDeletionRules.FootOption.C,
                "SW_G" => CobAnnotationDeletionRules.FootOption.G,
                "SW_VG" => CobAnnotationDeletionRules.FootOption.VG,
                "SW_CG" => CobAnnotationDeletionRules.FootOption.CG,
                "SW_CC" => CobAnnotationDeletionRules.FootOption.CC,
                _ => CobAnnotationDeletionRules.FootOption.None
            };

            // C_WITH_CBR: raw=SW_C and all three CBR dims are positive
            if (baseFoot == CobAnnotationDeletionRules.FootOption.C)
            {
                bool allCbrPositive =
                    IsDimPositive(wedge, "CBRA") &&
                    IsDimPositive(wedge, "CBRL") &&
                    IsDimPositive(wedge, "CBRD");

                if (allCbrPositive)
                {
                    Logger.Info("[COB.Cleanup] Foot rule: raw=SW_C + (CBRA/CBRL/CBRD all > 0) → C_WITH_CBR.");
                    return CobAnnotationDeletionRules.FootOption.C_WITH_CBR;
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