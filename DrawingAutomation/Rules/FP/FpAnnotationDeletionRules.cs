// DrawingAutomation/Rules/FP/FpAnnotationDeletionRules.cs
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using WAD.Runner.Application;
using WAD.Runner.DrawingAutomation.Rules.Common;

namespace WAD.Runner.DrawingAutomation.Rules.FP;

/// <summary>
/// Plans which FP drawing annotations to delete for a given drawing type,
/// shank orientation, and foot option.
/// </summary>
/// <remarks>
/// The rule engine works in two phases:
/// <list type="number">
///   <item>Build a <em>keep-set</em> — every annotation that must survive.</item>
///   <item>Delete everything else (or intersect with the known superset for safety).</item>
/// </list>
///
/// ── SKETCH → ANNOTATION NAMING ──────────────────────────────────────────
///
/// Annotations are referenced as <c>DIM_NAME@SKETCH_NAME</c>.
/// Sketch names depend on shank orientation:
/// <code>
///   Sketch role   STD                                  180° REV
///   ──────────────────────────────────────────────────────────────────────
///   Front         ANNOT_STD_FRONT_sketch               ANNOT_180_DEG_REV_FRONT_sketch
///   Top           ANNOT_STD_TOP_sketch                 ANNOT_180_DEG_REV_TOP_sketch
///   FR/BR         ANNOT_FR_BR_STD_FRONT_sketch         ANNOT_FR_BR_180_DEG_REV_FRONT_sketch
///   Left          ANNOT_LEFT_sketch                    (shank-independent)
///   Foot-opt left ANNOT_FOOT_OPTIONS_LEFT_sketch       (shank-independent)
/// </code>
///
/// ── WHAT EACH VIEW ALWAYS KEEPS, BY DRAWING TYPE ────────────────────────
/// <code>
///   View    │ PGB           │ Production                              │ Customer
///   ────────┼───────────────┼─────────────────────────────────────────┼──────────────────────────
///   Front   │ TL, K         │ TL, K                                   │ TL, K
///   Side    │ BA            │ BA                                      │ BA
///   Top     │ TD, TDF       │ TD, TDF                                 │ TD, TDF
///   Detail  │ W, ISA        │ W, ISA                                  │ W, ISA
///   Section │ T, FD, RA     │ T, FD, ERL, CA, H, FNA, HA, RA, BA     │ T, FD, H, HA, FNA, RA
///           │               │ + foot-option dims + FR/BR              │ + foot-option dims + FR/BR
/// </code>
///
/// ── CONDITIONAL ANNOTATIONS (shown only when dimension > 0) ─────────────
/// <code>
///   View    │ PGB  │ Production                              │ Customer
///   ────────┼──────┼─────────────────────────────────────────┼──────────────────────────
///   Front   │ —    │ VR                                      │ VR
///   Side    │ —    │ VBL                                     │ VBL
///   Detail  │ —    │ VW, VRA, W2, GA, CD, GD, GR_G, GR_VG,  │ VW, VRA, W2, GA, CD
///           │      │ B                                        │
///   Section │ —    │ FL_C/G/VG, G, CBRA, CBRL, CGR, CGD, RA2│ FL_C/G/VG, G, RA2
/// </code>
///
/// ── FOOT OPTION RULES ────────────────────────────────────────────────────
/// <code>
///   Foot        │ Section FL dim  │ Section extra dims (Prod only) │ FR/BR suffix
///   ────────────┼─────────────────┼────────────────────────────────┼─────────────
///   None        │ —               │ —                              │ —
///   C           │ FL_C (if F>0)   │ —                              │ C
///   G           │ FL_G (if F>0)   │ —                              │ G
///   VG          │ FL_VG (if F>0)  │ —                              │ VG
///   CG          │ —               │ G, CGR, CGD (if each >0)       │ C
///   CC          │ FL_C (if F>0)   │ G, CGR, CGD (if each >0)       │ C
///   C_WITH_CBR  │ FL_C (if F>0)   │ CBRA, CBRL (if each >0)        │ C
/// </code>
/// Note: FL_* are named incorrectly in the 3D model (should be F_*). Handled as FL_* until fixed.
/// Note: FR/BR kept only when HasFrBr is true (FR and BR dimensions > 0).
/// </remarks>
public static class FpAnnotationDeletionRules
{
    // ── Global switch ────────────────────────────────────────────────────

    /// <summary>Set to <c>false</c> to disable all rules (every method returns empty).</summary>
    public static bool RulesEnabled { get; set; } = true;

    // ════════════════════════════════════════════════════════════════════
    // PUBLIC TYPES
    // ════════════════════════════════════════════════════════════════════

    public enum DrawingType { Pgb, Production, Customer }
    public enum ShankType { Std, Deg180Rev }
    public enum FootOption { None, C, G, VG, CG, CC, C_WITH_CBR }
    public enum ViewKind { Front, Side, Top, Detail, Section }

    /// <summary>A single annotation scheduled for deletion.</summary>
    public sealed record DeletionTarget(string ViewName, string AnnotationFullName);

    // ── Options ──────────────────────────────────────────────────────────

    /// <summary>
    /// Feature flags that gate optional annotations.
    /// Each flag corresponds to a non-zero dimension value in the part.
    /// </summary>
    public sealed class Options
    {
        // ── Front / Side ─────────────────────────────────────────────────

        /// <summary>VW/VR groove present (VW &gt; 0 AND VR &gt; 0).
        /// Keeps VR in Front; keeps VW, VR, VRA in Detail.</summary>
        public bool HasVwVr { get; init; }

        /// <summary>SLB feature active (VBL &gt; 0). Keeps VBL in Side.</summary>
        public bool HasSlb { get; init; }

        // ── Detail ───────────────────────────────────────────────────────

        /// <summary>Secondary wire width present (W2 &gt; 0). Keeps W2 in Detail.</summary>
        public bool HasW2 { get; init; }

        /// <summary>GA dimension &gt; 0. Keeps GA in Detail.</summary>
        public bool HasGa { get; init; }

        /// <summary>CD dimension &gt; 0. Keeps CD in Detail.</summary>
        public bool HasCd { get; init; }

        /// <summary>GD dimension &gt; 0. Keeps GD in Detail (Production only).</summary>
        public bool HasGd { get; init; }

        /// <summary>GR dimension &gt; 0. Keeps the GR annotation matching the active foot option in Detail (Production only).</summary>
        public bool HasGr { get; init; }

        /// <summary>B dimension &gt; 0. Keeps B in Detail (Production only).</summary>
        public bool HasB { get; init; }

        // ── Section ──────────────────────────────────────────────────────

        /// <summary>Secondary rake angle non-zero (RA2 &gt; 0). Keeps RA2 in Section.</summary>
        public bool HasRa2 { get; init; }

        /// <summary>Edge-relief depth non-zero (ERD &gt; 0).
        /// Keeps ERD in Section (Production only).</summary>
        public bool HasErd { get; init; }

        /// <summary>Front/back relief geometry present (FR &gt; 0 AND BR &gt; 0).
        /// Keeps FR_x / BR_x dims in Section.</summary>
        public bool HasFrBr { get; init; }

        /// <summary>F dimension &gt; 0. Keeps FL_C / FL_G / FL_VG (whichever applies) in Section.</summary>
        public bool HasF { get; init; }

        /// <summary>G dimension &gt; 0. Keeps G in Section for CG/CC foot options.</summary>
        public bool HasG { get; init; }

        /// <summary>CGR dimension &gt; 0. Keeps CGR in Section for CG/CC (Production only).</summary>
        public bool HasCgr { get; init; }

        /// <summary>CGD dimension &gt; 0. Keeps CGD in Section for CG/CC (Production only).</summary>
        public bool HasCgd { get; init; }

        /// <summary>CBRA dimension &gt; 0. Keeps CBRA in Section for C_WITH_CBR (Production only).</summary>
        public bool HasCbra { get; init; }

        /// <summary>CBRL dimension &gt; 0. Keeps CBRL in Section for C_WITH_CBR (Production only).</summary>
        public bool HasCbrl { get; init; }

        /// <summary>
        /// Override the full annotation name for K.
        /// Defaults to <c>"K@Engraving"</c>.
        /// </summary>
        public string? KAnnotationFullName { get; init; }

        /// <summary>
        /// Override the full annotation name for ERD.
        /// Defaults to <c>"ERD@{frontSketch}"</c>.
        /// </summary>
        public string? ErdAnnotationFullName { get; init; }
    }

    // ── ViewNameMap ──────────────────────────────────────────────────────

    /// <summary>
    /// Maps logical view kinds to their actual SolidWorks drawing-view names.
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

        internal AnnotationDeletionCore.ViewNameMap ToCore() => new()
        {
            Front = Front,
            Side = Side,
            Top = Top,
            Detail = Detail,
            Section = Section
        };
    }

    // ════════════════════════════════════════════════════════════════════
    // DIAGNOSTICS
    // ════════════════════════════════════════════════════════════════════

    public static void DumpDeletionPlan(
        string title,
        IReadOnlyList<DeletionTarget> deletions,
        int maxPerView = 200)
    {
        var core = (deletions ?? Array.Empty<DeletionTarget>())
            .Select(d => new AnnotationDeletionCore.DeletionTarget(d.ViewName, d.AnnotationFullName))
            .ToList()
            .AsReadOnly();

        AnnotationDeletionCore.DumpDeletionPlan(title, core, tagPrefix: "FP", maxPerView: maxPerView);
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
            tagPrefix: "FP",
            activateEachView: activateEachView,
            maxPerView: maxPerView);
    }

    // ════════════════════════════════════════════════════════════════════
    // PUBLIC API — CAD-AGNOSTIC
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns deletion candidates derived purely from the rule set,
    /// without scanning an actual SolidWorks document.
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

    /// <summary>
    /// Filters <see cref="GetAnnotationsToDelete"/> to annotations that actually
    /// exist in the drawing (supplied as a pre-scanned dictionary).
    /// </summary>
    public static IReadOnlyList<DeletionTarget> GetExistingAnnotationsToDelete_FromKnownSuperset(
        DrawingType drawingType,
        ShankType shankType,
        FootOption footOption,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> existingByViewName,
        Options? options = null,
        ViewNameMap? viewNames = null)
    {
        if (!RulesEnabled) return Empty();

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

    // ════════════════════════════════════════════════════════════════════
    // PUBLIC API — SOLIDWORKS CAD-AWARE
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scans the drawing and returns every annotation that is NOT in the keep-set.
    /// <para>Uses <see cref="AnnotationDeletionCore.GetExistingMinusKeep"/> — the most
    /// defensive mode: anything not explicitly kept is scheduled for deletion.</para>
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
        ValidateDrawingModel(drawingModel);
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
    /// Scans the drawing and intersects with the known superset — safer when the
    /// drawing may contain unexpected annotation names that should not be touched.
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
        ValidateDrawingModel(drawingModel);
        if (!RulesEnabled) return Empty();

        options ??= new Options();
        viewNames ??= new ViewNameMap();

        var existingByView = AnnotationDeletionCore.CollectExistingDisplayDimensionFullNamesByView(
            drawingModel, viewNames.ToCore(), activateEachView);

        return GetExistingAnnotationsToDelete_FromKnownSuperset(
            drawingType, shankType, footOption, existingByView, options, viewNames);
    }

    // ════════════════════════════════════════════════════════════════════
    // KNOWN SUPERSET
    //
    // Every annotation that can ever appear across all configs and shanks.
    // Anything outside this set is invisible to the rule engine — it will
    // never be scheduled for deletion even in "delete everything else" mode.
    // ════════════════════════════════════════════════════════════════════

    private static HashSet<AnnotationDeletionCore.Ann> BuildAllKnownAnnotations()
    {
        var all = new HashSet<AnnotationDeletionCore.Ann>();

        foreach (var shank in BothShanks)
        {
            var fs = FrontSketch(shank);
            var ts = TopSketch(shank);
            var frbr = FrBrSketch(shank);

            // ── Front view ───────────────────────────────────────────────
            Add(all, V.Front, $"TL@{fs}");
            Add(all, V.Front, "TL@part_axis");
            Add(all, V.Front, "TL@ANNOT_LEFT_sketch");   // older template variant
            Add(all, V.Front, "VR@ANNOT_LEFT_sketch");

            // ── Side view ────────────────────────────────────────────────
            Add(all, V.Side, $"BA@{fs}");
            Add(all, V.Side, $"VBL@{fs}");

            // ── Top view ─────────────────────────────────────────────────
            Add(all, V.Top, $"TD@{ts}");
            Add(all, V.Top, $"TDF@{ts}");

            // ── Section view — always-possible cavity dims ───────────────
            foreach (var dim in AllSectionCavityDims)
                Add(all, V.Section, $"{dim}@{fs}");

            // FL_* variants (named FL_* in model until designer fixes to F_*)
            foreach (var flVariant in new[] { "FL_C", "FL_G", "FL_VG" })
                Add(all, V.Section, $"{flVariant}@{fs}");

            // CG/CC family
            foreach (var dim in CgDims)
                Add(all, V.Section, $"{dim}@{fs}");

            // C_WITH_CBR family
            Add(all, V.Section, $"CBRA@{fs}");
            Add(all, V.Section, $"CBRL@{fs}");

            // FR/BR — all 6 variants (only 2 are ever kept, rest are deleted)
            foreach (var suffix in FrBrSuffixes)
            {
                Add(all, V.Section, $"FR_{suffix}@{frbr}");
                Add(all, V.Section, $"BR_{suffix}@{frbr}");
            }

            // 180° REV only — naming typo in some template revisions
            if (shank == ShankType.Deg180Rev)
                foreach (var dim in CgDims)
                    Add(all, V.Section, $"{dim}@ANNOT_180_DEG_REV_FRONT_FRONT_sketch");
        }

        // ── K (engraving sketch) ─────────────────────────────────────────
        Add(all, V.Front, "K@Engraving");
        Add(all, V.Front, "K@ANNOT_LEFT_sketch");        // older template variant

        // ── Detail view — shank-independent ─────────────────────────────
        foreach (var dim in new[] { "W", "ISA", "VW", "VR", "VRA", "W2" })
            Add(all, V.Detail, $"{dim}@ANNOT_LEFT_sketch");

        foreach (var dim in new[] { "CD", "CR", "GD", "GR_G", "GR_VG", "GA", "B" })
            Add(all, V.Detail, $"{dim}@ANNOT_FOOT_OPTIONS_LEFT_sketch");

        return all;
    }

    // ════════════════════════════════════════════════════════════════════
    // KEEP-SET DISPATCH
    // ════════════════════════════════════════════════════════════════════

    private static HashSet<AnnotationDeletionCore.Ann> BuildKeepSet(
        DrawingType drawingType,
        ShankType shankType,
        FootOption footOption,
        Options options)
        => drawingType switch
        {
            DrawingType.Pgb => BuildKeep_Pgb(shankType, options),
            DrawingType.Production => BuildKeep_ProductionOrCustomer(shankType, footOption, options, isCustomer: false),
            DrawingType.Customer => BuildKeep_ProductionOrCustomer(shankType, footOption, options, isCustomer: true),
            _ => throw new ArgumentOutOfRangeException(nameof(drawingType), drawingType, null)
        };

    // ════════════════════════════════════════════════════════════════════
    // PGB  (minimal — no foot options, no conditional dims)
    //
    // Always kept:
    //   Front   : TL, K
    //   Side    : BA
    //   Top     : TD, TDF
    //   Detail  : W, ISA
    //   Section : T, FD, RA
    // ════════════════════════════════════════════════════════════════════

    private static HashSet<AnnotationDeletionCore.Ann> BuildKeep_Pgb(ShankType shank, Options opt)
    {
        var keep = new HashSet<AnnotationDeletionCore.Ann>();
        var fs = FrontSketch(shank);

        KeepFront(keep, shank, opt, includeVr: false);
        KeepSide(keep, shank, opt, allowVbl: false);
        KeepTop(keep, shank);
        KeepDetail_Base(keep, opt, isProduction: false);

        // Section: reduced set — no foot options for PGB
        Add(keep, V.Section, $"T@{fs}");
        Add(keep, V.Section, $"FD@{fs}");
        Add(keep, V.Section, $"RA@{fs}");

        return keep;
    }

    // ════════════════════════════════════════════════════════════════════
    // PRODUCTION & CUSTOMER  (shared base, diverge in Section and Detail)
    //
    // Always kept (both):
    //   Front   : TL, K
    //   Side    : BA
    //   Top     : TD, TDF
    //   Detail  : W, ISA
    //   Section : T, FD, H, FNA, HA, RA  + foot-option dims + FR/BR
    //
    // Production-only in Section:
    //   ERL, CA, BA, [ERD]
    //
    // Production-only conditional in Detail:
    //   GD, GR_G, GR_VG, B  (G/VG foot options)
    //
    // Production-only conditional in Section:
    //   CGR, CGD (CG/CC foot options), CBRA, CBRL (C_WITH_CBR)
    // ════════════════════════════════════════════════════════════════════

    private static HashSet<AnnotationDeletionCore.Ann> BuildKeep_ProductionOrCustomer(
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
        KeepDetail_Base(keep, opt, isProduction: !isCustomer);

        // ── Section: shared cavity dims ──────────────────────────────────
        Add(keep, V.Section, $"T@{fs}");
        Add(keep, V.Section, $"H@{fs}");
        Add(keep, V.Section, $"HA@{fs}");
        Add(keep, V.Section, $"FNA@{fs}");
        Add(keep, V.Section, $"RA@{fs}");

        if (opt.HasRa2)
            Add(keep, V.Section, $"RA2@{fs}");

        // ── Section: Production-only cavity dims ─────────────────────────
        if (!isCustomer)
        {
            Add(keep, V.Section, $"FD@{fs}");
            Add(keep, V.Section, $"ERL@{fs}");
            Add(keep, V.Section, $"CA@{fs}");
            Add(keep, V.Section, $"BA@{fs}");

            if (opt.HasErd)
                Add(keep, V.Section, ResolveErd(opt, fs));
        }

        // ── Detail + Section: foot-option dims ───────────────────────────
        KeepFootOption_Detail(keep, foot, isCustomer, opt);
        KeepFootOption_Section(keep, shank, foot, opt, isCustomer);

        return keep;
    }

    // ════════════════════════════════════════════════════════════════════
    // SHARED VIEW HELPERS
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Front view: TL (front sketch + part axis) + K.
    /// Optionally VR when <paramref name="includeVr"/> is true and <see cref="Options.HasVwVr"/>.
    /// </summary>
    private static void KeepFront(
        HashSet<AnnotationDeletionCore.Ann> keep,
        ShankType shank,
        Options opt,
        bool includeVr)
    {
        Add(keep, V.Front, $"TL@{FrontSketch(shank)}");
        Add(keep, V.Front, "TL@part_axis");

        var kName = ResolveAnnotationName(opt.KAnnotationFullName, "K@Engraving");
        Add(keep, V.Front, kName);

        if (includeVr && opt.HasVwVr)
            Add(keep, V.Front, "VR@ANNOT_LEFT_sketch");
    }

    /// <summary>
    /// Side view: BA always; VBL when SLB is active and <paramref name="allowVbl"/> is true.
    /// </summary>
    private static void KeepSide(
        HashSet<AnnotationDeletionCore.Ann> keep,
        ShankType shank,
        Options opt,
        bool allowVbl)
    {
        Add(keep, V.Side, $"BA@{FrontSketch(shank)}");

        if (allowVbl && opt.HasSlb)
            Add(keep, V.Side, $"VBL@{FrontSketch(shank)}");
    }

    /// <summary>Top view: TD + TDF (always).</summary>
    private static void KeepTop(HashSet<AnnotationDeletionCore.Ann> keep, ShankType shank)
    {
        var ts = TopSketch(shank);
        Add(keep, V.Top, $"TD@{ts}");
        Add(keep, V.Top, $"TDF@{ts}");
    }

    /// <summary>
    /// Detail view base dims:
    ///   Always : W, ISA
    ///   If HasVwVr : VW, VR, VRA
    ///   If HasW2   : W2
    ///   If HasGa   : GA
    ///   If HasCd   : CD
    ///   Production-only conditionals: GD, B
    ///   GR_G / GR_VG are handled separately based on foot option.
    /// </summary>
    private static void KeepDetail_Base(
        HashSet<AnnotationDeletionCore.Ann> keep,
        Options opt,
        bool isProduction)
    {
        // Always
        Add(keep, V.Detail, "W@ANNOT_LEFT_sketch");
        Add(keep, V.Detail, "ISA@ANNOT_LEFT_sketch");

        // Conditional — both drawing types
        if (opt.HasVwVr)
        {
            Add(keep, V.Detail, "VW@ANNOT_LEFT_sketch");
            Add(keep, V.Detail, "VR@ANNOT_LEFT_sketch");
            Add(keep, V.Detail, "VRA@ANNOT_LEFT_sketch");
        }

        if (opt.HasW2)
            Add(keep, V.Detail, "W2@ANNOT_LEFT_sketch");

        if (opt.HasGa)
            Add(keep, V.Detail, "GA@ANNOT_FOOT_OPTIONS_LEFT_sketch");

        if (opt.HasCd)
            Add(keep, V.Detail, "CD@ANNOT_FOOT_OPTIONS_LEFT_sketch");

        // Conditional — Production only
        if (isProduction)
        {
            if (opt.HasGd)
                Add(keep, V.Detail, "GD@ANNOT_FOOT_OPTIONS_LEFT_sketch");

            if (opt.HasB)
                Add(keep, V.Detail, "B@ANNOT_FOOT_OPTIONS_LEFT_sketch");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // FOOT-OPTION — DETAIL VIEW
    //
    // Foot-option detail dims handled here:
    //   Production only:
    //     G  -> GR_G  (if HasGr)
    //     VG -> GR_VG (if HasGr)
    // ════════════════════════════════════════════════════════════════════

    private static void KeepFootOption_Detail(
        HashSet<AnnotationDeletionCore.Ann> keep,
        FootOption foot,
        bool isCustomer,
        Options opt)
    {
        if (isCustomer)
            return;

        if (!opt.HasGr)
            return;

        switch (foot)
        {
            case FootOption.G:
                Add(keep, V.Detail, "GR_G@ANNOT_FOOT_OPTIONS_LEFT_sketch");
                break;

            case FootOption.VG:
                Add(keep, V.Detail, "GR_VG@ANNOT_FOOT_OPTIONS_LEFT_sketch");
                break;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // FOOT-OPTION — SECTION VIEW
    //
    // FL_* dims: shown when HasF is true (dimension F > 0).
    //            Named FL_C/FL_G/FL_VG in 3D model (will be F_C/F_G/F_VG after fix).
    //
    // FR/BR dims: all 6 variants exist in the drawing. Keep only the 2 that match
    //             the current foot option, when HasFrBr is true.
    //
    // CG extra dims  (G, CGR, CGD)    — Production only, each gated on its own flag.
    // C_WITH_CBR dims (CBRA, CBRL)    — Production only, each gated on its own flag.
    // ════════════════════════════════════════════════════════════════════

    private static void KeepFootOption_Section(
        HashSet<AnnotationDeletionCore.Ann> keep,
        ShankType shank,
        FootOption foot,
        Options opt,
        bool isCustomer)
    {
        var fs = FrontSketch(shank);
        var frbr = FrBrSketch(shank);

        // FL variant (if F > 0) — applies to C, G, VG, CC, C_WITH_CBR
        if (opt.HasF)
        {
            var flDim = FlDimForFoot(foot);
            if (flDim is not null)
                Add(keep, V.Section, $"{flDim}@{fs}");
        }

        // G / CGR / CGD — CG and CC foot options, Production only, each individually gated
        if (!isCustomer && (foot == FootOption.CG || foot == FootOption.CC))
        {
            if (opt.HasG) Add(keep, V.Section, $"G@{fs}");
            if (opt.HasCgr) Add(keep, V.Section, $"CGR@{fs}");
            if (opt.HasCgd) Add(keep, V.Section, $"CGD@{fs}");

            // 180° REV template typo variant
            if (shank == ShankType.Deg180Rev)
            {
                const string typoSketch = "ANNOT_180_DEG_REV_FRONT_FRONT_sketch";
                if (opt.HasG) Add(keep, V.Section, $"G@{typoSketch}");
                if (opt.HasCgr) Add(keep, V.Section, $"CGR@{typoSketch}");
                if (opt.HasCgd) Add(keep, V.Section, $"CGD@{typoSketch}");
            }
        }

        // Customer also keeps G when present (spec lists G as conditional for Customer Section)
        if (isCustomer && opt.HasG && (foot == FootOption.CG || foot == FootOption.CC))
            Add(keep, V.Section, $"G@{fs}");

        // CBRA / CBRL — C_WITH_CBR only, Production only, each individually gated
        if (!isCustomer && foot == FootOption.C_WITH_CBR)
        {
            if (opt.HasCbra) Add(keep, V.Section, $"CBRA@{fs}");
            if (opt.HasCbrl) Add(keep, V.Section, $"CBRL@{fs}");
        }

        // FR / BR — keep only the suffix matching the foot option
        if (opt.HasFrBr)
        {
            var frBrSuffix = FrBrSuffixForFoot(foot);
            if (frBrSuffix is not null)
                AddFrBr(keep, frbr, frBrSuffix);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // MICRO-HELPERS
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns the FL_* dim name for the given foot option, or null if none applies.
    /// Note: named FL_* in the 3D model until the designer renames them to F_*.
    /// </summary>
    private static string? FlDimForFoot(FootOption foot) => foot switch
    {
        FootOption.C => "FL_C",
        FootOption.G => "FL_G",
        FootOption.VG => "FL_VG",
        FootOption.CC => "FL_C",    // CC = C + CG
        FootOption.C_WITH_CBR => "FL_C",
        FootOption.CG => null,      // CG has no FL dim
        FootOption.None => null,
        _ => throw new ArgumentOutOfRangeException(nameof(foot), foot, null)
    };

    /// <summary>
    /// Returns the FR/BR suffix (C, G, VG) for the given foot option,
    /// or null if no FR/BR applies.
    /// </summary>
    private static string? FrBrSuffixForFoot(FootOption foot) => foot switch
    {
        FootOption.C => "C",
        FootOption.G => "G",
        FootOption.VG => "VG",
        FootOption.CG => "C",      // CG uses C-type relief
        FootOption.CC => "C",      // CC uses C-type relief
        FootOption.C_WITH_CBR => "C",
        FootOption.None => null,
        _ => throw new ArgumentOutOfRangeException(nameof(foot), foot, null)
    };

    /// <summary>Adds FR_{suffix} and BR_{suffix} to the Section keep-set.</summary>
    private static void AddFrBr(
        HashSet<AnnotationDeletionCore.Ann> keep, string frbr, string suffix)
    {
        Add(keep, V.Section, $"FR_{suffix}@{frbr}");
        Add(keep, V.Section, $"BR_{suffix}@{frbr}");
    }

    // ════════════════════════════════════════════════════════════════════
    // NAME RESOLUTION
    // ════════════════════════════════════════════════════════════════════

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

    private static string ResolveErd(Options opt, string frontSketch)
        => ResolveAnnotationName(opt.ErdAnnotationFullName, $"ERD@{frontSketch}");

    private static string ResolveAnnotationName(string? overrideName, string defaultName)
        => string.IsNullOrWhiteSpace(overrideName) ? defaultName : overrideName!.Trim();

    // ════════════════════════════════════════════════════════════════════
    // CONSTANTS
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// All section-view cavity dims that can ever appear regardless of drawing type.
    /// This is the union of Production + Customer + PGB section dims.
    /// Used only for building the known superset.
    /// </summary>
    private static readonly string[] AllSectionCavityDims =
    {
        "T", "FD", "ERL", "ERD", "CA", "H", "HA", "FNA", "RA", "RA2", "BA"
    };

    /// <summary>Dim names in the CG/CC family.</summary>
    private static readonly string[] CgDims = { "G", "CGR", "CGD" };

    /// <summary>Foot-option suffixes used in FR/BR annotation names.</summary>
    private static readonly string[] FrBrSuffixes = { "C", "G", "VG" };

    private static readonly ShankType[] BothShanks = { ShankType.Std, ShankType.Deg180Rev };

    // ════════════════════════════════════════════════════════════════════
    // UTILITY
    // ════════════════════════════════════════════════════════════════════

    private static void Add(
        HashSet<AnnotationDeletionCore.Ann> set,
        AnnotationDeletionCore.ViewKind view,
        string fullName)
        => set.Add(new AnnotationDeletionCore.Ann(view, fullName));

    private static IReadOnlyList<DeletionTarget> Empty()
        => new ReadOnlyCollection<DeletionTarget>(new List<DeletionTarget>());

    private static void ValidateDrawingModel(ModelDoc2 drawingModel)
    {
        if (drawingModel is null)
            throw new ArgumentNullException(nameof(drawingModel));

        if (drawingModel.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            throw new ArgumentException(
                "ModelDoc2 must be a Drawing document.", nameof(drawingModel));
    }

    /// <summary>
    /// Local alias for <see cref="AnnotationDeletionCore.ViewKind"/> —
    /// keeps call-sites readable without polluting the public namespace.
    /// </summary>
    private static class V
    {
        internal static readonly AnnotationDeletionCore.ViewKind Front = AnnotationDeletionCore.ViewKind.Front;
        internal static readonly AnnotationDeletionCore.ViewKind Side = AnnotationDeletionCore.ViewKind.Side;
        internal static readonly AnnotationDeletionCore.ViewKind Top = AnnotationDeletionCore.ViewKind.Top;
        internal static readonly AnnotationDeletionCore.ViewKind Detail = AnnotationDeletionCore.ViewKind.Detail;
        internal static readonly AnnotationDeletionCore.ViewKind Section = AnnotationDeletionCore.ViewKind.Section;
    }
}