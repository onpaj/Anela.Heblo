# Specification: Extract `MarginLevelDto` Construction Duplication in Catalog

## Summary
`MarginLevelDto { Percentage, Amount, CostLevel, CostTotal }` is constructed inline, field-by-field, 12 times across `GetProductMarginsHandler.cs` and `GetCatalogDetailHandler.cs` — always from a `MarginLevel` domain value with no transformation. This is a pure internal refactor: introduce a single mapping factory on the DTO and replace all 12 call sites with it. No behavior, API contract, or response payload changes.

## Background
`GetProductMarginsHandler.MapToMarginDto` builds M0–M3 averages (4 occurrences) and a monthly-history projection (4 occurrences) this way. `GetCatalogDetailHandler.GetMarginHistoryFromMargins` builds M0–M3 for each monthly margin-history row (4 occurrences). Both handlers already depend on the shared `MarginLevelDto` contract in `Catalog/Contracts/`, and both map from the same domain type, `Anela.Heblo.Domain.Features.Catalog.MarginLevel` (the per-level value inside `MarginData.M0..M3`). Any future field added to `MarginLevelDto` (or renamed on `MarginLevel`) currently requires touching 12 near-identical blocks in two files — this was flagged by the daily arch-review routine as a duplication/maintainability risk.

## Functional Requirements

### FR-1: Add a mapping factory to `MarginLevelDto`
Add a static factory method on `MarginLevelDto` (in `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/MarginLevelDto.cs`) that maps one `MarginLevel` domain value to one `MarginLevelDto`, copying all four fields (`Percentage`, `Amount`, `CostLevel`, `CostTotal`) with no transformation.

**Acceptance criteria:**
- The factory method is `public static` on `MarginLevelDto`.
- The factory takes a single `MarginLevel` parameter (the actual domain type at every call site — not the coarser `MarginData` type named in the originating issue's suggested-fix snippet; see NFR-3 / Open Questions).
- The factory returns a new `MarginLevelDto` with `Percentage`, `Amount`, `CostLevel`, `CostTotal` copied verbatim from the input.
- `MarginLevelDto` remains a plain class (per project DTO rules — no records), and the new method does not change its existing public shape (properties, `JsonPropertyName` attributes) in any way.

### FR-2: Replace all inline constructions with the factory
Replace every inline `new MarginLevelDto { Percentage = ..., Amount = ..., CostLevel = ..., CostTotal = ... }` block that copies straight from a `MarginLevel` value with a call to the new factory.

**Acceptance criteria:**
- All 4 inline constructions in `GetProductMarginsHandler.MapToMarginDto`'s M0–M3 averages block are replaced (`dto.M0`, `dto.M1`, `dto.M2`, `dto.M3` assignments).
- All 4 inline constructions in `GetProductMarginsHandler.MapToMarginDto`'s `MonthlyHistory` projection (inside the `Select(m => new MonthlyMarginDto { ... M0 = ..., M1 = ..., M2 = ..., M3 = ... })` lambda) are replaced.
- All 4 inline constructions in `GetCatalogDetailHandler.GetMarginHistoryFromMargins`'s `Select(m => new MarginHistoryDto { ... M0 = ..., M1 = ..., M2 = ..., M3 = ... })` lambda are replaced.
- Total: 12 call sites replaced, 0 remaining inline `MarginLevelDto` constructions of this shape in either file.
- No other code in either handler is touched (sorting, filtering, pagination, error handling, logging, `ManufactureCostDto`/other DTO mappings, etc. are left exactly as-is).

### FR-3: Preserve behavior exactly
This is a structural refactor only.

**Acceptance criteria:**
- For any given `MarginLevel` input, `MarginLevelDto.From(level)` (or whatever the factory is named) produces field-for-field identical output to the inline construction it replaces.
- All existing tests covering `GetProductMarginsHandler` and `GetCatalogDetailHandler` (and any margin-related response/DTO assertions) continue to pass unmodified.
- The JSON shape of `GetProductMarginsResponse` and `GetCatalogDetailResponse` is byte-for-byte unchanged for the same inputs.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact expected or required — this replaces object-initializer syntax with an equivalent static factory call; no additional allocations, loops, or I/O are introduced.

### NFR-2: Security
Not applicable. No new inputs, no new data exposure, no auth-relevant code paths touched.

### NFR-3: Correctness of the mapped source type
The issue's suggested-fix snippet names the factory parameter type `MarginData`. Codebase inspection (`backend/src/Anela.Heblo.Domain/Features/Catalog/MarginData.cs` and `MarginLevel.cs`) shows this is imprecise: `MarginData` is the *cascade container* (`M0..M3`, each a `MarginLevel`); the type actually passed at every one of the 12 call sites (`marginHistory.Averages.M0`, `m.Value.M1`, etc.) is `MarginLevel`, the per-level value type with the four scalar fields. The factory must be typed against `MarginLevel`, not `MarginData` — implementers must not copy the issue snippet's type name verbatim. See architecture review for the authoritative decision.

## Data Model
No data model changes. Existing types involved:
- `Anela.Heblo.Domain.Features.Catalog.MarginLevel` (domain value; source of the mapping) — `Percentage`, `Amount`, `CostTotal`, `CostLevel`, all `decimal`.
- `Anela.Heblo.Domain.Features.Catalog.MarginData` (domain cascade container; unchanged) — holds `M0..M3` as `MarginLevel`.
- `Anela.Heblo.Application.Features.Catalog.Contracts.MarginLevelDto` (application DTO; gains one static factory method, no property changes) — `Percentage`, `Amount`, `CostLevel`, `CostTotal`, all `decimal`, each with an existing `[JsonPropertyName]`.

## API / Interface Design
No public API, controller, endpoint, or route changes. No OpenAPI/TypeScript client regeneration is triggered — the DTO's public shape (properties and JSON attributes) is unchanged, only an internal static factory method is added.

## Dependencies
None. Self-contained to:
- `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/MarginLevelDto.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs`

## Out of Scope
- Renaming, restructuring, or adding fields to `MarginLevelDto`, `MarginLevel`, or `MarginData`.
- Touching the other DTO constructions in these handlers that are *not* the `MarginLevelDto` pattern (e.g. `ManufactureCostDto`, `MarginHistoryDto`'s own top-level fields, `MonthlyMarginDto`'s `Month` field, `CatalogItemDto`/`LotDto` mapping, `AutoMapper` usage in `GetCatalogDetailHandler`).
- Any AutoMapper profile changes — the existing inline/manual mapping style for `MarginLevelDto` is kept (just centralized), not converted to an AutoMapper mapping.
- Any frontend changes (no frontend code references this construction pattern; it is backend-only).
- Any change to the daily arch-review routine or issue-filing process that produced the originating finding.

## Open Questions
None. The one ambiguity in the source issue (factory parameter type named `MarginData` in the suggested-fix snippet) is resolved by direct codebase inspection in NFR-3: the correct type is `MarginLevel`, confirmed against every one of the 12 call sites and against `MarginData.cs`/`MarginLevel.cs`.

## Status: COMPLETE
