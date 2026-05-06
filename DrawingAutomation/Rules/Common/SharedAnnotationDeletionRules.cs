// DrawingAutomation/Rules/Common/SharedAnnotationDeletionRules.cs
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using WAD.Runner.Application;

namespace WAD.Runner.DrawingAutomation.Rules.Common;

/// <summary>
/// Shared annotation deletion rules for COB, UTUS, and FP wedge types.
///
/// Previously these three types each had their own identical rules class (CobAnnotationDeletionRules,
/// UtusAnnotationDeletionRules, FpAnnotationDeletionRules). The rule bodies, enums, Options class,
/// ViewNameMap, superset, and keep-set logic were byte-for-byte copies.
///
/// This class contains the logic once. Thin wrappers in COB/, UTUS/, FP/ delegate here
/// with their wedge-specific tag prefix, so log output and the public API surface remain clean.
///
/// ── TEMPLATE ANNOTATION NAMING ──────────────────────────────────────────
/// Annotations are referenced as DIM_NAME@SKETCH_NAME.
/// Sketch names depend on shank orientation:
///
///   Sketch role   STD                               180° REV
///   ─────────────────────────────────────────────────────────────────────
///   Front         ANNOT_STD_FRONT_sketch            ANNOT_180_DEG_REV_FRONT_sketch
///   Top           ANNOT_STD_TOP_sketch              ANNOT_180_DEG_REV_TOP_sketch
///   FR/BR         ANNOT_FR_BR_STD_FRONT_sketch      ANNOT_FR_BR_180_DEG_REV_FRONT_sketch
///   Left          ANNOT_LEFT_sketch                 (shank-independent)
///   Foot-opt left ANNOT_FOOT_OPTIONS_LEFT_sketch    (shank-independent)
///
/// ── KEEP SETS BY DRAWING TYPE ───────────────────────────────────────────
///
///   View    │ PGB       │ Production                              │ Customer
///   ────────┼───────────┼─────────────────────────────────────────┼──────────────────────
///   Front   │ TL, K     │ TL, K                                   │ TL, K
///   Side    │ BA        │ BA                                      │ BA
///   Top     │ TD, TDF   │ TD, TDF                                 │ TD, TDF
///   Detail  │ W, ISA    │ W, ISA                                  │ W, ISA
///   Section │ T, FD, RA │ T, FD, ERL, CA, H, FNA, HA, RA, BA     │ T, FD, H, HA, FNA, RA
///           │           │ + foot-option dims + FR/BR              │ + foot-option dims + FR/BR
/// </summary>
public static class SharedAnnotationDeletionRules
{
    // ── Global switch ─────────────────────────────────────────────────
    public static bool RulesEnabled { get; set; } = true;

    // ── Public types ──────────────────────────────────────────────────

    public enum DrawingType { Pgb, Production, Customer }
    public enum ShankType { Std, Deg180Rev }
    public enum FootOption { None, C, G, VG, CG, CC, C_WITH_CBR }
    public enum ViewKind { Front, Side, Top, Detail, Section }

    public sealed record DeletionTarget(string ViewName, string AnnotationFullName);

    public sealed class Options
    {
        // Front / Side
        public bool HasVwVr { get; init; }
        public bool HasSlb { get; init; }

        // Detail
        public bool HasW2 { get; init; }
        public bool HasGa { get; init; }
        public bool HasCd { get; init; }
        public bool HasGd { get; init; }
        public bool HasGr { get; init; }
        public bool HasB { get; init; }

        // Section
        public bool HasRa2 { get; init; }
        public bool HasErd { get; init; }
        public bool HasFrBr { get; init; }
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

    // ── Public API ────────────────────────────────────────────────────

    public static void DumpDeletionPlan(
        string tagPrefix,
        string title,
        IReadOnlyList<DeletionTarget> deletions,
        int maxPerView = 200)
    {
        var core = (deletions ?? Array.Empty<DeletionTarget>())
            .Select(d => new AnnotationDeletionCore.DeletionTarget(d.ViewName, d.AnnotationFullName))
            .ToList().AsReadOnly();

        AnnotationDeletionCore.DumpDeletionPlan(title, core, tagPrefix: tagPrefix, maxPerView: maxPerView);
    }

    public static void DumpExistingDimensionNames(
        string tagPrefix,
        ModelDoc2 drawingModel,
        ViewNameMap? viewNames = null,
        bool activateEachView = true,
        int maxPerView = 250)
    {
        AnnotationDeletionCore.DumpExistingDisplayDimensionFullNamesFromDrawing(
            drawingModel,
            (viewNames ?? new ViewNameMap()).ToCore(),
            tagPrefix: tagPrefix,
            activateEachView: activateEachView,
            maxPerView: maxPerView);
    }

    /// <summary>
    /// Scans the drawing and returns every annotation that is NOT in the keep-set.
    /// Anything not explicitly kept is scheduled for deletion.
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
            .ToList().AsReadOnly();
    }

    // ── Keep-set dispatch ─────────────────────────────────────────────

    private static HashSet<AnnotationDeletionCore.Ann> BuildKeepSet(
        DrawingType drawingType, ShankType shankType, FootOption footOption, Options options)
        => drawingType switch
        {
            DrawingType.Pgb => BuildKeep_Pgb(shankType, options),
            DrawingType.Production => BuildKeep_ProductionOrCustomer(shankType, footOption, options, isCustomer: false),
            DrawingType.Customer => BuildKeep_ProductionOrCustomer(shankType, footOption, options, isCustomer: true),
            _ => throw new ArgumentOutOfRangeException(nameof(drawingType), drawingType, null)
        };

    // ── PGB keep-set ──────────────────────────────────────────────────

    private static HashSet<AnnotationDeletionCore.Ann> BuildKeep_Pgb(ShankType shank, Options opt)
    {
        var keep = new HashSet<AnnotationDeletionCore.Ann>();
        var fs = FrontSketch(shank);

        KeepFront(keep, shank, opt, includeVr: false);
        KeepSide(keep, shank, opt, allowVbl: false);
        KeepTop(keep, shank);
        KeepDetail_Base(keep, opt, isProduction: false);

        Add(keep, V.Section, $"T@{fs}");
        Add(keep, V.Section, $"FD@{fs}");
        Add(keep, V.Section, $"RA@{fs}");

        return keep;
    }

    // ── Production + Customer keep-set ────────────────────────────────

    private static HashSet<AnnotationDeletionCore.Ann> BuildKeep_ProductionOrCustomer(
        ShankType shank, FootOption foot, Options opt, bool isCustomer)
    {
        var keep = new HashSet<AnnotationDeletionCore.Ann>();
        var fs = FrontSketch(shank);

        KeepFront(keep, shank, opt, includeVr: true);
        KeepSide(keep, shank, opt, allowVbl: true);
        KeepTop(keep, shank);
        KeepDetail_Base(keep, opt, isProduction: !isCustomer);

        // Section: shared
        Add(keep, V.Section, $"T@{fs}");
        Add(keep, V.Section, $"H@{fs}");
        Add(keep, V.Section, $"HA@{fs}");
        Add(keep, V.Section, $"FNA@{fs}");
        Add(keep, V.Section, $"RA@{fs}");

        if (opt.HasRa2) Add(keep, V.Section, $"RA2@{fs}");

        // Section: Production only
        if (!isCustomer)
        {
            Add(keep, V.Section, $"FD@{fs}");
            Add(keep, V.Section, $"ERL@{fs}");
            Add(keep, V.Section, $"CA@{fs}");
            Add(keep, V.Section, $"BA@{fs}");

            if (opt.HasErd)
                Add(keep, V.Section, ResolveAnnotationName(opt.ErdAnnotationFullName, $"ERD@{fs}"));
        }

        KeepFootOption_Detail(keep, foot, isCustomer, opt);
        KeepFootOption_Section(keep, shank, foot, opt, isCustomer);

        return keep;
    }

    // ── Shared view helpers ───────────────────────────────────────────

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

    private static void KeepDetail_Base(HashSet<AnnotationDeletionCore.Ann> keep, Options opt, bool isProduction)
    {
        Add(keep, V.Detail, "W@ANNOT_LEFT_sketch");
        Add(keep, V.Detail, "ISA@ANNOT_LEFT_sketch");

        if (opt.HasVwVr)
        {
            Add(keep, V.Detail, "VW@ANNOT_LEFT_sketch");
            Add(keep, V.Detail, "VR@ANNOT_LEFT_sketch");
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

    private static void KeepFootOption_Detail(
        HashSet<AnnotationDeletionCore.Ann> keep, FootOption foot, bool isCustomer, Options opt)
    {
        if (isCustomer || !opt.HasGr) return;

        if (foot == FootOption.G) Add(keep, V.Detail, "GR_G@ANNOT_FOOT_OPTIONS_LEFT_sketch");
        if (foot == FootOption.VG) Add(keep, V.Detail, "GR_VG@ANNOT_FOOT_OPTIONS_LEFT_sketch");
    }

    private static void KeepFootOption_Section(
        HashSet<AnnotationDeletionCore.Ann> keep, ShankType shank, FootOption foot, Options opt, bool isCustomer)
    {
        var fs = FrontSketch(shank);
        var frbr = FrBrSketch(shank);

        // FL_* (when F > 0)
        if (opt.HasF)
        {
            var flDim = FlDimForFoot(foot);
            if (flDim is not null) Add(keep, V.Section, $"{flDim}@{fs}");
        }

        // CG/CC dims — Production only
        if (!isCustomer && (foot == FootOption.CG || foot == FootOption.CC))
        {
            if (opt.HasG) Add(keep, V.Section, $"G@{fs}");
            if (opt.HasCgr) Add(keep, V.Section, $"CGR@{fs}");
            if (opt.HasCgd) Add(keep, V.Section, $"CGD@{fs}");

            // Template typo variant in some 180° REV revisions
            if (shank == ShankType.Deg180Rev)
            {
                const string typo = "ANNOT_180_DEG_REV_FRONT_FRONT_sketch";
                if (opt.HasG) Add(keep, V.Section, $"G@{typo}");
                if (opt.HasCgr) Add(keep, V.Section, $"CGR@{typo}");
                if (opt.HasCgd) Add(keep, V.Section, $"CGD@{typo}");
            }
        }

        // Customer also keeps G for CG/CC
        if (isCustomer && opt.HasG && (foot == FootOption.CG || foot == FootOption.CC))
            Add(keep, V.Section, $"G@{fs}");

        // C_WITH_CBR dims — Production only
        if (!isCustomer && foot == FootOption.C_WITH_CBR)
        {
            if (opt.HasCbra) Add(keep, V.Section, $"CBRA@{fs}");
            if (opt.HasCbrl) Add(keep, V.Section, $"CBRL@{fs}");
        }

        // FR/BR — keep only the matching suffix
        if (opt.HasFrBr)
        {
            var suffix = FrBrSuffixForFoot(foot);
            if (suffix is not null)
            {
                Add(keep, V.Section, $"FR_{suffix}@{frbr}");
                Add(keep, V.Section, $"BR_{suffix}@{frbr}");
            }
        }
    }

    // ── Name helpers ──────────────────────────────────────────────────

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
        => string.IsNullOrWhiteSpace(overrideName) ? defaultName : overrideName!.Trim();

    // ── Constants ─────────────────────────────────────────────────────

    private static readonly string[] AllSectionCavityDims =
        { "T", "FD", "ERL", "ERD", "CA", "H", "HA", "FNA", "RA", "RA2", "BA" };

    private static readonly string[] CgDims = { "G", "CGR", "CGD" };
    private static readonly string[] FrBrSuffixes = { "C", "G", "VG" };
    private static readonly ShankType[] BothShanks = { ShankType.Std, ShankType.Deg180Rev };

    // ── Utility ───────────────────────────────────────────────────────

    private static void Add(HashSet<AnnotationDeletionCore.Ann> set, AnnotationDeletionCore.ViewKind view, string fullName)
        => set.Add(new AnnotationDeletionCore.Ann(view, fullName));

    private static IReadOnlyList<DeletionTarget> Empty()
        => new ReadOnlyCollection<DeletionTarget>(new List<DeletionTarget>());

    // Local view kind aliases for readability
    private static class V
    {
        internal static readonly AnnotationDeletionCore.ViewKind Front = AnnotationDeletionCore.ViewKind.Front;
        internal static readonly AnnotationDeletionCore.ViewKind Side = AnnotationDeletionCore.ViewKind.Side;
        internal static readonly AnnotationDeletionCore.ViewKind Top = AnnotationDeletionCore.ViewKind.Top;
        internal static readonly AnnotationDeletionCore.ViewKind Detail = AnnotationDeletionCore.ViewKind.Detail;
        internal static readonly AnnotationDeletionCore.ViewKind Section = AnnotationDeletionCore.ViewKind.Section;
    }
}
