namespace WAD.Runner.DrawingAutomation.Profiles;
public readonly record struct ViewNames(string Front, string Side, string Top, string Detail, string Section);


public sealed record DrawingProfile(
    DrawingProfileKey Key,
    string ProfileName,


    Func<IEnumerable<string>, string> SheetSelector,


    IReadOnlyList<string> ViewsOrder,


    ViewNames Views,


    Func<string, bool> UseBreaklinesForView,


    Func<string, double, double> ScaleForView,

    ScalePolicy Scale
);
