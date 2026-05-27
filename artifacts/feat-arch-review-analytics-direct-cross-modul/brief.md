## Module
Analytics

## Finding

Two files in the Analytics module take direct compile-time dependencies on Catalog-owned types:

**1. `AnalyticsProduct` (Domain layer) imports `ProductType` from Catalog domain**
`backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProduct.cs:1–13`
```csharp
using Anela.Heblo.Domain.Features.Catalog;   // ← cross-module domain import

public class AnalyticsProduct
{
    public required ProductType Type { get; init; }  // ← Catalog's enum
```
The Analytics domain entity carries a hard dependency on a type owned by the Catalog domain.

**2. `AnalyticsRepository` (Analytics Application layer) injects `ICatalogRepository` directly**
`backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs:5–21`
```csharp
using Anela.Heblo.Domain.Features.Catalog;        // ← Catalog domain
...
public class AnalyticsRepository : IAnalyticsRepository
{
    private readonly ICatalogRepository _catalogRepository;  // ← Catalog's repository

    public AnalyticsRepository(ICatalogRepository catalogRepository, ...)
```
`AnalyticsRepository` delegates every product data fetch to `ICatalogRepository.GetProductsWithSalesInPeriod()` and `GetByIdAsync()`, calling these on `CatalogAggregate` objects directly.

Both `GetMarginReportHandler` (line 7, line 57) and `GetProductMarginSummaryHandler` (line 5, line 37) also import `ProductType` from Catalog as a downstream consequence.

## Why it matters

`development_guidelines.md` explicitly forbids:
- **"Direct access to another module's entities"**
- **"Shared repositories across modules"**

Analytics can never be deployed or tested in isolation; it is tightly coupled to Catalog's internal data model (`CatalogAggregate`, `MarginData`, `PurchaseHistory`, etc.). Any internal refactoring of the `CatalogAggregate` structure silently breaks the Analytics repository's manual field mappings (lines 52–116 of `AnalyticsRepository.cs`).

## Suggested fix

Apply the adapter pattern documented in `development_guidelines.md` (§ *Cross-Module Communication*):

1. **Analytics owns the contract.** Add `IAnalyticsProductSource` to `Application/Features/Analytics/Contracts/`:
```csharp
public interface IAnalyticsProductSource
{
    IAsyncEnumerable<AnalyticsProduct> StreamProductsWithSalesAsync(
        DateTime from, DateTime to, AnalyticsProductType[] types,
        CancellationToken ct = default);
    Task<AnalyticsProduct?> GetProductAnalysisDataAsync(
        string productId, DateTime from, DateTime to,
        CancellationToken ct = default);
}
```
Define `AnalyticsProductType` as an Analytics-owned enum (mirroring the values currently borrowed from Catalog).

2. **Catalog implements the adapter.** Move the existing `CatalogAggregate → AnalyticsProduct` mapping code from `AnalyticsRepository` into a new `CatalogAnalyticsSourceAdapter` class inside `Application/Features/Catalog/Infrastructure/`.

3. **Catalog registers the binding.** In `CatalogModule.AddCatalogModule()`:
```csharp
services.AddScoped<IAnalyticsProductSource, CatalogAnalyticsSourceAdapter>();
```

4. **`AnalyticsRepository` drops `ICatalogRepository`.** It now depends only on `IAnalyticsProductSource` (Analytics-owned) and `ApplicationDbContext` (already correct).

---
_Filed by daily arch-review routine on 2026-05-26._