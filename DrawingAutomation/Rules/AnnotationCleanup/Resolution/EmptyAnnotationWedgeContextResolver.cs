using System;

using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

public sealed class EmptyAnnotationWedgeContextResolver :
    IAnnotationWedgeContextResolver
{
    public static EmptyAnnotationWedgeContextResolver Instance { get; } =
        new();

    private EmptyAnnotationWedgeContextResolver()
    {
    }

    public AnnotationWedgeContext Resolve(WedgeData wedge)
    {
        if (wedge is null)
            throw new ArgumentNullException(nameof(wedge));

        return AnnotationWedgeContext.Empty;
    }
}
