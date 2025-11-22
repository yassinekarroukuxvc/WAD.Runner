namespace WAD.Runner.DrawingAutomation.SolidWorks;

public sealed record DeleteSheetsResult(
    bool Ok,
    string KeepSheet,
    IReadOnlyList<string> Deleted,
    IReadOnlyList<string> NotDeleted
);
