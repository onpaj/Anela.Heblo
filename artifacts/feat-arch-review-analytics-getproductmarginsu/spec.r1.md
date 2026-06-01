# Specification: Refactor GetProductMarginSummary Handler to Depend on Abstractions

## Summary
Extract interfaces for `MarginCalculator` and `MonthlyBreakdownGenerator`, relocate `MarginCalculator` from the Domain layer to the Application layer, and update `GetProductMarginSummaryHandler` to depend on those interfaces. This aligns the handler with the Dependency Inversion Principle and the established interface pattern used by sibling services (`IProductFilterService`, `IReportBuilderService`) in the same module.

## Background
The `GetProductMarginSummaryHandler` (Application layer) currently injects two concrete classes:

- `MarginCalculator` — located in the **Domain layer** (`Anela.Heblo.Domain.Features.Analytics.MarginCalculator`), violating Clean Architecture by having the Application layer depend on a concrete Domain class with no abstraction.
- `MonthlyBreakdownGenerator` — located in the Application layer, also injected as a concrete type with no interface.

Both are registered in `AnalyticsModule.cs` (lines 37–38) under a comment labelled "Legacy services (keeping for backward compatibility)". The comment is misleading: these services are the **active primary path** for every `GetProductMarginSummary` request and have no replacement. Sibling handlers in the same module already depend on `IProductFilterService` and `IReportBuilderService` via interfaces — the two services in question were simply missed during the prior extraction. The result is inconsistent abstractions across the Analytics module, untestable code paths, and incorrect documentation that creates confusion about the code's intent.

## Functional Requirements

### FR-1: Define `IMarginCalculator` Interface
Create a new interface `IMarginCalculator` in `backend/src/Anela.Heblo.Application/Features/Analytics/Services/IMarginCalculator.cs` that exposes the full public surface currently consumed by `GetProductMarginSummaryHandler` and any other callers.

**Acceptance criteria:**
- Interface declares: `Task<MarginCalculationResult> CalculateAsync(...)`, `string GetGroupKey(AnalyticsProduct product, ProductGroupingMode groupingMode)`, `string GetGroupDisplayName(string groupKey, ProductGroupingMode mode, List<AnalyticsProduct> products)`, `decimal GetMarginAmountForLevel(AnalyticsProduct product, string marginLevel)`.
- Signatures match the existing public methods of `MarginCalculator` exactly (parameter names, types, nullability, async patterns).
- Interface lives under `Anela.Heblo.Application.Features.Analytics.Services` namespace.
- All current public methods of `MarginCalculator` consumed by Application-layer code are represented; any methods only used internally remain off the interface.

### FR-2: Define `IMonthlyBreakdownGenerator` Interface
Create `IMonthlyBreakdownGenerator` in the same `Services/` folder.

**Acceptance criteria:**
- Interface declares: `List<MonthlyProductMarginDto> Generate(MarginCalculationResult result, DateRange dateRange, ProductGroupingMode groupingMode, string marginLevel = "M2")`.
- Default parameter values preserved.
- Lives under `Anela.Heblo.Application.Features.Analytics.Services` namespace.

### FR-3: Relocate `MarginCalculator` to the Application Layer
Move `MarginCalculator` from `Anela.Heblo.Domain.Features.Analytics` to `Anela.Heblo.Application.Features.Analytics.Services`.

**Acceptance criteria:**
- File physically moved to `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs`.
- Namespace updated to `Anela.Heblo.Application.Features.Analytics.Services`.
- Class implements `IMarginCalculator`.
- All references to the old namespace are updated across the solution (handlers, tests, DI registrations).
- The Domain project no longer contains `MarginCalculator`.
- The Domain project's references and assembly remain unchanged otherwise; no Domain-layer entity or value object is touched.

### FR-4: Make `MonthlyBreakdownGenerator` Implement `IMonthlyBreakdownGenerator`
Update the existing `MonthlyBreakdownGenerator` class to implement the new interface.

**Acceptance criteria:**
- Class implements `IMonthlyBreakdownGenerator`.
- No behavioural changes — only the implementation declaration is added.
- File location unchanged.

### FR-5: Update DI Registrations in `AnalyticsModule`
In `AnalyticsModule.cs`, register both services behind their interfaces and remove the misleading comment.

**Acceptance criteria:**
- `services.AddScoped<IMarginCalculator, MarginCalculator>()` replaces the existing concrete registration.
- `services.AddScoped<IMonthlyBreakdownGenerator, MonthlyBreakdownGenerator>()` replaces the existing concrete registration.
- Service lifetime (`Scoped` vs `Transient` vs `Singleton`) matches the current registration to avoid behaviour change.
- The comment `// Legacy services (keeping for backward compatibility)` is removed.
- No other registrations in `AnalyticsModule` are altered.

### FR-6: Update `GetProductMarginSummaryHandler` to Inject Interfaces
Modify the handler constructor to depend on `IMarginCalculator` and `IMonthlyBreakdownGenerator`.

**Acceptance criteria:**
- Constructor signature uses interface types.
- Private readonly fields renamed to reflect interface types (`_marginCalculator`, `_monthlyBreakdownGenerator` field names may remain — only types change).
- No behavioural changes in the handler logic.
- The handler compiles and all existing tests pass.

### FR-7: Update All Other Consumers of the Concrete Classes
Find and update any other classes that inject `MarginCalculator` or `MonthlyBreakdownGenerator` as concrete dependencies.

**Acceptance criteria:**
- A solution-wide search for `MarginCalculator` and `MonthlyBreakdownGenerator` confirms no remaining concrete-type constructor injections (excluding the DI registration itself).
- Any callers found are updated to use the interfaces.
- If no other callers exist, this is documented in the PR description.

### FR-8: Update or Add Unit Tests
Verify the handler is now mockable and that existing behaviour is preserved.

**Acceptance criteria:**
- Existing tests for `GetProductMarginSummaryHandler` continue to pass after the refactor.
- At least one unit test demonstrates that `IMarginCalculator` and `IMonthlyBreakdownGenerator` can be mocked (e.g., using Moq or NSubstitute, consistent with the project's mocking convention) and injected into the handler.
- Existing tests for `MarginCalculator` and `MonthlyBreakdownGenerator` are updated to reflect the new namespace and continue to pass.

## Non-Functional Requirements

### NFR-1: Performance
No performance change is expected or permitted. The refactor is purely structural — same code paths, same allocations, same DI lifetimes. Benchmark or response-time validation is not required, but any measurable regression in `GetProductMarginSummary` request latency is a blocker.

### NFR-2: Security
No security surface change. No new endpoints, no auth changes, no data exposure changes. Existing authorization on the `GetProductMarginSummary` endpoint remains in effect.

### NFR-3: Backwards Compatibility
- The public API contract of the `GetProductMarginSummary` endpoint is unchanged.
- Response DTOs, error codes, and status codes are unchanged.
- No database schema changes.
- No breaking changes for consumers of the HTTP API.

### NFR-4: Code Quality
- `dotnet build` succeeds with zero new warnings.
- `dotnet format` produces no diffs.
- All existing unit tests pass.
- Clean Architecture layering is verified: the Domain project has no reference to Application; the Application project may reference Domain.

## Data Model
No data model changes. The refactor touches only DI wiring and class organization. Entities, DTOs, persistence, and repository contracts remain identical.

Affected types (for reference, not modified):
- `MarginCalculationResult` (Application layer DTO)
- `MonthlyProductMarginDto` (Application layer DTO)
- `AnalyticsProduct` (Domain entity)
- `ProductGroupingMode` (Domain enum)
- `DateRange` (shared value object)

## API / Interface Design

### New Interfaces

```csharp
// backend/src/Anela.Heblo.Application/Features/Analytics/Services/IMarginCalculator.cs
namespace Anela.Heblo.Application.Features.Analytics.Services;

public interface IMarginCalculator
{
    Task<MarginCalculationResult> CalculateAsync(/* current parameter list preserved verbatim */);
    string GetGroupKey(AnalyticsProduct product, ProductGroupingMode groupingMode);
    string GetGroupDisplayName(string groupKey, ProductGroupingMode mode, List<AnalyticsProduct> products);
    decimal GetMarginAmountForLevel(AnalyticsProduct product, string marginLevel);
}
```

```csharp
// backend/src/Anela.Heblo.Application/Features/Analytics/Services/IMonthlyBreakdownGenerator.cs
namespace Anela.Heblo.Application.Features.Analytics.Services;

public interface IMonthlyBreakdownGenerator
{
    List<MonthlyProductMarginDto> Generate(
        MarginCalculationResult result,
        DateRange dateRange,
        ProductGroupingMode groupingMode,
        string marginLevel = "M2");
}
```

### Handler Constructor (After)

```csharp
public GetProductMarginSummaryHandler(
    IAnalyticsRepository analyticsRepository,
    IMarginCalculator marginCalculator,
    IMonthlyBreakdownGenerator monthlyBreakdownGenerator)
{
    _analyticsRepository = analyticsRepository;
    _marginCalculator = marginCalculator;
    _monthlyBreakdownGenerator = monthlyBreakdownGenerator;
}
```

### HTTP API
No HTTP API changes. The `GetProductMarginSummary` endpoint contract (request, response, status codes, auth) is preserved exactly.

## Dependencies
- **MediatR** — already used by handler; no version change.
- **.NET 8 DI container** — already in use; no changes.
- **Existing test framework** (xUnit + Moq/NSubstitute per project convention) — used to add the mockability test.

No new NuGet packages. No external service dependencies.

## Out of Scope
- Refactoring `MarginCalculator` internals or splitting it into smaller services.
- Changing the calculation algorithm or business logic for margin computation.
- Refactoring `MonthlyBreakdownGenerator` internals.
- Touching other handlers in the Analytics module unless they directly inject `MarginCalculator` or `MonthlyBreakdownGenerator` (FR-7).
- Changes to `IProductFilterService` or `IReportBuilderService`.
- Performance optimization of margin calculation.
- Adding new functionality to either service.
- Changes to the `GetProductMarginSummary` HTTP endpoint, request DTO, or response DTO.
- Database schema changes.
- Frontend changes.

## Open Questions
None.

## Status: COMPLETE