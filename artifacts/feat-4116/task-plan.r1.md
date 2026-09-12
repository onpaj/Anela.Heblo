# Atomic Transaction for Gift Package Manufacture/Disassembly Implementation Plan

**Goal:** Wrap `GiftPackageManufactureService.CreateManufactureAsync` and `DisassembleGiftPackageAsync` in a single database transaction each, so the `GiftPackageManufactureLog` row and every stock operation it produces commit or roll back together — eliminating the current orphaned-log defect where a mid-operation failure leaves a log row with no (or partial) matching stock movements.

**Architecture:** Add `ExecuteInTransactionAsync<TResult>` to the generic `IRepository<TEntity,TKey>` abstraction (Xcc), implement it in `BaseRepository<TEntity,TKey>` using `Context.Database.CreateExecutionStrategy().ExecuteAsync(...)` wrapping `BeginTransactionAsync`/`CommitAsync`/rollback-and-rethrow (required because the DbContext is configured with `PollyExecutionStrategy`, which forbids calling `BeginTransactionAsync` directly), and a no-op pass-through in `EmptyRepository<TEntity,TKey>`. `GiftPackageManufactureService` then wraps each use-case method's log-creation + stock-operation writes in one call to this new method, with all cross-module reads (`GetGiftPackageDetailAsync`) happening before the transaction opens.

**Tech Stack:** .NET 8, EF Core 8 (Npgsql provider), Polly (via the existing `PollyExecutionStrategy`), xUnit, Moq, FluentAssertions, Testcontainers.PostgreSql (via the existing `PostgresSharedContainerFixture`).

---

### task: add-execute-in-transaction-repository-method

**Context:** This is the foundational change (FR-3). `IRepository<TEntity,TKey>` (in the `Anela.Heblo.Xcc` project) is the generic repository abstraction used across the codebase. Almost every concrete repository derives from `BaseRepository<TEntity,TKey>` (in `Anela.Heblo.Persistence`), which is backed by `ApplicationDbContext`. There is also a vestigial `EmptyRepository<TEntity,TKey>` stub with no production consumers. Adding a new member to `IRepository<TEntity,TKey>` requires every class that implements the interface to implement the new member too, or the solution fails to build.

**IMPORTANT — verified by actually building the solution after adding the interface member:** four **test-only** helper classes implement `IRepository<TEntity,TKey>` directly (via a more specific interface such as `IPackingMaterialRepository : IRepository<PackingMaterial,int>`) without deriving from `BaseRepository<,>`. All four must also get the new member or `dotnet build` fails with `CS0535 ... does not implement interface member 'IRepository<...>.ExecuteInTransactionAsync<TResult>(...)'`:
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs`
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialAllocationRepository.cs`
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetConsumptionHistoryQueryCountTests.cs` (nested private class `CountingRepositoryWrapper`)
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsListQueryCountTests.cs` (nested private class `CountingRepositoryWrapper`)

**Files:**
- Modify: `backend/src/Anela.Heblo.Xcc/Persistance/IRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Repositories/BaseRepository.cs`
- Modify: `backend/src/Anela.Heblo.Xcc/Persistance/EmptyRepository.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialAllocationRepository.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetConsumptionHistoryQueryCountTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsListQueryCountTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Xcc/EmptyRepositoryExecuteInTransactionTests.cs`

- [ ] **Step 1: Add the interface member to `IRepository<TEntity,TKey>`**

In `backend/src/Anela.Heblo.Xcc/Persistance/IRepository.cs`, the current end of the file reads:

```csharp
    // Unit of Work operations
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

Replace it with:

```csharp
    // Unit of Work operations
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside a single all-or-nothing database transaction and
    /// returns its result on success; on any exception the transaction is rolled back and the
    /// original exception is rethrown unchanged. This wraps the entire underlying DbContext, not
    /// just <typeparamref name="TEntity"/> — calling it on one repository also covers writes made
    /// through any other repository sharing the same DbContext instance in the current DI scope
    /// (the same whole-context semantics <see cref="SaveChangesAsync"/> already has).
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Implement it in `BaseRepository<TEntity,TKey>`**

In `backend/src/Anela.Heblo.Persistence/Repositories/BaseRepository.cs`, the current end of the file reads:

```csharp
    public virtual async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await Context.SaveChangesAsync(cancellationToken);
    }
}
```

Replace it with:

```csharp
    public virtual async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await Context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="operation"/> inside a single all-or-nothing database transaction,
    /// opened via <see cref="Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy"/> so it is
    /// safe under a retrying execution strategy (e.g. PollyExecutionStrategy) — EF Core forbids
    /// calling BeginTransactionAsync directly when the configured strategy retries on failure.
    /// Everything the transaction must cover (entity adds, SaveChangesAsync calls) must happen
    /// inside <paramref name="operation"/>, since a retry re-runs the whole delegate from a clean
    /// change tracker. This wraps the entire underlying DbContext, not just <typeparamref name="TEntity"/>
    /// — the same whole-context semantics <see cref="SaveChangesAsync"/> already has.
    /// </summary>
    public virtual async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        var strategy = Context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var result = await operation(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }
}
```

- [ ] **Step 3: Add the no-op pass-through in `EmptyRepository<TEntity,TKey>`**

In `backend/src/Anela.Heblo.Xcc/Persistance/EmptyRepository.cs`, the current end of the file reads:

```csharp
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Return 0 as no changes are saved
        return Task.FromResult(0);
    }
}
```

Replace it with:

```csharp
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Return 0 as no changes are saved
        return Task.FromResult(0);
    }

    /// <summary>
    /// No-op pass-through: invokes <paramref name="operation"/> directly with no real transaction.
    /// Provides no isolation if ever mixed with real repositories in the same logical operation —
    /// it only "succeeds" because it has nothing of its own to roll back.
    /// </summary>
    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
        => operation(cancellationToken);
}
```

- [ ] **Step 4: Fix the four test-only direct `IRepository<,>` implementers**

In `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs`, find:

```csharp
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_saveChangesException != null)
            throw _saveChangesException;
        return Task.FromResult(0);
    }
```

Replace it with:

```csharp
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_saveChangesException != null)
            throw _saveChangesException;
        return Task.FromResult(0);
    }

    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
        => operation(cancellationToken);
```

In `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialAllocationRepository.cs`, find:

```csharp
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(0);
}
```

Replace it with:

```csharp
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(0);

    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
        => operation(cancellationToken);
}
```

In `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetConsumptionHistoryQueryCountTests.cs`, inside the nested `CountingRepositoryWrapper` class, find:

```csharp
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _inner.SaveChangesAsync(cancellationToken);

        public Task<IEnumerable<PackingMaterial>> FindAsync(System.Linq.Expressions.Expression<System.Func<PackingMaterial, bool>> predicate, CancellationToken cancellationToken = default)
            => _inner.FindAsync(predicate, cancellationToken);
```

Replace it with:

```csharp
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _inner.SaveChangesAsync(cancellationToken);

        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default)
            => _inner.ExecuteInTransactionAsync(operation, cancellationToken);

        public Task<IEnumerable<PackingMaterial>> FindAsync(System.Linq.Expressions.Expression<System.Func<PackingMaterial, bool>> predicate, CancellationToken cancellationToken = default)
            => _inner.FindAsync(predicate, cancellationToken);
```

(This delegates to the wrapped real `PackingMaterialRepository` — `_inner` — exactly like every other member on this wrapper.)

In `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsListQueryCountTests.cs`, inside its own nested `CountingRepositoryWrapper` class, find the identical snippet:

```csharp
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _inner.SaveChangesAsync(cancellationToken);

        public Task<IEnumerable<PackingMaterial>> FindAsync(System.Linq.Expressions.Expression<System.Func<PackingMaterial, bool>> predicate, CancellationToken cancellationToken = default)
            => _inner.FindAsync(predicate, cancellationToken);
```

Replace it with:

```csharp
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _inner.SaveChangesAsync(cancellationToken);

        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default)
            => _inner.ExecuteInTransactionAsync(operation, cancellationToken);

        public Task<IEnumerable<PackingMaterial>> FindAsync(System.Linq.Expressions.Expression<System.Func<PackingMaterial, bool>> predicate, CancellationToken cancellationToken = default)
            => _inner.FindAsync(predicate, cancellationToken);
```

- [ ] **Step 5: Build the whole solution and confirm it compiles**

Run (from the repo root):

```bash
dotnet build Anela.Heblo.sln
```

Expected: build output ends with `0 Error(s)` (pre-existing warnings are unrelated and unaffected by this change — do not try to fix them).

- [ ] **Step 6: Write a characterization test for `EmptyRepository.ExecuteInTransactionAsync`**

Create `backend/test/Anela.Heblo.Tests/Xcc/EmptyRepositoryExecuteInTransactionTests.cs`:

```csharp
using Anela.Heblo.Xcc.Domain;
using Anela.Heblo.Xcc.Persistance;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Xcc;

public class EmptyRepositoryExecuteInTransactionTests
{
    private sealed class TestEntity : Entity<int>
    {
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_InvokesOperationAndReturnsItsResult()
    {
        var repository = new EmptyRepository<TestEntity, int>();
        var called = false;

        var result = await repository.ExecuteInTransactionAsync(ct =>
        {
            called = true;
            return Task.FromResult(42);
        });

        called.Should().BeTrue();
        result.Should().Be(42);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_PropagatesOperationExceptionUnchanged()
    {
        var repository = new EmptyRepository<TestEntity, int>();

        Func<Task> act = () => repository.ExecuteInTransactionAsync<int>(ct =>
            throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }
}
```

- [ ] **Step 7: Run the new test**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~EmptyRepositoryExecuteInTransactionTests"
```

Expected: `Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2`

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Xcc/Persistance/IRepository.cs \
        backend/src/Anela.Heblo.Persistence/Repositories/BaseRepository.cs \
        backend/src/Anela.Heblo.Xcc/Persistance/EmptyRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialAllocationRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetConsumptionHistoryQueryCountTests.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsListQueryCountTests.cs \
        backend/test/Anela.Heblo.Tests/Xcc/EmptyRepositoryExecuteInTransactionTests.cs
git commit -m "feat: add ExecuteInTransactionAsync to IRepository abstraction

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01LkwnHn1TLfFi9jP5okeVwP"
```

---

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

### task: add-gift-package-manufacture-atomicity-integration-tests

**Context:** Moq-based unit tests cannot prove real transactional rollback — that requires a real relational database. The codebase's established pattern for this is `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs`, which uses the shared `PostgresSharedContainerFixture` Testcontainers fixture, raw-SQL `CREATE TABLE IF NOT EXISTS` for a minimal schema, and a `SaveChangesInterceptor` that throws on a specific `SaveChangesAsync` call to simulate a mid-write failure — then asserts zero rows survive.

That existing test's `DbContext` is built the bare way (`new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_connectionString)`), which uses EF Core's default, non-retrying execution strategy — one that already permits `BeginTransactionAsync` directly. This new test **must not** copy that detail: FR-3's whole reason to route through `Context.Database.CreateExecutionStrategy().ExecuteAsync(...)` (instead of calling `BeginTransactionAsync` directly) is that the real app is configured with `PollyExecutionStrategy`, which reports `RetriesOnFailure => true` and therefore rejects a bare `BeginTransactionAsync`. If this test's `DbContext` doesn't also configure `PollyExecutionStrategy`, it would never exercise the one behavior (FR-3 acceptance criterion (c)) that `ExecuteInTransactionAsync` exists to satisfy. `PollyExecutionStrategy`'s constructor takes three dependencies, all cheaply constructible outside full DI:

```csharp
public PollyExecutionStrategy(
    ExecutionStrategyDependencies dependencies,     // supplied automatically by the `deps` parameter of npgsql.ExecutionStrategy(deps => ...)
    IDbResiliencePipelineProvider pipelineProvider,  // new DbResiliencePipelineProvider(Options.Create(new DbResilienceOptions()), metrics, NullLogger<DbResiliencePipelineProvider>.Instance)
    DbResilienceMetrics metrics,                     // new DbResilienceMetrics(a trivial IMeterFactory)
    ILogger<PollyExecutionStrategy> logger)          // NullLogger<PollyExecutionStrategy>.Instance
```

The failure-injection exception must be non-transient (per `TransientErrorClassifier.IsTransient`, a plain `InvalidOperationException` with no inner `PostgresException`/`SocketException`/etc. is never treated as transient), otherwise Polly's retry pipeline would retry the whole operation and it could spuriously succeed on a later attempt instead of demonstrating rollback. This mirrors the existing transport-box test's own `ThrowOnFirstSaveInterceptor`, which uses `InvalidOperationException` for exactly this reason.

The child entity's FK to the log is `ManufactureLogId` (not `LogId`) — see `GiftPackageManufactureItem.cs`.

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufacture/GiftPackageManufactureAtomicityIntegrationTests.cs`

- [ ] **Step 1: Create the test file with schema setup, the Polly-configured context helper, and one happy-path test**

Create `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufacture/GiftPackageManufactureAtomicityIntegrationTests.cs`:

```csharp
using System.Diagnostics.Metrics;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.Contracts.Models;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.GiftPackageManufacture;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Catalog.Stock;
using Anela.Heblo.Persistence.Infrastructure.Resilience;
using Anela.Heblo.Persistence.Logistics.GiftPackageManufacture;
using Anela.Heblo.Tests.Common;
using AutoMapper;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.GiftPackageManufacture;

/// <summary>
/// Proves that CreateManufactureAsync/DisassembleGiftPackageAsync commit the
/// GiftPackageManufactureLog row, its GiftPackageManufactureItem children, and every
/// StockUpOperation row together — or none of them — even though several separate
/// SaveChangesAsync calls are involved. Unlike ChangeTransportBoxStateReceiveAtomicityIntegrationTests
/// (whose atomicity comes for free from one SaveChangesAsync call), this feature needs an
/// explicit transaction because the log's DB-generated Id must exist before the stock
/// operations that reference it can be created — so the DbContext here is deliberately built
/// WITH PollyExecutionStrategy configured (unlike that other test), to actually exercise the
/// CreateExecutionStrategy().ExecuteAsync(...) path FR-3 introduces.
/// </summary>
[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class GiftPackageManufactureAtomicityIntegrationTests : IAsyncLifetime
{
    private readonly PostgresSharedContainerFixture _fixture;
    private string _connectionString = null!;

    public GiftPackageManufactureAtomicityIntegrationTests(PostgresSharedContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _connectionString = await _fixture.CreateDatabaseAsync("giftpackage_manufacture_atomicity");

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS public."GiftPackageManufactureLogs" (
                "Id"                     serial NOT NULL PRIMARY KEY,
                "GiftPackageCode"        varchar(50) NOT NULL,
                "QuantityCreated"        integer NOT NULL,
                "StockOverrideApplied"   boolean NOT NULL,
                "CreatedAt"              timestamp without time zone NOT NULL,
                "CreatedBy"              text NOT NULL,
                "OperationType"          integer NOT NULL
            );

            CREATE TABLE IF NOT EXISTS public."GiftPackageManufactureItems" (
                "Id"                serial NOT NULL PRIMARY KEY,
                "ManufactureLogId"  integer NOT NULL REFERENCES public."GiftPackageManufactureLogs" ("Id") ON DELETE CASCADE,
                "ProductCode"       varchar(50) NOT NULL,
                "QuantityConsumed"  integer NOT NULL
            );

            CREATE INDEX IF NOT EXISTS "IX_GiftPackageManufactureLogs_CreatedAt"
                ON public."GiftPackageManufactureLogs" ("CreatedAt");

            CREATE INDEX IF NOT EXISTS "IX_GiftPackageManufactureLogs_GiftPackageCode"
                ON public."GiftPackageManufactureLogs" ("GiftPackageCode");

            CREATE INDEX IF NOT EXISTS "IX_GiftPackageManufactureLogs_OperationType"
                ON public."GiftPackageManufactureLogs" ("OperationType");

            CREATE INDEX IF NOT EXISTS "IX_GiftPackageManufactureItems_ManufactureLogId"
                ON public."GiftPackageManufactureItems" ("ManufactureLogId");

            CREATE INDEX IF NOT EXISTS "IX_GiftPackageManufactureItems_ProductCode"
                ON public."GiftPackageManufactureItems" ("ProductCode");

            CREATE TABLE IF NOT EXISTS public."StockUpOperations" (
                "Id"             serial NOT NULL PRIMARY KEY,
                "DocumentNumber" varchar(100) NOT NULL,
                "ProductCode"    varchar(50)  NOT NULL,
                "Amount"         integer NOT NULL,
                "SourceType"     integer NOT NULL,
                "SourceId"       integer NOT NULL,
                "State"          integer NOT NULL,
                "CreatedAt"      timestamp with time zone NOT NULL,
                "SubmittedAt"    timestamp with time zone NULL,
                "CompletedAt"    timestamp with time zone NULL,
                "ErrorMessage"   varchar(2000) NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_StockUpOperations_DocumentNumber_Unique"
                ON public."StockUpOperations" ("DocumentNumber");
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options.Name, options.Version);
        public void Dispose() { }
    }

    /// <summary>
    /// Builds an ApplicationDbContext with PollyExecutionStrategy configured, exactly like
    /// production (PersistenceModule.AddPersistenceServices) — deliberately NOT the bare
    /// UseNpgsql(connectionString) pattern most other integration tests in this repo use,
    /// because BaseRepository.ExecuteInTransactionAsync's whole reason to route through
    /// CreateExecutionStrategy().ExecuteAsync(...) only gets exercised under a retrying strategy.
    /// </summary>
    private ApplicationDbContext CreateContext(IInterceptor? interceptor = null)
    {
        var metrics = new DbResilienceMetrics(new TestMeterFactory());
        var pipelineProvider = new DbResiliencePipelineProvider(
            Options.Create(new DbResilienceOptions()),
            metrics,
            NullLogger<DbResiliencePipelineProvider>.Instance);

        var builder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString, npgsql =>
            {
                npgsql.ExecutionStrategy(deps =>
                    new PollyExecutionStrategy(
                        deps,
                        pipelineProvider,
                        metrics,
                        NullLogger<PollyExecutionStrategy>.Instance));
            });

        if (interceptor != null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new ApplicationDbContext(builder.Options);
    }

    private static GiftPackageManufactureService CreateService(
        ApplicationDbContext context,
        Mock<IManufactureClient> manufactureClientMock,
        Mock<ILogisticsCatalogSource> catalogSourceMock,
        Mock<IMapper> mapperMock)
    {
        var giftPackageRepository = new GiftPackageManufactureRepository(context);
        var stockUpRepository = new StockUpOperationRepository(context, NullLogger<StockUpOperationRepository>.Instance);
        var stockUpProcessingService = new StockUpProcessingService(
            stockUpRepository, Mock.Of<IEshopStockDomainService>(), NullLogger<StockUpProcessingService>.Instance);
        var stockOperationAdapter = new LogisticsStockOperationAdapter(stockUpProcessingService);

        return new GiftPackageManufactureService(
            manufactureClientMock.Object,
            giftPackageRepository,
            catalogSourceMock.Object,
            stockOperationAdapter,
            mapperMock.Object,
            TimeProvider.System,
            NullLogger<GiftPackageManufactureService>.Instance);
    }

    /// <summary>
    /// Configures a 2-ingredient BOM for "SET001": ING001 (Amount 2.0) and ING002 (Amount 1.5).
    /// </summary>
    private static void SetupTwoIngredientBom(
        Mock<IManufactureClient> manufactureClientMock,
        Mock<ILogisticsCatalogSource> catalogSourceMock,
        string giftPackageCode)
    {
        var product = new LogisticsGiftPackageItem
        {
            ProductCode = giftPackageCode,
            ProductName = "Test Gift Set",
            AvailableStock = 100m,
            TotalSoldInPeriod = 50,
            StockMinSetup = 5,
            OptimalStockDaysSetup = 30,
        };

        catalogSourceMock
            .Setup(x => x.GetGiftPackageAsync(giftPackageCode, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        manufactureClientMock
            .Setup(x => x.GetSetPartsAsync(giftPackageCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPart>
            {
                new ProductPart { ProductCode = "ING001", ProductName = "Ingredient 1", Amount = 2.0 },
                new ProductPart { ProductCode = "ING002", ProductName = "Ingredient 2", Amount = 1.5 },
            });

        catalogSourceMock
            .Setup(x => x.GetCatalogItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string code, CancellationToken _) => new LogisticsCatalogItem { ProductCode = code, AvailableStock = 100m });
    }

    [Fact]
    public async Task CreateManufactureAsync_Succeeds_CommitsLogItemsAndStockOperationsTogether()
    {
        var manufactureClientMock = new Mock<IManufactureClient>();
        var catalogSourceMock = new Mock<ILogisticsCatalogSource>();
        var mapperMock = new Mock<IMapper>();
        SetupTwoIngredientBom(manufactureClientMock, catalogSourceMock, "SET001");
        mapperMock
            .Setup(m => m.Map<GiftPackageManufactureDto>(It.IsAny<GiftPackageManufactureLog>()))
            .Returns((GiftPackageManufactureLog log) => new GiftPackageManufactureDto
            {
                Id = log.Id,
                GiftPackageCode = log.GiftPackageCode,
                QuantityCreated = log.QuantityCreated,
                CreatedBy = log.CreatedBy,
                CreatedAt = log.CreatedAt,
            });

        await using var context = CreateContext();
        var service = CreateService(context, manufactureClientMock, catalogSourceMock, mapperMock);

        var result = await service.CreateManufactureAsync("SET001", 5, false, "tester", CancellationToken.None);

        result.Id.Should().BeGreaterThan(0);

        await using var readContext = CreateContext();
        var logs = await readContext.Set<GiftPackageManufactureLog>()
            .Include(x => x.ConsumedItems)
            .ToListAsync();
        logs.Should().HaveCount(1);
        logs[0].ConsumedItems.Should().HaveCount(2);

        var stockOps = await readContext.Set<StockUpOperation>()
            .Where(op => op.SourceType == StockUpSourceType.GiftPackageManufacture && op.SourceId == logs[0].Id)
            .ToListAsync();
        stockOps.Should().HaveCount(3); // 2 ingredient stock-downs + 1 output stock-up
        stockOps.Should().Contain(op => op.DocumentNumber == $"GPM-{logs[0].Id:000000}-SET001" && op.Amount == 5);
        stockOps.Should().Contain(op => op.DocumentNumber == $"GPM-{logs[0].Id:000000}-ING001" && op.Amount == -10);
        stockOps.Should().Contain(op => op.DocumentNumber == $"GPM-{logs[0].Id:000000}-ING002" && op.Amount == -7);
    }
}
```

- [ ] **Step 2: Run the happy-path test**

Requires Docker running locally (Testcontainers pulls/starts a `postgres:16` container).

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureAtomicityIntegrationTests"
```

Expected: `Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1`

- [ ] **Step 3: Add the failure-injection interceptor and the `CreateManufactureAsync` rollback test**

In the same file, add the interceptor as a new private nested class (placed after `TestMeterFactory`, before `CreateContext`) and the new test method (placed after `CreateManufactureAsync_Succeeds_CommitsLogItemsAndStockOperationsTogether`):

```csharp
    /// <summary>
    /// Throws a non-transient exception on the Nth SavingChangesAsync call. Non-transient (per
    /// TransientErrorClassifier.IsTransient) so PollyExecutionStrategy's retry pipeline does not
    /// mask it by retrying — the same reasoning as the transport-box atomicity test's own
    /// ThrowOnFirstSaveInterceptor, generalized to a configurable call number.
    /// </summary>
    private sealed class ThrowOnNthSaveInterceptor : SaveChangesInterceptor
    {
        private readonly int _failOnCallNumber;
        private int _callCount;

        public ThrowOnNthSaveInterceptor(int failOnCallNumber)
        {
            _failOnCallNumber = failOnCallNumber;
        }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            _callCount++;
            if (_callCount == _failOnCallNumber)
            {
                throw new InvalidOperationException($"Simulated failure on save #{_failOnCallNumber}");
            }

            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            _callCount++;
            if (_callCount == _failOnCallNumber)
            {
                throw new InvalidOperationException($"Simulated failure on save #{_failOnCallNumber}");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
```

```csharp
    [Fact]
    public async Task CreateManufactureAsync_SaveChangesFailsOnFinalOperation_RollsBackLogItemsAndStockOperationsTogether()
    {
        // CreateManufactureAsync issues 4 SaveChangesAsync calls for a 2-ingredient BOM: the log
        // (#1), one per ingredient stock-down (#2, #3), and the output stock-up (#4). Failing on
        // the LAST call proves that writes from calls #1-#3 — already sent to Postgres but not yet
        // committed, since they share one ambient transaction — are rolled back too, not just that
        // the 4th write never landed.
        var manufactureClientMock = new Mock<IManufactureClient>();
        var catalogSourceMock = new Mock<ILogisticsCatalogSource>();
        var mapperMock = new Mock<IMapper>();
        SetupTwoIngredientBom(manufactureClientMock, catalogSourceMock, "SET001");
        mapperMock
            .Setup(m => m.Map<GiftPackageManufactureDto>(It.IsAny<GiftPackageManufactureLog>()))
            .Returns((GiftPackageManufactureLog log) => new GiftPackageManufactureDto { Id = log.Id, GiftPackageCode = log.GiftPackageCode });

        await using var context = CreateContext(new ThrowOnNthSaveInterceptor(failOnCallNumber: 4));
        var service = CreateService(context, manufactureClientMock, catalogSourceMock, mapperMock);

        var ex = await Record.ExceptionAsync(() =>
            service.CreateManufactureAsync("SET001", 5, false, "tester", CancellationToken.None));

        ex.Should().BeOfType<InvalidOperationException>();

        await using var readContext = CreateContext();
        (await readContext.Set<GiftPackageManufactureLog>().CountAsync()).Should().Be(0);
        (await readContext.Set<GiftPackageManufactureItem>().CountAsync()).Should().Be(0);
        (await readContext.Set<StockUpOperation>().CountAsync()).Should().Be(0);
    }
```

- [ ] **Step 4: Run both `CreateManufactureAsync` tests**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureAtomicityIntegrationTests"
```

Expected: `Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2`

- [ ] **Step 5: Add the `DisassembleGiftPackageAsync` happy-path and rollback tests**

Add both methods after `CreateManufactureAsync_SaveChangesFailsOnFinalOperation_RollsBackLogItemsAndStockOperationsTogether`:

```csharp
    [Fact]
    public async Task DisassembleGiftPackageAsync_Succeeds_CommitsLogItemsAndStockOperationsTogether()
    {
        var manufactureClientMock = new Mock<IManufactureClient>();
        var catalogSourceMock = new Mock<ILogisticsCatalogSource>();
        var mapperMock = new Mock<IMapper>();
        SetupTwoIngredientBom(manufactureClientMock, catalogSourceMock, "SET001");

        await using var context = CreateContext();
        var service = CreateService(context, manufactureClientMock, catalogSourceMock, mapperMock);

        var result = await service.DisassembleGiftPackageAsync("SET001", 3, "tester", CancellationToken.None);

        result.ReturnedComponents.Should().HaveCount(2);

        await using var readContext = CreateContext();
        var logs = await readContext.Set<GiftPackageManufactureLog>()
            .Include(x => x.ConsumedItems)
            .Where(x => x.OperationType == GiftPackageOperationType.Disassembly)
            .ToListAsync();
        logs.Should().HaveCount(1);
        logs[0].ConsumedItems.Should().HaveCount(2);

        var stockOps = await readContext.Set<StockUpOperation>()
            .Where(op => op.SourceType == StockUpSourceType.GiftPackageManufacture && op.SourceId == logs[0].Id)
            .ToListAsync();
        stockOps.Should().HaveCount(3); // 1 package stock-down + 2 component stock-ups
        stockOps.Should().Contain(op => op.DocumentNumber == $"GPD-{logs[0].Id:000000}-SET001" && op.Amount == -3);
        stockOps.Should().Contain(op => op.DocumentNumber == $"GPD-{logs[0].Id:000000}-ING001" && op.Amount == 6);
        stockOps.Should().Contain(op => op.DocumentNumber == $"GPD-{logs[0].Id:000000}-ING002" && op.Amount == 4);
    }

    [Fact]
    public async Task DisassembleGiftPackageAsync_SaveChangesFailsOnFinalOperation_RollsBackLogItemsAndStockOperationsTogether()
    {
        // Same 4-SaveChangesAsync shape as CreateManufactureAsync: log (#1), package stock-down
        // (#2), then one stock-up per component (#3, #4). Failing on the last call again proves
        // the earlier, already-sent writes roll back too.
        var manufactureClientMock = new Mock<IManufactureClient>();
        var catalogSourceMock = new Mock<ILogisticsCatalogSource>();
        var mapperMock = new Mock<IMapper>();
        SetupTwoIngredientBom(manufactureClientMock, catalogSourceMock, "SET001");

        await using var context = CreateContext(new ThrowOnNthSaveInterceptor(failOnCallNumber: 4));
        var service = CreateService(context, manufactureClientMock, catalogSourceMock, mapperMock);

        var ex = await Record.ExceptionAsync(() =>
            service.DisassembleGiftPackageAsync("SET001", 3, "tester", CancellationToken.None));

        ex.Should().BeOfType<InvalidOperationException>();

        await using var readContext = CreateContext();
        (await readContext.Set<GiftPackageManufactureLog>()
            .CountAsync(x => x.OperationType == GiftPackageOperationType.Disassembly)).Should().Be(0);
        (await readContext.Set<GiftPackageManufactureItem>().CountAsync()).Should().Be(0);
        (await readContext.Set<StockUpOperation>().CountAsync()).Should().Be(0);
    }
```

- [ ] **Step 6: Run the whole test class**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureAtomicityIntegrationTests"
```

Expected: `Passed!  - Failed: 0, Passed: 4, Skipped: 0, Total: 4`

- [ ] **Step 7: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufacture/GiftPackageManufactureAtomicityIntegrationTests.cs
git commit -m "test: add Postgres integration tests proving gift package manufacture/disassembly atomicity

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01LkwnHn1TLfFi9jP5okeVwP"
```

---

### task: run-full-suite-and-validate-build

**Context:** Final validation pass across everything touched by this feature, per this repo's standard (CLAUDE.md): `dotnet build` + `dotnet format` for backend changes, plus the full test suite.

**Files:** none (validation only).

- [ ] **Step 1: Run every test class touched or added by this feature**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureServiceTests|FullyQualifiedName~GiftPackageManufactureAtomicityIntegrationTests|FullyQualifiedName~EmptyRepositoryExecuteInTransactionTests|FullyQualifiedName~MockPackingMaterialRepository|FullyQualifiedName~GetConsumptionHistoryQueryCountTests|FullyQualifiedName~PackingMaterialsListQueryCountTests"
```

Expected: all pass, `Failed: 0`.

- [ ] **Step 2: Run the full backend test suite**

```bash
cd backend && dotnet test
```

Expected: `Failed: 0` (the pre-existing suite plus every test added in this plan). Note the `GiftPackageManufactureAtomicityIntegrationTests` class requires Docker to be available (Testcontainers); if Docker is unavailable in the execution environment, this is the only class expected to be inconclusive — everything else must still pass.

- [ ] **Step 3: Build the whole solution**

```bash
cd /home/user/worktrees/feature-4116-Arch-Review-Logistics-Manufacture-And-Disassembly && dotnet build Anela.Heblo.sln
```

Expected: `0 Error(s)`.

- [ ] **Step 4: Format the code**

```bash
cd /home/user/worktrees/feature-4116-Arch-Review-Logistics-Manufacture-And-Disassembly && dotnet format
```

Expected: completes without error. If it rewrites any file this plan touched, re-run Step 2 and Step 3 to confirm the reformatted code still builds and passes.

- [ ] **Step 5: Verify formatting is clean**

```bash
cd /home/user/worktrees/feature-4116-Arch-Review-Logistics-Manufacture-And-Disassembly && dotnet format --verify-no-changes
```

Expected: exits successfully with no reported changes needed (if Step 4 already applied formatting).

- [ ] **Step 6: Final commit (only if `dotnet format` changed anything)**

```bash
git add -A
git commit -m "chore: apply dotnet format

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01LkwnHn1TLfFi9jP5okeVwP"
```

If `dotnet format` made no changes, skip this step — there is nothing to commit.
