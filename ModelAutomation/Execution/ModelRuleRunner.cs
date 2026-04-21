using System;
using System.Collections.Generic;
using System.Linq;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Rules;

namespace WAD.Runner.ModelAutomation.Execution
{
    /// <summary>
    /// Carries the context for one feature-rule build pass.
    /// This lets the rule set vary its output by target configuration/profile.
    /// </summary>
    public sealed record FeatureRuleContext(
        DrawingType DrawingType,
        WedgeSubclass Subclass,
        string TargetConfigurationName,
        string? FeatureRuleProfile = null
    );

    /// <summary>
    /// Produces a batch feature-toggle plan (suppress/unsuppress) for a given wedge type.
    /// IMPORTANT: No SolidWorks calls here. No rebuilds. Pure planning.
    /// </summary>
    public static class ModelRuleRunner
    {
        public sealed record FeaturePlan(
            IReadOnlyList<string> Suppress,
            IReadOnlyList<string> Unsuppress)
        {
            public static FeaturePlan Empty { get; } =
                new(Array.Empty<string>(), Array.Empty<string>());
        }

        public static FeaturePlan BuildFeaturePlan(
            WedgeType wedgeType,
            WedgeData wedge,
            FeatureRuleContext context)
        {
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));
            if (context is null) throw new ArgumentNullException(nameof(context));

            Logger.Info(
                $"[ModelRuleRunner] BuildFeaturePlan → wedgeType={wedgeType}, " +
                $"subclass={context.Subclass}, drawingType={context.DrawingType}, " +
                $"targetConfig={context.TargetConfigurationName}, " +
                $"ruleProfile={context.FeatureRuleProfile ?? "(none)"}");

            var rules = WedgeAutomationProfileRegistry.For(wedgeType).FeatureRules;
            var plan = rules.Build(wedge, context);

            var unsupSet = Normalize(plan.Unsuppress);
            var supSet = Normalize(plan.Suppress);

            supSet.RemoveWhere(n => unsupSet.Contains(n));

            var unsup = unsupSet.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
            var sup = supSet.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

            Logger.Info($"[ModelRuleRunner] Plan → unsuppress={unsup.Count}, suppress={sup.Count}");
            return new FeaturePlan(sup, unsup);
        }

        /// <summary>
        /// Backward-compatible convenience overload for callers that do not need
        /// per-configuration rule variation.
        /// </summary>
        public static FeaturePlan BuildFeaturePlan(
            WedgeType wedgeType,
            WedgeData wedge,
            DrawingType drawingType)
        {
            return BuildFeaturePlan(
                wedgeType,
                wedge,
                new FeatureRuleContext(
                    drawingType,
                    wedge.Subclass,
                    TargetConfigurationName: string.Empty,
                    FeatureRuleProfile: null));
        }

        private static HashSet<string> Normalize(IEnumerable<string> names)
        {
            return new HashSet<string>(
                names.Where(s => !string.IsNullOrWhiteSpace(s))
                     .Select(s => s.Trim()),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Wedge-type feature rules should implement this. Pure planning only.
    /// </summary>
    public interface IFeatureRuleSet
    {
        ModelRuleRunner.FeaturePlan Build(WedgeData wedge, FeatureRuleContext context);
    }
}
