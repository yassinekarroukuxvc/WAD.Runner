using System;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

public sealed class AnnotationNameTemplate
{
    private readonly Func<AnnotationCleanupContext, string> _resolver;

    public AnnotationNameTemplate(string pattern)
    {
        Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
        _resolver = ctx => ResolvePattern(pattern, ctx);
    }

    private AnnotationNameTemplate(string displayPattern, Func<AnnotationCleanupContext, string> resolver)
    {
        Pattern = displayPattern;
        _resolver = resolver;
    }

    public string Pattern { get; }

    public string Resolve(AnnotationCleanupContext ctx) => _resolver(ctx).Trim();

    public static AnnotationNameTemplate WithOptionalOverride(
        Func<AnnotationCleanupContext, string?> overrideSelector,
        string fallbackPattern)
    {
        if (overrideSelector is null) throw new ArgumentNullException(nameof(overrideSelector));
        if (string.IsNullOrWhiteSpace(fallbackPattern)) throw new ArgumentException("Fallback pattern is required.", nameof(fallbackPattern));

        return new AnnotationNameTemplate(
            fallbackPattern,
            ctx =>
            {
                var overrideName = overrideSelector(ctx);
                return string.IsNullOrWhiteSpace(overrideName)
                    ? ResolvePattern(fallbackPattern, ctx)
                    : overrideName.Trim();
            });
    }

    private static string ResolvePattern(string pattern, AnnotationCleanupContext ctx)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));

        return pattern
            .Replace("{FrontSketch}", ctx.Sketches.FrontSketch)
            .Replace("{TopSketch}", ctx.Sketches.TopSketch)
            .Replace("{FrBrSketch}", ctx.Sketches.FrBrSketch)
            .Replace("{CgDeg180TypoSketch}", ctx.Sketches.CgDeg180TypoSketch);
    }
}
