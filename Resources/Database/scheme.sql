-- Always enable FK enforcement in SQLite connections
PRAGMA foreign_keys = ON;

-- 1) Article master: maps (Firma, Artikel) -> xPartSpec
CREATE TABLE IF NOT EXISTS S_Artikel (
    Firma       INTEGER     NOT NULL,
    Artikel     TEXT        NOT NULL,   -- part number
    xPartSpec   TEXT        NOT NULL,   -- PartSpec ID
    PRIMARY KEY (Firma, Artikel)
);

-- 2) Localized article descriptions (one per language per article)
CREATE TABLE IF NOT EXISTS S_ArtikelSpr (
    Firma       INTEGER     NOT NULL,
    Sprache     TEXT        NOT NULL,   -- e.g., 'E' for English, 'D' for German
    Artikel     TEXT        NOT NULL,
    Bezeichnung TEXT        NOT NULL,
    PRIMARY KEY (Firma, Sprache, Artikel),
    FOREIGN KEY (Firma, Artikel) REFERENCES S_Artikel(Firma, Artikel) ON DELETE CASCADE
);

-- 3) PartSpec header (not strictly required by your code, but good FK target)
CREATE TABLE IF NOT EXISTS XS_PartSpecHeader (
    Firma       INTEGER     NOT NULL,
    PartSpec    TEXT        NOT NULL,
    PRIMARY KEY (Firma, PartSpec)
);

-- 4) PartSpec spec rows (this is what your loader consumes)
-- Column_ID is a semicolon payload: "value;ltol;utol;?;?;unit"
CREATE TABLE IF NOT EXISTS XS_PartSpecSpecs (
    Firma       INTEGER     NOT NULL,
    PartSpec    TEXT        NOT NULL,
    Template    TEXT        NOT NULL,   -- 'Wed-Spec2' (dimensions), 'Wed-Spec1' (common), etc.
    xRow        TEXT        NOT NULL,   -- e.g., 'Wed-FL', 'Wed-GA', 'Wed-Title', ...
    Column_ID   TEXT        NOT NULL,   -- payload string
    PRIMARY KEY (Firma, PartSpec, Template, xRow),
    FOREIGN KEY (Firma, PartSpec) REFERENCES XS_PartSpecHeader(Firma, PartSpec) ON DELETE CASCADE
);

-- Helpful indexes for typical lookups
CREATE INDEX IF NOT EXISTS IX_S_Artikel_xPartSpec ON S_Artikel (Firma, xPartSpec);
CREATE INDEX IF NOT EXISTS IX_S_ArtikelSpr_Artikel ON S_ArtikelSpr (Firma, Artikel, Sprache);
CREATE INDEX IF NOT EXISTS IX_XS_Specs_Lookup ON XS_PartSpecSpecs (Firma, PartSpec, Template);
