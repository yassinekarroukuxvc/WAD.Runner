namespace WAD.Runner.DataManagement.Infrastructure.Transport.Dtos;

/// <summary>
/// Wed_K-Value row: payload formatted as "k_mm;comment;;;;;;".
/// </summary>
public sealed record WedKValueDto(
    string Payload
);
