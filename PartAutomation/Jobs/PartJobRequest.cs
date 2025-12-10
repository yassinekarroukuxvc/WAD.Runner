using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.PartAutomation.Jobs;

public sealed class PartJobRequest
{
    public string ArticleNumber { get; init; } = string.Empty;
    public WedgeSubclass Subclass { get; init; }
    public DrawingType DrawingType { get; init; }

    public string OutputRoot { get; init; } = string.Empty;
    public string PartTemplatePath { get; init; } = string.Empty;
    public string EquationTemplatePath { get; init; } = string.Empty;
    public string? FileBase { get; init; }

    public WedgeData WedgeData { get; init; } = null!;

    // 🔹 NEW: wedge type (CKVD / COB)
    public WedgeType WedgeType { get; init; }
}
