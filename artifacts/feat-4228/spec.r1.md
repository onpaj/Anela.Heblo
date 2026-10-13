# Specification: Remove stale IMarginCalculationService comment from AnalyticsModule

## Summary
`AnalyticsModule.cs` line 32 carries a stale comment claiming `IMarginCalculationService` is "registered by CatalogModule and injected here." No code in the Analytics module injects or references `IMarginCalculationService` — that interface belongs to and is used exclusively within the Catalog module. Analytics performs its margin calculations through its own `IMarginCalculator` (registered by `AnalyticsModule` itself). This is a documentation-only correction: delete the misleading comment.

## Background
A daily architecture-review routine flagged the comment on 2026-09-19 (issue #4228). Investigation confirms:
- `IMarginCalculationService` is defined in `Anela.Heblo.Domain.Features.Catalog.Services` and is registered only in `CatalogModule.cs` (`services.AddTransient<IMarginCalculationService, MarginCalculationService>();`).
- Its only consumer is `CatalogRepository` (via constructor injection) plus a few explanatory comments in `Catalog/CostProviders/*` describing an internal Catalog call chain.
- No file under `Features/Analytics/**` references `IMarginCalculationService` anywhere.
- Analytics instead defines and registers its own `IMarginCalculator` (`Analytics/Services/MarginCalculator.cs`), registered in `AnalyticsModule.cs` line 47 (`services.AddScoped<IMarginCalculator, MarginCalculator>();`), and consumed by `GetMarginReportHandler`, `GetProductMarginAnalysisHandler`, `GetProductMarginSummaryHandler`, `ReportBuilderService`, and `MonthlyBreakdownGenerator`.

The comment is therefore factually wrong and describes a cross-module runtime dependency (Analytics → CatalogModule for DI registration order) that does not exist. Leaving it in place risks a future developer wasting time hunting for a non-existent injection point, or wrongly concluding that module registration order between Analytics and Catalog is load-bearing.

## Functional Requirements

### FR-1: Remove the stale comment
Delete line 32 of `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs`:
```csharp
// Note: IMarginCalculationService is registered by CatalogModule and injected here
```
No other line in the file changes. `IMarginCalculator` registration (line 47) and all other module wiring are untouched.

**Acceptance criteria:**
- The comment string `IMarginCalculationService is registered by CatalogModule` no longer appears anywhere in `AnalyticsModule.cs`.
- `services.AddScoped<IProductFilterService, ProductFilterService>();` and `services.AddScoped<IReportBuilderService, ReportBuilderService>();` (the two lines that previously surrounded the stale comment) remain present and unchanged.
- No other file is modified — `grep -rn "IMarginCalculationService" backend/src` continues to show only the Catalog-module occurrences that exist today (i.e., the same set minus the one in `AnalyticsModule.cs`).

## Non-Functional Requirements

### NFR-1: No behavioral change
This is a comment-only removal. Runtime DI registration, module load order, and all Analytics/Catalog behavior must be byte-for-byte unaffected. The build output and test results must be identical to the pre-change baseline.

## Data Model
Not applicable — no data model changes.

## API / Interface Design
Not applicable — no API, contract, or interface changes. `IMarginCalculationService` and `IMarginCalculator` interfaces themselves are unchanged; only an inline comment is removed.

## Dependencies
None. This change has no dependency on other in-flight work and does not touch Catalog module code.

## Out of Scope
- Any change to `IMarginCalculationService`, `IMarginCalculator`, `CatalogModule.cs`, or any Catalog-module file.
- Any broader audit of other stale comments elsewhere in the codebase.
- Any refactor of Analytics/Catalog module boundaries.

## Open Questions
None.

## Status: COMPLETE
