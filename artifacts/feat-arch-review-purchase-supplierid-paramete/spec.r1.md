# Specification: Remove dead `supplierId` parameter from `IPurchaseOrderRepository.GetPaginatedAsync`

## Summary
The `supplierId` parameter on `IPurchaseOrderRepository.GetPaginatedAsync` is plumbed through the API contract, handler, and both repository implementations but its filtering body is empty — every caller that passes a value gets unfiltered results, silently. This spec removes the parameter end-to-end (interface, both implementations, handler, request DTO, and controller query string), aligning the public contract with actual behavior. The frontend list page does not currently expose a supplier filter, so removal is the lowest-risk option and matches the YAGNI/dead-code stance in the brief.

## Background

The arch-review routine (2026-05-22) flagged matching dead `if (supplierId.HasValue) { … }` blocks in two places:

- `backend/src/Anela.Heblo.Persistence/Purchase/PurchaseOrders/PurchaseOrderRepository.cs:50-54`
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/InMemoryPurchaseOrderRepository.cs:70-74`

Both contain the same comment: *"SupplierId filtering is disabled as we now use SupplierName. In future, implement supplier name filtering if needed."* The comment is misleading — the `PurchaseOrder` entity (`backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrder.cs:9-10`) still stores both `SupplierId` (long) and `SupplierName` (string). Nothing was actually migrated; the filter was simply turned off.

Current call graph:

- HTTP query param `SupplierId` (int) → `GetPurchaseOrdersRequest.SupplierId` (`int?`) → `GetPurchaseOrdersHandler` → `IPurchaseOrderRepository.GetPaginatedAsync(..., int? supplierId, ...)` → no-op.
- The frontend list page (`frontend/src/components/pages/PurchaseOrderList.tsx`) builds the request without ever setting `supplierId`; the only UI references to `supplierId` are in the create/edit form helpers (`frontend/src/components/purchase-orders/form/PurchaseOrderHelpers.tsx`), which target the create/update endpoints, not the list endpoint.
- The TypeScript hook (`frontend/src/api/hooks/usePurchaseOrders.ts:24,65-66`) still accepts and forwards `supplierId`, but no caller sets it.
- Additional type mismatch: the parameter is `int?` while the domain stores `long SupplierId`. Implementing the filter would also require a type fix.

There are **no tests** asserting supplier-based filtering at the list endpoint (verified by grep of `GetPaginatedAsync` tests in the Purchase module — none exist; only Invoices/Catalog/DataQuality tests cover their own equivalent methods).

The decision direction (remove vs. implement) was settled by the brief's "If the filter is not needed" branch, since there is no UI consumer, no test, and no product driver for supplier filtering on the purchase-orders list today. If supplier filtering becomes a product need later, it can be reintroduced as a typed (`long?`) parameter with real implementation and tests at that time.

## Functional Requirements

### FR-1: Remove `supplierId` from the repository interface
Delete the `int? supplierId` parameter from `IPurchaseOrderRepository.GetPaginatedAsync` in `backend/src/Anela.Heblo.Domain/Features/Purchase/IPurchaseOrderRepository.cs`. The parameter order of remaining arguments is preserved.

**Acceptance criteria:**
- `IPurchaseOrderRepository.GetPaginatedAsync` signature no longer contains `int? supplierId`.
- Solution compiles after the change with no warnings introduced by this edit.

### FR-2: Remove `supplierId` from the EF Core implementation
Delete the parameter and the dead `if (supplierId.HasValue) { … }` block from `PurchaseOrderRepository.GetPaginatedAsync` in `backend/src/Anela.Heblo.Persistence/Purchase/PurchaseOrders/PurchaseOrderRepository.cs`.

**Acceptance criteria:**
- Parameter removed from method signature.
- The empty `if` block and its misleading comment are deleted.
- All other filter branches (`searchTerm`, `status`, `fromDate`, `toDate`, `activeOrdersOnly`) remain functionally unchanged.

### FR-3: Remove `supplierId` from the in-memory implementation
Apply the same edit to `InMemoryPurchaseOrderRepository.GetPaginatedAsync` in `backend/src/Anela.Heblo.Application/Features/Purchase/Services/InMemoryPurchaseOrderRepository.cs`.

**Acceptance criteria:**
- Parameter removed from method signature.
- The empty `if` block and its comment are deleted.
- Behavior matches the EF implementation for the remaining parameters.

### FR-4: Remove `supplierId` from the handler call site
In `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrders/GetPurchaseOrdersHandler.cs`, remove the `request.SupplierId` argument from the `_repository.GetPaginatedAsync(...)` invocation.

**Acceptance criteria:**
- The handler no longer references `request.SupplierId`.
- The handler still passes all other filter values through unchanged.
- The structured log line on entry remains; if `SupplierId` is mentioned in any future log template, it is removed (currently it is not logged).

### FR-5: Remove `SupplierId` from the request DTO
Delete the `public int? SupplierId { get; set; }` property from `GetPurchaseOrdersRequest` in `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrders/GetPurchaseOrdersRequest.cs`.

**Acceptance criteria:**
- Property removed.
- DTO remains a class (per project rule — no records for DTOs).
- No remaining backend reference to `GetPurchaseOrdersRequest.SupplierId`.

### FR-6: Regenerate OpenAPI clients and update frontend hook
The TypeScript client (`frontend/src/api/generated/api-client.ts`) and the hand-written wrapper (`frontend/src/api/hooks/usePurchaseOrders.ts`) currently include `supplierId` for the list endpoint. After regeneration the generated file loses the parameter automatically; the wrapper must be updated to match.

**Acceptance criteria:**
- `purchaseOrders_GetPurchaseOrders` in `frontend/src/api/generated/api-client.ts` no longer has a `supplierId` parameter (verified after `npm run build` regenerates the client).
- `GetPurchaseOrdersRequest` interface in `frontend/src/api/hooks/usePurchaseOrders.ts` no longer declares `supplierId?: number`.
- The `if (request.supplierId) params.append("SupplierId", …)` block in `usePurchaseOrders.ts` is removed.
- Form helpers (`PurchaseOrderHelpers.tsx`) are **not** touched — they reference the create/update endpoints' `supplierId`, which is unrelated and remains valid.
- `npm run build` and `npm run lint` pass.

### FR-7: Verify backward compatibility of the HTTP contract
The `GET /api/purchase-orders` endpoint must continue to accept and ignore (or 400, see Open Questions) a `SupplierId` query string from older clients during deployment. The default ASP.NET Core query-binding behavior silently ignores unknown query parameters, which is acceptable.

**Acceptance criteria:**
- Sending `GET /api/purchase-orders?SupplierId=42` against the patched backend returns the same payload as `GET /api/purchase-orders` (HTTP 200, no validation error, `SupplierId` is ignored by model binding).
- This is the existing default ASP.NET Core behavior; no extra code is required to preserve it. A single integration test pins this contract.

### FR-8: Tests
Add regression coverage for the list endpoint to prevent silent reintroduction of the dead parameter.

**Acceptance criteria:**
- Unit test in `backend/test/Anela.Heblo.Tests/Features/Purchase/` (create folder if missing) that calls `GetPurchaseOrdersHandler.Handle` with the in-memory repository and asserts the standard filters work (search term, status, date range, active-only).
- Integration or controller-level test confirming `GET /api/purchase-orders?SupplierId=99` returns the same row count as `GET /api/purchase-orders` (FR-7 contract pin).
- Existing Purchase tests continue to pass.

## Non-Functional Requirements

### NFR-1: Performance
No performance change expected. The dead block executed zero LINQ operations; removing it removes nothing from the query plan. Verify the generated SQL for the list endpoint is identical before/after the change (manual EF logging spot-check is sufficient).

### NFR-2: Security
No new security surface. Removing an ignored parameter does not expose data; the endpoint's existing authorization (the standard MVC controller policy applied to `PurchaseOrdersController`) is unchanged.

### NFR-3: Backward compatibility
- Old TypeScript clients in production continue to work because ASP.NET Core ignores unknown query-string parameters. (FR-7.)
- No database migration required.
- No public contract is "broken" in the meaningful sense, because the broken behavior (silent no-op) is precisely what is being removed.

### NFR-4: Code quality
- `dotnet build` must produce no new warnings.
- `dotnet format` clean.
- `npm run build` and `npm run lint` clean.
- No commented-out code or "removed for future use" notes left behind.

## Data Model

No schema changes.

Domain entity (`PurchaseOrder`) continues to hold both `SupplierId` (long) and `SupplierName` (string). Neither column is dropped or renamed. The entity is unchanged.

## API / Interface Design

### Backend

**Before:**
```csharp
Task<(List<PurchaseOrder> Orders, int TotalCount)> GetPaginatedAsync(
    string? searchTerm,
    string? status,
    DateTime? fromDate,
    DateTime? toDate,
    int? supplierId,
    bool? activeOrdersOnly,
    int pageNumber,
    int pageSize,
    string sortBy,
    bool sortDescending,
    CancellationToken cancellationToken = default);
```

**After:**
```csharp
Task<(List<PurchaseOrder> Orders, int TotalCount)> GetPaginatedAsync(
    string? searchTerm,
    string? status,
    DateTime? fromDate,
    DateTime? toDate,
    bool? activeOrdersOnly,
    int pageNumber,
    int pageSize,
    string sortBy,
    bool sortDescending,
    CancellationToken cancellationToken = default);
```

`GetPurchaseOrdersRequest` loses the `SupplierId` property. The HTTP endpoint path and verb (`GET /api/purchase-orders`) are unchanged; the `SupplierId` query parameter is removed from the documented contract (OpenAPI regeneration handles this).

### Frontend

`usePurchaseOrders.ts` `GetPurchaseOrdersRequest` interface loses `supplierId?: number`. The query-builder loses its `SupplierId` append. The auto-generated `api-client.ts` regenerates without the parameter as a side effect of `npm run build`.

UI is unaffected — no list filter currently references supplier.

## Dependencies

- **OpenAPI client regeneration**: The TypeScript client is generated on backend build (`npm run build` or backend swagger generation per `docs/development/api-client-generation.md`). The regeneration must happen as part of this change.
- **Existing tests**: All purchase order tests must continue to pass. None currently reference `supplierId` for the purchase list, but the build will fail to compile if any do.

No external services, no new libraries.

## Out of Scope

- Implementing real supplier filtering (by id, name, or both). This is explicitly deferred. If it becomes a product need, it should be reintroduced with: a typed `long?` parameter matching the domain, a real `WHERE` clause in both repositories, tests, and a UI filter in `PurchaseOrderList.tsx`.
- Cleaning up the misleading "we now use SupplierName" narrative in any other location — grep confirmed it appears only in the two dead blocks being removed.
- Changing the `SupplierId` / `SupplierName` shape of the domain entity or any create/update DTO.
- Refactoring other `GetPaginatedAsync` implementations across the codebase (Invoices, Catalog/Inventory, DataQuality) — they are unrelated.
- Changes to `PurchaseOrderHelpers.tsx`, which uses `supplierId` for create/update payloads (a different endpoint).

## Open Questions

None.

## Status: COMPLETE