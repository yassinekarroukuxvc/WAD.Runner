// ModelAutomation/Rules/IModelConfigurationRules.cs
using System;
using System.Collections.Generic;
using System.Linq;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Rules
{
    /// <summary>
    /// Controls how feature toggles should be applied for a job.
    /// </summary>
    public enum ToggleApplicationMode
    {
        /// <summary>
        /// Build one feature plan and apply it only in the currently active configuration.
        /// </summary>
        ActiveConfiguration,

        /// <summary>
        /// Build one feature plan and apply it across all configurations.
        /// </summary>
        AllConfigurations,

        /// <summary>
        /// Build and apply one feature plan per explicit configuration step.
        /// Each step may carry its own feature-rule profile, so the rules can
        /// return different suppress/unsuppress sets for different configurations.
        /// </summary>
        ExplicitSteps
    }

    /// <summary>
    /// One explicit toggle pass.
    /// The orchestrator activates <see cref="ConfigurationName"/> and then asks the
    /// feature rules to build the plan for that step using <see cref="FeatureRuleProfile"/>
    /// (or the configuration name itself when the profile is left null).
    /// </summary>
    public sealed record FeatureToggleStep(
        string ConfigurationName,
        string? FeatureRuleProfile = null
    );

    /// <summary>
    /// The result of configuration planning for a single job.
    /// Tells the orchestrator:
    ///   - which SW configuration should remain active after the toggle phase
    ///   - whether to use a single-plan mode or explicit per-config steps
    ///   - the explicit step list when per-config planning is required
    /// </summary>
    public sealed record ConfigurationPlan(
        string ConfigurationName,
        ToggleApplicationMode ToggleMode,
        IReadOnlyList<FeatureToggleStep>? ToggleSteps = null
    );

    /// <summary>
    /// Small helpers to build configuration plans consistently.
    /// </summary>
    public static class ConfigurationPlanFactory
    {
        public static ConfigurationPlan ForActive(string configurationName)
            => new(configurationName, ToggleApplicationMode.ActiveConfiguration);

        public static ConfigurationPlan ForAll(string configurationName)
            => new(configurationName, ToggleApplicationMode.AllConfigurations);

        public static ConfigurationPlan ForExplicit(
            string configurationName,
            IEnumerable<FeatureToggleStep>? steps)
            => new(
                configurationName,
                ToggleApplicationMode.ExplicitSteps,
                NormalizeSteps(configurationName, steps));

        public static FeatureToggleStep Step(string configurationName, string? featureRuleProfile = null)
            => new(configurationName, featureRuleProfile);

        public static bool HasExplicitSteps(IReadOnlyList<FeatureToggleStep>? steps)
            => steps is { Count: > 0 };

        private static IReadOnlyList<FeatureToggleStep> NormalizeSteps(
            string finalConfigurationName,
            IEnumerable<FeatureToggleStep>? steps)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<FeatureToggleStep>();

            void add(FeatureToggleStep? step)
            {
                if (step is null || string.IsNullOrWhiteSpace(step.ConfigurationName))
                    return;

                var config = step.ConfigurationName.Trim();
                if (!seen.Add(config))
                    return;

                var profile = string.IsNullOrWhiteSpace(step.FeatureRuleProfile)
                    ? null
                    : step.FeatureRuleProfile.Trim();

                result.Add(new FeatureToggleStep(config, profile));
            }

            if (steps is not null)
            {
                foreach (var step in steps)
                    add(step);
            }

            // Always include the final active configuration so it is never skipped.
            add(new FeatureToggleStep(finalConfigurationName));

            return result;
        }
    }

    /// <summary>
    /// Per-wedge-type rule set that decides which SolidWorks configuration should
    /// remain active and how feature toggles should be applied.
    ///
    /// One implementation per wedge type. The orchestrator never contains
    /// wedge-specific configuration-selection logic — it only calls this interface.
    ///
    /// Design contract (pure logic, no SW calls):
    ///   - Implementations must never throw for valid input.
    ///   - If explicitToggleSteps is supplied, implementations may switch to
    ///     ExplicitSteps mode while still keeping their normal final active
    ///     configuration decision.
    ///   - If the combination is unrecognised, return a safe fallback plan.
    /// </summary>
    public interface IModelConfigurationRules
    {
        ConfigurationPlan Resolve(
            WedgeSubclass subclass,
            DrawingType drawingType,
            WedgeData? wedge,
            IReadOnlyList<FeatureToggleStep>? explicitToggleSteps = null);
    }
}
