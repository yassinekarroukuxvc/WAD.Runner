using System;
using System.Collections.Generic;

namespace WAD.Runner.DrawingAutomation.Profiles;

public static class DrawingViewNames
{
    public const string Front = "Front";
    public const string Side = "Side";
    public const string Top = "Top";
    public const string Detail = "Detail";
    public const string Section = "Section";

    public static readonly IReadOnlyList<string> Primary =
        Array.AsReadOnly(new[] { Front, Side, Top });

    public static readonly IReadOnlyList<string> FixedScale =
        Array.AsReadOnly(new[] { Detail, Section });

    public static readonly IReadOnlyList<string> LayoutOrder =
        Array.AsReadOnly(new[] { Front, Side, Top, Detail, Section });

    public static readonly IReadOnlySet<string> StandardBreaklineViews =
        new HashSet<string>(
            new[] { Front, Side, Detail, Section },
            StringComparer.OrdinalIgnoreCase);

    public static readonly IReadOnlySet<string> SecondaryBreaklineViews =
        new HashSet<string>(
            new[] { Detail, Section },
            StringComparer.OrdinalIgnoreCase);

    public static readonly IReadOnlySet<string> NoBreaklineViews =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
