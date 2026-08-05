# Implementation Plan: Atomic and idempotent TransportBox Receive

## Problem being fixed

`ChangeTransportBoxStateHandler.HandleReceived` (Logistics) creates one `StockUpOperation` per
distinct product via `ILogisticsStockOperationService.CreateOperationAsync`, and that service
currently calls `SaveChangesAsync` **immediately** inside the create — a separate, independent
DB commit from the box's own state-transition commit that happens later in `Handle`. If the
process crashes or the box's own `SaveChangesAsync` fails between these two commits, the
`StockUpOperation` rows are already durably committed (and get picked up by the background
Shoptet-sync job, increasing real inventory) while the box never reaches `Received`. A retry then
tries to re-insert the same deterministic `DocumentNumber`s and fails with a unique-constraint
violation, wedging the box permanently.

The fix (already architecturally decided — no open questions): stop calling `SaveChangesAsync`
immediately in the operation-create path for the Receive call, and let it ride along with the box
update's own `SaveChangesAsync` at the end of `Handle` (both repositories share one
`ApplicationDbContext` instance per request scope, confirmed in the architecture review — a single
`SaveChangesAsync` wraps everything staged on the change tracker in one implicit transaction). This
is done via a new `bool persistImmediately = true` parameter, placed **after** `CancellationToken`
(not before), threaded through the whole call chain, defaulting to `true` so the *only* caller that
needs to change is `ChangeTransportBoxStateHandler.HandleReceived` (passing `persistImmediately:
false`). `GiftPackageManufactureService`'s four call sites (a second, unrelated consumer of the same
shared service) omit the parameter entirely and keep getting `true` — zero code changes required
there. Additionally, an idempotency pre-check (`GetByDocumentNumberAsync`, which already exists) is
added inside the shared service so that retrying a Receive whose operations were partially or fully
created in a prior interrupted attempt no longer throws a unique-constraint violation — it silently
skips creating duplicates and lets the box transition proceed.

**Explicit constraint (do not deviate):** the codebase has a CI-enforced script
(`scripts/check-no-managed-tx.sh`) that fails the build if `BeginTransaction`/`UseTransaction`
appear anywhere in `backend/src` — the resilience layer's `PollyExecutionStrategy` is incompatible
with caller-owned transactions. None of the tasks below use an explicit transaction; all atomicity
comes from deferring to a single, already-existing `SaveChangesAsync` call. Do not introduce
`Database.BeginTransactionAsync` anywhere.

There are 3 tasks. They must be done in order — each depends on the interface signature the
previous task introduces:

1. `add-persist-immediately-and-idempotency-to-stockup-processing-service` — introduces the
   `persistImmediately` parameter and the idempotency pre-check on `IStockUpProcessingService` /
   `StockUpProcessingService` (Catalog layer). This is the foundational signature change; it is
   backward compatible on its own (optional parameter, existing callers keep compiling and behaving
   identically) so the build stays green after this task alone.
2. `thread-persist-immediately-through-logistics-stock-operation-contract` — adds the same
   parameter to `ILogisticsStockOperationService` / `LogisticsStockOperationAdapter` (the
   Logistics↔Catalog module-boundary contract), forwarding it unchanged into the Task 1 method.
   Depends on Task 1's new `IStockUpProcessingService.CreateOperationAsync` signature (7 params,
   `bool persistImmediately = true` as the 7th, after `CancellationToken`).
3. `defer-stockup-persist-in-transport-box-receive-and-fix-tests` — the actual bug fix: the one call
   site in `ChangeTransportBoxStateHandler.HandleReceived` passes `persistImmediately: false`, plus
   all now-broken Moq test expectations across the test suite are updated. Depends on Task 2's new
   `ILogisticsStockOperationService.CreateOperationAsync` signature (7 params, same shape).

All commands below assume the working directory is the repository root (where `Anela.Heblo.sln`
lives): `/Users/rem/orca/workspaces/Anela.Heblo/worktrees/feature-3844-Arch-Review-Transportboxes-Receive-Creates-Commits`.

---

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

### task: thread-persist-immediately-through-logistics-stock-operation-contract

**Goal**

Add the same `bool persistImmediately = true` parameter (after `CancellationToken`) to
`ILogisticsStockOperationService.CreateOperationAsync` and its only implementation,
`LogisticsStockOperationAdapter.CreateOperationAsync`, forwarding the value unchanged into
`IStockUpProcessingService.CreateOperationAsync` (whose new signature was introduced in the
previous task, `add-persist-immediately-and-idempotency-to-stockup-processing-service`, and is now:
`Task CreateOperationAsync(string documentNumber, string productCode, int amount, StockUpSourceType sourceType, int sourceId, CancellationToken ct = default, bool persistImmediately = true)`).

This is a pure pass-through change — no new logic, no idempotency check here (that already lives
inside `StockUpProcessingService`, one level down, and applies uniformly regardless of which
caller reaches it through this adapter).

**Files to touch**

1. `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ILogisticsStockOperationService.cs`
2. `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/LogisticsStockOperationAdapter.cs`
3. `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/LogisticsStockOperationAdapterTests.cs`

**Step 1 — write the failing test first**

Open
`backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/LogisticsStockOperationAdapterTests.cs`.
Its current full content is:

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class LogisticsStockOperationAdapterTests
{
    private readonly Mock<IStockUpProcessingService> _service = new();

    private LogisticsStockOperationAdapter CreateAdapter() => new(_service.Object);

    private void SetupServiceReturnsCompleted()
    {
        _service
            .Setup(s => s.CreateOperationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<StockUpSourceType>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task CreateOperationAsync_WithTransportBoxSource_DelegatesToServiceWithCorrectEnum()
    {
        SetupServiceReturnsCompleted();

        await CreateAdapter().CreateOperationAsync(
            "DOC-1", "PROD-1", 5, LogisticsStockOperationSource.TransportBox, 10);

        _service.Verify(s => s.CreateOperationAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            StockUpSourceType.TransportBox,
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateOperationAsync_WithGiftPackageManufactureSource_DelegatesToServiceWithCorrectEnum()
    {
        SetupServiceReturnsCompleted();

        await CreateAdapter().CreateOperationAsync(
            "DOC-1", "PROD-1", 5, LogisticsStockOperationSource.GiftPackageManufacture, 10);

        _service.Verify(s => s.CreateOperationAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            StockUpSourceType.GiftPackageManufacture,
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateOperationAsync_PassesThroughAllParameters()
    {
        var ct = new CancellationToken(false);
        SetupServiceReturnsCompleted();

        await CreateAdapter().CreateOperationAsync(
            "DOC-42", "SET-99", 7, LogisticsStockOperationSource.TransportBox, 55, ct);

        _service.Verify(s => s.CreateOperationAsync(
            "DOC-42",
            "SET-99",
            7,
            StockUpSourceType.TransportBox,
            55,
            ct), Times.Once);
    }

    [Fact]
    public async Task CreateOperationAsync_WithUnknownSource_ThrowsArgumentOutOfRangeException()
    {
        var unknownSource = (LogisticsStockOperationSource)999;

        var act = () => CreateAdapter().CreateOperationAsync(
            "DOC-1", "PROD-1", 1, unknownSource, 0);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
```

Make two kinds of change to this file:

**(a)** Update `SetupServiceReturnsCompleted` and the three existing `Verify` calls that reference
`CreateOperationAsync` on the inner `_service` mock, to add `It.IsAny<bool>()` as a 7th argument (so
they keep matching regardless of what `persistImmediately` value the adapter forwards — this test
file is about enum-mapping and parameter pass-through, not about the `persistImmediately` value
itself, which gets its own dedicated test below).

Replace:

```csharp
    private void SetupServiceReturnsCompleted()
    {
        _service
            .Setup(s => s.CreateOperationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<StockUpSourceType>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }
```

with:

```csharp
    private void SetupServiceReturnsCompleted()
    {
        _service
            .Setup(s => s.CreateOperationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<StockUpSourceType>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .Returns(Task.CompletedTask);
    }
```

Replace (in `CreateOperationAsync_WithTransportBoxSource_DelegatesToServiceWithCorrectEnum`):

```csharp
        _service.Verify(s => s.CreateOperationAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            StockUpSourceType.TransportBox,
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
```

with:

```csharp
        _service.Verify(s => s.CreateOperationAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            StockUpSourceType.TransportBox,
            It.IsAny<int>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<bool>()), Times.Once);
```

Replace (in `CreateOperationAsync_WithGiftPackageManufactureSource_DelegatesToServiceWithCorrectEnum`):

```csharp
        _service.Verify(s => s.CreateOperationAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            StockUpSourceType.GiftPackageManufacture,
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
```

with:

```csharp
        _service.Verify(s => s.CreateOperationAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            StockUpSourceType.GiftPackageManufacture,
            It.IsAny<int>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<bool>()), Times.Once);
```

Replace (in `CreateOperationAsync_PassesThroughAllParameters`):

```csharp
        _service.Verify(s => s.CreateOperationAsync(
            "DOC-42",
            "SET-99",
            7,
            StockUpSourceType.TransportBox,
            55,
            ct), Times.Once);
```

with:

```csharp
        _service.Verify(s => s.CreateOperationAsync(
            "DOC-42",
            "SET-99",
            7,
            StockUpSourceType.TransportBox,
            55,
            ct,
            It.IsAny<bool>()), Times.Once);
```

**(b)** Add a new dedicated test for the `persistImmediately` pass-through, appended right after
`CreateOperationAsync_PassesThroughAllParameters` (and before
`CreateOperationAsync_WithUnknownSource_ThrowsArgumentOutOfRangeException`):

```csharp
    [Fact]
    public async Task CreateOperationAsync_PersistImmediatelyFalse_ForwardsToService()
    {
        SetupServiceReturnsCompleted();

        await CreateAdapter().CreateOperationAsync(
            "DOC-1", "PROD-1", 5, LogisticsStockOperationSource.TransportBox, 10,
            CancellationToken.None, persistImmediately: false);

        _service.Verify(s => s.CreateOperationAsync(
            "DOC-1",
            "PROD-1",
            5,
            StockUpSourceType.TransportBox,
            10,
            It.IsAny<CancellationToken>(),
            false), Times.Once);
    }
```

**Step 2 — run tests and confirm failure**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~LogisticsStockOperationAdapterTests"
```

Expected: compile errors, because neither `ILogisticsStockOperationService.CreateOperationAsync`
nor `LogisticsStockOperationAdapter.CreateOperationAsync` nor the mocked
`IStockUpProcessingService.CreateOperationAsync` calls in this test file accept a 7th `bool`
argument yet on the adapter's own call signature (the mocked `IStockUpProcessingService` already
does, from the previous task, so only the adapter-level calls in the new test and the
`persistImmediately:` named argument will fail to compile).

**Step 3 — implement the interface change**

Replace the full contents of
`backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ILogisticsStockOperationService.cs`
(currently):

```csharp
namespace Anela.Heblo.Application.Features.Logistics.Contracts;

public interface ILogisticsStockOperationService
{
    Task CreateOperationAsync(
        string documentNumber,
        string productCode,
        int amount,
        LogisticsStockOperationSource sourceType,
        int sourceId,
        CancellationToken cancellationToken = default);
}
```

with:

```csharp
namespace Anela.Heblo.Application.Features.Logistics.Contracts;

public interface ILogisticsStockOperationService
{
    /// <param name="persistImmediately">
    /// When true (default), the underlying StockUpOperation is committed to the database
    /// immediately. When false, it is only staged on the shared ApplicationDbContext's
    /// change tracker and flushed later by the caller's own SaveChangesAsync call, so it
    /// commits atomically together with other pending changes in the same request. Placed
    /// after CancellationToken (not before) so existing call sites that pass
    /// cancellationToken positionally as their last argument are unaffected and keep
    /// getting persistImmediately: true. Do not reorder this parameter.
    /// </param>
    Task CreateOperationAsync(
        string documentNumber,
        string productCode,
        int amount,
        LogisticsStockOperationSource sourceType,
        int sourceId,
        CancellationToken cancellationToken = default,
        bool persistImmediately = true);
}
```

**Step 4 — implement the adapter change**

Replace the full contents of
`backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/LogisticsStockOperationAdapter.cs`
(currently):

```csharp
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class LogisticsStockOperationAdapter : ILogisticsStockOperationService
{
    private readonly IStockUpProcessingService _stockUpProcessingService;

    public LogisticsStockOperationAdapter(IStockUpProcessingService stockUpProcessingService)
    {
        _stockUpProcessingService = stockUpProcessingService;
    }

    public Task CreateOperationAsync(
        string documentNumber,
        string productCode,
        int amount,
        LogisticsStockOperationSource sourceType,
        int sourceId,
        CancellationToken cancellationToken = default)
    {
        var mappedSourceType = MapSourceType(sourceType);
        return _stockUpProcessingService.CreateOperationAsync(
            documentNumber,
            productCode,
            amount,
            mappedSourceType,
            sourceId,
            cancellationToken);
    }

    private static StockUpSourceType MapSourceType(LogisticsStockOperationSource sourceType) => sourceType switch
    {
        LogisticsStockOperationSource.TransportBox => StockUpSourceType.TransportBox,
        LogisticsStockOperationSource.GiftPackageManufacture => StockUpSourceType.GiftPackageManufacture,
        _ => throw new ArgumentOutOfRangeException(nameof(sourceType), sourceType, null),
    };
}
```

with:

```csharp
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class LogisticsStockOperationAdapter : ILogisticsStockOperationService
{
    private readonly IStockUpProcessingService _stockUpProcessingService;

    public LogisticsStockOperationAdapter(IStockUpProcessingService stockUpProcessingService)
    {
        _stockUpProcessingService = stockUpProcessingService;
    }

    public Task CreateOperationAsync(
        string documentNumber,
        string productCode,
        int amount,
        LogisticsStockOperationSource sourceType,
        int sourceId,
        CancellationToken cancellationToken = default,
        bool persistImmediately = true)
    {
        var mappedSourceType = MapSourceType(sourceType);
        return _stockUpProcessingService.CreateOperationAsync(
            documentNumber,
            productCode,
            amount,
            mappedSourceType,
            sourceId,
            cancellationToken,
            persistImmediately);
    }

    private static StockUpSourceType MapSourceType(LogisticsStockOperationSource sourceType) => sourceType switch
    {
        LogisticsStockOperationSource.TransportBox => StockUpSourceType.TransportBox,
        LogisticsStockOperationSource.GiftPackageManufacture => StockUpSourceType.GiftPackageManufacture,
        _ => throw new ArgumentOutOfRangeException(nameof(sourceType), sourceType, null),
    };
}
```

**Step 5 — run tests again and confirm they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~LogisticsStockOperationAdapterTests"
```

All 5 tests (4 original + 1 new `CreateOperationAsync_PersistImmediatelyFalse_ForwardsToService`)
must pass.

**Step 6 — build and format check**

```bash
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
```

**Step 7 — commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ILogisticsStockOperationService.cs \
        backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/LogisticsStockOperationAdapter.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/LogisticsStockOperationAdapterTests.cs
git commit -m "Thread persistImmediately through ILogisticsStockOperationService/LogisticsStockOperationAdapter"
```

**Acceptance criteria**

- `dotnet build Anela.Heblo.sln` succeeds with no errors.
- `dotnet format Anela.Heblo.sln --verify-no-changes` reports no changes.
- All 5 tests in `LogisticsStockOperationAdapterTests` pass, including the new
  `CreateOperationAsync_PersistImmediatelyFalse_ForwardsToService`.
- `ILogisticsStockOperationService.CreateOperationAsync` has exactly this signature:
  `Task CreateOperationAsync(string documentNumber, string productCode, int amount, LogisticsStockOperationSource sourceType, int sourceId, CancellationToken cancellationToken = default, bool persistImmediately = true)`.
- `LogisticsStockOperationAdapter.CreateOperationAsync` forwards `persistImmediately` unchanged
  into `_stockUpProcessingService.CreateOperationAsync`.
- No other file besides the 3 listed above is modified by this task.

---

### task: defer-stockup-persist-in-transport-box-receive-and-fix-tests

**Goal**

This is the actual bug fix. Change the single call site inside
`ChangeTransportBoxStateHandler.HandleReceived` to pass `persistImmediately: false`, so that the
`StockUpOperation` inserts it stages are **not** flushed immediately — they ride along with
`Handle`'s existing box-update `SaveChangesAsync` call (unchanged, still at the end of `Handle`),
making the two writes commit as one atomic unit (FR-1). Combined with the idempotency pre-check
added in `add-persist-immediately-and-idempotency-to-stockup-processing-service`, retrying a Receive
whose operations were partially created in a prior interrupted attempt now succeeds instead of
permanently failing on a unique-constraint violation (FR-2).

This task depends on the previous task's new `ILogisticsStockOperationService.CreateOperationAsync`
signature: `Task CreateOperationAsync(string documentNumber, string productCode, int amount, LogisticsStockOperationSource sourceType, int sourceId, CancellationToken cancellationToken = default, bool persistImmediately = true)`.

No control-flow restructuring is needed or wanted: `HandleReceived` already runs, in full, before
`transition.ChangeStateAsync`, `_repository.UpdateAsync(box, cancellationToken)`, and
`_repository.SaveChangesAsync(cancellationToken)` execute later in `Handle` (lines 126, 134-135 of
`ChangeTransportBoxStateHandler.cs`) — do not touch those lines, do not touch `Handle`'s control
flow at all. The only production-code change in this task is the one call site inside
`HandleReceived`.

This task also must fix every Moq `Setup`/`Verify` expression across the test suite that targets
`CreateOperationAsync` and omits the new 7th parameter — because the C# compiler bakes the omitted
parameter's default value (`true`) into the compiled expression tree at the call site, any such
`Setup`/`Verify` will now only match invocations where `persistImmediately == true`, but the
production call being fixed here now always passes `false`. Left unfixed, this breaks
`ChangeTransportBoxStateHandlerTests` (compile succeeds, but `Verify` assertions fail at runtime).

**Files to touch**

1. `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`
2. `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs`

**No change needed (verify only) —** `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs`. Its `CreateOperationAsync` mock `Setup` (around line 193-201) omits the `persistImmediately` argument, and `GiftPackageManufactureService.cs`'s four real call sites (in `CreateManufactureAsync` and `DisassembleGiftPackageAsync`) also omit it — both sides resolve the same compiled-in default of `true`, so the existing setup still matches and this file requires no edits. You must still run its test suite in Step 6 below to confirm this (FR-3: no regression to the shared-service's other consumer).

**Step 1 — write the failing test first**

Open
`backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs`.
Add a new `[Fact]` immediately after the existing
`Handle_InTransitToReceived_DistinctProductCodes_CreatesOneOperationPerProduct` test method (which
currently ends at line 405 with `);` followed by a closing `}` — insert the new test right after
that method's closing `}`, before the next existing test
`Handle_InTransitToReceived_RoundsFractionalAmounts`):

```csharp

    [Fact]
    public async Task Handle_InTransitToReceived_PassesPersistImmediatelyFalse()
    {
        // Arrange — Receive must defer the SaveChangesAsync for StockUpOperation creation so
        // it commits atomically with the box's own state-transition SaveChangesAsync (FR-1):
        // both writes share the same ApplicationDbContext instance and must be flushed together.
        var box = CreateTestBoxWithItems(TransportBoxState.InTransit);
        SetupReceivedTransitionMocks(box);

        var request = new ChangeTransportBoxStateRequest { BoxId = 1, NewState = TransportBoxState.Received };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>(),
                false),
            Times.Once);
    }
```

This test will fail to compile right now, because `ILogisticsStockOperationService.CreateOperationAsync`
already has the 7-parameter signature (from the previous task) — so it compiles fine — but at
runtime it will **fail** the `Times.Once` assertion, because current production code (before Step 3
below) calls `CreateOperationAsync` without naming `persistImmediately`, which resolves to `true`,
not `false`.

**Step 2 — run this one test and confirm it fails**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Handle_InTransitToReceived_PassesPersistImmediatelyFalse"
```

Expected: the test runs and fails (Moq `Verify` throws `MockException: ... Expected invocation on
the mock at least once, but was never performed` or similar, because no invocation matching
`persistImmediately == false` occurred).

**Step 3 — implement the production fix**

In
`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`,
inside the `HandleReceived` method, replace this call (currently):

```csharp
            await _stockOperationService.CreateOperationAsync(
                documentNumber,
                group.ProductCode,
                group.Amount,
                LogisticsStockOperationSource.TransportBox,
                box.Id,
                cancellationToken);
```

with:

```csharp
            await _stockOperationService.CreateOperationAsync(
                documentNumber,
                group.ProductCode,
                group.Amount,
                LogisticsStockOperationSource.TransportBox,
                box.Id,
                cancellationToken,
                persistImmediately: false);
```

Do not change anything else in this file — not `Handle`, not the rest of `HandleReceived`, not any
other handler method.

**Step 4 — run the new test again and confirm it passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Handle_InTransitToReceived_PassesPersistImmediatelyFalse"
```

**Step 5 — fix the now-broken existing Moq expressions in `ChangeTransportBoxStateHandlerTests.cs`**

Run the full test class to see the breakage first:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ChangeTransportBoxStateHandlerTests"
```

Expected at this point: several existing tests fail (their `Verify` calls implicitly expect
`persistImmediately == true` because they omit the parameter, but the real call now always passes
`false`). Fix each occurrence below, in this same file.

**(a)** The constructor's shared `_stockUpProcessingServiceMock` setup. Replace:

```csharp
        _stockUpProcessingServiceMock
            .Setup(x => x.CreateOperationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
```

with:

```csharp
        _stockUpProcessingServiceMock
            .Setup(x => x.CreateOperationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .Returns(Task.CompletedTask);
```

**(b)** The generic "no calls happened" assertion. This exact block of code appears **twice**
verbatim in the file — once in `Handle_OpenedToQuarantine_DoesNotCreateStockUpOperations` and once
in `Handle_OpenedToReserve_NullLocation_ReturnsTransportBoxStateChangeError`. Replace **both**
occurrences of:

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
```

with:

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>(),
                It.IsAny<bool>()),
            Times.Never);
```

(`Times.Never` assertions are unaffected either way by the missing 7th parameter since no invocation
occurs in those two tests at all, but keep them consistent with the rest of the file so a future
signature change doesn't silently start passing them for the wrong reason.)

**(c)** The generic "exactly one call happened" assertion (all-`It.IsAny`, `Times.Once`). This exact
block appears **three times** verbatim — in `Handle_QuarantineToReceived_CreatesStockUpOperations`,
and twice more as the second `Verify` inside
`Handle_InTransitToReceived_AggregatesDuplicateProductCodes_IntoSingleStockUpOperation` and
`Handle_ReserveToReceived_AggregatesDuplicateProductCodes_IntoSingleStockUpOperation`. Replace **all
three** occurrences of:

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
```

with:

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>(),
                It.IsAny<bool>()),
            Times.Once);
```

**(d)** The literal-argument assertion for the aggregated `"BOX-000001-P-001"`/amount-8 case. This
exact block appears **twice** verbatim — as the first `Verify` in
`Handle_InTransitToReceived_AggregatesDuplicateProductCodes_IntoSingleStockUpOperation` and as the
first `Verify` in `Handle_ReserveToReceived_AggregatesDuplicateProductCodes_IntoSingleStockUpOperation`.
Replace **both** occurrences of:

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                "BOX-000001-P-001",
                "P-001",
                8,
                LogisticsStockOperationSource.TransportBox,
                1,
                It.IsAny<CancellationToken>()),
            Times.Once);
```

with:

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                "BOX-000001-P-001",
                "P-001",
                8,
                LogisticsStockOperationSource.TransportBox,
                1,
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()),
            Times.Once);
```

**(e)** In `Handle_InTransitToReceived_DistinctProductCodes_CreatesOneOperationPerProduct`, there are
three `Verify` calls. Replace the first one (P-001, amount 2):

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                "BOX-000001-P-001", "P-001", 2,
                LogisticsStockOperationSource.TransportBox, 1, It.IsAny<CancellationToken>()),
            Times.Once);
```

with:

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                "BOX-000001-P-001", "P-001", 2,
                LogisticsStockOperationSource.TransportBox, 1, It.IsAny<CancellationToken>(),
                It.IsAny<bool>()),
            Times.Once);
```

Replace the second one (P-002, amount 4):

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                "BOX-000001-P-002", "P-002", 4,
                LogisticsStockOperationSource.TransportBox, 1, It.IsAny<CancellationToken>()),
            Times.Once);
```

with:

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                "BOX-000001-P-002", "P-002", 4,
                LogisticsStockOperationSource.TransportBox, 1, It.IsAny<CancellationToken>(),
                It.IsAny<bool>()),
            Times.Once);
```

Replace the third one (generic, `Times.Exactly(2)`):

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
```

with:

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>(),
                It.IsAny<bool>()),
            Times.Exactly(2));
```

**(f)** In `Handle_InTransitToReceived_RoundsFractionalAmounts`, replace:

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                It.IsAny<string>(), "P-001", 3,
                It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
```

with:

```csharp
        _stockUpProcessingServiceMock.Verify(
            x => x.CreateOperationAsync(
                It.IsAny<string>(), "P-001", 3,
                It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>(),
                It.IsAny<bool>()),
            Times.Once);
```

**Step 6 — run the full test class and the GiftPackageManufactureServiceTests class**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ChangeTransportBoxStateHandlerTests"
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureServiceTests"
```

All tests in both classes must pass. `GiftPackageManufactureServiceTests` requires no code changes
(see "No change needed" note above) — this run is to confirm FR-3 (no regression to the shared
service's other consumer).

**Step 7 — run the full backend test suite, build, and format check**

```bash
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
dotnet test Anela.Heblo.sln
```

All must succeed. `dotnet test Anela.Heblo.sln` (not just the filtered subsets from earlier steps)
must show zero failures — this catches any other test file in the solution with a
`CreateOperationAsync` expectation that wasn't in the file list enumerated above.

**Step 8 — commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs
git commit -m "Defer StockUpOperation persistence in TransportBox Receive so it commits atomically with the box state transition"
```

**Acceptance criteria**

- `dotnet build Anela.Heblo.sln` succeeds with no errors.
- `dotnet format Anela.Heblo.sln --verify-no-changes` reports no changes.
- `dotnet test Anela.Heblo.sln` passes with zero failures across the whole solution.
- `ChangeTransportBoxStateHandler.HandleReceived`'s call to `_stockOperationService.CreateOperationAsync`
  passes `persistImmediately: false` as its 7th argument; no other line in
  `ChangeTransportBoxStateHandler.cs` is changed.
- `ChangeTransportBoxStateHandlerTests.cs` has a passing
  `Handle_InTransitToReceived_PassesPersistImmediatelyFalse` test that asserts the call includes a
  literal `false` for `persistImmediately`, and every other pre-existing `Setup`/`Verify` targeting
  `CreateOperationAsync` in that file includes an explicit 7th argument (`It.IsAny<bool>()`).
- `GiftPackageManufactureServiceTests.cs` is unmodified and its tests still pass unchanged (FR-3).

---

## Self-review notes (writing-plans skill, Self-Review section)

- **Spec coverage:** FR-1 (atomic persistence) is satisfied by deferring `SaveChangesAsync` via
  `persistImmediately: false` in Task 3, riding on `Handle`'s existing single `SaveChangesAsync`
  call (Task 3, Step 3) — no explicit transaction is introduced anywhere, consistent with the
  CI-enforced `scripts/check-no-managed-tx.sh` constraint stated explicitly in Task 3's goal and the
  plan preamble. FR-2 (idempotent retry) is satisfied by the `GetByDocumentNumberAsync` pre-check
  added in Task 1. FR-3 (no regression to `GiftPackageManufactureService`) is satisfied by the
  `persistImmediately = true` default at every layer (verified explicitly, with no code change
  required, in Task 3's "No change needed" note and Step 6). FR-4 (error surfacing) requires no
  dedicated code change per the spec ("no new dedicated exception type or error code is required")
  — it is a natural consequence of FR-2's skip-instead-of-throw behavior, already covered by Task 1.
  NFR-1 (performance: at most one extra existence-check query per product) is satisfied — Task 1
  adds exactly one `GetByDocumentNumberAsync` call per `CreateOperationAsync` invocation, no batching
  required per the spec ("not mandatory for this fix"). NFR-3 (unique index remains, no migration) —
  no schema-touching file appears anywhere in this plan.
- **No placeholders:** every task gives exact file paths, exact current code (verified against the
  live repository files read during planning, not just the arch-review/design excerpts), exact new
  code, and exact commands with what to expect. No "TBD", no "similar to Task N" shortcuts — every
  duplicated Moq block in Task 3 Step 5 is spelled out in full at each occurrence.
- **Type/signature consistency:** `IStockUpProcessingService.CreateOperationAsync` (Task 1) →
  `ILogisticsStockOperationService.CreateOperationAsync` (Task 2, pass-through, `sourceType` typed
  as `LogisticsStockOperationSource` instead of `StockUpSourceType`, mapped by
  `LogisticsStockOperationAdapter.MapSourceType`, unchanged from today) → the one call site in
  `ChangeTransportBoxStateHandler.HandleReceived` (Task 3) — all three signatures place
  `bool persistImmediately = true` immediately after the `CancellationToken` parameter, consistently
  named, consistently defaulted. Verified against the actual current file contents on disk, not
  assumed from the design doc.
