# DrawingAutomation

This folder owns the SolidWorks drawing-generation phase of WedgeAutoDraw.
The refactor organizes the code by responsibility while keeping the runtime
flow easy to follow.

## Runtime flow

1. `Execution/DrawingAutomationExecutor` creates the automation context.
2. `Execution/DrawingPipelineRouter` selects either the production/customer
   pipeline or the overlay pipeline.
3. The selected pipeline orchestrates shared services; it does not contain
   wedge-specific decision trees.
4. `Wedges/DrawingWedgeModuleRegistry` resolves the module for the active wedge.
5. The module supplies the drawing profiles, annotation catalogs, table-key
   policy, referenced-configuration policy, overlay positioning rule, and
   small behavior flags for that wedge.

## Folder responsibilities

- `Execution` — top-level workflows and phase boundaries.
- `Wedges` — one module per wedge type; COB, FP, and UTUS share an explicit
  COB-like base because their drawing behavior is currently identical.
- `Profiles` — immutable view maps, sheet selection, scale policy, and
  breakline policy.
- `Views` — SolidWorks view positioning, scaling, geometry, annotation
  positioning, and breakline application.
- `Overlay` — overlay payload, configuration binding, magnification,
  positioning, scaling, annotations, and export.
- `Rules` — annotation cleanup rules and execution engine.
- `Tables` — drawing-table filtering and mutation.
- `SolidWorks` / `Interop` — focused adapters around SolidWorks COM APIs.
- `Planning` — converts drawing data into annotation placement plans.
- `Metadata` — drawing metadata application.
- `Common` — small operations genuinely shared by both pipelines.

## Adding a wedge type

Create one implementation of `IDrawingWedgeModule` under `Wedges/<Type>` and
register it in `DrawingWedgeModuleRegistry`. The module is the single place to
connect that wedge's:

- production, customer, and overlay profiles;
- annotation cleanup catalogs;
- referenced model-configuration selection;
- dimension-table key policy;
- overlay view-positioning rule;
- behavior flags and optional breakline overrides.

Shared behavior should be extracted only when two or more wedge modules truly
use the same rules. Avoid adding a central switch for a wedge-specific choice.

## View layout ownership

`DrawingViewLayoutCoordinator` is the only owner of layout sequencing:

1. configure Detail and Section scale;
2. auto-scale Front, Side, and Top;
3. calculate and apply enabled breaklines using the final scale;
4. perform the final positioning pass;
5. rebuild at controlled boundaries.

`BreaklineLayoutCalculator` contains the calculation policy and
`BreaklineService` contains the SolidWorks mutation. Overlay macro placement is
isolated under `Overlay/Positioning` and does not compete with normal drawing
view positioning.

## Design constraints

- Pipelines orchestrate; services execute; wedge modules decide.
- A class should have one reason to change, but cohesive SolidWorks operations
  remain together to avoid excessive file fragmentation.
- No fallback wedge behavior is silently selected. Unsupported wedge types fail
  with a clear registration error.
- Annotation deletion remains identity-based and batched to minimize COM calls.
