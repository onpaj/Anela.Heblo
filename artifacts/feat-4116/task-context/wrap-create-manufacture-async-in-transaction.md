### task: wrap-create-manufacture-async-in-transaction

**Context:** `GiftPackageManufactureService.CreateManufactureAsync` (in `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs`) currently commits the `GiftPackageManufactureLog` via its own `SaveChangesAsync`, only *then* fetches the BOM/ingredient detail (`GetGiftPackageDetailAsync`, which calls out to `IManufactureClient`/`ILogisticsCatalogSource`), then loops over ingredients calling `_stockOperationService.CreateOperationAsync` once per ingredient, then once more for the output product. If anything after the log's save throws, the log row is left orphaned. This task (FR-1):
1. Moves `GetGiftPackageDetailAsync` to run **before** any transaction opens (it's a cross-module read, not a DB write — the transaction must not hold a connection open across it).
2. Wraps log creation + save + the ingredient loop + the output stock-up in one call to the `ExecuteInTransactionAsync` method added in the previous task.

The task depends on `add-execute-in-transaction-repository-method` having been completed (the `IGiftPackageManufactureRepository` mock used by the existing tests must already support `ExecuteInTransactionAsync`, and `IGiftPackageManufactureRepository : IRepository<GiftPackageManufactureLog,int>` inherits it automatically).

**IMPORTANT — verified empirically:** Moq 4.20.72 does **not** throw a `NullReferenceException` when an unstubbed generic `Task<TResult>`-returning method is called — it returns a completed `Task<TResult>` whose result is `default(TResult)` (i.e. `null` for a reference-type `TResult`), and it does **not** invoke the `Func<CancellationToken, Task<TResult>>` delegate passed to it. Concretely, once `CreateManufactureAsync` is wrapped and the test's `_giftPackageRepositoryMock` has no stub for `ExecuteInTransactionAsync<GiftPackageManufactureDto>`, `CreateManufactureAsync` returns `null`, and the existing test's `result.Should().NotBeNull();` assertion fails with the FluentAssertions message `Expected result not to be <null>.` — not a `NullReferenceException`.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs`

- [ ] **Step 1: Reorder and wrap `CreateManufactureAsync`**

In `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs`, find the entire body of `CreateManufactureAsync` (the method signature/attribute stay the same — only the body changes):

```csharp
    {
        // Create the manufacture log
        var manufactureLog = new GiftPackageManufactureLog(
            giftPackageCode,
            quantity,
            allowStockOverride,
            _timeProvider.GetUtcNow().DateTime,
            userName);

        // CRITICAL: Save the log FIRST to get the ID for DocumentNumber
        await _giftPackageRepository.AddAsync(manufactureLog);
        await _giftPackageRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created GiftPackageManufactureLog {LogId} for {GiftPackageCode}, quantity: {Quantity}",
            manufactureLog.Id, giftPackageCode, quantity);

        // Add consumed items - get detailed info with ingredients
        var giftPackage = await GetGiftPackageDetailAsync(giftPackageCode, 1.0m, null, null, cancellationToken);

        // Stock-up for each ingredient (negative amounts = consumption)
        foreach (var ingredient in giftPackage.Ingredients ?? new List<GiftPackageIngredientDto>())
        {
            var consumedQuantity = (int)(ingredient.RequiredQuantity * quantity);
            manufactureLog.AddConsumedItem(ingredient.ProductCode, consumedQuantity);

            // DocumentNumber format: GPM-{logId:000000}-{productCode}
            var documentNumber = $"GPM-{manufactureLog.Id:000000}-{ingredient.ProductCode}";

            _logger.LogDebug("Creating stock-down operation: {DocumentNumber} - {ProductCode}, Amount: {Amount}",
                documentNumber, ingredient.ProductCode, -consumedQuantity);

            await _stockOperationService.CreateOperationAsync(
                documentNumber,
                ingredient.ProductCode,
                -consumedQuantity,  // Negative = consumption
                LogisticsStockOperationSource.GiftPackageManufacture,
                manufactureLog.Id,
                cancellationToken);

            _logger.LogDebug("Created stock operation: {DocumentNumber}", documentNumber);
        }

        // Stock-up for output product (positive amount = production)
        var outputDocNumber = $"GPM-{manufactureLog.Id:000000}-{giftPackageCode}";

        _logger.LogDebug("Creating output product stock-up operation: {DocumentNumber} - {ProductCode}, Amount: {Amount}",
            outputDocNumber, giftPackageCode, quantity);

        await _stockOperationService.CreateOperationAsync(
            outputDocNumber,
            giftPackageCode,
            quantity,  // Positive = production
            LogisticsStockOperationSource.GiftPackageManufacture,
            manufactureLog.Id,
            cancellationToken);

        _logger.LogInformation("Successfully completed GiftPackageManufacture {LogId} for {GiftPackageCode}",
            manufactureLog.Id, giftPackageCode);

        return _mapper.Map<GiftPackageManufactureDto>(manufactureLog);
    }
```

Replace it with:

```csharp
    {
        // Fetch BOM/ingredient detail BEFORE opening the transaction: this calls out to
        // IManufactureClient/ILogisticsCatalogSource (other modules) and must not hold a
        // DB transaction open across those cross-module reads.
        var giftPackage = await GetGiftPackageDetailAsync(giftPackageCode, 1.0m, null, null, cancellationToken);

        // Cross-repository note: ExecuteInTransactionAsync is opened via _giftPackageRepository,
        // but it also covers writes made through _stockOperationService (IStockUpOperationRepository)
        // because both share the same Scoped ApplicationDbContext in this DI scope. If Phase 2 gives
        // each module its own DbContext, this call site must be re-examined.
        return await _giftPackageRepository.ExecuteInTransactionAsync(async ct =>
        {
            // Create the manufacture log
            var manufactureLog = new GiftPackageManufactureLog(
                giftPackageCode,
                quantity,
                allowStockOverride,
                _timeProvider.GetUtcNow().DateTime,
                userName);

            // CRITICAL: Save the log FIRST to get the ID for DocumentNumber
            await _giftPackageRepository.AddAsync(manufactureLog, ct);
            await _giftPackageRepository.SaveChangesAsync(ct);

            _logger.LogInformation("Created GiftPackageManufactureLog {LogId} for {GiftPackageCode}, quantity: {Quantity}",
                manufactureLog.Id, giftPackageCode, quantity);

            // Stock-up for each ingredient (negative amounts = consumption)
            foreach (var ingredient in giftPackage.Ingredients ?? new List<GiftPackageIngredientDto>())
            {
                var consumedQuantity = (int)(ingredient.RequiredQuantity * quantity);
                manufactureLog.AddConsumedItem(ingredient.ProductCode, consumedQuantity);

                // DocumentNumber format: GPM-{logId:000000}-{productCode}
                var documentNumber = $"GPM-{manufactureLog.Id:000000}-{ingredient.ProductCode}";

                _logger.LogDebug("Creating stock-down operation: {DocumentNumber} - {ProductCode}, Amount: {Amount}",
                    documentNumber, ingredient.ProductCode, -consumedQuantity);

                await _stockOperationService.CreateOperationAsync(
                    documentNumber,
                    ingredient.ProductCode,
                    -consumedQuantity,  // Negative = consumption
                    LogisticsStockOperationSource.GiftPackageManufacture,
                    manufactureLog.Id,
                    ct);

                _logger.LogDebug("Created stock operation: {DocumentNumber}", documentNumber);
            }

            // Stock-up for output product (positive amount = production)
            var outputDocNumber = $"GPM-{manufactureLog.Id:000000}-{giftPackageCode}";

            _logger.LogDebug("Creating output product stock-up operation: {DocumentNumber} - {ProductCode}, Amount: {Amount}",
                outputDocNumber, giftPackageCode, quantity);

            await _stockOperationService.CreateOperationAsync(
                outputDocNumber,
                giftPackageCode,
                quantity,  // Positive = production
                LogisticsStockOperationSource.GiftPackageManufacture,
                manufactureLog.Id,
                ct);

            _logger.LogInformation("Successfully completed GiftPackageManufacture {LogId} for {GiftPackageCode}",
                manufactureLog.Id, giftPackageCode);

            return _mapper.Map<GiftPackageManufactureDto>(manufactureLog);
        }, cancellationToken);
    }
```

- [ ] **Step 2: Run the existing test suite for this class and see the expected failure**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureServiceTests"
```

Expected: `CreateManufactureAsync_ShouldCreateManufactureLogWithConsumedItems` FAILS with message `Expected result not to be <null>.` (from the `result.Should().NotBeNull();` assertion). All other tests in the class still pass (they don't exercise `CreateManufactureAsync`). Total: 10, of which 1 fails.

- [ ] **Step 3: Stub `ExecuteInTransactionAsync` on the repository mock**

In `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs`, find the constructor:

```csharp
    public GiftPackageManufactureServiceTests()
    {
        _manufactureClientMock = new Mock<IManufactureClient>();
        _giftPackageRepositoryMock = new Mock<IGiftPackageManufactureRepository>();
        _catalogSourceMock = new Mock<ILogisticsCatalogSource>();
        _stockOperationServiceMock = new Mock<ILogisticsStockOperationService>();
        _mapperMock = new Mock<IMapper>();
        _timeProviderMock = new Mock<TimeProvider>();
        _loggerMock = new Mock<ILogger<GiftPackageManufactureService>>();

        _timeProviderMock.Setup(x => x.GetUtcNow())
            .Returns(new DateTimeOffset(_testDateTime, TimeSpan.Zero));

        _service = new GiftPackageManufactureService(
            _manufactureClientMock.Object,
            _giftPackageRepositoryMock.Object,
            _catalogSourceMock.Object,
            _stockOperationServiceMock.Object,
            _mapperMock.Object,
            _timeProviderMock.Object,
            _loggerMock.Object);
    }
```

Replace it with:

```csharp
    public GiftPackageManufactureServiceTests()
    {
        _manufactureClientMock = new Mock<IManufactureClient>();
        _giftPackageRepositoryMock = new Mock<IGiftPackageManufactureRepository>();
        _catalogSourceMock = new Mock<ILogisticsCatalogSource>();
        _stockOperationServiceMock = new Mock<ILogisticsStockOperationService>();
        _mapperMock = new Mock<IMapper>();
        _timeProviderMock = new Mock<TimeProvider>();
        _loggerMock = new Mock<ILogger<GiftPackageManufactureService>>();

        _timeProviderMock.Setup(x => x.GetUtcNow())
            .Returns(new DateTimeOffset(_testDateTime, TimeSpan.Zero));

        // CreateManufactureAsync/DisassembleGiftPackageAsync now wrap their writes in
        // _giftPackageRepository.ExecuteInTransactionAsync(...). Moq does not invoke a delegate
        // parameter on an unstubbed call (it returns a completed Task<TResult> with a default
        // result instead), so every return-type overload actually used must be stubbed to run
        // the delegate, or the wrapped method body never executes.
        _giftPackageRepositoryMock
            .Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<GiftPackageManufactureDto>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<GiftPackageManufactureDto>>, CancellationToken>(
                (operation, ct) => operation(ct));

        _giftPackageRepositoryMock
            .Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<GiftPackageDisassemblyDto>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<GiftPackageDisassemblyDto>>, CancellationToken>(
                (operation, ct) => operation(ct));

        _service = new GiftPackageManufactureService(
            _manufactureClientMock.Object,
            _giftPackageRepositoryMock.Object,
            _catalogSourceMock.Object,
            _stockOperationServiceMock.Object,
            _mapperMock.Object,
            _timeProviderMock.Object,
            _loggerMock.Object);
    }
```

(`GiftPackageManufactureDto` and `GiftPackageDisassemblyDto` are both already in scope via the existing `using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;` at the top of the file — no new `using` needed. The `GiftPackageDisassemblyDto` overload is not exercised by any test in this file yet, but stubbing it now means the next task's disassembly wrapping cannot silently regress this file.)

- [ ] **Step 4: Run the tests again and confirm they all pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureServiceTests"
```

Expected: `Passed!  - Failed: 0, Passed: 10, Skipped: 0, Total: 10`

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs \
        backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs
git commit -m "feat: wrap CreateManufactureAsync in a single DB transaction (FR-1)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01LkwnHn1TLfFi9jP5okeVwP"
```

---
