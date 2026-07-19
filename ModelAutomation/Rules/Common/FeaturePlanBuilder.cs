using System;
using System.Collections.Generic;
using System.Linq;
using WAD.Runner.ModelAutomation.Execution;

namespace WAD.Runner.ModelAutomation.Rules.Common;

/// <summary>
/// Builds a deterministic feature plan from known, active, and force-suppressed names.
/// The builder is reusable: calling Build() does not mutate the accumulated state.
/// </summary>
public sealed class FeaturePlanBuilder
{
    private readonly HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _forceSuppress = new(StringComparer.OrdinalIgnoreCase);

    public FeaturePlanBuilder Know(params string[] names) => Know((IEnumerable<string>)names);

    public FeaturePlanBuilder Know(IEnumerable<string>? names)
    {
        AddRange(_known, names);
        return this;
    }

    public FeaturePlanBuilder Activate(params string[] names) => Activate((IEnumerable<string>)names);

    public FeaturePlanBuilder Activate(IEnumerable<string>? names)
    {
        AddRange(_active, names);
        AddRange(_known, names);
        return this;
    }

    public FeaturePlanBuilder ForceSuppress(params string[] names) => ForceSuppress((IEnumerable<string>)names);

    public FeaturePlanBuilder ForceSuppress(IEnumerable<string>? names)
    {
        AddRange(_forceSuppress, names);
        AddRange(_known, names);
        return this;
    }

    public FeaturePlanBuilder Deactivate(params string[] names) => Deactivate((IEnumerable<string>)names);

    public FeaturePlanBuilder Deactivate(IEnumerable<string>? names)
    {
        if (names is null) return this;

        foreach (var name in names)
        {
            if (!string.IsNullOrWhiteSpace(name))
                _active.Remove(name.Trim());
        }

        return this;
    }

    /// <summary>
    /// Activates one item from a mutually-exclusive group and deactivates every other item.
    /// </summary>
    public FeaturePlanBuilder ActivateOnly(string activeName, IEnumerable<string> candidates)
    {
        if (string.IsNullOrWhiteSpace(activeName))
            throw new ArgumentException("The active feature name is required.", nameof(activeName));
        if (candidates is null)
            throw new ArgumentNullException(nameof(candidates));

        var normalizedCandidates = candidates
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!normalizedCandidates.Contains(activeName.Trim(), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"'{activeName}' is not part of the mutually-exclusive feature group.");

        Know(normalizedCandidates);
        Deactivate(normalizedCandidates);
        Activate(activeName);
        return this;
    }

    public ModelRuleRunner.FeaturePlan Build()
    {
        var active = new HashSet<string>(_active, StringComparer.OrdinalIgnoreCase);
        active.ExceptWith(_forceSuppress);

        var suppress = new HashSet<string>(_known, StringComparer.OrdinalIgnoreCase);
        suppress.ExceptWith(active);
        suppress.UnionWith(_forceSuppress);

        return new ModelRuleRunner.FeaturePlan(
            suppress.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            active.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static void AddRange(HashSet<string> set, IEnumerable<string>? names)
    {
        if (names is null) return;

        foreach (var name in names)
        {
            if (!string.IsNullOrWhiteSpace(name))
                set.Add(name.Trim());
        }
    }
}
