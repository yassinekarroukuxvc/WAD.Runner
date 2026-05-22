// DrawingAutomation/Rules/CKVD/CkvdAnnotationDeletionRules.cs
using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using WAD.Runner.DrawingAutomation.Rules.Common;

namespace WAD.Runner.DrawingAutomation.Rules.CKVD;

/// <summary>
/// CKVD production/customer annotation keep rules.
/// The deletion engine deletes existing DisplayDimensions that are not in the keep-set.
/// </summary>
public static class CkvdAnnotationDeletionRules
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
            "CKVD",
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

        AddCkvdFgCommon(keep, opt);

        Add(keep, V.Detail, "W@sketch_ISA_grinding");
        Add(keep, V.Detail, "B@sketch_section_V-Groove");
        Add(keep, V.Detail, "GA@sketch_section_V-Groove");
        Add(keep, V.Detail, "GR@sketch_section_V-Groove");
        Add(keep, V.Detail, "GD@sketch_section_V-Groove");

        AddFgSection(keep);
        return keep;
    }

    private static HashSet<AnnotationDeletionCore.Ann> BuildFgCustomerKeepSet(
        SharedAnnotationDeletionRules.Options opt)
    {
        var keep = new HashSet<AnnotationDeletionCore.Ann>();

        AddCkvdFgCommon(keep, opt);

        Add(keep, V.Detail, "W@sketch_ISA_grinding");
        Add(keep, V.Detail, "B@sketch_section_V-Groove");
        Add(keep, V.Detail, "GA@sketch_section_V-Groove");
        Add(keep, V.Detail, "GD@sketch_section_V-Groove");

        AddFgSection(keep);
        return keep;
    }

    private static HashSet<AnnotationDeletionCore.Ann> BuildPgbProductionKeepSet(
        SharedAnnotationDeletionRules.Options opt)
    {
        var keep = new HashSet<AnnotationDeletionCore.Ann>();

        Add(keep, V.Front, "TL@TL_cutting");
        AddSide(keep, opt);
        AddTop(keep);
        Add(keep, V.Detail, "W@sketch_ISA_grinding");
        Add(keep, V.Section, "FL@sketch_FA_BA_grinding");

        return keep;
    }

    private static void AddCkvdFgCommon(
        HashSet<AnnotationDeletionCore.Ann> keep,
        SharedAnnotationDeletionRules.Options opt)
    {
        Add(keep, V.Front, "TL@TL_cutting");
        Add(keep, V.Front, "W@sketch_ISA_grinding");

        if (opt.HasVr)
        {
            Add(keep, V.Front, "VW@sketch_VW_VR_grinding");
            Add(keep, V.Front, "VR@sketch_VW_VR_grinding");
        }

        AddSide(keep, opt);
        AddTop(keep);
    }

    private static void AddSide(
        HashSet<AnnotationDeletionCore.Ann> keep,
        SharedAnnotationDeletionRules.Options opt)
    {
        Add(keep, V.Side, "BA@sketch_FA_BA_grinding");
        Add(keep, V.Side, "FA@sketch_FA_BA_grinding");
        Add(keep, V.Side, "E@sketch_FA_BA_grinding");

        if (opt.HasX) Add(keep, V.Side, "X@sketch_FA_BA_grinding");
        if (opt.HasFx) Add(keep, V.Side, "FX@sketch_FA_BA_grinding");
    }

    private static void AddTop(HashSet<AnnotationDeletionCore.Ann> keep)
    {
        Add(keep, V.Top, "TD@sketch_TL_cutting");
        Add(keep, V.Top, "TDF@sketch_TDF_cutting");
    }

    private static void AddFgSection(HashSet<AnnotationDeletionCore.Ann> keep)
    {
        Add(keep, V.Section, "FR@FG_Production_Wed_F");
        Add(keep, V.Section, "BR@FG_Production_Wed_F");
        Add(keep, V.Section, "F@FG_Production_Wed_F");
        Add(keep, V.Section, "FL@sketch_FA_BA_grinding");
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
