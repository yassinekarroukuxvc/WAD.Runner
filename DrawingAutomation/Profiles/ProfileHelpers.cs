using WAD.Runner.DrawingAutomation.Views;

namespace WAD.Runner.DrawingAutomation.Profiles;

internal static class ProfileHelpers
{
    public static IDictionary<string, string> ToNameMap(DrawingProfile profile)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));

        var v = profile.Views;
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Front"] = v.Front,
            ["Side"] = v.Side,
            ["Top"] = v.Top,
            ["Detail"] = v.Detail,
            ["Section"] = v.Section
        };
    }

    public static ViewAutoScaleService.Policy ToAutoScalePolicy(ScalePolicy p)
        => new(
            FillRatioHeight: p.FillRatioHeight,
            MinScale: p.MinScale,
            MaxScale: p.MaxScale,
            Step: p.Step,
            TopMarginMm: p.TopMarginMm,
            BottomMarginMm: p.BottomMarginMm
        );
}
