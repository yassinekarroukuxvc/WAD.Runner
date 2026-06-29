
using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Tables
{


    public static class DimensionTableKeyFilter
    {


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


            return new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        }


        private static readonly Dictionary<WedgeType, Dictionary<WedgeSubclass, Dictionary<DrawingType, string[]>>> _rules
            = new()
            {
                [WedgeType.COB] = new Dictionary<WedgeSubclass, Dictionary<DrawingType, string[]>>()
                {

                    [WedgeSubclass.FG] = new Dictionary<DrawingType, string[]>()
                    {
                        [DrawingType.Production] = new[]
                        {
                            "T","F",
                            "FD","FL",
                            "H",
                            "VBL",
                            "RC","CD","GR","GD",
                            "B","BF",
                            "G",
                            "VR",
                            "W","VW","FR","BR","ERW",
                            "TL","TD","TDF",
                            "VFL",
                            "Y",
                            "ERW"
                        },

                        [DrawingType.Customer] = new[]
                        {
                            "T",
                            "H",
                            "VBL",
                            "B","BF",
                            "G",
                            "VR",
                            "W","VW","W2","FR","BR","TD","TDF","TL"
                        },

                        [DrawingType.Overlay] = new[]
                        {
                            "TL", "TD", "TDF", "W", "ISA",
                            "K",
                            "RA", "T",
                            "BA"
                        }
                    },


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

                    [WedgeSubclass.FG] = new Dictionary<DrawingType, string[]>()
                    {
                        [DrawingType.Production] = new[]
                        {
                            "T","F",
                            "FD","FL",
                            "H",
                            "VBL",
                            "RC","CD","GR","GD",
                            "B","BF",
                            "G",
                            "VR",
                            "W","VW","FR","BR","ERW",
                            "TL","TD","TDF",
                            "VFL",
                            "Y",
                            "ERW"
                        },

                        [DrawingType.Customer] = new[]
                        {
                            "T",
                            "H",
                            "VBL",
                            "B","BF",
                            "G",
                            "VR",
                            "W","VW","W2","FR","BR","TD","TDF","TL"
                        },

                        [DrawingType.Overlay] = new[]
                        {
                            "TL", "TD", "TDF", "W", "ISA",
                            "K",
                            "RA", "T",
                            "BA"
                        }
                    },


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

                    [WedgeSubclass.FG] = new Dictionary<DrawingType, string[]>()
                    {
                        [DrawingType.Production] = new[]
                        {
                            "T","F",
                            "FD","FL",
                            "H",
                            "VBL",
                            "RC","CD","GR","GD",
                            "B","BF",
                            "G",
                            "VR",
                            "W","VW","FR","BR","ERW",
                            "TL","TD","TDF",
                            "VFL",
                            "Y",
                            "ERW"
                        },

                        [DrawingType.Customer] = new[]
                        {
                            "T",
                            "H",
                            "VBL",
                            "B","BF",
                            "G",
                            "VR",
                            "W","VW","W2","FR","BR","TD","TDF","TL"
                        },

                        [DrawingType.Overlay] = new[]
                        {
                            "TL", "TD", "TDF", "W", "ISA",
                            "K",
                            "RA", "T",
                            "BA"
                        }
                    },


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

                [WedgeType.CKVD] = new Dictionary<WedgeSubclass, Dictionary<DrawingType, string[]>>()
                {

                    [WedgeSubclass.FG] = new Dictionary<DrawingType, string[]>()
                    {
                        [DrawingType.Production] = new[]
                        {
                            "FL","F","B","GD","GR",
                            "VR","W","VW",
                            "FR","BR",
                            "FX","X","E"
                        },

                        [DrawingType.Customer] = new[]
                        {
                            "FL","F","B","GD","GR",
                            "VR","W","VW",
                            "FR","BR",
                            "FX","X","E"
                        },

                        [DrawingType.Overlay] = new[]
                        {
                            "TL", "TD", "TDF", "W", "ISA",
                            "K","BA"
                        }
                    },


                    [WedgeSubclass.PGB] = new Dictionary<DrawingType, string[]>()
                    {
                        [DrawingType.Production] = new[]
                        {
                            "W", "ISA",
                            "T", "FL"
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
