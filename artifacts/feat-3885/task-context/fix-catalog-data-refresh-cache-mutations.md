### task: fix-catalog-data-refresh-cache-mutations

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs:197-247`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogDataRefreshServiceTests.cs`

#### Goal
Fix both cache-isolation violations described in the spec (FR-1, FR-2) in a single pass, since they live in the same file, follow the same pattern, and are trivially reviewable together.

#### Context (from spec.r1.md / arch-review.r1.md / design.r1.md)
- `CatalogCacheStore.ReplaceCacheAtomicallyAsync(List<CatalogAggregate>)` promotes current→stale and installs the new list (`CatalogCacheStore.cs:108-129`). It is `async` — callers must `await` it.
- `CatalogCacheStore.SetManufactureDifficultySettingsData(IDictionary<string, List<ManufactureDifficultySetting>>)` installs a new dictionary and runs `InvalidateSourceData`/`SetLoadDateInCache` (`CatalogCacheStore.cs:341-346`).
- `CatalogCacheStore.TryGetCurrent()` returns `List<CatalogAggregate>?` — the live "current" snapshot, or `null` if none exists yet (`CatalogCacheStore.cs:146-147`).
- `CatalogCacheStore.GetCatalogData()` returns `List<CatalogAggregate>` — current if present, else stale, else empty (falls back the same way `Merge()` relies on) (`CatalogCacheStore.cs:70-103`).
- `CatalogAggregate.Clone()` deep-copies via `MemberwiseClone()` plus explicit `ManufactureDifficultySettings = ManufactureDifficultySettings.Clone()` (`CatalogAggregate.cs:310-317`). `ManufactureHistory` is a plain settable list property carried by `MemberwiseClone` (shallow list reference) — safe here because we always **replace** the whole list on the clone (`clone.ManufactureHistory = manufactures.ToList()`), never mutate the list in place.
- `ManufactureDifficultyConfiguration.Assign(List<ManufactureDifficultySetting>, DateTime)` reassigns `Settings`/`ManufactureDifficulty` on the instance it's called on — call it only on a **clone**, never on the shared instance (`ManufactureDifficultyConfiguration.cs:19-23`).

#### Implementation steps

- [ ] **Step 1: Write failing tests for FR-1 (single-product branch does not mutate live state)**

Add to `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogDataRefreshServiceTests.cs`, replacing the existing `RefreshManufactureDifficultySettingsData_SingleProduct_UpdatesLiveAggregate` test (its name describes the bug being fixed) with:

```csharp
[Fact]
public async Task RefreshManufactureDifficultySettingsData_SingleProduct_DoesNotMutateSharedDictionaryOrAggregate()
{
    // Arrange
    var originalSetting = new ManufactureDifficultySetting
    {
        Id = 1,
        ProductCode = "ABC",
        DifficultyValue = 1,
        ValidFrom = DateTime.UtcNow.AddDays(-10)
    };
    _cacheStore.SetManufactureDifficultySettingsData(
        new Dictionary<string, List<ManufactureDifficultySetting>> { ["ABC"] = new List<ManufactureDifficultySetting> { originalSetting } });

    var catalog = new List<CatalogAggregate> { new CatalogAggregate { ProductCode = "ABC" } };
    await _cacheStore.ReplaceCacheAtomicallyAsync(catalog);

    // Snapshot references taken BEFORE the call under test
    var dictBefore = _cacheStore.GetManufactureDifficultySettingsData();
    var aggregateBefore = _cacheStore.TryGetCurrent()!.Single(p => p.ProductCode == "ABC");

    var newSetting = new ManufactureDifficultySetting
    {
        Id = 2,
        ProductCode = "ABC",
        DifficultyValue = 5,
        ValidFrom = DateTime.UtcNow
    };

    var manufactureDifficultyRepoMock = new Mock<IManufactureDifficultyRepository>();
    manufactureDifficultyRepoMock.Setup(r => r.ListAsync("ABC", It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<ManufactureDifficultySetting> { newSetting });

    var service = CreateService(manufactureDifficultyRepo: manufactureDifficultyRepoMock.Object, options: Options.Create(new DataSourceOptions()));

    // Act
    await service.RefreshManufactureDifficultySettingsData("ABC", CancellationToken.None);

    // Assert: pre-call references are untouched (isolation contract)
    dictBefore.Should().ContainKey("ABC");
    dictBefore["ABC"].Should().ContainSingle().Which.Should().Be(originalSetting);
    aggregateBefore.ManufactureDifficultySettings.Settings.Should().ContainSingle().Which.Should().Be(originalSetting);

    // Assert: a freshly-obtained snapshot reflects the update
    var dictAfter = _cacheStore.GetManufactureDifficultySettingsData();
    dictAfter["ABC"].Should().ContainSingle().Which.Should().Be(newSetting);

    var aggregateAfter = _cacheStore.TryGetCurrent()!.Single(p => p.ProductCode == "ABC");
    aggregateAfter.ManufactureDifficultySettings.Settings.Should().ContainSingle().Which.Should().Be(newSetting);
    aggregateAfter.ManufactureDifficultySettings.ManufactureDifficulty.Should().Be(5);

    // Assert: Set*Data plumbing ran (load date updated)
    _cacheStore.GetLoadDateFromCache("CachedManufactureDifficultySettingsData").Should().NotBeNull();
}

[Fact]
public async Task RefreshManufactureDifficultySettingsData_SingleProduct_NoCurrentSnapshot_UpdatesDictionaryWithoutThrowing()
{
    // Arrange - no ReplaceCacheAtomicallyAsync call, so TryGetCurrent() is null
    var newSetting = new ManufactureDifficultySetting { Id = 1, ProductCode = "XYZ", DifficultyValue = 3, ValidFrom = DateTime.UtcNow };
    var manufactureDifficultyRepoMock = new Mock<IManufactureDifficultyRepository>();
    manufactureDifficultyRepoMock.Setup(r => r.ListAsync("XYZ", It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<ManufactureDifficultySetting> { newSetting });

    var service = CreateService(manufactureDifficultyRepo: manufactureDifficultyRepoMock.Object, options: Options.Create(new DataSourceOptions()));

    // Act
    var ex = await Record.ExceptionAsync(() => service.RefreshManufactureDifficultySettingsData("XYZ", CancellationToken.None));

    // Assert
    ex.Should().BeNull();
    _cacheStore.TryGetCurrent().Should().BeNull();
    _cacheStore.GetManufactureDifficultySettingsData()["XYZ"].Should().ContainSingle().Which.Should().Be(newSetting);
}
```

- [ ] **Step 2: Run the new tests to confirm they fail against current code**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CatalogDataRefreshServiceTests"`
Expected: `RefreshManufactureDifficultySettingsData_SingleProduct_DoesNotMutateSharedDictionaryOrAggregate` FAILS (the dictionary/aggregate references taken "before" are mutated by current code, so `dictBefore["ABC"]` and `aggregateBefore.ManufactureDifficultySettings.Settings` will already show the new value). The "no current snapshot" test likely passes already (no isolation being violated there) — that's fine, it's a regression guard for step 4's null-check behavior.

- [ ] **Step 3: Fix `RefreshManufactureDifficultySettingsData` single-product branch**

Replace the `else` branch in `CatalogDataRefreshService.cs` (currently lines ~208-219):

```csharp
        else
        {
            // Single product: copy-then-set the dictionary so InvalidateSourceData/SetLoadDateInCache
            // run through the same Set*Data plumbing every other refresh path uses.
            var existingDict = _cacheStore.GetManufactureDifficultySettingsData();
            var newDict = new Dictionary<string, List<ManufactureDifficultySetting>>(existingDict)
            {
                [product] = difficultySettings.ToList()
            };
            _cacheStore.SetManufactureDifficultySettingsData(newDict);

            // Update the live snapshot, if one exists, by swapping in a clone of the touched
            // product rather than mutating the shared aggregate a concurrent reader may hold.
            var current = _cacheStore.TryGetCurrent();
            var productAggregate = current?.SingleOrDefault(s => s.ProductCode == product);
            if (current != null && productAggregate != null)
            {
                var updated = current.Select(p =>
                {
                    if (p != productAggregate)
                    {
                        return p;
                    }

                    var clone = p.Clone();
                    clone.ManufactureDifficultySettings.Assign(difficultySettings, _timeProvider.GetUtcNow().UtcDateTime);
                    return clone;
                }).ToList();

                await _cacheStore.ReplaceCacheAtomicallyAsync(updated);
            }
        }
```

- [ ] **Step 4: Run the tests again to confirm FR-1 tests pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CatalogDataRefreshServiceTests"`
Expected: PASS for both new tests, and the pre-existing `RefreshManufactureDifficultySettingsData_SingleProduct_UpdatesLiveAggregate` test was removed in Step 1 (superseded), so it no longer runs.

- [ ] **Step 5: Write a failing test for FR-2 (`RefreshManufactureCostData` does not mutate live state)**

Add to the same test file:

```csharp
[Fact]
public async Task RefreshManufactureCostData_DoesNotMutateLiveCatalogAggregates()
{
    // Arrange
    var product = new CatalogAggregate { ProductCode = "P100" };
    var untouchedProduct = new CatalogAggregate { ProductCode = "P200" };
    var catalog = new List<CatalogAggregate> { product, untouchedProduct };
    await _cacheStore.ReplaceCacheAtomicallyAsync(catalog);

    var manufactureHistory = new List<CatalogManufactureRecord>
    {
        new CatalogManufactureRecord { ProductCode = "P100", Date = DateTime.UtcNow, Amount = 3 }
    };
    _cacheStore.SetManufactureHistoryData(manufactureHistory);

    var beforeSnapshot = _cacheStore.TryGetCurrent()!;
    var productBefore = beforeSnapshot.Single(p => p.ProductCode == "P100");

    var service = CreateService(options: Options.Create(new DataSourceOptions()));

    // Act
    await service.RefreshManufactureCostData(CancellationToken.None);

    // Assert: the object referenced before the call is untouched
    productBefore.ManufactureHistory.Should().BeNullOrEmpty();

    // Assert: a fresh snapshot reflects the update, untouched product passed through
    var afterSnapshot = _cacheStore.TryGetCurrent()!;
    afterSnapshot.Single(p => p.ProductCode == "P100").ManufactureHistory.Should().ContainSingle();
    afterSnapshot.Single(p => p.ProductCode == "P200").ManufactureHistory.Should().BeNullOrEmpty();
}
```

Add `using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;` to the test file's usings if `CatalogManufactureRecord` is not already resolvable (check existing usings first — `CatalogDataRefreshService.cs` does not import this namespace directly since it only calls `_cacheStore.GetManufactureHistoryData()`, but the test needs the concrete type to construct records).

- [ ] **Step 6: Run the test to confirm it fails against current code**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RefreshManufactureCostData_DoesNotMutateLiveCatalogAggregates"`
Expected: FAIL — `productBefore.ManufactureHistory` already shows the new entry because current code mutates in place.

- [ ] **Step 7: Fix `RefreshManufactureCostData`**

Replace the method body in `CatalogDataRefreshService.cs` (currently lines ~231-247):

```csharp
    public async Task RefreshManufactureCostData(CancellationToken ct)
    {
        // Add ManufactureHistory data
        var manufactureMap = _cacheStore.GetManufactureHistoryData()
            .GroupBy(p => p.ProductCode)
            .ToDictionary(k => k.Key, v => v.ToList());

        var catalogData = _cacheStore.GetCatalogData();
        var updated = (catalogData ?? []).Select(product =>
        {
            if (!manufactureMap.TryGetValue(product.ProductCode, out var manufactures))
            {
                return product;
            }

            var clone = product.Clone();
            clone.ManufactureHistory = manufactures.ToList();
            return clone;
        }).ToList();

        await _cacheStore.ReplaceCacheAtomicallyAsync(updated);
    }
```

Note: `ct` remains unused (matches the existing signature and every other refresh method's unused-`ct` pattern already present in this class — not introduced by this change).

- [ ] **Step 8: Run the full `CatalogDataRefreshServiceTests` suite to confirm everything passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CatalogDataRefreshServiceTests"`
Expected: All tests PASS, including `RefreshManufactureCostData_DoesNotMutateLiveCatalogAggregates`.

- [ ] **Step 9: Run the full backend test suite for the Catalog area to check for regressions**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Catalog"`
Expected: All PASS. Pay particular attention to `CatalogCacheStoreTests` and `CatalogMergeServiceTests` — neither should need changes since their APIs are untouched.

- [ ] **Step 10: Build and format the whole backend solution**

Run: `cd backend && dotnet build` then `dotnet format`
Expected: build succeeds with no new warnings/errors; `dotnet format` makes no unexpected changes (or only whitespace matching the new code).

- [ ] **Step 11: Commit**

```bash
cd backend
git add src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs \
        test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogDataRefreshServiceTests.cs
git commit -m "fix(catalog): stop CatalogDataRefreshService mutating live cached aggregates in place"
```

#### Acceptance criteria
- All acceptance criteria in `spec.r1.md` FR-1 and FR-2 are met and covered by the tests above.
- `dotnet build` and `dotnet format` succeed with no new warnings.
- `CatalogDataRefreshServiceTests`, `CatalogCacheStoreTests`, and `CatalogMergeServiceTests` all pass unchanged (the latter two require no code changes).
- No public interface (`ICatalogRepository`, `CatalogCacheStore`) changed signature.
