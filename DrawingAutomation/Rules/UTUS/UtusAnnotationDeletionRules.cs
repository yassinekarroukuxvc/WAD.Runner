using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using WAD.Runner.DrawingAutomation.Rules.Common;

namespace WAD.Runner.DrawingAutomation.Rules.UTUS;

/// <summary>
/// Backwards-compatible facade for UTUS annotation deletion rules.
/// The shared implementation lives in <see cref="SharedAnnotationDeletionRules"/>.
/// </summary>
public static class UtusAnnotationDeletionRules
{
    public static bool RulesEnabled
    {
        get => SharedAnnotationDeletionRules.RulesEnabled;
        set => SharedAnnotationDeletionRules.RulesEnabled = value;
    }

    public enum DrawingType { Pgb, Production, Customer }
    public enum ShankType { Std, Deg180Rev }
    public enum FootOption { None, C, G, VG, CG, CC, C_WITH_CBR }
    public enum ViewKind { Front, Side, Top, Detail, Section }

    public sealed record DeletionTarget(string ViewName, string AnnotationFullName);

    public sealed class Options
    {
        public bool HasVwVr { get; init; }
        public bool HasSlb { get; init; }
        public bool HasW2 { get; init; }
        public bool HasGa { get; init; }
        public bool HasCd { get; init; }
        public bool HasGd { get; init; }
        public bool HasGr { get; init; }
        public bool HasB { get; init; }
        public bool HasRa2 { get; init; }
        public bool HasErd { get; init; }

        // Legacy callers set HasFrBr. Newer callers may set HasFr/HasBr independently.
        public bool HasFr { get; init; }
        public bool HasBr { get; init; }
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
    }

    public static void DumpDeletionPlan(string title, IReadOnlyList<DeletionTarget> deletions, int maxPerView = 200)
        => SharedAnnotationDeletionRules.DumpDeletionPlan("UTUS", title, ToShared(deletions), maxPerView);

    public static void DumpExistingDisplayDimensionFullNamesFromDrawing(
        ModelDoc2 drawingModel,
        ViewNameMap? viewNames = null,
        bool activateEachView = true,
        int maxPerView = 250)
        => SharedAnnotationDeletionRules.DumpExistingDimensionNames(
            "UTUS",
            drawingModel,
            ToShared(viewNames),
            activateEachView,
            maxPerView);

    public static IReadOnlyList<DeletionTarget> GetAnnotationsToDelete(
        DrawingType drawingType,
        ShankType shankType,
        FootOption footOption,
        Options? options = null,
        ViewNameMap? viewNames = null)
        => FromShared(SharedAnnotationDeletionRules.GetAnnotationsToDelete(
            ToShared(drawingType),
            ToShared(shankType),
            ToShared(footOption),
            ToShared(options),
            ToShared(viewNames)));

    public static IReadOnlyList<DeletionTarget> GetExistingAnnotationsToDelete_FromKnownSuperset(
        DrawingType drawingType,
        ShankType shankType,
        FootOption footOption,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> existingByViewName,
        Options? options = null,
        ViewNameMap? viewNames = null)
        => FromShared(SharedAnnotationDeletionRules.GetExistingAnnotationsToDelete_FromKnownSuperset(
            ToShared(drawingType),
            ToShared(shankType),
            ToShared(footOption),
            existingByViewName,
            ToShared(options),
            ToShared(viewNames)));

    public static IReadOnlyList<DeletionTarget> PlanDeletionsFromDrawing(
        ModelDoc2 drawingModel,
        DrawingType drawingType,
        ShankType shankType,
        FootOption footOption,
        Options? options = null,
        ViewNameMap? viewNames = null,
        bool activateEachView = true)
        => FromShared(SharedAnnotationDeletionRules.PlanDeletionsFromDrawing(
            drawingModel,
            ToShared(drawingType),
            ToShared(shankType),
            ToShared(footOption),
            ToShared(options),
            ToShared(viewNames),
            activateEachView));

    public static IReadOnlyList<DeletionTarget> PlanDeletionsFromDrawing_FromKnownSuperset(
        ModelDoc2 drawingModel,
        DrawingType drawingType,
        ShankType shankType,
        FootOption footOption,
        Options? options = null,
        ViewNameMap? viewNames = null,
        bool activateEachView = true)
        => FromShared(SharedAnnotationDeletionRules.PlanDeletionsFromDrawing_FromKnownSuperset(
            drawingModel,
            ToShared(drawingType),
            ToShared(shankType),
            ToShared(footOption),
            ToShared(options),
            ToShared(viewNames),
            activateEachView));

    private static IReadOnlyList<SharedAnnotationDeletionRules.DeletionTarget> ToShared(IReadOnlyList<DeletionTarget>? targets)
        => (targets ?? Array.Empty<DeletionTarget>())
            .Select(t => new SharedAnnotationDeletionRules.DeletionTarget(t.ViewName, t.AnnotationFullName))
            .ToList()
            .AsReadOnly();

    private static IReadOnlyList<DeletionTarget> FromShared(IReadOnlyList<SharedAnnotationDeletionRules.DeletionTarget> targets)
        => targets.Select(t => new DeletionTarget(t.ViewName, t.AnnotationFullName)).ToList().AsReadOnly();

    private static SharedAnnotationDeletionRules.Options ToShared(Options? options)
    {
        if (options is null) return new SharedAnnotationDeletionRules.Options();

        var hasFr = options.HasFr || options.HasFrBr;
        var hasBr = options.HasBr || options.HasFrBr;

        return new SharedAnnotationDeletionRules.Options
        {
            HasVwVr = options.HasVwVr,
            HasSlb = options.HasSlb,
            HasW2 = options.HasW2,
            HasGa = options.HasGa,
            HasCd = options.HasCd,
            HasGd = options.HasGd,
            HasGr = options.HasGr,
            HasB = options.HasB,
            HasRa2 = options.HasRa2,
            HasErd = options.HasErd,
            HasFr = hasFr,
            HasBr = hasBr,
            HasF = options.HasF,
            HasG = options.HasG,
            HasCgr = options.HasCgr,
            HasCgd = options.HasCgd,
            HasCbra = options.HasCbra,
            HasCbrl = options.HasCbrl,
            KAnnotationFullName = options.KAnnotationFullName,
            ErdAnnotationFullName = options.ErdAnnotationFullName
        };
    }

    private static SharedAnnotationDeletionRules.ViewNameMap ToShared(ViewNameMap? viewNames)
        => viewNames is null
            ? new SharedAnnotationDeletionRules.ViewNameMap()
            : new SharedAnnotationDeletionRules.ViewNameMap
            {
                Front = viewNames.Front,
                Side = viewNames.Side,
                Top = viewNames.Top,
                Detail = viewNames.Detail,
                Section = viewNames.Section
            };

    private static SharedAnnotationDeletionRules.DrawingType ToShared(DrawingType value)
        => (SharedAnnotationDeletionRules.DrawingType)(int)value;

    private static SharedAnnotationDeletionRules.ShankType ToShared(ShankType value)
        => (SharedAnnotationDeletionRules.ShankType)(int)value;

    private static SharedAnnotationDeletionRules.FootOption ToShared(FootOption value)
        => (SharedAnnotationDeletionRules.FootOption)(int)value;
}
