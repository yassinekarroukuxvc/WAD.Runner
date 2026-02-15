public sealed record WedSpec1Dto(
    string ArticleNumber,
    string? WedPolish,
    string? WedPS,
    string? WedNotes,
    string? WedOverlay,
    string? WedEngrave,
    string? WedCoining,

    // NEW (COB/UT-US)
    string? WedType,
    string? WedFootOption,
    string? WedWireExit,
    string? WedFeedHSlot,

    string? DwgText1,
    string? DwgText2,
    string? DwgText3,
    string? DwgText4,
    string? DwgText5,
    string? DwgText6,
    string? DwgText7
);
