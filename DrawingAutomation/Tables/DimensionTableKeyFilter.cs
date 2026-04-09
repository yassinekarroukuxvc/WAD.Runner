// DrawingAutomation/Tables/DimensionTableKeyFilter.cs
using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge; // WedgeType, WedgeSubclass, DrawingType

namespace WAD.Runner.DrawingAutomation.Tables
{
    /// <summary>
    /// Static whitelist: which dimension KEYS are allowed to appear in the Dimension Table.
    ///
    /// - If a wedge type is NOT registered => no filtering (table shows all non-zero, as today).
    /// - If a wedge type IS registered but a specific (subclass/drawingType) is missing => no filtering.
    ///
    /// Important: TableService still skips dimensions whose nominal is 0 (this file doesn't change that).
    /// </summary>
    public static class DimensionTableKeyFilter
    {
        /// <summary>
        /// Returns a whitelist of allowed dimension keys for the given wedge type + drawing type + subclass.
        /// Returns null => "no whitelist, do not filter".
        /// </summary>
        public static HashSet<string>? GetAllowedKeys(
            WedgeType wedgeType,
            DrawingType drawingType,
            WedgeSubclass subclass)
        {
            if (!_rules.TryGetValue(wedgeType, out var bySubclass))
                return null;

            if (!bySubclass.TryGetValue(subclass, out var byDrawingType))
                return null;

            if (!byDrawingType.TryGetValue(drawingType, out var keys))
                return null;

            // Return a copy for safety
            return new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        }

        // --------------------------------------------------------------------
        // Static rules:
        // wedgeType -> subclass(FG/PGB) -> drawingType(Production/Customer/Overlay) -> keys[]
        // --------------------------------------------------------------------

        private static readonly Dictionary<WedgeType, Dictionary<WedgeSubclass, Dictionary<DrawingType, string[]>>> _rules
            = new()
            {
                [WedgeType.COB] = new Dictionary<WedgeSubclass, Dictionary<DrawingType, string[]>>()
                {
                    // FG: Production + Customer + Overlay
                    [WedgeSubclass.FG] = new Dictionary<DrawingType, string[]>()
                    {
                        [DrawingType.Production] = new[]
                        {
                            //"TL", "TD", "TDF",
                            //"K",
                            "T","F",
                            "FD", "FL",
                            //"FRO",
                            "ERL",
                            //"ERW",
                            "H",
                            //"HA",
                            "VBL",
                            //"CA",
                            "RC", "CD", "GR", "GD",
                            //"GA",
                            "B","BF",
                            "G",
                            //"CGD", "CGR", "CFD",
                            //"CBRA",
                            "CBRL",
                            //"BA","ISA","RA",
                            "VR",
                            //"VRA",
                            "W","VW","W2","FR","BR","ERD",
                        },

                        [DrawingType.Customer] = new[]
                        {
                            "T","F",
                            "FD", "FL",
                            "ERL",
                            "H",
                            "VBL",
                            "RC", "CD", "GR", "GD",
                            "B","BF",
                            "G",
                            "CBRL",
                            "VR",
                            "W","VW","W2","FR","BR","ERD",
                        },

                        [DrawingType.Overlay] = new[]
                        {
                            "TL", "TD", "TDF", "W", "ISA",
                            "K",
                            "RA", "T",
                            "BA"
                        }
                    },

                    // PGB: Production + Overlay
                    [WedgeSubclass.PGB] = new Dictionary<DrawingType, string[]>()
                    {
                        [DrawingType.Production] = new[]
                        {
                            "W", "ISA",
                            "T", "FD"
                        },

                        [DrawingType.Overlay] = new[]
                        {
                            "TL", "TD", "TDF", "W", "ISA",
                            "K",
                            "BA"
                        }
                    }
                },

                [WedgeType.UTUS] = new Dictionary<WedgeSubclass, Dictionary<DrawingType, string[]>>()
                {
                    // UTUS = same as COB
                    [WedgeSubclass.FG] = new Dictionary<DrawingType, string[]>()
                    {
                        [DrawingType.Production] = new[]
                        {
                            //"TL", "TD", "TDF",
                            //"K",
                            "T","F",
                            "FD", "FL",
                            //"FRO",
                            "ERL",
                            //"ERW",
                            "H",
                            //"HA",
                            "VBL",
                            //"CA",
                            "RC", "CD", "GR", "GD",
                            //"GA",
                            "B","BF",
                            "G",
                            //"CGD", "CGR", "CFD",
                            //"CBRA",
                            "CBRL",
                            //"BA","ISA","RA",
                            "VR",
                            //"VRA",
                            "W","VW","W2","FR","BR","ERD",
                        },

                        [DrawingType.Customer] = new[]
                        {
                            "T","F",
                            "FD", "FL",
                            "ERL",
                            "H",
                            "VBL",
                            "RC", "CD", "GR", "GD",
                            "B","BF",
                            "G",
                            "CBRL",
                            "VR",
                            "W","VW","W2","FR","BR","ERD",
                        },

                        [DrawingType.Overlay] = new[]
                        {
                            "TL", "TD", "TDF", "W", "ISA",
                            "K",
                            "RA", "T",
                            "BA"
                        }
                    },

                    // PGB = same as COB
                    [WedgeSubclass.PGB] = new Dictionary<DrawingType, string[]>()
                    {
                        [DrawingType.Production] = new[]
                        {
                            "W", "ISA",
                            "T", "FD"
                        },

                        [DrawingType.Overlay] = new[]
                        {
                            "TL", "TD", "TDF", "W", "ISA",
                            "K",
                            "BA"
                        }
                    }
                },

                [WedgeType.FP] = new Dictionary<WedgeSubclass, Dictionary<DrawingType, string[]>>()
                {
                    // FP = same as COB
                    [WedgeSubclass.FG] = new Dictionary<DrawingType, string[]>()
                    {
                        [DrawingType.Production] = new[]
                        {
                            //"TL", "TD", "TDF",
                            //"K",
                            "T","F",
                            "FD", "FL",
                            //"FRO",
                            "ERL",
                            //"ERW",
                            "H",
                            //"HA",
                            "VBL",
                            //"CA",
                            "RC", "CD", "GR", "GD",
                            //"GA",
                            "B","BF",
                            "G",
                            //"CGD", "CGR", "CFD",
                            //"CBRA",
                            "CBRL",
                            //"BA","ISA","RA",
                            "VR",
                            //"VRA",
                            "W","VW","W2","FR","BR","ERD",
                        },

                        [DrawingType.Customer] = new[]
                        {
                            "T","F",
                            "FD", "FL",
                            "ERL",
                            "H",
                            "VBL",
                            "RC", "CD", "GR", "GD",
                            "B","BF",
                            "G",
                            "CBRL",
                            "VR",
                            "W","VW","W2","FR","BR","ERD",
                        },

                        [DrawingType.Overlay] = new[]
                        {
                            "TL", "TD", "TDF", "W", "ISA",
                            "K",
                            "RA", "T",
                            "BA"
                        }
                    },

                    // PGB = same as COB
                    [WedgeSubclass.PGB] = new Dictionary<DrawingType, string[]>()
                    {
                        [DrawingType.Production] = new[]
                        {
                            "W", "ISA",
                            "T", "FD"
                        },

                        [DrawingType.Overlay] = new[]
                        {
                            "TL", "TD", "TDF", "W", "ISA",
                            "K",
                            "BA"
                        }
                    }
                },
            };
    }
}