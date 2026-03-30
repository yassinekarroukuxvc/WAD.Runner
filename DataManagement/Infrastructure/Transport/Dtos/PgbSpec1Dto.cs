namespace WAD.Runner.DataManagement.Infrastructure.Transport.Dtos;

/// <summary>
/// PGB-Spec1: part-level properties for PGB subclass.
/// </summary>
public sealed record PgbSpec1Dto(
    string ArticleNumber,
    string? Polish,
    string? PS,
    string? Remarks,
    string? Engrave,     // maps Wed-Engrave
    string? FLBlank,    // maps Wed-FL-Blank (Coining)
    string? DwgText1,
    string? DwgText2,
    string? DwgText3,
    string? DwgText4,
    string? DwgText5,
    string? DwgText6,
    string? DwgText7,
    string? WedType,
    string? WedFootOption,
    string? WedWireExit,
    string? WedFeedHSlot,
    string? PgbFgStyle //maps the wedge type : CKVD,COB...
);
