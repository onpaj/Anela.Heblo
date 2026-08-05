### task: add-persist-immediately-and-idempotency-to-stockup-processing-service

**Goal**

Add a `bool persistImmediately = true` parameter (placed after `CancellationToken`) to
`IStockUpProcessingService.CreateOperationAsync` / `StockUpProcessingService.CreateOperationAsync`,
and add an idempotency pre-check (via the already-existing
`IStockUpOperationRepository.GetByDocumentNumberAsync`) so that creating an operation whose
`DocumentNumber` already exists in the database is a silent no-op instead of an unhandled
unique-constraint violation. When `persistImmediately` is `false`, the new `StockUpOperation` is
staged via `AddAsync` but `SaveChangesAsync` is **not** called — the caller is responsible for a
later `SaveChangesAsync` (this is what makes a downstream commit atomic across multiple staged
changes on the same `ApplicationDbContext`).

This task only touches the Catalog application layer and its tests. No other caller in the
codebase needs any change for this task alone, because `persistImmediately` defaults to `true`
(today's immediate-commit behavior).

**Files to touch**

1. `backend/src/Anela.Heblo.Application/Features/Catalog/Services/IStockUpProcessingService.cs`
2. `backend/src/Anela.Heblo.Application/Features/Catalog/Services/StockUpProcessingService.cs`
3. `backend/test/Anela.Heblo.Tests/Features/Catalog/Stock/StockUpProcessingServiceTests.cs`

**Step 1 — write the failing tests first**

Open `backend/test/Anela.Heblo.Tests/Features/Catalog/Stock/StockUpProcessingServiceTests.cs`. Its
current full content is:

```csharp
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.Catalog.Stock;

public class StockUpProcessingServiceTests
{
    private readonly Mock<IStockUpOperationRepository> _repo = new();
    private readonly Mock<IEshopStockDomainService> _eshop = new();

    private StockUpProcessingService CreateService() =>
        new(_repo.Object, _eshop.Object, NullLogger<StockUpProcessingService>.Instance);

    private static StockUpOperation PendingOperation(string docNumber = "BOX-000001-AKL001") =>
        new(docNumber, "AKL001", 5, StockUpSourceType.TransportBox, 1);

    [Fact]
    public async Task ProcessPendingOperations_SuccessfulSubmit_MarksCompleted()
    {
        // Arrange
        var operation = PendingOperation();
        _repo.Setup(r => r.GetByStateAsync(StockUpOperationState.Pending, default))
             .ReturnsAsync([operation]);
        _eshop.Setup(e => e.StockUpAsync(It.IsAny<StockUpRequest>()))
              .Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        await service.ProcessPendingOperationsAsync();

        // Assert — operation should be Completed after a successful REST call
        operation.State.Should().Be(StockUpOperationState.Completed);
    }


    [Fact]
    public async Task ProcessPendingOperations_StockUpAsyncThrows_MarksAsFailed()
    {
        // Arrange
        var operation = PendingOperation();
        _repo.Setup(r => r.GetByStateAsync(StockUpOperationState.Pending, default))
             .ReturnsAsync([operation]);
        _eshop.Setup(e => e.StockUpAsync(It.IsAny<StockUpRequest>()))
              .ThrowsAsync(new HttpRequestException("Shoptet stock update failed for AKL001: [unknown-product] Product does not exist."));

        var service = CreateService();

        // Act
        await service.ProcessPendingOperationsAsync();

        // Assert
        operation.State.Should().Be(StockUpOperationState.Failed);
        operation.ErrorMessage.Should().Contain("unknown-product");
    }

    [Fact]
    public async Task ProcessPendingOperations_CallsStockUpAsyncAndCompletes()
    {
        // Arrange
        var operation = PendingOperation();
        _repo.Setup(r => r.GetByStateAsync(StockUpOperationState.Pending, default))
             .ReturnsAsync([operation]);
        _eshop.Setup(e => e.StockUpAsync(It.IsAny<StockUpRequest>()))
              .Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        await service.ProcessPendingOperationsAsync();

        // Assert
        operation.State.Should().Be(StockUpOperationState.Completed);
        _eshop.Verify(e => e.StockUpAsync(It.IsAny<StockUpRequest>()), Times.Once);
    }
}
```

Add three new `[Fact]` test methods to the end of the class, immediately before the final closing
`}` of the class (i.e. right after `ProcessPendingOperations_CallsStockUpAsyncAndCompletes`'s
closing `}`):

```csharp

    [Fact]
    public async Task CreateOperationAsync_DocumentNumberAlreadyExists_SkipsCreateAndDoesNotSave()
    {
        // Arrange — a prior (possibly interrupted) attempt already created this operation
        var existing = PendingOperation("BOX-000001-AKL001");
        _repo.Setup(r => r.GetByDocumentNumberAsync("BOX-000001-AKL001", It.IsAny<CancellationToken>()))
             .ReturnsAsync(existing);

        var service = CreateService();

        // Act — retrying the same create must be a safe no-op, not a duplicate insert
        await service.CreateOperationAsync("BOX-000001-AKL001", "AKL001", 5, StockUpSourceType.TransportBox, 1);

        // Assert
        _repo.Verify(r => r.AddAsync(It.IsAny<StockUpOperation>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateOperationAsync_DocumentNumberDoesNotExist_PersistImmediatelyDefaultTrue_AddsAndSaves()
    {
        // Arrange
        _repo.Setup(r => r.GetByDocumentNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((StockUpOperation?)null);

        var service = CreateService();

        // Act — persistImmediately omitted, must default to true (today's behavior for
        // existing callers such as GiftPackageManufactureService)
        await service.CreateOperationAsync("BOX-000002-AKL002", "AKL002", 3, StockUpSourceType.TransportBox, 2);

        // Assert
        _repo.Verify(r => r.AddAsync(It.Is<StockUpOperation>(op =>
            op.DocumentNumber == "BOX-000002-AKL002" &&
            op.ProductCode == "AKL002" &&
            op.Amount == 3 &&
            op.SourceType == StockUpSourceType.TransportBox &&
            op.SourceId == 2), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateOperationAsync_PersistImmediatelyFalse_AddsButDoesNotSave()
    {
        // Arrange
        _repo.Setup(r => r.GetByDocumentNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((StockUpOperation?)null);

        var service = CreateService();

        // Act — deferred flush: caller is responsible for a later SaveChangesAsync so this
        // commits atomically together with other pending changes on the same DbContext
        await service.CreateOperationAsync(
            "BOX-000003-AKL003", "AKL003", 2, StockUpSourceType.TransportBox, 3,
            CancellationToken.None, persistImmediately: false);

        // Assert
        _repo.Verify(r => r.AddAsync(It.IsAny<StockUpOperation>(), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
```

**Step 2 — run the new tests and confirm they fail to compile / fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~StockUpProcessingServiceTests"
```

Expected: compile error, because `CreateOperationAsync` does not yet accept a `persistImmediately`
argument and `IStockUpOperationRepository` mock has no matching overload issue — the actual failure
you should see is a C# compiler error (`CS1501: No overload for method 'CreateOperationAsync' takes
7 arguments` for the third new test, and the first two new tests compiling fine but failing at
runtime because `GetByDocumentNumberAsync` is never called by current production code, so the
`AddAsync`/`SaveChangesAsync` Verify(Times.Never) assertions in test 1 will actually incorrectly
pass while test 2's Verify(Times.Once) will fail because `AddAsync` is called with an operation that
was never checked for pre-existence — the important thing is: do not skip this step, actually run it
and observe a failure before proceeding). If step 2 shows only the expected compile error, that is
sufficient confirmation to proceed to Step 3.

**Step 3 — implement the interface change**

Replace the full contents of
`backend/src/Anela.Heblo.Application/Features/Catalog/Services/IStockUpProcessingService.cs`
(currently):

```csharp
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.Services;

public interface IStockUpProcessingService
{
    /// <summary>
    /// Creates a new stock-up operation in Pending state.
    /// Called by handlers/services when they need to schedule a stock-up operation.
    /// </summary>
    Task CreateOperationAsync(
        string documentNumber,
        string productCode,
        int amount,
        StockUpSourceType sourceType,
        int sourceId,
        CancellationToken ct = default);

    /// <summary>
    /// Processes all pending stock-up operations.
    /// Called by background task to submit operations to Shoptet.
    /// </summary>
    Task ProcessPendingOperationsAsync(CancellationToken ct = default);
}
```

with:

```csharp
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.Services;

public interface IStockUpProcessingService
{
    /// <summary>
    /// Creates a new stock-up operation in Pending state.
    /// Called by handlers/services when they need to schedule a stock-up operation.
    /// If a StockUpOperation with the same DocumentNumber already exists, the create is
    /// skipped (idempotent no-op) instead of throwing a unique-constraint violation.
    /// </summary>
    /// <param name="persistImmediately">
    /// When true (default), the new operation is flushed to the database immediately via
    /// SaveChangesAsync, preserving today's behavior for existing callers (e.g.
    /// GiftPackageManufactureService). When false, the operation is only staged on the
    /// shared ApplicationDbContext's change tracker (via AddAsync) and the caller is
    /// responsible for a later SaveChangesAsync call that flushes it together with other
    /// pending changes, as one atomic commit. This parameter is deliberately placed after
    /// CancellationToken (not before) so every existing call site that passes a
    /// CancellationToken positionally as its last argument keeps compiling unchanged and
    /// keeps getting persistImmediately: true. Do not reorder this parameter.
    /// </param>
    Task CreateOperationAsync(
        string documentNumber,
        string productCode,
        int amount,
        StockUpSourceType sourceType,
        int sourceId,
        CancellationToken ct = default,
        bool persistImmediately = true);

    /// <summary>
    /// Processes all pending stock-up operations.
    /// Called by background task to submit operations to Shoptet.
    /// </summary>
    Task ProcessPendingOperationsAsync(CancellationToken ct = default);
}
```

**Step 4 — implement the service change**

In `backend/src/Anela.Heblo.Application/Features/Catalog/Services/StockUpProcessingService.cs`,
replace this method (currently lines 22-42):

```csharp
    public async Task CreateOperationAsync(
        string documentNumber,
        string productCode,
        int amount,
        StockUpSourceType sourceType,
        int sourceId,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Creating StockUpOperation for document {DocumentNumber}, product {ProductCode}, amount {Amount}",
            documentNumber, productCode, amount);

        var operation = new StockUpOperation(documentNumber, productCode, amount, sourceType, sourceId);

        await _repository.AddAsync(operation, ct);
        await _repository.SaveChangesAsync(ct);

        _logger.LogDebug(
            "StockUpOperation created with ID {OperationId} in Pending state",
            operation.Id);
    }
```

with:

```csharp
    public async Task CreateOperationAsync(
        string documentNumber,
        string productCode,
        int amount,
        StockUpSourceType sourceType,
        int sourceId,
        CancellationToken ct = default,
        bool persistImmediately = true)
    {
        var existing = await _repository.GetByDocumentNumberAsync(documentNumber, ct);
        if (existing != null)
        {
            _logger.LogInformation(
                "StockUpOperation {DocumentNumber} already exists (Id={OperationId}, State={State}); skipping duplicate create",
                documentNumber, existing.Id, existing.State);
            return;
        }

        _logger.LogInformation(
            "Creating StockUpOperation for document {DocumentNumber}, product {ProductCode}, amount {Amount}",
            documentNumber, productCode, amount);

        var operation = new StockUpOperation(documentNumber, productCode, amount, sourceType, sourceId);

        await _repository.AddAsync(operation, ct);

        if (persistImmediately)
        {
            await _repository.SaveChangesAsync(ct);
        }

        _logger.LogDebug(
            "StockUpOperation created with ID {OperationId} in Pending state",
            operation.Id);
    }
```

Do not change any other method in this file (`ProcessPendingOperationsAsync`,
`ProcessOperationAsync` stay exactly as they are).

**Step 5 — run the tests again and confirm they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~StockUpProcessingServiceTests"
```

All 6 tests in `StockUpProcessingServiceTests` (the 3 original `ProcessPendingOperations_*` tests
plus the 3 new `CreateOperationAsync_*` tests) must pass.

**Step 6 — build and format check**

```bash
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
```

Both must succeed with no errors and no formatting diffs. (If `dotnet format --verify-no-changes`
reports diffs, run `dotnet format Anela.Heblo.sln` to apply them, then re-run
`--verify-no-changes` to confirm.)

**Step 7 — commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Services/IStockUpProcessingService.cs \
        backend/src/Anela.Heblo.Application/Features/Catalog/Services/StockUpProcessingService.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Stock/StockUpProcessingServiceTests.cs
git commit -m "Add persistImmediately + idempotency pre-check to StockUpProcessingService.CreateOperationAsync"
```

**Acceptance criteria**

- `dotnet build Anela.Heblo.sln` succeeds with no errors.
- `dotnet format Anela.Heblo.sln --verify-no-changes` reports no changes.
- All tests in `StockUpProcessingServiceTests` pass, including the 3 new ones:
  `CreateOperationAsync_DocumentNumberAlreadyExists_SkipsCreateAndDoesNotSave`,
  `CreateOperationAsync_DocumentNumberDoesNotExist_PersistImmediatelyDefaultTrue_AddsAndSaves`,
  `CreateOperationAsync_PersistImmediatelyFalse_AddsButDoesNotSave`.
- `IStockUpProcessingService.CreateOperationAsync` has exactly this signature:
  `Task CreateOperationAsync(string documentNumber, string productCode, int amount, StockUpSourceType sourceType, int sourceId, CancellationToken ct = default, bool persistImmediately = true)`.
- No other file besides the 3 listed above is modified by this task.

---
