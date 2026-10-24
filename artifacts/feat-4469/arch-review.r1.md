# Architecture Review: Decouple IStockAnalysisCalculator from use case

## Skip Design: true
Backend-only refactor; no UI.

## Architectural Fit Assessment
Verified in code: `Purchase/Contracts/` already holds service-facing types (`StockStatusFilter`, `StockSeverity`, `StockAnalysisSortBy`, `MaterialStockSnapshot`, DTOs such as `SupplierDto`). `SortItems` already takes `StockAnalysisSortBy` from Contracts rather than the request, so `FilterItems` is the lone outlier. The change aligns the service with existing convention (use case -> service). The DTO classes `StockAnalysisItemDto`, `LastPurchaseInfoDto`, `StockAnalysisSummaryDto` exist only in `GetPurchaseStockAnalysisResponse.cs` (Manufacture has its own unrelated types). Namespace move does not alter OpenAPI schema names (NSwag/Swagger use simple type names), so the generated TS client is unaffected; verify no schema-name collision.

## Proposed Architecture

### Component Overview
```
GetPurchaseStockAnalysisHandler (UseCases)
   |  builds StockAnalysisFilter from request
   v
IStockAnalysisCalculator (Services) --> Contracts: StockAnalysisItemDto, StockAnalysisSummaryDto,
StockAnalysisCalculator                  LastPurchaseInfoDto, StockAnalysisFilter, StockStatusFilter
GetPurchaseStockAnalysisResponse (UseCases) --> Contracts DTOs
```

### Key Design Decisions

#### Decision 1: Filter shape
**Options considered:** pass two primitive args; pass record; pass class.
**Chosen approach:** `StockAnalysisFilter` sealed-free plain class with settable properties, in `Contracts/`.
**Rationale:** Matches issue and CLAUDE.md (DTO-like types are classes, avoid records). Extensible for future filter fields.

#### Decision 2: File layout
One type per file in `Contracts/`, namespace `Anela.Heblo.Application.Features.Purchase.Contracts` (the same namespace the response file already imports), so most consumers need no new `using`.

## Implementation Guidance

### Directory / Module Structure
New: `Contracts/StockAnalysisItemDto.cs`, `Contracts/StockAnalysisSummaryDto.cs`, `Contracts/LastPurchaseInfoDto.cs`, `Contracts/StockAnalysisFilter.cs`.
Edit: `UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs` (remove the three DTOs), `Services/IStockAnalysisCalculator.cs`, `Services/StockAnalysisCalculator.cs`, `UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs` (line ~52), tests `StockAnalysisCalculatorTests.cs` (and drop now-unneeded `using` of the use-case namespace there), plus any file that fails to compile. Because types move to Contracts, files that imported only the use-case namespace for these DTOs (e.g. `LowStockEfficiencyTileTests.cs`, `LowStockEfficiencyTile.cs`) may need `using ...Contracts`.
Docs: `docs/processes/calc-purchase-stock-analysis.md` `owns:` add the four new Contracts files; bump `verified_at`.

### Interfaces and Contracts
```csharp
public class StockAnalysisFilter {
    public bool OnlyConfigured { get; set; }
    public StockStatusFilter StockStatus { get; set; } = StockStatusFilter.All;
}
List<StockAnalysisItemDto> FilterItems(List<StockAnalysisItemDto> items, StockAnalysisFilter filter);
```

### Data Flow
Controller -> MediatR request -> handler: analyze all items -> `new StockAnalysisFilter { OnlyConfigured = request.OnlyConfigured, StockStatus = request.StockStatus }` -> `FilterItems` -> search/sort/page. Summary still computed on the unfiltered set.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Missed `using` breaks compile in tests/tile | Low | `dotnet build` of solution and tests |
| OpenAPI client diff | Low | Rebuild; confirm generated client unchanged |
| Process-doc CI gate fails | Medium | Update `owns:` and `verified_at` in same PR; run `check.py index` |

## Specification Amendments
None.

## Prerequisites
None.
