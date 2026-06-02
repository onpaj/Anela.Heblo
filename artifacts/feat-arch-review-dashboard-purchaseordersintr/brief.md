## Module
Dashboard

## Finding
`PurchaseOrdersInTransitTile` is placed inside the Dashboard module at:
`backend/src/Anela.Heblo.Application/Features/Dashboard/Tiles/PurchaseOrdersInTransitTile.cs`

Despite its Dashboard location, it directly injects and calls `IPurchaseOrderRepository` from the Purchase domain (lines 1–2, 10, 28):

```csharp
using Anela.Heblo.Domain.Features.Purchase;
...
private readonly IPurchaseOrderRepository _purchaseOrderRepository;
...
var inTransitOrders = await _purchaseOrderRepository.GetByStatusAsync(PurchaseOrderStatus.InTransit, cancellationToken);
```

This is inconsistent with the pattern followed by every other purchase-domain tile. `LowStockEfficiencyTile`, also Purchase-domain, lives at `backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/LowStockEfficiencyTile.cs` and is registered in `PurchaseModule.cs`. The Dashboard module should own no tiles that depend on another module's domain — those tiles belong to the module that owns the domain data.

Additionally, `DashboardModule.cs` (line 22) registers `PurchaseOrdersInTransitTile` directly, compounding the cross-module coupling instead of letting Purchase manage it.

## Why it matters
- The Dashboard module gains a compile-time dependency on `Anela.Heblo.Domain.Features.Purchase`, violating the module independence rule ("No direct access to another module's entities").
- Placement is inconsistent with the established convention for all other feature tiles, making the codebase harder to navigate.
- If `IPurchaseOrderRepository` changes its signature, `DashboardModule` becomes a hidden breakage site that developers may not expect to touch.

## Suggested fix
Move the tile to the Purchase module (one file move, no logic changes):

```
backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTile.cs
```

Register it in `PurchaseModule.cs` alongside `LowStockEfficiencyTile`:
```csharp
services.RegisterTile<PurchaseOrdersInTransitTile>();
```

Remove the `Dashboard/Tiles/PurchaseOrdersInTransitTile.cs` file and the corresponding `RegisterTile` call and `using` statement from `DashboardModule.cs`.

---
_Filed by daily arch-review routine on 2026-05-28._