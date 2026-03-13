using WAD.Runner.DataManagement.Infrastructure.Transport.Dtos;

namespace WAD.Runner.Application.Ports;

/// <summary>
/// Low-level transport port for the Java API. Shapes are transport DTOs
/// (anti-corruption layer lives above this). No domain models here.
/// </summary>
public interface IJavaWedgeTransport
{
    // ---- FG (Wed-*) ----
    Task<WedSpec1Dto> GetWedSpec1Async(string article, CancellationToken ct);
    Task<IReadOnlyList<WedSpec2RowDto>> GetWedSpec2Async(string article, CancellationToken ct);
    Task<WedKValueDto?> GetWedKValueAsync(string article, CancellationToken ct);
    Task<IReadOnlyList<WedMarkingRowDto>> GetWedMarkingAsync(string article, CancellationToken ct);

    // ---- PGB (PGB-*) ----
    Task<PgbSpec1Dto> GetPgbSpec1Async(string article, CancellationToken ct);
    Task<IReadOnlyList<PgbSpec2RowDto>> GetPgbSpec2Async(string article, CancellationToken ct);

    Task<string?> GetArticleDescriptionAsync(string article, CancellationToken ct);
}
