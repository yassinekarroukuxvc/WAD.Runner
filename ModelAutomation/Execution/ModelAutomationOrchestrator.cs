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
using WAD.Runner.ModelAutomation.Core;
using WAD.Runner.ModelAutomation.Rules;
using WAD.Runner.ModelAutomation.SolidWorks;
using WAD.Runner.ModelAutomation.Tolerances;

namespace WAD.Runner.ModelAutomation.Execution;

/// <summary>
/// Thin workflow coordinator. It knows the fixed automation sequence, but delegates
/// all wedge-specific decisions to the selected WedgeAutomationProfile.
/// </summary>
public sealed class ModelAutomationOrchestrator
{
    private readonly ModelDimensionApplier _dimensionApplier;

    public ModelAutomationOrchestrator(ModelDimensionApplier? dimensionApplier = null)
    {
        _dimensionApplier = dimensionApplier ?? new ModelDimensionApplier();
    }

    public string Run(ModelJobRequest job, SldWorks swApp, CancellationToken ct)
    {
        if (job is null) throw new ArgumentNullException(nameof(job));
        if (swApp is null) throw new ArgumentNullException(nameof(swApp));
        ct.ThrowIfCancellationRequested();

        var paths = PathPlanner.Build(
            article: string.IsNullOrWhiteSpace(job.ArticleNumber) ? "UNKNOWN" : job.ArticleNumber.Trim(),
            subclass: job.Subclass,
            drawingType: job.DrawingType,
            outputRoot: string.IsNullOrWhiteSpace(job.OutputRoot) ? Path.Combine("Resources", "Out") : job.OutputRoot,
            fileBase: job.FileBase);

        ValidateInputs(job);
        PrepareTemplates(job, paths);

        var context = new ModelAutomationContext(job, paths);
        var profile = WedgeAutomationProfileRegistry.For(job.WedgeType);

        using var editor = new ModelEditor(swApp);
        editor.OpenPart(Path.GetFullPath(paths.PartPath));

        var configurationPlan = ResolveConfiguration(profile, job);
        editor.ActivateConfiguration(configurationPlan.ConfigurationName);

        if (!context.HasWedgeData)
        {
            Logger.Warn("[ModelAutomationOrchestrator] No WedgeData. Only opening/rebuilding/saving the template.");
            editor.RebuildOnce();
            editor.Save();
            editor.Close();
            return Path.GetFullPath(paths.PartPath);
        }

        ApplyFeatureToggles(editor, profile, context, configurationPlan);
        ApplyEquationsAndTolerances(editor, profile, context, configurationPlan);

        editor.RebuildOnce();
        EnforcePostRebuildSuppressions(editor, profile, context, configurationPlan);
        editor.Save();
        editor.Close();

        Logger.Success($"[ModelAutomationOrchestrator] Completed model automation -> {paths.PartPath}");
        return Path.GetFullPath(paths.PartPath);
    }

    public Task<string> RunAsync(ModelJobRequest job, SldWorks swApp, CancellationToken ct)
        => Task.FromResult(Run(job, swApp, ct));

    private static ConfigurationPlan ResolveConfiguration(WedgeAutomationProfile profile, ModelJobRequest job)
    {
        var basePlan = profile.ConfigurationRules.Resolve(job.Subclass, job.DrawingType, job.WedgeData, job.ToggleStepsOverride);

        var finalConfiguration = string.IsNullOrWhiteSpace(job.FinalActiveConfigurationOverride)
            ? basePlan.ConfigurationName
            : job.FinalActiveConfigurationOverride.Trim();

        if (ConfigurationPlanFactory.HasExplicitSteps(job.ToggleStepsOverride))
            return ConfigurationPlanFactory.ForExplicit(finalConfiguration, job.ToggleStepsOverride);

        if (string.Equals(finalConfiguration, basePlan.ConfigurationName, StringComparison.OrdinalIgnoreCase))
            return basePlan;

        return basePlan.ToggleMode switch
        {
            ToggleApplicationMode.ActiveConfiguration => ConfigurationPlanFactory.ForActive(finalConfiguration),
            ToggleApplicationMode.AllConfigurations => ConfigurationPlanFactory.ForAll(finalConfiguration),
            ToggleApplicationMode.ExplicitSteps => ConfigurationPlanFactory.ForExplicit(finalConfiguration, basePlan.ToggleSteps),
            _ => basePlan
        };
    }

    private static void ApplyFeatureToggles(
        ModelEditor editor,
        WedgeAutomationProfile profile,
        ModelAutomationContext context,
        ConfigurationPlan configPlan)
    {
        var wedge = context.Wedge!;

        switch (configPlan.ToggleMode)
        {
            case ToggleApplicationMode.ActiveConfiguration:
                ApplyFeaturePlan(editor, profile, context, configPlan.ConfigurationName, null, swInConfigurationOpts_e.swThisConfiguration);
                return;

            case ToggleApplicationMode.AllConfigurations:
                ApplyFeaturePlan(editor, profile, context, configPlan.ConfigurationName, null, swInConfigurationOpts_e.swAllConfiguration);
                return;

            case ToggleApplicationMode.ExplicitSteps:
                foreach (var step in configPlan.ToggleSteps ?? Array.Empty<FeatureToggleStep>())
                {
                    if (!editor.ActivateConfiguration(step.ConfigurationName))
                    {
                        Logger.Warn($"[ModelAutomationOrchestrator] Missing configuration '{step.ConfigurationName}'. Feature pass skipped.");
                        continue;
                    }

                    ApplyFeaturePlan(editor, profile, context, step.ConfigurationName, step.FeatureRuleProfile, swInConfigurationOpts_e.swThisConfiguration);
                }

                editor.ActivateConfiguration(configPlan.ConfigurationName);
                return;
        }
    }

    private static void ApplyFeaturePlan(
        ModelEditor editor,
        WedgeAutomationProfile profile,
        ModelAutomationContext context,
        string configurationName,
        string? ruleProfile,
        swInConfigurationOpts_e scope)
    {
        var ruleContext = new FeatureRuleContext(context.DrawingType, context.Subclass, configurationName, ruleProfile);
        var featurePlan = ModelRuleRunner.BuildFeaturePlan(profile, context.Wedge!, ruleContext);
        editor.ApplyFeatureToggles(featurePlan.Suppress, featurePlan.Unsuppress, scope);
    }

    private void ApplyEquationsAndTolerances(
        ModelEditor editor,
        WedgeAutomationProfile profile,
        ModelAutomationContext context,
        ConfigurationPlan configPlan)
    {
        if (configPlan.ToggleMode == ToggleApplicationMode.ExplicitSteps)
        {
            foreach (var step in configPlan.ToggleSteps ?? Array.Empty<FeatureToggleStep>())
            {
                if (!editor.ActivateConfiguration(step.ConfigurationName))
                {
                    Logger.Warn($"[ModelAutomationOrchestrator] Missing configuration '{step.ConfigurationName}'. Equation/tolerance pass skipped.");
                    continue;
                }

                ApplyEquationAndTolerancePass(editor, profile, context);
            }

            editor.ActivateConfiguration(configPlan.ConfigurationName);
            return;
        }

        ApplyEquationAndTolerancePass(editor, profile, context);
    }

    private void ApplyEquationAndTolerancePass(ModelEditor editor, WedgeAutomationProfile profile, ModelAutomationContext context)
    {
        var equationPlan = profile.EquationPlanner.Build(context);
        var result = _dimensionApplier.Apply(editor, context.Paths.EquationsPath, equationPlan);
        if (!result.Success)
            Logger.Warn($"[ModelAutomationOrchestrator] Equation apply failed: {result.Error}");

        var tolerancePlan = profile.ToleranceRules.Build(context.Wedge!, context.DrawingType, context.Subclass);
        if (tolerancePlan.Count > 0)
            new ToleranceApplier(editor.Model).Apply(tolerancePlan);

        var tolKeys = GetLengthToleranceKeys(context.Wedge!);
        if (tolKeys.Count > 0)
            editor.ApplyLengthTolerances(context.Wedge!, tolKeys);
    }

    private static IReadOnlyCollection<DimensionKey> GetLengthToleranceKeys(WedgeData wedge)
        => wedge.Dimensions
            .Where(kvp => kvp.Value.Nominal.Unit == UnitKind.Millimeter)
            .Where(kvp => !kvp.Value.Tol.IsZero)
            .Select(kvp => kvp.Key)
            .Distinct()
            .ToArray();

    private static void EnforcePostRebuildSuppressions(
        ModelEditor editor,
        WedgeAutomationProfile profile,
        ModelAutomationContext context,
        ConfigurationPlan configPlan)
    {
        if (profile.PostRebuildSuppressions.Count == 0)
            return;

        if (!editor.ActivateConfiguration(configPlan.ConfigurationName))
            return;

        var finalStepProfile = (configPlan.ToggleSteps ?? Array.Empty<FeatureToggleStep>())
            .LastOrDefault(s => string.Equals(s.ConfigurationName, configPlan.ConfigurationName, StringComparison.OrdinalIgnoreCase))
            ?.FeatureRuleProfile;

        var ruleContext = new FeatureRuleContext(context.DrawingType, context.Subclass, configPlan.ConfigurationName, finalStepProfile);
        var plan = ModelRuleRunner.BuildFeaturePlan(profile, context.Wedge!, ruleContext);
        var suppress = plan.Suppress
            .Where(x => profile.PostRebuildSuppressions.Contains(x, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (suppress.Length == 0) return;
        Logger.Info("[ModelAutomationOrchestrator] Post-rebuild suppressions -> " + string.Join(", ", suppress));
        editor.ApplyFeatureToggles(suppress, Array.Empty<string>(), swInConfigurationOpts_e.swThisConfiguration);
    }

    private static void ValidateInputs(ModelJobRequest job)
    {
        if (string.IsNullOrWhiteSpace(job.PartTemplatePath) || !File.Exists(job.PartTemplatePath))
            throw new FileNotFoundException($"Part template not found: {job.PartTemplatePath}");
        if (string.IsNullOrWhiteSpace(job.EquationTemplatePath) || !File.Exists(job.EquationTemplatePath))
            throw new FileNotFoundException($"Equation template not found: {job.EquationTemplatePath}");
    }

    private static void PrepareTemplates(ModelJobRequest job, PathPlanner.Plan paths)
    {
        TemplatePreparer.CopyTemplate(job.PartTemplatePath, paths.PartPath, overwrite: true);
        TemplatePreparer.CopyTemplate(job.EquationTemplatePath, paths.EquationsPath, overwrite: true);

        var attrs = File.GetAttributes(paths.EquationsPath);
        if ((attrs & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(paths.EquationsPath, attrs & ~FileAttributes.ReadOnly);
    }
}
