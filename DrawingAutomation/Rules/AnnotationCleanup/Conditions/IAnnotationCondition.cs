using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Conditions;

public interface IAnnotationCondition
{
    bool IsMatch(AnnotationCleanupContext ctx);
    string Describe();
}
