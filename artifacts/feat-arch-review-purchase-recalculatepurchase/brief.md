## Module
Purchase

## Finding
`RecalculatePurchasePriceHandler` imports and directly injects `IProductPriceErpClient` from the **Catalog module's domain layer**:

- File: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/RecalculatePurchasePrice/RecalculatePurchasePriceHandler.cs`
- Line 4: `using Anela.Heblo.Domain.Features.Catalog.Price;`
- Lines 13–14: `IProductPriceErpClient` injected as a constructor dependency

`IProductPriceErpClient` is defined at `backend/src/Anela.Heblo.Domain/Features/Catalog/Price/IProductPriceErpClient.cs` — it is owned by the Catalog module's domain.

## Why it matters
This violates the module isolation rule from `development_guidelines.md`: modules must communicate **only through the consuming module's own contracts**, never by reaching into another module's domain or application types. The Purchase module now has a hard compile-time dependency on Catalog's domain layer, which prevents future extraction of either module and defeats the contract-based decoupling architecture.

The established pattern in this codebase (see `ILeafletKnowledgeSource` example in the guidelines) is:
1. **Consumer (Purchase)** defines an interface in `Purchase/Contracts/` (e.g. `IPurchasePriceRecalculationService`)
2. **Provider (Catalog)** implements an adapter in `Catalog/Infrastructure/` that delegates to `IProductPriceErpClient`
3. **Catalog's module** registers the DI binding

## Suggested fix
1. Add `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/IPurchasePriceRecalculationService.cs`:
   ```csharp
   public interface IPurchasePriceRecalculationService
   {
       Task RecalculatePurchasePriceAsync(int bomId, CancellationToken cancellationToken);
   }
   ```
2. Add an adapter in Catalog (e.g. `Catalog/Infrastructure/CatalogPurchasePriceAdapter.cs`) that implements the new interface by delegating to `IProductPriceErpClient`.
3. Update `CatalogModule.cs` to register `services.AddScoped<IPurchasePriceRecalculationService, CatalogPurchasePriceAdapter>()`.
4. Replace the `IProductPriceErpClient` dependency in `RecalculatePurchasePriceHandler` with `IPurchasePriceRecalculationService`.

---
_Filed by daily arch-review routine on 2026-05-27._