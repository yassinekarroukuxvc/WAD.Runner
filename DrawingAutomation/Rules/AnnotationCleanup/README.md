# Annotation Cleanup Refactor

This folder contains the data-driven annotation cleanup subsystem.

Main flow:

1. `BaseAnnotationCleanupRunner` resolves runtime inputs from `DrawingRun`, `DrawingData`, and `WedgeData`.
2. `SharedAnnotationDeletionRules` preserves the old public API but now delegates COB-like and PGB keep-set generation to the new catalog-driven engine.
3. `AnnotationCleanupPlanner` evaluates declarative `AnnotationKeepRule` entries.
4. `AnnotationDeletionCore` reads existing SolidWorks display dimensions and computes `existing - keep`.
5. `AnnotationCleanupService.RemoveDimensionsByFullNamesInView` deletes the planned annotations.

Important behavior preserved:

- Cleanup is keep-list based: existing display dimensions that are not explicitly kept are deleted.
- Dimension positivity means the nominal value exists and is greater than `1e-12`.
- `FL_*` annotations are controlled by `F`, not `FL`.
- `FR` and `BR` are independent.
- `VRA` follows the VW/VR rule, not its own positivity.
- The legacy 180-degree CG/CC typo sketch `ANNOT_180_DEG_REV_FRONT_FRONT_sketch` is preserved.
- `CR@ANNOT_FOOT_OPTIONS_LEFT_sketch` remains controlled by the known-superset compatibility list but has no keep rule, so it is deleted when present.

Profiles:

- `CobLikeProduction`
- `CobLikeCustomer`
- `CobLikeOverlay`
- `PgbProduction`
- `PgbOverlay`

Overlay catalogs currently clone the corresponding production profile because the legacy code treated overlay as production. They are isolated so overlay-specific rules can be added later by editing only the overlay catalog.
