```markdown
# Architecture Review: Remove Dead Catalog Lookup in UpdatePurchaseOrderHandler

## Skip Design: true

## Architectural Fit Assessment

This change is a **pure dead-code removal in a single MediatR handler**. It does not introduce architectural deviations — it *restores* alignment with the codebase's stated direction:

- The Catalog domain already exposes `ICatalogRepository.GetByIdsAsync` (`backend/src/Anela.Heblo.Domain/Features/Catalog/ICatalogRepository.cs:54`), explicitly added to avoid N+1 patterns. The removed loop directly contradicted that design.
- The Purchase feature follows the Application's Vertical Slice convention: each use case is one folder under `Features/Purchase/UseCases/*` with `Handler` + `Request` + `Response`. The edit stays inside `UpdatePurchaseOrder/UpdatePurchaseOrderHandler.cs` — no slice boundaries are crossed.
- The `PurchaseOrderLineDto.MaterialName` is sourced from the denormalized `PurchaseOrderLine.MaterialName` entity field. This is the established convention in both `CreatePurchaseOrderHandler.MapToResponseAsync` (no catalog lookup) and `GetPurchaseOrderByIdHandler` (catalog lookup only because `CatalogNote` lives solely in the catalog). The Update handler is the outlier.

**Integration points** are limited to: the single handler file, its DI registration (unchanged), and any tests that referenced catalog calls inside `MapToResponseAsync` (none exist — only `UpdatePurchaseOrderStatusHandlerTests.cs` lives under the Purchase test folder).

## Proposed Architecture

### Component Overview

```
PUT /api/purchase-orders/{id}
        │
        ▼
PurchaseOrdersController ── MediatR ──► UpdatePurchaseOrderHandler
                                              │
                                              ├─ ISupplierRepository.GetByIdAsync  (validates supplier)
                                              ├─ ICatalogRepository.GetByIdAsync   (USED — to set MaterialName on AddLine/UpdateLine in Handle())
                                              ├─ IPurchaseOrderRepository           (load, persist)
                                              │
                                              └─ MapToResponseAsync(purchaseOrder)  ◄── NO catalog calls after this change
                                                       │
                                                       └─► UpdatePurchaseOrderResponse  (MaterialName comes from entity)
```

The architecture **does not change**. Only the `MapToResponseAsync` method is simplified: the loop body becomes a pure entity-to-DTO projection with no awaits.

### Key Design Decisions

#### Decision 1: Drop the lookup outright vs. switch to `GetByIdsAsync`
**Options considered:**
- (a) Delete the two unused lines (spec FR-1).
- (b) Replace the per-line `GetByIdAsync` with a single `GetByIdsAsync` outside the loop.

**Chosen approach:** (a) — delete.

**Rationale:** The fetched value is never read. Replacing a dead call with a "more efficient" dead call still produces zero observable output and adds a dictionary allocation. `GetByIdsAsync` becomes warranted only when a catalog-only field (e.g. `CatalogNote`) is added to `UpdatePurchaseOrderResponse`; that is explicitly out of scope.

#### Decision 2: Keep `ICatalogRepository` as a constructor dependency
**Options considered:**
- (a) Remove `_catalogRepository`, the constructor parameter, and the `using Anela.Heblo.Domain.Features.Catalog;` directive (FR-2's conditional path).
- (b) Keep the dependency.

**Chosen approach:** (b) — keep.

**Rationale:** Verification of the file shows `ICatalogRepository` is still consumed inside the main `Handle` method at `UpdatePurchaseOrderHandler.cs:80` and `:93`, where it computes `materialName` for `UpdateLine`/`AddLine`. Those calls are legitimate (the result is passed to the domain method). The conditional in spec FR-2 ("If `MapToResponseAsync` was the only consumer…") therefore evaluates to false. Field, constructor parameter, and `using` directive must **stay**.

> This is an amendment to the spec — see "Specification Amendments" below.

#### Decision 3: Scope of the "sibling handler" check (FR-4)
**Options considered:**
- (a) Grep narrowly for unused `material` lookups inside response-mapping methods.
- (b) Grep for any `_catalogRepository.GetByIdAsync(...)` inside a line loop.

**Chosen approach:** (a) — match the spec's definition of "dead" (result unused).

**Rationale:** A grep across `Features/Purchase/UseCases` shows four call sites:
- `UpdatePurchaseOrderHandler.cs:80, :93` — result **used** in `UpdateLine`/`AddLine`. Not dead. Not an N+1 to fix in this change.
- `UpdatePurchaseOrderHandler.cs:128` — result **unused**. This is the target of FR-1.
- `CreatePurchaseOrderHandler.cs:79` — result **used** in `AddLine`. Not dead. `CreatePurchaseOrderHandler.MapToResponseAsync` already does the right thing (no catalog calls). 
- `GetPurchaseOrderByIdHandler.cs:54` — result **used** for `CatalogNote`. Legitimate per the brief.

Conclusion: only the one site qualifies. The other in-loop `GetByIdAsync` calls in the same handler (`:80`, `:93`) are an N+1 *performance* concern but not "dead." They are out of scope for this brief and should be filed separately if desired.

## Implementation Guidance

### Directory / Module Structure

No new files. Single-file edit:

```
backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/
    └── UpdatePurchaseOrderHandler.cs   ← edit MapToResponseAsync only
```

### Interfaces and Contracts

No interface or contract changes:
- `UpdatePurchaseOrderRequest` / `UpdatePurchaseOrderResponse` / `PurchaseOrderLineDto` shapes are unchanged.
- `ICatalogRepository`, `IPurchaseOrderRepository`, `ISupplierRepository` are unchanged.
- The handler's constructor signature is **unchanged** (Decision 2).
- DI registration is **unchanged**.

### Data Flow

For `PUT /api/purchase-orders/{id}` after the change, the response-mapping phase has the following shape:

```
purchaseOrder (entity, already loaded with .Lines tracked)
        │
        ▼
foreach line in purchaseOrder.Lines:        ── synchronous, no awaits ──►
    project to PurchaseOrderLineDto using only entity fields
        │
        ▼
return UpdatePurchaseOrderResponse { ..., Lines = lines }
```

The handler-level data flow (load → validate supplier → mutate aggregate → save) is untouched.

The concrete edit:

- Remove `UpdatePurchaseOrderHandler.cs:127–129` (the comment and the two `var material` / `var materialName` lines).
- The enclosing `foreach (var line in purchaseOrder.Lines)` loop body becomes the single `lines.Add(new PurchaseOrderLineDto { … })` block already present at `:131–141`.
- `MapToResponseAsync` no longer needs to be `async` in principle, **but** keep it `async Task<UpdatePurchaseOrderResponse>` and keep the `CancellationToken cancellationToken` parameter to avoid a ripple change at the single call site (`:110`) and to preserve any future extension point. The compiler will emit a CS1998 ("async method lacks await") — suppress this by either:
    - (preferred) changing the signature to `private UpdatePurchaseOrderResponse MapToResponse(PurchaseOrder purchaseOrder, long supplierId)` and updating the call site `return MapToResponse(purchaseOrder, request.SupplierId);` — this is the cleaner option and aligns with NFR-2 ("no new warnings").
    - or adding `await Task.CompletedTask;` — **not recommended**; it just papers over the warning.

> **Recommended:** drop `async`, the `Task<…>` wrapper, and the `CancellationToken` parameter from `MapToResponseAsync`, and rename to `MapToResponse`. Update the call site at `:110` accordingly. This keeps the method honest and satisfies NFR-2 without any warning suppression.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Removing the await turns the method synchronous → CS1998 warning ("no new warnings" NFR-2 violated). | Medium | Apply Decision 3's recommended sub-step: change signature to a sync `MapToResponse` and update the single caller. |
| A test asserts on a mocked `_catalogRepository.GetByIdAsync` call count for the update flow. | Low | Search `backend/test/**/UpdatePurchaseOrder*` — only `UpdatePurchaseOrderStatusHandlerTests.cs` exists, and it targets a different handler. No update needed. If a test is later found, relax the call-count assertion. |
| Behavior divergence if `line.MaterialName` were ever stale relative to the catalog. | Low | The behavior is **already** this way — the response always returned `line.MaterialName`. Removing the unused lookup cannot change observable output. The denormalized `MaterialName` is refreshed on every `AddLine`/`UpdateLine` in `Handle` (which still uses the catalog), so staleness is bounded to lines that the user does not touch — same as today. |
| Accidental removal of `_catalogRepository` field breaks `Handle` at `:80` / `:93`. | High | Decision 2 explicitly keeps the field. Reviewer checklist item: confirm `_catalogRepository` references still resolve after the edit. |
| `using Anela.Heblo.Domain.Features.Catalog;` removed prematurely. | Low | The `using` is still needed for `ICatalogRepository` type binding in the field declaration. Do not remove. |

## Specification Amendments

1. **FR-2 is a no-op.** Verification confirms `_catalogRepository` is still consumed in `Handle` (lines 80 and 93, used by `UpdateLine`/`AddLine`). The field, constructor parameter, and `using Anela.Heblo.Domain.Features.Catalog;` directive **must remain**. The spec's conditional ("If `MapToResponseAsync` was the only consumer…") evaluates to false. Update the spec to state this explicitly so the implementer does not delete the dependency.

2. **Add sub-step to FR-1: convert `MapToResponseAsync` to a sync `MapToResponse`.** After the two dead lines are removed, the method has no remaining awaits. To honor NFR-2 (no new warnings, no `await Task.CompletedTask` filler), change the signature to `private UpdatePurchaseOrderResponse MapToResponse(PurchaseOrder purchaseOrder, long supplierId)`, drop the unused `CancellationToken`, and update the single call site at `UpdatePurchaseOrderHandler.cs:110`.

3. **FR-4 outcome clarified.** The grep `_catalogRepository.GetByIdAsync` under `Features/Purchase/UseCases` returns four hits; only `UpdatePurchaseOrderHandler.cs:128` matches the "dead" criterion (result unused). The other three in-loop calls (`UpdatePurchaseOrderHandler.cs:80`, `:93`, `CreatePurchaseOrderHandler.cs:79`) use the result and are out of scope. PR description should list this verification explicitly.

4. **Note on adjacent N+1.** `UpdatePurchaseOrderHandler.Handle` itself runs `GetByIdAsync` per line in the request (`:80`, `:93`). This is a genuine N+1 with a *used* result. It is **not** dead code and is therefore out of scope per the brief's framing, but it is the natural next ticket: refactor to a single `GetByIdsAsync(request.Lines.Select(l => l.MaterialId).Distinct())` call outside the loop. Recommend filing as a follow-up brief; do not bundle it here.

## Prerequisites

None. No migrations, configuration, infrastructure, or interface changes are required.

- The `ICatalogRepository.GetByIdsAsync` bulk method already exists and is registered, but is **not used** by this change (Decision 1).
- No frontend regeneration needed: the OpenAPI schema for `UpdatePurchaseOrderResponse` is byte-identical (NFR-3).
- No test fixtures or seed data changes.
- Validation gates per `CLAUDE.md`: `dotnet build` + `dotnet format` + run the Purchase test project. No E2E run required for a behavior-preserving dead-code removal.
```