## Module
Purchase

## Finding
`PurchaseModule.cs` is an Application-layer file that imports and directly references types from the Persistence (Infrastructure) layer:

```csharp
// backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs
using Anela.Heblo.Persistence;                                    // line 7
using Anela.Heblo.Persistence.Purchase.PurchaseOrders;            // line 8
...
services.AddScoped<IPurchaseOrderRepository>(provider =>
{
    var context = provider.GetRequiredService<ApplicationDbContext>();  // Persistence type
    return new PurchaseOrderRepository(context);                        // Persistence type
});
```

The Application layer must not depend on the Infrastructure/Persistence layer. That dependency is supposed to flow in the opposite direction (Persistence implements interfaces defined in Domain/Application). The DI wiring that binds `IPurchaseOrderRepository` → `PurchaseOrderRepository` belongs in `PersistenceModule.cs` or at the composition root (API layer), not in the Application module registration.

## Why it matters
This is a direct violation of the Clean Architecture dependency rule: Application → Infrastructure is forbidden. It couples the Application layer to a concrete database implementation, making it impossible to test Application logic without the Persistence project. It also defeats the purpose of the repository interface abstraction.

## Suggested fix
Move the `IPurchaseOrderRepository` → `PurchaseOrderRepository` DI registration to `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs`, following the pattern used by other modules (e.g. the Catalog or Journal repositories registered there). Remove the `using Anela.Heblo.Persistence` and `using Anela.Heblo.Persistence.Purchase.PurchaseOrders` imports from `PurchaseModule.cs` entirely.

---
_Filed by daily arch-review routine on 2026-05-22._