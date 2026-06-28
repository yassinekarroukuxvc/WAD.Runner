using System.Collections.Generic;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;

public interface IAnnotationRuleCatalog
{
    AnnotationCleanupProfile Profile { get; }
    IReadOnlyList<AnnotationKeepRule> Rules { get; }
}
