### task: implement-test-client-on-shoptet-order-client

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs:12`

- [ ] **Step 1: Add `IShoptetOrderTestClient` to the class's implemented-interface list**

At `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs` line 12, change:

```csharp
public class ShoptetOrderClient : IEshopOrderClient, IShoptetExpeditionOrderSource
```

to:

```csharp
public class ShoptetOrderClient : IEshopOrderClient, IShoptetOrderTestClient, IShoptetExpeditionOrderSource
```

No other change to this file — all 11 method bodies stay exactly where they are (the 4 relocated methods' implementations, e.g. `GetRecentOrdersAsync` at line 33, `ListByExternalCodePrefixAsync` at line 51, `CreateOrderAsync` at line 107, `DeleteOrderAsync` at line 189, already satisfy `IShoptetOrderTestClient`'s signatures verbatim since the interface was copied from the same method signatures).

- [ ] **Step 2: Build the adapter project to verify `ShoptetOrderClient` satisfies both interfaces**

Run:
```bash
cd backend && dotnet build src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
```
Expected: `Build succeeded.` If it fails with "does not implement interface member 'IShoptetOrderTestClient.X'", compare the failing signature character-for-character against `IShoptetOrderTestClient.cs` from the previous task — the two most common causes are a mismatched default-parameter value or a `CancellationToken ct = default` vs. no-default mismatch.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs
git commit -m "feat(shoptet-orders): implement IShoptetOrderTestClient on ShoptetOrderClient"
```

---

