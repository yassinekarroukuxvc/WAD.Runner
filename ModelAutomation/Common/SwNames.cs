namespace WAD.Runner.ModelAutomation.Common;

public static class SwNames
{
    // -------------------------------------------------
    // Common sketches / features
    // -------------------------------------------------
    public const string Engraving = "Engraving";
    public const string SketchCrmet = "Drawing_CRMET";

    // -------------------------------------------------
    // CKVD sketches
    // -------------------------------------------------
    public const string SketchFgWedW = "FG_Wed_W";
    public const string SketchFgWedVW = "FG_Wed_VW";

    // -------------------------------------------------
    // CKVD dimensions (full parameter paths)
    // -------------------------------------------------
    public const string DimVrMin = "VR_MIN@FG_Wed_VW";
    public const string DimVrMax = "VR_MAX@FG_Wed_VW";
    public const string DimVwLTol = "VW_LTOL@FG_Wed_VW";
    public const string DimVwUTol = "VW_UTOL@FG_Wed_VW";

    // -------------------------------------------------
    // OSG7 features (feature tree names)
    // -------------------------------------------------
    public const string Osg7Tl = "TL_feature";
    public const string Osg7Tdf = "TDF_feature";
    public const string Osg7Isa = "ISA_feature";
    public const string Osg7StdShank = "STD_shank_feature";
    public const string Osg7GGroove = "G_groove_feature";

    public const string Osg7Vr = "VR_feature";

    public const string Osg7Vfl = "VFL_feature";
    public const string Osg7Vlf = "VLF_feature"; // alias

    public const string Osg7FrStd = "FR_STD_feature";
    public const string Osg7BrStd = "BR_STD_feature";
    public const string Osg7FrStdVfl = "FR_STD_VFL_feature";
    public const string Osg7BrStdVfl = "BR_STD_VFL_feature";
}
