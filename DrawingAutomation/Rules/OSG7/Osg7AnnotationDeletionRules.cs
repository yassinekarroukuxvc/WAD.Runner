// DrawingAutomation/Rules/OSG7/Osg7AnnotationDeletionRules.cs
using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using WAD.Runner.DrawingAutomation.Rules.Common;

namespace WAD.Runner.DrawingAutomation.Rules.OSG7;

/// <summary>
/// OSG7 production/customer annotation keep rules.
/// The deletion engine deletes existing DisplayDimensions that are not in the keep-set.
/// </summary>
public static class Osg7AnnotationDeletionRules
{
    public static bool RulesEnabled { get; set; } = true;

    public static IReadOnlyList<SharedAnnotationDeletionRules.DeletionTarget> PlanDeletionsFromDrawing(
        ModelDoc2 drawingModel,
        SharedAnnotationDeletionRules.DrawingType drawingType,
        SharedAnnotationDeletionRules.ShankType shankType,
        SharedAnnotationDeletionRules.FootOption footOption,
        SharedAnnotationDeletionRules.Options? options = null,
        SharedAnnotationDeletionRules.ViewNameMap? viewNames = null,
        bool activateEachView = true)
    {
        if (drawingModel is null) throw new ArgumentNullException(nameof(drawingModel));
        if (!RulesEnabled) return Array.Empty<SharedAnnotationDeletionRules.DeletionTarget>();

        options ??= new SharedAnnotationDeletionRules.Options();
        viewNames ??= new SharedAnnotationDeletionRules.ViewNameMap();

        var existingByView = AnnotationDeletionCore.CollectExistingDisplayDimensionFullNamesByView(
            drawingModel,
            viewNames.ToCore(),
            activateEachView);

        var keep = BuildKeepSet(drawingType, options);
        var keepExpectedByView = AnnotationDeletionCore.BuildKeepExpectedFullNamesByView(keep, viewNames.ToCore());

        return AnnotationDeletionCore
            .GetExistingMinusKeep(existingByView, keepExpectedByView)
            .Select(d => new SharedAnnotationDeletionRules.DeletionTarget(d.ViewName, d.AnnotationFullName))
            .ToList()
            .AsReadOnly();
    }

    public static void DumpExistingDimensionNames(
        ModelDoc2 drawingModel,
        SharedAnnotationDeletionRules.ViewNameMap? viewNames = null,
        bool activateEachView = true,
        int maxPerView = 250)
        => SharedAnnotationDeletionRules.DumpExistingDimensionNames(
            "OSG7",
            drawingModel,
            viewNames,
            activateEachView,
            maxPerView);

    private static HashSet<AnnotationDeletionCore.Ann> BuildKeepSet(
        SharedAnnotationDeletionRules.DrawingType drawingType,
        SharedAnnotationDeletionRules.Options opt)
        => drawingType switch
        {
            SharedAnnotationDeletionRules.DrawingType.Pgb => BuildPgbProductionKeepSet(opt),
            SharedAnnotationDeletionRules.DrawingType.Production => BuildFgProductionKeepSet(opt),
            SharedAnnotationDeletionRules.DrawingType.Customer => BuildFgCustomerKeepSet(opt),
            _ => throw new ArgumentOutOfRangeException(nameof(drawingType), drawingType, null)
        };

    private static HashSet<AnnotationDeletionCore.Ann> BuildFgProductionKeepSet(
        SharedAnnotationDeletionRules.Options opt)
    {
        var keep = new HashSet<AnnotationDeletionCore.Ann>();

        Add(keep, V.Front, "TL@ANNOT_RIGH_PLAN");
        if (opt.HasVr) Add(keep, V.Front, "VR@ANNOT_RIGH_PLAN");

        AddSide(keep, opt);
        AddTop(keep);

        Add(keep, V.Detail, "W@ANNOT_RIGH_PLAN");
        Add(keep, V.Detail, "B@ANNOT_RIGH_PLAN");
        Add(keep, V.Detail, "GD@ANNOT_RIGH_PLAN");
        Add(keep, V.Detail, "GR@ANNOT_RIGH_PLAN");
        if (opt.HasVw) Add(keep, V.Detail, "VW@ANNOT_RIGH_PLAN");
        if (opt.HasVra) Add(keep, V.Detail, "VRA@ANNOT_RIGH_PLAN");

        AddFgSection(keep);
        return keep;
    }

    private static HashSet<AnnotationDeletionCore.Ann> BuildFgCustomerKeepSet(
        SharedAnnotationDeletionRules.Options opt)
    {
        var keep = new HashSet<AnnotationDeletionCore.Ann>();

        Add(keep, V.Front, "TL@ANNOT_RIGH_PLAN");
        AddSide(keep, opt);
        AddTop(keep);

        Add(keep, V.Detail, "W@ANNOT_RIGH_PLAN");
        Add(keep, V.Detail, "B@ANNOT_RIGH_PLAN");
        Add(keep, V.Detail, "GD@ANNOT_RIGH_PLAN");
        if (opt.HasVw) Add(keep, V.Detail, "VW@ANNOT_RIGH_PLAN");
        if (opt.HasVra) Add(keep, V.Detail, "VRA@ANNOT_RIGH_PLAN");

        AddFgSection(keep);
        return keep;
    }

    private static HashSet<AnnotationDeletionCore.Ann> BuildPgbProductionKeepSet(
        SharedAnnotationDeletionRules.Options opt)
    {
        var keep = new HashSet<AnnotationDeletionCore.Ann>();

        Add(keep, V.Front, "TL@ANNOT_RIGH_PLAN");
        AddSide(keep, opt);
        AddTop(keep);

        Add(keep, V.Detail, "W@ANNOT_RIGH_PLAN");
        if (opt.HasVw) Add(keep, V.Detail, "VW@ANNOT_RIGH_PLAN");
        if (opt.HasVra) Add(keep, V.Detail, "VRA@ANNOT_RIGH_PLAN");

        Add(keep, V.Section, "FL@ANNOT_FRONT_PLAN");
        return keep;
    }

    private static void AddSide(
        HashSet<AnnotationDeletionCore.Ann> keep,
        SharedAnnotationDeletionRules.Options opt)
    {
        Add(keep, V.Side, "FA@ANNOT_FRONT_PLAN");
        Add(keep, V.Side, "BA@ANNOT_FRONT_PLAN");
        if (opt.HasX) Add(keep, V.Side, "X@ANNOT_FRONT_PLAN");
        if (opt.HasFx) Add(keep, V.Side, "FX@ANNOT_FRONT_PLAN");
        if (opt.HasVfl) Add(keep, V.Side, "VFL@ANNOT_FRONT_PLAN");
    }

    private static void AddTop(HashSet<AnnotationDeletionCore.Ann> keep)
    {
        Add(keep, V.Top, "TD@ANNOT_TOP_PLAN");
        Add(keep, V.Top, "TDF@ANNOT_TOP_PLAN");
    }

    private static void AddFgSection(HashSet<AnnotationDeletionCore.Ann> keep)
    {
        Add(keep, V.Section, "FR@ANNOT_FRONT_PLAN");
        Add(keep, V.Section, "BR@ANNOT_FRONT_PLAN");
        Add(keep, V.Section, "FL@ANNOT_FRONT_PLAN");
        Add(keep, V.Section, "F@ANNOT_FRONT_PLAN");
    }

    private static void Add(HashSet<AnnotationDeletionCore.Ann> set, AnnotationDeletionCore.ViewKind view, string fullName)
        => set.Add(new AnnotationDeletionCore.Ann(view, fullName));

    private static class V
    {
        internal static readonly AnnotationDeletionCore.ViewKind Front = AnnotationDeletionCore.ViewKind.Front;
        internal static readonly AnnotationDeletionCore.ViewKind Side = AnnotationDeletionCore.ViewKind.Side;
        internal static readonly AnnotationDeletionCore.ViewKind Top = AnnotationDeletionCore.ViewKind.Top;
        internal static readonly AnnotationDeletionCore.ViewKind Detail = AnnotationDeletionCore.ViewKind.Detail;
        internal static readonly AnnotationDeletionCore.ViewKind Section = AnnotationDeletionCore.ViewKind.Section;
    }
}
