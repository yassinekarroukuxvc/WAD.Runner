# Annotation Cleanup

This folder contains the shared, data-driven annotation cleanup engine.
Wedge-specific annotation interpretation belongs under each wedge module's
`Annotations` folder.

## Runtime flow

1. `DrawingAnnotationCleanupStep` obtains the runner for the current wedge type.
2. `AnnotationCleanupContextFactory` gets the active `IDrawingWedgeModule`.
3. The module's `IAnnotationWedgeContextResolver` converts wedge properties into normalized annotation traits and sketch names.
4. `DimensionFactResolver` and `ViewNameResolver` add the shared dimension and view facts.
5. `AnnotationCleanupPlanner` evaluates the declarative keep rules from `AnnotationRuleCatalogRegistry`.
6. `DrawingAnnotationStateReader` reads the display dimensions currently present in the SolidWorks views.
7. `AnnotationDiffService` calculates `existing - keep` using exact annotation identities and safe aliases.
8. `ExactAnnotationDeletionService` deletes the planned annotations.
9. `AnnotationCleanupExecutor` verifies that protected annotations remain.

## Extensible wedge traits

The shared cleanup engine no longer contains central foot-option or shank-type enums.
It uses `AnnotationTraitSet` with normalized string traits instead:

- `AnnotationTraitNames.FootOption`
- `AnnotationTraitNames.ShankType`
- `AnnotationTraitNames.WedType`
- `AnnotationTraitNames.FeedHoleType`

A wedge may add any additional trait name without changing the shared engine.
Conditions may use `TraitIs(...)`, while common wrappers remain available:

- `FootIs(...)`
- `FootIn(...)`
- `ShankIs(...)`
- `WedTypeIs(...)`
- `FeedHoleIs(...)`

## Adding a new wedge type

Create the following under `Wedges/<Wedge>/Annotations`:

1. A token/constants file owned by the wedge.
2. An `IAnnotationWedgeContextResolver` that maps database properties to normalized traits.
3. One or more annotation rule catalogs.

Then expose the resolver and catalogs from the wedge's `IDrawingWedgeModule` and register the module once in `DrawingWedgeModuleRegistry`.

The shared `AnnotationCleanupContextFactory` must not contain wedge-name checks such as `IsCkvdProfile`.
Wedge-specific validation belongs in the wedge-specific resolver.

## Current ownership

- COB, FP, and UTUS reuse `Wedges/CobLike/Annotations`.
- CKVD owns style validation and annotation traits under `Wedges/Ckvd/Annotations`.
- OSG7 currently needs no special annotation traits and uses `EmptyAnnotationWedgeContextResolver`.
- 4516 owns its foot, shank, and feed-hole normalization under `Wedges/4516/Annotations`.

## Empty catalogs

A catalog with zero configured keep rules is treated as disabled. Cleanup is skipped
for that profile to prevent an empty catalog from deleting every annotation. This is
currently used by the 4516 overlay profiles until their overlay rules are added.

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

## Preserved compatibility rules

- Cleanup remains keep-list based.
- A dimension is positive when its nominal value is greater than `1e-12`.
- `SLB` falls back to `VBL` when no real `SLB` dimension exists.
- COB-like `FL_*` annotations remain controlled by the existing rules.
- `FR` and `BR` remain independent.
- The legacy 180-degree annotation sketch spelling is preserved where required.
- CKVD style selection still requires `LW_STYLE_A_CKVD` or `LW_STYLE_B_CKVD`, but validation now lives in `CkvdAnnotationContextResolver`.
