# Design: Consolidate GiftPackageManufacture to a single sync endpoint

Implements the "Decision" in `plan-01.md`: delete the `/enqueue` path (handler, request/response DTOs,
controller action, generated client members, frontend hook) and repoint the UI's manufacturing button to the
already-implemented sync path.

No new UI surface is introduced — the modal, its buttons, and its layout are unchanged. This is a wiring swap,
not a UX redesign, so the UX/UI section below only documents the one behavioral change (which handler the
existing button calls) and is intentionally thin.

## UX/UI

No new screens, layout, or visual elements. One interaction changes underneath an existing button:

```
┌─ GiftPackageManufacturingDetail (modal) ──────────────────┐
│ ...                                                        │
│ [ Výroba ] tab                                             │
│   Množství k výrobě: [ - ] [ 3 ] [ + ]                     │
│   ( Potřebné (3) )  ( Týdenní (7) )                        │
│                                                              │
│   ┌────────────────────────────────────────────────────┐  │
│   │ 🔄  Zadat k výrobě (3 ks)                           │  │  ← onClick target changes:
│   └────────────────────────────────────────────────────┘  │    handleEnqueueManufacture → handleManufacture
│   ┌────────────────────────────────────────────────────┐  │    (calls onManufacture, not onEnqueueManufacture)
│   │ 📄  Zobrazit operace naskladnění                    │  │
│   └────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────┘
```

Button label, position, disabled state (`!validationResults.isValid`), and icon are unchanged. No loading
spinner exists on this button today (neither the sync nor the async path wires `isPending` into it) — that
remains out of scope; not introducing it now avoids scope creep beyond the finding.

Component hierarchy after the change (unchanged shape, one prop removed):

```
GiftPackageManufacturing (index.tsx)
 ├─ owns: createManufactureMutation = useCreateGiftPackageManufacture()
 ├─ handleManufacture(quantity) → createManufactureMutation.mutateAsync(...)
 └─ <GiftPackageManufacturingDetail
       onManufacture={handleManufacture}      ← sole manufacture callback now
       ... (onEnqueueManufacture prop removed)
    />
       └─ "Zadat k výrobě" button onClick → local handleManufacture() → props.onManufacture(quantity) → onClose()
```

## Component design

### Backend

**Removed components** (delete entirely, no replacement):
- `EnqueueGiftPackageManufactureHandler` (`.../UseCases/EnqueueGiftPackageManufacture/EnqueueGiftPackageManufactureHandler.cs`)
- `EnqueueGiftPackageManufactureRequest` (same folder)
- `EnqueueGiftPackageManufactureResponse` (same folder)
- The now-empty `UseCases/EnqueueGiftPackageManufacture/` folder itself
- `LogisticsController.EnqueueGiftPackageManufacture` action (`LogisticsController.cs:94-105`, including its
  XML doc comment and the `[HttpPost("gift-packages/manufacture/enqueue")]` route)
- The `using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.EnqueueGiftPackageManufacture;`
  line in `LogisticsController.cs:3`

**Unchanged, becomes sole entry point:**
- `CreateGiftPackageManufactureHandler` / `CreateGiftPackageManufactureRequest` / `CreateGiftPackageManufactureResponse`
- `LogisticsController.CreateGiftPackageManufacture` action (`LogisticsController.cs:71-79`) — no code change,
  it already returns the honest response
- `IGiftPackageManufactureService.CreateManufactureAsync` and its implementation — untouched. Both old handlers
  called the identical method with identical arguments in identical order, so there is zero behavioral delta
  to the manufacturing logic, stock-operation creation, or the downstream `StockUpProcessingService` recurring
  job.

No new interfaces, no DI registration changes (MediatR auto-discovers handlers by assembly scan; removing a
handler class requires no explicit unregistration). No module wiring in `GiftPackageManufactureModule.cs` or
`CatalogModule.cs` references the enqueue types — confirmed via repo-wide grep, so removal is a pure deletion
with no dangling registrations.

### Frontend

**`useGiftPackageManufacturing.ts`** — remove:
- `useEnqueueGiftPackageManufacture` hook (lines 109-125)
- Its imports: `EnqueueGiftPackageManufactureRequest`, `EnqueueGiftPackageManufactureResponse` from
  `../generated/api-client`

`useCreateGiftPackageManufacture` (lines 74-88) is unchanged and becomes the only manufacture mutation hook.
Its existing `onSuccess` (invalidating `giftPackages "available"` and `giftPackages "manufacture" "log"`)
is the query-invalidation behavior the UI will now always get on manufacture success. Note this differs from
the enqueue hook's `onSuccess` (which invalidated a `giftPackages "jobs"` key that no other hook or query in
this codebase produces/consumes — grepped, no `"jobs"` query key exists elsewhere, so it was dead
invalidation). No new invalidation logic needed; the sync hook's existing behavior is strictly more correct
(it actually refreshes the data the modal and list depend on).

**`GiftPackageManufacturingDetail.tsx`**:
- Remove the `onEnqueueManufacture: (quantity: number) => Promise<void>` prop from
  `GiftPackageManufacturingDetailProps` and the component's destructured props.
- Remove the local `handleEnqueueManufacture` function (lines 99-108).
- Add a local `handleManufacture` wrapper mirroring the removed one's shape, calling the renamed prop:
  ```ts
  const handleManufacture = async () => {
    if (!selectedPackage) return;
    try {
      await onManufacture(quantity);
      onClose();
    } catch (error) {
      console.error('Manufacturing error:', error);
    }
  };
  ```
- Change the "Zadat k výrobě" button's `onClick` (line 404) from `handleEnqueueManufacture` to the new
  `handleManufacture`. Label, icon, disabled condition, and styling stay identical.

**`index.tsx`**:
- Remove `useEnqueueGiftPackageManufacture` import and the `enqueueManufactureMutation` it backs (line 32).
- Remove `handleEnqueueManufacture` (lines 98-114) and the `EnqueueGiftPackageManufactureRequest` import
  (line 8).
- Remove the `onEnqueueManufacture={handleEnqueueManufacture}` prop passed to `<GiftPackageManufacturingDetail>`
  (line 138).
- `handleManufacture` (lines 80-96, backed by `createManufactureMutation`) is unchanged and becomes the sole
  handler passed as `onManufacture`.

No other component in the `GiftPackageManufacturing` folder references either hook or the enqueue types
(`GiftPackageManufacturingList.tsx`, `GiftPackageManufacturingFilters.tsx`, `GiftPackageManufacturingSummary.tsx`,
`DisassemblyTabContent.tsx` are all unaffected — confirmed via grep).

**Generated client** (`frontend/src/api/generated/api-client.ts`): not hand-edited. Regenerating via
`npm run build` (per `docs/development/api-client-generation.md`) after the backend deletion removes
`logistics_EnqueueGiftPackageManufacture`, `EnqueueGiftPackageManufactureRequest`, and
`EnqueueGiftPackageManufactureResponse` from the generated output automatically, since the OpenAPI spec no
longer describes that route once the controller action is gone.

### Tests

- `StockUpGate.test.tsx`: remove the `useEnqueueGiftPackageManufacture` import, its `jest.mock` entry, the
  `mockUseEnqueueGiftPackageManufacture` const, and its `beforeEach` default mock (lines 6-8, 14, 91, 122-126).
  Also drop `EnqueueGiftPackageManufactureRequest` from the `jest.mock("../../../../api/generated/api-client")`
  factory (line 62-64). These tests only assert `useStockUpOperationsSummary` gating behavior — the enqueue
  mock is incidental scaffolding, not something the test suite is verifying; removing it changes nothing about
  what the three `it()` blocks check.
- `useGiftPackageManufacturing.test.ts`: no change needed — it already only tests `useCreateGiftPackageManufacture`
  (and query/detail hooks); it never imported or tested the enqueue hook.
- No new test file is required by this change; FR-3's acceptance criterion (button triggers the sync mutation)
  is satisfiable by the existing `StockUpGate.test.tsx` render path if desired, but since that suite doesn't
  currently exercise button clicks (it mocks out `GiftPackageManufacturingDetail` entirely), verification of
  the click-to-mutation wiring is a manual/E2E concern per the plan's step 5, not a new unit test obligation.

## Data schemas

No database schema changes — `GiftPackageManufactureLog`, `GiftPackageManufactureItem`, and `StockUpOperation`
entities and their EF configurations (`Anela.Heblo.Persistence/Logistics/GiftPackageManufacture/*`) are
untouched.

**Request/response shapes removed from the API surface:**

```
DELETE POST /api/logistics/gift-packages/manufacture/enqueue

Request (removed):
{
  "giftPackageCode": string,
  "quantity": number,
  "allowStockOverride": boolean
}

Response (removed):
{
  "jobId": string,       // was manufactureLog.Id.ToString() — never a real job identifier
  "message": string      // was the misleading "will be processed asynchronously" text
}
```

**Surviving shape (unchanged):**

```
POST /api/logistics/gift-packages/manufacture

Request:
{
  "giftPackageCode": string,
  "quantity": number,
  "allowStockOverride": boolean
}

Response:
{
  "manufacture": GiftPackageManufactureDto   // existing shape, unaffected by this change
}
```

No event payloads are involved — `CreateManufactureAsync` does not publish domain events for this operation
(confirmed by reading `GiftPackageManufactureService.cs`); nothing to update there.

## Traceability to plan-01 requirements

| Requirement | Design element |
|---|---|
| FR-1 (single endpoint) | Controller action + route removal above |
| FR-2 (backend cleanup) | Handler/Request/Response deletion above |
| FR-3 (frontend rewiring) | `GiftPackageManufacturingDetail.tsx` + `index.tsx` changes above |
| FR-4 (OpenAPI regen) | Generated client section above — no manual edits, rebuild only |
| FR-5 (test updates) | Tests section above |
