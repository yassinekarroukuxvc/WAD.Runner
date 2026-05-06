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
using WAD.Runner.ModelAutomation.Common;
using WAD.Runner.ModelAutomation.Rules;
using WAD.Runner.ModelAutomation.SolidWorks;
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
    /// This version uses the centralized wedge profile registry so rule selection
    /// lives in one place.
    /// </summary>
    public sealed class ModelAutomationOrchestrator
    {
        private readonly ModelDimensionApplier _dimensionApplier;
        private readonly TolerancePlanner _tolerancePlanner = new();

        private static readonly string[] PostRebuildCriticalSuppressions =
        {
            "ERW_STD_feature",
            "ERW_STD_sketch",
            "ERW_180_DEG_REV_feature",
            "ERW_180_DEG_REV_sketch"
        };

        public ModelAutomationOrchestrator(ModelDimensionApplier? dimensionApplier = null)
        {
            _dimensionApplier = dimensionApplier ?? new ModelDimensionApplier(
                mode: DimensionApplyMode.EquationFilePrimary,
                fallbackToAlternate: false);
        }

        /// <summary>
        /// Real synchronous execution path.
        /// Keep this as the main implementation because the workflow is fully synchronous today.
        /// </summary>
        public string Run(ModelJobRequest job, SldWorks swApp, CancellationToken ct)
        {
            if (job is null) throw new ArgumentNullException(nameof(job));
            if (swApp is null) throw new ArgumentNullException(nameof(swApp));

            ct.ThrowIfCancellationRequested();

            var profile = WedgeAutomationProfileRegistry.For(job.WedgeType);

            var plan = PathPlanner.Build(
                article: (job.ArticleNumber ?? "UNKNOWN").Trim(),
                subclass: job.Subclass,
                drawingType: job.DrawingType,
                outputRoot: string.IsNullOrWhiteSpace(job.OutputRoot)
                    ? Path.Combine("Resources", "Out")
                    : job.OutputRoot,
                fileBase: job.FileBase);

            var modPartPath = Path.GetFullPath(plan.PartPath);
            var equationsOutPath = Path.GetFullPath(plan.EquationsPath);

            ValidateInputFiles(job);
            PrepareTemplates(job, modPartPath, equationsOutPath);

            using var editor = new ModelEditor(swApp);
            editor.OpenPart(modPartPath);

            var basePlan = profile.ConfigurationRules.Resolve(
                job.Subclass,
                job.DrawingType,
                job.WedgeData,
                job.ToggleStepsOverride);

            var configPlan = ApplyJobOverrides(basePlan, job);

            Logger.Info(
                $"[ModelOrchestrator] ConfigPlan → config='{configPlan.ConfigurationName}', " +
                $"toggleMode={configPlan.ToggleMode}, " +
                $"steps=[{string.Join(", ", (configPlan.ToggleSteps ?? Array.Empty<FeatureToggleStep>()).Select(FormatStep))}]");

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

            ApplyFeatureToggles(editor, wedge, job, configPlan);

            IReadOnlyDictionary<DimensionKey, DomainDimension> effectiveDims =
                profile.EquationNormalizer.Normalize(wedge, job.DrawingType);

            var tolPlan = _tolerancePlanner.Build(
                wedgeType: job.WedgeType,
                wedge: wedge,
                drawingType: job.DrawingType,
                subclass: job.Subclass);

            var tolKeys = wedge.Dimensions
                .Where(kvp => kvp.Value.Nominal.Unit == UnitKind.Millimeter)
                .Where(kvp => !kvp.Value.Tol.IsZero)
                .Select(kvp => kvp.Key)
                .Distinct()
                .ToArray();

            ApplyDimensionsAndTolerances(
                editor,
                wedge,
                job,
                configPlan,
                equationsOutPath,
                effectiveDims,
                tolPlan,
                tolKeys);

            editor.RebuildOnce();
            EnforcePostRebuildCriticalSuppressions(editor, wedge, job, configPlan);
            editor.Save();
            editor.Close();

            Logger.Success($"[ModelOrchestrator] Model automation complete → {modPartPath}");
            return modPartPath;
        }

        /// <summary>
        /// Compatibility wrapper for existing callers.
        /// </summary>
        public Task<string> RunAsync(ModelJobRequest job, SldWorks swApp, CancellationToken ct)
            => Task.FromResult(Run(job, swApp, ct));

        private static void ValidateInputFiles(ModelJobRequest job)
        {
            if (string.IsNullOrWhiteSpace(job.PartTemplatePath) || !File.Exists(job.PartTemplatePath))
                throw new FileNotFoundException($"Part template not found: {job.PartTemplatePath}");

            if (string.IsNullOrWhiteSpace(job.EquationTemplatePath) || !File.Exists(job.EquationTemplatePath))
                throw new FileNotFoundException($"Equation template not found: {job.EquationTemplatePath}");
        }

        private static void PrepareTemplates(ModelJobRequest job, string modPartPath, string equationsOutPath)
        {
            TemplatePreparer.CopyTemplate(job.PartTemplatePath, modPartPath, overwrite: true);
            TemplatePreparer.CopyTemplate(job.EquationTemplatePath, equationsOutPath, overwrite: true);

            var eqAttrs = File.GetAttributes(equationsOutPath);
            if ((eqAttrs & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(equationsOutPath, eqAttrs & ~FileAttributes.ReadOnly);
                Logger.Info($"[ModelOrchestrator] Cleared read-only on equations file: {equationsOutPath}");
            }
        }

        private static ConfigurationPlan ApplyJobOverrides(ConfigurationPlan basePlan, ModelJobRequest job)
        {
            var finalConfiguration = string.IsNullOrWhiteSpace(job.FinalActiveConfigurationOverride)
                ? basePlan.ConfigurationName
                : job.FinalActiveConfigurationOverride.Trim();

            if (ConfigurationPlanFactory.HasExplicitSteps(job.ToggleStepsOverride))
                return ConfigurationPlanFactory.ForExplicit(finalConfiguration, job.ToggleStepsOverride);

            if (!string.Equals(finalConfiguration, basePlan.ConfigurationName, StringComparison.OrdinalIgnoreCase))
            {
                return basePlan.ToggleMode switch
                {
                    ToggleApplicationMode.ActiveConfiguration => ConfigurationPlanFactory.ForActive(finalConfiguration),
                    ToggleApplicationMode.AllConfigurations => ConfigurationPlanFactory.ForAll(finalConfiguration),
                    ToggleApplicationMode.ExplicitSteps => ConfigurationPlanFactory.ForExplicit(finalConfiguration, basePlan.ToggleSteps),
                    _ => basePlan
                };
            }

            return basePlan;
        }

        private static void ApplyFeatureToggles(
            ModelEditor editor,
            WedgeData wedge,
            ModelJobRequest job,
            ConfigurationPlan configPlan)
        {
            switch (configPlan.ToggleMode)
            {
                case ToggleApplicationMode.ActiveConfiguration:
                    {
                        var featurePlan = BuildFeaturePlanForCurrentConfig(job, wedge, configPlan.ConfigurationName, featureRuleProfile: null);
                        editor.ApplyFeatureToggles(
                            featurePlan.Suppress,
                            featurePlan.Unsuppress,
                            swInConfigurationOpts_e.swThisConfiguration);
                        return;
                    }

                case ToggleApplicationMode.AllConfigurations:
                    {
                        var featurePlan = BuildFeaturePlanForCurrentConfig(job, wedge, configPlan.ConfigurationName, featureRuleProfile: null);
                        editor.ApplyFeatureToggles(
                            featurePlan.Suppress,
                            featurePlan.Unsuppress,
                            swInConfigurationOpts_e.swAllConfiguration);
                        return;
                    }

                case ToggleApplicationMode.ExplicitSteps:
                    {
                        var steps = configPlan.ToggleSteps ?? Array.Empty<FeatureToggleStep>();

                        foreach (var step in steps)
                        {
                            if (!editor.ActivateConfiguration(step.ConfigurationName))
                            {
                                Logger.Warn($"[ModelOrchestrator] Skipping toggle pass for missing config '{step.ConfigurationName}'.");
                                continue;
                            }

                            var featurePlan = BuildFeaturePlanForCurrentConfig(
                                job,
                                wedge,
                                step.ConfigurationName,
                                step.FeatureRuleProfile);

                            Logger.Info(
                                $"[ModelOrchestrator] Applying explicit feature plan in config '{step.ConfigurationName}' " +
                                $"(profile={step.FeatureRuleProfile ?? "(none)"})");

                            editor.ApplyFeatureToggles(
                                featurePlan.Suppress,
                                featurePlan.Unsuppress,
                                swInConfigurationOpts_e.swThisConfiguration);
                        }

                        editor.ActivateConfiguration(configPlan.ConfigurationName);
                        return;
                    }

                default:
                    throw new ArgumentOutOfRangeException(nameof(configPlan.ToggleMode), configPlan.ToggleMode, null);
            }
        }

        private void ApplyDimensionsAndTolerances(
            ModelEditor editor,
            WedgeData wedge,
            ModelJobRequest job,
            ConfigurationPlan configPlan,
            string equationsOutPath,
            IReadOnlyDictionary<DimensionKey, DomainDimension> effectiveDims,
            TolerancePlan tolPlan,
            IReadOnlyCollection<DimensionKey> tolKeys)
        {
            if (configPlan.ToggleMode == ToggleApplicationMode.ExplicitSteps)
            {
                var steps = configPlan.ToggleSteps ?? Array.Empty<FeatureToggleStep>();

                foreach (var step in steps)
                {
                    if (!editor.ActivateConfiguration(step.ConfigurationName))
                    {
                        Logger.Warn($"[ModelOrchestrator] Skipping dimension/tolerance pass for missing config '{step.ConfigurationName}'.");
                        continue;
                    }

                    ApplyDimensionsAndTolerancesForActiveConfiguration(
                        editor,
                        wedge,
                        job,
                        equationsOutPath,
                        effectiveDims,
                        tolPlan,
                        tolKeys);
                }

                editor.ActivateConfiguration(configPlan.ConfigurationName);
                return;
            }

            ApplyDimensionsAndTolerancesForActiveConfiguration(
                editor,
                wedge,
                job,
                equationsOutPath,
                effectiveDims,
                tolPlan,
                tolKeys);
        }

        private void ApplyDimensionsAndTolerancesForActiveConfiguration(
            ModelEditor editor,
            WedgeData wedge,
            ModelJobRequest job,
            string equationsOutPath,
            IReadOnlyDictionary<DimensionKey, DomainDimension> effectiveDims,
            TolerancePlan tolPlan,
            IReadOnlyCollection<DimensionKey> tolKeys)
        {
            var activeConfigName = TryGetActiveConfigurationName(editor.Model);
            Logger.Info($"[ModelOrchestrator] Applying dimensions/tolerances in active config '{activeConfigName}'.");

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
                    $"[ModelOrchestrator] Dimension apply failed in config '{activeConfigName}'. " +
                    $"Method={applyRes.MethodUsed}. Error={applyRes.Error}");
            }

            try
            {
                if (tolPlan.Count > 0)
                {
                    Logger.Info(
                        $"[ModelOrchestrator] Applying tolerance plan in config '{activeConfigName}': {tolPlan.Count} updates…");

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
                    Logger.Info($"[ModelOrchestrator] No tolerance plan updates for active config '{activeConfigName}'.");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(
                    $"[ModelOrchestrator] Tolerance plan/apply failed in config '{activeConfigName}' (continuing): {ex.Message}");
            }

            if (tolKeys.Count > 0)
                editor.ApplyLengthTolerances(wedge, tolKeys);
            else
                Logger.Info($"[ModelOrchestrator] No non-zero length tolerances found in WedgeData for config '{activeConfigName}'.");
        }


        /// <summary>
        /// Re-applies suppression for feature/sketch pairs that SolidWorks may pull
        /// back on during the final rebuild because of parent-child feature links.
        ///
        /// The normal toggle pass already suppresses these.  This final, suppress-only
        /// pass happens after dimensions/tolerances and after the single rebuild, so
        /// parent features such as ROUND_BR cannot re-unsuppress ERW afterward.
        /// </summary>
        private static void EnforcePostRebuildCriticalSuppressions(
            ModelEditor editor,
            WedgeData wedge,
            ModelJobRequest job,
            ConfigurationPlan configPlan)
        {
            if (!editor.ActivateConfiguration(configPlan.ConfigurationName))
            {
                Logger.Warn($"[ModelOrchestrator] Post-rebuild suppression skipped; final config '{configPlan.ConfigurationName}' could not be activated.");
                return;
            }

            var featureRuleProfile = ResolveFinalFeatureRuleProfile(configPlan);
            var featurePlan = BuildFeaturePlanForCurrentConfig(
                job,
                wedge,
                configPlan.ConfigurationName,
                featureRuleProfile);

            var suppress = featurePlan.Suppress
                .Where(IsPostRebuildCriticalSuppression)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (suppress.Length == 0)
            {
                Logger.Info("[ModelOrchestrator] Post-rebuild critical suppression -> nothing to enforce.");
                return;
            }

            Logger.Info(
                "[ModelOrchestrator] Post-rebuild critical suppression -> " +
                string.Join(", ", suppress));

            editor.ApplyFeatureToggles(
                suppress,
                Array.Empty<string>(),
                swInConfigurationOpts_e.swThisConfiguration);
        }

        private static string? ResolveFinalFeatureRuleProfile(ConfigurationPlan configPlan)
        {
            var steps = configPlan.ToggleSteps ?? Array.Empty<FeatureToggleStep>();

            return steps
                .LastOrDefault(s => string.Equals(
                    s.ConfigurationName,
                    configPlan.ConfigurationName,
                    StringComparison.OrdinalIgnoreCase))
                ?.FeatureRuleProfile;
        }

        private static bool IsPostRebuildCriticalSuppression(string name)
            => PostRebuildCriticalSuppressions.Contains(name, StringComparer.OrdinalIgnoreCase);

        private static ModelRuleRunner.FeaturePlan BuildFeaturePlanForCurrentConfig(
            ModelJobRequest job,
            WedgeData wedge,
            string configurationName,
            string? featureRuleProfile)
        {
            var context = new FeatureRuleContext(
                DrawingType: job.DrawingType,
                Subclass: job.Subclass,
                TargetConfigurationName: configurationName,
                FeatureRuleProfile: featureRuleProfile);

            return ModelRuleRunner.BuildFeaturePlan(job.WedgeType, wedge, context);
        }

        private static string FormatStep(FeatureToggleStep step)
            => string.IsNullOrWhiteSpace(step.FeatureRuleProfile)
                ? step.ConfigurationName
                : $"{step.ConfigurationName}:{step.FeatureRuleProfile}";

        private static string TryGetActiveConfigurationName(ModelDoc2 model)
        {
            try
            {
                return model.ConfigurationManager?.ActiveConfiguration?.Name ?? "(unknown)";
            }
            catch
            {
                return "(unknown)";
            }
        }
    }
}
