using System;
using System.Collections.Generic;
using System.Linq;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Conditions;

public static class AnnotationConditions
{
    public static IAnnotationCondition Always() => new AlwaysCondition();
    public static IAnnotationCondition DimPositive(string key) => new DimensionPositiveCondition(key);
    public static IAnnotationCondition DimsPositive(params string[] keys) => new AllDimensionsPositiveCondition(keys);
    public static IAnnotationCondition All(params IAnnotationCondition[] conditions) => new AllCondition(conditions);
    public static IAnnotationCondition Any(params IAnnotationCondition[] conditions) => new AnyCondition(conditions);
    public static IAnnotationCondition Not(IAnnotationCondition condition) => new NotCondition(condition);
    public static IAnnotationCondition FootIs(FootOption foot) => new FootCondition(new[] { foot });
    public static IAnnotationCondition FootIn(params FootOption[] feet) => new FootCondition(feet);
    public static IAnnotationCondition ShankIs(ShankType shank) => new ShankCondition(shank);

    private sealed class AlwaysCondition : IAnnotationCondition
    {
        public bool IsMatch(AnnotationCleanupContext ctx) => true;
        public string Describe() => "always";
    }

    private sealed class DimensionPositiveCondition : IAnnotationCondition
    {
        private readonly string _key;
        public DimensionPositiveCondition(string key) => _key = key;
        public bool IsMatch(AnnotationCleanupContext ctx) => ctx.Dimensions.IsPositive(_key);
        public string Describe() => $"{_key} > 0";
    }

    private sealed class AllDimensionsPositiveCondition : IAnnotationCondition
    {
        private readonly string[] _keys;
        public AllDimensionsPositiveCondition(IEnumerable<string> keys) => _keys = (keys ?? Array.Empty<string>()).ToArray();
        public bool IsMatch(AnnotationCleanupContext ctx) => ctx.Dimensions.ArePositive(_keys);
        public string Describe() => string.Join(" and ", _keys.Select(k => $"{k} > 0"));
    }

    private sealed class AllCondition : IAnnotationCondition
    {
        private readonly IAnnotationCondition[] _conditions;
        public AllCondition(IEnumerable<IAnnotationCondition> conditions) => _conditions = (conditions ?? Array.Empty<IAnnotationCondition>()).ToArray();
        public bool IsMatch(AnnotationCleanupContext ctx) => _conditions.All(c => c.IsMatch(ctx));
        public string Describe() => string.Join(" and ", _conditions.Select(c => $"({c.Describe()})"));
    }

    private sealed class AnyCondition : IAnnotationCondition
    {
        private readonly IAnnotationCondition[] _conditions;
        public AnyCondition(IEnumerable<IAnnotationCondition> conditions) => _conditions = (conditions ?? Array.Empty<IAnnotationCondition>()).ToArray();
        public bool IsMatch(AnnotationCleanupContext ctx) => _conditions.Any(c => c.IsMatch(ctx));
        public string Describe() => string.Join(" or ", _conditions.Select(c => $"({c.Describe()})"));
    }

    private sealed class NotCondition : IAnnotationCondition
    {
        private readonly IAnnotationCondition _condition;
        public NotCondition(IAnnotationCondition condition) => _condition = condition ?? throw new ArgumentNullException(nameof(condition));
        public bool IsMatch(AnnotationCleanupContext ctx) => !_condition.IsMatch(ctx);
        public string Describe() => $"not ({_condition.Describe()})";
    }

    private sealed class FootCondition : IAnnotationCondition
    {
        private readonly HashSet<FootOption> _feet;
        public FootCondition(IEnumerable<FootOption> feet) => _feet = new HashSet<FootOption>(feet ?? Array.Empty<FootOption>());
        public bool IsMatch(AnnotationCleanupContext ctx) => _feet.Contains(ctx.Foot);
        public string Describe() => $"foot in [{string.Join(", ", _feet)}]";
    }

    private sealed class ShankCondition : IAnnotationCondition
    {
        private readonly ShankType _shank;
        public ShankCondition(ShankType shank) => _shank = shank;
        public bool IsMatch(AnnotationCleanupContext ctx) => ctx.Shank == _shank;
        public string Describe() => $"shank is {_shank}";
    }
}
