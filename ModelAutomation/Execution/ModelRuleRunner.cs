using System;
using System.Collections.Generic;
using System.Linq;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Rules;

namespace WAD.Runner.ModelAutomation.Execution;

public sealed record FeatureRuleContext(
    DrawingType DrawingType,
    WedgeSubclass Subclass,
    string TargetConfigurationName,
    string? FeatureRuleProfile = null
);

public interface IFeatureRuleSet
{
    ModelRuleRunner.FeaturePlan Build(WedgeData wedge, FeatureRuleContext context);
}

public static class ModelRuleRunner
{
    public sealed record FeaturePlan(IReadOnlyList<string> Suppress, IReadOnlyList<string> Unsuppress)
    {
        public static FeaturePlan Empty { get; } = new(Array.Empty<string>(), Array.Empty<string>());
    }

    public static FeaturePlan BuildFeaturePlan(
        WedgeAutomationProfile profile,
        WedgeData wedge,
        FeatureRuleContext context)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        if (wedge is null) throw new ArgumentNullException(nameof(wedge));
        if (context is null) throw new ArgumentNullException(nameof(context));

        Logger.Info(
            $"[ModelRuleRunner] profile={profile.Name}, subclass={context.Subclass}, drawingType={context.DrawingType}, " +
            $"targetConfig={context.TargetConfigurationName}, ruleProfile={context.FeatureRuleProfile ?? "(none)"}");

        var raw = profile.FeatureRules.Build(wedge, context);
        var unsuppress = Normalize(raw.Unsuppress);
        var suppress = Normalize(raw.Suppress);

        suppress.RemoveWhere(x => unsuppress.Contains(x));

        return new FeaturePlan(
            suppress.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            unsuppress.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public static FeaturePlan BuildFeaturePlan(WedgeType wedgeType, WedgeData wedge, FeatureRuleContext context)
        => BuildFeaturePlan(WedgeAutomationProfileRegistry.For(wedgeType), wedge, context);

    private static HashSet<string> Normalize(IEnumerable<string>? names)
        => new((names ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
}
