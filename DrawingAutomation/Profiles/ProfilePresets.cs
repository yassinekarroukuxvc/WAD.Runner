using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Profiles;

public static class ProfilePresets
{
    private static readonly IReadOnlyList<string> StandardViewOrder =
        new[] { "Front", "Side", "Top", "Detail", "Section" };

    private static readonly ScalePolicy DefaultScale = new(
        FillRatioHeight: 0.80,
        MinScale: 2.0,
        MaxScale: 8.0,
        Step: 0.5,
        TopMarginMm: 0.0,
        BottomMarginMm: 0.0);

    private static readonly ViewNames CkvdFgProductionViews = new(
        Front: "Drawing View49",
        Side: "Drawing View48",
        Top: "Drawing View42",
        Detail: "Drawing View44",
        Section: "Section View V-V");

    private static readonly ViewNames CkvdFgCustomerViews = new(
        Front: "Drawing View54",
        Side: "Drawing View53",
        Top: "Drawing View50",
        Detail: "Drawing View51",
        Section: "Section View Y-Y");

    private static readonly ViewNames CkvdFgOverlayViews = new(
        Front: "Drawing View5",
        Side: "Drawing View4",
        Top: "Drawing View3",
        Detail: "Drawing View1",
        Section: "Drawing View2");

    private static readonly ViewNames CkvdPgbProductionViews = new(
        Front: "Drawing View56",
        Side: "Drawing View55",
        Top: "Drawing View37",
        Detail: "Drawing View46",
        Section: "Section View W-W");

    private static readonly ViewNames CkvdPgbOverlayViews = CkvdFgOverlayViews;

    private static readonly ViewNames CobLikeFgProductionViews = new(
        Front: "Drawing View17",
        Side: "Drawing View16",
        Top: "Drawing View18",
        Detail: "Drawing View19",
        Section: "Section View AA-AA");

    private static readonly ViewNames CobLikeFgCustomerViews = new(
        Front: "Drawing View2",
        Side: "Drawing View1",
        Top: "Drawing View3",
        Detail: "Drawing View4",
        Section: "Section View AA-AA");

    private static readonly ViewNames CobLikeFgOverlayViews = new(
        Front: "COB_FG_Ovl_Front",
        Side: "COB_FG_Ovl_Side",
        Top: "COB_FG_Ovl_Top",
        Detail: "Drawing View3",
        Section: "Drawing View4");

    private static readonly ViewNames CobLikePgbProductionViews = new(
        Front: "Drawing View22",
        Side: "Drawing View21",
        Top: "Drawing View23",
        Detail: "Drawing View24",
        Section: "Section View AA-AA");

    private static readonly ViewNames CobLikePgbOverlayViews = new(
        Front: "COB_PGB_Ovl_Front",
        Side: "COB_PGB_Ovl_Side",
        Top: "COB_PGB_Ovl_Top",
        Detail: "Drawing View1",
        Section: "Drawing View2");

    private static readonly ViewNames Osg7FgProductionViews = new(
        Front: "Drawing View19",
        Side: "Drawing View17",
        Top: "Drawing View18",
        Detail: "Drawing View20",
        Section: "Section View AC-AC");

    private static readonly ViewNames Osg7FgCustomerViews = new(
        Front: "Drawing View19",
        Side: "Drawing View17",
        Top: "Drawing View18",
        Detail: "Drawing View20",
        Section: "Section View AC-AC");

    private static readonly ViewNames Osg7FgOverlayViews = new(
        Front: "OSG7_FG_Ovl_Front",
        Side: "OSG7_FG_Ovl_Side",
        Top: "OSG7_FG_Ovl_Top",
        Detail: "Drawing View3",
        Section: "Drawing View4");

    private static readonly ViewNames Osg7PgbProductionViews = new(
        Front: "Drawing View14",
        Side: "Drawing View12",
        Top: "Drawing View13",
        Detail: "Drawing View15",
        Section: "Section View AD-AD");

    private static readonly ViewNames Osg7PgbOverlayViews = new(
        Front: "OSG7_PGB_Ovl_Front",
        Side: "OSG7_PGB_Ovl_Side",
        Top: "OSG7_PGB_Ovl_Top",
        Detail: "Drawing View5",
        Section: "Drawing View6");

    public static DrawingProfile CkvdFgProduction()
        => Production(
            WedgeSubclass.FG,
            "CKVD FG Production",
            CkvdFgProductionViews,
            PreferAny("PRODUCTION"));

    public static DrawingProfile CkvdFgCustomer()
        => Customer(
            WedgeSubclass.FG,
            "CKVD FG Customer",
            CkvdFgCustomerViews,
            PreferAny("CUSTOMER"));

    public static DrawingProfile CkvdFgOverlay()
        => Overlay(
            WedgeSubclass.FG,
            "CKVD FG Overlay",
            CkvdFgOverlayViews,
            PreferAny("OVERLAY"));

    public static DrawingProfile CkvdPgbProduction()
        => Production(
            WedgeSubclass.PGB,
            "CKVD PGB Production",
            CkvdPgbProductionViews,
            PreferAny("PGB"));

    public static DrawingProfile CkvdPgbOverlay()
        => Overlay(
            WedgeSubclass.PGB,
            "CKVD PGB Overlay",
            CkvdPgbOverlayViews,
            PreferAny("PGB_OVERLAY"));

    public static DrawingProfile Osg7FgProduction()
        => Production(
            WedgeSubclass.FG,
            "OSG7 FG Production",
            Osg7FgProductionViews,
            PreferAny("CUSTOMER"));//keep it like this because this  is the correct sheet

    public static DrawingProfile Osg7FgCustomer()
        => Customer(
            WedgeSubclass.FG,
            "OSG7 FG Customer",
            Osg7FgCustomerViews,
            PreferAny("CUSTOMER"));

    public static DrawingProfile Osg7FgOverlay()
        => Overlay(
            WedgeSubclass.FG,
            "OSG7 FG Overlay",
            Osg7FgOverlayViews,
            PreferAny("FG"));

    public static DrawingProfile Osg7PgbProduction()
        => Production(
            WedgeSubclass.PGB,
            "OSG7 PGB Production",
            Osg7PgbProductionViews,
            PreferAny("PGB"));

    public static DrawingProfile Osg7PgbOverlay()
        => Overlay(
            WedgeSubclass.PGB,
            "OSG7 PGB Overlay",
            Osg7PgbOverlayViews,
            PreferAny("PGB_OVERLAY"));

    public static DrawingProfile CobFgProduction()
        => CobLikeFgProduction("COB");

    public static DrawingProfile CobFgCustomer()
        => CobLikeFgCustomer("COB");

    public static DrawingProfile CobFgOverlay()
        => CobLikeFgOverlay("COB");

    public static DrawingProfile CobPgbProduction()
        => CobLikePgbProduction("COB");

    public static DrawingProfile CobPgbOverlay()
        => CobLikePgbOverlay("COB");

    public static DrawingProfile UtusFgProduction()
        => CobLikeFgProduction("UTUS");

    public static DrawingProfile UtusFgCustomer()
        => CobLikeFgCustomer("UTUS");

    public static DrawingProfile UtusFgOverlay()
        => CobLikeFgOverlay("UTUS");

    public static DrawingProfile UtusPgbProduction()
        => CobLikePgbProduction("UTUS");

    public static DrawingProfile UtusPgbOverlay()
        => CobLikePgbOverlay("UTUS");

    public static DrawingProfile FpFgProduction()
        => CobLikeFgProduction("FP");

    public static DrawingProfile FpFgCustomer()
        => CobLikeFgCustomer("FP");

    public static DrawingProfile FpFgOverlay()
        => CobLikeFgOverlay("FP");

    public static DrawingProfile FpPgbProduction()
        => CobLikePgbProduction("FP");

    public static DrawingProfile FpPgbOverlay()
        => CobLikePgbOverlay("FP");

    // Compatibility aliases retained for existing call sites outside this folder.
    public static DrawingProfile FgProduction() => CkvdFgProduction();
    public static DrawingProfile FgCustomer() => CkvdFgCustomer();
    public static DrawingProfile FgOverlay() => CkvdFgOverlay();
    public static DrawingProfile PgbProduction() => CkvdPgbProduction();
    public static DrawingProfile PgbOverlay() => CkvdPgbOverlay();

    private static DrawingProfile CobLikeFgProduction(string wedgeName)
        => Production(
            WedgeSubclass.FG,
            $"{wedgeName} FG Production",
            CobLikeFgProductionViews,
            PreferAny("PRODUCTION", "PRODCUTION"));

    private static DrawingProfile CobLikeFgCustomer(string wedgeName)
        => Customer(
            WedgeSubclass.FG,
            $"{wedgeName} FG Customer",
            CobLikeFgCustomerViews,
            PreferAny("CUSTOMER"));

    private static DrawingProfile CobLikeFgOverlay(string wedgeName)
        => Overlay(
            WedgeSubclass.FG,
            $"{wedgeName} FG Overlay",
            CobLikeFgOverlayViews,
            PreferAny("FG"));

    private static DrawingProfile CobLikePgbProduction(string wedgeName)
        => Production(
            WedgeSubclass.PGB,
            $"{wedgeName} PGB Production",
            CobLikePgbProductionViews,
            PreferAny("PGB"));

    private static DrawingProfile CobLikePgbOverlay(string wedgeName)
        => Overlay(
            WedgeSubclass.PGB,
            $"{wedgeName} PGB Overlay",
            CobLikePgbOverlayViews,
            PreferAny("PGB"));

    private static DrawingProfile Production(
        WedgeSubclass subclass,
        string profileName,
        ViewNames views,
        Func<IEnumerable<string>, string> sheetSelector)
        => Create(
            subclass,
            DrawingType.Production,
            profileName,
            views,
            sheetSelector,
            useBreaklines: true);

    private static DrawingProfile Customer(
        WedgeSubclass subclass,
        string profileName,
        ViewNames views,
        Func<IEnumerable<string>, string> sheetSelector)
        => Create(
            subclass,
            DrawingType.Customer,
            profileName,
            views,
            sheetSelector,
            useBreaklines: true);

    private static DrawingProfile Overlay(
        WedgeSubclass subclass,
        string profileName,
        ViewNames views,
        Func<IEnumerable<string>, string> sheetSelector)
        => Create(
            subclass,
            DrawingType.Overlay,
            profileName,
            views,
            sheetSelector,
            useBreaklines: false);

    private static DrawingProfile Create(
        WedgeSubclass subclass,
        DrawingType drawingType,
        string profileName,
        ViewNames views,
        Func<IEnumerable<string>, string> sheetSelector,
        bool useBreaklines)
        => new(
            Key: new DrawingProfileKey(subclass, drawingType),
            ProfileName: profileName,
            SheetSelector: sheetSelector,
            ViewsOrder: StandardViewOrder,
            Views: views,
            UseBreaklinesForView: useBreaklines
                ? IsBreaklineView
                : _ => false,
            ScaleForView: (_, fallback) => fallback,
            Scale: DefaultScale);

    private static bool IsBreaklineView(string logicalView)
        => logicalView is "Front" or "Side" or "Detail" or "Section";

    private static Func<IEnumerable<string>, string> PreferAny(params string[] preferredNames)
        => available =>
        {
            var sheets = (available ?? Array.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();

            if (sheets.Length == 0)
                throw new InvalidOperationException("The drawing contains no selectable sheets.");

            foreach (var preferred in preferredNames)
            {
                var match = sheets.FirstOrDefault(name =>
                    string.Equals(
                        name.Trim(),
                        preferred,
                        StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(match))
                    return match;
            }

            return sheets[0];
        };
}
