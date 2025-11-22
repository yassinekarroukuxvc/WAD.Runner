namespace WAD.Runner.DrawingAutomation.Profiles;
public readonly record struct ViewNames(string Front, string Side, string Top, string Detail, string Section);

/// <summary>
/// Pure profile data + tiny policies (delegates) so executors don’t hard-code things.
/// </summary>
public sealed record DrawingProfile(
    DrawingProfileKey Key,
    string ProfileName,

    // Sheet selection
    Func<IEnumerable<string>, string> SheetSelector,

    // Logical order the executor should process views in.
    IReadOnlyList<string> ViewsOrder,

    // Actual view names present in the template.
    ViewNames Views,

    // Whether breaklines should be applied for a given logical view key.
    Func<string, bool> UseBreaklinesForView,

    // Scale policy per logical view key. Caller passes a fallback scale (kept for future uses).
    Func<string, double, double> ScaleForView,

    ScalePolicy Scale
);
