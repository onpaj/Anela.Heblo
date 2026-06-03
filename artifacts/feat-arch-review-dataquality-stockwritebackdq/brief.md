## Module
DataQuality

## Finding
`StockWriteBackDqtComparer` (`backend/src/Anela.Heblo.Application/Features/DataQuality/Services/StockWriteBackDqtComparer.cs`, lines 1–2 and 17–18) directly injects two repository interfaces owned by the **Catalog** module:

```csharp
using Anela.Heblo.Domain.Features.Catalog.Stock;
// ...
private readonly IStockUpOperationRepository _operationRepository;
private readonly IStockTakingRepository _stockTakingRepository;
```

`IStockUpOperationRepository` (`Domain/Features/Catalog/Stock/IStockUpOperationRepository.cs`) is a full CRUD repository interface with write methods (`AddAsync`, `UpdateAsync`, `SaveChangesAsync`, `GetByDocumentNumberAsync`, `GetBySourceAsync`, etc.) that DataQuality will never use — DataQuality only calls `GetAll()` (line 33).

`IStockTakingRepository` extends the generic `IRepository<StockTakingRecord, int>` which similarly exposes write operations DataQuality doesn't need — DataQuality only calls `GetByDateRangeAsync` (line 36).

Per `docs/architecture/development_guidelines.md`:
> Communication between modules exclusively through `contracts/` (e.g., `IProductQueryService`)
> No direct access to another module's entities

DataQuality directly consumes Catalog's repository layer, bypassing the required contract/adapter pattern.

## Why it matters
- **Module boundary**: DataQuality is coupled to Catalog's persistence layer. Any refactoring of `IStockUpOperationRepository` (e.g., splitting or renaming) breaks DataQuality.
- **ISP**: DataQuality depends on write methods (`AddAsync`, `UpdateAsync`, `SaveChangesAsync`) it never calls. Testing `StockWriteBackDqtComparer` requires a mock that implements the full CRUD contract.
- **Future microservice readiness**: the guidelines note each module must be deployable independently. DataQuality directly importing Catalog repository interfaces breaks this.

## Suggested fix
1. Add narrow read-only contracts to `DataQuality/Contracts/`:
   ```csharp
   // DataQuality/Contracts/IStockOperationQuery.cs
   public interface IStockOperationQuery
   {
       IQueryable<StockUpOperation> GetAll();
   }

   // DataQuality/Contracts/IStockTakingQuery.cs
   public interface IStockTakingQuery
   {
       Task<List<StockTakingRecord>> GetByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default);
   }
   ```
2. Add adapters in the **Catalog** module's `Infrastructure/` that delegate to existing repositories.
3. Register adapters in `CatalogModule.cs`.
4. Update `StockWriteBackDqtComparer` to inject the new DataQuality-owned interfaces.

---
_Filed by daily arch-review routine on 2026-06-01._