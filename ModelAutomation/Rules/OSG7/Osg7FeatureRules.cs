using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.Rules.Common;

namespace WAD.Runner.ModelAutomation.Rules.OSG7;

public sealed class Osg7FeatureRules : IFeatureRuleSet
{
    private const decimal PositiveEpsilon = 0.000001m;

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

    private static readonly string[] CommonOverlayNames =
    {
        "ref_point",
        "cut_plan_feature",
        "cut_feature"
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

    public ModelRuleRunner.FeaturePlan Build(WedgeData wedge, FeatureRuleContext context)
    {
        if (wedge is null)
            throw new ArgumentNullException(nameof(wedge));

        if (context is null)
            throw new ArgumentNullException(nameof(context));

        var isPgb = context.Subclass == WedgeSubclass.PGB;
        var isOverlay = context.DrawingType == DrawingType.Overlay;
        var hasVr = HasPositiveNominal(wedge, "VR");
        var hasVfl = HasPositiveNominal(wedge, "VFL");
        var hasG = HasPositiveNominal(wedge, "GD");

        Logger.Info(
            $"[Osg7FeatureRules] Build -> subclass={context.Subclass}, drawingType={context.DrawingType}, " +
            $"isPGB={isPgb}, overlay={isOverlay}, VR>0={hasVr}, VFL>0={hasVfl}, G>0={hasG}");

        var active = NewNameSet();

        if (isPgb)
            AddPgbRules(active, isOverlay, hasVr, hasVfl);
        else
            AddFgRules(active, isOverlay, hasVr, hasVfl, hasG);

        var suppress = GetAllManagedNames();
        suppress.ExceptWith(active);

        Logger.Success($"[Osg7FeatureRules] Build -> done. unsuppress={active.Count}, suppress={suppress.Count}");

        return new ModelRuleRunner.FeaturePlan(
            Suppress: suppress.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            Unsuppress: active.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
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

        AddAll(active, CommonOverlayNames);
        AddAll(active, PgbOverlayAlwaysNames);

        if (!hasVr)
        {
            AddAll(active, PgbOverlayWNames);
        }
        else
        {
            Logger.Info("[Osg7FeatureRules] Overlay + VR>0 -> suppressing PGB_W_overlay_sketch.");
        }
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

        AddAll(active, CommonOverlayNames);
        AddAll(active, FgOverlayAlwaysNames);

        if (!hasVr)
        {
            AddAll(active, FgOverlayWNames);
        }
        else
        {
            Logger.Info("[Osg7FeatureRules] Overlay + VR>0 -> suppressing FG_W_overlay_sketch.");
        }

        if (hasVr)
            AddAll(active, FgVrOverlayNames);

        if (hasG)
            AddAll(active, FgGOverlayNames);
    }

    private static HashSet<string> GetAllManagedNames()
    {
        var all = NewNameSet();

        AddAll(all, PgbBaseNames);
        AddAll(all, FgBaseNames);
        AddAll(all, VrNames);
        AddAll(all, VflNames);
        AddAll(all, CommonOverlayNames);

        AddAll(all, PgbOverlayAlwaysNames);
        AddAll(all, PgbOverlayWNames);

        AddAll(all, FgOverlayAlwaysNames);
        AddAll(all, FgOverlayWNames);

        AddAll(all, FgVrOverlayNames);
        AddAll(all, FgGOverlayNames);

        return all;
    }

    private static bool HasPositiveNominal(WedgeData wedge, string key)
        => WedgeDimensionReader.HasPositiveNominal(wedge, key, PositiveEpsilon);

    private static HashSet<string> NewNameSet()
        => new(StringComparer.OrdinalIgnoreCase);

    private static void AddAll(HashSet<string> set, IEnumerable<string> items)
    {
        foreach (var name in items)
        {
            if (!string.IsNullOrWhiteSpace(name))
                set.Add(name.Trim());
        }
    }
}
