# Decouple `TransportBoxCompletionService` from Catalog-Owned Stock-Up Types — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the last source-level dependency from the Logistics module on Catalog-owned domain types (`IStockUpOperationRepository`, `StockUpOperation`, `StockUpOperationState`, `StockUpSourceType`) by introducing a Logistics-owned query contract and a Catalog-side adapter, then deleting the two pre-existing carve-outs in `ModuleBoundariesTests`.

**Architecture:** Apply the same consumer-owns-the-contract pattern already in force for the write side (`ILogisticsStockOperationService` + `LogisticsStockOperationAdapter`, PR #2201). Add a sibling read-side interface (`ILogisticsStockOperationQueryService`), a thin DTO (`LogisticsStockOperationStatus`), and a mirrored enum (`LogisticsStockOperationState`) in the Logistics `Contracts/` folder. Provide a `LogisticsStockOperationQueryAdapter` in `Catalog.Infrastructure/` that delegates to the existing `IStockUpOperationRepository`. `TransportBoxCompletionService` consumes only the new contract; module boundaries are enforced by the existing reflection-based arch test once the two pre-existing allowlist entries are removed.

**Tech Stack:** .NET 8, C# 12, xUnit, FluentAssertions, Moq, MediatR (not directly touched), Microsoft.Extensions.DependencyInjection.

---

## File Inventory

**Created**
- `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/LogisticsStockOperationState.cs` — enum with explicit integer values mirroring `StockUpOperationState`.
- `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/Models/LogisticsStockOperationStatus.cs` — DTO class with `DocumentNumber` and `State` only.
- `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ILogisticsStockOperationQueryService.cs` — query interface.
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/LogisticsStockOperationQueryAdapter.cs` — `internal sealed` provider-side adapter.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/LogisticsStockOperationQueryAdapterTests.cs` — adapter unit tests including enum-parity guard.

**Modified**
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — register the new adapter next to `LogisticsStockOperationAdapter` (line ~51).
- `backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs` — swap dependency, remove Catalog imports, update call site and state checks.
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs` — switch mocked dependency to the new interface, replace `CreateOperation` helper with `CreateStatus`.
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — remove the two entries in `LogisticsCatalogAllowlist` (lines 83 and 89).

**Unchanged (regression guards via FR-5 architecture test)**
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs`
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GetTransportBoxByCode/GetTransportBoxByCodeHandler.cs`

---

## Task 1: Add Logistics-owned contract types

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/LogisticsStockOperationState.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/Models/LogisticsStockOperationStatus.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ILogisticsStockOperationQueryService.cs`

- [ ] **Step 1: Create `LogisticsStockOperationState` enum**

Write `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/LogisticsStockOperationState.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Logistics.Contracts;

public enum LogisticsStockOperationState
{
    Pending = 0,
    Submitted = 1,
    Completed = 2,
    Failed = 3,
}
```

- [ ] **Step 2: Create `LogisticsStockOperationStatus` DTO**

Write `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/Models/LogisticsStockOperationStatus.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Logistics.Contracts.Models;

public class LogisticsStockOperationStatus
{
    public string DocumentNumber { get; init; } = string.Empty;
    public LogisticsStockOperationState State { get; init; }
}
```

DTO is a `class` (not a `record`) per the project rule — OpenAPI client generators mishandle record parameter order, and the convention is enforced project-wide.

- [ ] **Step 3: Create `ILogisticsStockOperationQueryService` interface**

Write `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ILogisticsStockOperationQueryService.cs`:

```csharp
using Anela.Heblo.Application.Features.Logistics.Contracts.Models;

namespace Anela.Heblo.Application.Features.Logistics.Contracts;

public interface ILogisticsStockOperationQueryService
{
    Task<IReadOnlyList<LogisticsStockOperationStatus>> GetOperationsBySourceAsync(
        LogisticsStockOperationSource sourceType,
        int sourceId,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Build to confirm the contract compiles in isolation**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds; no warnings about the new files.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/LogisticsStockOperationState.cs \
        backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/Models/LogisticsStockOperationStatus.cs \
        backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ILogisticsStockOperationQueryService.cs
git commit -m "feat: add Logistics-owned ILogisticsStockOperationQueryService contract"
```

---

## Task 2: Write failing tests for `LogisticsStockOperationQueryAdapter`

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/LogisticsStockOperationQueryAdapterTests.cs`

- [ ] **Step 1: Create the test file**

Write `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/LogisticsStockOperationQueryAdapterTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.Contracts.Models;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class LogisticsStockOperationQueryAdapterTests
{
    private readonly Mock<IStockUpOperationRepository> _repository = new();

    private LogisticsStockOperationQueryAdapter CreateAdapter() => new(_repository.Object);

    private static StockUpOperation CreateOperation(
        int id,
        string documentNumber,
        StockUpOperationState state)
    {
        var operation = new StockUpOperation(
            documentNumber,
            "PROD-001",
            1,
            StockUpSourceType.TransportBox,
            1);

        typeof(StockUpOperation).GetProperty("Id")!.SetValue(operation, id);
        typeof(StockUpOperation).GetProperty("State")!.SetValue(operation, state);
        return operation;
    }

    private void SetupRepositoryReturns(StockUpSourceType sourceType, int sourceId, List<StockUpOperation> operations)
    {
        _repository
            .Setup(r => r.GetBySourceAsync(sourceType, sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operations);
    }

    [Fact]
    public async Task GetOperationsBySourceAsync_WithTransportBoxSource_CallsRepositoryWithMappedEnum()
    {
        SetupRepositoryReturns(StockUpSourceType.TransportBox, 42, new List<StockUpOperation>());

        await CreateAdapter().GetOperationsBySourceAsync(
            LogisticsStockOperationSource.TransportBox, 42);

        _repository.Verify(
            r => r.GetBySourceAsync(StockUpSourceType.TransportBox, 42, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetOperationsBySourceAsync_WithGiftPackageManufactureSource_CallsRepositoryWithMappedEnum()
    {
        SetupRepositoryReturns(StockUpSourceType.GiftPackageManufacture, 7, new List<StockUpOperation>());

        await CreateAdapter().GetOperationsBySourceAsync(
            LogisticsStockOperationSource.GiftPackageManufacture, 7);

        _repository.Verify(
            r => r.GetBySourceAsync(StockUpSourceType.GiftPackageManufacture, 7, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetOperationsBySourceAsync_WithUnknownSource_ThrowsArgumentOutOfRangeException()
    {
        var unknownSource = (LogisticsStockOperationSource)999;

        var act = () => CreateAdapter().GetOperationsBySourceAsync(unknownSource, 1);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(StockUpOperationState.Pending, LogisticsStockOperationState.Pending)]
    [InlineData(StockUpOperationState.Submitted, LogisticsStockOperationState.Submitted)]
    [InlineData(StockUpOperationState.Completed, LogisticsStockOperationState.Completed)]
    [InlineData(StockUpOperationState.Failed, LogisticsStockOperationState.Failed)]
    public async Task GetOperationsBySourceAsync_MapsStateOneToOne(
        StockUpOperationState catalogState,
        LogisticsStockOperationState expectedLogisticsState)
    {
        SetupRepositoryReturns(
            StockUpSourceType.TransportBox,
            1,
            new List<StockUpOperation> { CreateOperation(1, "DOC-1", catalogState) });

        var result = await CreateAdapter().GetOperationsBySourceAsync(
            LogisticsStockOperationSource.TransportBox, 1);

        result.Should().ContainSingle()
            .Which.State.Should().Be(expectedLogisticsState);
    }

    [Fact]
    public async Task GetOperationsBySourceAsync_ProjectsDocumentNumber()
    {
        SetupRepositoryReturns(
            StockUpSourceType.TransportBox,
            1,
            new List<StockUpOperation>
            {
                CreateOperation(1, "DOC-A", StockUpOperationState.Completed),
                CreateOperation(2, "DOC-B", StockUpOperationState.Pending),
            });

        var result = await CreateAdapter().GetOperationsBySourceAsync(
            LogisticsStockOperationSource.TransportBox, 1);

        result.Select(s => s.DocumentNumber).Should().BeEquivalentTo(new[] { "DOC-A", "DOC-B" });
    }

    [Fact]
    public async Task GetOperationsBySourceAsync_WhenRepositoryEmpty_ReturnsEmptyList()
    {
        SetupRepositoryReturns(StockUpSourceType.TransportBox, 1, new List<StockUpOperation>());

        var result = await CreateAdapter().GetOperationsBySourceAsync(
            LogisticsStockOperationSource.TransportBox, 1);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOperationsBySourceAsync_HandlesEveryCatalogStateMember_WithoutThrowing()
    {
        // Enum-parity guard: if Catalog adds a new StockUpOperationState member (e.g. Reconciling),
        // this test fails before production traffic hits the adapter's exhaustive switch.
        foreach (var state in Enum.GetValues<StockUpOperationState>())
        {
            _repository.Reset();
            SetupRepositoryReturns(
                StockUpSourceType.TransportBox,
                1,
                new List<StockUpOperation> { CreateOperation(1, "DOC-1", state) });

            var act = () => CreateAdapter().GetOperationsBySourceAsync(
                LogisticsStockOperationSource.TransportBox, 1);

            await act.Should().NotThrowAsync(
                $"adapter must map Catalog state {state} to a Logistics state");
        }
    }

    [Fact]
    public async Task GetOperationsBySourceAsync_PropagatesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        SetupRepositoryReturns(StockUpSourceType.TransportBox, 1, new List<StockUpOperation>());

        await CreateAdapter().GetOperationsBySourceAsync(
            LogisticsStockOperationSource.TransportBox, 1, cts.Token);

        _repository.Verify(
            r => r.GetBySourceAsync(StockUpSourceType.TransportBox, 1, cts.Token),
            Times.Once);
    }
}
```

- [ ] **Step 2: Run the new tests and confirm they fail (no adapter yet)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~LogisticsStockOperationQueryAdapterTests"`
Expected: build fails with `CS0246: The type or namespace name 'LogisticsStockOperationQueryAdapter' could not be found`. (RED — adapter not yet implemented.)

---

## Task 3: Implement `LogisticsStockOperationQueryAdapter`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/LogisticsStockOperationQueryAdapter.cs`

- [ ] **Step 1: Write the adapter**

Write `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/LogisticsStockOperationQueryAdapter.cs`:

```csharp
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.Contracts.Models;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class LogisticsStockOperationQueryAdapter : ILogisticsStockOperationQueryService
{
    private readonly IStockUpOperationRepository _repository;

    public LogisticsStockOperationQueryAdapter(IStockUpOperationRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<LogisticsStockOperationStatus>> GetOperationsBySourceAsync(
        LogisticsStockOperationSource sourceType,
        int sourceId,
        CancellationToken cancellationToken = default)
    {
        var mappedSourceType = MapSourceType(sourceType);
        var operations = await _repository.GetBySourceAsync(mappedSourceType, sourceId, cancellationToken);

        var result = new List<LogisticsStockOperationStatus>(operations.Count);
        foreach (var operation in operations)
        {
            result.Add(new LogisticsStockOperationStatus
            {
                DocumentNumber = operation.DocumentNumber,
                State = MapState(operation.State),
            });
        }

        return result;
    }

    private static StockUpSourceType MapSourceType(LogisticsStockOperationSource sourceType) => sourceType switch
    {
        LogisticsStockOperationSource.TransportBox => StockUpSourceType.TransportBox,
        LogisticsStockOperationSource.GiftPackageManufacture => StockUpSourceType.GiftPackageManufacture,
        _ => throw new ArgumentOutOfRangeException(nameof(sourceType), sourceType, null),
    };

    private static LogisticsStockOperationState MapState(StockUpOperationState state) => state switch
    {
        StockUpOperationState.Pending => LogisticsStockOperationState.Pending,
        StockUpOperationState.Submitted => LogisticsStockOperationState.Submitted,
        StockUpOperationState.Completed => LogisticsStockOperationState.Completed,
        StockUpOperationState.Failed => LogisticsStockOperationState.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };
}
```

- [ ] **Step 2: Run the adapter tests and confirm they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~LogisticsStockOperationQueryAdapterTests"`
Expected: all 9 test cases (1 theory × 4 rows + 5 facts) pass.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/LogisticsStockOperationQueryAdapter.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/LogisticsStockOperationQueryAdapterTests.cs
git commit -m "feat: add LogisticsStockOperationQueryAdapter implementing the Logistics-owned query contract"
```

---

## Task 4: Register the adapter in `CatalogModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs:51`

- [ ] **Step 1: Add the DI binding next to the existing write-side adapter registration**

In `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`, locate line 51:

```csharp
        services.AddTransient<ILogisticsStockOperationService, LogisticsStockOperationAdapter>();
```

Replace it with two lines:

```csharp
        services.AddTransient<ILogisticsStockOperationService, LogisticsStockOperationAdapter>();
        // Logistics owns the query contract; Catalog (this module) provides the adapter implementation.
        services.AddTransient<ILogisticsStockOperationQueryService, LogisticsStockOperationQueryAdapter>();
```

The lifetime is `AddTransient` to match the existing sibling on line 51 (per arch-review amendment #1). The adapter is stateless; the repository it delegates to is itself scoped.

- [ ] **Step 2: Confirm no DI registration for the new contract exists in `LogisticsModule`**

Run: `grep -rn "ILogisticsStockOperationQueryService" backend/src/Anela.Heblo.Application/Features/Logistics`
Expected: only the interface definition file (`Contracts/ILogisticsStockOperationQueryService.cs`) is listed. No `LogisticsModule` registration.

- [ ] **Step 3: Build to confirm the registration compiles**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
git commit -m "feat: register LogisticsStockOperationQueryAdapter in CatalogModule"
```

---

## Task 5: Update `TransportBoxCompletionServiceTests` to consume the new contract (RED)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs`

The existing tests construct real `StockUpOperation` entities via a `CreateOperation` helper and mock `IStockUpOperationRepository`. After this task they will mock `ILogisticsStockOperationQueryService` and construct `LogisticsStockOperationStatus` DTOs via a new `CreateStatus` helper. The test file must compile against the new interface even though the production service still has the old constructor — this is the deliberate RED step: build fails because the test ctor call no longer matches the service signature.

- [ ] **Step 1: Rewrite the test file end-to-end**

Replace the full contents of `backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs` with:

```csharp
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.Contracts.Models;
using Anela.Heblo.Application.Features.Logistics.Services;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Services;

public class TransportBoxCompletionServiceTests
{
    private readonly Mock<ILogger<TransportBoxCompletionService>> _loggerMock;
    private readonly Mock<ITransportBoxRepository> _transportBoxRepositoryMock;
    private readonly Mock<ILogisticsStockOperationQueryService> _stockOperationQueryServiceMock;
    private readonly TransportBoxCompletionService _service;

    public TransportBoxCompletionServiceTests()
    {
        _loggerMock = new Mock<ILogger<TransportBoxCompletionService>>();
        _transportBoxRepositoryMock = new Mock<ITransportBoxRepository>();
        _stockOperationQueryServiceMock = new Mock<ILogisticsStockOperationQueryService>();
        _service = new TransportBoxCompletionService(
            _loggerMock.Object,
            _transportBoxRepositoryMock.Object,
            _stockOperationQueryServiceMock.Object);
    }

    [Fact]
    public async Task CompleteReceivedBoxesAsync_NoReceivedBoxes_DoesNothing()
    {
        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox>());

        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        _transportBoxRepositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CompleteReceivedBoxesAsync_AllOperationsCompleted_TransitionsBoxToStocked()
    {
        var box = CreateBox(1, "BOX-001", TransportBoxState.Received);
        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box });

        var operations = new List<LogisticsStockOperationStatus>
        {
            CreateStatus("BOX-000001-PROD1", LogisticsStockOperationState.Completed),
            CreateStatus("BOX-000001-PROD2", LogisticsStockOperationState.Completed),
        };
        SetupQueryReturns(box.Id, operations);

        _transportBoxRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _transportBoxRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        box.State.Should().Be(TransportBoxState.Stocked);
        _transportBoxRepositoryMock.Verify(
            x => x.UpdateAsync(box, It.IsAny<CancellationToken>()),
            Times.Once);
        _transportBoxRepositoryMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CompleteReceivedBoxesAsync_AnyOperationFailed_TransitionsBoxToError()
    {
        var box = CreateBox(1, "BOX-001", TransportBoxState.Received);
        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box });

        var operations = new List<LogisticsStockOperationStatus>
        {
            CreateStatus("BOX-000001-PROD1", LogisticsStockOperationState.Completed),
            CreateStatus("BOX-000001-PROD2", LogisticsStockOperationState.Failed),
        };
        SetupQueryReturns(box.Id, operations);

        _transportBoxRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _transportBoxRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        box.State.Should().Be(TransportBoxState.Error);
    }

    [Fact]
    public async Task CompleteReceivedBoxesAsync_OperationsPending_LeavesBoxInReceived()
    {
        var box = CreateBox(1, "BOX-001", TransportBoxState.Received);
        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box });

        var operations = new List<LogisticsStockOperationStatus>
        {
            CreateStatus("BOX-000001-PROD1", LogisticsStockOperationState.Completed),
            CreateStatus("BOX-000001-PROD2", LogisticsStockOperationState.Pending),
        };
        SetupQueryReturns(box.Id, operations);

        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        box.State.Should().Be(TransportBoxState.Received);
        _transportBoxRepositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CompleteReceivedBoxesAsync_NoOperationsForBox_TransitionsToError()
    {
        var box = CreateBox(1, "BOX-001", TransportBoxState.Received);
        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box });

        SetupQueryReturns(box.Id, new List<LogisticsStockOperationStatus>());

        _transportBoxRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _transportBoxRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        box.State.Should().Be(TransportBoxState.Error);
    }

    [Fact]
    public async Task CompleteReceivedBoxesAsync_MultipleBoxes_ProcessesAll()
    {
        var box1 = CreateBox(1, "BOX-001", TransportBoxState.Received);
        var box2 = CreateBox(2, "BOX-002", TransportBoxState.Received);
        var box3 = CreateBox(3, "BOX-003", TransportBoxState.Received);

        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box1, box2, box3 });

        SetupQueryReturns(box1.Id, new List<LogisticsStockOperationStatus>
        {
            CreateStatus("BOX-000001-PROD1", LogisticsStockOperationState.Completed),
        });
        SetupQueryReturns(box2.Id, new List<LogisticsStockOperationStatus>
        {
            CreateStatus("BOX-000002-PROD1", LogisticsStockOperationState.Failed),
        });
        SetupQueryReturns(box3.Id, new List<LogisticsStockOperationStatus>
        {
            CreateStatus("BOX-000003-PROD1", LogisticsStockOperationState.Pending),
        });

        _transportBoxRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _transportBoxRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        box1.State.Should().Be(TransportBoxState.Stocked);
        box2.State.Should().Be(TransportBoxState.Error);
        box3.State.Should().Be(TransportBoxState.Received);

        _transportBoxRepositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        _transportBoxRepositoryMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task CompleteReceivedBoxesAsync_OperationsSubmitted_LeavesBoxInReceived()
    {
        var box = CreateBox(1, "BOX-001", TransportBoxState.Received);
        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box });

        var operations = new List<LogisticsStockOperationStatus>
        {
            CreateStatus("BOX-000001-PROD1", LogisticsStockOperationState.Completed),
            CreateStatus("BOX-000001-PROD2", LogisticsStockOperationState.Submitted),
        };
        SetupQueryReturns(box.Id, operations);

        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        box.State.Should().Be(TransportBoxState.Received);
        _transportBoxRepositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void SetupQueryReturns(int sourceId, IReadOnlyList<LogisticsStockOperationStatus> operations)
    {
        _stockOperationQueryServiceMock
            .Setup(x => x.GetOperationsBySourceAsync(
                LogisticsStockOperationSource.TransportBox,
                sourceId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(operations);
    }

    private static TransportBox CreateBox(int id, string code, TransportBoxState state)
    {
        var box = new TransportBox();
        typeof(TransportBox).GetProperty("Id")!.SetValue(box, id);
        typeof(TransportBox).GetProperty("Code")!.SetValue(box, code);
        typeof(TransportBox).GetProperty("State")!.SetValue(box, state);
        return box;
    }

    private static LogisticsStockOperationStatus CreateStatus(string documentNumber, LogisticsStockOperationState state)
        => new()
        {
            DocumentNumber = documentNumber,
            State = state,
        };
}
```

- [ ] **Step 2: Run the test project build and confirm it fails**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: build fails. The error names a constructor mismatch on `TransportBoxCompletionService` — the third parameter is still `IStockUpOperationRepository` in production. Typical message: `CS1503: Argument 3: cannot convert from 'Moq.Mock<ILogisticsStockOperationQueryService>.Object' to 'Anela.Heblo.Domain.Features.Catalog.Stock.IStockUpOperationRepository'`. (RED — production not yet rewired.)

---

## Task 6: Rewire `TransportBoxCompletionService` to the new contract (GREEN)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs`

- [ ] **Step 1: Replace the file contents**

Replace the full contents of `backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs` with:

```csharp
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.Contracts.Models;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.Services;

public class TransportBoxCompletionService : ITransportBoxCompletionService
{
    private readonly ILogger<TransportBoxCompletionService> _logger;
    private readonly ITransportBoxRepository _transportBoxRepository;
    private readonly ILogisticsStockOperationQueryService _stockOperationQueryService;

    public TransportBoxCompletionService(
        ILogger<TransportBoxCompletionService> logger,
        ITransportBoxRepository transportBoxRepository,
        ILogisticsStockOperationQueryService stockOperationQueryService)
    {
        _logger = logger;
        _transportBoxRepository = transportBoxRepository;
        _stockOperationQueryService = stockOperationQueryService;
    }

    public async Task CompleteReceivedBoxesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting CompleteReceivedBoxes background task");

        var receivedBoxes = await _transportBoxRepository.GetReceivedBoxesAsync(cancellationToken);

        _logger.LogInformation("Found {Count} transport boxes in Received state", receivedBoxes.Count);

        if (receivedBoxes.Count == 0)
        {
            _logger.LogDebug("No boxes to process");
            return;
        }

        int completedCount = 0;
        int errorCount = 0;
        int skippedCount = 0;

        foreach (var box in receivedBoxes)
        {
            try
            {
                var result = await ProcessBoxAsync(box, cancellationToken);

                switch (result)
                {
                    case BoxProcessingResult.Completed:
                        completedCount++;
                        break;
                    case BoxProcessingResult.Failed:
                        errorCount++;
                        break;
                    case BoxProcessingResult.Skipped:
                        skippedCount++;
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing box {BoxId} ({BoxCode})",
                    box.Id, box.Code);
                errorCount++;
            }
        }

        _logger.LogInformation(
            "CompleteReceivedBoxes finished. Completed: {Completed}, Failed: {Failed}, Skipped: {Skipped}",
            completedCount, errorCount, skippedCount);
    }

    private async Task<BoxProcessingResult> ProcessBoxAsync(
        TransportBox box,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Processing box {BoxId} ({BoxCode})", box.Id, box.Code);

        var operations = await _stockOperationQueryService.GetOperationsBySourceAsync(
            LogisticsStockOperationSource.TransportBox,
            box.Id,
            cancellationToken);

        if (operations.Count == 0)
        {
            _logger.LogWarning(
                "Box {BoxId} ({BoxCode}) has no StockUpOperations, marking as Error",
                box.Id, box.Code);

            box.Error(DateTime.UtcNow, "System",
                "No stock-up operations found for this box");
            await _transportBoxRepository.UpdateAsync(box, cancellationToken);
            await _transportBoxRepository.SaveChangesAsync(cancellationToken);

            return BoxProcessingResult.Failed;
        }

        var allCompleted = operations.All(op => op.State == LogisticsStockOperationState.Completed);
        var anyFailed = operations.Any(op => op.State == LogisticsStockOperationState.Failed);
        var pendingOrSubmitted = operations.Any(op =>
            op.State == LogisticsStockOperationState.Pending ||
            op.State == LogisticsStockOperationState.Submitted);

        if (allCompleted)
        {
            _logger.LogInformation(
                "All {Count} stock-up operations for box {BoxId} ({BoxCode}) completed, marking as Stocked",
                operations.Count, box.Id, box.Code);

            box.ToPick(DateTime.UtcNow, "System");
            await _transportBoxRepository.UpdateAsync(box, cancellationToken);
            await _transportBoxRepository.SaveChangesAsync(cancellationToken);

            return BoxProcessingResult.Completed;
        }

        if (anyFailed)
        {
            var failedOps = operations
                .Where(op => op.State == LogisticsStockOperationState.Failed)
                .ToList();

            var errorMessage = $"{failedOps.Count} stock-up operation(s) failed. " +
                             $"Document numbers: {string.Join(", ", failedOps.Select(op => op.DocumentNumber))}";

            _logger.LogWarning(
                "Box {BoxId} ({BoxCode}) has {FailedCount} failed stock-up operations, marking as Error",
                box.Id, box.Code, failedOps.Count);

            box.Error(DateTime.UtcNow, "System", errorMessage);
            await _transportBoxRepository.UpdateAsync(box, cancellationToken);
            await _transportBoxRepository.SaveChangesAsync(cancellationToken);

            return BoxProcessingResult.Failed;
        }

        if (pendingOrSubmitted)
        {
            _logger.LogDebug(
                "Box {BoxId} ({BoxCode}) still has {Count} operations in progress, skipping",
                box.Id, box.Code,
                operations.Count(op =>
                    op.State == LogisticsStockOperationState.Pending ||
                    op.State == LogisticsStockOperationState.Submitted));

            return BoxProcessingResult.Skipped;
        }

        _logger.LogWarning("Box {BoxId} ({BoxCode}) in unexpected state, skipping",
            box.Id, box.Code);
        return BoxProcessingResult.Skipped;
    }

    private enum BoxProcessingResult
    {
        Completed,
        Failed,
        Skipped,
    }
}
```

Behavior is unchanged. The only diffs: the `using` block, the field/ctor parameter type, the call site, and the four `op.State == X` comparisons (now against `LogisticsStockOperationState`).

- [ ] **Step 2: Build the whole solution**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds with zero errors and no new warnings.

- [ ] **Step 3: Run the rewired tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxCompletionServiceTests"`
Expected: all 7 tests pass.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs \
        backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs
git commit -m "refactor(logistics): consume Logistics-owned query contract in TransportBoxCompletionService"
```

---

## Task 7: Remove the pre-existing allowlist entries and confirm the boundary holds

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs:77-90`

- [ ] **Step 1: Delete the two `LogisticsCatalogAllowlist` entries**

In `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`, replace the `LogisticsCatalogAllowlist` block (currently lines 73–90):

```csharp
    // Allowlist for Logistics → Catalog. Pre-existing violations in TransportBoxCompletionService
    // that are out of scope for the 2026-06-01 Logistics-Catalog boundary introduction.
    // Remove these entries when TransportBoxCompletionService is refactored to use a
    // Logistics-owned contract instead of IStockUpOperationRepository / StockUpOperation directly.
    private static readonly HashSet<string> LogisticsCatalogAllowlist = new(StringComparer.Ordinal)
    {
        // TransportBoxCompletionService injects IStockUpOperationRepository (a Catalog-owned
        // repository) to persist stock-up operations when a transport box is completed.
        // Decoupling this requires introducing a Logistics-owned contract (e.g.
        // ILogisticsStockUpGateway) and a Catalog adapter — tracked as a follow-up.
        "Anela.Heblo.Application.Features.Logistics.Services.TransportBoxCompletionService -> Anela.Heblo.Domain.Features.Catalog.Stock.IStockUpOperationRepository",

        // StockUpOperation is the value type produced and persisted by TransportBoxCompletionService
        // via IStockUpOperationRepository. Covered by the same follow-up as the entry above;
        // the compiler-generated nested types (+<>c, +<ProcessBoxAsync>d__5) are handled
        // automatically via the DeclaringType allowlist check in the test harness.
        "Anela.Heblo.Application.Features.Logistics.Services.TransportBoxCompletionService -> Anela.Heblo.Domain.Features.Catalog.Stock.StockUpOperation",
    };
```

with:

```csharp
    // Allowlist for Logistics → Catalog. Empty — TransportBoxCompletionService now consumes
    // the Logistics-owned ILogisticsStockOperationQueryService contract; the Catalog adapter
    // lives in Catalog.Infrastructure and is captured by the reverse-direction
    // CatalogLogisticsAllowlist below.
    private static readonly HashSet<string> LogisticsCatalogAllowlist = new(StringComparer.Ordinal);
```

- [ ] **Step 2: Run the architecture tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"`
Expected: all rows of `Consumer_types_should_not_reference_provider_owned_namespaces` pass, including `Logistics -> Catalog`. The standalone `Logistics_types_should_not_reference_Purchase_owned_namespaces` also still passes.

- [ ] **Step 3: Negative-control check (manual, do not commit)**

Re-add `using Anela.Heblo.Domain.Features.Catalog.Stock;` to `TransportBoxCompletionService.cs` and run the test again. Expected: `Logistics -> Catalog` row fails, naming `TransportBoxCompletionService -> Anela.Heblo.Domain.Features.Catalog.Stock.IStockUpOperationRepository` (or similar) under the FluentAssertions `BeEmpty` failure. Revert the change before continuing.

```bash
git diff backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs
# Confirm only the temporary `using` line was added.
git checkout -- backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs
```

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test: remove Logistics→Catalog allowlist entries now that TransportBoxCompletionService is decoupled"
```

---

## Task 8: Full-solution validation

- [ ] **Step 1: Verify no Logistics file still imports Catalog**

Run: `grep -rn "Anela.Heblo.Domain.Features.Catalog\|Anela.Heblo.Application.Features.Catalog" backend/src/Anela.Heblo.Application/Features/Logistics backend/src/Anela.Heblo.Domain/Features/Logistics`
Expected: zero matches. (The reverse-direction adapter `LogisticsCatalogTransportSourceAdapter`, if present, lives in Logistics and implements a Catalog-owned consumer contract — that file does **not** import a Catalog namespace; it only references types in `Anela.Heblo.Application.Features.Logistics.Contracts` and `Anela.Heblo.Domain.Features.Logistics.*`. If grep returns it, re-read the file: a `using Anela.Heblo.Application.Features.Catalog.Contracts;` line would still be a violation. None is expected in this branch.)

- [ ] **Step 2: Verify no Catalog domain type leaks via name reference**

Run: `grep -rn "IStockUpOperationRepository\|StockUpSourceType\|StockUpOperationState\b" backend/src/Anela.Heblo.Application/Features/Logistics backend/src/Anela.Heblo.Domain/Features/Logistics`
Expected: zero matches.

- [ ] **Step 3: Formatter**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: exit code 0. If it reports formatting drift, run `dotnet format backend/Anela.Heblo.sln`, inspect the diff, and amend the most recent commit only if the changes touch files this plan modified.

- [ ] **Step 4: Full build**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds with zero errors and no new warnings.

- [ ] **Step 5: Full test suite**

Run: `dotnet test backend/Anela.Heblo.sln --no-build`
Expected: all tests pass, including:
- `LogisticsStockOperationQueryAdapterTests` (Task 2/3)
- `TransportBoxCompletionServiceTests` (Task 5/6)
- `ModuleBoundariesTests.Consumer_types_should_not_reference_provider_owned_namespaces` rows for `Logistics -> Catalog` (Task 7)
- `LogisticsStockOperationAdapterTests` (existing — regression guard for write side)

- [ ] **Step 6: Smoke-check DI composition**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj && dotnet run --project backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj -- --urls "http://localhost:5099" &`
Wait ~5 seconds, then:
```
curl -sf http://localhost:5099/health || echo "health endpoint not reachable"
kill %1
```
Expected: the API starts without DI exceptions. If your local environment requires Azure Key Vault and a database to fully start, you may skip this step provided Tasks 1–7 all pass; the unit tests already exercise the DI-shape contract (`AddTransient<ILogisticsStockOperationQueryService, LogisticsStockOperationQueryAdapter>` resolves a stateless adapter with one scoped dependency).

- [ ] **Step 7: No further commit needed**

This task only verifies. No file changes.

---

## Self-Review

**Spec coverage**
- FR-1 (Logistics-owned types) — Task 1.
- FR-2 (Catalog adapter) — Task 3, tests in Task 2 (covers enum-parity per arch-review amendment #3).
- FR-3 (DI registration) — Task 4 (uses `AddTransient` per arch-review amendment #1).
- FR-4 (rewire `TransportBoxCompletionService`) — Task 5 (tests) + Task 6 (production).
- FR-5 (architecture test enforces boundary) — Task 7 (removes the two existing allowlist entries; uses the existing `Logistics -> Catalog` rule rather than adding a new method, per arch-review amendment #2).
- FR-6 (verify the original three files remain clean) — covered passively by the FR-5 architecture test in Task 7; no code changes needed.
- NFR-1/2/3 (perf, security, maintainability) — no behavior or attack-surface change; in-process projection bounded by per-box op count.
- NFR-4 (testability) — Task 5 swaps mocks to the new contract and constructs DTOs directly via a `CreateStatus` helper.

**Placeholder scan** — no TBD, no "implement later", every code block is concrete and self-contained.

**Type consistency** — `ILogisticsStockOperationQueryService.GetOperationsBySourceAsync` and its consumer/test call sites all use the exact same signature: `(LogisticsStockOperationSource sourceType, int sourceId, CancellationToken cancellationToken = default)` returning `Task<IReadOnlyList<LogisticsStockOperationStatus>>`. Enum members `Pending|Submitted|Completed|Failed` are spelled identically across the new enum, the adapter switch, the consumer comparisons, and the tests. DTO fields `DocumentNumber` and `State` match between adapter projection, consumer reads, and test fixture builder.

**Arch-review amendments incorporated**
1. `AddTransient` (not `AddScoped`) — Task 4.
2. Remove allowlist entries instead of authoring a new test method — Task 7.
3. Enum-parity contract test in the Catalog test suite — Task 2 (`GetOperationsBySourceAsync_HandlesEveryCatalogStateMember_WithoutThrowing`).
4. Grep coverage in Task 8 explicitly excludes Logistics-side `LogisticsCatalogTransportSourceAdapter` and explains why.
