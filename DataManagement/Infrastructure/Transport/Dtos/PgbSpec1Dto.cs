namespace WAD.Runner.DataManagement.Infrastructure.Transport.Dtos;

/// <summary>
/// PGB-Spec1: part-level properties for PGB subclass.
/// </summary>
public sealed record PgbSpec1Dto(
    string ArticleNumber,
    string? Polish,
    string? PS,
    string? Remarks
);
