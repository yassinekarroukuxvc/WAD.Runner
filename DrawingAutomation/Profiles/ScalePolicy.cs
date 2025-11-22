namespace WAD.Runner.DrawingAutomation.Profiles;

public sealed record ScalePolicy(
    double FillRatioHeight,   // e.g., 0.80
    double MinScale,          // e.g., 2.0
    double MaxScale,          // e.g., 8.0
    double Step,              // e.g., 0.5
    double TopMarginMm = 0.0,
    double BottomMarginMm = 0.0
);
