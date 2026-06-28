namespace WAD.Runner.DrawingAutomation.Profiles;

public sealed record ScalePolicy(
    double FillRatioHeight,
    double MinScale,
    double MaxScale,
    double Step,
    double TopMarginMm = 0.0,
    double BottomMarginMm = 0.0
);
