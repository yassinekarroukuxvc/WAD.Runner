using System.Globalization;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Planning;

internal static class NoteRules
{
    /// <summary>
    /// Builds CAD-agnostic notes from DrawingData.Metadata, WedgeData.Properties, and WedgeData.Marking.
    /// Positions are simple defaults with light heuristics; you can later route them via JSON config.
    /// </summary>
    public static List<NoteSpec> Build(LayoutContext ctx)
    {
        var notes = new List<NoteSpec>();

        // ---- anchor helpers (use table position if available, else fallback) ----
        var howToOrderPos = TryGetTablePos(ctx, "HowToOrder", fallbackX: 10, fallbackY: 10);
        var markingPos = TryGetTablePos(ctx, "Marking", fallbackX: 10, fallbackY: 30);
        var titlePos = new double[] { 10.0, 6.0 };

        // ==== 1) Title from Drawing.Metadata ====
        var title = FirstNonEmpty(
            TryMeta(ctx.Drawing, "Title"),
            TryMeta(ctx.Drawing, "DrawingTitle"),
            TryMeta(ctx.Drawing, "TitleText")
        );

        if (!string.IsNullOrWhiteSpace(title))
        {
            notes.Add(new NoteSpec
            {
                Id = "Title",
                PositionMm = titlePos,
                Text = title!
            });
        }

        // ==== 2) Overlay calibration (from Drawing.Metadata) ====
        var calib = FirstNonEmpty(
            TryMeta(ctx.Drawing, "OverlayCalibrationMicron"),
            TryMeta(ctx.Drawing, "CalibrationMicron"),
            TryMeta(ctx.Drawing, "Overlay_Calibration")
        );
        if (!string.IsNullOrWhiteSpace(calib))
        {
            notes.Add(new NoteSpec
            {
                Id = "OverlayCalibration",
                PositionMm = Offset(markingPos, dx: 0, dy: -8),
                Text = $"Calibration: {calib} µm"
            });
        }

        // ==== 3) Wedge properties (Polish / PS / Remarks / Notes) ====
        // We look for common keys across Wed/PGB specs.
        AddIfPresent(notes, "Polish", ctx.Wedge, howToOrderPos, yStep: 6,
            "Wed-Polish", "PGB-Polish", "Polish");

        AddIfPresent(notes, "PS", ctx.Wedge, howToOrderPos, yStep: 6,
            "Wed-PS", "PGB-PS", "PS");

        AddIfPresent(notes, "Remarks", ctx.Wedge, howToOrderPos, yStep: 6,
            "PGB-Remarks", "Wed-Remarks", "Remarks");

        AddIfPresent(notes, "Notes", ctx.Wedge, howToOrderPos, yStep: 6,
            "Wed-Notes", "Notes");

        // ==== 4) Marking block (Wed-Marking) ====
        if (ctx.Wedge.Marking is not null)
        {
            double y = markingPos[1];

            if (!string.IsNullOrWhiteSpace(ctx.Wedge.Marking.Overlay))
            {
                notes.Add(new NoteSpec
                {
                    Id = "Marking-Overlay",
                    PositionMm = new[] { markingPos[0], y },
                    Text = ctx.Wedge.Marking.Overlay.Trim()
                });
                y += 6;
            }

            // TB-1..TB-7 if present
            var tbs = new[]
            {
                ("Marking-TB-1", ctx.Wedge.Marking.TB1),
                ("Marking-TB-2", ctx.Wedge.Marking.TB2),
                ("Marking-TB-3", ctx.Wedge.Marking.TB3),
                ("Marking-TB-4", ctx.Wedge.Marking.TB4),
                ("Marking-TB-5", ctx.Wedge.Marking.TB5),
                ("Marking-TB-6", ctx.Wedge.Marking.TB6),
                ("Marking-TB-7", ctx.Wedge.Marking.TB7)
            };

            foreach (var (id, text) in tbs)
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    notes.Add(new NoteSpec
                    {
                        Id = id,
                        PositionMm = new[] { markingPos[0], y },
                        Text = text!.Trim()
                    });
                    y += 6;
                }
            }

            if (!string.IsNullOrWhiteSpace(ctx.Wedge.Marking.Text))
            {
                notes.Add(new NoteSpec
                {
                    Id = "Marking-Text",
                    PositionMm = new[] { markingPos[0], y },
                    Text = ctx.Wedge.Marking.Text.Trim()
                });
            }
        }

        return notes;
    }

    // ---------------- helpers ----------------

    private static string? TryMeta(DrawingData d, string key)
        => d.Metadata.TryGetValue(key, out var v) ? v : null;

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v!.Trim();
        }
        return null;
    }

    private static double[] TryGetTablePos(LayoutContext ctx, string logicalName, double fallbackX, double fallbackY)
    {
        if (ctx.Drawing.Tables.TryGetValue(logicalName, out var t) && t.PositionMm is { Length: 2 })
            return new[] { t.PositionMm[0], t.PositionMm[1] };
        return new[] { fallbackX, fallbackY };
    }

    private static double[] Offset(double[] pos, double dx, double dy)
        => new[] { pos[0] + dx, pos[1] + dy };

    /// <summary>
    /// Adds a note if any of the provided property keys exist and are non-empty.
    /// Places it under the anchor (x,yStart) and advances y by yStep for each note.
    /// </summary>
    private static void AddIfPresent(
        List<NoteSpec> notes,
        string label,
        WedgeData wedge,
        double[] anchor,
        double yStep,
        params string[] keys)
    {
        var text = FirstNonEmpty(keys.Select(k => TryProp(wedge, k)).ToArray());
        if (string.IsNullOrWhiteSpace(text)) return;

        // Put under anchor; stack vertically if multiple calls happen on the same anchor
        var y = NextY(notes, anchor, yStep);
        notes.Add(new NoteSpec
        {
            Id = label,
            PositionMm = new[] { anchor[0], y },
            Text = $"{label}: {text!.Trim()}"
        });
    }

    private static string? TryProp(WedgeData w, string key)
        => w.Properties.TryGetValue(key, out var v) ? v : null;

    private static double NextY(List<NoteSpec> notes, double[] anchor, double yStep)
    {
        // Find the last note at this anchor X and below/near anchor Y to keep a tidy stack.
        var sameColumn = notes
            .Where(n => NearlyEqual(n.PositionMm[0], anchor[0]))
            .OrderBy(n => n.PositionMm[1])
            .ToList();

        if (sameColumn.Count == 0)
            return anchor[1];

        var lastY = sameColumn.Last().PositionMm[1];
        return lastY + yStep;
    }

    private static bool NearlyEqual(double a, double b)
        => Math.Abs(a - b) <= 0.0001;
}
