namespace WAD.Runner.DataManagement.Infrastructure.Transport.Dtos;

/// <summary>
/// One PGB-Spec2 row: Key like "PGB-TL" and the payload string
/// formatted as "nom;Ltol;Utol;Comment;;;;".
/// </summary>
public sealed record PgbSpec2RowDto(
    string Key,
    string Payload
);
