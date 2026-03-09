using System;
using System.Collections.Generic;
using System.Linq;
using WAD.Runner.DataManagement.Domain.Wedge;    // WedgeSubclass
using WAD.Runner.DataManagement.Domain.Drawing;  // DrawingType

namespace WAD.Runner.DrawingAutomation.Profiles;

public static class ProfilePresets
{
    private static readonly IReadOnlyList<string> StdOrder =
        new[] { "Front", "Side", "Top", "Detail", "Section" };

    private static readonly ScalePolicy DefaultScale = new(
        FillRatioHeight: 0.80,
        MinScale: 2.0,
        MaxScale: 8.0,
        Step: 0.5,
        TopMarginMm: 0.0,
        BottomMarginMm: 0.0
    );

    private static ViewNames CkvdFgProductionViews() => new(
        Front: "Drawing View49",
        Side: "Drawing View48",
        Top: "Drawing View42",
        Detail: "Drawing View44",
        Section: "Section View V-V"
    );

    private static ViewNames CkvdFgCustomerViews() => new(
        Front: "Drawing View54",
        Side: "Drawing View53",
        Top: "Drawing View50",
        Detail: "Drawing View51",
        Section: "Section View Y-Y"
    );

    private static ViewNames CkvdFgOverlayViews() => new(
        Front: "Drawing View5",
        Side: "Drawing View4",
        Top: "Drawing View3",
        Detail: "Drawing View1",
        Section: "Drawing View2"
    );

    private static ViewNames CkvdPgbProductionViews() => new(
        Front: "Drawing View56",
        Side: "Drawing View55",
        Top: "Drawing View37",
        Detail: "Drawing View46",
        Section: "Section View W-W"
    );

    private static ViewNames CkvdPgbOverlayViews() => new(
        Front: "Drawing View5",
        Side: "Drawing View4",
        Top: "Drawing View3",
        Detail: "Drawing View1",
        Section: "Drawing View2"
    );

    private static ViewNames CobFgProductionViews() => new(
        Front: "Drawing View7",
        Side: "Drawing View6",
        Top: "Drawing View8",
        Detail: "Drawing View9",
        Section: "Section View AF-AF"
    );

    private static ViewNames CobFgCustomerViews() => new(
        Front: "Drawing View2",
        Side: "Drawing View1",
        Top: "Drawing View3",
        Detail: "Drawing View4",
        Section: "Section View AE-AE"
    );

    private static ViewNames CobFgOverlayViews() => new(
        Front: "COB_FG_Ovl_Front",
        Side: "COB_FG_Ovl_Side",
        Top: "COB_FG_Ovl_Top",
        Detail: "Drawing View3",
        Section: "Drawing View4"
    );

    private static ViewNames CobPgbProductionViews() => new(
        Front: "Drawing View12",
        Side: "Drawing View11",
        Top: "Drawing View13",
        Detail: "Drawing View14",
        Section: "Section View AG-AG"
    );

    private static ViewNames CobPgbOverlayViews() => new(
        Front: "COB_PGB_Ovl_Front",
        Side: "COB_PGB_Ovl_Side",
        Top: "COB_PGB_Ovl_Top",
        Detail: "Drawing View1",
        Section: "Drawing View2"
    );

    // OSG7
    private static ViewNames Osg7FgProductionViews() => new(
        Front: "Drawing View3",
        Side: "Drawing View2",
        Top: "Drawing View1",
        Detail: "Drawing View4",
        Section: "Section View A-A"
    );

    private static ViewNames Osg7FgCustomerViews() => new(
        Front: "OSG7_FG_Cust_Front",
        Side: "OSG7_FG_Cust_Side",
        Top: "OSG7_FG_Cust_Top",
        Detail: "OSG7_FG_Cust_Detail",
        Section: "OSG7_FG_Cust_Section"
    );

    private static ViewNames Osg7FgOverlayViews() => new(
        Front: "OSG7_FG_Ovl_Front",
        Side: "OSG7_FG_Ovl_Side",
        Top: "OSG7_FG_Ovl_Top",
        Detail: "OSG7_FG_Ovl_Detail",
        Section: "OSG7_FG_Ovl_Section"
    );

    private static ViewNames Osg7PgbProductionViews() => new(
        Front: "OSG7_PGB_Prod_Front",
        Side: "OSG7_PGB_Prod_Side",
        Top: "OSG7_PGB_Prod_Top",
        Detail: "OSG7_PGB_Prod_Detail",
        Section: "OSG7_PGB_Prod_Section"
    );

    private static ViewNames Osg7PgbOverlayViews() => new(
        Front: "OSG7_PGB_Ovl_Front",
        Side: "OSG7_PGB_Ovl_Side",
        Top: "OSG7_PGB_Ovl_Top",
        Detail: "OSG7_PGB_Ovl_Detail",
        Section: "OSG7_PGB_Ovl_Section"
    );

    private static Func<IEnumerable<string>, string> Prefer(string preferred)
        => (available) =>
        {
            var hit = available?.FirstOrDefault(n =>
                string.Equals(n?.Trim(), preferred, StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(hit) ? (available?.FirstOrDefault() ?? preferred) : hit!;
        };

    private static double Keep(double fb) => fb;

    public static DrawingProfile CkvdFgProduction() => new(
        Key: new DrawingProfileKey(WedgeSubclass.FG, DrawingType.Production),
        ProfileName: "CKVD FG Production",
        SheetSelector: Prefer("PRODUCTION"),
        ViewsOrder: StdOrder,
        Views: CkvdFgProductionViews(),
        UseBreaklinesForView: v => v is "Front" or "Side" or "Detail" or "Section",
        ScaleForView: (_, fb) => Keep(fb),
        Scale: DefaultScale
    );

    public static DrawingProfile CkvdFgCustomer() => new(
        Key: new DrawingProfileKey(WedgeSubclass.FG, DrawingType.Customer),
        ProfileName: "CKVD FG Customer",
        SheetSelector: Prefer("CUSTOMER"),
        ViewsOrder: StdOrder,
        Views: CkvdFgCustomerViews(),
        UseBreaklinesForView: v => v is "Front" or "Side" or "Detail" or "Section",
        ScaleForView: (_, fb) => Keep(fb),
        Scale: DefaultScale
    );

    public static DrawingProfile CkvdFgOverlay() => new(
        Key: new DrawingProfileKey(WedgeSubclass.FG, DrawingType.Overlay),
        ProfileName: "CKVD FG Overlay",
        SheetSelector: Prefer("OVERLAY"),
        ViewsOrder: StdOrder,
        Views: CkvdFgOverlayViews(),
        UseBreaklinesForView: v => false,
        ScaleForView: (_, fb) => Keep(fb),
        Scale: DefaultScale
    );

    public static DrawingProfile CkvdPgbProduction() => new(
        Key: new DrawingProfileKey(WedgeSubclass.PGB, DrawingType.Production),
        ProfileName: "CKVD PGB Production",
        SheetSelector: Prefer("PGB"),
        ViewsOrder: StdOrder,
        Views: CkvdPgbProductionViews(),
        UseBreaklinesForView: v => v is "Front" or "Side" or "Detail" or "Section",
        ScaleForView: (_, fb) => Keep(fb),
        Scale: DefaultScale
    );

    public static DrawingProfile CkvdPgbOverlay() => new(
        Key: new DrawingProfileKey(WedgeSubclass.PGB, DrawingType.Overlay),
        ProfileName: "CKVD PGB Overlay",
        SheetSelector: Prefer("PGB_OVERLAY"),
        ViewsOrder: StdOrder,
        Views: CkvdPgbOverlayViews(),
        UseBreaklinesForView: v => false,
        ScaleForView: (_, fb) => Keep(fb),
        Scale: DefaultScale
    );

    public static DrawingProfile Osg7FgProduction() => new(
        Key: new DrawingProfileKey(WedgeSubclass.FG, DrawingType.Production),
        ProfileName: "OSG7 FG Production",
        SheetSelector: Prefer("PRODUCTION"),
        ViewsOrder: StdOrder,
        Views: Osg7FgProductionViews(),
        UseBreaklinesForView: v => v is "Front" or "Side" or "Detail" or "Section",
        ScaleForView: (_, fb) => Keep(fb),
        Scale: DefaultScale
    );

    public static DrawingProfile Osg7FgCustomer() => new(
        Key: new DrawingProfileKey(WedgeSubclass.FG, DrawingType.Customer),
        ProfileName: "OSG7 FG Customer",
        SheetSelector: Prefer("CUSTOMER"),
        ViewsOrder: StdOrder,
        Views: Osg7FgCustomerViews(),
        UseBreaklinesForView: v => v is "Front" or "Side" or "Detail" or "Section",
        ScaleForView: (_, fb) => Keep(fb),
        Scale: DefaultScale
    );

    public static DrawingProfile Osg7FgOverlay() => new(
        Key: new DrawingProfileKey(WedgeSubclass.FG, DrawingType.Overlay),
        ProfileName: "OSG7 FG Overlay",
        SheetSelector: Prefer("OVERLAY"),
        ViewsOrder: StdOrder,
        Views: Osg7FgOverlayViews(),
        UseBreaklinesForView: v => false,
        ScaleForView: (_, fb) => Keep(fb),
        Scale: DefaultScale
    );

    public static DrawingProfile Osg7PgbProduction() => new(
        Key: new DrawingProfileKey(WedgeSubclass.PGB, DrawingType.Production),
        ProfileName: "OSG7 PGB Production",
        SheetSelector: Prefer("PGB"),
        ViewsOrder: StdOrder,
        Views: Osg7PgbProductionViews(),
        UseBreaklinesForView: v => v is "Front" or "Side" or "Detail" or "Section",
        ScaleForView: (_, fb) => Keep(fb),
        Scale: DefaultScale
    );

    public static DrawingProfile Osg7PgbOverlay() => new(
        Key: new DrawingProfileKey(WedgeSubclass.PGB, DrawingType.Overlay),
        ProfileName: "OSG7 PGB Overlay",
        SheetSelector: Prefer("PGB_OVERLAY"),
        ViewsOrder: StdOrder,
        Views: Osg7PgbOverlayViews(),
        UseBreaklinesForView: v => false,
        ScaleForView: (_, fb) => Keep(fb),
        Scale: DefaultScale
    );

    public static DrawingProfile FgProduction() => CkvdFgProduction();
    public static DrawingProfile FgCustomer() => CkvdFgCustomer();
    public static DrawingProfile FgOverlay() => CkvdFgOverlay();
    public static DrawingProfile PgbProduction() => CkvdPgbProduction();
    public static DrawingProfile PgbOverlay() => CkvdPgbOverlay();

    public static DrawingProfile CobFgProduction() => new(
        Key: new DrawingProfileKey(WedgeSubclass.FG, DrawingType.Production),
        ProfileName: "COB FG Production",
        SheetSelector: Prefer("PRODUCTION"),
        ViewsOrder: StdOrder,
        Views: CobFgProductionViews(),
        UseBreaklinesForView: v => v is "Front" or "Side" or "Detail" or "Section",
        ScaleForView: (_, fb) => Keep(fb),
        Scale: DefaultScale
    );

    public static DrawingProfile CobFgCustomer() => new(
        Key: new DrawingProfileKey(WedgeSubclass.FG, DrawingType.Customer),
        ProfileName: "COB FG Customer",
        SheetSelector: Prefer("CUSTOMER"),
        ViewsOrder: StdOrder,
        Views: CobFgCustomerViews(),
        UseBreaklinesForView: v => v is "Front" or "Side" or "Detail" or "Section",
        ScaleForView: (_, fb) => Keep(fb),
        Scale: DefaultScale
    );

    public static DrawingProfile CobFgOverlay() => new(
        Key: new DrawingProfileKey(WedgeSubclass.FG, DrawingType.Overlay),
        ProfileName: "COB FG Overlay",
        SheetSelector: Prefer("FG"),
        ViewsOrder: StdOrder,
        Views: CobFgOverlayViews(),
        UseBreaklinesForView: v => false,
        ScaleForView: (_, fb) => Keep(fb),
        Scale: DefaultScale
    );

    public static DrawingProfile CobPgbProduction() => new(
        Key: new DrawingProfileKey(WedgeSubclass.PGB, DrawingType.Production),
        ProfileName: "COB PGB Production",
        SheetSelector: Prefer("PGB"),
        ViewsOrder: StdOrder,
        Views: CobPgbProductionViews(),
        UseBreaklinesForView: v => v is "Front" or "Side" or "Detail" or "Section",
        ScaleForView: (_, fb) => Keep(fb),
        Scale: DefaultScale
    );

    public static DrawingProfile CobPgbOverlay() => new(
        Key: new DrawingProfileKey(WedgeSubclass.PGB, DrawingType.Overlay),
        ProfileName: "COB PGB Overlay",
        SheetSelector: Prefer("PGB"),
        ViewsOrder: StdOrder,
        Views: CobPgbOverlayViews(),
        UseBreaklinesForView: v => false,
        ScaleForView: (_, fb) => Keep(fb),
        Scale: DefaultScale
    );
}
