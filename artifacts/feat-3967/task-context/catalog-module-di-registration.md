### task: catalog-module-di-registration

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs:61-66`

- [ ] **Step 1: Register the two new adapter bindings**

The file already has `using Anela.Heblo.Application.Features.DataQuality.Contracts;` (line 21) and `using Anela.Heblo.Application.Features.Catalog.Infrastructure;` (line 8), so no new `using` is needed. Find this existing block (lines 61-66):

```csharp
        // DataQuality owns the query contracts; Catalog (this module) provides the adapter implementations.
        services.AddScoped<IStockOperationQuery, DataQualityStockOperationQueryAdapter>();
        services.AddScoped<IStockTakingQuery, DataQualityStockTakingQueryAdapter>();
        services.AddScoped<IMaterialLotStockQuery, DataQualityMaterialLotStockQueryAdapter>();
        // DataQuality owns the resilience contract; Catalog (this module) provides the adapter implementation.
        services.AddScoped<IDqtResilienceService, DataQualityResilienceAdapter>();
```

Replace it with (appending the two new registrations to the same "DataQuality owns the query contracts" group, after the resilience registration):

```csharp
        // DataQuality owns the query contracts; Catalog (this module) provides the adapter implementations.
        services.AddScoped<IStockOperationQuery, DataQualityStockOperationQueryAdapter>();
        services.AddScoped<IStockTakingQuery, DataQualityStockTakingQueryAdapter>();
        services.AddScoped<IMaterialLotStockQuery, DataQualityMaterialLotStockQueryAdapter>();
        // DataQuality owns the resilience contract; Catalog (this module) provides the adapter implementation.
        services.AddScoped<IDqtResilienceService, DataQualityResilienceAdapter>();
        services.AddScoped<IDqtEshopStockSource, DataQualityEshopStockSourceAdapter>();
        services.AddScoped<IDqtErpStockSource, DataQualityErpStockSourceAdapter>();
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
git commit -m "Register IDqtEshopStockSource/IDqtErpStockSource adapters in CatalogModule"
```

---

