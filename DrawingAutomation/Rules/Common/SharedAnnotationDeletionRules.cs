using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace WAD.Runner.DrawingAutomation.Rules.Common;

/// <summary>
/// Shared annotation deletion rules for COB-like drawing templates (COB, UTUS, FP).
/// The engine builds a keep-set for the active drawing context and deletes existing
/// annotations that are not explicitly kept.
/// </summary>
public static class SharedAnnotationDeletionRules
{
    public static bool RulesEnabled { get; set; } = true;

    public enum DrawingType { Pgb, Production, Customer }
    public enum ShankType { Std, Deg180Rev }
    public enum FootOption { None, C, G, VG, CG, CC, C_WITH_CBR }
    public enum ViewKind { Front, Side, Top, Detail, Section }

    public sealed record DeletionTarget(string ViewName, string AnnotationFullName);

    public sealed class Options
    {
        // Front / Side
        public bool HasVwVr { get; init; }
        public bool HasVw { get; init; }
        public bool HasVr { get; init; }
        public bool HasX { get; init; }
        public bool HasFx { get; init; }
        public bool HasVfl { get; init; }
        public bool HasSlb { get; init; }

        // Detail
        public bool HasVra { get; init; }
        public bool HasW2 { get; init; }
        public bool HasGa { get; init; }
        public bool HasCd { get; init; }
        public bool HasGd { get; init; }
        public bool HasGr { get; init; }
        public bool HasB { get; init; }

        // Section
        public bool HasRa2 { get; init; }
        public bool HasErd { get; init; }
        public bool HasFr { get; init; }
        public bool HasBr { get; init; }
        public bool HasFrBr => HasFr && HasBr;
        public bool HasF { get; init; }
        public bool HasG { get; init; }
        public bool HasCgr { get; init; }
        public bool HasCgd { get; init; }
        public bool HasCbra { get; init; }
        public bool HasCbrl { get; init; }

        public string? KAnnotationFullName { get; init; }
        public string? ErdAnnotationFullName { get; init; }
    }

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

        internal AnnotationDeletionCore.ViewNameMap ToCore() => new()
        {
            Front = Front,
            Side = Side,
            Top = Top,
            Detail = Detail,
            Section = Section
        };
    }

    public static void DumpDeletionPlan(
        string tagPrefix,
        string title,
        IReadOnlyList<DeletionTarget> deletions,
        int maxPerView = 200)
    {
        var core = (deletions ?? Array.Empty<DeletionTarget>())
            .Select(d => new AnnotationDeletionCore.DeletionTarget(d.ViewName, d.AnnotationFullName))
            .ToList()
            .AsReadOnly();

        AnnotationDeletionCore.DumpDeletionPlan(title, core, tagPrefix, maxPerView);
    }

    public static void DumpExistingDimensionNames(
        string tagPrefix,
        ModelDoc2 drawingModel,
        ViewNameMap? viewNames = null,
        bool activateEachView = true,
        int maxPerView = 250)
    {
        if (drawingModel is null) throw new ArgumentNullException(nameof(drawingModel));

        AnnotationDeletionCore.DumpExistingDisplayDimensionFullNamesFromDrawing(
            drawingModel,
            (viewNames ?? new ViewNameMap()).ToCore(),
            tagPrefix,
            activateEachView,
            maxPerView);
    }

    /// <summary>
    /// CAD-agnostic mode: returns all known template annotations except the keep-set.
    /// </summary>
    public static IReadOnlyList<DeletionTarget> GetAnnotationsToDelete(
        DrawingType drawingType,
        ShankType shankType,
        FootOption footOption,
        Options? options = null,
        ViewNameMap? viewNames = null)
    {
        if (!RulesEnabled) return Empty();

        options ??= new Options();
        viewNames ??= new ViewNameMap();

        var keep = BuildKeepSet(drawingType, shankType, footOption, options);
        var all = BuildAllKnownAnnotations();

        return AnnotationDeletionCore
            .GetAnnotationsToDelete_FromKnownSuperset(keep, all, viewNames.ToCore())
            .Select(d => new DeletionTarget(d.ViewName, d.AnnotationFullName))
            .ToList()
            .AsReadOnly();
    }

    public static IReadOnlyList<DeletionTarget> GetExistingAnnotationsToDelete_FromKnownSuperset(
        DrawingType drawingType,
        ShankType shankType,
        FootOption footOption,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> existingByViewName,
        Options? options = null,
        ViewNameMap? viewNames = null)
    {
        if (!RulesEnabled) return Empty();
        if (existingByViewName is null) throw new ArgumentNullException(nameof(existingByViewName));

        options ??= new Options();
        viewNames ??= new ViewNameMap();

        var candidates = GetAnnotationsToDelete(drawingType, shankType, footOption, options, viewNames)
            .Select(c => new AnnotationDeletionCore.DeletionTarget(c.ViewName, c.AnnotationFullName))
            .ToList()
            .AsReadOnly();

        return AnnotationDeletionCore
            .FilterCandidatesByExisting_FromKnownSuperset(candidates, existingByViewName)
            .Select(d => new DeletionTarget(d.ViewName, d.AnnotationFullName))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Defensive CAD-aware mode: scans the drawing and deletes every existing annotation
    /// that is not explicitly kept.
    /// </summary>
    public static IReadOnlyList<DeletionTarget> PlanDeletionsFromDrawing(
        ModelDoc2 drawingModel,
        DrawingType drawingType,
        ShankType shankType,
        FootOption footOption,
        Options? options = null,
        ViewNameMap? viewNames = null,
        bool activateEachView = true)
    {
        if (drawingModel is null) throw new ArgumentNullException(nameof(drawingModel));
        if (!RulesEnabled) return Empty();

        options ??= new Options();
        viewNames ??= new ViewNameMap();

        var existingByView = AnnotationDeletionCore.CollectExistingDisplayDimensionFullNamesByView(
            drawingModel, viewNames.ToCore(), activateEachView);

        var keep = BuildKeepSet(drawingType, shankType, footOption, options);
        var keepExpectedByView = AnnotationDeletionCore.BuildKeepExpectedFullNamesByView(keep, viewNames.ToCore());

        return AnnotationDeletionCore
            .GetExistingMinusKeep(existingByView, keepExpectedByView)
            .Select(d => new DeletionTarget(d.ViewName, d.AnnotationFullName))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Safer CAD-aware mode for templates with unrelated annotations: scans the drawing,
    /// then only deletes matching annotations from the known template superset.
    /// </summary>
    public static IReadOnlyList<DeletionTarget> PlanDeletionsFromDrawing_FromKnownSuperset(
        ModelDoc2 drawingModel,
        DrawingType drawingType,
        ShankType shankType,
        FootOption footOption,
        Options? options = null,
        ViewNameMap? viewNames = null,
        bool activateEachView = true)
    {
        if (drawingModel is null) throw new ArgumentNullException(nameof(drawingModel));
        if (!RulesEnabled) return Empty();

        options ??= new Options();
        viewNames ??= new ViewNameMap();

        var existingByView = AnnotationDeletionCore.CollectExistingDisplayDimensionFullNamesByView(
            drawingModel, viewNames.ToCore(), activateEachView);

        return GetExistingAnnotationsToDelete_FromKnownSuperset(
            drawingType, shankType, footOption, existingByView, options, viewNames);
    }

    private static HashSet<AnnotationDeletionCore.Ann> BuildKeepSet(
        DrawingType drawingType,
        ShankType shankType,
        FootOption footOption,
        Options options)
        => drawingType switch
        {
            DrawingType.Pgb => BuildKeepPgb(shankType, options),
            DrawingType.Production => BuildKeepProductionOrCustomer(shankType, footOption, options, isCustomer: false),
            DrawingType.Customer => BuildKeepProductionOrCustomer(shankType, footOption, options, isCustomer: true),
            _ => throw new ArgumentOutOfRangeException(nameof(drawingType), drawingType, null)
        };

    private static HashSet<AnnotationDeletionCore.Ann> BuildKeepPgb(ShankType shank, Options opt)
    {
        var keep = new HashSet<AnnotationDeletionCore.Ann>();
        var fs = FrontSketch(shank);

        KeepFront(keep, shank, opt, includeVr: false);
        KeepSide(keep, shank, opt, allowVbl: false);
        KeepTop(keep, shank);
        KeepDetailBase(keep, opt, isProduction: false);

        Add(keep, V.Section, $"T@{fs}");
        Add(keep, V.Section, $"FD@{fs}");
        Add(keep, V.Section, $"RA@{fs}");

        return keep;
    }

    private static HashSet<AnnotationDeletionCore.Ann> BuildKeepProductionOrCustomer(
        ShankType shank,
        FootOption foot,
        Options opt,
        bool isCustomer)
    {
        var keep = new HashSet<AnnotationDeletionCore.Ann>();
        var fs = FrontSketch(shank);

        KeepFront(keep, shank, opt, includeVr: true);
        KeepSide(keep, shank, opt, allowVbl: true);
        KeepTop(keep, shank);
        KeepDetailBase(keep, opt, isProduction: !isCustomer);

        Add(keep, V.Section, $"T@{fs}");
        Add(keep, V.Section, $"H@{fs}");
        Add(keep, V.Section, $"HA@{fs}");
        Add(keep, V.Section, $"FNA@{fs}");
        Add(keep, V.Section, $"RA@{fs}");

        if (opt.HasRa2)
            Add(keep, V.Section, $"RA2@{fs}");

        if (!isCustomer)
        {
            Add(keep, V.Section, $"FD@{fs}");
            Add(keep, V.Section, $"ERL@{fs}");
            Add(keep, V.Section, $"CA@{fs}");
            Add(keep, V.Section, $"BA@{fs}");

            if (opt.HasErd)
                Add(keep, V.Section, ResolveAnnotationName(opt.ErdAnnotationFullName, $"ERD@{fs}"));
        }

        KeepFootOptionDetail(keep, foot, isCustomer, opt);
        KeepFootOptionSection(keep, shank, foot, opt, isCustomer);

        return keep;
    }

    private static void KeepFront(HashSet<AnnotationDeletionCore.Ann> keep, ShankType shank, Options opt, bool includeVr)
    {
        Add(keep, V.Front, $"TL@{FrontSketch(shank)}");
        Add(keep, V.Front, "TL@part_axis");
        Add(keep, V.Front, ResolveAnnotationName(opt.KAnnotationFullName, "K@Engraving"));

        if (includeVr && opt.HasVwVr)
            Add(keep, V.Front, "VR@ANNOT_LEFT_sketch");
    }

    private static void KeepSide(HashSet<AnnotationDeletionCore.Ann> keep, ShankType shank, Options opt, bool allowVbl)
    {
        Add(keep, V.Side, $"BA@{FrontSketch(shank)}");
        Add(keep, V.Side, $"TL@{FrontSketch(shank)}");

        if (allowVbl && opt.HasSlb)
            Add(keep, V.Side, $"VBL@{FrontSketch(shank)}");
    }

    private static void KeepTop(HashSet<AnnotationDeletionCore.Ann> keep, ShankType shank)
    {
        var ts = TopSketch(shank);
        Add(keep, V.Top, $"TD@{ts}");
        Add(keep, V.Top, $"TDF@{ts}");
    }

    private static void KeepDetailBase(HashSet<AnnotationDeletionCore.Ann> keep, Options opt, bool isProduction)
    {
        Add(keep, V.Detail, "W@ANNOT_LEFT_sketch");

        if (!opt.HasVwVr)
            Add(keep, V.Detail, "ISA@ANNOT_LEFT_sketch");

        if (opt.HasVwVr)
        {
            Add(keep, V.Detail, "VW@ANNOT_LEFT_sketch");
            Add(keep, V.Detail, "VR@ANNOT_LEFT_sketch");
            Add(keep, V.Detail, "VRA@ANNOT_LEFT_sketch");
        }

        if (opt.HasW2) Add(keep, V.Detail, "W2@ANNOT_LEFT_sketch");
        if (opt.HasGa) Add(keep, V.Detail, "GA@ANNOT_FOOT_OPTIONS_LEFT_sketch");
        if (opt.HasCd) Add(keep, V.Detail, "CD@ANNOT_FOOT_OPTIONS_LEFT_sketch");

        if (isProduction)
        {
            if (opt.HasGd) Add(keep, V.Detail, "GD@ANNOT_FOOT_OPTIONS_LEFT_sketch");
            if (opt.HasB) Add(keep, V.Detail, "B@ANNOT_FOOT_OPTIONS_LEFT_sketch");
        }
    }

    private static void KeepFootOptionDetail(
        HashSet<AnnotationDeletionCore.Ann> keep,
        FootOption foot,
        bool isCustomer,
        Options opt)
    {
        if (isCustomer || !opt.HasGr) return;

        if (foot == FootOption.G) Add(keep, V.Detail, "GR_G@ANNOT_FOOT_OPTIONS_LEFT_sketch");
        if (foot == FootOption.VG) Add(keep, V.Detail, "GR_VG@ANNOT_FOOT_OPTIONS_LEFT_sketch");
    }

    private static void KeepFootOptionSection(
        HashSet<AnnotationDeletionCore.Ann> keep,
        ShankType shank,
        FootOption foot,
        Options opt,
        bool isCustomer)
    {
        var fs = FrontSketch(shank);
        var frbr = FrBrSketch(shank);

        if (opt.HasF)
        {
            var flDim = FlDimForFoot(foot);
            if (flDim is not null)
                Add(keep, V.Section, $"{flDim}@{fs}");
        }

        if (!isCustomer && (foot == FootOption.CG || foot == FootOption.CC))
        {
            if (opt.HasG) Add(keep, V.Section, $"G@{fs}");
            if (opt.HasCgr) Add(keep, V.Section, $"CGR@{fs}");
            if (opt.HasCgd) Add(keep, V.Section, $"CGD@{fs}");

            if (shank == ShankType.Deg180Rev)
            {
                const string typo = "ANNOT_180_DEG_REV_FRONT_FRONT_sketch";
                if (opt.HasG) Add(keep, V.Section, $"G@{typo}");
                if (opt.HasCgr) Add(keep, V.Section, $"CGR@{typo}");
                if (opt.HasCgd) Add(keep, V.Section, $"CGD@{typo}");
            }
        }

        if (isCustomer && opt.HasG && (foot == FootOption.CG || foot == FootOption.CC))
            Add(keep, V.Section, $"G@{fs}");

        if (!isCustomer && foot == FootOption.C_WITH_CBR)
        {
            if (opt.HasCbra) Add(keep, V.Section, $"CBRA@{fs}");
            if (opt.HasCbrl) Add(keep, V.Section, $"CBRL@{fs}");
        }

        var frBrSuffix = FrBrSuffixForFoot(foot);
        if (frBrSuffix is not null)
        {
            if (opt.HasFr) Add(keep, V.Section, $"FR_{frBrSuffix}@{frbr}");
            if (opt.HasBr) Add(keep, V.Section, $"BR_{frBrSuffix}@{frbr}");
        }
    }

    private static HashSet<AnnotationDeletionCore.Ann> BuildAllKnownAnnotations()
    {
        var all = new HashSet<AnnotationDeletionCore.Ann>();

        foreach (var shank in BothShanks)
        {
            var fs = FrontSketch(shank);
            var ts = TopSketch(shank);
            var frbr = FrBrSketch(shank);

            Add(all, V.Front, $"TL@{fs}");
            Add(all, V.Front, "TL@part_axis");
            Add(all, V.Front, "TL@ANNOT_LEFT_sketch");
            Add(all, V.Front, "VR@ANNOT_LEFT_sketch");

            Add(all, V.Side, $"BA@{fs}");
            Add(all, V.Side, $"TL@{fs}");
            Add(all, V.Side, $"VBL@{fs}");

            Add(all, V.Top, $"TD@{ts}");
            Add(all, V.Top, $"TDF@{ts}");

            foreach (var dim in AllSectionCavityDims)
                Add(all, V.Section, $"{dim}@{fs}");

            foreach (var dim in new[] { "FL_C", "FL_G", "FL_VG" })
                Add(all, V.Section, $"{dim}@{fs}");

            foreach (var dim in CgDims)
                Add(all, V.Section, $"{dim}@{fs}");

            Add(all, V.Section, $"CBRA@{fs}");
            Add(all, V.Section, $"CBRL@{fs}");

            foreach (var suffix in FrBrSuffixes)
            {
                Add(all, V.Section, $"FR_{suffix}@{frbr}");
                Add(all, V.Section, $"BR_{suffix}@{frbr}");
            }

            if (shank == ShankType.Deg180Rev)
            {
                foreach (var dim in CgDims)
                    Add(all, V.Section, $"{dim}@ANNOT_180_DEG_REV_FRONT_FRONT_sketch");
            }
        }

        Add(all, V.Front, "K@Engraving");
        Add(all, V.Front, "K@ANNOT_LEFT_sketch");

        foreach (var dim in new[] { "W", "ISA", "VW", "VR", "VRA", "W2" })
            Add(all, V.Detail, $"{dim}@ANNOT_LEFT_sketch");

        foreach (var dim in new[] { "CD", "CR", "GD", "GR_G", "GR_VG", "GA", "B" })
            Add(all, V.Detail, $"{dim}@ANNOT_FOOT_OPTIONS_LEFT_sketch");

        return all;
    }

    public static string FrontSketch(ShankType shank)
        => shank == ShankType.Std ? "ANNOT_STD_FRONT_sketch" : "ANNOT_180_DEG_REV_FRONT_sketch";

    public static string TopSketch(ShankType shank)
        => shank == ShankType.Std ? "ANNOT_STD_TOP_sketch" : "ANNOT_180_DEG_REV_TOP_sketch";

    public static string FrBrSketch(ShankType shank)
        => shank == ShankType.Std ? "ANNOT_FR_BR_STD_FRONT_sketch" : "ANNOT_FR_BR_180_DEG_REV_FRONT_sketch";

    private static string? FlDimForFoot(FootOption foot) => foot switch
    {
        FootOption.C => "FL_C",
        FootOption.G => "FL_G",
        FootOption.VG => "FL_VG",
        FootOption.CC => "FL_C",
        FootOption.C_WITH_CBR => "FL_C",
        FootOption.CG => null,
        FootOption.None => null,
        _ => throw new ArgumentOutOfRangeException(nameof(foot), foot, null)
    };

    private static string? FrBrSuffixForFoot(FootOption foot) => foot switch
    {
        FootOption.C => "C",
        FootOption.G => "G",
        FootOption.VG => "VG",
        FootOption.CG => "C",
        FootOption.CC => "C",
        FootOption.C_WITH_CBR => "C",
        FootOption.None => null,
        _ => throw new ArgumentOutOfRangeException(nameof(foot), foot, null)
    };

    private static string ResolveAnnotationName(string? overrideName, string defaultName)
        => string.IsNullOrWhiteSpace(overrideName) ? defaultName : overrideName.Trim();

    private static void Add(HashSet<AnnotationDeletionCore.Ann> set, AnnotationDeletionCore.ViewKind view, string fullName)
        => set.Add(new AnnotationDeletionCore.Ann(view, fullName));

    private static IReadOnlyList<DeletionTarget> Empty()
        => new ReadOnlyCollection<DeletionTarget>(new List<DeletionTarget>());

    private static readonly string[] AllSectionCavityDims =
        { "T", "FD", "ERL", "ERD", "CA", "H", "HA", "FNA", "RA", "RA2", "BA" };

    private static readonly string[] CgDims = { "G", "CGR", "CGD" };
    private static readonly string[] FrBrSuffixes = { "C", "G", "VG" };
    private static readonly ShankType[] BothShanks = { ShankType.Std, ShankType.Deg180Rev };

    private static class V
    {
        internal static readonly AnnotationDeletionCore.ViewKind Front = AnnotationDeletionCore.ViewKind.Front;
        internal static readonly AnnotationDeletionCore.ViewKind Side = AnnotationDeletionCore.ViewKind.Side;
        internal static readonly AnnotationDeletionCore.ViewKind Top = AnnotationDeletionCore.ViewKind.Top;
        internal static readonly AnnotationDeletionCore.ViewKind Detail = AnnotationDeletionCore.ViewKind.Detail;
        internal static readonly AnnotationDeletionCore.ViewKind Section = AnnotationDeletionCore.ViewKind.Section;
    }
}
