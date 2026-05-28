# Specification: Remove dead `SupplierId` field from `PurchaseOrderSummaryDto`

## Summary
The `PurchaseOrderSummaryDto.SupplierId` field is always serialized as `0` by `GetPurchaseOrdersHandler`, making it a misleading API contract (dead data) with an additional `int`/`long` type mismatch versus the domain entity. This change removes the field from the DTO and the generated TypeScript client, cleans up the hard-coded `SupplierId = 0` assignment, deletes the stale `// No longer using SupplierId` comment, and verifies that no live consumer depends on the value.

## Background
A prior migration (`20250802173830_ChangeSupplierIdToSupplierName`) shifted the purchase-order supplier identity from a numeric `SupplierId` to a free-text `SupplierName`. Since then:

- `PurchaseOrder.SupplierId` (`long`) is still kept on the domain entity for legacy reasons.
- `GetPurchaseOrdersHandler` (`backend/.../GetPurchaseOrders/GetPurchaseOrdersHandler.cs:44`) explicitly hard-codes `SupplierId = 0` with the inline comment `// No longer using SupplierId`.
- `PurchaseOrderSummaryDto` (`backend/.../Contracts/PurchaseOrderSummaryDto.cs:9`) still exposes `public int SupplierId { get; set; }`, which is serialized on every list response and surfaces in the generated TypeScript client.
- The legacy `?SupplierId=` query string is already silently ignored (`PurchaseOrdersControllerTests.GetPurchaseOrders_WithLegacySupplierIdQueryString_IsSilentlyIgnored`), confirming the field is no longer a real contract on the list endpoint.

A quick consumer audit shows no frontend code reads `summaryDto.supplierId`. All `supplierId` references in the frontend (`PurchaseOrderHelpers.tsx` lines 92, 120, 156) operate on the detail DTO (`PurchaseOrderDto`) and the supplier selector — not on the list response. Removing the field is therefore the right cleanup: it eliminates dead data, the type mismatch, and the stale comment in a single pass.

## Functional Requirements

### FR-1: Remove `SupplierId` from `PurchaseOrderSummaryDto`
Delete the `public int SupplierId { get; set; }` property from `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/PurchaseOrderSummaryDto.cs`. All other fields (`Id`, `OrderNumber`, `SupplierName`, `OrderDate`, `ExpectedDeliveryDate`, `ContactVia`, `Status`, `InvoiceAcquired`, `TotalAmount`, `LineCount`, `IsEditable`, `CreatedAt`, `CreatedBy`) remain unchanged.

**Acceptance criteria:**
- `PurchaseOrderSummaryDto` no longer declares `SupplierId`.
- The class continues to compile and the rest of its surface is untouched.
- No other backend file references `PurchaseOrderSummaryDto.SupplierId` after the change (grep returns zero hits).

### FR-2: Remove the hard-coded `SupplierId = 0` assignment and stale comment in `GetPurchaseOrdersHandler`
In `GetPurchaseOrdersHandler.cs:44`, remove the `SupplierId = 0, // No longer using SupplierId` line from the object initializer used to build each `PurchaseOrderSummaryDto`. The surrounding initializer and ordering of remaining assignments stay intact.

**Acceptance criteria:**
- The handler no longer assigns `SupplierId` on the DTO.
- The `// No longer using SupplierId` comment is gone.
- `dotnet build` succeeds and `dotnet format` reports no changes for the file.

### FR-3: Regenerate the TypeScript client without `supplierId` on the summary type
After the backend change, regenerate the OpenAPI-derived TypeScript client so that the generated summary type (the class corresponding to `PurchaseOrderSummaryDto` in `frontend/src/api/generated/api-client.ts`, currently around lines 33120–33165) no longer carries a `supplierId` property, constructor read, or `toJSON` write.

**Acceptance criteria:**
- The regenerated summary class in `api-client.ts` has no `supplierId` field, no `this.supplierId = _data["supplierId"]` line, and no `data["supplierId"] = this.supplierId` line.
- Other generated types that legitimately use `supplierId` (e.g. detail DTOs, create/update requests, supplier-selection types) keep their `supplierId` property unchanged.
- `npm run build` and `npm run lint` both pass.

### FR-4: Verify no frontend consumer depends on the removed field
Audit the frontend for any read of `supplierId` on objects typed by the summary DTO (purchase order list responses). Update or remove anything that still reads it. The expected outcome based on current code is "no consumers to fix," but this must be confirmed, not assumed.

**Acceptance criteria:**
- A grep of `frontend/src` for `supplierId` against summary-typed values (list rows in `purchase-orders` views/components and any list-page hooks) returns no usages.
- If a usage is discovered, it is either deleted (if dead) or rewritten to use another field (e.g. `supplierName`); the spec is then amended via Open Questions.

### FR-5: Update or remove tests that reference the summary DTO's `SupplierId`
Any backend or frontend test that asserts on `PurchaseOrderSummaryDto.SupplierId` (or its `supplierId` mirror in the TypeScript client) must be updated to no longer reference the field. Tests that target other DTOs' `SupplierId` (domain `PurchaseOrder`, create/update requests, controller filter tests) are out of scope and remain untouched.

**Acceptance criteria:**
- `dotnet test` for `backend/test/Anela.Heblo.Tests` passes.
- Frontend test suites (`npm test`) pass.
- No remaining test asserts equality, presence, or absence of `SupplierId` on the list-summary type.

## Non-Functional Requirements

### NFR-1: Performance
No performance change is expected. List-response payloads shrink by one integer per row, which is a marginal improvement and does not require benchmarking.

### NFR-2: Security
No security-sensitive surface is touched. `SupplierId` was never an authorization key in this codebase; removing it does not affect auth or PII exposure.

### NFR-3: API compatibility
This is a **breaking change** to the JSON shape of `GET /api/purchase-orders` (the field disappears from each row). Acceptable here because:
- The field has been a constant `0` since the `ChangeSupplierIdToSupplierName` migration, so no consumer can be relying on a meaningful value.
- Public API consumers are limited to the in-repo frontend (single deployable, regenerated together with the backend).
- The legacy `SupplierId` filter has already been silently ignored; this completes that deprecation.

No versioned API contract or deprecation period is required.

### NFR-4: Code quality
- `dotnet format` and `npm run lint` clean after the change.
- No new TODO/FIXME comments introduced.
- The change is surgical: only the four files listed in "API / Interface Design" are modified (plus the regenerated client and any tests caught by FR-5).

## Data Model
No domain or persistence changes. In particular:

- `PurchaseOrder.SupplierId` (`long`) on the domain entity (`backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrder.cs:9`) is **kept as-is**. It is still used by the domain constructor, `Update` method, EF Core mappings, and migrations; removing it is a larger refactor that is explicitly out of scope (see "Out of Scope").
- No database migration is required.
- No other DTO is modified. `PurchaseOrderDto` and create/update request DTOs (which legitimately carry `supplierId`) are unchanged.

## API / Interface Design

### Endpoint affected
`GET /api/purchase-orders` (handled by `GetPurchaseOrdersHandler`) — response shape change only.

**Before** (per row):
```json
{
  "id": 123,
  "orderNumber": "PO-2026-0042",
  "supplierId": 0,
  "supplierName": "Acme Supplies",
  "...": "..."
}
```

**After** (per row): same as before with the `supplierId` key omitted.

### Files modified
1. `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/PurchaseOrderSummaryDto.cs` — remove the `SupplierId` property (FR-1).
2. `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrders/GetPurchaseOrdersHandler.cs` — remove the `SupplierId = 0,` initializer line and the inline comment (FR-2).
3. `frontend/src/api/generated/api-client.ts` — regenerated; the summary class loses its `supplierId` field, reader, and writer (FR-3).
4. Any test files that assert on the summary DTO's `SupplierId` (FR-5).

### Files explicitly **not** modified
- `backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrder.cs`
- All Persistence migrations
- Any other DTO carrying a legitimate `SupplierId`/`supplierId`

## Dependencies
- The OpenAPI → TypeScript client generation pipeline (auto-runs on backend build per `docs/development/api-client-generation.md`) must regenerate `frontend/src/api/generated/api-client.ts` cleanly.
- No external services, libraries, or feature flags involved.
- No coordination with other in-flight branches required (single-file scope on the backend).

## Out of Scope
- **Removing `SupplierId` from the domain entity `PurchaseOrder`.** It is still wired into the constructor, `Update`, EF Core property map, and database schema. Cleaning it up requires a domain refactor + migration and belongs in a separate feature.
- **Changing `PurchaseOrderDto` (the detail DTO) or any create/update request DTO.** Those `supplierId` fields are independent and still used (or pending their own review).
- **Adjusting the `?SupplierId=` query-string filter behavior.** It is already silently ignored and intentionally covered by an existing test.
- **Reintroducing a meaningful supplier identifier on the summary.** If a future need arises, it should be a fresh DTO field (probably `long`) added with consumer-side wiring — not a revival of the dead `int SupplierId`.
- **Repointing `SupplierName` to a normalized supplier entity.** Not requested.

## Open Questions
None.

## Status: COMPLETE