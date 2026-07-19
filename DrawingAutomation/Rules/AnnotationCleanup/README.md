# Annotation Cleanup

This folder contains the data-driven annotation cleanup subsystem.

## Runtime flow

1. `DrawingAnnotationCleanupStep` obtains the runner for the current wedge type.
2. `AnnotationCleanupContextFactory` resolves the drawing profile, shank, foot option, dimensions, view names, and sketch names.
3. `AnnotationCleanupPlanner` evaluates the declarative keep rules from `AnnotationRuleCatalogRegistry`.
4. `DrawingAnnotationStateReader` reads the display dimensions currently present in the SolidWorks views.
5. `AnnotationDiffService` calculates `existing - keep` using exact annotation identities and safe aliases.
6. `ExactAnnotationDeletionService` selects and deletes one exact `DisplayDimension` object at a time.
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

## Wedge-family behavior

The cleanup profile resolver uses `Core/DrawingWedgeBehavior.cs`:

- CKVD uses the CKVD annotation catalogs.
- OSG7 uses the OSG7 annotation catalogs.
- COB, FP, and UTUS use the shared COB-like catalogs.

Registering a future wedge type in `DrawingWedgeBehaviorCatalog` automatically makes a cleanup runner available. Add a new annotation catalog only when the new wedge cannot reuse an existing family.

## Preserved compatibility rules

- Cleanup remains keep-list based.
- A dimension is positive when its nominal value is greater than `1e-12`.
- `SLB` falls back to `VBL` when no real `SLB` dimension exists.
- `FL_*` annotations are controlled by `F`, not `FL`.
- `FR` and `BR` remain independent.
- `VRA` follows the VW/VR condition.
- The legacy 180-degree annotation sketch spelling is preserved where required by existing templates.
