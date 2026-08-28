using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DataManagement.Domain.Validation.Rules._4516;
using WAD.Runner.DataManagement.Domain.Validation.Rules.ABT;
using WAD.Runner.DataManagement.Domain.Validation.Rules.AB16;
using WAD.Runner.DataManagement.Domain.Validation.Rules.COB;
using WAD.Runner.DataManagement.Domain.Validation.Rules._45CK;
using WAD.Runner.DataManagement.Domain.Validation.Rules.OSG7;
using WAD.Runner.DataManagement.Domain.Validation.Rules.M;
using WAD.Runner.DataManagement.Domain.Validation.Rules._1001;

namespace WAD.Runner.DataManagement.Domain.Validation;

public static class WedgeDimensionValidator
{
    /// <summary>
    /// Validates a wedge and applies wedge-specific inferred properties.
    ///
    /// For WedgeType._4516 this method can modify WedgeData.Properties:
    /// - normalize/infer Wed-Feed_H/Slot;
    /// - normalize/infer Wed-Foot_Option.
    /// </summary>
    public static DimensionValidationResult Validate(
        WedgeData wedge,
        WedgeType wedgeType)
    {
        if (wedge is null)
            throw new ArgumentNullException(nameof(wedge));

        var issues =
            new List<DimensionValidationIssue>();

        ApplyWedgeSpecificPropertyResolution(
            wedge,
            wedgeType,
            issues);

        var ruleSet =
            WedgeDimensionValidationRuleCatalog.For(
                wedgeType);

        if (wedge.Subclass == WedgeSubclass.FG)
        {
            ValidateRequiredStandalone(
                wedge,
                wedgeType,
                ruleSet.RequiredStandalone,
                issues);

            ValidateRequiredAndGroups(
                wedge,
                wedgeType,
                ruleSet.RequiredAndGroups,
                issues);

            ValidateRequiredOrGroups(
                wedge,
                wedgeType,
                ruleSet.RequiredOrGroups,
                issues);
        }

        ValidateConditionalAndGroups(
            wedge,
            wedgeType,
            ruleSet.ConditionalAndGroups,
            issues);

        ValidateConditionalOrGroups(
            wedge,
            wedgeType,
            ruleSet.ConditionalOrGroups,
            issues);

        if (wedgeType == WedgeType.COB)
        {
            CobConditionalDimensionValidator.Validate(
                wedge,
                wedgeType,
                issues);
        }

        if (wedgeType == WedgeType._4516)
        {
            Wedge4516ConditionalDimensionValidator.Validate(
                wedge,
                wedgeType,
                issues);
        }

        if (wedgeType == WedgeType.ABT)
        {
            AbtConditionalDimensionValidator.Validate(
                wedge,
                wedgeType,
                issues);
        }

        if (wedgeType == WedgeType.AB16)
        {
            Ab16ConditionalDimensionValidator.Validate(
                wedge,
                wedgeType,
                issues);
        }

        if (wedgeType == WedgeType._45CK)
        {
            _45CKConditionalDimensionValidator.Validate(
                wedge,
                wedgeType,
                issues);
        }

        if (wedgeType == WedgeType.OSG7)
        {
            Osg7CrossDimensionValidator.Validate(
                wedge,
                wedgeType,
                issues);
        }

        if (wedgeType == WedgeType.M)
        {
            MConditionalDimensionValidator.Validate(
                wedge,
                wedgeType,
                issues);
        }

        if (wedgeType == WedgeType._1001)
        {
            _1001ConditionalDimensionValidator.Validate(
                wedge,
                wedgeType,
                issues);
        }

        return new DimensionValidationResult(
            wedge.ArticleNumber,
            wedgeType,
            issues);
    }

    public static void ValidateOrThrow(
        WedgeData wedge,
        WedgeType wedgeType)
    {
        var result =
            Validate(
                wedge,
                wedgeType);

        if (!result.IsValid)
        {
            throw new WedgeDimensionValidationException(
                result);
        }
    }

    private static void ApplyWedgeSpecificPropertyResolution(
        WedgeData wedge,
        WedgeType wedgeType,
        List<DimensionValidationIssue> issues)
    {
        if (wedgeType != WedgeType._4516)
            return;

        issues.AddRange(
            Wedge4516PropertyResolver.ResolveAndApply(
                wedge,
                wedgeType));
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
                BuildMissingOrInvalidMessage(
                    wedge,
                    slot,
                    "must be > 0"));
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
            foreach (var slot in group.Slots.Where(
                         slot => !slot.IsPositive(wedge)))
            {
                AddIssue(
                    wedge,
                    wedgeType,
                    issues,
                    "Type 1-2 Required AND group",
                    group.DisplayName,
                    slot.DisplayName,
                    BuildMissingOrInvalidMessage(
                        wedge,
                        slot,
                        "is required by group " +
                        $"[{group.DisplayName}] and must be > 0"));
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
            if (group.Slots.Any(
                    slot => slot.IsPositive(wedge)))
            {
                continue;
            }

            AddIssue(
                wedge,
                wedgeType,
                issues,
                "Type 1-3 Required OR group",
                group.DisplayName,
                group.DisplayName,
                $"at least one of [{group.DisplayName}] must be " +
                "present and > 0");
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
            var positiveSlots =
                group.Slots
                    .Where(
                        slot => slot.IsPositive(wedge))
                    .ToList();

            if (positiveSlots.Count == 0)
                continue;

            foreach (var slot in group.Slots.Where(
                         slot => !slot.IsPositive(wedge)))
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
                        "must be > 0 because conditional group " +
                        $"[{group.DisplayName}] is active. " +
                        "Triggered by: " +
                        string.Join(
                            ", ",
                            positiveSlots.Select(
                                item => item.DisplayName))));
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
            var positiveSlots =
                group.Slots
                    .Where(
                        slot => slot.IsPositive(wedge))
                    .ToList();

            if (positiveSlots.Count <= 1)
                continue;

            AddIssue(
                wedge,
                wedgeType,
                issues,
                "Type 2-3 Conditional OR group",
                group.DisplayName,
                string.Join(
                    ", ",
                    positiveSlots.Select(
                        item => item.DisplayName)),
                $"at most one of [{group.DisplayName}] is expected " +
                "to be > 0. Found: " +
                string.Join(
                    ", ",
                    positiveSlots.Select(
                        item => item.DisplayName)));
        }
    }

    private static string BuildMissingOrInvalidMessage(
        WedgeData wedge,
        DimensionSlot slot,
        string suffix)
    {
        var presentAliases =
            slot.Aliases
                .Where(
                    alias =>
                        WedgeDimensionAccess.TryGetDimension(
                            wedge,
                            alias,
                            out _))
                .ToList();

        if (presentAliases.Count == 0)
            return $"missing; {suffix}";

        var values =
            presentAliases
                .Select(
                    alias =>
                        WedgeDimensionAccess.TryGetDimension(
                            wedge,
                            alias,
                            out var dimension)
                            ? $"{alias}={dimension!.Nominal.Value}"
                            : alias)
                .ToList();

        return
            $"invalid ({string.Join(", ", values)}); {suffix}";
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
        issues.Add(
            new DimensionValidationIssue(
                wedge.ArticleNumber,
                wedgeType,
                requirementType,
                ruleName,
                dimension,
                message));
    }
}