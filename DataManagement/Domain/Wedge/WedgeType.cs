namespace WAD.Runner.DataManagement.Domain.Wedge;

/// <summary>
/// Enumerates all supported wedge families.
/// Each wedge type may have its own dimensional keys,
/// SolidWorks templates, and drawing rules.
/// </summary>
public enum WedgeType
{
    /// <summary>Default or unknown type (fallback).</summary>
    Unknown = 0,

    /// <summary>CKVD — Capillary wedge design, most standard type.</summary>
    CKVD = 1,

    /// <summary>COB — Coining wedge (can have variants like COB-UT-US).</summary>
    COB = 2,

    /// <summary>FG — Standard fine-grinding wedge (legacy FG subclass).</summary>
    FG = 3,

    /// <summary>PGB — Polished grinding block type.</summary>
    PGB = 4,

    /// <summary>OSG7 — Overlay wedge type for specific geometries.</summary>
    OSG7 = 5,

    UTUS = 6,

    /// <summary>Other special or test wedge types.</summary>
    Other = 99
}
