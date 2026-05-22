using System;
using System.Collections.Generic;
using System.Linq;
using WAD.Runner.ModelAutomation.Execution;

namespace WAD.Runner.ModelAutomation.Rules.Common;

/// <summary>
/// Small mutable helper used only inside feature planners.
/// Active names are unsuppressed; known-but-inactive names are suppressed.
/// </summary>
public sealed class FeaturePlanBuilder
{
    private readonly HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _forceSuppress = new(StringComparer.OrdinalIgnoreCase);

    public FeaturePlanBuilder Know(params string[] names) { AddRange(_known, names); return this; }
    public FeaturePlanBuilder Know(IEnumerable<string> names) { AddRange(_known, names); return this; }
    public FeaturePlanBuilder Activate(params string[] names) { AddRange(_active, names); AddRange(_known, names); return this; }
    public FeaturePlanBuilder Activate(IEnumerable<string> names) { AddRange(_active, names); AddRange(_known, names); return this; }
    public FeaturePlanBuilder ForceSuppress(params string[] names) { AddRange(_forceSuppress, names); AddRange(_known, names); return this; }
    public FeaturePlanBuilder ForceSuppress(IEnumerable<string> names) { AddRange(_forceSuppress, names); AddRange(_known, names); return this; }
    public FeaturePlanBuilder Deactivate(params string[] names) { foreach (var n in names) _active.Remove(n); return this; }

    public ModelRuleRunner.FeaturePlan Build()
    {
        _active.ExceptWith(_forceSuppress);
        var suppress = new HashSet<string>(_known, StringComparer.OrdinalIgnoreCase);
        suppress.ExceptWith(_active);
        suppress.UnionWith(_forceSuppress);
        return new ModelRuleRunner.FeaturePlan(
            suppress.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            _active.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static void AddRange(HashSet<string> set, IEnumerable<string>? names)
    {
        if (names is null) return;
        foreach (var name in names)
            if (!string.IsNullOrWhiteSpace(name)) set.Add(name.Trim());
    }
}
