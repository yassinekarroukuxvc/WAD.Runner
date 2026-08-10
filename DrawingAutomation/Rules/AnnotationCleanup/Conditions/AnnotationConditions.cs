using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Conditions;

public static class AnnotationConditions
{
    public static IAnnotationCondition Always()
        => new AlwaysCondition();

    public static IAnnotationCondition DimPresent(string key)
        => new DimensionPresentCondition(key);

    public static IAnnotationCondition DimPositive(string key)
        => new DimensionPositiveCondition(key);

    public static IAnnotationCondition DimsPositive(params string[] keys)
        => new AllDimensionsPositiveCondition(keys);

    public static IAnnotationCondition All(
        params IAnnotationCondition[] conditions)
        => new AllCondition(conditions);

    public static IAnnotationCondition Any(
        params IAnnotationCondition[] conditions)
        => new AnyCondition(conditions);

    public static IAnnotationCondition Not(
        IAnnotationCondition condition)
        => new NotCondition(condition);

    public static IAnnotationCondition TraitIs(
        string traitName,
        params string[] acceptedValues)
        => new TraitCondition(
            traitName,
            acceptedValues);

    public static IAnnotationCondition FootIs(string foot)
        => TraitIs(
            AnnotationTraitNames.FootOption,
            foot);

    public static IAnnotationCondition FootIn(
        params string[] feet)
        => TraitIs(
            AnnotationTraitNames.FootOption,
            feet);

    public static IAnnotationCondition ShankIs(string shank)
        => TraitIs(
            AnnotationTraitNames.ShankType,
            shank);

    public static IAnnotationCondition WedTypeIs(
        params string[] acceptedTokens)
        => TraitIs(
            AnnotationTraitNames.WedType,
            acceptedTokens);

    public static IAnnotationCondition FeedHoleIs(
        params string[] acceptedTokens)
        => TraitIs(
            AnnotationTraitNames.FeedHoleType,
            acceptedTokens);

    private sealed class AlwaysCondition : IAnnotationCondition
    {
        public bool IsMatch(AnnotationCleanupContext ctx)
            => true;

        public string Describe()
            => "always";
    }

    private sealed class DimensionPresentCondition : IAnnotationCondition
    {
        private readonly string _key;

        public DimensionPresentCondition(string key)
            => _key = key;

        public bool IsMatch(AnnotationCleanupContext ctx)
            => ctx.Dimensions.IsPresent(_key);

        public string Describe()
            => $"{_key} is present";
    }

    private sealed class DimensionPositiveCondition : IAnnotationCondition
    {
        private readonly string _key;

        public DimensionPositiveCondition(string key)
            => _key = key;

        public bool IsMatch(AnnotationCleanupContext ctx)
            => ctx.Dimensions.IsPositive(_key);

        public string Describe()
            => $"{_key} > 0";
    }

    private sealed class AllDimensionsPositiveCondition :
        IAnnotationCondition
    {
        private readonly string[] _keys;

        public AllDimensionsPositiveCondition(
            IEnumerable<string> keys)
            => _keys = (keys ?? Array.Empty<string>())
                .ToArray();

        public bool IsMatch(AnnotationCleanupContext ctx)
            => ctx.Dimensions.ArePositive(_keys);

        public string Describe()
            => string.Join(
                " and ",
                _keys.Select(key => $"{key} > 0"));
    }

    private sealed class AllCondition : IAnnotationCondition
    {
        private readonly IAnnotationCondition[] _conditions;

        public AllCondition(
            IEnumerable<IAnnotationCondition> conditions)
            => _conditions = (conditions ??
                Array.Empty<IAnnotationCondition>())
                .ToArray();

        public bool IsMatch(AnnotationCleanupContext ctx)
            => _conditions.All(condition =>
                condition.IsMatch(ctx));

        public string Describe()
            => string.Join(
                " and ",
                _conditions.Select(condition =>
                    $"({condition.Describe()})"));
    }

    private sealed class AnyCondition : IAnnotationCondition
    {
        private readonly IAnnotationCondition[] _conditions;

        public AnyCondition(
            IEnumerable<IAnnotationCondition> conditions)
            => _conditions = (conditions ??
                Array.Empty<IAnnotationCondition>())
                .ToArray();

        public bool IsMatch(AnnotationCleanupContext ctx)
            => _conditions.Any(condition =>
                condition.IsMatch(ctx));

        public string Describe()
            => string.Join(
                " or ",
                _conditions.Select(condition =>
                    $"({condition.Describe()})"));
    }

    private sealed class NotCondition : IAnnotationCondition
    {
        private readonly IAnnotationCondition _condition;

        public NotCondition(IAnnotationCondition condition)
            => _condition = condition ??
                throw new ArgumentNullException(nameof(condition));

        public bool IsMatch(AnnotationCleanupContext ctx)
            => !_condition.IsMatch(ctx);

        public string Describe()
            => $"not ({_condition.Describe()})";
    }

    private sealed class TraitCondition : IAnnotationCondition
    {
        private readonly string _traitName;
        private readonly string[] _acceptedValues;

        public TraitCondition(
            string traitName,
            IEnumerable<string> acceptedValues)
        {
            if (string.IsNullOrWhiteSpace(traitName))
            {
                throw new ArgumentException(
                    "Trait name is required.",
                    nameof(traitName));
            }

            _traitName = traitName.Trim();
            _acceptedValues = (acceptedValues ??
                Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(AnnotationTokenNormalizer.Normalize)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public bool IsMatch(AnnotationCleanupContext ctx)
            => ctx.Traits.IsAny(
                _traitName,
                _acceptedValues);

        public string Describe()
            => $"{_traitName} in " +
               $"[{string.Join(", ", _acceptedValues)}]";
    }
}
