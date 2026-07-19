# DrawingAutomation Refactor Notes

## Goal

Improve reliability and make future wedge types easier to add without introducing a large framework or changing the public drawing entry points.

## Main structural changes

### One wedge-behavior catalog

`Core/DrawingWedgeBehavior.cs` is now the central place for drawing behavior shared by wedge families. It defines:

- CKVD, OSG7, or COB-like family
- overlay magnification source (`FL` or `T`)
- default overlay reference-point sketch
- whether Front/Side overlay views are repositioned
- whether the Front overlay view is deleted when `VR = 0`

This replaces repeated `CKVD/OSG7/COB/FP/UTUS` checks in overlay scaling, configuration binding, metadata, and annotation cleanup.

### Shared wedge facts

`Core/DrawingWedgeFacts.cs` provides consistent:

- millimeter dimension reading
- positive-dimension checks
- case-insensitive property lookup
- standard versus 180-degree reverse shank detection

### Smaller profile definitions

`Profiles/ProfilePresets.cs` now uses small profile factories. COB, FP, and UTUS reuse the same view layouts while keeping their existing public preset methods and profile names.

The production sheet selector accepts both `PRODUCTION` and the legacy misspelling `PRODCUTION`.

### Compact table filters

`Tables/DimensionTableKeyFilter.cs` stores rules by one simple composite key. COB, FP, and UTUS share one COB-like registration method instead of three duplicated rule trees.

## Reliability improvements

- Input paths are validated before drawing automation starts.
- The generated part must exist after the model phase before drawing relinking begins.
- A drawing template cannot be copied over itself.
- Drawing templates are copied through a temporary file before replacement.
- Open drawings are closed when a later pipeline step throws.
- `DrawingService` now implements `IDisposable` and closes using the actual SolidWorks document title when possible.
- Pipeline routing detects ambiguous pipeline registrations instead of silently using the first match.
- Profile registration checks duplicate keys, profile-key mismatches, and invalid scale policies.
- PDF/TIFF output paths are normalized safely.
- SolidWorks save results are logged with success, error, warning, and path information.

## Correctness fixes

- The calculated COB-like Detail-view Y position is now passed to the overlay macro. Previously it was calculated but the hardcoded `2.4` value was still used.
- Overlay magnification is calculated in one service and reused by scaling and metadata.
- Overlay dimension reads consistently use millimeters.
- Overlay max-value calculations use the absolute upper tolerance in millimeters.
- Profile breakline policies and profile view order are now actually used by the production pipeline.

## Removed files

The two embedded archives were removed because they contained older or duplicate rule implementations:

- `Rules.zip`
- `Rules_Refactored_AnnotationCleanup.zip`

`Profiles/ProfileHelpers.cs` was removed because its view-name mapping duplicated `Core/DrawingViewNameMap.cs`.

## Adding a future wedge type

1. Add the new value to the project `WedgeType` enum.
2. Register its family and overlay behavior in `Core/DrawingWedgeBehavior.cs`.
3. Add a small `IDrawingProfileModule` under `Profiles/Modules` and register it in `DrawingProfileCatalog`.
4. Reuse an existing profile factory or add only the new template view names and sheet names.
5. Add dimension-table filters only when the new wedge requires filtering.
6. Add annotation catalogs only when the wedge cannot reuse CKVD, OSG7, or COB-like cleanup behavior.

## Recommended regression tests

Test Production, Customer, and Overlay drawings for every supported subclass and wedge type, with particular attention to:

- COB/FP/UTUS standard and 180-degree reverse overlays
- VW + VR non-standard cut overlays
- CKVD PGB overlay configuration binding
- CKVD `VR = 0` Front-view deletion
- templates using `PRODUCTION` and templates using `PRODCUTION`
- drawing relinking, PDF export, TIFF export, tables, annotation cleanup, and metadata

## Validation performed here

- All C# files passed lexical and bracket-balance checks.
- Profile preset references were checked against the available public preset methods.
- Duplicate full type names were checked across the folder.
- Removed helper and changed overlay method references were checked for stale call sites.

A complete .NET build and SolidWorks runtime test could not be performed in this environment because it does not contain the .NET SDK, the full project references, or SolidWorks interop/runtime.
