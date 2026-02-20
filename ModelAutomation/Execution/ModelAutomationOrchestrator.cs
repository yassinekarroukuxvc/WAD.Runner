// ModelAutomation/Execution/ModelAutomationOrchestrator.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using SolidWorks.Interop.sldworks;

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
using SolidWorks.Interop.swconst;

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

            // Configuration activation (same mapping you used before)
            var configOk = editor.ActivateConfiguration(ResolveConfiguration(job.Subclass, job.DrawingType));
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
                job.DrawingType);

            if (!applyRes.Success)
                Logger.Warn($"[ModelOrchestrator] Dimension apply failed. Method={applyRes.MethodUsed}. Error={applyRes.Error}");

            // Tolerances (still no rebuild)
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

            editor.SetEngraving(engraving);

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

        private static string ResolveConfiguration(WedgeSubclass subclass, DrawingType drawingType)
        {
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
    }
}
