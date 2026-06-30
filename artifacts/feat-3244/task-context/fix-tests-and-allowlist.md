### task: fix-tests-and-allowlist

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerFullHistoryTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/FlatManufactureCostProviderTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryCacheOptimizationTests.cs`

#### FR-9: ModuleBoundariesTests.cs — remove 8 allowlist entries

The 8 entries to remove are in `CatalogManufactureAllowlist` (lines 137–146). The block to remove:

```csharp
        // Deliberate pragmatic leak: ManufactureHistoryRecord flows through Catalog's cache layer.
        // All entries below are tracked under the same follow-up: introduce Catalog-owned
        // CatalogManufactureHistoryRecord DTO and map in the ManufactureCatalogSourceAdapter.
        "Anela.Heblo.Application.Features.Catalog.CatalogRepository -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
        "Anela.Heblo.Application.Features.Catalog.Contracts.ICatalogManufactureSource -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
        "Anela.Heblo.Application.Features.Catalog.Infrastructure.CatalogCacheStore -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
        "Anela.Heblo.Application.Features.Catalog.Infrastructure.CatalogDataRefreshService -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
        "Anela.Heblo.Application.Features.Catalog.Infrastructure.CatalogMergeService -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
        // Cost providers in Catalog.CostProviders compute costs from ManufactureHistoryRecord.
        "Anela.Heblo.Application.Features.Catalog.CostProviders.FlatManufactureCostProvider -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
        "Anela.Heblo.Application.Features.Catalog.CostProviders.ManufactureBasedMaterialCostProvider -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
        // GetCatalogDetailHandler maps ManufactureHistoryRecord from CatalogAggregate into response DTOs.
        "Anela.Heblo.Application.Features.Catalog.UseCases.GetCatalogDetail.GetCatalogDetailHandler -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
```

- [ ] Step 1: In `ModuleBoundariesTests.cs`, remove the 8 string entries listed above from `CatalogManufactureAllowlist`, along with their associated comments. The 3 entries for `IManufactureClient` handlers (`UpdateProductCompositionOrderHandler`, `GetProductCompositionHandler`, `GetProductUsageHandler`) and the entries for `ManufactureTemplate`, `Ingredient`, `GetProductUsageResponse` must remain — do not touch them.

After the removal the `CatalogManufactureAllowlist` should retain exactly these entries:
```csharp
    private static readonly HashSet<string> CatalogManufactureAllowlist = new(StringComparer.Ordinal)
    {
        // Follow-up: migrate UpdateProductCompositionOrderHandler off IManufactureClient.
        "Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder.UpdateProductCompositionOrderHandler -> Anela.Heblo.Domain.Features.Manufacture.IManufactureClient",

        // Follow-up: migrate GetProductCompositionHandler off IManufactureClient.
        "Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition.GetProductCompositionHandler -> Anela.Heblo.Domain.Features.Manufacture.IManufactureClient",

        // Follow-up: migrate GetProductUsageHandler off IManufactureClient.
        "Anela.Heblo.Application.Features.Catalog.UseCases.GetProductUsage.GetProductUsageHandler -> Anela.Heblo.Domain.Features.Manufacture.IManufactureClient",

        // GetProductUsageResponse holds ManufactureTemplate in its payload.
        // Follow-up: introduce Catalog-owned ManufactureTemplateDto.
        "Anela.Heblo.Application.Features.Catalog.UseCases.GetProductUsage.GetProductUsageResponse -> Anela.Heblo.Domain.Features.Manufacture.ManufactureTemplate",

        // Handlers reference ManufactureTemplate and Ingredient directly via IManufactureClient.
        // Compiler-generated types (+<>c, +d__N) are covered by the declaring-type check.
        "Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder.UpdateProductCompositionOrderHandler -> Anela.Heblo.Domain.Features.Manufacture.ManufactureTemplate",
        "Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder.UpdateProductCompositionOrderHandler -> Anela.Heblo.Domain.Features.Manufacture.Ingredient",
        "Anela.Heblo.Application.Features.Catalog.UseCases.GetProductUsage.GetProductUsageHandler -> Anela.Heblo.Domain.Features.Manufacture.ManufactureTemplate",
        "Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition.GetProductCompositionHandler -> Anela.Heblo.Domain.Features.Manufacture.ManufactureTemplate",
        "Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition.GetProductCompositionHandler -> Anela.Heblo.Domain.Features.Manufacture.Ingredient",
    };
```

#### FR-10a: GetCatalogDetailHandlerFullHistoryTests.cs

The test file uses `ManufactureHistoryRecord` in two places:
1. Line 9: `using Anela.Heblo.Domain.Features.Manufacture;`
2. Lines 174–194: two `new ManufactureHistoryRecord { ... }` object initializers in `Handle_Should_Exclude_PreFloor_Records_When_MonthsBack_Is_999`

- [ ] Step 2: Replace `using Anela.Heblo.Domain.Features.Manufacture;` (line 9) with `using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;`.

- [ ] Step 3: Replace the two `ManufactureHistoryRecord` instantiations (lines 174–194):
```csharp
// old
        catalogItem.ManufactureHistory = new List<ManufactureHistoryRecord>
        {
            new ManufactureHistoryRecord
            {
                Date = new DateTime(2019, 12, 31),
                Amount = 5,
                PricePerPiece = 3.0M,
                PriceTotal = 15.0M,
                ProductCode = "TEST002",
                DocumentNumber = "MFG-PRE-FLOOR-001"
            },
            new ManufactureHistoryRecord
            {
                Date = new DateTime(2020, 1, 1),
                Amount = 7,
                PricePerPiece = 4.0M,
                PriceTotal = 28.0M,
                ProductCode = "TEST002",
                DocumentNumber = "MFG-AT-FLOOR-001"
            }
        };
// new
        catalogItem.ManufactureHistory = new List<CatalogManufactureRecord>
        {
            new CatalogManufactureRecord
            {
                Date = new DateTime(2019, 12, 31),
                Amount = 5,
                PricePerPiece = 3.0M,
                PriceTotal = 15.0M,
                ProductCode = "TEST002",
                DocumentNumber = "MFG-PRE-FLOOR-001"
            },
            new CatalogManufactureRecord
            {
                Date = new DateTime(2020, 1, 1),
                Amount = 7,
                PricePerPiece = 4.0M,
                PriceTotal = 28.0M,
                ProductCode = "TEST002",
                DocumentNumber = "MFG-AT-FLOOR-001"
            }
        };
```

#### FR-10b: FlatManufactureCostProviderTests.cs

The test file uses `ManufactureHistoryRecord` in multiple places:
- Line 8: `using Anela.Heblo.Domain.Features.Manufacture;`
- Lines 55–60, 122–125, 138–141, 199–200, 246–249, 304–305, 319–323: `new ManufactureHistoryRecord { ... }` inside `ManufactureHistory = new List<ManufactureHistoryRecord> { ... }` assignments

- [ ] Step 4: Replace `using Anela.Heblo.Domain.Features.Manufacture;` (line 8) with `using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;`.

- [ ] Step 5: Replace every `ManufactureHistoryRecord` with `CatalogManufactureRecord` throughout the file. Use a global replace — the type name is not used for anything other than manufacture history records in this file. All occurrences are in `new List<ManufactureHistoryRecord>` and `new ManufactureHistoryRecord` patterns:

Replace all occurrences of:
- `new List<ManufactureHistoryRecord>` → `new List<CatalogManufactureRecord>`
- `new ManufactureHistoryRecord` → `new CatalogManufactureRecord`
- `List<ManufactureHistoryRecord>` → `List<CatalogManufactureRecord>`

#### FR-10c: ManufactureBasedMaterialCostProviderTests.cs

The test file uses `ManufactureHistoryRecord` in these places:
- Line 7: `using Anela.Heblo.Domain.Features.Manufacture;`
- Lines 52–63 in `BuildManufacturedProduct`: `new ManufactureHistoryRecord { ... }` inside `.Select(h => new ManufactureHistoryRecord { ... })`

- [ ] Step 6: Replace `using Anela.Heblo.Domain.Features.Manufacture;` (line 7) with `using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;`.

- [ ] Step 7: Replace the `.Select(h => new ManufactureHistoryRecord { ... })` in `BuildManufacturedProduct` (lines 52–63):
```csharp
// old
        if (history != null)
        {
            agg.ManufactureHistory = history
                .Select(h => new ManufactureHistoryRecord
                {
                    Date = h.date,
                    PricePerPiece = h.pricePerPiece,
                    Amount = h.amount,
                    PriceTotal = h.pricePerPiece * (decimal)h.amount,
                    ProductCode = productCode,
                    DocumentNumber = "TEST-DOC",
                })
                .ToList();
        }
// new
        if (history != null)
        {
            agg.ManufactureHistory = history
                .Select(h => new CatalogManufactureRecord
                {
                    Date = h.date,
                    PricePerPiece = h.pricePerPiece,
                    Amount = h.amount,
                    PriceTotal = h.pricePerPiece * (decimal)h.amount,
                    ProductCode = productCode,
                    DocumentNumber = "TEST-DOC",
                })
                .ToList();
        }
```

#### FR-10d: CatalogRepositoryTests.cs

The test file uses `ManufactureHistoryRecord` in these places:
- Line 16: `using Anela.Heblo.Domain.Features.Manufacture;`
- Lines 79–80: `.ReturnsAsync(new List<ManufactureHistoryRecord>())`

- [ ] Step 8: In `CatalogRepositoryTests.cs`, replace `using Anela.Heblo.Domain.Features.Manufacture;` (line 16) with `using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;`.

- [ ] Step 9: Replace the mock setup return at lines 79–80:
```csharp
// old
        _manufactureSourceMock
            .Setup(x => x.GetManufactureHistoryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureHistoryRecord>());
// new
        _manufactureSourceMock
            .Setup(x => x.GetManufactureHistoryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogManufactureRecord>());
```

Note: After this change, verify `ManufactureDifficultySetting` (used at lines 211–212 in the constructor mock setup in `CatalogRepositoryCacheOptimizationTests.cs`) still resolves — `ManufactureDifficultySetting` is in `Anela.Heblo.Domain.Features.Manufacture` but this test references it through `IManufactureDifficultyRepository` which may have its own using already. If the `using` for the difficulty setting was the only reason the `Manufacture` namespace was imported, a separate using may need to be added for just the difficulty types. Check after build.

#### FR-10e: CatalogRepositoryCacheOptimizationTests.cs

The test file uses `ManufactureHistoryRecord` in these places:
- Line 15: `using Anela.Heblo.Domain.Features.Manufacture;`
- Lines 214–215: `.ReturnsAsync(new List<ManufactureHistoryRecord>())`

- [ ] Step 10: In `CatalogRepositoryCacheOptimizationTests.cs`, replace `using Anela.Heblo.Domain.Features.Manufacture;` (line 15) with `using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;`.

- [ ] Step 11: Replace the mock setup return at lines 214–215:
```csharp
// old
        _manufactureSourceMock.Setup(x => x.GetManufactureHistoryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureHistoryRecord>());
// new
        _manufactureSourceMock.Setup(x => x.GetManufactureHistoryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogManufactureRecord>());
```

Note: `ManufactureDifficultySetting` at line 211 (`_manufactureDifficultyRepositoryMock.Setup(...)`) does not itself reference `ManufactureHistoryRecord` — it is a different type. If `ManufactureDifficultySetting` needs the `Manufacture` namespace, add `using Anela.Heblo.Domain.Features.Manufacture;` back alongside the new `ManufactureHistory` using. Check after build.

#### Final verification

- [ ] Step 12: Build and run all tests:
```
dotnet build backend/ -v quiet
dotnet test backend/test/Anela.Heblo.Tests/ -v normal --filter "FullyQualifiedName~ModuleBoundariesTests|FullyQualifiedName~GetCatalogDetailHandlerFullHistoryTests|FullyQualifiedName~FlatManufactureCostProviderTests|FullyQualifiedName~ManufactureBasedMaterialCostProviderTests|FullyQualifiedName~CatalogRepositoryTests|FullyQualifiedName~CatalogRepositoryCacheOptimizationTests"
```

- [ ] Step 13: Run the full test suite to catch any transitive regressions:
```
dotnet test backend/test/Anela.Heblo.Tests/ -v quiet
```

- [ ] Step 14: Run dotnet format to ensure code style compliance:
```
dotnet format backend/ --verify-no-changes
```
If format reports changes, apply them: `dotnet format backend/`

- [ ] Step 15: Commit all test and allowlist changes:
```
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git add backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerFullHistoryTests.cs
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/FlatManufactureCostProviderTests.cs
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs
git add backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryCacheOptimizationTests.cs
git commit -m "test: update Catalog tests and remove ManufactureHistoryRecord allowlist entries after CatalogManufactureRecord decoupling"
```
