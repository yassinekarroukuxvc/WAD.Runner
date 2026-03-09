// ModelAutomation/Execution/ModelAutomationOrchestrator.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application; // Logger
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DataManagement.Domain.Drawing;

using WAD.Runner.ModelAutomation.Common;
using WAD.Runner.ModelAutomation.SolidWorks;
using WAD.Runner.ModelAutomation.Rules;
using WAD.Runner.ModelAutomation.Rules.OSG7;
using WAD.Runner.ModelAutomation.Rules.COB;


using WAD.Runner.ModelAutomation.Tolerances;

namespace WAD.Runner.ModelAutomation.Execution
{
    /// <summary>
    /// Runs the end-to-end ModelAutomation workflow:
    /// 1) Copy templates + open part
    /// 2) Apply feature toggles (batch, no rebuild)
    /// 3) Compute effective dimensions (wedge rules, pure logic)
    /// 4) Apply dimensions (equation file primary + optional direct fallback, no rebuild)
    /// 5) ONE rebuild, save, close
    /// </summary>
    public sealed class ModelAutomationOrchestrator
    {
        private readonly ModelDimensionApplier _dimensionApplier;

        // ✅ NEW: tolerance planner (pure logic)
        private readonly TolerancePlanner _tolerancePlanner = new();

        public ModelAutomationOrchestrator(ModelDimensionApplier? dimensionApplier = null)
        {
            _dimensionApplier = dimensionApplier ?? new ModelDimensionApplier(
                mode: DimensionApplyMode.EquationFilePrimary,
                fallbackToAlternate: false);
        }

        public async Task<string> RunAsync(ModelJobRequest job, SldWorks swApp, CancellationToken ct)
        {
            if (job is null) throw new ArgumentNullException(nameof(job));
            if (swApp is null) throw new ArgumentNullException(nameof(swApp));

            ct.ThrowIfCancellationRequested();

            // -----------------------------
            // Step 1) Plan paths + copy templates
            // -----------------------------
            var plan = PathPlanner.Build(
                article: (job.ArticleNumber ?? "UNKNOWN").Trim(),
                subclass: job.Subclass,
                drawingType: job.DrawingType,
                outputRoot: string.IsNullOrWhiteSpace(job.OutputRoot)
                    ? Path.Combine("Resources", "Out")
                    : job.OutputRoot!,
                fileBase: job.FileBase
            );

            var modPartPath = Path.GetFullPath(plan.PartPath);
            var equationsOutPath = Path.GetFullPath(plan.EquationsPath);

            if (string.IsNullOrWhiteSpace(job.PartTemplatePath) || !File.Exists(job.PartTemplatePath))
                throw new FileNotFoundException($"Part template not found: {job.PartTemplatePath}");

            if (string.IsNullOrWhiteSpace(job.EquationTemplatePath) || !File.Exists(job.EquationTemplatePath))
                throw new FileNotFoundException($"Equation template not found: {job.EquationTemplatePath}");

            TemplatePreparer.CopyTemplate(job.PartTemplatePath!, modPartPath, overwrite: true);
            TemplatePreparer.CopyTemplate(job.EquationTemplatePath!, equationsOutPath, overwrite: true);

            // Ensure equations file is writable
            var eqAttrs = File.GetAttributes(equationsOutPath);
            if ((eqAttrs & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(equationsOutPath, eqAttrs & ~FileAttributes.ReadOnly);
                Logger.Info($"[ModelOrchestrator] Cleared read-only on equations file: {equationsOutPath}");
            }

            // -----------------------------
            // Step 2) Open + config + feature toggles (fast batch, NO rebuild)
            // -----------------------------
            using var editor = new ModelEditor(swApp);

            editor.OpenPart(modPartPath);

            // IMPORTANT (COB change):
            // - For COB + FG (Production/Customer/Overlay): always use "Default"
            // - For COB + PGB: use COB_STD_PGB / COB_180_DEG_REV_PGB depending on shank type
            var configName = ResolveConfiguration(job.WedgeType, job.Subclass, job.DrawingType, job.WedgeData);
            var configOk = editor.ActivateConfiguration(configName);

            var toggleScope = configOk
                ? swInConfigurationOpts_e.swThisConfiguration
                : swInConfigurationOpts_e.swAllConfiguration;

            if (job.WedgeData is null)
            {
                Logger.Warn("[ModelOrchestrator] No WedgeData provided; skipping toggles/dims/tolerances. Will rebuild once and save.");
                editor.RebuildOnce();
                editor.Save();
                editor.Close();
                return modPartPath;
            }

            var wedge = job.WedgeData;

            // Feature rules: return two name sets (suppress/unsuppress), apply once
            var featurePlan = ModelRuleRunner.BuildFeaturePlan(job.WedgeType, wedge, job.DrawingType);
            editor.ApplyFeatureToggles(featurePlan.Suppress, featurePlan.Unsuppress);

            // -----------------------------
            // Step 3) Compute effective dimensions (pure logic, wedge rules)
            // -----------------------------
            IEquationInputNormalizer normalizer =
                job.WedgeType switch
                {
                    WedgeType.OSG7 => new Osg7EquationInputNormalizer(),
                    WedgeType.COB => new CobEquationInputNormalizer(),
                    _ => new NoOpEquationInputNormalizer()
                };

            IReadOnlyDictionary<DimensionKey, WAD.Runner.DataManagement.Domain.Dimensions.Dimension> effectiveDims =
                normalizer.Normalize(wedge, job.DrawingType);

            // quick sanity check (remove later)
            if (effectiveDims.TryGetValue(DimensionKey.From("funnel_gap"), out var fg))
                Logger.Info($"[ModelOrchestrator] effective funnel_gap = {fg.Nominal.Value} {fg.Nominal.Unit}");
            else
                Logger.Warn("[ModelOrchestrator] funnel_gap not found in effectiveDims");

            // -----------------------------
            // Step 4) Apply dimensions (primary equation file + optional direct fallback)
            // -----------------------------
            var applyRes = _dimensionApplier.Apply(
                editor,
                equationsOutPath,
                effectiveDims,
                wedge,
                job.WedgeType,     // ✅ NEW: wedgeType passed through
                job.DrawingType);

            if (!applyRes.Success)
                Logger.Warn($"[ModelOrchestrator] Dimension apply failed. Method={applyRes.MethodUsed}. Error={applyRes.Error}");

            // -----------------------------
            // Step 4.6) Push DB tolerances into template sketch parameters (Overlay etc.)
            // Still NO rebuild.
            // -----------------------------
            try
            {
                var tolPlan = _tolerancePlanner.Build(
                    wedgeType: job.WedgeType,
                    wedge: wedge,
                    drawingType: job.DrawingType,
                    subclass: job.Subclass);

                if (tolPlan.Count > 0)
                {
                    Logger.Info($"[ModelOrchestrator] Applying tolerance plan: {tolPlan.Count} updates…");

                    // NOTE: ModelEditor must expose ModelDoc2 via a property like `Model`.
                    var model = editor.Model;
                    if (model == null)
                    {
                        Logger.Warn("[ModelOrchestrator] editor.Model is null; cannot apply tolerance plan.");
                    }
                    else
                    {
                        var tolApplier = new ToleranceApplier(model);
                        tolApplier.Apply(tolPlan);
                    }
                }
                else
                {
                    Logger.Info("[ModelOrchestrator] No tolerance plan updates for this job.");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[ModelOrchestrator] Tolerance plan/apply step failed (continuing): {ex.Message}");
            }

            // -----------------------------
            // Existing standard tolerance application (kept)
            // NOTE: This is separate from overlay sketch-parameter tolerances above.
            // -----------------------------
            var tolKeys = wedge.Dimensions
                .Where(kvp => kvp.Value.Nominal.Unit == UnitKind.Millimeter)
                .Where(kvp => !kvp.Value.Tol.IsZero)
                .Select(kvp => kvp.Key)
                .Distinct()
                .ToArray();

            if (tolKeys.Length > 0)
                editor.ApplyLengthTolerances(wedge, tolKeys);
            else
                Logger.Info("[ModelOrchestrator] No non-zero length tolerances found in WedgeData.");

            // Optional: engraving property (no rebuild)
            var engraving =
                wedge.Marking?.Text ??
                (wedge.Properties.TryGetValue("Marking", out var s) ? s : null);

            //editor.SetEngraving(engraving);

            // -----------------------------
            // Step 5) ONE rebuild, save, close
            // -----------------------------
            editor.RebuildOnce();
            editor.Save();
            editor.Close();

            await Task.Yield();
            ct.ThrowIfCancellationRequested();

            Logger.Success($"[ModelOrchestrator] Done → {modPartPath}");
            return modPartPath;
        }

        private static string ResolveConfiguration(WedgeType wedgeType, WedgeSubclass subclass, DrawingType drawingType, WedgeData? wedge)
        {
            // Only COB has the special config mapping requested here.
            if (wedgeType == WedgeType.COB)
            {
                // COB + PGB: config depends on shank type
                if (subclass == WedgeSubclass.PGB)
                {
                    var shank = ResolveCobShankType(wedge);

                    // NOTE: you currently return "Default" for both; keep as-is until you wire real config names.
                    return shank == CobShankType.Rev180
                        ? "Default"
                        : "Default";
                }

                // COB + FG (Production/Customer/Overlay): always Default
                return "Default";
            }

            // Fallback: keep existing mapping for other wedge types (CKVD etc.)
            return subclass switch
            {
                WedgeSubclass.PGB when drawingType == DrawingType.Overlay => "PGB_OVERLAY",
                WedgeSubclass.PGB when drawingType == DrawingType.Customer => "PGB_CUSTOMER_DRAWING",
                WedgeSubclass.PGB => "PGB_DRAWING",

                _ when drawingType == DrawingType.Overlay => "FG_OVERLAY",
                _ when drawingType == DrawingType.Customer => "FG_CUSTOMER_DRAWING",
                _ => "FG_PRODUCTION_DRAWING"
            };
        }

        private static CobShankType ResolveCobShankType(WedgeData? wedge)
        {
            if (wedge == null) return CobShankType.Std;

            // Keep the same loose property parsing you used in CobFeatureRules
            var raw =
                GetPropLoose(wedge, "Wed-Type") ??
                GetPropLoose(wedge, "Wed_Type") ??
                GetPropLoose(wedge, "Wed Type") ??
                GetPropLoose(wedge, "Shank_Type") ??
                GetPropLoose(wedge, "shank_type") ??
                string.Empty;

            raw = NormalizeDbToken(raw);

            if (EqualsAny(raw,
                    "SW_180REV",
                    "SW_180_DEG_REV",
                    "SW_180DEGREV",
                    "180_DEG_REV",
                    "180DEGREV",
                    "180REV",
                    "REV",
                    "REVERSE"))
                return CobShankType.Rev180;

            return CobShankType.Std;
        }

        private static string? GetPropLoose(WedgeData wedge, string key)
        {
            try
            {
                if (wedge?.Properties == null || wedge.Properties.Count == 0)
                    return null;

                if (wedge.Properties.TryGetValue(key, out var exact))
                    return exact;

                var target = NormalizeKey(key);

                foreach (var kv in wedge.Properties)
                {
                    var k = NormalizeKey(kv.Key);
                    if (string.Equals(k, target, StringComparison.OrdinalIgnoreCase))
                        return kv.Value;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeKey(string? k)
        {
            k ??= string.Empty;
            k = k.Trim();
            return k.Replace("-", "").Replace("_", "").Replace(" ", "");
        }

        private static string NormalizeDbToken(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;

            s = s.Trim();
            var semi = s.IndexOf(';');
            if (semi >= 0)
                s = s.Substring(0, semi);

            return s.Trim();
        }

        private static bool EqualsAny(string value, params string[] options)
            => options.Any(o => string.Equals(value, o, StringComparison.OrdinalIgnoreCase));

        private enum CobShankType { Std, Rev180 }
    }
}