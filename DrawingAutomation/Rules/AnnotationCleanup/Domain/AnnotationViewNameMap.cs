using System;
using System.Collections.Generic;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

public sealed class AnnotationViewNameMap
{
    public string Front { get; init; } = "Front View";
    public string Side { get; init; } = "Side View";
    public string Top { get; init; } = "Top View";
    public string Detail { get; init; } = "Detail View";
    public string Section { get; init; } = "Section View";

    public string Resolve(AnnotationView view) => view switch
    {
        AnnotationView.Front => Front,
        AnnotationView.Side => Side,
        AnnotationView.Top => Top,
        AnnotationView.Detail => Detail,
        AnnotationView.Section => Section,
        _ => throw new ArgumentOutOfRangeException(nameof(view), view, null)
    };

    public IEnumerable<string> AllNominalNames()
        => new[] { Front, Side, Top, Detail, Section };
}
