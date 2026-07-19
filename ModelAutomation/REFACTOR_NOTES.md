# ModelAutomation refactor notes

## Goal

Keep the automation easy to understand while making future wedge types safer to add. The refactor avoids adding a dependency-injection framework, reflection-based registration, or a large hierarchy of abstractions.

## Main structural changes

- `WedgeFacts` is now the single unit-aware reader for dimensions, tolerances, bounds, and loose database properties.
- `CobLikeFacts` adds only COB-family interpretations: shank type, foot option, overlay VR case, and shared accessors.
- Removed duplicate implementations:
  - `CobLikeRuleFacts.cs`
  - `CobLikeGeometryCalculator.cs`
  - `WedgeDimensionReader.cs`
- Removed the stale nested `Rules.zip`, which contained older copies of the source files.
- `FeaturePlanBuilder` now supports mutually-exclusive groups through `ActivateOnly(...)` and no longer mutates itself when `Build()` is called.
- `WedgeAutomationProfileRegistry` remains the one explicit place to register a new wedge type and now rejects duplicate registrations during startup.

## Correctness fixes

- Foot overlay selection now chooses the smallest **positive** value among `W`, `VW`, and `W2`. Previously, `W = 0` could incorrectly win.
- Corrected the FP target typo from `GO_MAX` to `GR_MAX`.
- COB-family tolerance values now consistently use absolute lower/upper magnitudes.
- COB-family min/max values now consistently use `nominal - |lower|` and `nominal + |upper|`.
- Left-overlay tolerance selection now follows `VW` presence, matching the feature-selection logic.
- The combined `RA2H + VBL` front overlay sketch now receives its own tolerance updates.
- FG foot-overlay tolerances are no longer planned for PGB drawings.
- SolidWorks configuration activation, equation import, and save results are checked instead of being logged as successful unconditionally.
- Direct model tolerances now use signed SolidWorks values derived from absolute database magnitudes.
- Critical ERW suppression applies child sketches before parent features.
- Feature indexing now walks nested subfeatures recursively.

## File safety improvements

- Article numbers and custom file bases are sanitized before being used as path segments.
- Template and equation files are replaced through temporary files, reducing the chance of leaving partially written output.
- Copying a template onto itself is handled safely.
- Cancellation is checked between the main automation stages.

## Adding a new wedge type

For a completely different model family:

1. Add its configuration, feature, equation, and tolerance rule classes.
2. Add one profile entry in `WedgeAutomationProfileRegistry.BuildProfiles()`.

For another COB-like family:

1. Inherit from `CobLikeConfigurationRulesBase`.
2. Inherit from `CobLikeFeatureRulesBase` and override only variant-specific adjustments.
3. Inherit from `CobLikeToleranceRulesBase` and add only variant-specific targets.
4. Register it with `CreateCobLikeProfile(...)` in the registry.

## Required validation in the full project

This folder was syntax-parsed successfully, but this environment does not contain the .NET SDK, the project domain assemblies, or SolidWorks interop/runtime. Before deployment, run:

1. A normal solution build with warnings enabled.
2. Existing regression articles for CKVD, COB, FP, UTUS, and OSG7.
3. COB-family overlay cases covering:
   - STD and 180_DEG_REV
   - PGB and FG
   - no VW, VW = W, and VW != W
   - raw `VR + VRR` below and above 0.5 mm
   - RA2H only, VBL only, and RA2H + VBL
   - C, C with CBR, CC, G, and VG foot options
4. A forced missing-configuration and failed-equation-import test to confirm the job stops without saving an incorrect part.
