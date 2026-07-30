# Plan: Remove duplicate `EnqueueGiftPackageManufacture` endpoint / fix misleading async contract

## Summary

`POST /api/logistics/gift-packages/manufacture` and `POST /api/logistics/gift-packages/manufacture/enqueue`
both call `GiftPackageManufactureService.CreateManufactureAsync` synchronously and return only after the
manufacture log and stock operation rows are created. The `/enqueue` endpoint's response claims the work
"will be processed asynchronously" and returns a `JobId`, implying a poll-able background job that does not
exist for this operation. This is a real behavioral duplication with a misleading contract on one of the two
paths, and it must be fixed without breaking the actual production UI flow, which currently depends on the
`/enqueue` endpoint.

## Context (investigation findings, not present in the original finding)

Read before implementing — these change which "suggested fix" option applies:

1. **The frontend's live button uses the `/enqueue` path, not the sync one.**
   `GiftPackageManufacturingDetail.tsx` renders exactly one manufacturing button ("Zadat k výrobě"), wired to
   `onEnqueueManufacture` (`GiftPackageManufacturingDetail.tsx:404`). The `onManufacture` prop (backed by
   `useCreateGiftPackageManufacture` / `handleManufacture` in `index.tsx:80-96`) is threaded through as a prop
   but **never invoked anywhere in the component** — it is dead code today. Naively deleting the `/enqueue`
   endpoint (finding's "Option A" as literally stated) would remove the only endpoint the UI actually calls.

2. **The misleading part is narrower than "stock operations are created inline, not queued."**
   `CreateManufactureAsync` (`GiftPackageManufactureService.cs:143-208`) does two things synchronously:
   creates the `GiftPackageManufactureLog`, and calls `ILogisticsStockOperationService.CreateOperationAsync`
   per ingredient, which inserts a `StockUpOperation` row in `Pending` state
   (`StockUpProcessingService.cs:22-42`). Actually pushing those pending operations to the eshop
   (`IEshopStockDomainService.StockUpAsync`) **is** deferred to a recurring background task
   (`StockUpProcessingService.ProcessPendingOperationsAsync`, registered via `RegisterRefreshTask` in
   `CatalogModule.cs:276-279`) — so "stock operations will be processed asynchronously" is not entirely
   fabricated, it describes the real eshop stock-up pipeline (trackable via the separate
   `/stock-up-operations` page the UI already links to).
   What *is* misleading: the endpoint name (`Enqueue...`), the `JobId` field, and the message together imply
   the **manufacture operation itself** was queued and can be tracked by that `JobId`. In reality the
   manufacture log is already committed by the time the response returns, and `JobId` is just
   `manufactureLog.Id` as a string — there is no handler, controller action, or job store that accepts this
   `JobId` to report status. It mimics the naming convention of the real async-job features in this codebase
   (e.g. `useEnqueueInvoiceImport` / `EnqueueImportInvoicesResponse`, which back a genuine pollable job), which
   makes the false impression stronger for anyone reading the code by pattern-matching.

3. No backend unit tests reference `EnqueueGiftPackageManufactureHandler` or `EnqueueGiftPackageManufactureRequest`.
   One frontend test (`StockUpGate.test.tsx`) mocks both hooks; no e2e test touches either route.

## Decision

Go with the finding's **Option A** (remove the duplicate), adapted for the fact that `/enqueue` — not the
sync endpoint — is the one actually wired into the UI today:

- Keep exactly one endpoint: `POST /api/logistics/gift-packages/manufacture`, backed by
  `CreateGiftPackageManufactureHandler` (already correctly, honestly named — no async claim).
- Delete the `/enqueue` endpoint, handler, request, and response types.
- Repoint the frontend's manufacturing button to call the surviving sync endpoint instead of the one being
  removed (i.e., promote the currently-dead `onManufacture`/`handleManufacture` wiring to be the one actually
  used by the button, and delete the now-fully-dead `onEnqueueManufacture` plumbing).
- The eshop stock-up pipeline (`StockUpOperation` rows + recurring processing job) is untouched — it already
  works the same way regardless of which controller endpoint created the operations, and the UI's existing
  "Zobrazit operace naskladnění" link continues to be the correct way to track that async part.

Option B (implement real Hangfire-backed async queuing) is rejected: nothing in the finding, the code, or the
task description indicates a real requirement for async manufacture-job tracking; building that would be
speculative scope beyond what's asked (YAGNI).

## Functional requirements

**FR-1 — Single manufacture endpoint.**
`POST /api/logistics/gift-packages/manufacture` remains the only way to trigger gift package manufacture.
`POST /api/logistics/gift-packages/manufacture/enqueue` is removed (404 after the change).
- Acceptance: route table / Swagger no longer lists `/enqueue`; calling it returns 404.

**FR-2 — Backend cleanup.**
Remove `EnqueueGiftPackageManufactureHandler`, `EnqueueGiftPackageManufactureRequest`,
`EnqueueGiftPackageManufactureResponse`, and the corresponding controller action in `LogisticsController.cs`.
- Acceptance: `dotnet build` succeeds with zero references to the removed types anywhere in `backend/`.

**FR-3 — Frontend rewiring.**
`GiftPackageManufacturingDetail.tsx`'s manufacturing button calls the sync `onManufacture` handler (backed by
`useCreateGiftPackageManufacture` / `logistics_CreateGiftPackageManufacture`) instead of
`onEnqueueManufacture`. Remove `useEnqueueGiftPackageManufacture`, the `EnqueueGiftPackageManufactureRequest`
import/usage, and the `onEnqueueManufacture` prop/handler chain from `index.tsx` and
`GiftPackageManufacturingDetail.tsx`.
- Acceptance: clicking "Zadat k výrobě" in the UI results in a call to
  `POST /api/logistics/gift-packages/manufacture` (verified manually or via an updated component test), and
  the success path still invalidates the same queries (`giftPackages "available"` and `"manufacture" "log"`,
  per `useCreateGiftPackageManufacture`'s existing `onSuccess`).

**FR-4 — OpenAPI client regeneration.**
Regenerate the TypeScript API client (`npm run build` per `docs/development/api-client-generation.md`) so
`EnqueueGiftPackageManufactureRequest/Response` and `logistics_EnqueueGiftPackageManufacture` are removed from
`frontend/src/api/generated/api-client.ts`.
- Acceptance: no leftover references to `Enqueue*GiftPackage*` in the generated client after rebuild.

**FR-5 — Test updates.**
Update `StockUpGate.test.tsx` (and any other test mocking `useEnqueueGiftPackageManufacture`) to drop the
removed hook and mock/assert the sync hook instead. Add/adjust a test asserting the manufacture button now
triggers the sync mutation.
- Acceptance: `npm run build` (FE), `npm run lint`, and the full FE test suite pass; `dotnet build` +
  `dotnet format` pass; no test file still imports a deleted symbol.

## Non-functional requirements

- **No behavior change to the actual manufacturing side effects** (log creation, stock operation rows,
  eshop push timing) — this is a pure API-surface consolidation, not a logic change.
- **No regression to the async eshop stock-up pipeline** — `StockUpProcessingService` and its recurring job
  registration in `CatalogModule.cs` must remain untouched.
- Response contract for the surviving endpoint is unchanged (`CreateGiftPackageManufactureResponse` already
  exists and is unaffected).

## Data model

No entity/schema changes. `GiftPackageManufactureLog`, `StockUpOperation` and their relationships are
unaffected — this is purely a controller/handler/DTO/frontend-hook surface reduction.

## Interfaces

- Removed: `POST /api/logistics/gift-packages/manufacture/enqueue`
  (`LogisticsController.cs:94-105`, `EnqueueGiftPackageManufactureHandler.cs`,
  `EnqueueGiftPackageManufactureRequest.cs`, `EnqueueGiftPackageManufactureResponse.cs`).
- Unchanged, becomes sole entry point: `POST /api/logistics/gift-packages/manufacture`
  (`LogisticsController.cs:71-78`, `CreateGiftPackageManufactureHandler.cs`).
- Frontend: `useEnqueueGiftPackageManufacture` removed from `useGiftPackageManufacturing.ts`;
  `GiftPackageManufacturingDetail`'s manufacture button switches from `onEnqueueManufacture` to
  `onManufacture`.

## Dependencies and scope

- Depends on: existing `CreateGiftPackageManufactureHandler` / `useCreateGiftPackageManufacture` (already
  implemented and tested — `useGiftPackageManufacturing.test.ts:296+`), OpenAPI client generation tooling.
- Out of scope: any change to `StockUpProcessingService`, the recurring stock-up processing job, the
  `/stock-up-operations` page, or `GiftPackageManufactureLog`/disassembly logic. No Hangfire-based real async
  queuing is being introduced (Option B rejected — see Decision).

## Rough plan

1. **Backend removal**: delete `EnqueueGiftPackageManufactureHandler.cs`, `EnqueueGiftPackageManufactureRequest.cs`,
   `EnqueueGiftPackageManufactureResponse.cs`, and the `EnqueueGiftPackageManufacture` controller action +
   its `using` in `LogisticsController.cs`. `dotnet build` + `dotnet format`.
2. **Frontend removal/rewiring**: in `useGiftPackageManufacturing.ts` remove `useEnqueueGiftPackageManufacture`
   and its imports; in `GiftPackageManufacturingDetail.tsx` change the manufacture button's `onClick` from
   `handleEnqueueManufacture`/`onEnqueueManufacture` to use `onManufacture`, and remove the now-unused
   `onEnqueueManufacture` prop/handler; in `index.tsx` remove `enqueueManufactureMutation`,
   `handleEnqueueManufacture`, and the `EnqueueGiftPackageManufactureRequest` import, keeping
   `createManufactureMutation`/`handleManufacture` as the sole path passed down as `onManufacture`.
3. **Regenerate OpenAPI client**: run the FE build per `docs/development/api-client-generation.md` so
   generated types/methods for the removed endpoint disappear from `api-client.ts`.
4. **Update tests**: fix `StockUpGate.test.tsx` mocks/assertions to reference only the sync hook; verify/adjust
   button-click test coverage in the `GiftPackageManufacturing` test suite to assert the sync mutation fires.
5. **Validate**: `dotnet build`, `dotnet format`, `npm run build`, `npm run lint`, run touched BE/FE test
   suites. Manually exercise the "Zadat k výrobě" flow against a running instance to confirm the button still
   works end-to-end (per `docs/testing` UI-verification expectation).

## Open questions

- **Should the surviving sync response show any user-facing message** equivalent to "Log ID: X — stock
  operations will be processed asynchronously" so users still get feedback that eshop stock-up happens in the
  background? Default assumed here: no change needed — the UI already exposes this via the separate
  "Zobrazit operace naskladnění" (`/stock-up-operations`) link, so no new UI copy is required, but flagging in
  case product wants an inline toast on manufacture success.
- **Confirm no external/legacy caller hits `/enqueue` directly** (e.g., a saved Postman collection, an
  external integration, or a not-yet-found reference) before deleting the route — a repo-wide search
  (frontend, backend, e2e, docs) found none, but this is worth a final grep pass immediately before deletion
  in case something was added between now and implementation.
- **Naming of the button/handler post-merge**: default assumed here is to keep the existing button label
  ("Zadat k výrobě") and just repoint its handler — no copy change requested by the finding.
