# Architecture Assessment: Consolidate GiftPackageManufacture to a single sync endpoint

## Verdict

**Approved as designed.** `design-01.md` is accurate against the current codebase and does not violate any
documented invariant in `docs/architecture/development_guidelines.md` or `docs/architecture/filesystem.md`.
This is a pure deletion + rewiring — no new components, no new module boundaries, no new persistence, no new
DI registration pattern to get wrong. Proceed to implementation as specified, with two small course
corrections below (both already flagged as open questions in `plan-01.md`, now resolved here).

## Verification performed

I re-read the actual source referenced by `design-01.md`/`plan-01.md` rather than trusting the prior write-up,
since this step's job is to catch drift between the design and reality:

- `LogisticsController.cs:71-118` — confirmed both routes exist exactly as described (`manufacture` at
  line 71-79, `manufacture/enqueue` at line 97-105), and confirmed the XML summary on the enqueue action
  ("Queue gift package manufacturing process as background job") repeats the same false async claim as the
  response message — the design's deletion list (route + doc comment + `using`) needs to remove this too,
  which it already does ("including its XML doc comment").
- `EnqueueGiftPackageManufactureHandler.cs` — confirmed it calls the identical
  `CreateManufactureAsync(giftPackageCode, quantity, allowStockOverride, cancellationToken)` signature as
  `CreateGiftPackageManufactureHandler`, confirming zero behavioral divergence between the two paths.
- `GiftPackageManufactureService.cs:142-208` — confirmed `CreateManufactureAsync` is fully synchronous
  (log insert → per-ingredient `CreateOperationAsync` → output `CreateOperationAsync`, all awaited in
  sequence) and confirmed no `IPublisher`/domain-event dispatch exists in this method or the class.
- `GiftPackageManufactureModule.cs` — confirmed it registers only the repository and
  `IGiftPackageManufactureService`; no explicit MediatR handler registration exists for either
  `CreateGiftPackageManufactureHandler` or `EnqueueGiftPackageManufactureHandler`, confirming MediatR
  assembly-scan auto-discovery is genuinely in play and deleting the enqueue handler class requires no
  companion DI change.
- `useGiftPackageManufacturing.ts` — confirmed `useEnqueueGiftPackageManufacture` (lines 112-125) invalidates
  `[...QUERY_KEYS.giftPackages, "jobs"]`, and confirmed via repo grep that no query anywhere in the frontend
  reads a `"jobs"` key — the design's "dead invalidation" claim holds.
- `index.tsx` — confirmed `onManufacture`/`handleManufacture` (backed by `useCreateGiftPackageManufacture`) is
  wired into `<GiftPackageManufacturingDetail>` as a prop but, in `GiftPackageManufacturingDetail.tsx`, the
  `onManufacture` prop is destructured and typed (lines 21, 32) and **never called** anywhere else in the
  file — confirmed by grep, only those two lines mention `onManufacture`. The live button (line 404) calls
  `handleEnqueueManufacture` → `onEnqueueManufacture` only. The design's central premise (delete `/enqueue`,
  but repoint the UI onto the currently-dead sync path rather than naively deleting the endpoint the UI
  actually calls) is correct and necessary — the finding's literal "Option A" would have broken the feature.
- `StockUpGate.test.tsx` — confirmed it mocks both hooks (`useCreateGiftPackageManufacture` and
  `useEnqueueGiftPackageManufacture`, lines 12-15, 90-91, 116-126) and the generated-client factory mocks
  `EnqueueGiftPackageManufactureRequest` (line 62-64), but the three `it()` blocks only assert
  `useStockUpOperationsSummary` call arguments — confirming the design's claim that the enqueue mock is
  incidental scaffolding, safe to delete without touching what the suite actually verifies.
- `GiftPackageManufactureModule.cs` / `ModuleBoundariesTests.cs` — confirmed no architecture test enforces
  anything about the enqueue types specifically (the module-boundary allow-list only covers
  `IManufactureClient`/`ProductPart`/`LogisticsGiftPackageItem` edges), so deletion trips no reflection-based
  guard rail.
- Repo-wide grep for `EnqueueGiftPackageManufacture` / `onEnqueueManufacture` / `handleEnqueueManufacture`
  confirms the design's file list is exhaustive: 3 backend files (handler/request/response), plus
  `LogisticsController.cs`; 4 frontend files (`index.tsx`, `GiftPackageManufacturingDetail.tsx`,
  `StockUpGate.test.tsx`, `useGiftPackageManufacturing.ts`) plus the generated client (regenerated, not
  hand-edited). No E2E test, Swagger snapshot test, or other consumer references either path.
- `CatalogModule.cs` — confirmed `StockUpProcessingService` (the real async piece — the recurring job that
  pushes `Pending` `StockUpOperation` rows to the eshop) is registered there via `RegisterRefreshTask`,
  independent of which controller endpoint created the rows. Deleting the enqueue endpoint cannot regress
  this pipeline, since both old handlers fed it identically.

## Alignment with documented invariants

- **DTOs live in the feature's own folder, never API/Xcc** (`development_guidelines.md` §Contracts and DTOs)
  — respected; this design removes DTOs, doesn't add any elsewhere.
- **Controllers only orchestrate MediatR requests, no business logic** (`filesystem.md` Component Placement
  Rules) — respected; controller change is a pure action removal.
- **Vertical slice / UseCases folder-per-handler convention** (`filesystem.md` Complex Features) — respected;
  the surviving `CreateGiftPackageManufacture` UseCase folder is untouched and already follows the pattern.
- **No shared/global DTOs, no cross-module coupling introduced** — not applicable here; nothing new is added.
- **OpenAPI client is generated, never hand-edited** (`filesystem.md` OpenAPI Client Generation,
  `docs/development/api-client-generation.md`) — respected; design explicitly calls for regeneration, not
  manual edits to `api-client.ts`.
- **User identity resolution (ADR-005)** — not implicated; neither handler touches `ICurrentUserService` or
  identity concerns directly (it's inside `GiftPackageManufactureService`, unchanged).
- **Dark mode (ADR-006)** — not implicated; no new visual elements, button markup is reused verbatim per the
  design's wireframe.

No invariant is bent or worked around by this design. It is a subtractive change plus a one-line `onClick`
swap.

## Course corrections (small, worth doing during implementation)

1. **Controller XML doc comment must be deleted, not just the route.** Confirmed while re-reading
   `LogisticsController.cs:94-96`: the summary comment ("Queue gift package manufacturing process as
   background job") is itself part of the misleading contract (it's what NSwag turns into the Swagger
   operation description). `design-01.md` already lists this under "including its XML doc comment" — flagging
   here only so implementation doesn't drop it as an afterthought once the action body is deleted.

2. **Resolve plan-01's open question on user-facing feedback now, don't leave it open through
   implementation.** The plan asks whether the surviving response needs an inline message about async
   eshop stock-up. Recommendation: **no new UI copy.** The modal already has a dedicated
   "Zobrazit operace naskladnění" button (`GiftPackageManufacturingDetail.tsx:417-424`) linking to
   `/stock-up-operations` — that is the correct, existing surface for this information, and bolting a status
   string onto the manufacture response would duplicate it. Confirmed no other synchronous mutation success
   path in this file (`useDisassembleGiftPackage`) shows a comparable inline message either — `toast.success`
   is used there for the disassemble path but manufacture never had a toast on the sync path historically, so
   don't add one now; that would be scope creep beyond "remove the duplicate."

## Risks and mitigations

- **Risk: forgetting the frontend rewiring step and only deleting the backend endpoint** would 404 the only
  button end users actually click. Mitigation: this is already sequenced first in `plan-01.md`'s "Decision"
  and called out explicitly as the reason Option A must be adapted — implementation must not skip straight to
  backend deletion and regenerate the client before repointing `onClick`, or there will be a window (even if
  only within one commit) where the app is broken if built/deployed mid-sequence. Practically this is a
  non-issue since it's one PR, but worth sequencing FE prop rename before backend deletion in the diff so a
  partial revert never leaves the button dead.
- **Risk: OpenAPI regeneration touching unrelated parts of `api-client.ts`.** Low — regeneration is
  deterministic from the controller surface; only the enqueue members will disappear. Standard practice per
  `docs/development/api-client-generation.md` already covers reviewing the generated diff before commit.
- **Risk: e2e nightly suite has a hidden dependency on `/enqueue`.** Ruled out by grep across
  `frontend/test/e2e/` — no hits. No further mitigation needed.

## Prerequisites before implementation

None outstanding. No schema change, no migration, no feature-flag gating, no cross-module contract to
design. Implementation can proceed directly per `design-01.md`'s Component design and Rough plan sections.

## Scope check

The design does not introduce Option B (Hangfire-backed real async queuing) and correctly treats it as
out of scope / YAGNI, consistent with `plan-01.md`'s Decision and with `CLAUDE.md`'s "don't design for
hypothetical future requirements" guidance. If a genuine need for pollable async manufacture jobs surfaces
later, it should be filed as a new feature request, not folded into this cleanup.
