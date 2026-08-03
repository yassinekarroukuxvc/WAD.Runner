using System;
using System.Collections.Generic;

namespace WAD.Runner.DrawingAutomation.Profiles;

public readonly record struct ViewNames(
    string Front,
    string Side,
    string Top,
    string Detail,
    string Section)
{
    public IDictionary<string, string> ToLogicalMap()
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [DrawingViewNames.Front] = Front,
            [DrawingViewNames.Side] = Side,
            [DrawingViewNames.Top] = Top,
            [DrawingViewNames.Detail] = Detail,
            [DrawingViewNames.Section] = Section
        };
}

public sealed record DrawingProfile(
    DrawingProfileKey Key,
    string ProfileName,
    Func<IEnumerable<string>, string> SheetSelector,
    ViewNames Views,
    IReadOnlySet<string> BreaklineViews,
    ScalePolicy Scale)
{
    public bool UsesBreakline(string logicalView)
        => !string.IsNullOrWhiteSpace(logicalView) &&
           BreaklineViews.Contains(logicalView);
}
