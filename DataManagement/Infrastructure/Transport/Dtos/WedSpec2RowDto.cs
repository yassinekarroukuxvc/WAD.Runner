namespace WAD.Runner.DataManagement.Infrastructure.Transport.Dtos;

/// <summary>
/// One Wed-Spec2 row: Key like "Wed-TL" and the payload string
/// formatted as "nom;Ltol;Utol;Comment;;;;".
/// </summary>
public sealed record WedSpec2RowDto(
    string Key,
    string Payload
);
