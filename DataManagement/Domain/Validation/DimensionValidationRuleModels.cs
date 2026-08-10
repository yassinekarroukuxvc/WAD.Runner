using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Validation;

internal sealed class DimensionSlot
{
    public string DisplayName { get; }

    public IReadOnlyList<string> Aliases { get; }

    public DimensionSlot(
        string displayName,
        params string[] aliases)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException(
                "Display name cannot be empty.",
                nameof(displayName));
        }

        DisplayName = displayName.Trim();

        Aliases =
            aliases is { Length: > 0 }
                ? aliases
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Select(a => a.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : new[]
                {
                    DisplayName
                };
    }

    public bool IsPositive(WedgeData wedge)
    {
        return Aliases.Any(
            alias => WedgeDimensionAccess.IsPositive(
                wedge,
                alias));
    }
}

internal sealed class DimensionGroup
{
    public string DisplayName { get; }

    public IReadOnlyList<DimensionSlot> Slots { get; }

    public DimensionGroup(
        string displayName,
        params DimensionSlot[] slots)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException(
                "Display name cannot be empty.",
                nameof(displayName));
        }

        DisplayName = displayName.Trim();
        Slots = slots ?? Array.Empty<DimensionSlot>();
    }
}

internal sealed class WedgeDimensionValidationRuleSet
{
    public static WedgeDimensionValidationRuleSet Empty { get; }
        = new();

    public IReadOnlyList<DimensionSlot> RequiredStandalone
    { get; init; }
        = Array.Empty<DimensionSlot>();

    public IReadOnlyList<DimensionGroup> RequiredAndGroups
    { get; init; }
        = Array.Empty<DimensionGroup>();

    public IReadOnlyList<DimensionGroup> RequiredOrGroups
    { get; init; }
        = Array.Empty<DimensionGroup>();

    public IReadOnlyList<DimensionSlot> OptionalStandalone
    { get; init; }
        = Array.Empty<DimensionSlot>();

    public IReadOnlyList<DimensionGroup> ConditionalAndGroups
    { get; init; }
        = Array.Empty<DimensionGroup>();

    public IReadOnlyList<DimensionGroup> ConditionalOrGroups
    { get; init; }
        = Array.Empty<DimensionGroup>();
}
