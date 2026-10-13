### task: packaging-contract

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/Contracts/IPackedOrderStatusUpdater.cs`

- [ ] **Step 1: Write the interface**

```csharp
namespace Anela.Heblo.Application.Features.Packaging.Contracts;

public interface IPackedOrderStatusUpdater
{
    /// <summary>
    /// Transitions the order to the configured "packed" state (Shoptet "Zabaleno", id 52 by default).
    /// Mirrors IEshopOrderClient.MarkAsPackedAsync; Packaging depends only on this narrower surface
    /// via the consumer-owns-contract pattern (see IShipmentDeliveryChecker / ILeafletKnowledgeSource).
    /// </summary>
    Task MarkAsPackedAsync(string orderCode, CancellationToken ct = default);
}
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded (new file adds a compiling, currently-unused interface — no other code references it yet).

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/Contracts/IPackedOrderStatusUpdater.cs
git commit -m "feat(packaging): add IPackedOrderStatusUpdater consumer-owned contract"
```

---
