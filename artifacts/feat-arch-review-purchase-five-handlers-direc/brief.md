## Module
Purchase

## Finding
Five Purchase handlers inject `ICatalogRepository` directly from `Anela.Heblo.Domain.Features.Catalog`, crossing the module boundary without a consumer-owned contract:

- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/CreatePurchaseOrderHandler.cs:5,17,25`
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderHandler.cs:4,15,22`
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderById/GetPurchaseOrderByIdHandler.cs:3,15,21`
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs:3,12,17`
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/RecalculatePurchasePrice/RecalculatePurchasePriceHandler.cs:2,11,16`

`ICatalogRepository` is a large, fat interface (18+ Refresh* methods, load timestamps, merge tracking, analytics methods) defined in the Catalog module's domain. Purchase handlers use only a small subset: `GetByIdAsync`, `GetAllAsync`, and `GetByIdsAsync`.

Per `development_guidelines.md` (Cross-Module Communication section and the ILeafletKnowledgeSource example), the correct pattern is:
1. Purchase defines a narrow consumer-owned interface in its own `Contracts/` folder
2. Catalog provides an adapter in its `Infrastructure/` folder that implements that interface
3. Catalog's DI module registers the binding

## Why it matters
- Violates module isolation: Purchase is now directly coupled to Catalog's domain internals. Any change to `ICatalogRepository` forces recompilation of Purchase handlers.
- Purchase handlers receive a bloated interface with 20+ methods they never use (ISP violation).
- The architectural test in `ModuleBoundariesTests.cs` enforces this pattern for Leaflet/KnowledgeBase — the same protection is absent here.
- Prevents treating Purchase and Catalog as independently deployable units.

## Suggested fix
1. Create `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/IMaterialCatalogService.cs` with only the methods Purchase needs:
   ```csharp
   public interface IMaterialCatalogService
   {
       Task<MaterialInfo?> GetByIdAsync(string id, CancellationToken ct = default);
       Task<IReadOnlyDictionary<string, MaterialInfo>> GetByIdsAsync(IEnumerable<string> ids, CancellationToken ct = default);
       Task<IEnumerable<MaterialInfo>> GetAllAsync(CancellationToken ct = default);
   }
   ```
2. Create an adapter in `Anela.Heblo.Application/Features/Catalog/Infrastructure/PurchaseMaterialCatalogAdapter.cs` implementing `IMaterialCatalogService` by delegating to the existing `ICatalogRepository`.
3. Register the adapter binding in `CatalogModule.cs`.
4. Replace `ICatalogRepository` injection in all five Purchase handlers with `IMaterialCatalogService`.

---
_Filed by daily arch-review routine on 2026-05-22._