# Specification: Decouple IReportBuilderService from UseCase Response Types

## Summary
Refactor `IReportBuilderService` so it no longer returns types nested inside specific use-case response classes. Introduce dedicated contract DTOs under `Features/Analytics/Contracts/` and have use-case handlers project from these contract types to their own response shapes. This restores the correct dependency direction (UseCases → Services → Contracts) and unblocks reuse of the report-building logic by future use cases.

## Background
`backend/src/Anela.Heblo.Application/Features/Analytics/Services/ReportBuilderService.cs` currently declares an interface whose method signatures reference nested types from two specific use cases:

- `GetProductMarginAnalysisResponse.MonthlyMarginBreakdown` (from `UseCases/GetProductMarginAnalysis/`)
- `GetMarginReportResponse.CategoryMarginSummary` (from `UseCases/GetMarginReport/`)
- `GetMarginReportResponse.ProductMarginSummary` (from `UseCases/GetMarginReport/`)

Per `docs/architecture/filesystem.md` and `docs/architecture/development_guidelines.md`:
- `Services/` houses domain services and shared business logic.
- `UseCases/` houses per-request handlers with request/response shapes scoped to that use case.
- `Contracts/` is the home for DTOs shared across use cases (e.g., `AnalysisMarginData`, `TopProductDto`, `MonthlyProductMarginDto`).

The current shape violates this layering by making a shared service depend on use-case-specific types. Consequences:

1. **Inverted dependencies** — `Services/` references `UseCases/` types at compile time, breaking the inward-pointing dependency rule.
2. **Semantic mismatch for new callers** — Any future use case (e.g., a margin export, scheduled job, dashboard widget) needing the same calculation must accept a type named after `GetProductMarginAnalysisResponse`, which is misleading.
3. **Fragility** — Renaming or restructuring either response class forces a change to the shared service interface and implementation.
4. **OCP violation** — The service is closed to extension for new callers needing the same computations with different response shapes.

This finding was raised by the daily architecture-review routine on 2026-05-27.

## Functional Requirements

### FR-1: New shared DTOs in the Contracts layer
Introduce three new DTOs under `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/`:

- `MonthlyMarginBreakdownDto.cs`
- `CategoryMarginSummaryDto.cs`
- `ProductMarginSummaryDto.cs`

Each DTO must mirror the **public surface** of the existing nested type it replaces (same property names, types, and defaults) so consumer-side projection is field-for-field.

The DTOs must be `class` (not `record`), per the project rule that DTOs visible to the OpenAPI surface must be classes. Even though these contracts are server-internal today, keeping them as classes preserves the option to expose them later without breaking the OpenAPI generator.

**Acceptance criteria:**
- Files exist at the paths above.
- Each DTO has the exact properties (name, type, default initializer) of the corresponding nested class.
- DTOs are declared as `public class` with public get/set properties.
- DTOs have no behavior beyond auto-properties.
- No `using` statements that import from `Features/Analytics/UseCases/`.

### FR-2: Updated IReportBuilderService interface
Change `IReportBuilderService` so all methods return the new contract DTOs:

```csharp
public interface IReportBuilderService
{
    List<MonthlyMarginBreakdownDto> BuildMonthlyBreakdown(
        List<SalesDataPoint> salesData,
        AnalyticsProduct productData,
        DateTime startDate,
        DateTime endDate);

    List<CategoryMarginSummaryDto> BuildCategorySummaries(
        Dictionary<string, CategoryData> categoryTotals);

    ProductMarginSummaryDto BuildProductSummary(
        AnalyticsProduct product,
        AnalysisMarginData marginData);
}
```

**Acceptance criteria:**
- `IReportBuilderService` no longer references any type defined under `Features/Analytics/UseCases/`.
- Method names, parameter lists, and parameter order are unchanged.
- The interface compiles without using directives pointing to `UseCases/`.

### FR-3: Updated ReportBuilderService implementation
Update the concrete `ReportBuilderService` class to:

- Return the new contract DTOs.
- Preserve all current calculation logic byte-for-byte (no behavioral changes to formulas, rounding, ordering, grouping, or filtering).

**Acceptance criteria:**
- All existing unit tests for `ReportBuilderService` continue to pass after updating only the test assertions' return-type names (no logic changes in tests beyond the type swap).
- No call to `new GetProductMarginAnalysisResponse.MonthlyMarginBreakdown(...)` etc. remains inside the service.
- The service file contains no `using ...UseCases.GetProductMarginAnalysis` or `using ...UseCases.GetMarginReport` directives.

### FR-4: Use-case handlers map contract DTOs to response types
Each use-case handler that consumes `IReportBuilderService` must perform a local projection from the contract DTO to its own response's nested type:

- `GetProductMarginAnalysisHandler` (or equivalent) maps `MonthlyMarginBreakdownDto` → `GetProductMarginAnalysisResponse.MonthlyMarginBreakdown`.
- `GetMarginReportHandler` (or equivalent) maps `CategoryMarginSummaryDto` → `GetMarginReportResponse.CategoryMarginSummary` and `ProductMarginSummaryDto` → `GetMarginReportResponse.ProductMarginSummary`.

The projection should be inline (LINQ `Select` or a private static mapping helper inside the handler file). Do **not** introduce a global mapper, AutoMapper profile, or shared mapping utility for this — projections are one-to-one and trivial.

**Acceptance criteria:**
- Each affected handler builds its response by projecting contract DTOs to nested response types.
- Projection code is contained within the handler file or a private static method in the same file.
- No new mapping library, profile, or shared mapper class is introduced.
- The existing **response wire shape** (JSON shape returned to the client) is byte-for-byte identical to today.

### FR-5: Preserve nested response types
The nested types `GetProductMarginAnalysisResponse.MonthlyMarginBreakdown`, `GetMarginReportResponse.CategoryMarginSummary`, and `GetMarginReportResponse.ProductMarginSummary` must remain in place, unchanged. They are part of the use-case response contract and the OpenAPI-generated TypeScript client depends on them.

**Acceptance criteria:**
- The three nested classes exist with identical property signatures after the refactor.
- The OpenAPI spec (`swagger.json`) emitted by the build is unchanged for the affected endpoints (verify by diff).
- The auto-generated TypeScript client (`frontend/src/api/`) requires no code changes after regeneration.

## Non-Functional Requirements

### NFR-1: Performance
- The added projection step is O(n) over the result list with no allocations beyond the new DTO instances themselves. No measurable degradation expected for margin endpoints (≤ a few hundred items per response in practice). No new performance budget — endpoint latency must remain within current observed range (±5%).

### NFR-2: Backward compatibility
- HTTP response payloads for `GET` margin/margin-analysis endpoints must be byte-identical after the refactor (same field names, ordering, types, null handling).
- No database schema changes.
- No changes to the OpenAPI schema.

### NFR-3: Code quality
- `dotnet build` succeeds with zero warnings introduced by this change.
- `dotnet format` produces no diff.
- Existing analyzer rules (nullable, naming, etc.) are satisfied.

### NFR-4: Test coverage
- Existing unit tests for `ReportBuilderService` and the affected handlers continue to pass after updating type references.
- No reduction in coverage percentage on touched files.

## Data Model

No persistence changes. New in-memory DTOs only:

| Type | Location | Purpose |
|---|---|---|
| `MonthlyMarginBreakdownDto` | `Features/Analytics/Contracts/` | Output shape for `BuildMonthlyBreakdown` |
| `CategoryMarginSummaryDto` | `Features/Analytics/Contracts/` | Output shape for `BuildCategorySummaries` |
| `ProductMarginSummaryDto` | `Features/Analytics/Contracts/` | Output shape for `BuildProductSummary` |

Property sets mirror the existing nested classes one-for-one. Authoritative property definitions are the current nested types in `GetProductMarginAnalysisResponse` and `GetMarginReportResponse`; the new DTOs must be transcribed from those.

## API / Interface Design

**Internal interface change** (no public HTTP API change):

Before:
```csharp
List<GetProductMarginAnalysisResponse.MonthlyMarginBreakdown> BuildMonthlyBreakdown(...);
List<GetMarginReportResponse.CategoryMarginSummary>           BuildCategorySummaries(...);
GetMarginReportResponse.ProductMarginSummary                   BuildProductSummary(...);
```

After:
```csharp
List<MonthlyMarginBreakdownDto> BuildMonthlyBreakdown(...);
List<CategoryMarginSummaryDto>  BuildCategorySummaries(...);
ProductMarginSummaryDto         BuildProductSummary(...);
```

**HTTP / OpenAPI surface:** unchanged. Use-case responses still expose the same nested types under the same paths and the regenerated TypeScript client is bit-identical.

**Dependency direction after refactor:**
```
UseCases  ──depends on──▶  Services  ──depends on──▶  Contracts
   │                                                      ▲
   └───────────────depends on (response DTOs)─────────────┘
```

## Dependencies
- Existing types under `Features/Analytics/Contracts/` (e.g., `AnalysisMarginData`, `SalesDataPoint`, `CategoryData`, `AnalyticsProduct`) — used as inputs to the interface; unchanged.
- The OpenAPI client generation pipeline — must be re-run after the refactor to confirm no diff.
- Unit test suite for `ReportBuilderService` and the affected margin use-case handlers.

No new NuGet packages, no DI registration changes (the existing `IReportBuilderService → ReportBuilderService` registration is untouched).

## Out of Scope
- Renaming or reshaping the `MonthlyMarginBreakdown`, `CategoryMarginSummary`, or `ProductMarginSummary` nested classes in the use-case responses.
- Changing any HTTP endpoint shape, URL, verb, status code, or auth requirement.
- Introducing AutoMapper, Mapster, or any other mapping framework.
- Consolidating `ProductMarginSummaryDto` with the existing `TopProductDto` (they may have overlapping fields, but unifying them is a separate concern; see Open Questions).
- Adding new use cases that consume `IReportBuilderService`.
- Refactoring any other service in `Features/Analytics/Services/` that may have similar layering issues.
- Frontend changes (none should be required; verify by regenerating the TS client).
- Database migrations.

## Open Questions
None.

## Status: COMPLETE