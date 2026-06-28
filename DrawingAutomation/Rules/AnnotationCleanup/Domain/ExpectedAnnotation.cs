namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

public sealed record ExpectedAnnotation(
    AnnotationView View,
    string FullName,
    string RuleId,
    string? Reason);
