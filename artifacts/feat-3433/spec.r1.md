# Specification: Move StockValueService to Catalog Module as Cross-Module Adapter

## Summary

`StockValueService`, currently in `FinancialOverview.Application.Services`, directly injects two Catalog-owned domain interfaces (`IErpStockClient`, `IProductPriceErpClient`), violating the module boundary rule that forbids consumer modules from referencing provider-owned types. This refactoring moves the implementation into `Catalog.Application.Infrastructure` as a named adapter (`FinancialOverviewStockValueAdapter`), re-wires the DI binding in `CatalogModule`, and adds a `ModuleBoundariesTests` rule to prevent regression — following the exact same pattern already used for `IManufactureCatalogSource`/`CatalogManufactureCatalogSourceAdapter` and `ILeafletKnowledgeSource`/`KnowledgeBaseLeafletSourceAdapter`.

## Background

The daily arch-review routine (2026-06-30) flagged that `FinancialOverview.Application.Features.FinancialOverview.Services.StockValueService` imports `Anela.Heblo.Domain.Features.Catalog.Stock.IErpStockClient` and `Anela.Heblo.Domain.Features.Catalog.Price.IProductPriceErpClient`. Both are Catalog-module domain contracts. The development guidelines (`docs/architecture/development_guidelines.md`, section *Cross-Module Communication*) require that: (a) the consumer module owns the contract interface, (b) the provider module implements an adapter, and (c) the provider registers the DI binding. The current placement inverts this: FinancialOverview both owns the contract (`IStockValueService` in `Domain.Features.FinancialOverview`) and implements it using Catalog internals. The contract ownership is correct; only the implementation placement is wrong.

## Functional Requirements

### FR-1: Relocate StockValueService to Catalog.Infrastructure as an adapter

Create `Anela.Heblo.Application.Features.Catalog.Infrastructure.FinancialOverviewStockValueAdapter` that:
- Is declared `internal sealed` (consistent with other adapters in that folder such as `CatalogManufactureCatalogSourceAdapter`).
- Implements `Anela.Heblo.Domain.Features.FinancialOverview.IStockValueService` (the existing, unchanged contract).
- Contains the full body of the current `StockValueService`: `IErpStockClient`, `IProductPriceErpClient`, and `ILogger<>` constructor parameters; all private helper methods (`CalculateMonthlyStockChangeAsync`, `GetWarehouseStockValueAsync`); and the warehouse ID constants.
- Namespace: `Anela.Heblo.Application.Features.Catalog.Infrastructure`.

**Acceptance criteria:**
- File exists at `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/FinancialOverviewStockValueAdapter.cs`.
- Class is `internal sealed` and implements `IStockValueService`.
- All existing logic (price dictionary build, month loop, parallel warehouse tasks, error handling and logging) is preserved verbatim — this is a structural move, not a logic change.
- No reference to `Anela.Heblo.Domain.Features.Catalog.*` appears inside any `FinancialOverview` namespace type after the change.

### FR-2: Register the adapter binding in CatalogModule

Add a single line to `CatalogModule.AddCatalogModule()`:
```csharp
services.AddScoped<IStockValueService, FinancialOverviewStockValueAdapter>();
```
using the fully qualified `IStockValueService` from `Anela.Heblo.Domain.Features.FinancialOverview`.

**Acceptance criteria:**
- `CatalogModule.cs` contains the registration of `IStockValueService` → `FinancialOverviewStockValueAdapter`.
- The `using` directive for `Anela.Heblo.Domain.Features.FinancialOverview` is added to `CatalogModule.cs`.

### FR-3: Remove the misplaced registration and file from FinancialOverviewModule

Remove:
- `services.AddScoped<IStockValueService, StockValueService>();` from `FinancialOverviewModule.AddFinancialOverviewModule()`.
- The `using Anela.Heblo.Application.Features.FinancialOverview.Services;` import that supported it in `FinancialOverviewModule.cs` (if it is only used for `StockValueService`).
- The file `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/StockValueService.cs`.

**Acceptance criteria:**
- `FinancialOverviewModule.cs` registers only `IFinancialAnalysisService` and the background refresh task; it no longer references `StockValueService`.
- The old `StockValueService.cs` file no longer exists in the repository.
- `FinancialOverview.Services` namespace contains no types that import `Anela.Heblo.Domain.Features.Catalog.*`.

### FR-4: Add a ModuleBoundariesTests rule for FinancialOverview → Catalog

Extend `ModuleBoundariesTests.Rules()` in `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` with a new `ModuleBoundaryRule`:

```csharp
new ModuleBoundaryRule(
    Name: "FinancialOverview -> Catalog",
    InspectedNamespacePrefix: "Anela.Heblo.Application.Features.FinancialOverview",
    ForbiddenNamespacePrefixes: new[]
    {
        "Anela.Heblo.Domain.Features.Catalog",
        "Anela.Heblo.Application.Features.Catalog",
        "Anela.Heblo.Persistence.Catalog",
    },
    Allowlist: new HashSet<string>(StringComparer.Ordinal)),
```

No allowlist entries are expected; the violation being fixed is the only one. The allowlist must be empty to confirm a clean boundary.

**Acceptance criteria:**
- The new rule appears in `Rules()`.
- The test passes with an empty allowlist (zero violations) once FR-1 through FR-3 are complete.
- `dotnet test --filter "FullyQualifiedName~ModuleBoundariesTests"` is green.

## Non-Functional Requirements

### NFR-1: No behavior change

This is a pure structural refactoring. The service is registered as `AddScoped` in both the old and the new location; lifetime must remain `Scoped`. Logic inside the adapter must be character-for-character identical to the original `StockValueService` body. No new caching, retry, or error-handling is introduced.

### NFR-2: Build and format pass

`dotnet build` and `dotnet format --verify-no-changes` must succeed after the change with no new warnings or errors.

### NFR-3: Assembly layering preserved

`IStockValueService` remains in `Anela.Heblo.Domain.Features.FinancialOverview` (Domain layer). The adapter in `Anela.Heblo.Application.Features.Catalog.Infrastructure` is in the Application layer. No new Domain→Application references are introduced. The existing `Domain_must_not_reference_Application_and_relocated_invoice_types_must_be_gone` test must continue to pass.

## Data Model

No database entities or migrations are involved. The service performs read-only ERP API calls (`IErpStockClient.StockToDateAsync`, `IProductPriceErpClient.GetAllAsync`) and returns in-memory computed `IReadOnlyList<MonthlyStockChange>` (a FinancialOverview-owned domain value object). No data model changes.

## API / Interface Design

No HTTP endpoints change. `IStockValueService` signature is unchanged:

```csharp
// Anela.Heblo.Domain.Features.FinancialOverview — unchanged
public interface IStockValueService
{
    Task<IReadOnlyList<MonthlyStockChange>> GetStockValueChangesAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken);
}
```

The adapter in Catalog.Infrastructure replaces the implementation. All consumers (currently `FinancialAnalysisService` in FinancialOverview, and the background refresh task wiring in `FinancialOverviewModule`) continue to inject `IStockValueService` without change.

## Dependencies

- `IErpStockClient` — `Anela.Heblo.Domain.Features.Catalog.Stock` (Catalog-owned; the adapter legitimately uses it because it lives inside the Catalog module).
- `IProductPriceErpClient` — `Anela.Heblo.Domain.Features.Catalog.Price` (same rationale).
- `IStockValueService` — `Anela.Heblo.Domain.Features.FinancialOverview` (FinancialOverview-owned contract; the adapter implements it, introducing a deliberate cross-module interface dependency from Catalog → FinancialOverview Domain, which is a benign downward reference — Domain interfaces do not pull in Application types).
- `MonthlyStockChange` / `StockChangeByType` — `Anela.Heblo.Domain.Features.FinancialOverview` (return types; same as above).
- `ILogger<FinancialOverviewStockValueAdapter>` — `Microsoft.Extensions.Logging`; no change.

Note: the Catalog adapter referencing `IStockValueService` (a FinancialOverview Domain type) does introduce a `Catalog → FinancialOverview.Domain` reference. This direction is acceptable: Catalog is the provider implementing a consumer-owned contract, exactly as `CatalogManufactureCatalogSourceAdapter` implements `IManufactureCatalogSource`. If a future `ModuleBoundariesTests` rule is added for `Catalog → FinancialOverview`, `FinancialOverviewStockValueAdapter` must be listed in that rule's allowlist with a justification comment.

## Out of Scope

- Any change to `IStockValueService`'s method signature, behavior, or error-handling semantics.
- Any change to `FinancialAnalysisService`, `GetFinancialOverviewHandler`, or any other FinancialOverview consumer.
- Moving `MonthlyStockChange` or `StockChangeByType` out of the FinancialOverview domain.
- Adding retry logic, caching, or resilience wrappers to the adapter.
- Updating any E2E or integration tests (none currently cover `StockValueService` directly).
- Addressing any other arch-review findings in FinancialOverview (tracked separately).

## Open Questions

None.

## Status: COMPLETE
