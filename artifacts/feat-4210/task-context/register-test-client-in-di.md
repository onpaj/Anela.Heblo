### task: register-test-client-in-di

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs:51-52`

- [ ] **Step 1: Add the new DI registration**

At `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs`, the existing registrations at lines 51-52 read:

```csharp
        services.AddTransient<IEshopOrderClient>(sp => sp.GetRequiredService<ShoptetOrderClient>());
        services.AddTransient<IShoptetExpeditionOrderSource>(sp => sp.GetRequiredService<ShoptetOrderClient>());
```

Insert a new line immediately after the `IEshopOrderClient` registration (before the `IShoptetExpeditionOrderSource` line), so the block reads:

```csharp
        services.AddTransient<IEshopOrderClient>(sp => sp.GetRequiredService<ShoptetOrderClient>());
        services.AddTransient<IShoptetOrderTestClient>(sp => sp.GetRequiredService<ShoptetOrderClient>());
        services.AddTransient<IShoptetExpeditionOrderSource>(sp => sp.GetRequiredService<ShoptetOrderClient>());
```

No `using` addition is needed — `IShoptetOrderTestClient` is in namespace `Anela.Heblo.Adapters.ShoptetApi.Orders`, which this file already imports via `using Anela.Heblo.Adapters.ShoptetApi.Orders;` at line 7.

- [ ] **Step 2: Build the adapter project**

Run:
```bash
cd backend && dotnet build src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs
git commit -m "feat(shoptet-orders): register IShoptetOrderTestClient in DI"
```

---

