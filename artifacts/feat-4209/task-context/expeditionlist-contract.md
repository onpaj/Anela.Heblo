### task: expeditionlist-contract

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/IOrderStatusReader.cs`

- [ ] **Step 1: Write the interface**

```csharp
namespace Anela.Heblo.Application.Features.ExpeditionList.Contracts;

public interface IOrderStatusReader
{
    /// <summary>
    /// Returns the order's current Shoptet status id.
    /// Mirrors IEshopOrderClient.GetOrderStatusIdAsync; may throw HttpRequestException with
    /// StatusCode == HttpStatusCode.NotFound when the order does not exist — callers depend on this
    /// exact exception shape (see PrintExpeditionOrderHandler's 404 handling). Implementations must
    /// let it propagate unmodified.
    /// </summary>
    Task<int> GetOrderStatusIdAsync(string orderCode, CancellationToken ct = default);
}
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded (new, currently-unused interface).

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/IOrderStatusReader.cs
git commit -m "feat(expedition-list): add IOrderStatusReader consumer-owned contract"
```

---
