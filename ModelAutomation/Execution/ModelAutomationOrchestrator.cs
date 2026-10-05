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
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Common;
using WAD.Runner.ModelAutomation.Core;
using WAD.Runner.ModelAutomation.Rules;
using WAD.Runner.ModelAutomation.SolidWorks;

namespace WAD.Runner.ModelAutomation.Execution;

public sealed class ModelAutomationOrchestrator
{
    private readonly ModelDimensionApplier _dimensionApplier;

    public ModelAutomationOrchestrator(
        ModelDimensionApplier? dimensionApplier = null)
    {
        _dimensionApplier =
            dimensionApplier ?? new ModelDimensionApplier();
    }

    public string Run(
        ModelJobRequest job,
        SldWorks swApp,
        CancellationToken ct)
    {
        if (job is null)
            throw new ArgumentNullException(nameof(job));

        if (swApp is null)
            throw new ArgumentNullException(nameof(swApp));

        ct.ThrowIfCancellationRequested();

        ValidateInputs(job);

        var paths =
            PathPlanner.Build(
                article: job.ArticleNumber,
                subclass: job.Subclass,
                drawingType: job.DrawingType,
                outputRoot: job.OutputRoot,
                fileBase: job.FileBase);

        PrepareTemplates(
            job,
            paths);

        ct.ThrowIfCancellationRequested();

        var context =
            new ModelAutomationContext(
                job,
                paths);

        var profile =
            WedgeAutomationProfileRegistry.For(
                job.WedgeType);

        var configurationPlan =
            ResolveConfiguration(
                profile,
                job);

        using var editor =
            new ModelEditor(swApp);

        editor.OpenPart(
            Path.GetFullPath(
                paths.PartPath));

        // Old configuration behavior:
        // Try to activate the requested configuration,
        // but do not stop the automation if SolidWorks
        // does not report a successful activation.
        editor.ActivateConfiguration(
            configurationPlan.ConfigurationName);

        if (!context.HasWedgeData)
        {
            Logger.Warn(
                "[ModelAutomationOrchestrator] No WedgeData. " +
                "Rebuilding and saving the copied template only.");

            editor.RebuildOnce();
            editor.Save();
            editor.Close();

            return Path.GetFullPath(
                paths.PartPath);
        }

        ct.ThrowIfCancellationRequested();

        ApplyFeatureToggles(
            editor,
            profile,
            context,
            configurationPlan);

        ct.ThrowIfCancellationRequested();

        ApplyEquationsAndTolerances(
            editor,
            profile,
            context,
            configurationPlan);

        ct.ThrowIfCancellationRequested();

        editor.RebuildOnce();

        // ------------------------------------------------------------
        // POST-REBUILD RECONCILIATION
        // ------------------------------------------------------------
        //
        // Some feature families still need their normal post-rebuild
        // suppressions reasserted for every drawing type.
        //
        // The special OverlayCutFeatureFinalizer, however, is ONLY
        // allowed to run for:
        //
        //      FG  + Overlay
        //      PGB + Overlay
        //
        // Production and Customer models must never pass through the
        // overlay cut finalizer. Their cut/reference features are handled
        // as part of their normal feature plan instead.
        //
        FinalizeConfigurations(
            editor,
            profile,
            context,
            configurationPlan);

        ct.ThrowIfCancellationRequested();

        editor.Save();
        editor.Close();

        Logger.Success(
            "[ModelAutomationOrchestrator] " +
            $"Completed model automation -> {paths.PartPath}");

        return Path.GetFullPath(
            paths.PartPath);
    }

    /// <summary>
    /// SolidWorks COM work stays on the caller's thread.
    /// The Task wrapper exists for API compatibility and
    /// captures synchronous exceptions in the returned task.
    /// </summary>
    public Task<string> RunAsync(
        ModelJobRequest job,
        SldWorks swApp,
        CancellationToken ct)
    {
        try
        {
            return Task.FromResult(
                Run(
                    job,
                    swApp,
                    ct));
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<string>(
                ct);
        }
        catch (Exception ex)
        {
            return Task.FromException<string>(
                ex);
        }
    }

    private static ConfigurationPlan ResolveConfiguration(
        WedgeAutomationProfile profile,
        ModelJobRequest job)
    {
        var basePlan =
            profile.ConfigurationRules.Resolve(
                job.Subclass,
                job.DrawingType,
                job.WedgeData,
                job.ToggleStepsOverride);

        var finalConfiguration =
            string.IsNullOrWhiteSpace(
                job.FinalActiveConfigurationOverride)
                ? basePlan.ConfigurationName
                : job.FinalActiveConfigurationOverride.Trim();

        if (ConfigurationPlanFactory.HasExplicitSteps(
                job.ToggleStepsOverride))
        {
            return ConfigurationPlanFactory.ForExplicit(
                finalConfiguration,
                job.ToggleStepsOverride);
        }

        if (string.Equals(
                finalConfiguration,
                basePlan.ConfigurationName,
                StringComparison.OrdinalIgnoreCase))
        {
            return basePlan;
        }

        return basePlan.ToggleMode switch
        {
            ToggleApplicationMode.ActiveConfiguration =>
                ConfigurationPlanFactory.ForActive(
                    finalConfiguration),

            ToggleApplicationMode.AllConfigurations =>
                ConfigurationPlanFactory.ForAll(
                    finalConfiguration),

            ToggleApplicationMode.ExplicitSteps =>
                ConfigurationPlanFactory.ForExplicit(
                    finalConfiguration,
                    basePlan.ToggleSteps),

            _ => throw new ArgumentOutOfRangeException(
                nameof(basePlan.ToggleMode),
                basePlan.ToggleMode,
                "Unknown toggle mode.")
        };
    }

    // ================================================================
    // OVERLAY CUT FINALIZER GATE
    // ================================================================

    /// <summary>
    /// The OverlayCutFeatureFinalizer is ONLY valid for Overlay drawings
    /// belonging to FG or PGB.
    ///
    /// Production and Customer drawings must use their complete normal
    /// feature plan without splitting overlay cut/reference names out.
    /// </summary>
    private static bool ShouldUseOverlayCutFinalizer(
        ModelAutomationContext context)
    {
        return
            context.DrawingType == DrawingType.Overlay &&
            (
                context.Subclass == WedgeSubclass.FG ||
                context.Subclass == WedgeSubclass.PGB
            );
    }

    // ================================================================
    // FEATURE TOGGLES
    // ================================================================

    private static void ApplyFeatureToggles(
        ModelEditor editor,
        WedgeAutomationProfile profile,
        ModelAutomationContext context,
        ConfigurationPlan configurationPlan)
    {
        switch (configurationPlan.ToggleMode)
        {
            case ToggleApplicationMode.ActiveConfiguration:

                ApplyFeaturePlan(
                    editor,
                    profile,
                    context,
                    configurationPlan.ConfigurationName,
                    ruleProfile: null,
                    swInConfigurationOpts_e
                        .swThisConfiguration);

                return;

            case ToggleApplicationMode.AllConfigurations:

                ApplyFeaturePlan(
                    editor,
                    profile,
                    context,
                    configurationPlan.ConfigurationName,
                    ruleProfile: null,
                    swInConfigurationOpts_e
                        .swAllConfiguration);

                return;

            case ToggleApplicationMode.ExplicitSteps:

                foreach (
                    var step in
                    configurationPlan.ToggleSteps ??
                    Array.Empty<FeatureToggleStep>())
                {
                    if (!editor.ActivateConfiguration(
                            step.ConfigurationName))
                    {
                        Logger.Warn(
                            "[ModelAutomationOrchestrator] " +
                            $"Missing configuration " +
                            $"'{step.ConfigurationName}'. " +
                            "Feature pass skipped.");

                        continue;
                    }

                    ApplyFeaturePlan(
                        editor,
                        profile,
                        context,
                        step.ConfigurationName,
                        step.FeatureRuleProfile,
                        swInConfigurationOpts_e
                            .swThisConfiguration);
                }

                // Old behavior:
                // Restore the final configuration without
                // throwing if SolidWorks rejects the switch.
                editor.ActivateConfiguration(
                    configurationPlan.ConfigurationName);

                return;

            default:

                throw new ArgumentOutOfRangeException(
                    nameof(configurationPlan.ToggleMode),
                    configurationPlan.ToggleMode,
                    "Unknown toggle mode.");
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
        var featurePlan =
            BuildFeaturePlanFor(
                profile,
                context,
                configurationName,
                ruleProfile);

        // ============================================================
        // PRODUCTION / CUSTOMER
        // ============================================================
        //
        // DO NOT call OverlayCutFeatureFinalizer.Split() here.
        //
        // Production and Customer must apply their COMPLETE feature
        // plan normally. This includes any cut/reference features that
        // their wedge rules want suppressed or unsuppressed.
        //
        // If we called Split() here and simply skipped ApplyLast(),
        // those cut/reference features would disappear from the normal
        // plan and would never be applied.
        //
        if (!ShouldUseOverlayCutFinalizer(context))
        {
            var result =
                editor.ApplyFeatureToggles(
                    featurePlan.Suppress,
                    featurePlan.Unsuppress,
                    scope);

            if (!result.IsSuccess)
            {
                Logger.Warn(
                    "[ModelAutomationOrchestrator] " +
                    "Feature plan completed with " +
                    $"missing={result.Missing.Count}, " +
                    $"failed={result.Failed.Count}, " +
                    $"config={configurationName}.");
            }

            return;
        }

        // ============================================================
        // FG / PGB OVERLAY ONLY
        // ============================================================
        //
        // Overlay cut/reference features are separated from the normal
        // feature pass so they can be reapplied LAST.
        //
        var split =
            OverlayCutFeatureFinalizer.Split(
                featurePlan);

        // Apply normal feature families first.
        var normalResult =
            editor.ApplyFeatureToggles(
                split.NormalPlan.Suppress,
                split.NormalPlan.Unsuppress,
                scope);

        if (!normalResult.IsSuccess)
        {
            Logger.Warn(
                "[ModelAutomationOrchestrator] " +
                "Normal overlay feature plan completed with " +
                $"missing={normalResult.Missing.Count}, " +
                $"failed={normalResult.Failed.Count}, " +
                $"config={configurationName}.");
        }

        // Overlay cut/reference state is deliberately applied last.
        OverlayCutFeatureFinalizer.ApplyLast(
            editor,
            split.FinalCutPlan,
            scope,
            configurationName,
            phase: "feature-plan");
    }

    // ================================================================
    // EQUATIONS / TOLERANCES
    // ================================================================

    private void ApplyEquationsAndTolerances(
        ModelEditor editor,
        WedgeAutomationProfile profile,
        ModelAutomationContext context,
        ConfigurationPlan configurationPlan)
    {
        if (configurationPlan.ToggleMode ==
            ToggleApplicationMode.ExplicitSteps)
        {
            foreach (
                var step in
                configurationPlan.ToggleSteps ??
                Array.Empty<FeatureToggleStep>())
            {
                if (!editor.ActivateConfiguration(
                        step.ConfigurationName))
                {
                    Logger.Warn(
                        "[ModelAutomationOrchestrator] " +
                        $"Missing configuration " +
                        $"'{step.ConfigurationName}'. " +
                        "Equation/tolerance pass skipped.");

                    continue;
                }

                ApplyEquationAndTolerancePass(
                    editor,
                    profile,
                    context);
            }

            // Old behavior:
            // Attempt to restore the final configuration,
            // but do not treat failure as fatal.
            editor.ActivateConfiguration(
                configurationPlan.ConfigurationName);

            return;
        }

        ApplyEquationAndTolerancePass(
            editor,
            profile,
            context);
    }

    private void ApplyEquationAndTolerancePass(
        ModelEditor editor,
        WedgeAutomationProfile profile,
        ModelAutomationContext context)
    {
        var equationPlan =
            profile.EquationPlanner.Build(
                context);

        var result =
            _dimensionApplier.Apply(
                editor,
                context.Paths.EquationsPath,
                equationPlan);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                "Equation application failed using " +
                $"{result.MethodUsed}: {result.Error}");
        }

        var tolerancePlan =
            profile.ToleranceRules.Build(
                context.Wedge!,
                context.DrawingType,
                context.Subclass);

        if (tolerancePlan.Count > 0)
        {
            new ToleranceApplier(
                    editor.Model)
                .Apply(
                    tolerancePlan);
        }

        var toleranceKeys =
            GetLengthToleranceKeys(
                context.Wedge!);

        if (toleranceKeys.Count > 0)
        {
            editor.ApplyLengthTolerances(
                context.Wedge!,
                toleranceKeys);
        }
    }

    private static IReadOnlyCollection<DimensionKey>
        GetLengthToleranceKeys(
            WedgeData wedge)
    {
        if (wedge.Dimensions is null)
            return Array.Empty<DimensionKey>();

        return wedge.Dimensions
            .Where(
                kvp =>
                    kvp.Value is not null)
            .Where(
                kvp =>
                    kvp.Value.Nominal.Unit ==
                    UnitKind.Millimeter)
            .Where(
                kvp =>
                    !kvp.Value.Tol.IsZero)
            .Select(
                kvp =>
                    kvp.Key)
            .Distinct()
            .ToArray();
    }

    // ================================================================
    // POST-REBUILD FINALIZATION
    // ================================================================

    /// <summary>
    /// Runs post-rebuild reconciliation for every configuration touched
    /// by the configuration plan.
    ///
    /// Normal PostRebuildSuppressions are still allowed for every drawing
    /// type.
    ///
    /// OverlayCutFeatureFinalizer is ONLY invoked for FG/PGB Overlay.
    /// Production and Customer drawings never enter that finalizer.
    /// </summary>
    private static void FinalizeConfigurations(
        ModelEditor editor,
        WedgeAutomationProfile profile,
        ModelAutomationContext context,
        ConfigurationPlan configurationPlan)
    {
        var targets =
            ResolveFinalizationTargets(
                configurationPlan);

        foreach (var target in targets)
        {
            // This pass is configuration-specific.
            //
            // If SolidWorks refuses the switch, skip the optional
            // reconciliation instead of accidentally modifying the
            // wrong configuration.
            if (!editor.ActivateConfiguration(
                    target.ConfigurationName))
            {
                Logger.Warn(
                    "[ModelAutomationOrchestrator] " +
                    "Post-rebuild finalization skipped because " +
                    $"configuration '{target.ConfigurationName}' " +
                    "could not be activated.");

                continue;
            }

            var plan =
                BuildFeaturePlanFor(
                    profile,
                    context,
                    target.ConfigurationName,
                    target.RuleProfile);

            // --------------------------------------------------------
            // NORMAL POST-REBUILD SUPPRESSIONS
            //
            // These remain valid for Overlay, Production and Customer.
            // --------------------------------------------------------
            EnforcePostRebuildSuppressions(
                editor,
                profile,
                target.ConfigurationName,
                plan);

            // --------------------------------------------------------
            // OVERLAY CUT FINALIZER
            //
            // FG Overlay  -> YES
            // PGB Overlay -> YES
            //
            // Production  -> NO
            // Customer    -> NO
            // --------------------------------------------------------
            if (ShouldUseOverlayCutFinalizer(
                    context))
            {
                ReapplyFinalOverlayCutState(
                    editor,
                    target.ConfigurationName,
                    plan);
            }
        }
    }

    /// <summary>
    /// Builds the ordered list of configurations that need post-rebuild
    /// reconciliation.
    ///
    /// Explicit-step plans contribute every distinct step configuration
    /// (keeping the last rule profile declared for each one). All modes
    /// end with the final active configuration.
    /// </summary>
    private static List<FinalizationTarget>
        ResolveFinalizationTargets(
            ConfigurationPlan configurationPlan)
    {
        var targets =
            new List<FinalizationTarget>();

        if (configurationPlan.ToggleMode ==
            ToggleApplicationMode.ExplicitSteps)
        {
            foreach (
                var step in
                configurationPlan.ToggleSteps ??
                Array.Empty<FeatureToggleStep>())
            {
                if (string.IsNullOrWhiteSpace(
                        step.ConfigurationName))
                {
                    continue;
                }

                var existingIndex =
                    targets.FindIndex(
                        target =>
                            string.Equals(
                                target.ConfigurationName,
                                step.ConfigurationName,
                                StringComparison
                                    .OrdinalIgnoreCase));

                if (existingIndex >= 0)
                {
                    // Last step declared for the configuration wins.
                    targets[existingIndex] =
                        new FinalizationTarget(
                            targets[existingIndex]
                                .ConfigurationName,
                            step.FeatureRuleProfile);

                    continue;
                }

                targets.Add(
                    new FinalizationTarget(
                        step.ConfigurationName,
                        step.FeatureRuleProfile));
            }
        }

        var finalIndex =
            targets.FindIndex(
                target =>
                    string.Equals(
                        target.ConfigurationName,
                        configurationPlan
                            .ConfigurationName,
                        StringComparison
                            .OrdinalIgnoreCase));

        if (finalIndex >= 0)
        {
            // Move final active configuration to the end.
            var finalTarget =
                targets[finalIndex];

            targets.RemoveAt(
                finalIndex);

            targets.Add(
                finalTarget);

            return targets;
        }

        targets.Add(
            new FinalizationTarget(
                configurationPlan.ConfigurationName,
                null));

        return targets;
    }

    private static void EnforcePostRebuildSuppressions(
        ModelEditor editor,
        WedgeAutomationProfile profile,
        string configurationName,
        ModelRuleRunner.FeaturePlan plan)
    {
        if (profile.PostRebuildSuppressions.Count == 0)
            return;

        var suppress =
            plan.Suppress
                .Where(
                    name =>
                        profile.PostRebuildSuppressions
                            .Contains(
                                name,
                                StringComparer
                                    .OrdinalIgnoreCase))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (suppress.Length == 0)
            return;

        Logger.Info(
            "[ModelAutomationOrchestrator] " +
            $"Post-rebuild suppressions -> " +
            $"config={configurationName}, " +
            string.Join(
                ", ",
                suppress));

        editor.ApplyFeatureToggles(
            suppress,
            Array.Empty<string>(),
            swInConfigurationOpts_e
                .swThisConfiguration);
    }

    /// <summary>
    /// Reapplies only the overlay cut/reference portion of the feature plan.
    ///
    /// IMPORTANT:
    /// The caller is responsible for ensuring that this method is invoked
    /// only for an FG/PGB Overlay model.
    /// </summary>
    private static void ReapplyFinalOverlayCutState(
        ModelEditor editor,
        string configurationName,
        ModelRuleRunner.FeaturePlan plan)
    {
        var finalCutPlan =
            OverlayCutFeatureFinalizer
                .ExtractFinalCutPlan(
                    plan);

        OverlayCutFeatureFinalizer.ApplyLast(
            editor,
            finalCutPlan,
            swInConfigurationOpts_e
                .swThisConfiguration,
            configurationName,
            phase: "post-rebuild-final");
    }

    // ================================================================
    // FEATURE PLAN BUILD
    // ================================================================

    private static ModelRuleRunner.FeaturePlan
        BuildFeaturePlanFor(
            WedgeAutomationProfile profile,
            ModelAutomationContext context,
            string configurationName,
            string? ruleProfile)
    {
        var ruleContext =
            new FeatureRuleContext(
                context.DrawingType,
                context.Subclass,
                configurationName,
                ruleProfile);

        return ModelRuleRunner.BuildFeaturePlan(
            profile,
            context.Wedge!,
            ruleContext);
    }

    // ================================================================
    // VALIDATION
    // ================================================================

    private static void ValidateInputs(
        ModelJobRequest job)
    {
        if (string.IsNullOrWhiteSpace(
                job.PartTemplatePath) ||
            !File.Exists(
                job.PartTemplatePath))
        {
            throw new FileNotFoundException(
                $"Part template not found: " +
                $"{job.PartTemplatePath}",
                job.PartTemplatePath);
        }

        if (string.IsNullOrWhiteSpace(
                job.EquationTemplatePath) ||
            !File.Exists(
                job.EquationTemplatePath))
        {
            throw new FileNotFoundException(
                $"Equation template not found: " +
                $"{job.EquationTemplatePath}",
                job.EquationTemplatePath);
        }
    }

    // ================================================================
    // TEMPLATE PREPARATION
    // ================================================================

    private static void PrepareTemplates(
        ModelJobRequest job,
        PathPlanner.Plan paths)
    {
        TemplatePreparer.CopyTemplate(
            job.PartTemplatePath,
            paths.PartPath,
            overwrite: true);

        TemplatePreparer.CopyTemplate(
            job.EquationTemplatePath,
            paths.EquationsPath,
            overwrite: true);

        var attributes =
            File.GetAttributes(
                paths.EquationsPath);

        if ((attributes &
             FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(
                paths.EquationsPath,
                attributes &
                ~FileAttributes.ReadOnly);
        }
    }

    private sealed record FinalizationTarget(
        string ConfigurationName,
        string? RuleProfile);
}