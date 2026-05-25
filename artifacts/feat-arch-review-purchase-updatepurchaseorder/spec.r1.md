# Specification: Remove Dead Catalog Lookup in UpdatePurchaseOrderHandler

## Summary
Remove a dead catalog lookup in `UpdatePurchaseOrderHandler.MapToResponseAsync` that performs N catalog reads per order update without using the result. The DTO already sources `MaterialName` from the entity, so the lookup is wasted compute on every `PUT /api/purchase-orders/{id}` request.

## Background
During the daily architecture review on 2026-05-22, an N+1 anti-pattern was found in the Purchase module's update flow.

`UpdatePurchaseOrderHandler.MapToResponseAsync` (backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderHandler.cs:121-157) iterates over each order line and calls `_catalogRepository.GetByIdAsync(line.MaterialId, ...)`. The fetched `material` is used only to compute a local `materialName` variable that is then discarded — the DTO assigns `MaterialName = line.MaterialName` (the value already persisted on the entity), not the computed variable.

For an order with N lines this triggers N catalog lookups that contribute nothing to the response. The repository's bulk method `GetByIdsAsync` was added explicitly to eliminate this kind of N+1 pattern (per its in-code comment). The sibling handler `GetPurchaseOrderByIdHandler` performs the same lookup *legitimately* because it maps `CatalogNote = catalogItem.Note`, a field that lives only in the catalog. The update handler has no such need.

## Functional Requirements

### FR-1: Remove the unused catalog lookup
Delete the two unused lines from `MapToResponseAsync` in `UpdatePurchaseOrderHandler`:

```csharp
var material = await _catalogRepository.GetByIdAsync(line.MaterialId, cancellationToken);
var materialName = material?.ProductName ?? "Unknown Material";
```

No replacement is required. The DTO continues to read `MaterialName = line.MaterialName` from the entity.

**Acceptance criteria:**
- Lines 125-126 (approx.) of `UpdatePurchaseOrderHandler.cs` are removed.
- The `foreach (var line in purchaseOrder.Lines)` loop no longer awaits any catalog repository call.
- The returned `UpdatePurchaseOrderResponse` is byte-identical to the previous implementation for any valid input (i.e., `MaterialName` values in the response are unchanged because they came from `line.MaterialName` all along).
- `_catalogRepository` is still injected only if used elsewhere in the handler; otherwise the field, constructor parameter, and `using` for `Anela.Heblo.Domain.Features.Catalog` are removed.

### FR-2: Remove now-dead dependencies on `ICatalogRepository`
If `MapToResponseAsync` was the only consumer of `ICatalogRepository` in `UpdatePurchaseOrderHandler`, remove:
- The private readonly `_catalogRepository` field.
- The constructor parameter and its assignment.
- Any unused `using` directive for the catalog namespace.

**Acceptance criteria:**
- `UpdatePurchaseOrderHandler` does not reference `ICatalogRepository` after the change unless another method genuinely uses it.
- The project compiles (`dotnet build`) with no new warnings.
- DI registration for the handler still resolves correctly (the handler's constructor takes only the dependencies it actually uses).

### FR-3: Preserve existing handler behavior
The handler must continue to:
- Update the purchase order as before.
- Recompute supplier, totals, and line aggregates exactly as it does today.
- Return the same shape of `UpdatePurchaseOrderResponse` with the same field values for the same inputs.

**Acceptance criteria:**
- No change to the public method signatures of `UpdatePurchaseOrderHandler`, the request, or the response DTO.
- No change to validation, persistence, or transaction semantics.
- Existing unit/integration tests for `UpdatePurchaseOrderHandler` pass without modification (other than any test that asserted on the catalog call count, if such a test exists).

### FR-4: Verify no other handlers have the same dead lookup
Briefly check sibling handlers in `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/**` for the same pattern (catalog `GetByIdAsync` inside a line loop whose result is unused). Apply the same removal where confirmed.

**Acceptance criteria:**
- A grep for `GetByIdAsync(line.MaterialId` (or equivalent) under the Purchase use-cases folder reveals no remaining dead lookups.
- Any additional removals are listed in the PR description.
- If `GetPurchaseOrderByIdHandler` is the only other handler with such a call, it is left untouched (its use of `catalogItem.Note` is legitimate).

## Non-Functional Requirements

### NFR-1: Performance
- After the change, `PUT /api/purchase-orders/{id}` issues zero catalog repository calls during response mapping (down from N per order line).
- No regression in response time; expected modest improvement proportional to line count and catalog backing-store latency.

### NFR-2: Code quality
- `dotnet build` produces no new warnings.
- `dotnet format` leaves the file unchanged after the edit.
- No dead `using` directives remain.

### NFR-3: Backwards compatibility
- The HTTP contract for `PUT /api/purchase-orders/{id}` is unchanged.
- The generated OpenAPI schema for `UpdatePurchaseOrderResponse` is identical.
- No frontend changes are required.

## Data Model
No changes. `PurchaseOrderLine` continues to store `MaterialName` as a denormalized field; `UpdatePurchaseOrderResponse.PurchaseOrderLineDto.MaterialName` continues to be sourced from that field.

## API / Interface Design
No changes. The affected endpoint is `PUT /api/purchase-orders/{id}`; its request and response shapes are unchanged.

## Dependencies
- `Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder.UpdatePurchaseOrderHandler` (the file being edited).
- `Anela.Heblo.Domain.Features.Catalog.ICatalogRepository` (dependency may be removed from this handler).
- Existing test project for the Purchase feature (no new tests required unless coverage gaps are uncovered).

## Out of Scope
- Refactoring `GetPurchaseOrderByIdHandler` (its catalog lookup is legitimate). If a bulk `GetByIdsAsync` refactor for that handler is desired, it should be filed as a separate brief.
- Adding `CatalogNote` (or other catalog-only fields) to `UpdatePurchaseOrderResponse`. If product later wants this, the implementation must use `GetByIdsAsync` once outside the loop.
- Broader audit of N+1 patterns outside the Purchase module.
- Any frontend changes.
- Changes to caching policy in `ICatalogRepository`.

## Open Questions
None.

## Status: COMPLETE