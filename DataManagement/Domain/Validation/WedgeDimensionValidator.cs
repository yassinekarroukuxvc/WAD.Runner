using System;
using System.Collections.Generic;
using System.Linq;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Wedge;
using DomDim = WAD.Runner.DataManagement.Domain.Dimensions.Dimension;

namespace WAD.Runner.DataManagement.Domain.Validation;

public static class WedgeDimensionValidator
{
    public static DimensionValidationResult Validate(WedgeData wedge, WedgeType wedgeType)
    {
        if (wedge is null)
            throw new ArgumentNullException(nameof(wedge));

        var ruleSet = WedgeDimensionValidationRuleSet.For(wedgeType);
        var issues = new List<DimensionValidationIssue>();

        ValidateRequiredStandalone(wedge, wedgeType, ruleSet.RequiredStandalone, issues);
        ValidateRequiredAndGroups(wedge, wedgeType, ruleSet.RequiredAndGroups, issues);
        ValidateRequiredOrGroups(wedge, wedgeType, ruleSet.RequiredOrGroups, issues);
        ValidateConditionalAndGroups(wedge, wedgeType, ruleSet.ConditionalAndGroups, issues);
        ValidateConditionalOrGroups(wedge, wedgeType, ruleSet.ConditionalOrGroups, issues);

        return new DimensionValidationResult(wedge.ArticleNumber, wedgeType, issues);
    }

    public static void ValidateOrThrow(WedgeData wedge, WedgeType wedgeType)
    {
        var result = Validate(wedge, wedgeType);
        if (!result.IsValid)
            throw new WedgeDimensionValidationException(result);
    }

    private static void ValidateRequiredStandalone(
        WedgeData wedge,
        WedgeType wedgeType,
        IReadOnlyList<DimensionSlot> slots,
        List<DimensionValidationIssue> issues)
    {
        foreach (var slot in slots)
        {
            if (slot.IsPositive(wedge))
                continue;

            AddIssue(
                wedge,
                wedgeType,
                issues,
                "Type 1-1 Required standalone",
                slot.DisplayName,
                slot.DisplayName,
                BuildMissingOrInvalidMessage(wedge, slot, "must be > 0"));
        }
    }

    private static void ValidateRequiredAndGroups(
        WedgeData wedge,
        WedgeType wedgeType,
        IReadOnlyList<DimensionGroup> groups,
        List<DimensionValidationIssue> issues)
    {
        foreach (var group in groups)
        {
            foreach (var slot in group.Slots.Where(s => !s.IsPositive(wedge)))
            {
                AddIssue(
                    wedge,
                    wedgeType,
                    issues,
                    "Type 1-2 Required AND group",
                    group.DisplayName,
                    slot.DisplayName,
                    BuildMissingOrInvalidMessage(wedge, slot, $"is required by group [{group.DisplayName}] and must be > 0"));
            }
        }
    }

    private static void ValidateRequiredOrGroups(
        WedgeData wedge,
        WedgeType wedgeType,
        IReadOnlyList<DimensionGroup> groups,
        List<DimensionValidationIssue> issues)
    {
        foreach (var group in groups)
        {
            if (group.Slots.Any(s => s.IsPositive(wedge)))
                continue;

            AddIssue(
                wedge,
                wedgeType,
                issues,
                "Type 1-3 Required OR group",
                group.DisplayName,
                group.DisplayName,
                $"at least one of [{group.DisplayName}] must be present and > 0");
        }
    }

    private static void ValidateConditionalAndGroups(
        WedgeData wedge,
        WedgeType wedgeType,
        IReadOnlyList<DimensionGroup> groups,
        List<DimensionValidationIssue> issues)
    {
        foreach (var group in groups)
        {
            var positiveSlots = group.Slots.Where(s => s.IsPositive(wedge)).ToList();
            if (positiveSlots.Count == 0)
                continue;

            foreach (var slot in group.Slots.Where(s => !s.IsPositive(wedge)))
            {
                AddIssue(
                    wedge,
                    wedgeType,
                    issues,
                    "Type 2-2 Conditional AND group",
                    group.DisplayName,
                    slot.DisplayName,
                    BuildMissingOrInvalidMessage(
                        wedge,
                        slot,
                        $"must be > 0 because conditional group [{group.DisplayName}] is active. Triggered by: {string.Join(", ", positiveSlots.Select(s => s.DisplayName))}"));
            }
        }
    }

    private static void ValidateConditionalOrGroups(
        WedgeData wedge,
        WedgeType wedgeType,
        IReadOnlyList<DimensionGroup> groups,
        List<DimensionValidationIssue> issues)
    {
        foreach (var group in groups)
        {
            var positiveSlots = group.Slots.Where(s => s.IsPositive(wedge)).ToList();
            if (positiveSlots.Count <= 1)
                continue;

            AddIssue(
                wedge,
                wedgeType,
                issues,
                "Type 2-3 Conditional OR group",
                group.DisplayName,
                string.Join(", ", positiveSlots.Select(s => s.DisplayName)),
                $"at most one of [{group.DisplayName}] is expected to be > 0. Found: {string.Join(", ", positiveSlots.Select(s => s.DisplayName))}");
        }
    }

    private static string BuildMissingOrInvalidMessage(WedgeData wedge, DimensionSlot slot, string suffix)
    {
        var presentAliases = slot.Aliases.Where(a => TryGetDimension(wedge, a, out _)).ToList();

        if (presentAliases.Count == 0)
            return $"missing; {suffix}";

        var values = presentAliases
            .Select(a => TryGetDimension(wedge, a, out var d) ? $"{a}={d!.Nominal.Value}" : a)
            .ToList();

        return $"invalid ({string.Join(", ", values)}); {suffix}";
    }

    private static void AddIssue(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues,
        string requirementType,
        string ruleName,
        string dimension,
        string message)
    {
        issues.Add(new DimensionValidationIssue(
            wedge.ArticleNumber,
            wedgeType,
            requirementType,
            ruleName,
            dimension,
            message));
    }

    private static bool TryGetDimension(WedgeData wedge, string key, out DomDim? dimension)
    {
        dimension = null;

        if (wedge.Dimensions is null || wedge.Dimensions.Count == 0)
            return false;

        var target = DimensionKey.From(key);
        if (wedge.Dimensions.TryGetValue(target, out var exact))
        {
            dimension = exact;
            return dimension is not null;
        }

        foreach (var kvp in wedge.Dimensions)
        {
            if (!string.Equals(kvp.Key.Value, key, StringComparison.OrdinalIgnoreCase))
                continue;

            dimension = kvp.Value;
            return dimension is not null;
        }

        return false;
    }

    internal sealed class DimensionSlot
    {
        public string DisplayName { get; }
        public IReadOnlyList<string> Aliases { get; }

        public DimensionSlot(string displayName, params string[] aliases)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("Display name cannot be empty.", nameof(displayName));

            DisplayName = displayName.Trim();
            Aliases = aliases is { Length: > 0 }
                ? aliases.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                : new[] { DisplayName };
        }

        public bool IsPositive(WedgeData wedge)
        {
            foreach (var alias in Aliases)
            {
                if (WedgeDimensionValidator.TryGetDimension(wedge, alias, out var dimension) && dimension!.Nominal.Value > 0m)
                    return true;
            }

            return false;
        }
    }

    internal sealed class DimensionGroup
    {
        public string DisplayName { get; }
        public IReadOnlyList<DimensionSlot> Slots { get; }

        public DimensionGroup(string displayName, params DimensionSlot[] slots)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("Display name cannot be empty.", nameof(displayName));

            DisplayName = displayName.Trim();
            Slots = slots ?? Array.Empty<DimensionSlot>();
        }
    }

    internal sealed class WedgeDimensionValidationRuleSet
    {
        public IReadOnlyList<DimensionSlot> RequiredStandalone { get; init; } = Array.Empty<DimensionSlot>();
        public IReadOnlyList<DimensionGroup> RequiredAndGroups { get; init; } = Array.Empty<DimensionGroup>();
        public IReadOnlyList<DimensionGroup> RequiredOrGroups { get; init; } = Array.Empty<DimensionGroup>();
        public IReadOnlyList<DimensionSlot> OptionalStandalone { get; init; } = Array.Empty<DimensionSlot>();
        public IReadOnlyList<DimensionGroup> ConditionalAndGroups { get; init; } = Array.Empty<DimensionGroup>();
        public IReadOnlyList<DimensionGroup> ConditionalOrGroups { get; init; } = Array.Empty<DimensionGroup>();

        public static WedgeDimensionValidationRuleSet For(WedgeType wedgeType)
        {
            return wedgeType switch
            {
                WedgeType.CKVD => BuildCkvdRules(),
                WedgeType.COB or WedgeType.UTUS or WedgeType.FP => BuildCobLikeRules(),
                _ => new WedgeDimensionValidationRuleSet()
            };
        }

        private static WedgeDimensionValidationRuleSet BuildCkvdRules()
        {
            return new WedgeDimensionValidationRuleSet
            {
                RequiredStandalone = Slots("TL", "TD", "TDF", "FL", "E", "ISA", "W", "F", "FR", "BR", "GD", "B", "FA", "BA", "GA", "GR"),
                RequiredOrGroups = new[]
                {
                    Group("X, XF", Slot("X"), Slot("FX"))
                },
                OptionalStandalone = Slots("TIP"),
                ConditionalAndGroups = new[]
                {
                    Group("VW, VR, VRR, VRA", Slot("VW"), Slot("VR"), Slot("VRR"))
                }
            };
        }

        private static WedgeDimensionValidationRuleSet BuildCobLikeRules()
        {
            return new WedgeDimensionValidationRuleSet
            {
                RequiredStandalone = Slots("TL", "TD", "TDF", "FL", "T", "FD", "RA", "ISA", "W", "BF", "FR", "ERL", "ERW", "ERD", "CA", "HA", "Y", "MB", "FNA"),
                RequiredOrGroups = new[]
                {
                    Group("H, HH", Slot("H"), Slot("HH"))
                },
                OptionalStandalone = Slots("VBL", "BA", "W2", "F", "BR", "BRO", "CL", "FLC", "GO", "FLG", "FLER", "CBL", "C", "MI", "FNO", "T1", "MFL"),
                ConditionalAndGroups = new[]
                {
                    Group("VW, VR, VRR, VRA", Slot("VW"), Slot("VR"), Slot("VRR")),
                    Group("CD, RC/CR", Slot("CD"), Slot("RC/CR", "RC", "CR")),
                    Group("CGD, CGR, G", Slot("CGD"), Slot("CGR"), Slot("G")),
                    Group("CBRD, CBRL, CBRA", Slot("CBRD"), Slot("CBRL"), Slot("CBRA")),
                    Group("B, GR, GA", Slot("B"), Slot("GR"), Slot("GA")),
                    Group("GD, GR", Slot("GD"), Slot("GR"))
                }
            };
        }

        private static DimensionSlot Slot(string key)
            => new(key, key);

        private static DimensionSlot Slot(string displayName, params string[] aliases)
            => new(displayName, aliases);

        private static DimensionSlot[] Slots(params string[] keys)
            => keys.Select(Slot).ToArray();

        private static DimensionGroup Group(string displayName, params DimensionSlot[] slots)
            => new(displayName, slots);
    }
}
