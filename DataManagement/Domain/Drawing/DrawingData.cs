using System.Collections.Generic;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Drawing;

/// <summary>
/// CAD-agnostic drawing configuration used to plan views/annotations.
/// </summary>
public sealed class DrawingData
{
    public DrawingType DrawingType { get; }
    public WedgeSubclass Subclass { get; }
    public string ArticleNumber { get; }

    /// <summary>Per-view layout keyed by logical view name ("Front","Side","Top","Detail","Section","Overlay").</summary>
    public IReadOnlyDictionary<string, ViewConfig> Views { get; }

    /// <summary>Optional table placements by logical table name ("Dimension","HowToOrder","Polish",...).</summary>
    public IReadOnlyDictionary<string, TableConfig> Tables { get; }

    /// <summary>Misc metadata (title, number, notes, etc.).</summary>
    public IReadOnlyDictionary<string, string?> Metadata { get; }

    public DrawingData(
        DrawingType drawingType,
        WedgeSubclass subclass,
        string articleNumber,
        IReadOnlyDictionary<string, ViewConfig> views,
        IReadOnlyDictionary<string, TableConfig>? tables = null,
        IReadOnlyDictionary<string, string?>? metadata = null)
    {
        DrawingType = drawingType;
        Subclass = subclass;
        ArticleNumber = string.IsNullOrWhiteSpace(articleNumber) ? "-" : articleNumber.Trim();
        Views = views ?? new Dictionary<string, ViewConfig>();
        Tables = tables ?? new Dictionary<string, TableConfig>();
        Metadata = metadata ?? new Dictionary<string, string?>();
    }
}
