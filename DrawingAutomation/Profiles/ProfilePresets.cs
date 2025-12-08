using System;
using System.Collections.Generic;
using System.Linq;
using WAD.Runner.DataManagement.Domain.Wedge;    // WedgeSubclass
using WAD.Runner.DataManagement.Domain.Drawing;  // DrawingType

namespace WAD.Runner.DrawingAutomation.Profiles;

public static class ProfilePresets
{
    // default processing order
    private static readonly IReadOnlyList<string> StdOrder =
        new[] { "Front", "Side", "Top", "Detail", "Section" };

    // Default unified autoscale policy (same as you had in the executor)
    private static readonly ScalePolicy DefaultScale = new(
        FillRatioHeight: 0.80,
        MinScale: 2.0,
        MaxScale: 8.0,
        Step: 0.5,
        TopMarginMm: 0.0,
        BottomMarginMm: 0.0
    );

    // ── CKVD: actual view names per your mapping ─────────────────────────
    private static ViewNames ForFgProduction() => new(
        Front: "Drawing View49",
        Side: "Drawing View48",
        Top: "Drawing View42",
        Detail: "Drawing View44",
        Section: "Section View V-V"
    );

    private static ViewNames ForFgCustomer() => new(
        Front: "Drawing View54",
        Side: "Drawing View53",
        Top: "Drawing View50",
        Detail: "Drawing View51",
        Section: "Section View Y-Y"
    );

    private static ViewNames ForFgOverlay() => new(
        Front: "Drawing View5",
        Side: "Drawing View4",
        Top: "Drawing View3",
        Detail: "Drawing View1",
        Section: "Drawing View2"
    );

    private static ViewNames ForPgbProduction() => new(
        Front: "Drawing View56",
        Side: "Drawing View55",
        Top: "Drawing View37",
        Detail: "Drawing View46",
        Section: "Section View W-W"
    );

    // You didn’t provide CKVD names for PGB Overlay yet; keep placeholders for now.
    private static ViewNames ForPgbOverlay() => new(
        Front: "Drawing View5",
        Side: "Drawing View4",
        Top: "Drawing View3",
        Detail: "Drawing View1",
        Section: "Drawing View2"
    );

    // Sheet selector helpers (simple + safe).
    // We match case-insensitively and fall back to the first sheet if the preferred name does not exist.
    private static Func<IEnumerable<string>, string> Prefer(string preferred)
        => (available) =>
        {
            var hit = available?.FirstOrDefault(n =>
                string.Equals(n?.Trim(), preferred, StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(hit) ? (available?.FirstOrDefault() ?? preferred) : hit!;
        };

    // Simple scale policy: return the fallback for now (unified autoscale drives the final scale).
    private static double Keep(double fb) => fb;

    // ── Profiles ─────────────────────────────────────────────────────────

    // FG — Production
    public static DrawingProfile FgProduction() => new(
        Key: new DrawingProfileKey(WedgeSubclass.FG, DrawingType.Production),
        ProfileName: "FG Production",
        SheetSelector: Prefer("PRODUCTION"),
        ViewsOrder: StdOrder,
        Views: ForFgProduction(),
        UseBreaklinesForView: v => v is "Front" or "Side" or "Detail" or "Section",
        ScaleForView: (_, fb) => Keep(fb),
        Scale: DefaultScale
    );

    // FG — Customer
    public static DrawingProfile FgCustomer() => new(
        Key: new DrawingProfileKey(WedgeSubclass.FG, DrawingType.Customer),
        ProfileName: "FG Customer",
        SheetSelector: Prefer("CUSTOMER"),
        ViewsOrder: StdOrder,
        Views: ForFgCustomer(),
        UseBreaklinesForView: v => v is "Front" or "Side" or "Detail" or "Section",
        ScaleForView: (_, fb) => Keep(fb),
        Scale: DefaultScale
    );

    // FG — Overlay
    public static DrawingProfile FgOverlay() => new(
        Key: new DrawingProfileKey(WedgeSubclass.FG, DrawingType.Overlay),
        ProfileName: "FG Overlay",
        SheetSelector: Prefer("OVERLAY"),
        ViewsOrder: StdOrder,
        Views: ForFgOverlay(),
        UseBreaklinesForView: v => false,                    // typically none for overlay
        ScaleForView: (_, fb) => Keep(fb),
        Scale: DefaultScale
    );

    // PGB — Production
    public static DrawingProfile PgbProduction() => new(
        Key: new DrawingProfileKey(WedgeSubclass.PGB, DrawingType.Production),
        ProfileName: "PGB Production",
        SheetSelector: Prefer("PGB"),
        ViewsOrder: StdOrder,
        Views: ForPgbProduction(),
        UseBreaklinesForView: v => v is "Front" or "Side" or "Detail" or "Section",
        ScaleForView: (_, fb) => Keep(fb),
        Scale: DefaultScale
    );

    // PGB — Overlay (pending exact CKVD names)
    public static DrawingProfile PgbOverlay() => new(
        Key: new DrawingProfileKey(WedgeSubclass.PGB, DrawingType.Overlay),
        ProfileName: "PGB Overlay",
        SheetSelector: Prefer("PGB_OVERLAY"),
        ViewsOrder: StdOrder,
        Views: ForPgbOverlay(),
        UseBreaklinesForView: v => false,
        ScaleForView: (_, fb) => Keep(fb),
        Scale: DefaultScale
    );
}
