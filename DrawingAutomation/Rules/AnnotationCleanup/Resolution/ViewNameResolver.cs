using System.Collections.Generic;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

public static class ViewNameResolver
{
    public static AnnotationViewNameMap Resolve(IDictionary<string, string>? nameMap)
        => new()
        {
            Front = Resolve(nameMap, "Front"),
            Side = Resolve(nameMap, "Side"),
            Top = Resolve(nameMap, "Top"),
            Detail = Resolve(nameMap, "Detail"),
            Section = Resolve(nameMap, "Section")
        };

    private static string Resolve(IDictionary<string, string>? nameMap, string logical)
    {
        if (nameMap != null && nameMap.TryGetValue(logical, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
            return mapped.Trim();
        return logical;
    }
}
