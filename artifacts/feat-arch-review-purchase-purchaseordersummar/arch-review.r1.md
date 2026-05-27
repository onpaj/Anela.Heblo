# Architecture Review: Remove dead `SupplierId` field from `PurchaseOrderSummaryDto`

## Skip Design: true

This is a backend-only cleanup with a regenerated TypeScript client — no UI components, screens, or visual design decisions are introduced. The frontend list view never read the dead field, so no rendering changes are needed.

## Architectural Fit Assessment

The change fits cleanly into the existing **Clean Architecture + Vertical Slice** layout. `PurchaseOrderSummaryDto` lives in the canonical `Application/Features/Purchase/Contracts/` folder (per `docs/development/api-client-generation.md` §"Best Practices for DTOs"), is a class (not a record — correct per the project's NSwag rule), and is only produced by `GetPurchaseOrdersHandler`. The removal is purely subtractive: no new interfaces, no boundary changes, no MediatR pipeline impact.

**Integration points touched:**
- **Contract**: `PurchaseOrderSummaryDto` (one property removed).
- **Handler projection**: `GetPurchaseOrdersHandler.Handle` object initializer (one line removed).
- **OpenAPI surface**: schema for `PurchaseOrderSummaryDto` shrinks; generated `api-client.ts` regenerates with the field, reader, and writer gone.
- **Test surface**: `GetPurchaseOrdersHandlerTests` and `PurchaseOrdersControllerTests` were verified — neither asserts on the summary's `SupplierId`. The legacy query-string test (`GetPurchaseOrders_WithLegacySupplierIdQueryString_IsSilentlyIgnored`) is unaffected.

**Untouched, by design:**
- Domain entity `PurchaseOrder.SupplierId` (`long`) — still wired through constructor, `Update`, EF Core mapping, and migrations. Out of scope per spec.
- `CreatePurchaseOrderRequest.supplierId`, `UpdatePurchaseOrderRequest.SupplierId`, and the detail DTO consumed by `PurchaseOrderHelpers.tsx` (lines 92, 120, 156 — all operate on create/update payloads or the detail-DTO supplier selector, not list rows).

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────┐
│  React list view             │
│  (purchase-orders list page) │  reads supplierName only
└──────────────┬───────────────┘
               │ TanStack Query
               ▼
┌──────────────────────────────┐
│ api-client.ts (NSwag)        │
│  PurchaseOrderSummaryDto     │  ◄── regenerated, supplierId removed
└──────────────┬───────────────┘
               │ HTTP GET /api/purchase-orders
               ▼
┌──────────────────────────────┐
│ PurchaseOrdersController     │
└──────────────┬───────────────┘
               │ MediatR
               ▼
┌──────────────────────────────┐
│ GetPurchaseOrdersHandler     │  ◄── drop `SupplierId = 0,` from initializer
└──────────────┬───────────────┘
               │ projects to
               ▼
┌──────────────────────────────┐
│ PurchaseOrderSummaryDto      │  ◄── delete `int SupplierId`
│ (Application/.../Contracts)  │
└──────────────────────────────┘

  ╔══════════════════════════════════════════════╗
  ║ UNTOUCHED                                    ║
  ║  Domain.PurchaseOrder.SupplierId (long)      ║
  ║  CreatePurchaseOrderRequest.SupplierId       ║
  ║  UpdatePurchaseOrderRequest.SupplierId       ║
  ║  PurchaseOrderDto (detail) SupplierId        ║
  ║  EF Core mapping + migrations                ║
  ║  Legacy ?SupplierId=… query (already ignored)║
  ╚══════════════════════════════════════════════╝
```

### Key Design Decisions

#### Decision 1: Remove vs. populate the field
**Options considered:**
1. Populate from entity: `SupplierId = (int)order.SupplierId` and fix the type to `long`.
2. Remove the field outright.

**Chosen approach:** Remove.

**Rationale:** The migration `20250802173830_ChangeSupplierIdToSupplierName` made supplier identity a free-text `SupplierName` for purchase orders. The domain `SupplierId` is retained only for legacy/persistence reasons (a separate cleanup). No frontend consumer reads the field. Reviving it would re-couple a list-row DTO to a deprecated identifier and re-expose the `int` vs `long` mismatch. Removal eliminates dead data, a misleading constant `0`, and a stale comment in one pass.

#### Decision 2: Single-edit on the backend, regenerate everything else
**Options considered:**
1. Hand-edit `api-client.ts` to drop the three `supplierId` references in the summary class.
2. Drop the BE property and let the NSwag pipeline regenerate the TS client (per `docs/development/api-client-generation.md`).

**Chosen approach:** Regenerate.

**Rationale:** `api-client.ts` is auto-generated (Debug-mode PostBuild + `npm run prebuild`). Hand-edits would be overwritten on the next build and would also bypass schema verification. Regeneration is the project's standard workflow.

#### Decision 3: Accept the breaking JSON change without a deprecation window
**Options considered:**
1. Keep the field for one release marked `[Obsolete]`, then remove.
2. Remove immediately.

**Chosen approach:** Remove immediately.

**Rationale:** Single-deployable monolith, only consumer is the in-repo frontend regenerated against the same build (per CLAUDE.md "Project facts" — single Docker image, OpenAPI client auto-generated on build). The field has been a constant `0` since the supplier-name migration, so no consumer could rely on a meaningful value. NFR-3 in the spec explicitly accepts this.

#### Decision 4: Leave domain `PurchaseOrder.SupplierId` alone
**Options considered:**
1. Combine this cleanup with a domain-layer removal of `SupplierId`.
2. Strictly limit scope to the DTO surface.

**Chosen approach:** Strict scope.

**Rationale:** The domain field touches the entity constructor, `Update` method, EF Core property map, and an existing migration (`20250901111349_AddSupplierIdToPurchaseOrder`). Removing it requires a migration + cross-cutting refactor and is correctly listed as Out of Scope. Bundling would inflate blast radius without a corresponding consumer benefit on this branch.

## Implementation Guidance

### Directory / Module Structure
No new files. Edits confined to:

| Path | Change |
|------|--------|
| `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/PurchaseOrderSummaryDto.cs` | Delete `public int SupplierId { get; set; }` (line 9). |
| `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrders/GetPurchaseOrdersHandler.cs` | Delete the `SupplierId = 0, // No longer using SupplierId` initializer line (line 44). |
| `frontend/src/api/generated/api-client.ts` | **Regenerated only** — do not hand-edit. Expect three changes: field declaration at ~L32792, reader at ~L32818, writer at ~L32844, plus the matching `IPurchaseOrderSummaryDto` interface member at ~L32863. No edits to `CreatePurchaseOrderRequest` (~L33119) or other DTOs. |

Existing handler tests (`GetPurchaseOrdersHandlerTests.cs`) and controller tests (`PurchaseOrdersControllerTests.cs`) **do not assert on the summary's `SupplierId`** — verified by grep. No test updates are anticipated; FR-5 is a guard, not a known-to-fail item.

### Interfaces and Contracts

**`PurchaseOrderSummaryDto` after change (canonical contract):**
```csharp
public class PurchaseOrderSummaryDto
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = null!;
    public string SupplierName { get; set; } = null!;
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDeliveryDate { get; set; }
    public ContactVia? ContactVia { get; set; }
    public string Status { get; set; } = null!;
    public bool InvoiceAcquired { get; set; }
    public decimal TotalAmount { get; set; }
    public int LineCount { get; set; }
    public bool IsEditable { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
}
```

**OpenAPI schema delta:** one property removed from the `PurchaseOrderSummaryDto` schema component. The endpoint signature for `GET /api/purchase-orders` is otherwise stable.

### Data Flow

For the only affected use case — listing purchase orders:

1. `PurchaseOrdersController` receives `GET /api/purchase-orders` with the existing filters; the legacy `?SupplierId=` parameter remains silently ignored (no model-binding change since it's not a property).
2. MediatR dispatches `GetPurchaseOrdersRequest` to `GetPurchaseOrdersHandler`.
3. `IPurchaseOrderRepository.GetPaginatedAsync` returns domain `PurchaseOrder` objects (still carrying their internal `SupplierId`).
4. The handler projects each `PurchaseOrder` to `PurchaseOrderSummaryDto`, now **without** writing `SupplierId`. Domain `SupplierId` is simply not read into the DTO.
5. The response serializes via System.Text.Json — JSON output drops the `"supplierId": 0` key per row.
6. The frontend deserializes via the regenerated `PurchaseOrderSummaryDto.init`; `supplierId` is no longer assigned. No list-page code reads it (verified — only `PurchaseOrderHelpers.tsx` references `supplierId`, and only against create/update payloads and the detail DTO).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Regenerated `api-client.ts` accidentally drops `supplierId` from non-summary types (`CreatePurchaseOrderRequest`, `UpdatePurchaseOrderRequest`, `PurchaseOrderDto`, supplier types). | High | After regen, diff `frontend/src/api/generated/api-client.ts` and confirm changes are localized to the `PurchaseOrderSummaryDto` class (~L32789) and `IPurchaseOrderSummaryDto` interface (~L32860). Run `npm run build` — `PurchaseOrderHelpers.tsx` lines 92/120/156 will fail to compile if `CreatePurchaseOrderRequest.supplierId` was incorrectly removed. |
| Client file not regenerated locally before commit (Debug-only PostBuild). | Medium | Run `dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual` (or `cd frontend && npm run generate-client`) explicitly before validating. CI compiles `npm run build` which runs the prebuild step. |
| An unanticipated frontend reader of `summary.supplierId` (e.g., column in a list grid we didn't enumerate). | Low | After regen, run `npm run build` + `npm run lint`. Because `supplierId?` was optional on the summary type and TypeScript will simply mark the property as `undefined`, the typechecker would flag any usage on a `PurchaseOrderSummaryDto` value. If found, treat as Spec amendment per FR-4. |
| Diff size of `api-client.ts` (massive generated file) makes reviewer fatigue likely. | Low | Keep the regenerated change in a single commit separate from the BE source edits, or call out the affected line ranges in the PR description: `PurchaseOrderSummaryDto` class declaration, `init`, `toJSON`, and `IPurchaseOrderSummaryDto` interface. |
| Stale CI snapshots of OpenAPI spec or contract tests. | Low | None present in this repo (no OpenAPI contract tests). `dotnet build` + `dotnet test` + `npm run build` are sufficient gates. |

## Specification Amendments

None. The spec is precise, scope is correctly bounded, and acceptance criteria are individually verifiable. Two refinements for the implementer (clarifications, not changes):

- **Validation order matters.** Backend edits (FR-1, FR-2) must precede the TS client regen (FR-3). The regen reads the live OpenAPI schema from `Anela.Heblo.API` after build — regenerating before the BE change re-emits the dead field.
- **FR-4 expected outcome is "no diff."** Concrete verification command:
  ```
  rg -n "supplierId" frontend/src \
    --glob '!frontend/src/api/generated/api-client.ts' \
    --glob '!frontend/src/components/purchase-orders/form/PurchaseOrderHelpers.tsx'
  ```
  Result should be empty. Hits in `PurchaseOrderHelpers.tsx` (lines 92, 120, 156) are expected and operate on create/update/detail DTOs, not the summary.

## Prerequisites

- Working `dotnet` SDK 8 and NSwag tool (`dotnet tool restore`) for `Anela.Heblo.API`.
- Working frontend toolchain (`npm install` in `frontend/`).
- No database migration, configuration change, feature flag, or infrastructure prerequisite.
- No coordination with other open branches: a `git log --oneline -- backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/PurchaseOrderSummaryDto.cs backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrders/GetPurchaseOrdersHandler.cs` check before pushing is sufficient.

**Validation sequence before declaring done** (per CLAUDE.md):
1. `dotnet build` (succeeds, no analyzer warnings).
2. `dotnet format` (no changes).
3. `dotnet test backend/test/Anela.Heblo.Tests` (all green; especially `GetPurchaseOrders_WithLegacySupplierIdQueryString_IsSilentlyIgnored`).
4. `dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual`.
5. Inspect `api-client.ts` diff is scoped to `PurchaseOrderSummaryDto` / `IPurchaseOrderSummaryDto`.
6. `npm run build` + `npm run lint` (clean).
7. `npm test` (clean).