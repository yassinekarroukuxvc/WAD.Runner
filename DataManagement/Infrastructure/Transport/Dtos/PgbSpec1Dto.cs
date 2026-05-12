namespace WAD.Runner.DataManagement.Infrastructure.Transport.Dtos;

public sealed record PgbSpec1Dto(
    string ArticleNumber,
    string? Polish,
    string? PS,
    string? Remarks,
    string? Engrave,
    string? FLBlank,
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
    string? PgbFgStyle
);
