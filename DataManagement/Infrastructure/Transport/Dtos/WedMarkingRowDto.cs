namespace WAD.Runner.DataManagement.Infrastructure.Transport.Dtos;

/// <summary>
/// Wed-Marking row: XRow is one of
/// "Marking-Overlay", "Marking-TB-1" .. "Marking-TB-7", "Marking-Text".
/// Text is optional (may be null/empty).
/// </summary>
public sealed record WedMarkingRowDto(
    string XRow,
    string? Text
);
