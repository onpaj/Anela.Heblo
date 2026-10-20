### task: create-shoptet-order-test-client-interface

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/IShoptetOrderTestClient.cs`

- [ ] **Step 1: Create the new interface file**

Create `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/IShoptetOrderTestClient.cs` with this exact content:

```csharp
using Anela.Heblo.Application.Features.ShoptetOrders;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders;

/// <summary>
/// Shoptet order lifecycle operations needed only for integration-test setup/teardown
/// (creating and deleting real test orders in the live Shoptet store, and looking them
/// up by test-seed prefix). Not part of the Application-layer contract — Application
/// handlers must never depend on this interface.
/// </summary>
public interface IShoptetOrderTestClient
{
    Task<string> CreateOrderAsync(CreateEshopOrderRequest request, CancellationToken ct = default);
    Task DeleteOrderAsync(string orderCode, CancellationToken ct = default);
    Task<List<EshopOrderSummary>> GetRecentOrdersAsync(int count = 20, CancellationToken ct = default);
    Task<List<EshopOrderSummary>> ListByExternalCodePrefixAsync(string prefix, string? emailFilter = null, CancellationToken ct = default);
}
```

Note: `CreateEshopOrderRequest` and `EshopOrderSummary` live in `Anela.Heblo.Application.Features.ShoptetOrders` (Application layer). The `using` above is required — this mirrors the existing, pre-approved dependency direction: `ShoptetOrderClient.cs` (same adapter project) already has `using Anela.Heblo.Application.Features.ShoptetOrders;` at its top to reference these same DTOs. Do not move the DTOs — that is explicitly out of scope.

- [ ] **Step 2: Build the adapter project to verify the new file compiles standalone**

Run:
```bash
cd backend && dotnet build src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
```
Expected: `Build succeeded.` The new interface is not referenced anywhere yet, so this only proves the file itself is valid C# with a resolvable `using`.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/IShoptetOrderTestClient.cs
git commit -m "feat(shoptet-orders): add IShoptetOrderTestClient adapter interface"
```

---

