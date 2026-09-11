### task: swap-giftpackage-service-to-batch-lookup

**Scope:** Replace the per-ingredient `GetCatalogItemAsync` loop in `GiftPackageManufactureService.GetGiftPackageDetailAsync` with a single call to `GetCatalogItemsAsync` (added by the previous task, already merged into `ILogisticsCatalogSource` / `LogisticsCatalogSourceAdapter`), and update `GiftPackageManufactureServiceTests.cs` accordingly. This task assumes `ILogisticsCatalogSource.GetCatalogItemsAsync(IReadOnlyList<string> codes, CancellationToken cancellationToken)` already exists and returns `Task<IReadOnlyDictionary<string, LogisticsCatalogItem>>` (keyed by product code, missing codes simply absent from the dictionary).

#### Current state of the files this task touches

`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs` — the relevant excerpt (current lines 77–140, `GetGiftPackageDetailAsync`, shown in full for context; only lines 111–118 change):

```csharp
    public async Task<GiftPackageDto> GetGiftPackageDetailAsync(string giftPackageCode, decimal salesCoefficient = 1.0m, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default)
    {
        var (actualFromDate, actualToDate, daysDiff) = ResolveDateRange(fromDate, toDate);

        // Get the basic product info from catalog
        var product = await _catalogSource.GetGiftPackageAsync(giftPackageCode, actualFromDate, actualToDate, cancellationToken);

        if (product == null)
        {
            throw new ArgumentException($"Gift package '{giftPackageCode}' not found or is not a set product");
        }

        var (dailySales, suggestedQuantity, severity, stockCoveragePercent) =
            ComputePackageMetrics(product, salesCoefficient, daysDiff);

        // Create the detailed gift package with ingredients
        var giftPackage = new GiftPackageDto
        {
            Code = product.ProductCode,
            Name = product.ProductName,
            AvailableStock = (int)product.AvailableStock,
            DailySales = dailySales,
            OverstockMinimal = product.StockMinSetup,
            OverstockOptimal = product.OptimalStockDaysSetup,
            SuggestedQuantity = suggestedQuantity,
            Severity = severity,
            StockCoveragePercent = stockCoveragePercent,
            Ingredients = new List<GiftPackageIngredientDto>()
        };

        // Load BOM (Bill of Materials) from manufacture repository
        var productParts = await _manufactureClient.GetSetPartsAsync(giftPackageCode, cancellationToken);

        // Map ProductPart objects to GiftPackageIngredientDto with stock data
        var ingredientCodes = productParts.Select(p => p.ProductCode).Distinct().ToList();
        var ingredientCatalog = new Dictionary<string, LogisticsCatalogItem>(StringComparer.Ordinal);
        foreach (var code in ingredientCodes)
        {
            var item = await _catalogSource.GetCatalogItemAsync(code, cancellationToken);
            if (item != null)
                ingredientCatalog[code] = item;
        }

        var ingredients = new List<GiftPackageIngredientDto>();
        foreach (var part in productParts)
        {
            ingredientCatalog.TryGetValue(part.ProductCode, out var ingredientItem);

            var ingredient = new GiftPackageIngredientDto
            {
                ProductCode = part.ProductCode,
                ProductName = part.ProductName,
                RequiredQuantity = part.Amount,
                AvailableStock = (double)(ingredientItem?.AvailableStock ?? 0),
                Image = ingredientItem?.Image
            };

            ingredients.Add(ingredient);
        }

        giftPackage.Ingredients = ingredients;

        return giftPackage;
    }
```

`backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs` currently has 5 tests that set up or verify `_catalogSourceMock` against the single-item `GetCatalogItemAsync` for ingredient resolution (line numbers as currently in the file):
- `GetGiftPackageDetailAsync_ShouldReturnGiftPackageWithIngredients` (lines 101–139) — two per-code `Setup(x => x.GetCatalogItemAsync("ING001", ...))` / `("ING002", ...)` calls.
- `CreateManufactureAsync_ShouldCreateManufactureLogWithConsumedItems` (lines 156–219) — same two per-code setups.
- `GetGiftPackageDetailAsync_WithCustomDateRange_ShouldUseSpecifiedDates` (lines 272–300) — one `Setup(x => x.GetCatalogItemAsync(It.IsAny<string>(), ...))` with a callback returning a synthesized item per code.
- `GetGiftPackageDetailAsync_CallsGetCatalogItemAsyncPerIngredient` (lines 302–327) — sets up the callback-based single-item mock and asserts `Times.Exactly(2)`. This test specifically encodes the N+1 behavior being removed and must be renamed/rewritten to assert `Times.Once` on the new batch method.
- `GetGiftPackageDetailAsync_MissingIngredientInCatalog_ReturnsZeroStockAndNullImage` (lines 329–353) — sets up `GetCatalogItemAsync(It.IsAny<string>(), ...)` to always return `null`.

#### Step 1 — Update the call site (write the change, this is the "red→green" step since existing tests currently mock the old method)

First, run the current test suite to confirm the baseline (all currently green before this task's edits, using the old single-item mocks):

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureServiceTests"
```

Expected: all tests pass (baseline, pre-change).

Now edit `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs`. Replace this block (current lines 110–118):

```csharp
        // Map ProductPart objects to GiftPackageIngredientDto with stock data
        var ingredientCodes = productParts.Select(p => p.ProductCode).Distinct().ToList();
        var ingredientCatalog = new Dictionary<string, LogisticsCatalogItem>(StringComparer.Ordinal);
        foreach (var code in ingredientCodes)
        {
            var item = await _catalogSource.GetCatalogItemAsync(code, cancellationToken);
            if (item != null)
                ingredientCatalog[code] = item;
        }
```

with:

```csharp
        // Map ProductPart objects to GiftPackageIngredientDto with stock data
        var ingredientCodes = productParts.Select(p => p.ProductCode).Distinct().ToList();
        var ingredientCatalog = await _catalogSource.GetCatalogItemsAsync(ingredientCodes, cancellationToken);
```

Nothing else in the file changes — the `using` list, class declaration, constructor, and every other method (`GetAvailableGiftPackagesAsync`, `CreateManufactureAsync`, `DisassembleGiftPackageAsync`, `ResolveDateRange`, `ComputePackageMetrics`, `CalculateSeverity`, `CalculateStockCoveragePercent`) are untouched. The downstream `foreach (var part in productParts)` loop that builds `GiftPackageIngredientDto` via `ingredientCatalog.TryGetValue(...)` is untouched — it works identically against the dictionary returned by `GetCatalogItemsAsync`.

Build to confirm compile success:

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build succeeds (the type of `ingredientCatalog` is now inferred as `IReadOnlyDictionary<string, LogisticsCatalogItem>`, which supports `TryGetValue` identically to `Dictionary<string, LogisticsCatalogItem>`).

Run the existing test suite again — this is expected to now **fail**, because the tests still mock the old single-item method which is no longer called:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureServiceTests"
```

Expected failures: `GetGiftPackageDetailAsync_ShouldReturnGiftPackageWithIngredients`, `CreateManufactureAsync_ShouldCreateManufactureLogWithConsumedItems`, `GetGiftPackageDetailAsync_WithCustomDateRange_ShouldUseSpecifiedDates`, `GetGiftPackageDetailAsync_CallsGetCatalogItemAsyncPerIngredient` (assertion `Times.Exactly(2)` now fails — the mocked `GetCatalogItemAsync` is never called), and `GetGiftPackageDetailAsync_MissingIngredientInCatalog_ReturnsZeroStockAndNullImage` (ingredients resolve to `AvailableStock = 0` regardless of the mock, since the unmocked `GetCatalogItemsAsync` on the `Mock<ILogisticsCatalogSource>` returns `null`/default by default rather than the intended dictionary, which will surface as either a `NullReferenceException` inside `TryGetValue` or an assertion mismatch depending on Moq's default-value behavior for the interface method). This confirms the tests are red because of the mock mismatch introduced by this task's edit, as expected before the next step fixes them.

#### Step 2 — Update the test mocks and assertions to match the new call pattern

Edit `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs`.

**2a.** In `GetGiftPackageDetailAsync_ShouldReturnGiftPackageWithIngredients` (current lines 101–139), replace:

```csharp
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemAsync("ING001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogisticsCatalogItem { ProductCode = "ING001", AvailableStock = 100m });
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemAsync("ING002", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogisticsCatalogItem { ProductCode = "ING002", AvailableStock = 75m });
```

with:

```csharp
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, LogisticsCatalogItem>
            {
                ["ING001"] = new LogisticsCatalogItem { ProductCode = "ING001", AvailableStock = 100m },
                ["ING002"] = new LogisticsCatalogItem { ProductCode = "ING002", AvailableStock = 75m },
            });
```

**2b.** In `CreateManufactureAsync_ShouldCreateManufactureLogWithConsumedItems` (current lines 156–219), replace the identical block:

```csharp
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemAsync("ING001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogisticsCatalogItem { ProductCode = "ING001", AvailableStock = 100m });
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemAsync("ING002", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogisticsCatalogItem { ProductCode = "ING002", AvailableStock = 75m });
```

with the same replacement:

```csharp
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, LogisticsCatalogItem>
            {
                ["ING001"] = new LogisticsCatalogItem { ProductCode = "ING001", AvailableStock = 100m },
                ["ING002"] = new LogisticsCatalogItem { ProductCode = "ING002", AvailableStock = 75m },
            });
```

**2c.** In `GetGiftPackageDetailAsync_WithCustomDateRange_ShouldUseSpecifiedDates` (current lines 272–300), replace:

```csharp
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string code, CancellationToken _) =>
                new LogisticsCatalogItem { ProductCode = code, AvailableStock = 50m });
```

with:

```csharp
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> codes, CancellationToken _) =>
                (IReadOnlyDictionary<string, LogisticsCatalogItem>)codes.ToDictionary(
                    code => code,
                    code => new LogisticsCatalogItem { ProductCode = code, AvailableStock = 50m }));
```

**2d.** Replace the whole `GetGiftPackageDetailAsync_CallsGetCatalogItemAsyncPerIngredient` test (current lines 302–327):

```csharp
    [Fact]
    public async Task GetGiftPackageDetailAsync_CallsGetCatalogItemAsyncPerIngredient()
    {
        // Arrange
        var giftPackageCode = "SET001";
        var product = CreateGiftPackageItem(giftPackageCode, "Test Gift Set 1", 100, 50);

        _catalogSourceMock
            .Setup(x => x.GetGiftPackageAsync(giftPackageCode, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _manufactureClientMock
            .Setup(x => x.GetSetPartsAsync(giftPackageCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestProductParts());
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string code, CancellationToken _) =>
                new LogisticsCatalogItem { ProductCode = code, AvailableStock = 50m });

        // Act
        var result = await _service.GetGiftPackageDetailAsync(giftPackageCode);

        // Assert
        result.Should().NotBeNull();
        result.Ingredients.Should().HaveCount(2);
        _catalogSourceMock.Verify(x => x.GetCatalogItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
```

with:

```csharp
    [Fact]
    public async Task GetGiftPackageDetailAsync_CallsGetCatalogItemsAsyncOncePerInvocation()
    {
        // Arrange
        var giftPackageCode = "SET001";
        var product = CreateGiftPackageItem(giftPackageCode, "Test Gift Set 1", 100, 50);

        _catalogSourceMock
            .Setup(x => x.GetGiftPackageAsync(giftPackageCode, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _manufactureClientMock
            .Setup(x => x.GetSetPartsAsync(giftPackageCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestProductParts());
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> codes, CancellationToken _) =>
                (IReadOnlyDictionary<string, LogisticsCatalogItem>)codes.ToDictionary(
                    code => code,
                    code => new LogisticsCatalogItem { ProductCode = code, AvailableStock = 50m }));

        // Act
        var result = await _service.GetGiftPackageDetailAsync(giftPackageCode);

        // Assert
        result.Should().NotBeNull();
        result.Ingredients.Should().HaveCount(2);
        _catalogSourceMock.Verify(x => x.GetCatalogItemsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        _catalogSourceMock.Verify(x => x.GetCatalogItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

(Note the added `Times.Never` check on the old single-item method, directly encoding FR-4's acceptance criterion that ingredient resolution no longer calls it.)

**2e.** Replace the `GetGiftPackageDetailAsync_MissingIngredientInCatalog_ReturnsZeroStockAndNullImage` test (current lines 329–353):

```csharp
    [Fact]
    public async Task GetGiftPackageDetailAsync_MissingIngredientInCatalog_ReturnsZeroStockAndNullImage()
    {
        // Arrange
        var giftPackageCode = "SET001";
        var product = CreateGiftPackageItem(giftPackageCode, "Test Gift Set 1", 100, 50);

        _catalogSourceMock
            .Setup(x => x.GetGiftPackageAsync(giftPackageCode, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _manufactureClientMock
            .Setup(x => x.GetSetPartsAsync(giftPackageCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestProductParts());
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LogisticsCatalogItem?)null);

        // Act
        var result = await _service.GetGiftPackageDetailAsync(giftPackageCode);

        // Assert — missing ingredients get zero stock and null image
        result.Should().NotBeNull();
        result.Ingredients.Should().HaveCount(2);
        result.Ingredients.Should().OnlyContain(i => i.AvailableStock == 0.0 && i.Image == null);
    }
```

with:

```csharp
    [Fact]
    public async Task GetGiftPackageDetailAsync_MissingIngredientInCatalog_ReturnsZeroStockAndNullImage()
    {
        // Arrange
        var giftPackageCode = "SET001";
        var product = CreateGiftPackageItem(giftPackageCode, "Test Gift Set 1", 100, 50);

        _catalogSourceMock
            .Setup(x => x.GetGiftPackageAsync(giftPackageCode, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _manufactureClientMock
            .Setup(x => x.GetSetPartsAsync(giftPackageCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestProductParts());
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, LogisticsCatalogItem>)new Dictionary<string, LogisticsCatalogItem>());

        // Act
        var result = await _service.GetGiftPackageDetailAsync(giftPackageCode);

        // Assert — missing ingredients get zero stock and null image
        result.Should().NotBeNull();
        result.Ingredients.Should().HaveCount(2);
        result.Ingredients.Should().OnlyContain(i => i.AvailableStock == 0.0 && i.Image == null);
    }
```

(`CreateTestProductParts()` returns two ingredients, `ING001` and `ING002` — see the existing helper at the bottom of the file, unchanged. An empty dictionary from `GetCatalogItemsAsync` means both are absent, matching the previous "single-item mock always returns null" behavior via `TryGetValue`'s false path.)

No other tests in this file reference `GetCatalogItemAsync`/`GetCatalogItemsAsync` for ingredient resolution — `GetAvailableGiftPackagesAsync_*` tests only mock `GetGiftPackageSetsAsync` and are unaffected; `GetGiftPackageDetailAsync_WithNonExistentProduct_ShouldThrowArgumentException` never reaches the ingredient-resolution code path (it throws before `GetSetPartsAsync` is called) and is unaffected.

Run the test suite again:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureServiceTests"
```

Expected: all tests pass (green).

#### Step 3 — Full solution verification

```bash
cd backend && dotnet build
cd backend && dotnet format --verify-no-changes
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: solution builds with no errors, `dotnet format` reports no changes needed, and the entire `Anela.Heblo.Tests` suite passes (this also re-confirms the `LogisticsCatalogSourceAdapterTests` additions from the previous task still pass alongside this task's changes, and confirms `GetTransportBoxByCodeHandler` — untouched, still calling the single-item `GetCatalogItemAsync` — continues to compile and its own tests, if any, are unaffected).

If `dotnet format` reports changes, apply them:

```bash
cd backend && dotnet format
```

then re-run `dotnet build` and the test suite to confirm nothing broke.

#### Step 4 — Commit

```bash
cd backend && git add \
  src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs \
  test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs
git commit -m "Replace per-ingredient catalog lookup with batch call in GiftPackageManufactureService

GetGiftPackageDetailAsync now resolves ingredient catalog data with a
single GetCatalogItemsAsync call instead of one GetCatalogItemAsync
call per distinct ingredient code, closing the N+1 pattern that also
affected CreateManufactureAsync and DisassembleGiftPackageAsync (both
call GetGiftPackageDetailAsync internally). Behavior for callers is
unchanged, including the existing 'missing ingredient -> zero stock,
null image' fallback.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01S92jEwnTs5hzRKfucYBmHw"
```

---

## Self-Review

Checked against the spec (`spec.r1.md`), arch review, and design documents:

- **FR-1** (add batch method to `ILogisticsCatalogSource`) — covered by `add-batch-catalog-lookup-method`, Step 2. Signature matches exactly: `Task<IReadOnlyDictionary<string, LogisticsCatalogItem>> GetCatalogItemsAsync(IReadOnlyList<string> codes, CancellationToken cancellationToken)`.
- **FR-2** (implement in `LogisticsCatalogSourceAdapter`) — covered by `add-batch-catalog-lookup-method`, Step 3. Delegates to `_catalogRepository.GetByIdsAsync` exactly once per call, reuses the existing private `ToCatalogItem`, mirrors `CatalogPackingProductSourceAdapter.GetByCodesAsync`'s `.ToDictionary` style as the arch review permits. Empty-input and missing-code behavior verified by the three new adapter tests in Step 1/3.
- **FR-3** (replace the loop in `GetGiftPackageDetailAsync`) — covered by `swap-giftpackage-service-to-batch-lookup`, Step 1. The exact before/after diff matches the spec's proposed replacement verbatim. The downstream `foreach` loop is explicitly left untouched.
- **FR-4** (update existing unit tests) — covered by `swap-giftpackage-service-to-batch-lookup`, Step 2, sub-steps 2a–2e. All five call sites that referenced `GetCatalogItemAsync` for ingredient resolution are updated; the renamed `GetGiftPackageDetailAsync_CallsGetCatalogItemsAsyncOncePerInvocation` test asserts `Times.Once` on the batch method and `Times.Never` on the single-item method; the "missing ingredient" test is preserved with an empty-dictionary mock.
- **NFR-1** (O(N) → O(1) round-trips) — structurally guaranteed by the Step 1 replacement: exactly one `GetCatalogItemsAsync` call replaces the `foreach` loop, and this is asserted by the `Times.Once` check in the renamed test.
- **NFR-2** (security — no change) — no new inputs, auth surface, or data exposure introduced by either task; not applicable to add further test coverage for.
- **Out of scope items** (`GetTransportBoxByCodeHandler`, `GetCatalogItemAsync` removal, `ICatalogRepository`/`CatalogAggregate` changes, benchmarking, `IManufactureClient` batching) — none of these files are touched by either task; explicitly called out in both tasks' scope notes.

Placeholder / hand-waving scan: every code block in both tasks is complete, compilable C# with no `TODO`, `...`, or "similar to above" references — each task restates the full current file content it edits and the full new content, so a developer reading only one task's section has everything needed. Type and method names are consistent across both tasks and match the interface signature exactly (`GetCatalogItemsAsync`, `IReadOnlyList<string> codes`, `CancellationToken cancellationToken`, `IReadOnlyDictionary<string, LogisticsCatalogItem>`).

Cross-task consistency check: Task 1 introduces `GetCatalogItemsAsync` on `ILogisticsCatalogSource`/`LogisticsCatalogSourceAdapter`; Task 2 consumes that exact signature at the call site and in test mocks (`It.IsAny<IReadOnlyList<string>>()`, `IReadOnlyDictionary<string, LogisticsCatalogItem>` return type) — no drift between the two tasks' assumed API shape.

No issues found requiring further correction.
