# Specification: Decouple IStockAnalysisCalculator from the GetPurchaseStockAnalysis use case

## Summary
`IStockAnalysisCalculator.FilterItems` currently accepts the MediatR request `GetPurchaseStockAnalysisRequest`, and the DTOs it returns live in the use-case folder. This refactor moves the three DTOs to `Purchase/Contracts/` and introduces a `StockAnalysisFilter` value object so the service layer no longer depends on the use-case namespace. Behaviour is unchanged.

## Background
Dependency direction should be use case -> service, not the reverse. `IStockAnalysisCalculator.cs` imports `...UseCases.GetPurchaseStockAnalysis` for `GetPurchaseStockAnalysisRequest` (line 46) and for `StockAnalysisItemDto`/`StockAnalysisSummaryDto`, which are defined in `GetPurchaseStockAnalysisResponse.cs`. `StockAnalysisCalculator.FilterItems` (line 120) and private `ShouldIncludeItem` read only `OnlyConfigured` and `StockStatus` from the request. Source: arch-review issue #4469 (same class of issue as #4105).

## Functional Requirements

### FR-1: Move response-shape DTOs to Contracts
Move `StockAnalysisItemDto`, `StockAnalysisSummaryDto`, `LastPurchaseInfoDto` out of `UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs` into three separate files in `Features/Purchase/Contracts/` (namespace `Anela.Heblo.Application.Features.Purchase.Contracts`), as classes (never records), with members unchanged. `GetPurchaseStockAnalysisResponse` stays in the use-case file.
**Acceptance criteria:**
- Three new files exist, one type each; the types are removed from the response file.
- Property names, types, defaults and order are identical.
- Generated OpenAPI schema names and shapes (and the TS client) are unchanged.

### FR-2: Introduce StockAnalysisFilter
Create `Contracts/StockAnalysisFilter.cs`: a class with `bool OnlyConfigured` and `StockStatusFilter StockStatus` (default `All`).
**Acceptance criteria:**
- Defaults match the request (`OnlyConfigured = false`, `StockStatus = All`).
- Class, not a record (project rule on DTO-like contracts).

### FR-3: Change FilterItems signature
`IStockAnalysisCalculator.FilterItems(List<StockAnalysisItemDto> items, StockAnalysisFilter filter)`; same change in `StockAnalysisCalculator`, including `ShouldIncludeItem`.
**Acceptance criteria:**
- `Services/IStockAnalysisCalculator.cs` and `Services/StockAnalysisCalculator.cs` have no `using` of any `UseCases` namespace.
- Filtering semantics are identical.

### FR-4: Update handler
`GetPurchaseStockAnalysisHandler` builds a `StockAnalysisFilter` from `request.OnlyConfigured` and `request.StockStatus` and passes it to `FilterItems`.
**Acceptance criteria:** No other production caller changes beyond `using` fixes.

### FR-5: Update tests and docs
- `StockAnalysisCalculatorTests` FilterItems tests use `StockAnalysisFilter`.
- Consumers that referenced DTOs via the use-case namespace compile (add `using ...Contracts` where missing: e.g. `LowStockEfficiencyTile`, controller, tests).
- `docs/processes/calc-purchase-stock-analysis.md`: add the new Contracts files to `owns:` (the Contracts files are listed individually, not by glob), bump `verified_at`, mention the filter in the relevant step if filtering is described; run `python3 scripts/process-docs/check.py index`.

## Non-Functional Requirements

### NFR-1: Performance
No change; same LINQ filtering.

### NFR-2: Security
No change; no endpoint or auth change.

### NFR-3: Compatibility
HTTP API contract and generated clients unchanged. `dotnet build`, `dotnet format` and all Purchase tests pass.

## Data Model
No persistence changes. New type: `StockAnalysisFilter { bool OnlyConfigured; StockStatusFilter StockStatus }`.

## API / Interface Design
Only the internal C# interface `IStockAnalysisCalculator.FilterItems` changes. No endpoint changes.

## Dependencies
`StockStatusFilter` already in `Contracts/`. Existing tests (calculator, handler, tile, controller) must be updated for namespaces.

## Out of Scope
- Changing `SortItems`, `CalculateSummary`, `AnalyzeItem`.
- Manufacture module's own filtering/DTOs (separate types).
- Other use-case/service couplings, frontend changes.

## Open Questions
None.

## Status: COMPLETE
