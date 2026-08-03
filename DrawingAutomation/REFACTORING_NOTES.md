# Refactoring notes

## Main structural changes

- Added a single `IDrawingWedgeModule` boundary and dedicated CKVD, OSG7, COB,
  FP, and UTUS modules.
- Moved wedge-specific annotation catalogs beside their wedge modules.
- Replaced the separate behavior, profile, positioning, configuration, and
  table-filter registries with one validated wedge-module registry.
- Replaced the broad profile preset/registration layer with immutable profile
  keys, a profile factory, and module-owned profile declarations.
- Consolidated overlay code previously split between `Common/Overlay` and
  `Overlay` into one feature area.
- Split overlay positioning into a context, rule contract, shared base,
  per-wedge rules, macro runner, and focused macro positioner.
- Made `DrawingViewLayoutCoordinator` the sole owner of normal view-layout
  sequencing.
- Split breakline calculation from SolidWorks mutation.
- Replaced the production pipeline's unused compatibility state with
  `PreparedProductionDrawing`, which contains only the resources actually used.

## Removed dead or redundant code

- Embedded historical zip archives inside the source folder.
- Legacy profile catalog, registration, preset, and module abstractions.
- Legacy wedge behavior/family catalog and overlay positioning catalog.
- Unused `ViewPlacementService`, `ViewAutoScaleService`, and the mixed-purpose
  `SecondaryViewPlacementService`.
- Test-only breakline handler code and duplicated breakline calculations.
- Standalone dimension table-key filter registry.
- Unused reflection, drawing-property, annotation insertion, annotation dump,
  exact-match deletion, and no-op drawing service helpers.
- Unused annotation cleanup snapshots, test factories, result helpers, and
  compatibility methods.

## Behavior intentionally retained

- Existing sheet names, view names, scale ranges, annotation rule catalogs,
  overlay reference points, and table-key sets.
- CKVD Front/Side breakline suppression while retaining Detail/Section
  breaklines for production/customer drawings.
- OSG7 TL breakline override.
- Front/Side/Top auto-scale and Detail/Section configured-scale policy.
- Overlay primary views using production configurations while Detail/Section
  use overlay configuration rules.
- Batched exact annotation deletion with a post-delete safety audit.

## Validation performed on this folder

- C# delimiter and preprocessor-balance scan.
- Duplicate full type-name scan.
- Internal namespace/import consistency scan.
- Legacy reference scan for every removed abstraction.
- Single-occurrence method/type/private-member scan to identify remaining dead
  code candidates.
- Archive integrity test after packaging.

A complete solution build and SolidWorks integration run still need to be
performed in the host WAD.Runner solution because the supplied archive contains
only this folder and does not include the project file, external domain
assemblies, SolidWorks interop assemblies, templates, or a SolidWorks runtime.
