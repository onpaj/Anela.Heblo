# Specification: Consolidate Duplicate Analytics DTOs

## Summary
The Analytics module defines three pairs of structurally identical types — one in `Contracts/`, one as a nested class on the corresponding response — and every handler copies field-for-field between them. This spec removes the duplication by collapsing each pair to a single type, eliminating the verbatim `Select(...new...)` mapping in the handlers and reducing the maintenance surface for future field additions.

## Background

The daily arch-review routine identified three duplicate DTO pairs in the Analytics module (filed 2026-06-03):

| Contracts/ type | Response nested class | Mapping location |
|---|---|---|
| `ProductMarginSummaryDto` | `GetMarginReportResponse.ProductMarginSummary` | `GetMarginReportHandler.cs:161–179` |
| `CategoryMarginSummaryDto` | `GetMarginReportResponse.CategoryMarginSummary` | `GetMarginReportHandler.cs:181–191` |
| `MonthlyMarginBreakdownDto` | `GetProductMarginAnalysisResponse.MonthlyMarginBreakdown` | `GetProductMarginAnalysisHandler.cs:93–102` |

In each case the handler performs a verbatim property-copy with no transformation, filtering, or shaping. The pair adds no abstraction value — it is redundant indirection.

Concrete cost already observable: `MonthlyMarginBreakdownDto` is missing the `MarginPercentage` field that exists on the response-nested counterpart. This divergence was introduced silently because the two types are physically separate. Any new margin-level field added in business logic must be added in two places and the mapping updated — the duplication actively invites drift.

The project rules (`docs/architecture/development_guidelines.md`) require that all DTOs exposed over OpenAPI are classes (never records) — both sides of each pair already comply, so collapsing them is a structural change only, not a typing change.

## Functional Requirements

### FR-1: Unify ProductMarginSummary
Collapse `ProductMarginSummaryDto` (`Contracts/ProductMarginSummaryDto.cs`) and `GetMarginReportResponse.ProductMarginSummary` (`GetMarginReportResponse.cs:19–43`) into a single type.

**Direction:** Keep the `Contracts/` DTO (`ProductMarginSummaryDto`) as the canonical type. Remove the nested `ProductMarginSummary` class from `GetMarginReportResponse`. Change `GetMarginReportResponse.ProductSummaries` to be `List<ProductMarginSummaryDto>`.

**Rationale for direction:** The `Contracts/` namespace is the project's public contract surface; the `Dto` suffix already signals public consumption; keeping it preserves a stable, descriptively-named type for OpenAPI consumers and any other handlers that might emit margin data in the future.

**Acceptance criteria:**
- `GetMarginReportResponse.ProductMarginSummary` nested class is deleted.
- `IReportBuilderService.BuildProductSummary` (or whichever service produces these) returns `ProductMarginSummaryDto` (or `IEnumerable<ProductMarginSummaryDto>`) directly.
- `GetMarginReportHandler.cs:161–179` no longer contains a `Select(dto => new ProductMarginSummary { ... })` mapping; instead assigns the service result directly.
- All fields previously present on either side are present on the unified type (no field loss).
- `dotnet build` succeeds.
- All Analytics module tests pass.

### FR-2: Unify CategoryMarginSummary
Collapse `CategoryMarginSummaryDto` (`Contracts/CategoryMarginSummaryDto.cs`) and `GetMarginReportResponse.CategoryMarginSummary` (`GetMarginReportResponse.cs:45–52`).

**Direction:** Keep `CategoryMarginSummaryDto`. Remove the nested class. Change `GetMarginReportResponse.CategorySummaries` to `List<CategoryMarginSummaryDto>`.

**Acceptance criteria:**
- `GetMarginReportResponse.CategoryMarginSummary` nested class is deleted.
- `IReportBuilderService.BuildCategorySummaries` returns `IEnumerable<CategoryMarginSummaryDto>` (or `List<>`) directly.
- `GetMarginReportHandler.cs:181–191` mapping is collapsed to a direct assignment.
- All fields preserved.
- `dotnet build` succeeds; Analytics tests pass.

### FR-3: Unify MonthlyMarginBreakdown
Collapse `MonthlyMarginBreakdownDto` (`Contracts/MonthlyMarginBreakdownDto.cs`) and `GetProductMarginAnalysisResponse.MonthlyMarginBreakdown` (`GetProductMarginAnalysisResponse.cs:20–25`).

**Direction:** Keep `MonthlyMarginBreakdownDto`. Remove the nested class. Change the response's monthly-breakdown collection to use `List<MonthlyMarginBreakdownDto>`.

**Acceptance criteria:**
- `GetProductMarginAnalysisResponse.MonthlyMarginBreakdown` nested class is deleted.
- The service producing the monthly breakdown returns `MonthlyMarginBreakdownDto` directly.
- `GetProductMarginAnalysisHandler.cs:93–102` mapping is collapsed to a direct assignment.
- The pre-existing divergence (`MarginPercentage` present on the nested class but absent from the Contracts DTO) is resolved by **including** `MarginPercentage` on the unified `MonthlyMarginBreakdownDto`. See Open Question OQ-1 — assumption is to keep the field because it is currently emitted to the API consumer and removing it would be a behaviour change; the spec defaults to "no behaviour change."
- All fields preserved (modulo OQ-1).
- `dotnet build` succeeds; Analytics tests pass.

### FR-4: Update OpenAPI clients and frontend consumers
Removing the nested response classes is a wire-shape change for OpenAPI: type names referenced by generated clients will change (e.g. the generated TS type `ProductMarginSummary` will disappear; `ProductMarginSummaryDto` will be used in its place). The field structure on the wire stays identical (same JSON shape), but the named type changes.

**Acceptance criteria:**
- Backend OpenAPI spec regenerates cleanly (the build-time generation step succeeds).
- Frontend TypeScript client regenerates without errors.
- All frontend code referencing the old nested-class generated type names is updated to the unified type name (e.g. `ProductMarginSummary` → `ProductMarginSummaryDto`).
- `npm run build` and `npm run lint` in `frontend/` succeed.
- Any frontend tests touching these types pass.

### FR-5: No behavioural change at the API boundary
The HTTP/JSON response payloads must remain byte-equivalent for existing endpoints — same field names, same casing, same types, same nullability, same order-where-applicable.

**Acceptance criteria:**
- A diff of a representative `GetMarginReport` response before and after the change shows zero JSON-payload differences for the same input.
- Same check for `GetProductMarginAnalysis`.
- (If integration/e2e tests exist for these endpoints, they pass unchanged.)

## Non-Functional Requirements

### NFR-1: Performance
Zero impact expected — the change removes an in-process `.Select(...new...)` allocation per item, which is a marginal improvement, not a regression. No additional measurement required.

### NFR-2: Security
None. Pure internal refactor; no auth, validation, or data-sensitivity surface is touched.

### NFR-3: Maintainability
Eliminating the duplication is the primary goal. After the change, adding a new field (e.g. a new margin level) requires editing one type and zero mapping code. The drift risk that produced the existing `MarginPercentage` divergence is removed.

### NFR-4: Build & test gates
- `dotnet build` and `dotnet format` must succeed.
- All existing backend tests in the Analytics module pass without modification (other than any test that explicitly references the deleted nested-class type names — those should be updated to the unified type).
- Frontend `npm run build` and `npm run lint` succeed after client regeneration.

## Data Model

The three unified types retain all existing fields. After consolidation:

- **`ProductMarginSummaryDto`** (`Contracts/ProductMarginSummaryDto.cs`) — the union of fields previously on `ProductMarginSummaryDto` and `GetMarginReportResponse.ProductMarginSummary`. Verify field-for-field equivalence during implementation; if a field exists on only one side, include it on the unified type (default: no behaviour loss).
- **`CategoryMarginSummaryDto`** (`Contracts/CategoryMarginSummaryDto.cs`) — same approach.
- **`MonthlyMarginBreakdownDto`** (`Contracts/MonthlyMarginBreakdownDto.cs`) — same approach. Includes `MarginPercentage` (see OQ-1).

All three remain plain C# classes (not records), per project rule.

No database schema changes. No migration. No persisted-data implications.

## API / Interface Design

### Internal interfaces (services)
The services that today return the Contracts/ DTO and feed the handler that re-projects to the nested class are simplified to return the unified Contracts/ DTO. Concretely:

- `IReportBuilderService.BuildProductSummary(...)` → returns `ProductMarginSummaryDto` (or collection thereof).
- `IReportBuilderService.BuildCategorySummaries(...)` → returns `IEnumerable<CategoryMarginSummaryDto>`.
- Whichever service produces the monthly breakdown for `GetProductMarginAnalysis` → returns `IEnumerable<MonthlyMarginBreakdownDto>`.

(Exact method names and signatures to be verified during implementation against the current code.)

### Response types
- `GetMarginReportResponse.ProductSummaries`: `List<ProductMarginSummaryDto>`.
- `GetMarginReportResponse.CategorySummaries`: `List<CategoryMarginSummaryDto>`.
- `GetProductMarginAnalysisResponse.MonthlyBreakdowns` (or current name): `List<MonthlyMarginBreakdownDto>`.

### External HTTP surface
Unchanged — same routes, same JSON shapes, same status codes.

### OpenAPI / TypeScript client
The generated client will rename the affected types (nested type names disappear; Contracts/ DTO names remain). Frontend code referencing the disappearing names must be updated.

## Dependencies

- **OpenAPI client regeneration** — both the .NET-side OpenAPI spec generation and the frontend TypeScript client generation must run as part of this change. See `docs/development/api-client-generation.md`.
- **Frontend code referencing affected generated types** — must be updated to the new type names. Scope: anywhere the frontend currently imports/uses `ProductMarginSummary`, `CategoryMarginSummary`, or `MonthlyMarginBreakdown` (the nested-class–derived names).
- No external services, no library additions, no new infrastructure.

## Out of Scope

- **Other Analytics DTO duplications** — only the three pairs called out in the brief. If similar patterns exist elsewhere in Analytics, file separately; do not expand scope here.
- **Other modules** — Manufacture, Catalog, Purchase, etc. Not touched.
- **Renaming fields, reshaping types, or adding new fields** — beyond resolving the `MarginPercentage` divergence (OQ-1), no field-level changes.
- **Removing the `Dto` suffix or relocating types** — the Contracts/ folder layout stays as-is.
- **Switching to records** — disallowed by project rule; stays as classes.
- **Updating documentation pages beyond what regenerated artifacts require.**

## Open Questions

### OQ-1: Should the unified `MonthlyMarginBreakdownDto` retain `MarginPercentage`?
The nested response class (`GetProductMarginAnalysisResponse.MonthlyMarginBreakdown`) currently has a `MarginPercentage` field that the Contracts/ `MonthlyMarginBreakdownDto` does not. The spec defaults to **keeping** `MarginPercentage` on the unified type, because:
- It is currently emitted on the wire to API consumers.
- Removing it would be an observable behaviour change for clients.
- The principle of "no behaviour change at the API boundary" (FR-5) requires keeping it.

Confirm with product/business owner that `MarginPercentage` is the intended/correct field for the monthly breakdown and not a one-off mistake to be removed. If confirmed correct, no action needed (default assumption holds). If it should be removed, FR-5 needs amending and any consumer using it needs warning.

## Status: HAS_QUESTIONS