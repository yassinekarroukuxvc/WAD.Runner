// ModelAutomation/Execution/ModelRuleRunner.cs
using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.Application; // Logger
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

using WAD.Runner.ModelAutomation.Rules; // CKVD/COB/OSG7 feature rule providers

namespace WAD.Runner.ModelAutomation.Execution
{
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

        public static FeaturePlan BuildFeaturePlan(WedgeType wedgeType, WedgeData wedge, DrawingType drawingType)
        {
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));

            // IMPORTANT:
            // - Do NOT rely on wedge.Properties for subclass routing; use wedge.Subclass directly.
            var subclass = wedge.Subclass;

            Logger.Info($"[ModelRuleRunner] BuildFeaturePlan → wedgeType={wedgeType}, subclass={subclass}, drawingType={drawingType}");

            IFeatureRuleSet rules = wedgeType switch
            {
                WedgeType.CKVD => new CkvdFeatureRules(),
                WedgeType.COB => new CobFeatureRules(),
                WedgeType.UTUS => new UtusFeatureRules(),
                WedgeType.FP => new FpFeatureRules(),
                _ => new DefaultFeatureRules()
            };

            // Build the raw plan (pure planning)
            // NOTE: subclass is now passed explicitly to rule sets.
            var plan = rules.Build(wedge, drawingType, subclass);

            // Normalize (trim, distinct, remove overlaps)
            var unsupSet = Normalize(plan.Unsuppress);
            var supSet = Normalize(plan.Suppress);

            // If name appears in both, unsuppress wins (safer)
            supSet.RemoveWhere(n => unsupSet.Contains(n));

            // Optional: deterministic ordering for logs
            var unsup = unsupSet.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
            var sup = supSet.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

            Logger.Info($"[ModelRuleRunner] Plan → unsuppress={unsup.Count}, suppress={sup.Count}");

            return new FeaturePlan(sup, unsup);
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
        ModelRuleRunner.FeaturePlan Build(WedgeData wedge, DrawingType drawingType, WedgeSubclass subclass);
    }
}