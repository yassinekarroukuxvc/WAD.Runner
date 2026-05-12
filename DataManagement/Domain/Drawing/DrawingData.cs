using System.Collections.Generic;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Drawing;

public sealed class DrawingData
{
    public DrawingType DrawingType { get; }
    public WedgeSubclass Subclass { get; }
    public string ArticleNumber { get; }

    public IReadOnlyDictionary<string, ViewConfig> Views { get; }

    public IReadOnlyDictionary<string, TableConfig> Tables { get; }

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
