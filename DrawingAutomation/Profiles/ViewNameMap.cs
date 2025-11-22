using System;
using System.Collections.Generic;
using WAD.Runner.DataManagement.Domain.Wedge; // WedgeSubclass
using WAD.Runner.DataManagement.Domain.Drawing; // DrawingType

namespace WAD.Runner.DrawingAutomation.Views
{
    /// <summary>
    /// Central mapping from logical keys ("Front","Side","Top","Detail","Section")
    /// to actual view names used inside each template sheet for (Subclass × DrawingType).
    /// </summary>
    public static class ViewNameMap
    {
        public static IDictionary<string, string> Get(WedgeSubclass sub, DrawingType type)
            => (sub, type) switch
            {
                // FG — PRODUCTION
                (WedgeSubclass.FG, DrawingType.Production) => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Front"] = "Drawing View49",
                    ["Side"] = "Drawing View48",
                    ["Top"] = "Drawing View42",
                    ["Detail"] = "Drawing View44",
                    ["Section"] = "Section View V-V"
                },

                // FG — CUSTOMER
                (WedgeSubclass.FG, DrawingType.Customer) => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Front"] = "Drawing View54",
                    ["Side"] = "Drawing View53",
                    ["Top"] = "Drawing View50",
                    ["Detail"] = "Drawing View51",
                    ["Section"] = "Section View Y-Y"
                },

                // FG — OVERLAY
                (WedgeSubclass.FG, DrawingType.Overlay) => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Front"] = "Drawing View5",
                    ["Side"] = "Drawing View4",
                    ["Top"] = "Drawing View3",
                    ["Detail"] = "Drawing View1",
                    ["Section"] = "Drawing View2"
                },

                // PGB — PRODUCTION
                (WedgeSubclass.PGB, DrawingType.Production) => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Front"] = "Drawing View56",
                    ["Side"] = "Drawing View55",
                    ["Top"] = "Drawing View37",
                    ["Detail"] = "Drawing View46",
                    ["Section"] = "Section View W-W"
                },

                // Fallback = logical names equal actual names
                _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Front"] = "Front",
                    ["Side"] = "Side",
                    ["Top"] = "Top",
                    ["Detail"] = "Detail",
                    ["Section"] = "Section"
                }
            };
    }
}
