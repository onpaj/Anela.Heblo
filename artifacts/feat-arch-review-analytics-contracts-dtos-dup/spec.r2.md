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

The cost is concrete: every new field added to the business logic (e.g. a new margin level) must be added in two places and the mapping updated. The brief notes that `ProductMarginSummaryDto` has a `MarginPercentage` field while `MonthlyMarginBreakdownDto` does not — an illustration of how drift accumulates across the Analytics module's parallel-type structure. (This is a cross-DTO observation, not a within-pair divergence; the three pairs covered by this spec are each currently in sync field-for-field.)

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
Collapse `MonthlyMarginBreakdownDto` (`Contracts/MonthlyMarginBreakdownDto.cs`) and `GetProductMarginAnalysisResponse.MonthlyMarginBreakdown` (`GetProductMarginAnalysisResponse.cs:18–25`).

**Direction:** Keep `MonthlyMarginBreakdownDto`. Remove the nested class. Change the response's monthly-breakdown collection to use `List<MonthlyMarginBreakdownDto>`.

The two types are already field-for-field identical (`Month`, `MarginAmount`, `Revenue`, `Cost`, `UnitsSold`) — no field reconciliation required. **Do not add a `MarginPercentage` field**; the nested class does not currently emit one, and adding it would itself be an observable wire-shape change (violating FR-5). Consumers needing a monthly margin percentage can derive it from `MarginAmount / Revenue`, consistent with the existing `AverageMarginPercentage` computation at `GetMarginReportHandler.cs:144–146`.

**Acceptance criteria:**
- `GetProductMarginAnalysisResponse.MonthlyMarginBreakdown` nested class is deleted.
- The service producing the monthly breakdown returns `MonthlyMarginBreakdownDto` directly.
- `GetProductMarginAnalysisHandler.cs:93–102` mapping is collapsed to a direct assignment.
- Unified `MonthlyMarginBreakdownDto` has exactly: `Month`, `MarginAmount`, `Revenue`, `Cost`, `UnitsSold`. No new fields added.
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
Eliminating the duplication is the primary goal. After the change, adding a new field (e.g. a new margin level) requires editing one type and zero mapping code. The drift risk inherent to parallel-type structures is removed for the three affected DTOs.

### NFR-4: Build & test gates
- `dotnet build` and `dotnet format` must succeed.
- All existing backend tests in the Analytics module pass without modification (other than any test that explicitly references the deleted nested-class type names — those should be updated to the unified type).
- Frontend `npm run build` and `npm run lint` succeed after client regeneration.

## Data Model

The three unified types retain all existing fields. After consolidation:

- **`ProductMarginSummaryDto`** (`Contracts/ProductMarginSummaryDto.cs`) — the union of fields previously on `ProductMarginSummaryDto` and `GetMarginReportResponse.ProductMarginSummary`. Verify field-for-field equivalence during implementation; if a field exists on only one side, include it on the unified type (default: no behaviour loss).
- **`CategoryMarginSummaryDto`** (`Contracts/CategoryMarginSummaryDto.cs`) — same approach.
- **`MonthlyMarginBreakdownDto`** (`Contracts/MonthlyMarginBreakdownDto.cs`) — fields are already identical between the two sides: `Month`, `MarginAmount`, `Revenue`, `Cost`, `UnitsSold`. No reconciliation needed.

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
- **Renaming fields, reshaping types, or adding new fields** — no field-level changes. Specifically, no `MarginPercentage` added to `MonthlyMarginBreakdownDto` (separate product decision if ever desired).
- **Removing the `Dto` suffix or relocating types** — the Contracts/ folder layout stays as-is.
- **Switching to records** — disallowed by project rule; stays as classes.
- **Updating documentation pages beyond what regenerated artifacts require.**

## Open Questions

None.

## Status: COMPLETE