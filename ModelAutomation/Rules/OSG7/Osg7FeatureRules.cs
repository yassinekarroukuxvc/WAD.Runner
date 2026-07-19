using System;
using System.Collections.Generic;
using System.Linq;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Core;
using WAD.Runner.ModelAutomation.Execution;

namespace WAD.Runner.ModelAutomation.Rules.OSG7;

public sealed class Osg7FeatureRules : IFeatureRuleSet
{
    /*
     * These features are required for every OSG7 model,
     * regardless of subclass or drawing type.
     */
    private static readonly string[] AlwaysOnNames =
    {
        "FRA_surface",
        "BRA_surface",
        "Trim_feature",
        "BR_cut",
        "FR_cut"
    };

    private static readonly string[] PgbBaseNames =
    {
        "TL_feature",
        "TD_sketch",
        "TDF_feature",
        "TDF_sketch",
        "FL_feature",
        "FL_sketch"
    };

    private static readonly string[] FgBaseNames =
    {
        "TL_feature",
        "TD_sketch",
        "TDF_feature",
        "TDF_sketch",
        "FL_feature",
        "FL_sketch",
        "FR_BR_feature",
        "FR_BR_sketch",
        "G_sketch",
        "FR_BR_cut_feature"
    };

    private static readonly string[] VrNames =
    {
        "VR_feature",
        "VR_sketch"
    };

    private static readonly string[] VflNames =
    {
        "VFL_feature",
        "VFL_sketch"
    };

    /*
     * Taper_line_sketch is used for production
     * and customer drawings, but not for overlays.
     */
    private static readonly string[] DrawingTaperLineNames =
    {
        "Taper_line_sketch"
    };

    private static readonly string[] CommonOverlayNames =
    {
        "ref_point",
        "cut_plan_feature",
        "cut_feature"
    };

    /*
     * Exactly one of these two sketches is active
     * for an overlay:
     *
     * - Standard case: FR_BR_overlay_sketch
     * - VFL case:      FR_BR_VFL_overlay_sketch
     */
    private static readonly string[] StandardFrBrOverlayNames =
    {
        "FR_BR_overlay_sketch"
    };

    private static readonly string[] VflFrBrOverlayNames =
    {
        "FR_BR_VFL_overlay_sketch"
    };

    private static readonly string[] PgbOverlayAlwaysNames =
    {
        "PGB_FL_overlay_sketch"
    };

    private static readonly string[] PgbOverlayWNames =
    {
        "PGB_W_overlay_sketch"
    };

    private static readonly string[] FgOverlayAlwaysNames =
    {
        "FG_FL_overlay_sketch"
    };

    private static readonly string[] FgOverlayWNames =
    {
        "FG_W_overlay_sketch"
    };

    private static readonly string[] FgVrOverlayNames =
    {
        "FG_VR_overlay_sketch"
    };

    private static readonly string[] FgGOverlayNames =
    {
        "FG_G_overlay_sketch"
    };

    private static readonly IReadOnlyCollection<string>
        AllManagedNames = BuildAllManagedNames();

    public ModelRuleRunner.FeaturePlan Build(
        WedgeData wedge,
        FeatureRuleContext context)
    {
        if (wedge is null)
            throw new ArgumentNullException(nameof(wedge));

        if (context is null)
            throw new ArgumentNullException(nameof(context));

        var facts = new WedgeFacts(wedge);

        var isPgb =
            context.Subclass == WedgeSubclass.PGB;

        var isOverlay =
            context.DrawingType == DrawingType.Overlay;

        var isProductionOrCustomer =
            context.DrawingType == DrawingType.Production ||
            context.DrawingType == DrawingType.Customer;

        var hasVr =
            HasPositiveLength(facts, "VR");

        var hasVfl =
            HasPositiveLength(facts, "VFL");

        var hasG =
            HasPositiveLength(facts, "GD");

        Logger.Info(
            "[Osg7FeatureRules] Build -> " +
            $"subclass={context.Subclass}, " +
            $"drawingType={context.DrawingType}, " +
            $"VR>0={hasVr}, " +
            $"VFL>0={hasVfl}, " +
            $"GD>0={hasG}.");

        var active = NewNameSet();

        /*
         * The new taper construction features must
         * always remain active.
         */
        AddAll(active, AlwaysOnNames);

        /*
         * Production and customer drawings use the
         * normal taper-line sketch.
         */
        if (isProductionOrCustomer)
            AddAll(active, DrawingTaperLineNames);

        if (isPgb)
        {
            AddPgbRules(
                active,
                isOverlay,
                hasVr,
                hasVfl);
        }
        else
        {
            AddFgRules(
                active,
                isOverlay,
                hasVr,
                hasVfl,
                hasG);
        }

        var suppress =
            new HashSet<string>(
                AllManagedNames,
                StringComparer.OrdinalIgnoreCase);

        suppress.ExceptWith(active);

        var suppressOrdered =
            suppress
                .OrderBy(
                    name => name,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var activeOrdered =
            active
                .OrderBy(
                    name => name,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        Logger.Info(
            "[Osg7FeatureRules] Active features/sketches -> " +
            string.Join(", ", activeOrdered));

        return new ModelRuleRunner.FeaturePlan(
            suppressOrdered,
            activeOrdered);
    }

    private static void AddPgbRules(
        HashSet<string> active,
        bool isOverlay,
        bool hasVr,
        bool hasVfl)
    {
        AddAll(active, PgbBaseNames);

        if (hasVr)
            AddAll(active, VrNames);

        if (hasVfl)
            AddAll(active, VflNames);

        if (!isOverlay)
            return;

        AddCommonOverlayRules(active, hasVfl);

        AddAll(active, PgbOverlayAlwaysNames);

        if (!hasVr)
            AddAll(active, PgbOverlayWNames);
    }

    private static void AddFgRules(
        HashSet<string> active,
        bool isOverlay,
        bool hasVr,
        bool hasVfl,
        bool hasG)
    {
        AddAll(active, FgBaseNames);

        if (hasVr)
            AddAll(active, VrNames);

        if (hasVfl)
            AddAll(active, VflNames);

        if (!isOverlay)
            return;

        AddCommonOverlayRules(active, hasVfl);

        AddAll(active, FgOverlayAlwaysNames);

        if (!hasVr)
            AddAll(active, FgOverlayWNames);

        if (hasVr)
            AddAll(active, FgVrOverlayNames);

        if (hasG)
            AddAll(active, FgGOverlayNames);
    }

    private static void AddCommonOverlayRules(
        HashSet<string> active,
        bool hasVfl)
    {
        AddAll(active, CommonOverlayNames);

        /*
         * The sketches are mutually exclusive.
         *
         * A positive VFL value means the VFL feature
         * is required and the VFL-specific taper
         * overlay sketch must be used.
         */
        if (hasVfl)
        {
            AddAll(active, VflFrBrOverlayNames);

            Logger.Info(
                "[Osg7FeatureRules] Overlay taper selection -> " +
                "FR_BR_VFL_sketch.");
        }
        else
        {
            AddAll(active, StandardFrBrOverlayNames);

            Logger.Info(
                "[Osg7FeatureRules] Overlay taper selection -> " +
                "FR_BR_overlays_sketch.");
        }
    }

    private static bool HasPositiveLength(
        WedgeFacts facts,
        string key)
    {
        return facts.TryGetLengthMm(
                   key,
                   out var valueMillimeters) &&
               valueMillimeters >
               WedgeFacts.DefaultPositiveEpsilon;
    }

    private static IReadOnlyCollection<string>
        BuildAllManagedNames()
    {
        var names = NewNameSet();

        AddAll(names, AlwaysOnNames);
        AddAll(names, PgbBaseNames);
        AddAll(names, FgBaseNames);
        AddAll(names, VrNames);
        AddAll(names, VflNames);
        AddAll(names, DrawingTaperLineNames);
        AddAll(names, CommonOverlayNames);
        AddAll(names, StandardFrBrOverlayNames);
        AddAll(names, VflFrBrOverlayNames);
        AddAll(names, PgbOverlayAlwaysNames);
        AddAll(names, PgbOverlayWNames);
        AddAll(names, FgOverlayAlwaysNames);
        AddAll(names, FgOverlayWNames);
        AddAll(names, FgVrOverlayNames);
        AddAll(names, FgGOverlayNames);

        return names
            .OrderBy(
                name => name,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static HashSet<string> NewNameSet()
    {
        return new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
    }

    private static void AddAll(
        HashSet<string> target,
        IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (!string.IsNullOrWhiteSpace(name))
                target.Add(name.Trim());
        }
    }
}