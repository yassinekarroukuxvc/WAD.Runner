using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Profiles;

public static class DrawingProfileFactory
{
    public static readonly ScalePolicy DefaultScalePolicy = new(
        FillRatioHeight: 0.80,
        MinScale: 2.0,
        MaxScale: 8.0,
        Step: 0.5,
        TopMarginMm: 0.0,
        BottomMarginMm: 0.0);

    public static DrawingProfile Create(
        WedgeType wedgeType,
        WedgeSubclass subclass,
        DrawingType drawingType,
        string profileName,
        ViewNames views,
        IEnumerable<string> preferredSheets,
        IReadOnlySet<string>? breaklineViews = null,
        ScalePolicy? scale = null)
    {
        if (string.IsNullOrWhiteSpace(profileName))
            throw new ArgumentException("A profile name is required.", nameof(profileName));

        return new DrawingProfile(
            new DrawingProfileKey(wedgeType, subclass, drawingType),
            profileName.Trim(),
            PreferAny(preferredSheets),
            views,
            breaklineViews ?? DrawingViewNames.StandardBreaklineViews,
            scale ?? DefaultScalePolicy);
    }

    private static Func<IEnumerable<string>, string> PreferAny(
        IEnumerable<string> preferredNames)
    {
        var preferred = (preferredNames ?? Array.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToArray();

        return available =>
        {
            var sheets = (available ?? Array.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();

            if (sheets.Length == 0)
                throw new InvalidOperationException("The drawing contains no selectable sheets.");

            foreach (var candidate in preferred)
            {
                var match = sheets.FirstOrDefault(name =>
                    string.Equals(name.Trim(), candidate, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(match))
                    return match;
            }

            return sheets[0];
        };
    }
}
