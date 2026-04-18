// ModelAutomation/Execution/ModelAutomationOrchestrator.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DataManagement.Domain.Drawing;

using WAD.Runner.ModelAutomation.Common;
using WAD.Runner.ModelAutomation.SolidWorks;
using WAD.Runner.ModelAutomation.Rules;
using WAD.Runner.ModelAutomation.Rules.COB;
using WAD.Runner.ModelAutomation.Rules.UTUS;
using WAD.Runner.ModelAutomation.Rules.FP;
using WAD.Runner.ModelAutomation.Rules.OSG7;
using WAD.Runner.ModelAutomation.Tolerances;

using DomainDimension = WAD.Runner.DataManagement.Domain.Dimensions.Dimension;

namespace WAD.Runner.ModelAutomation.Execution
{
    /// <summary>
    /// Runs the end-to-end ModelAutomation workflow:
    ///   1) Copy templates + open part
    ///   2) Activate configuration + apply feature toggles (batch, no rebuild)
    ///   3) Compute effective dimensions (pure logic)
    ///   4) Apply dimensions (equation file primary, optional direct fallback)
    ///   5) Apply tolerances (overlay sketch parameters)
    ///   6) Apply standard SW dimension tolerances
    ///   7) ONE rebuild, save, close
    ///
    /// Configuration selection and toggle scope are fully delegated to
    /// per-wedge-type <see cref="IModelConfigurationRules"/> implementations.
    /// This class contains no configuration logic.
    /// </summary>
    public sealed class ModelAutomationOrchestrator
    {
        private readonly ModelDimensionApplier _dimensionApplier;
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

            // ── Step 1: Plan paths + copy templates ───────────────────────────
            var plan = PathPlanner.Build(
                article: (job.ArticleNumber ?? "UNKNOWN").Trim(),
                subclass: job.Subclass,
                drawingType: job.DrawingType,
                outputRoot: string.IsNullOrWhiteSpace(job.OutputRoot)
                    ? Path.Combine("Resources", "Out")
                    : job.OutputRoot!,
                fileBase: job.FileBase);

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

            // ── Step 2: Open + activate config + apply feature toggles ────────
            using var editor = new ModelEditor(swApp);
            editor.OpenPart(modPartPath);

            var configPlan = ConfigurationRulesFactory
                .For(job.WedgeType)
                .Resolve(job.Subclass, job.DrawingType, job.WedgeData);

            Logger.Info(
                $"[ModelOrchestrator] ConfigPlan → config='{configPlan.ConfigurationName}', " +
                $"toggleScope={configPlan.ToggleScope}");

            editor.ActivateConfiguration(configPlan.ConfigurationName);

            if (job.WedgeData is null)
            {
                Logger.Warn("[ModelOrchestrator] No WedgeData — skipping toggles/dims/tolerances.");
                editor.RebuildOnce();
                editor.Save();
                editor.Close();
                return modPartPath;
            }

            var wedge = job.WedgeData;

            var featurePlan = ModelRuleRunner.BuildFeaturePlan(job.WedgeType, wedge, job.DrawingType);
            editor.ApplyFeatureToggles(featurePlan.Suppress, featurePlan.Unsuppress, configPlan.ToggleScope);

            // ── Step 3: Compute effective dimensions ──────────────────────────
            IEquationInputNormalizer normalizer = job.WedgeType switch
            {
                WedgeType.OSG7 => new Osg7EquationInputNormalizer(),
                WedgeType.COB => new CobEquationInputNormalizer(),
                WedgeType.UTUS => new UtusEquationInputNormalizer(),
                WedgeType.FP => new FpEquationInputNormalizer(),
                _ => new NoOpEquationInputNormalizer()
            };

            IReadOnlyDictionary<DimensionKey, DomainDimension> effectiveDims =
                normalizer.Normalize(wedge, job.DrawingType);

            if (effectiveDims.TryGetValue(DimensionKey.From("funnel_gap"), out var fg))
                Logger.Info($"[ModelOrchestrator] effective funnel_gap = {fg.Nominal.Value} {fg.Nominal.Unit}");
            else
                Logger.Warn("[ModelOrchestrator] funnel_gap not found in effectiveDims");

            // ── Step 4: Apply dimensions ──────────────────────────────────────
            var applyRes = _dimensionApplier.Apply(
                editor,
                equationsOutPath,
                effectiveDims,
                wedge,
                job.WedgeType,
                job.DrawingType);

            if (!applyRes.Success)
            {
                Logger.Warn(
                    $"[ModelOrchestrator] Dimension apply failed. " +
                    $"Method={applyRes.MethodUsed}. Error={applyRes.Error}");
            }

            // ── Step 5: Apply overlay sketch-parameter tolerances ─────────────
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

                    var model = editor.Model;
                    if (model == null)
                    {
                        Logger.Warn("[ModelOrchestrator] editor.Model is null; cannot apply tolerance plan.");
                    }
                    else
                    {
                        new ToleranceApplier(model).Apply(tolPlan);
                    }
                }
                else
                {
                    Logger.Info("[ModelOrchestrator] No tolerance plan updates for this job.");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[ModelOrchestrator] Tolerance plan/apply failed (continuing): {ex.Message}");
            }

            // ── Step 6: Apply standard SW dimension tolerances ────────────────
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

            // ── Step 7: Rebuild, save, close ──────────────────────────────────
            editor.RebuildOnce();
            editor.Save();
            editor.Close();

            await Task.Yield();
            ct.ThrowIfCancellationRequested();

            Logger.Success($"[ModelOrchestrator] Done → {modPartPath}");
            return modPartPath;
        }
    }
}