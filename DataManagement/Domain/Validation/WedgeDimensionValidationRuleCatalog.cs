using System.Linq;

using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Validation;

internal static class WedgeDimensionValidationRuleCatalog
{
    public static WedgeDimensionValidationRuleSet For(
        WedgeType wedgeType)
    {
        return wedgeType switch
        {
            WedgeType.CKVD => BuildCkvdRules(),
            WedgeType.OSG7 => BuildOsg7Rules(),

            WedgeType.COB or
            WedgeType.UTUS or
            WedgeType.FP => BuildCobLikeRules(),

            WedgeType._4516 => Build4516Rules(),
            WedgeType.ABT => BuildAbtRules(),
            WedgeType.AB16 => BuildAb16Rules(),
            WedgeType._45CK => Build45CkRules(),
            WedgeType.M => BuildMRules(),
            WedgeType._1001 => Build1001Rules(),

            _ => WedgeDimensionValidationRuleSet.Empty
        };
    }
    private static WedgeDimensionValidationRuleSet
        Build1001Rules()
    {
        return new WedgeDimensionValidationRuleSet
        {
            RequiredStandalone =
                Slots(
                    "TL",
                    "TD",
                    "TDF",
                    "W",
                    "ISA",
                    "FD",
                    "T",
                    "RA",
                    "BA",
                    "FRO",
                    "C",
                    "FTA",
                    "ND",
                    "NR",
                    "NA",
                    "FL"),

            ConditionalAndGroups =
                new[]
                {
                    Group(
                        "VR, VRA, VW, VRR",
                        Slot("VR"),
                        Slot("VRA"),
                        Slot("VW"),
                        Slot("VRR")),

                    Group(
                        "VBL, VBLR",
                        Slot("VBL"),
                        Slot("VBLR")),

                    Group(
                        "RA2, RA2H",
                        Slot("RA2"),
                        Slot("RA2H"))
                }
        };
    }

    private static WedgeDimensionValidationRuleSet
        BuildMRules()
    {
        return new WedgeDimensionValidationRuleSet
        {
            RequiredStandalone =
                Slots(
                    "TL",
                    "TD",
                    "TDF",
                    "W",
                    "ISA",
                    "FD",
                    "T",
                    "RA",
                    "BA",
                    "FRO",
                    "C",
                    "FTA",
                    "ND",
                    "NR",
                    "NA",
                    "FL"),

            ConditionalAndGroups =
                new[]
                {
                    Group(
                        "VR, VRA, VW, VRR",
                        Slot("VR"),
                        Slot("VRA"),
                        Slot("VW"),
                        Slot("VRR")),

                    Group(
                        "VBL, VBLR",
                        Slot("VBL"),
                        Slot("VBLR")),

                    Group(
                        "RA2, RA2H",
                        Slot("RA2"),
                        Slot("RA2H"))
                }
        };
    }
    private static WedgeDimensionValidationRuleSet
        Build45CkRules()
    {
        return new WedgeDimensionValidationRuleSet
        {
            RequiredStandalone =
                Slots(
                    "TL",
                    "TD",
                    "TDF",
                    "W",
                    "ISA",
                    "FD",
                    "T",
                    "RA",
                    "BA",
                    "FRO",
                    "ERL",
                    "ERD",
                    "ERW",
                    "CA",
                    "FL"),

            ConditionalAndGroups =
                new[]
                {
                    Group(
                        "VR, VRA, VW, VRR",
                        Slot("VR"),
                        Slot("VRA"),
                        Slot("VW"),
                        Slot("VRR")),

                    Group(
                        "VBL, VBLR",
                        Slot("VBL"),
                        Slot("VBLR")),

                    Group(
                        "RA2, RA2H",
                        Slot("RA2"),
                        Slot("RA2H")),

                    Group(
                        "B, GA, GD",
                        Slot("B"),
                        Slot("GA"),
                        Slot("GD")),

                    Group(
                        "CGO, CGR, G",
                        Slot("CGO"),
                        Slot("CGR"),
                        Slot("G")),

                    Group(
                        "HW, HH",
                        Slot("HW"),
                        Slot("HH")),

                    Group(
                        "ST, SW",
                        Slot("ST"),
                        Slot("SW"))
                }
        };
    }

    private static WedgeDimensionValidationRuleSet
        BuildAb16Rules()
    {
        return new WedgeDimensionValidationRuleSet
        {
            RequiredStandalone =
                Slots(
                    "TL",
                    "TD",
                    "TDF",
                    "W",
                    "ISA",
                    "FD",
                    "T",
                    "RA",
                    "BA",
                    "FRO",
                    "ERL",
                    "ERD",
                    "ERW",
                    "CA",
                    "FL"),

            ConditionalAndGroups =
                new[]
                {
                    Group(
                        "VR, VRA, VW, VRR",
                        Slot("VR"),
                        Slot("VRA"),
                        Slot("VW"),
                        Slot("VRR")),

                    Group(
                        "VBL, VBLR",
                        Slot("VBL"),
                        Slot("VBLR")),

                    Group(
                        "RA2, RA2H",
                        Slot("RA2"),
                        Slot("RA2H")),

                    Group(
                        "B, GA, GD",
                        Slot("B"),
                        Slot("GA"),
                        Slot("GD")),

                    Group(
                        "CGO, CGR, G",
                        Slot("CGO"),
                        Slot("CGR"),
                        Slot("G")),

                    Group(
                        "HW, HH",
                        Slot("HW"),
                        Slot("HH")),

                    Group(
                        "ST, SW",
                        Slot("ST"),
                        Slot("SW"))
                }
        };
    }

    private static WedgeDimensionValidationRuleSet
        BuildAbtRules()
    {
        return new WedgeDimensionValidationRuleSet
        {
            RequiredStandalone =
                Slots(
                    "TL",
                    "TD",
                    "TDF",
                    "W",
                    "ISA",
                    "FD",
                    "T",
                    "RA",
                    "BA",
                    "FRO",
                    "ERL",
                    "ERD",
                    "ERW",
                    "CA",
                    "FL"),

            ConditionalAndGroups =
                new[]
                {
                    Group(
                        "VR, VRA, VW, VRR",
                        Slot("VR"),
                        Slot("VRA"),
                        Slot("VW"),
                        Slot("VRR")),

                    Group(
                        "VBL, VBLR",
                        Slot("VBL"),
                        Slot("VBLR")),

                    Group(
                        "RA2, RA2H",
                        Slot("RA2"),
                        Slot("RA2H"))
                }
        };
    }

    private static WedgeDimensionValidationRuleSet
        Build4516Rules()
    {
        /*
         * The required 4516 dimensions depend on the resolved values of:
         *
         * - Wed-Feed_H/Slot
         * - Wed-Foot_Option
         *
         * Wedge4516PropertyResolver resolves/normalizes the properties first.
         * Wedge4516ConditionalDimensionValidator then validates the dimensions
         * required by the selected feed-hole type and foot option.
         */
        return WedgeDimensionValidationRuleSet.Empty;
    }

    private static WedgeDimensionValidationRuleSet
        BuildCkvdRules()
    {
        return new WedgeDimensionValidationRuleSet
        {
            RequiredStandalone =
                Slots(
                    "TL",
                    "TD",
                    "TDF",
                    "FL",
                    "E",
                    "ISA",
                    "W",
                    "F",
                    "FR",
                    "BR",
                    "GD",
                    "B",
                    "FA",
                    "BA",
                    "GA",
                    "GR"),

            RequiredOrGroups =
                new[]
                {
                    Group(
                        "X, FX",
                        Slot("X"),
                        Slot("FX"))
                },

            OptionalStandalone =
                Slots(
                    "TIP"),

            ConditionalAndGroups =
                new[]
                {
                    Group(
                        "VW, VR, VRR, VRA",
                        Slot("VW"),
                        Slot("VR"),
                        Slot("VRR"))
                }
        };
    }

    private static WedgeDimensionValidationRuleSet
        BuildOsg7Rules()
    {
        return new WedgeDimensionValidationRuleSet
        {
            RequiredStandalone =
                Slots(
                    "TL",
                    "TD",
                    "TDF",
                    "FL",
                    "ISA",
                    "W",
                    "F",
                    "FR",
                    "BR",
                    "GD",
                    "B",
                    "FA",
                    "BA",
                    "GA"),

            RequiredOrGroups =
                new[]
                {
                    Group(
                        "X, FX",
                        Slot("X"),
                        Slot("FX"))
                },

            OptionalStandalone =
                Slots(
                    "VFL",
                    "FRX",
                    "BRX"),

            ConditionalAndGroups =
                new[]
                {
                    Group(
                        "VW, VR, VRR, VRA",
                        Slot("VW"),
                        Slot("VR"),
                        Slot("VRR"))
                }
        };
    }

    private static WedgeDimensionValidationRuleSet
        BuildCobLikeRules()
    {
        return new WedgeDimensionValidationRuleSet
        {
            RequiredStandalone =
                Slots(
                    "TL",
                    "TD",
                    "TDF",
                    "FL",
                    "T",
                    "FD",
                    "RA",
                    "ISA",
                    "W",
                    "BF",
                    "FR",
                    "ERL",
                    "ERW",
                    "ERD",
                    "CA",
                    "HA",
                    "Y",
                    "MB",
                    "FNA"),

            RequiredOrGroups =
                new[]
                {
                    Group(
                        "H, HH",
                        Slot("H"),
                        Slot("HH"))
                },

            OptionalStandalone =
                Slots(
                    "VBL",
                    "BA",
                    "W2",
                    "F",
                    "BR",
                    "BRO",
                    "CL",
                    "FLC",
                    "GO",
                    "FLG",
                    "FLER",
                    "CBL",
                    "C",
                    "MI",
                    "FNO",
                    "T1",
                    "MFL"),

            ConditionalAndGroups =
                new[]
                {
                    Group(
                        "VW, VR, VRR, VRA",
                        Slot("VW"),
                        Slot("VR"),
                        Slot("VRR")),

                    Group(
                        "CD, RC/CR",
                        Slot("CD"),
                        Slot(
                            "RC/CR",
                            "RC",
                            "CR")),

                    Group(
                        "CGD, CGR, G",
                        Slot("CGD"),
                        Slot("CGR"),
                        Slot("G")),

                    Group(
                        "CBRD, CBRL, CBRA",
                        Slot("CBRD"),
                        Slot("CBRL"),
                        Slot("CBRA")),

                    Group(
                        "B, GR, GA",
                        Slot("B"),
                        Slot("GR"),
                        Slot("GA")),

                    Group(
                        "GD, GR",
                        Slot("GD"),
                        Slot("GR"))
                }
        };
    }

    private static DimensionSlot Slot(
        string key)
        => new(key, key);

    private static DimensionSlot Slot(
        string displayName,
        params string[] aliases)
        => new(displayName, aliases);

    private static DimensionSlot[] Slots(
        params string[] keys)
        => keys.Select(Slot).ToArray();

    private static DimensionGroup Group(
        string displayName,
        params DimensionSlot[] slots)
        => new(displayName, slots);
}