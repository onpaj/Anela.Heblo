### task: wrap-disassemble-gift-package-async-in-transaction

**Context:** `DisassembleGiftPackageAsync` (same file as the previous task) already validates quantity and fetches gift-package detail *before* creating the log, so — unlike `CreateManufactureAsync` — no reordering is needed here (FR-2). The transaction must wrap: log creation + save, the package stock-down operation, and the per-component stock-up loop.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs`

- [ ] **Step 1: Wrap `DisassembleGiftPackageAsync`**

Find the tail of the method, starting right after the stock-availability validation:

```csharp
        // 2. Create log entry with OperationType.Disassembly
        var disassemblyLog = new GiftPackageManufactureLog(
            giftPackageCode,
            quantity,
            _timeProvider.GetUtcNow().DateTime,
            userName,
            GiftPackageOperationType.Disassembly);

        // CRITICAL: Save the log FIRST to get the ID for DocumentNumber
        await _giftPackageRepository.AddAsync(disassemblyLog);
        await _giftPackageRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created GiftPackageDisassemblyLog {LogId} for {GiftPackageCode}, quantity: {Quantity}",
            disassemblyLog.Id, giftPackageCode, quantity);

        // 3. Stock-DOWN for finished product (negative amount)
        var packageDocNumber = $"GPD-{disassemblyLog.Id:000000}-{giftPackageCode}";

        _logger.LogDebug("Creating stock-down operation for package: {DocumentNumber} - {ProductCode}, Amount: {Amount}",
            packageDocNumber, giftPackageCode, -quantity);

        await _stockOperationService.CreateOperationAsync(
            packageDocNumber,
            giftPackageCode,
            -quantity,  // Negative = removal from stock
            LogisticsStockOperationSource.GiftPackageManufacture,
            disassemblyLog.Id,
            cancellationToken);

        // 4. Stock-UP for each component (positive amounts)
        var returnedComponents = new List<GiftPackageDisassemblyItemDto>();

        foreach (var ingredient in giftPackage.Ingredients ?? new List<GiftPackageIngredientDto>())
        {
            var returnedQuantity = (int)(ingredient.RequiredQuantity * quantity);
            disassemblyLog.AddConsumedItem(ingredient.ProductCode, returnedQuantity);

            // DocumentNumber format: GPD-{logId:000000}-{productCode}
            var documentNumber = $"GPD-{disassemblyLog.Id:000000}-{ingredient.ProductCode}";

            _logger.LogDebug("Creating stock-up operation for component: {DocumentNumber} - {ProductCode}, Amount: {Amount}",
                documentNumber, ingredient.ProductCode, returnedQuantity);

            await _stockOperationService.CreateOperationAsync(
                documentNumber,
                ingredient.ProductCode,
                returnedQuantity,  // Positive = return to stock
                LogisticsStockOperationSource.GiftPackageManufacture,
                disassemblyLog.Id,
                cancellationToken);

            returnedComponents.Add(new GiftPackageDisassemblyItemDto
            {
                ProductCode = ingredient.ProductCode,
                QuantityReturned = returnedQuantity
            });
        }

        _logger.LogInformation("Successfully completed GiftPackageDisassembly {LogId} for {GiftPackageCode}",
            disassemblyLog.Id, giftPackageCode);

        return new GiftPackageDisassemblyDto
        {
            GiftPackageCode = giftPackageCode,
            QuantityDisassembled = quantity,
            DisassembledAt = disassemblyLog.CreatedAt,
            DisassembledBy = disassemblyLog.CreatedBy,
            ReturnedComponents = returnedComponents
        };
    }
```

Replace it with:

```csharp
        // Cross-repository note: see CreateManufactureAsync above — same shared ApplicationDbContext
        // caveat applies to this call site.
        return await _giftPackageRepository.ExecuteInTransactionAsync(async ct =>
        {
            // 2. Create log entry with OperationType.Disassembly
            var disassemblyLog = new GiftPackageManufactureLog(
                giftPackageCode,
                quantity,
                _timeProvider.GetUtcNow().DateTime,
                userName,
                GiftPackageOperationType.Disassembly);

            // CRITICAL: Save the log FIRST to get the ID for DocumentNumber
            await _giftPackageRepository.AddAsync(disassemblyLog, ct);
            await _giftPackageRepository.SaveChangesAsync(ct);

            _logger.LogInformation("Created GiftPackageDisassemblyLog {LogId} for {GiftPackageCode}, quantity: {Quantity}",
                disassemblyLog.Id, giftPackageCode, quantity);

            // 3. Stock-DOWN for finished product (negative amount)
            var packageDocNumber = $"GPD-{disassemblyLog.Id:000000}-{giftPackageCode}";

            _logger.LogDebug("Creating stock-down operation for package: {DocumentNumber} - {ProductCode}, Amount: {Amount}",
                packageDocNumber, giftPackageCode, -quantity);

            await _stockOperationService.CreateOperationAsync(
                packageDocNumber,
                giftPackageCode,
                -quantity,  // Negative = removal from stock
                LogisticsStockOperationSource.GiftPackageManufacture,
                disassemblyLog.Id,
                ct);

            // 4. Stock-UP for each component (positive amounts)
            var returnedComponents = new List<GiftPackageDisassemblyItemDto>();

            foreach (var ingredient in giftPackage.Ingredients ?? new List<GiftPackageIngredientDto>())
            {
                var returnedQuantity = (int)(ingredient.RequiredQuantity * quantity);
                disassemblyLog.AddConsumedItem(ingredient.ProductCode, returnedQuantity);

                // DocumentNumber format: GPD-{logId:000000}-{productCode}
                var documentNumber = $"GPD-{disassemblyLog.Id:000000}-{ingredient.ProductCode}";

                _logger.LogDebug("Creating stock-up operation for component: {DocumentNumber} - {ProductCode}, Amount: {Amount}",
                    documentNumber, ingredient.ProductCode, returnedQuantity);

                await _stockOperationService.CreateOperationAsync(
                    documentNumber,
                    ingredient.ProductCode,
                    returnedQuantity,  // Positive = return to stock
                    LogisticsStockOperationSource.GiftPackageManufacture,
                    disassemblyLog.Id,
                    ct);

                returnedComponents.Add(new GiftPackageDisassemblyItemDto
                {
                    ProductCode = ingredient.ProductCode,
                    QuantityReturned = returnedQuantity
                });
            }

            _logger.LogInformation("Successfully completed GiftPackageDisassembly {LogId} for {GiftPackageCode}",
                disassemblyLog.Id, giftPackageCode);

            return new GiftPackageDisassemblyDto
            {
                GiftPackageCode = giftPackageCode,
                QuantityDisassembled = quantity,
                DisassembledAt = disassemblyLog.CreatedAt,
                DisassembledBy = disassemblyLog.CreatedBy,
                ReturnedComponents = returnedComponents
            };
        }, cancellationToken);
    }
```

(The `GiftPackageDisassemblyDto` overload of `ExecuteInTransactionAsync` was already stubbed on `_giftPackageRepositoryMock` in the previous task's constructor change, so no existing test needs further changes here — there is currently no test in `GiftPackageManufactureServiceTests.cs` that exercises `DisassembleGiftPackageAsync` through the repository mock; `DisassembleGiftPackageHandlerTests.cs` mocks `IGiftPackageManufactureService` itself and is unaffected by this change.)

- [ ] **Step 2: Add tests locking in that pre-transaction validation still throws before any repository call**

FR-2 requires that `quantity <= 0` and `quantity > AvailableStock` keep throwing before any DB write, exactly as before this change. Since this validation runs entirely before `ExecuteInTransactionAsync` is even called, it is unaffected by the wrap — these tests characterize and lock in that fact. In `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs`, add the following two `[Fact]` methods after `CreateManufactureAsync_ShouldCreateManufactureLogWithConsumedItems` (they reuse the existing private helpers `CreateGiftPackageItem` and `CreateTestProductParts` already defined at the bottom of this class):

```csharp
    [Fact]
    public async Task DisassembleGiftPackageAsync_WithZeroQuantity_ThrowsArgumentExceptionBeforeAnyRepositoryCall()
    {
        await _service.Invoking(x => x.DisassembleGiftPackageAsync("SET001", 0, "tester", CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();

        _giftPackageRepositoryMock.Verify(
            x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<GiftPackageDisassemblyDto>>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DisassembleGiftPackageAsync_WithQuantityExceedingAvailableStock_ThrowsInvalidOperationExceptionBeforeAnyRepositoryCall()
    {
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
            .ReturnsAsync((string code, CancellationToken _) => new LogisticsCatalogItem { ProductCode = code, AvailableStock = 50m });

        await _service.Invoking(x => x.DisassembleGiftPackageAsync(giftPackageCode, 999, "tester", CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();

        _giftPackageRepositoryMock.Verify(
            x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<GiftPackageDisassemblyDto>>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

- [ ] **Step 3: Build and run the full `GiftPackageManufactureServiceTests` class to confirm everything passes**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureServiceTests"
```

Expected: build `0 Error(s)`; tests `Passed!  - Failed: 0, Passed: 12, Skipped: 0, Total: 12`

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs \
        backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs
git commit -m "feat: wrap DisassembleGiftPackageAsync in a single DB transaction (FR-2)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01LkwnHn1TLfFi9jP5okeVwP"
```

---
