using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

public interface IAnnotationWedgeContextResolver
{
    AnnotationWedgeContext Resolve(WedgeData wedge);
}
