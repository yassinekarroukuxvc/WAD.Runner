# Annotation Cleanup

This folder contains the data-driven annotation cleanup subsystem.

## Runtime flow

1. `DrawingAnnotationCleanupStep` obtains the runner for the current wedge type.
2. `AnnotationCleanupContextFactory` resolves the drawing profile, shank, foot option, dimensions, view names, and sketch names.
3. `AnnotationCleanupPlanner` evaluates the declarative keep rules from `AnnotationRuleCatalogRegistry`.
4. `DrawingAnnotationStateReader` reads the display dimensions currently present in the SolidWorks views.
5. `AnnotationDiffService` calculates `existing - keep` using exact annotation identities and safe aliases.
6. `ExactAnnotationDeletionService` resolves exact `DisplayDimension` objects, selects the complete cleanup batch, and performs one deletion call.
7. `AnnotationCleanupExecutor` performs a final safety audit to confirm that annotations protected by active keep rules still exist.

## Safety rules

- Annotation matching never uses dimension-name prefixes or substrings.
- The referenced document suffix in `Dimension@Owner@Document` is ignored, but `Dimension@Owner` is compared exactly.
- A unique dimension-key fallback is allowed only when that key occurs once in the view.
- A deletion target must resolve to exactly one current `DisplayDimension`; ambiguous targets are skipped.
- Deletion count cannot exceed the planned count.
- If a view has keep rules but none match its template, cleanup for that view fails closed and deletes nothing.
- If an active keep-rule annotation disappears, cleanup raises a safety-audit error.
- Rule IDs are validated for uniqueness when the catalog registry is created.

## Aliases

`AnnotationKeepRule.Aliases` represents additional accepted SolidWorks names for the same logical annotation. Use `KeepWithAliases(...)` when templates expose a dimension under multiple names.

Example:

```csharp
KeepWithAliases(
    "OSG7-FG-CUST-DETAIL-GD",
    Detail,
    "GD@ANNOT_RIGH_PLAN",
    new[] { "GD" },
    Always(),
    "Keep GD under linked and drawing-only names.")
```

Aliases are treated as protected alternatives. This is intentionally conservative: when more than one accepted representation exists in a view, cleanup preserves them rather than risking deletion of the correct annotation.

## Wedge-module behavior

Annotation catalogs are supplied by the active `IDrawingWedgeModule`:

- CKVD owns its CKVD catalogs under `Wedges/Ckvd/Annotations`.
- OSG7 owns its OSG7 catalogs under `Wedges/Osg7/Annotations`.
- COB, FP, and UTUS explicitly reuse the shared catalogs under `Wedges/CobLike/Annotations`.

Register a future wedge once in `DrawingWedgeModuleRegistry`. Its module must expose every annotation catalog required by its drawing profiles; registry validation fails immediately when a profile resolves to a missing catalog.

## Preserved compatibility rules

- Cleanup remains keep-list based.
- A dimension is positive when its nominal value is greater than `1e-12`.
- `SLB` falls back to `VBL` when no real `SLB` dimension exists.
- `FL_*` annotations are controlled by `F`, not `FL`.
- `FR` and `BR` remain independent.
- `VRA` follows the VW/VR condition.
- The legacy 180-degree annotation sketch spelling is preserved where required by existing templates.

## CKVD Production/Customer annotation mapping

The CKVD catalogs use the corrected logical view mapping supplied by the model designer:

- The screenshot named **Right view** maps to the logical `Front` drawing view.
- The screenshot named **Front view** maps to the logical `Side` drawing view.
- Annotation cleanup does not position views.

CKVD style selection is read from `Wed-Type`:

- `LW_STYLE_A_CKVD`
- `LW_STYLE_B_CKVD`

CKVD rules distinguish between two optional-data checks:

- `DimPositive("VR")`, `DimPositive("VW")`, and `DimPositive("VRA")` keep optional dimensions only when their nominal value is positive.
- `DimPresent("X")`, `DimPresent("FX")`, `DimPresent("BRX")`, and `DimPresent("FRX")` keep dimensions only when the key was supplied in the source wedge data. This prevents a planner-calculated fallback value from being treated as a database-provided drawing annotation.

The overlay annotation catalogs were intentionally left on their previous rules because this update covers Production and Customer drawings only.
