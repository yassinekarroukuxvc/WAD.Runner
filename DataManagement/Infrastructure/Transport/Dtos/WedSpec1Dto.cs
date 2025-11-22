namespace WAD.Runner.DataManagement.Infrastructure.Transport.Dtos;

/// <summary>
/// Wed-Spec1: part-level properties coming from the Java API.
/// </summary>
public sealed record WedSpec1Dto(
    string ArticleNumber,
    string? WedPolish,
    string? WedPS,
    string? WedNotes,
    string? WedOverlay
);
