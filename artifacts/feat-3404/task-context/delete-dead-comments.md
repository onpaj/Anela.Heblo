# Task Plan — feat-3404

### task: delete-dead-comments

**Goal:** Delete the two commented-out `RegisterRefreshTask` blocks from `CatalogModule.cs`.

**File:** `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`

**Change:** Remove lines 244–270:
```
        // COMMENTED OUT - Old services replaced by new cost source architecture
        // services.RegisterRefreshTask<ISalesCostCalculationService>(
        //     nameof(ISalesCostCalculationService.Reload),
        //     async (serviceProvider, ct) =>
        //     {
        //         var catalogRepository = serviceProvider.GetRequiredService<ICatalogRepository>();
        //         var costService = serviceProvider.GetRequiredService<ISalesCostCalculationService>();
        //
        //         await catalogRepository.WaitForCurrentMergeAsync(ct);
        //         await costService.Reload();
        //     }
        // );

        // COMMENTED OUT - Old services replaced by new cost source architecture
        // services.RegisterRefreshTask<IManufactureCostCalculationService>(
        //     nameof(IManufactureCostCalculationService.Reload),
        //     async (serviceProvider, ct) =>
        //     {
        //         var catalogRepository = serviceProvider.GetRequiredService<ICatalogRepository>();
        //         var manufactureCostService = serviceProvider.GetRequiredService<IManufactureCostCalculationService>();
        //
        //         await catalogRepository.WaitForCurrentMergeAsync(ct);
        //         var catalogData = await catalogRepository.GetAllAsync(ct);
        //         await manufactureCostService.Reload(catalogData.ToList());
        //     }
        // );
```

**Verify:** `dotnet build backend/src/Anela.Heblo.Application/` succeeds.
