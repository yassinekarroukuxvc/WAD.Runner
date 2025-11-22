// PartAutomation/Jobs/PartJobRequest.cs
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.PartAutomation.Jobs;

public sealed class PartJobRequest
{
    public required string ArticleNumber { get; init; }
    public required WedgeSubclass Subclass { get; init; }
    public required DrawingType DrawingType { get; init; }
    public required string OutputRoot { get; init; }
    public required string PartTemplatePath { get; init; }
    public required string EquationTemplatePath { get; init; }
    public WedgeType WedgeType { get; init; } = WedgeType.CKVD;
    public string? FileBase { get; init; }
    public WedgeData? WedgeData { get; set; }
}
