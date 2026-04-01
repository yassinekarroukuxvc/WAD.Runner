// DrawingAutomation/Rules/UTUS/UtusAnnotationDeletionRules.cs
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using WAD.Runner.Application; // ✅ Logger (for Dump methods)
using WAD.Runner.DrawingAutomation.Rules.Common; // ✅ AnnotationDeletionCore

namespace WAD.Runner.DrawingAutomation.Rules.UTUS;

/// <summary>
/// UTUS Annotation deletion planning (PDF-driven rules + SolidWorks scanning).
///
/// Same behavior as COB.
///
/// ✅ Rules implemented:
/// - PGB (STD + 180_DEG_REV): as before, with your "Front has ONLY TL+K" rule still enforced.
/// - Production + Customer: implemented from the PDF, with your corrections:
///   1) Anything PDF says "Front View" → treat as "Side View" IN CODE (global guideline),
///      BUT…
///   2) Your explicit override: Front View only keeps TL @ANNOT_LEFT_sketch and K@ANNOT_LEFT_sketch.
///   3) Your explicit override: the "Front sketch list" (FRO/FD/FL/ERL/CA/RA/H/HA/FNA/BA) MUST be kept in SECTION view.
///   4) Production + Customer MUST also ALWAYS keep BA in SIDE view.
///
/// ✅ Detail View foot-option rules:
///   - FootOption CG: keep G/CGD/CGR in DETAIL view using the shank FRONT sketch (STD or 180_DEG_REV).
///   - FootOption CC: keep C-detail (CR/CD) + CG-detail (G/CGD/CGR) in DETAIL view.
///   - For 180_DEG_REV CGD/CGR, support both sketch name variants:
///       ANNOT_180_DEG_REV_FRONT_sketch  and  ANNOT_180_DEG_REV_FRONT_FRONT_sketch
///
/// ✅ Section View foot-option rules update:
///   - FootOption C, CC, CG: keep FR_C and BR_C in SECTION view for both shank types.
/// </summary>
public static class UtusAnnotationDeletionRules
{
    // ============================================================
    // 0) GLOBAL SWITCH
    // ============================================================

    public static bool RulesEnabled { get; set; } = true;

    // ---- Public enums ----

    public enum DrawingType
    {
        Pgb,
        Production,
        Customer
    }

    public enum ShankType
    {
        Std,
        Deg180Rev
    }

    public enum FootOption
    {
        None,       // for PGB (N/A)
        C,
        G,
        VG,
        CG,
        CC,
        C_WITH_CBR
    }

    public enum ViewKind
    {
        Front,
        Side,
        Top,
        Detail,
        Section
    }

    public sealed record DeletionTarget(string ViewName, string AnnotationFullName);

    public sealed class Options
    {
        public bool HasVwVr { get; init; }
        public bool HasW2 { get; init; }
        public bool HasSlb { get; init; }
        public bool HasRa2 { get; init; }

        public bool HasFrBr { get; init; }
        public bool HasF { get; init; }

        public bool HasK { get; init; }
        public string? KAnnotationFullName { get; init; }

        public bool HasErd { get; init; }
        public string? ErdAnnotationFullName { get; init; }
    }

    /// <summary>
    /// Nominal view-name map.
    /// </summary>
    public sealed class ViewNameMap
    {
        public string Front { get; init; } = "Front View";
        public string Side { get; init; } = "Side View";
        public string Top { get; init; } = "Top View";
        public string Detail { get; init; } = "Detail View";
        public string Section { get; init; } = "Section View";

        public string Resolve(ViewKind kind) => kind switch
        {
            ViewKind.Front => Front,
            ViewKind.Side => Side,
            ViewKind.Top => Top,
            ViewKind.Detail => Detail,
            ViewKind.Section => Section,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        internal AnnotationDeletionCore.ViewNameMap ToCore()
            => new AnnotationDeletionCore.ViewNameMap
            {
                Front = Front,
                Side = Side,
                Top = Top,
                Detail = Detail,
                Section = Section
            };
    }

    // ============================================================
    // 0) DUMP / DIAGNOSTICS
    // ============================================================

    public static void DumpDeletionPlan(
        string title,
        IReadOnlyList<DeletionTarget> deletions,
        int maxPerView = 200)
    {
        var core = (deletions ?? Array.Empty<DeletionTarget>())
            .Select(d => new AnnotationDeletionCore.DeletionTarget(d.ViewName, d.AnnotationFullName))
            .ToList()
            .AsReadOnly();

        AnnotationDeletionCore.DumpDeletionPlan(title, core, tagPrefix: "UTUS", maxPerView: maxPerView);
    }

    public static void DumpExistingDisplayDimensionFullNamesFromDrawing(
        ModelDoc2 drawingModel,
        ViewNameMap? viewNames = null,
        bool activateEachView = true,
        int maxPerView = 250)
    {
        AnnotationDeletionCore.DumpExistingDisplayDimensionFullNamesFromDrawing(
            drawingModel,
            (viewNames ?? new ViewNameMap()).ToCore(),
            tagPrefix: "UTUS",
            activateEachView: activateEachView,
            maxPerView: maxPerView);
    }

    // ============================================================
    // 1) CAD-agnostic rule API (returns deletion candidates)
    // ============================================================

    public static IReadOnlyList<DeletionTarget> GetAnnotationsToDelete(
        DrawingType drawingType,
        ShankType shankType,
        FootOption footOption,
        Options? options = null,
        ViewNameMap? viewNames = null)
    {
        if (!RulesEnabled)
            return new ReadOnlyCollection<DeletionTarget>(new List<DeletionTarget>());

        options ??= new Options();
        viewNames ??= new ViewNameMap();

        var keep = BuildKeepSet(drawingType, shankType, footOption, options);
        var all = BuildAllKnownAnnotations(options);

        var coreDeletions = AnnotationDeletionCore.GetAnnotationsToDelete_FromKnownSuperset(
            keep,
            all,
            viewNames.ToCore());

        var mapped = coreDeletions
            .Select(d => new DeletionTarget(d.ViewName, d.AnnotationFullName))
            .ToList();

        return new ReadOnlyCollection<DeletionTarget>(mapped);
    }

    // ============================================================
    // 2) "Smart search" API (filters candidates by what exists)
    // ============================================================

    public static IReadOnlyList<DeletionTarget> GetExistingAnnotationsToDelete_FromKnownSuperset(
        DrawingType drawingType,
        ShankType shankType,
        FootOption footOption,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> existingByViewName,
        Options? options = null,
        ViewNameMap? viewNames = null)
    {
        if (!RulesEnabled)
            return new ReadOnlyCollection<DeletionTarget>(new List<DeletionTarget>());

        options ??= new Options();
        viewNames ??= new ViewNameMap();

        var candidates = GetAnnotationsToDelete(drawingType, shankType, footOption, options, viewNames);

        var coreCandidates = candidates
            .Select(c => new AnnotationDeletionCore.DeletionTarget(c.ViewName, c.AnnotationFullName))
            .ToList()
            .AsReadOnly();

        var coreResults = AnnotationDeletionCore.FilterCandidatesByExisting_FromKnownSuperset(
            coreCandidates,
            existingByViewName);

        var mapped = coreResults
            .Select(d => new DeletionTarget(d.ViewName, d.AnnotationFullName))
            .ToList();

        return new ReadOnlyCollection<DeletionTarget>(mapped);
    }

    // ============================================================
    // 3) SolidWorks CAD-aware API (scan + apply rules)
    // ============================================================

    public static IReadOnlyList<DeletionTarget> PlanDeletionsFromDrawing(
        ModelDoc2 drawingModel,
        DrawingType drawingType,
        ShankType shankType,
        FootOption footOption,
        Options? options = null,
        ViewNameMap? viewNames = null,
        bool activateEachView = true)
    {
        if (drawingModel == null) throw new ArgumentNullException(nameof(drawingModel));
        if (drawingModel.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            throw new ArgumentException("ModelDoc2 must be a Drawing document.", nameof(drawingModel));

        if (!RulesEnabled)
            return new ReadOnlyCollection<DeletionTarget>(new List<DeletionTarget>());

        options ??= new Options();
        viewNames ??= new ViewNameMap();

        var existingByView = AnnotationDeletionCore.CollectExistingDisplayDimensionFullNamesByView(
            drawingModel,
            viewNames.ToCore(),
            activateEachView);

        var keep = BuildKeepSet(drawingType, shankType, footOption, options);
        var keepExpectedByView = AnnotationDeletionCore.BuildKeepExpectedFullNamesByView(keep, viewNames.ToCore());

        var coreDeletions = AnnotationDeletionCore.GetExistingMinusKeep(existingByView, keepExpectedByView);

        var mapped = coreDeletions
            .Select(d => new DeletionTarget(d.ViewName, d.AnnotationFullName))
            .ToList();

        return new ReadOnlyCollection<DeletionTarget>(mapped);
    }

    public static IReadOnlyList<DeletionTarget> PlanDeletionsFromDrawing_FromKnownSuperset(
        ModelDoc2 drawingModel,
        DrawingType drawingType,
        ShankType shankType,
        FootOption footOption,
        Options? options = null,
        ViewNameMap? viewNames = null,
        bool activateEachView = true)
    {
        if (drawingModel == null) throw new ArgumentNullException(nameof(drawingModel));
        if (drawingModel.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            throw new ArgumentException("ModelDoc2 must be a Drawing document.", nameof(drawingModel));

        if (!RulesEnabled)
            return new ReadOnlyCollection<DeletionTarget>(new List<DeletionTarget>());

        options ??= new Options();
        viewNames ??= new ViewNameMap();

        var existingByView = AnnotationDeletionCore.CollectExistingDisplayDimensionFullNamesByView(
            drawingModel,
            viewNames.ToCore(),
            activateEachView);

        return GetExistingAnnotationsToDelete_FromKnownSuperset(
            drawingType,
            shankType,
            footOption,
            existingByView,
            options,
            viewNames);
    }

    // ============================================================
    // RULES (keep-set + known superset)
    // ============================================================

    private static HashSet<AnnotationDeletionCore.Ann> BuildAllKnownAnnotations(Options opt)
    {
        var all = new HashSet<AnnotationDeletionCore.Ann>();

        // Front view ONLY dims
        AddFrontAlways(all);

        // Detail always
        AddDetailAlways(all);

        // Top always (per shank)
        AddTopAlways(all, ShankType.Std);
        AddTopAlways(all, ShankType.Deg180Rev);

        // PGB base items
        AddPgbBaseSuperset(all, ShankType.Std);
        AddPgbBaseSuperset(all, ShankType.Deg180Rev);

        // Production / Customer shared "front sketch dims" kept in SECTION view
        AddFrontSketchCavitySuperset_AsSection(all, ShankType.Std);
        AddFrontSketchCavitySuperset_AsSection(all, ShankType.Deg180Rev);

        // Optional: VBL (SLB)
        AddSideSlbSuperset(all, ShankType.Std);
        AddSideSlbSuperset(all, ShankType.Deg180Rev);

        // Optional: VW/VR/VRA
        AddDetailVwVrSuperset(all);

        // Optional: W2
        AddDetailW2Superset(all);

        // Optional: RA2, ERD
        AddSectionRa2Superset(all, ShankType.Std);
        AddSectionRa2Superset(all, ShankType.Deg180Rev);
        AddSectionErdSuperset(all, ShankType.Std);
        AddSectionErdSuperset(all, ShankType.Deg180Rev);

        // FR/BR superset
        AddSectionFrBrSuperset(all, ShankType.Std);
        AddSectionFrBrSuperset(all, ShankType.Deg180Rev);

        // Foot options
        AddFootOptionSuperset(all);

        return all;
    }

    private static HashSet<AnnotationDeletionCore.Ann> BuildKeepSet(
        DrawingType drawingType,
        ShankType shankType,
        FootOption footOption,
        Options options)
    {
        var keep = new HashSet<AnnotationDeletionCore.Ann>();

        switch (drawingType)
        {
            case DrawingType.Pgb:
                AddPgbKeep(keep, shankType, options);
                break;

            case DrawingType.Production:
                AddProductionKeep(keep, shankType, footOption, options);
                break;

            case DrawingType.Customer:
                AddCustomerKeep(keep, shankType, footOption, options);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(drawingType), drawingType, null);
        }

        return keep;
    }

    // ============================================================
    // KEEP RULES: FRONT (ONLY TL + K)
    // ============================================================

    private static void AddFrontAlways(HashSet<AnnotationDeletionCore.Ann> set)
    {
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Front, "TL@ANNOT_LEFT_sketch"));
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Front, "K@ANNOT_LEFT_sketch"));
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Front, "VR@ANNOT_LEFT_sketch"));
    }

    private static void AddFrontAlways(HashSet<AnnotationDeletionCore.Ann> set, Options opt)
    {
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Front, "TL@ANNOT_LEFT_sketch"));

        var kName = !string.IsNullOrWhiteSpace(opt.KAnnotationFullName)
            ? opt.KAnnotationFullName!.Trim()
            : "K@ANNOT_LEFT_sketch";

        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Front, kName));

        if (opt.HasVwVr)
            set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Front, "VR@ANNOT_LEFT_sketch"));
    }

    // ============================================================
    // KEEP RULES: DETAIL / TOP BASE
    // ============================================================

    private static void AddDetailAlways(HashSet<AnnotationDeletionCore.Ann> set)
    {
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "W@ANNOT_LEFT_sketch"));
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "ISA@ANNOT_LEFT_sketch"));
    }

    private static void AddTopAlways(HashSet<AnnotationDeletionCore.Ann> set, ShankType shank)
    {
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Top, $"TD@{TopSketch(shank)}"));
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Top, $"TDF@{TopSketch(shank)}"));
    }

    // ============================================================
    // PGB
    // ============================================================

    private static void AddPgbBaseSuperset(HashSet<AnnotationDeletionCore.Ann> set, ShankType shank)
    {
        // Top
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Top, $"TD@{TopSketch(shank)}"));
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Top, $"TDF@{TopSketch(shank)}"));

        // Side (PGB BA)
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Side, $"BA@{FrontSketch(shank)}"));

        // Section (PGB)
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"RA@{FrontSketch(shank)}"));
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"T@{FrontSketch(shank)}"));
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"FD@{FrontSketch(shank)}"));

        // Detail
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "W@ANNOT_LEFT_sketch"));
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "ISA@ANNOT_LEFT_sketch"));

        // Front only
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Front, "TL@ANNOT_LEFT_sketch"));
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Front, "K@ANNOT_LEFT_sketch"));
    }

    private static void AddPgbKeep(HashSet<AnnotationDeletionCore.Ann> keep, ShankType shank, Options options)
    {
        AddFrontAlways(keep, options);
        AddDetailAlways(keep);
        AddTopAlways(keep, shank);

        keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Side, $"BA@{FrontSketch(shank)}"));

        keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"RA@{FrontSketch(shank)}"));
        keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"T@{FrontSketch(shank)}"));
        keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"FD@{FrontSketch(shank)}"));
    }

    // ============================================================
    // PRODUCTION
    // ============================================================

    private static void AddProductionKeep(HashSet<AnnotationDeletionCore.Ann> keep, ShankType shank, FootOption foot, Options opt)
    {
        AddFrontAlways(keep, opt);
        AddTopAlways(keep, shank);
        AddDetailAlways(keep);

        if (opt.HasVwVr)
        {
            keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "VW@ANNOT_LEFT_sketch"));
            keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "VR@ANNOT_LEFT_sketch"));
            keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "VRA@ANNOT_LEFT_sketch"));
        }

        if (opt.HasW2)
            keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "W2@ANNOT_LEFT_sketch"));

        if (opt.HasSlb)
            keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Side, $"VBL@{FrontSketch(shank)}"));

        keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Side, $"BA@{FrontSketch(shank)}"));

        AddFrontSketchCavityKeep_AsSection(keep, shank);

        keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"T@{FrontSketch(shank)}"));

        if (opt.HasRa2)
            keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"RA2@{FrontSketch(shank)}"));

        if (opt.HasErd)
        {
            var erdName = !string.IsNullOrWhiteSpace(opt.ErdAnnotationFullName)
                ? opt.ErdAnnotationFullName!.Trim()
                : $"ERD@{FrontSketch(shank)}";

            keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, erdName));
        }

        AddFootOptionKeep_Production(keep, shank, foot, opt);
    }

    // ============================================================
    // CUSTOMER
    // ============================================================

    private static void AddCustomerKeep(HashSet<AnnotationDeletionCore.Ann> keep, ShankType shank, FootOption foot, Options opt)
    {
        AddFrontAlways(keep, opt);
        AddTopAlways(keep, shank);
        AddDetailAlways(keep);

        if (opt.HasVwVr)
        {
            keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "VW@ANNOT_LEFT_sketch"));
            keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "VR@ANNOT_LEFT_sketch"));
            keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "VRA@ANNOT_LEFT_sketch"));
        }

        if (opt.HasW2)
            keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "W2@ANNOT_LEFT_sketch"));

        if (opt.HasSlb)
            keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Side, $"VBL@{FrontSketch(shank)}"));

        keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Side, $"BA@{FrontSketch(shank)}"));

        
        keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"RA@{FrontSketch(shank)}"));
        keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"T@{FrontSketch(shank)}"));
        keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"H@{FrontSketch(shank)}"));
        keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"HA@{FrontSketch(shank)}"));
        keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"FNA@{FrontSketch(shank)}"));

        if (opt.HasRa2)
            keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"RA2@{FrontSketch(shank)}"));

        AddFootOptionKeep_Customer(keep, shank, foot, opt);
    }
    // ============================================================
    // FRONT-SKETCH CAVITY SET
    // ============================================================

    private static void AddFrontSketchCavitySuperset_AsSection(HashSet<AnnotationDeletionCore.Ann> set, ShankType shank)
    {
        foreach (var key in new[] { "FD", "FL", "ERL", "RA", "H", "HA", "FNA", "BA" })
            set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"{key}@{FrontSketch(shank)}"));
    }

    private static void AddFrontSketchCavityKeep_AsSection(HashSet<AnnotationDeletionCore.Ann> keep, ShankType shank)
    {
        AddFrontSketchCavitySuperset_AsSection(keep, shank);
    }

    // ============================================================
    // SLB / VWVR / W2 / RA2 / ERD SUPERSets
    // ============================================================

    private static void AddSideSlbSuperset(HashSet<AnnotationDeletionCore.Ann> set, ShankType shank)
    {
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Side, $"VBL@{FrontSketch(shank)}"));
    }

    private static void AddDetailVwVrSuperset(HashSet<AnnotationDeletionCore.Ann> set)
    {
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "VW@ANNOT_LEFT_sketch"));
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "VR@ANNOT_LEFT_sketch"));
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "VRA@ANNOT_LEFT_sketch"));
    }

    private static void AddDetailW2Superset(HashSet<AnnotationDeletionCore.Ann> set)
    {
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "W2@ANNOT_LEFT_sketch"));
    }

    private static void AddSectionRa2Superset(HashSet<AnnotationDeletionCore.Ann> set, ShankType shank)
    {
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"RA2@{FrontSketch(shank)}"));
    }

    private static void AddSectionErdSuperset(HashSet<AnnotationDeletionCore.Ann> set, ShankType shank)
    {
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"ERD@{FrontSketch(shank)}"));
    }

    // ============================================================
    // FR/BR SUPERSets
    // ============================================================

    private static void AddSectionFrBrSuperset(HashSet<AnnotationDeletionCore.Ann> set, ShankType shank)
    {
        var sketch = FrBrSketch(shank);

        foreach (var suffix in new[] { "C", "G", "VG" })
        {
            set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"FR_{suffix}@{sketch}"));
            set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"BR_{suffix}@{sketch}"));
        }
    }

    // ============================================================
    // FOOT OPTION SUPERSets + KEEP
    // ============================================================

    private static void AddFootOptionSuperset(HashSet<AnnotationDeletionCore.Ann> set)
    {
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "CR@ANNOT_FOOT_OPTIONS_LEFT_sketch"));
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "CD@ANNOT_FOOT_OPTIONS_LEFT_sketch"));

        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "GR_G@ANNOT_FOOT_OPTIONS_LEFT_sketch"));
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "GD_G@ANNOT_FOOT_OPTIONS_LEFT_sketch"));

        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "GR_VG@ANNOT_FOOT_OPTIONS_LEFT_sketch"));
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "GA@ANNOT_FOOT_OPTIONS_LEFT_sketch"));
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "B@ANNOT_FOOT_OPTIONS_LEFT_sketch"));

        AddCgFootOptionSuperset_AsDetail(set, ShankType.Std);
        AddCgFootOptionSuperset_AsDetail(set, ShankType.Deg180Rev);

        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, "G@ANNOT_CG_FOOT_OPTIONS_FRONT_sketch"));
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, "CGD@ANNOT_CG_FOOT_OPTIONS_FRONT_sketch"));
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, "CGR@ANNOT_CG_FOOT_OPTIONS_FRONT_sketch"));
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, "CFD@ANNOT_CG_FOOT_OPTIONS_FRONT_sketch"));

        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, "CBRA@ANNOT_CBR_FOOT_OPTIONS_FRONT_sketch"));
        set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, "CBRL@ANNOT_CBR_FOOT_OPTIONS_FRONT_sketch"));
    }

    private static void AddCgFootOptionSuperset_AsDetail(HashSet<AnnotationDeletionCore.Ann> set, ShankType shank)
    {
        foreach (var sketch in CgDetailSketchCandidates(shank))
        {
            set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, $"G@{sketch}"));
            set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, $"CGD@{sketch}"));
            set.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, $"CGR@{sketch}"));
        }
    }

    private static void AddFootOptionKeep_Production(HashSet<AnnotationDeletionCore.Ann> keep, ShankType shank, FootOption foot, Options opt)
    {
        switch (foot)
        {
            case FootOption.None:
                break;

            case FootOption.C:
                keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "CD@ANNOT_FOOT_OPTIONS_LEFT_sketch"));

                if (opt.HasFrBr)
                {
                    var frbr = FrBrSketch(shank);
                    keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"FR_C@{frbr}"));
                    keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"BR_C@{frbr}"));
                }
                break;

            case FootOption.G:
                keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "GR_G@ANNOT_FOOT_OPTIONS_LEFT_sketch"));
                keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "GD_G@ANNOT_FOOT_OPTIONS_LEFT_sketch"));

                if (opt.HasFrBr)
                {
                    var frbr = FrBrSketch(shank);
                    keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"FR_G@{frbr}"));
                    keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"BR_G@{frbr}"));
                }
                break;

            case FootOption.VG:
                keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "GR_VG@ANNOT_FOOT_OPTIONS_LEFT_sketch"));
                keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "GA@ANNOT_FOOT_OPTIONS_LEFT_sketch"));
                keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "B@ANNOT_FOOT_OPTIONS_LEFT_sketch"));

                if (opt.HasFrBr)
                {
                    var frbr = FrBrSketch(shank);
                    keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"FR_VG@{frbr}"));
                    keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"BR_VG@{frbr}"));
                }
                break;

            case FootOption.CG:
                AddCgFootOptionKeep_AsDetail(keep, shank);

                if (opt.HasFrBr)
                {
                    var frbr = FrBrSketch(shank);
                    keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"FR_C@{frbr}"));
                    keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"BR_C@{frbr}"));
                }
                break;

            case FootOption.CC:
                keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "CD@ANNOT_FOOT_OPTIONS_LEFT_sketch"));
                AddCgFootOptionKeep_AsDetail(keep, shank);

                if (opt.HasFrBr)
                {
                    var frbr = FrBrSketch(shank);
                    keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"FR_C@{frbr}"));
                    keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"BR_C@{frbr}"));
                }
                break;

            case FootOption.C_WITH_CBR:
                keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, "CD@ANNOT_FOOT_OPTIONS_LEFT_sketch"));
                keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, "CBRA@ANNOT_CBR_FOOT_OPTIONS_FRONT_sketch"));
                keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, "CBRL@ANNOT_CBR_FOOT_OPTIONS_FRONT_sketch"));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(foot), foot, null);
        }
    }

    private static void AddCgFootOptionKeep_AsDetail(HashSet<AnnotationDeletionCore.Ann> keep, ShankType shank)
    {
        foreach (var sketch in CgDetailSketchCandidates(shank))
        {
            keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, $"G@{sketch}"));
            keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, $"CGD@{sketch}"));
            keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Detail, $"CGR@{sketch}"));
        }
    }

    private static void AddFootOptionKeep_Customer(HashSet<AnnotationDeletionCore.Ann> keep, ShankType shank, FootOption foot, Options opt)
    {
        switch (foot)
        {
            case FootOption.C:
                if (opt.HasFrBr)
                {
                    var frbr = FrBrSketch(shank);
                    keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"FR_C@{frbr}"));
                    keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"BR_C@{frbr}"));
                }
                break;

            case FootOption.G:
                if (opt.HasFrBr)
                {
                    var frbr = FrBrSketch(shank);
                    keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"FR_G@{frbr}"));
                    keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"BR_G@{frbr}"));
                }
                break;

            case FootOption.VG:
                if (opt.HasFrBr)
                {
                    var frbr = FrBrSketch(shank);
                    keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"FR_VG@{frbr}"));
                    keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"BR_VG@{frbr}"));
                }
                break;

            case FootOption.CG:
                AddCgFootOptionKeep_AsDetail(keep, shank);

                if (opt.HasFrBr)
                {
                    var frbr = FrBrSketch(shank);
                    keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"FR_C@{frbr}"));
                    keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"BR_C@{frbr}"));
                }
                break;

            case FootOption.CC:
                AddCgFootOptionKeep_AsDetail(keep, shank);

                if (opt.HasFrBr)
                {
                    var frbr = FrBrSketch(shank);
                    keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"FR_C@{frbr}"));
                    keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, $"BR_C@{frbr}"));
                }
                break;

            case FootOption.C_WITH_CBR:
                keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, "CBRA@ANNOT_CBR_FOOT_OPTIONS_FRONT_sketch"));
                keep.Add(new AnnotationDeletionCore.Ann(AnnotationDeletionCore.ViewKind.Section, "CBRL@ANNOT_CBR_FOOT_OPTIONS_FRONT_sketch"));
                break;

            case FootOption.None:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(foot), foot, null);
        }
    }

    // ============================================================
    // Sketch name helpers
    // ============================================================

    private static string FrontSketch(ShankType shank)
        => shank == ShankType.Std
            ? "ANNOT_STD_FRONT_sketch"
            : "ANNOT_180_DEG_REV_FRONT_sketch";

    private static string TopSketch(ShankType shank)
        => shank == ShankType.Std
            ? "ANNOT_STD_TOP_sketch"
            : "ANNOT_180_DEG_REV_TOP_sketch";

    private static string FrBrSketch(ShankType shank)
        => shank == ShankType.Std
            ? "ANNOT_FR_BR_STD_FRONT_sketch"
            : "ANNOT_FR_BR_180_DEG_REV_FRONT_sketch";

    private static IEnumerable<string> CgDetailSketchCandidates(ShankType shank)
    {
        if (shank == ShankType.Std)
        {
            yield return "ANNOT_STD_FRONT_sketch";
            yield break;
        }

        yield return "ANNOT_180_DEG_REV_FRONT_sketch";
        yield return "ANNOT_180_DEG_REV_FRONT_FRONT_sketch";
    }
}