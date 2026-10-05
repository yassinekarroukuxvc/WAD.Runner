using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Core;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

public static class WedgePropertyReader
{
    // ================================================================
    // EXISTING GENERAL READERS
    // ================================================================

    public static string? GetPropLoose(
        WedgeData wedge,
        string key)
        => wedge is null
            ? null
            : new DrawingWedgeFacts(wedge).GetProperty(key);

    public static string? GetSubclassPropLoose(
        WedgeData wedge,
        string pgbKey,
        string fgKey,
        params string[] fgAliases)
        => wedge is null
            ? null
            : new DrawingWedgeFacts(wedge).GetSubclassProperty(
                pgbKey,
                fgKey,
                fgAliases);

    public static string? GetFirstPropLoose(
        WedgeData wedge,
        params string[] keys)
    {
        if (wedge is null ||
            keys is null)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;

            var value =
                GetPropLoose(
                    wedge,
                    key);

            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    // ================================================================
    // EXPLICIT SUBCLASS READERS
    // ================================================================

    /// <summary>
    /// Reads an FG property using ONLY the supplied FG property names.
    ///
    /// Do not put PGB-* names here.
    /// Do not put generic wedge_type / Wedge-Type aliases here when
    /// resolving a shank, because those represent the wedge family
    /// (UTUS, ABT, COB, etc.), not the shank orientation.
    /// </summary>
    public static string? GetFirstFgPropLoose(
        WedgeData wedge,
        params string[] keys)
    {
        return GetFirstPropLoose(
            wedge,
            keys);
    }

    /// <summary>
    /// Reads a PGB property using ONLY the supplied PGB property names.
    ///
    /// This intentionally does not fall back to Wed-* properties.
    /// PGB data must remain independent from FG data.
    /// </summary>
    public static string? GetFirstPgbPropLoose(
        WedgeData wedge,
        params string[] keys)
    {
        return GetFirstPropLoose(
            wedge,
            keys);
    }

    // ================================================================
    // COMMON SUBCLASS HELPERS
    // ================================================================

    /// <summary>
    /// Explicit shank/type property reader.
    ///
    /// FG  -> Wed-Type
    /// PGB -> PGB-Type
    ///
    /// There is deliberately no cross-subclass fallback.
    /// </summary>
    public static string? GetShankTypeProp(
        WedgeData wedge)
    {
        if (wedge is null)
            return null;

        return wedge.Subclass switch
        {
            WedgeSubclass.PGB =>
                GetFirstPgbPropLoose(
                    wedge,
                    "PGB-Type",
                    "PGB_Type",
                    "PGB Type"),

            WedgeSubclass.FG =>
                GetFirstFgPropLoose(
                    wedge,
                    "Wed-Type",
                    "Wed_Type",
                    "Wed Type"),

            _ => null
        };
    }

    /// <summary>
    /// FG-only foot-option reader.
    ///
    /// PGB intentionally returns null because PGB does not have
    /// a foot-option property.
    /// </summary>
    public static string? GetFgFootOptionProp(
        WedgeData wedge)
    {
        if (wedge is null ||
            wedge.Subclass != WedgeSubclass.FG)
        {
            return null;
        }

        return GetFirstFgPropLoose(
            wedge,
            "Wed-Foot_Option",
            "Wed_Foot_Option",
            "Wed Foot Option",
            "Wed-Foot Option",
            "Foot_Option",
            "Foot Option",
            "FootOption",
            "foot_option");
    }

    /// <summary>
    /// FG-only feed-hole reader.
    ///
    /// PGB intentionally returns null because PGB does not have
    /// a feed-hole property.
    /// </summary>
    public static string? GetFgFeedHoleProp(
        WedgeData wedge)
    {
        if (wedge is null ||
            wedge.Subclass != WedgeSubclass.FG)
        {
            return null;
        }

        return GetFirstFgPropLoose(
            wedge,
            "Wed-Feed_H/Slot",
            "Wed_Feed_H_Slot",
            "Wed Feed H Slot",
            "Wed-Feed H Slot",
            "Feed_H/Slot",
            "Feed_H_Slot",
            "Feed H Slot",
            "feed_h_slot");
    }
}