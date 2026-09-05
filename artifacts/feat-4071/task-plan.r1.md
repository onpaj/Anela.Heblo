# Extract Transport Box State-Transition Side Effects Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the four per-transition side effects and the inventory-restore helper out of `ChangeTransportBoxStateHandler` into independently testable collaborators, leaving the handler as pure orchestration, with zero change to observable behavior or to `ChangeTransportBoxStateRequest`/`Response`.

**Architecture:** Introduce `ITransportBoxTransitionSideEffect` (mirroring the existing `IIndexingStrategy` pattern in the KnowledgeBase module), four implementations registered via `IEnumerable<ITransportBoxTransitionSideEffect>` and resolved with `FirstOrDefault(s => s.Supports(from, to))`, plus a separate `ITransportBoxInventoryRestorer` for the unconditional Opened→New rollback path. The handler's `CallBackMap` dictionary and four private methods are deleted; `RestoreInventoryForItemsAsync` is deleted and replaced by a call to the new restorer.

**Tech Stack:** .NET 8, MediatR, xUnit, Moq, FluentAssertions (backend only — no frontend changes).

---

### task: create-side-effect-interface

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ITransportBoxTransitionSideEffect.cs`

- [ ] **Step 1: Create the interface**

```csharp
using Anela.Heblo.Domain.Features.Logistics.Transport;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

/// <summary>
/// One state-transition's side effect, dispatched by ChangeTransportBoxStateHandler.
/// Return null to let the transition continue; return a populated response to
/// short-circuit Handle() with a failure result — identical contract to the
/// private methods this interface replaces.
/// </summary>
public interface ITransportBoxTransitionSideEffect
{
    bool Supports(TransportBoxState from, TransportBoxState to);

    Task<ChangeTransportBoxStateResponse?> ExecuteAsync(
        TransportBox box,
        ChangeTransportBoxStateRequest request,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `cd backend && dotnet build src/Anela.Heblo.Application`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ITransportBoxTransitionSideEffect.cs
git commit -m "feat(logistics): add ITransportBoxTransitionSideEffect interface"
```

---

### task: extract-new-to-opened-side-effect

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/NewToOpenedSideEffect.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/NewToOpenedSideEffectTests.cs`

This is a mechanical move of `HandleNewToOpened`'s body (lines 214–248 of the current
`ChangeTransportBoxStateHandler.cs`) into its own class, unchanged.

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class NewToOpenedSideEffectTests
{
    private readonly Mock<ITransportBoxRepository> _repositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
    private readonly Mock<TimeProvider> _timeProviderMock = new();
    private readonly NewToOpenedSideEffect _sut;

    public NewToOpenedSideEffectTests()
    {
        _currentUserServiceMock.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("tester", "Tester", "tester@test.com", true));
        _timeProviderMock.Setup(x => x.GetUtcNow())
            .Returns(new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero));

        _sut = new NewToOpenedSideEffect(
            _repositoryMock.Object, _currentUserServiceMock.Object, _timeProviderMock.Object);
    }

    [Fact]
    public void Supports_NewToOpened_ReturnsTrue()
    {
        _sut.Supports(TransportBoxState.New, TransportBoxState.Opened).Should().BeTrue();
    }

    [Theory]
    [InlineData(TransportBoxState.Opened, TransportBoxState.Reserve)]
    [InlineData(TransportBoxState.New, TransportBoxState.Quarantine)]
    public void Supports_AnyOtherPair_ReturnsFalse(TransportBoxState from, TransportBoxState to)
    {
        _sut.Supports(from, to).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_MissingBoxCode_ReturnsRequiredFieldMissing()
    {
        var box = new TransportBox();
        var request = new ChangeTransportBoxStateRequest { BoxId = 1, NewState = TransportBoxState.Opened };

        var result = await _sut.ExecuteAsync(box, request, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.RequiredFieldMissing);
        result.Params.Should().Contain("field", "BoxCode");
    }

    [Fact]
    public async Task ExecuteAsync_DuplicateActiveCode_ReturnsDuplicateActiveBoxFound()
    {
        var box = new TransportBox();
        var request = new ChangeTransportBoxStateRequest
        {
            BoxId = 1, NewState = TransportBoxState.Opened, BoxCode = "b999"
        };
        _repositoryMock.Setup(x => x.IsBoxCodeActiveAsync("B999")).ReturnsAsync(true);

        var result = await _sut.ExecuteAsync(box, request, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TransportBoxDuplicateActiveBoxFound);
        result.Params.Should().Contain("code", "B999");
    }

    [Fact]
    public async Task ExecuteAsync_ValidCode_ClosesStaleStockedBoxesWithSameCode_ReturnsNull()
    {
        var box = new TransportBox();
        var request = new ChangeTransportBoxStateRequest
        {
            BoxId = 1, NewState = TransportBoxState.Opened, BoxCode = "B999"
        };
        _repositoryMock.Setup(x => x.IsBoxCodeActiveAsync("B999")).ReturnsAsync(false);

        var staleBox = new TransportBox();
        _repositoryMock
            .Setup(x => x.GetPagedListAsync(0, 0, null, null, "B999", TransportBoxState.Stocked, null, null, null))
            .ReturnsAsync((new List<TransportBox> { staleBox }, 1));

        var result = await _sut.ExecuteAsync(box, request, CancellationToken.None);

        result.Should().BeNull();
        _repositoryMock.Verify(x => x.UpdateAsync(staleBox, It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails to compile (NewToOpenedSideEffect does not exist yet)**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~NewToOpenedSideEffectTests"`
Expected: Build error — `NewToOpenedSideEffect` does not exist.

> Note: confirm the exact `GetPagedListAsync` overload/parameter order against
> `ITransportBoxRepository.cs` before finalizing this test — match its real signature rather
> than the illustrative call above if they differ (e.g. named vs. positional optional args).

- [ ] **Step 3: Implement `NewToOpenedSideEffect`**

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

public class NewToOpenedSideEffect : ITransportBoxTransitionSideEffect
{
    private readonly ITransportBoxRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;

    public NewToOpenedSideEffect(
        ITransportBoxRepository repository,
        ICurrentUserService currentUserService,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
    }

    public bool Supports(TransportBoxState from, TransportBoxState to) =>
        from == TransportBoxState.New && to == TransportBoxState.Opened;

    public async Task<ChangeTransportBoxStateResponse?> ExecuteAsync(
        TransportBox box, ChangeTransportBoxStateRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.BoxCode))
        {
            return new ChangeTransportBoxStateResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.RequiredFieldMissing,
                Params = new Dictionary<string, string> { { "field", "BoxCode" } }
            };
        }

        // Check if another active box with the same code already exists
        var normalizedCode = request.BoxCode.ToUpper();
        var isCodeActive = await _repository.IsBoxCodeActiveAsync(normalizedCode);
        if (isCodeActive)
        {
            return new ChangeTransportBoxStateResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.TransportBoxDuplicateActiveBoxFound,
                Params = new Dictionary<string, string> { { "code", normalizedCode } }
            };
        }

        // Close all stocked boxes
        var (stocked, _) = await _repository.GetPagedListAsync(skip: 0, take: 0, code: request.BoxCode, state: TransportBoxState.Stocked);
        foreach (var s in stocked)
        {
            s.Close(_timeProvider.GetUtcNow().UtcDateTime, _currentUserService.GetCurrentUser().Name ?? "System");
            await _repository.UpdateAsync(s, cancellationToken);
        }

        return null;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~NewToOpenedSideEffectTests"`
Expected: PASS (adjust the `GetPagedListAsync` mock setup to the real signature if step 3's copy differs).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/NewToOpenedSideEffect.cs backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/NewToOpenedSideEffectTests.cs
git commit -m "feat(logistics): extract NewToOpenedSideEffect from ChangeTransportBoxStateHandler"
```

---

### task: extract-open-to-reserve-side-effect

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/OpenToReserveSideEffect.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/OpenToReserveSideEffectTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class OpenToReserveSideEffectTests
{
    private readonly OpenToReserveSideEffect _sut = new();

    [Fact]
    public void Supports_OpenedToReserve_ReturnsTrue()
    {
        _sut.Supports(TransportBoxState.Opened, TransportBoxState.Reserve).Should().BeTrue();
    }

    [Fact]
    public void Supports_AnyOtherPair_ReturnsFalse()
    {
        _sut.Supports(TransportBoxState.Opened, TransportBoxState.Quarantine).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_MissingLocation_ReturnsRequiredFieldMissing()
    {
        var box = new TransportBox();
        var request = new ChangeTransportBoxStateRequest { BoxId = 1, NewState = TransportBoxState.Reserve };

        var result = await _sut.ExecuteAsync(box, request, CancellationToken.None);

        result.Should().NotBeNull();
        result!.ErrorCode.Should().Be(ErrorCodes.RequiredFieldMissing);
        result.Params.Should().Contain("field", "Location");
    }

    [Fact]
    public async Task ExecuteAsync_LocationProvided_ReturnsNull()
    {
        var box = new TransportBox();
        var request = new ChangeTransportBoxStateRequest
        {
            BoxId = 1, NewState = TransportBoxState.Reserve, Location = "A1"
        };

        var result = await _sut.ExecuteAsync(box, request, CancellationToken.None);

        result.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run to verify it fails to compile**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~OpenToReserveSideEffectTests"`
Expected: Build error — type does not exist.

- [ ] **Step 3: Implement `OpenToReserveSideEffect`**

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

public class OpenToReserveSideEffect : ITransportBoxTransitionSideEffect
{
    public bool Supports(TransportBoxState from, TransportBoxState to) =>
        from == TransportBoxState.Opened && to == TransportBoxState.Reserve;

    public Task<ChangeTransportBoxStateResponse?> ExecuteAsync(
        TransportBox box, ChangeTransportBoxStateRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Location))
        {
            return Task.FromResult<ChangeTransportBoxStateResponse?>(new ChangeTransportBoxStateResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.RequiredFieldMissing,
                Params = new Dictionary<string, string> { { "field", "Location" } }
            });
        }

        return Task.FromResult<ChangeTransportBoxStateResponse?>(null);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~OpenToReserveSideEffectTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/OpenToReserveSideEffect.cs backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/OpenToReserveSideEffectTests.cs
git commit -m "feat(logistics): extract OpenToReserveSideEffect from ChangeTransportBoxStateHandler"
```

---

### task: extract-open-to-quarantine-side-effect

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/OpenToQuarantineSideEffect.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/OpenToQuarantineSideEffectTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class OpenToQuarantineSideEffectTests
{
    private readonly OpenToQuarantineSideEffect _sut = new();

    [Fact]
    public void Supports_OpenedToQuarantine_ReturnsTrue()
    {
        _sut.Supports(TransportBoxState.Opened, TransportBoxState.Quarantine).Should().BeTrue();
    }

    [Fact]
    public void Supports_AnyOtherPair_ReturnsFalse()
    {
        _sut.Supports(TransportBoxState.Opened, TransportBoxState.Reserve).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_AlwaysReturnsNull()
    {
        var box = new TransportBox();
        var request = new ChangeTransportBoxStateRequest { BoxId = 1, NewState = TransportBoxState.Quarantine };

        var result = await _sut.ExecuteAsync(box, request, CancellationToken.None);

        result.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run to verify it fails to compile**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~OpenToQuarantineSideEffectTests"`
Expected: Build error — type does not exist.

- [ ] **Step 3: Implement `OpenToQuarantineSideEffect`**

```csharp
using Anela.Heblo.Domain.Features.Logistics.Transport;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

// No location required for Quarantine — ToQuarantine() clears Location = null.
// Kept as an explicit, registered side effect (rather than omitted from dispatch)
// so future Quarantine-entry behavior has one obvious place to be added, and so
// dispatch-uniqueness tests can assert exactly one strategy handles this pair.
public class OpenToQuarantineSideEffect : ITransportBoxTransitionSideEffect
{
    public bool Supports(TransportBoxState from, TransportBoxState to) =>
        from == TransportBoxState.Opened && to == TransportBoxState.Quarantine;

    public Task<ChangeTransportBoxStateResponse?> ExecuteAsync(
        TransportBox box, ChangeTransportBoxStateRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult<ChangeTransportBoxStateResponse?>(null);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~OpenToQuarantineSideEffectTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/OpenToQuarantineSideEffect.cs backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/OpenToQuarantineSideEffectTests.cs
git commit -m "feat(logistics): extract OpenToQuarantineSideEffect from ChangeTransportBoxStateHandler"
```

---

### task: extract-received-side-effect

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ReceivedSideEffect.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ReceivedSideEffectTests.cs`

This moves `HandleReceived`'s body (lines 273–305 of the current handler) unchanged —
including its exact `_logger.LogDebug`/`LogInformation` calls and message templates, and the
`BOX-{box.Id:000000}-{group.ProductCode}` document-number format.

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases;
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class ReceivedSideEffectTests
{
    private readonly Mock<ILogisticsStockOperationService> _stockOperationServiceMock = new();
    private readonly ReceivedSideEffect _sut;

    public ReceivedSideEffectTests()
    {
        _stockOperationServiceMock
            .Setup(x => x.StageOperationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _sut = new ReceivedSideEffect(_stockOperationServiceMock.Object, NullLogger<ReceivedSideEffect>.Instance);
    }

    [Theory]
    [InlineData(TransportBoxState.InTransit)]
    [InlineData(TransportBoxState.Reserve)]
    [InlineData(TransportBoxState.Quarantine)]
    public void Supports_KnownOriginsToReceived_ReturnsTrue(TransportBoxState from)
    {
        _sut.Supports(from, TransportBoxState.Received).Should().BeTrue();
    }

    [Fact]
    public void Supports_NewToReceived_ReturnsFalse()
    {
        _sut.Supports(TransportBoxState.New, TransportBoxState.Received).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_AggregatesItemsByProductCode_StagesOneOperationPerProduct()
    {
        var box = CreateBoxWithItems(("SKU-1", 2.0), ("SKU-1", 3.0), ("SKU-2", 1.0));
        var request = new ChangeTransportBoxStateRequest { BoxId = box.Id, NewState = TransportBoxState.Received };

        var result = await _sut.ExecuteAsync(box, request, CancellationToken.None);

        result.Should().BeNull();
        _stockOperationServiceMock.Verify(x => x.StageOperationAsync(
            $"BOX-{box.Id:000000}-SKU-1", "SKU-1", 5,
            LogisticsStockOperationSource.TransportBox, box.Id, It.IsAny<CancellationToken>()), Times.Once);
        _stockOperationServiceMock.Verify(x => x.StageOperationAsync(
            $"BOX-{box.Id:000000}-SKU-2", "SKU-2", 1,
            LogisticsStockOperationSource.TransportBox, box.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static TransportBox CreateBoxWithItems(params (string ProductCode, double Amount)[] items)
    {
        var box = new TransportBox();
        box.Id = 1;
        var itemsField = typeof(TransportBox).GetField("_items", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var list = (List<TransportBoxItem>)itemsField.GetValue(box)!;
        foreach (var (productCode, amount) in items)
        {
            list.Add(new TransportBoxItem(productCode, "Product", amount, DateTime.UtcNow, "TestUser", null));
        }
        return box;
    }
}
```

> Note: `TransportBoxItem`'s constructor signature and `TransportBox`'s `_items` backing field
> are taken from `ChangeTransportBoxStateHandlerTests.CreateTestBoxWithMultipleItems` — verify
> against the current `TransportBoxItem.cs` before finalizing if this drifts.

- [ ] **Step 2: Run to verify it fails to compile**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ReceivedSideEffectTests"`
Expected: Build error — type does not exist.

- [ ] **Step 3: Implement `ReceivedSideEffect`**

```csharp
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

public class ReceivedSideEffect : ITransportBoxTransitionSideEffect
{
    private readonly ILogisticsStockOperationService _stockOperationService;
    private readonly ILogger<ReceivedSideEffect> _logger;

    public ReceivedSideEffect(ILogisticsStockOperationService stockOperationService, ILogger<ReceivedSideEffect> logger)
    {
        _stockOperationService = stockOperationService;
        _logger = logger;
    }

    public bool Supports(TransportBoxState from, TransportBoxState to) =>
        to == TransportBoxState.Received &&
        (from == TransportBoxState.InTransit || from == TransportBoxState.Reserve || from == TransportBoxState.Quarantine);

    public async Task<ChangeTransportBoxStateResponse?> ExecuteAsync(
        TransportBox box, ChangeTransportBoxStateRequest request, CancellationToken cancellationToken)
    {
        var aggregated = box.Items
            .GroupBy(i => i.ProductCode)
            .Select(g => new
            {
                ProductCode = g.Key,
                Amount = (int)Math.Round(g.Sum(i => i.Amount), MidpointRounding.AwayFromZero),
                LineCount = g.Count()
            })
            .ToList();

        foreach (var group in aggregated)
        {
            var documentNumber = $"BOX-{box.Id:000000}-{group.ProductCode}";

            await _stockOperationService.StageOperationAsync(
                documentNumber,
                group.ProductCode,
                group.Amount,
                LogisticsStockOperationSource.TransportBox,
                box.Id,
                cancellationToken);

            _logger.LogDebug("Staged StockUpOperation {DocumentNumber} for product {ProductCode}, amount {Amount} (aggregated from {LineCount} item line(s))",
                documentNumber, group.ProductCode, group.Amount, group.LineCount);
        }

        _logger.LogInformation("Staged {OperationCount} StockUpOperation(s) from {ItemCount} item line(s) for box {BoxId} ({BoxCode})",
            aggregated.Count, box.Items.Count, box.Id, box.Code);

        return null;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ReceivedSideEffectTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ReceivedSideEffect.cs backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ReceivedSideEffectTests.cs
git commit -m "feat(logistics): extract ReceivedSideEffect from ChangeTransportBoxStateHandler"
```

---

### task: extract-inventory-restorer

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ITransportBoxInventoryRestorer.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/TransportBoxInventoryRestorer.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/TransportBoxInventoryRestorerTests.cs`

This moves `RestoreInventoryForItemsAsync`'s body (lines 307–328 of the current handler)
unchanged into its own collaborator — not a `ITransportBoxTransitionSideEffect` (see
arch-review Decision 3: it is not dispatched by `(from, to)`, it runs unconditionally on the
Opened→New rollback path).

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class TransportBoxInventoryRestorerTests
{
    private readonly Mock<IInventoryReservationService> _inventoryReservationServiceMock = new();
    private readonly TransportBoxInventoryRestorer _sut;

    public TransportBoxInventoryRestorerTests()
    {
        _sut = new TransportBoxInventoryRestorer(_inventoryReservationServiceMock.Object);
    }

    [Fact]
    public async Task RestoreAsync_ItemWithSourceInventoryId_CallsRestore()
    {
        var item = new TransportBoxItem("SKU-1", "Product", 3.0, DateTime.UtcNow, "user", null) { SourceInventoryId = 42 };
        var timestamp = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        await _sut.RestoreAsync(new[] { item }, "tester", timestamp, boxId: 7, boxCode: "B001", CancellationToken.None);

        _inventoryReservationServiceMock.Verify(x => x.RestoreAsync(
            42, 3.0m, "tester", timestamp, 7, "B001", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RestoreAsync_ItemWithoutSourceInventoryId_SkipsRestore()
    {
        var item = new TransportBoxItem("SKU-1", "Product", 3.0, DateTime.UtcNow, "user", null);

        await _sut.RestoreAsync(new[] { item }, "tester", DateTime.UtcNow, boxId: 7, boxCode: "B001", CancellationToken.None);

        _inventoryReservationServiceMock.Verify(x => x.RestoreAsync(
            It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<DateTime>(),
            It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

> Note: verify `TransportBoxItem.SourceInventoryId` is settable (init/property setter) against
> the real type — if it is constructor-only or internal-set, adjust the test's item
> construction to match rather than the illustrative object-initializer above.

- [ ] **Step 2: Run to verify it fails to compile**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~TransportBoxInventoryRestorerTests"`
Expected: Build error — types do not exist.

- [ ] **Step 3: Implement the interface and class**

```csharp
// ITransportBoxInventoryRestorer.cs
using Anela.Heblo.Domain.Features.Logistics.Transport;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

public interface ITransportBoxInventoryRestorer
{
    Task RestoreAsync(
        IReadOnlyList<TransportBoxItem> items,
        string userName,
        DateTime timestamp,
        int boxId,
        string? boxCode,
        CancellationToken cancellationToken);
}
```

```csharp
// TransportBoxInventoryRestorer.cs
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Domain.Features.Logistics.Transport;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

public class TransportBoxInventoryRestorer : ITransportBoxInventoryRestorer
{
    private readonly IInventoryReservationService _inventoryReservationService;

    public TransportBoxInventoryRestorer(IInventoryReservationService inventoryReservationService)
    {
        _inventoryReservationService = inventoryReservationService;
    }

    public async Task RestoreAsync(
        IReadOnlyList<TransportBoxItem> items,
        string userName,
        DateTime timestamp,
        int boxId,
        string? boxCode,
        CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            if (item.SourceInventoryId == null) continue;

            await _inventoryReservationService.RestoreAsync(
                inventoryId: item.SourceInventoryId.Value,
                amount: (decimal)item.Amount,
                userName: userName,
                timestamp: timestamp,
                boxId: boxId,
                boxCode: boxCode,
                cancellationToken: cancellationToken);
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~TransportBoxInventoryRestorerTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ITransportBoxInventoryRestorer.cs backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/TransportBoxInventoryRestorer.cs backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/TransportBoxInventoryRestorerTests.cs
git commit -m "feat(logistics): extract TransportBoxInventoryRestorer from ChangeTransportBoxStateHandler"
```

---

### task: refactor-handler-orchestration

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs` (path as given by current file: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`)

This is the task that actually removes the `CallBackMap` dictionary and the four private
methods plus `RestoreInventoryForItemsAsync`, and wires in the new collaborators. Do this
task only after all five extraction tasks above are committed — the old private methods stay
in place (dead but harmless, since nothing calls them once dispatch changes) until this step
removes them in one clean diff.

- [ ] **Step 1: Update the constructor and remove `CallBackMap`**

Replace the field/constructor block (current lines 12–54) with:

```csharp
public class ChangeTransportBoxStateHandler : IRequestHandler<ChangeTransportBoxStateRequest, ChangeTransportBoxStateResponse>
{
    private readonly ITransportBoxRepository _repository;
    private readonly IMediator _mediator;
    private readonly ILogger<ChangeTransportBoxStateHandler> _logger;
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;
    private readonly IEnumerable<ITransportBoxTransitionSideEffect> _sideEffects;
    private readonly ITransportBoxInventoryRestorer _inventoryRestorer;

    public ChangeTransportBoxStateHandler(
        ITransportBoxRepository repository,
        IMediator mediator,
        ILogger<ChangeTransportBoxStateHandler> logger,
        ICurrentUserService currentUserService,
        TimeProvider timeProvider,
        IEnumerable<ITransportBoxTransitionSideEffect> sideEffects,
        ITransportBoxInventoryRestorer inventoryRestorer)
    {
        _repository = repository;
        _mediator = mediator;
        _logger = logger;
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
        _sideEffects = sideEffects;
        _inventoryRestorer = inventoryRestorer;
    }
```

Remove: the `CallBackMap` static field entirely, and the `IInventoryReservationService` /
`ILogisticsStockOperationService` fields and constructor parameters (they are no longer used
directly by the handler).

- [ ] **Step 2: Replace the dispatch block inside `Handle()`**

Replace:

```csharp
            if (CallBackMap.TryGetValue(new Tuple<TransportBoxState, TransportBoxState>(box.State, request.NewState), out var callbackFactory))
            {
                var callback = callbackFactory(this);
                var callbackResult = await callback(box, request, cancellationToken);
                if (callbackResult != null)
                {
                    return callbackResult;
                }
            }
```

with:

```csharp
            var sideEffect = _sideEffects.FirstOrDefault(s => s.Supports(box.State, request.NewState));
            if (sideEffect != null)
            {
                var sideEffectResult = await sideEffect.ExecuteAsync(box, request, cancellationToken);
                if (sideEffectResult != null)
                {
                    return sideEffectResult;
                }
            }
```

- [ ] **Step 3: Replace the inventory-restore call site**

Replace:

```csharp
            if (itemsToRestore != null)
            {
                await RestoreInventoryForItemsAsync(itemsToRestore, userName, currentTime, box.Id, box.Code, cancellationToken);
            }
```

with:

```csharp
            if (itemsToRestore != null)
            {
                await _inventoryRestorer.RestoreAsync(itemsToRestore, userName, currentTime, box.Id, box.Code, cancellationToken);
            }
```

- [ ] **Step 4: Delete the now-unused private methods**

Delete `HandleNewToOpened`, `HandleOpenToQuarantine`, `HandleOpenToReserve`, `HandleReceived`,
and `RestoreInventoryForItemsAsync` in full (everything from the line
`private async Task<ChangeTransportBoxStateResponse?> HandleNewToOpened(...)` to the closing
brace of `RestoreInventoryForItemsAsync`, i.e. through the end of the class body before the
final `}`).

- [ ] **Step 5: Build**

Run: `cd backend && dotnet build src/Anela.Heblo.Application`
Expected: Build succeeded (existing test project will fail to build until the next task
updates its constructor calls — that's expected and fixed next).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs
git commit -m "refactor(logistics): reduce ChangeTransportBoxStateHandler to orchestration only"
```

---

### task: register-di

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs`

- [ ] **Step 1: Add the new registrations**

In `AddLogisticsModule()`, immediately after the existing
`services.AddTransient<ITransportBoxCompletionService, TransportBoxCompletionService>();` line,
add:

```csharp
        // Register transport box state-transition side effects (dispatched by
        // ChangeTransportBoxStateHandler via IEnumerable<ITransportBoxTransitionSideEffect>)
        services.AddTransient<UseCases.ChangeTransportBoxState.ITransportBoxTransitionSideEffect, UseCases.ChangeTransportBoxState.NewToOpenedSideEffect>();
        services.AddTransient<UseCases.ChangeTransportBoxState.ITransportBoxTransitionSideEffect, UseCases.ChangeTransportBoxState.OpenToReserveSideEffect>();
        services.AddTransient<UseCases.ChangeTransportBoxState.ITransportBoxTransitionSideEffect, UseCases.ChangeTransportBoxState.OpenToQuarantineSideEffect>();
        services.AddTransient<UseCases.ChangeTransportBoxState.ITransportBoxTransitionSideEffect, UseCases.ChangeTransportBoxState.ReceivedSideEffect>();
        services.AddTransient<UseCases.ChangeTransportBoxState.ITransportBoxInventoryRestorer, UseCases.ChangeTransportBoxState.TransportBoxInventoryRestorer>();
```

(Fully-qualify the types as shown, or add a
`using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;` at the top
of the file and drop the `UseCases.ChangeTransportBoxState.` prefix — match whichever style the
file's existing `using` block favors; the file currently has no `using` for this namespace, so
either is acceptable, but prefer adding the `using` for readability since five types are
referenced.)

- [ ] **Step 2: Build**

Run: `cd backend && dotnet build src/Anela.Heblo.Application`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs
git commit -m "feat(logistics): register transition side-effect and inventory-restorer DI"
```

---

### task: update-existing-tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs`

The handler's constructor shape changed (task `refactor-handler-orchestration`). Both test
files construct it directly and must be updated to match — **without changing any existing
assertion**, per spec NFR-4. Since the handler no longer talks to
`IInventoryReservationService`/`ILogisticsStockOperationService` directly, and side effects are
now resolved via `IEnumerable<ITransportBoxTransitionSideEffect>`, real (non-mocked) instances
of the four side effects and the restorer are wired up in the test constructors so that
existing behavior-level assertions (e.g. `HandleReceived`'s staging behavior, the code-required
error, etc.) keep passing unmodified.

- [ ] **Step 1: Update `ChangeTransportBoxStateHandlerTests` constructor**

Replace the `_handler = new ChangeTransportBoxStateHandler(...)` block (and the fields it
depends on) with real side-effect instances built from the existing mocks, so every existing
`[Fact]`/`[Theory]` in this file keeps exercising the same mocked dependencies it did before:

```csharp
    private readonly Mock<ITransportBoxRepository> _repositoryMock;
    private readonly Mock<IInventoryReservationService> _inventoryReservationServiceMock;
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<ILogger<ChangeTransportBoxStateHandler>> _loggerMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ILogisticsStockOperationService> _stockUpProcessingServiceMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly ChangeTransportBoxStateHandler _handler;

    public ChangeTransportBoxStateHandlerTests()
    {
        _repositoryMock = new Mock<ITransportBoxRepository>();
        _inventoryReservationServiceMock = new Mock<IInventoryReservationService>();
        _mediatorMock = new Mock<IMediator>();
        _loggerMock = new Mock<ILogger<ChangeTransportBoxStateHandler>>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _stockUpProcessingServiceMock = new Mock<ILogisticsStockOperationService>();
        _timeProviderMock = new Mock<TimeProvider>();

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("test-user", "Test User", "test@example.com", true));

        _timeProviderMock
            .Setup(x => x.GetUtcNow())
            .Returns(new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero));

        _stockUpProcessingServiceMock
            .Setup(x => x.StageOperationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sideEffects = new ITransportBoxTransitionSideEffect[]
        {
            new NewToOpenedSideEffect(_repositoryMock.Object, _currentUserServiceMock.Object, _timeProviderMock.Object),
            new OpenToReserveSideEffect(),
            new OpenToQuarantineSideEffect(),
            new ReceivedSideEffect(_stockUpProcessingServiceMock.Object, NullLogger<ReceivedSideEffect>.Instance),
        };
        var inventoryRestorer = new TransportBoxInventoryRestorer(_inventoryReservationServiceMock.Object);

        _handler = new ChangeTransportBoxStateHandler(
            _repositoryMock.Object,
            _mediatorMock.Object,
            _loggerMock.Object,
            _currentUserServiceMock.Object,
            _timeProviderMock.Object,
            sideEffects,
            inventoryRestorer);
    }
```

Add `using Microsoft.Extensions.Logging.Abstractions;` to this file's `using` block for
`NullLogger<T>` if not already present.

- [ ] **Step 2: Update `ChangeTransportBoxStateReceiveAtomicityIntegrationTests.CreateHandler`**

Replace the `return new ChangeTransportBoxStateHandler(...)` block with:

```csharp
        var sideEffects = new ITransportBoxTransitionSideEffect[]
        {
            new NewToOpenedSideEffect(transportBoxRepository, currentUserService.Object, TimeProvider.System),
            new OpenToReserveSideEffect(),
            new OpenToQuarantineSideEffect(),
            new ReceivedSideEffect(adapter, NullLogger<ReceivedSideEffect>.Instance),
        };
        var inventoryRestorer = new TransportBoxInventoryRestorer(Mock.Of<IInventoryReservationService>());

        return new ChangeTransportBoxStateHandler(
            transportBoxRepository,
            mediator.Object,
            NullLogger<ChangeTransportBoxStateHandler>.Instance,
            currentUserService.Object,
            TimeProvider.System,
            sideEffects,
            inventoryRestorer);
```

- [ ] **Step 3: Build and run both test files**

Run: `cd backend && dotnet build test/Anela.Heblo.Tests`
Expected: Build succeeded.

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ChangeTransportBoxState"`
Expected: All existing tests in both files PASS, unmodified in their assertions (the
integration test file requires the shared Postgres test container — see
`docs/testing/testing-strategy.md` / `PostgresSharedContainerFixture` for how it's normally
run in this repo; if the container isn't available in the current environment, at minimum
confirm the file builds and skip execution, noting this in the task's completion note).

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs
git commit -m "test(logistics): update ChangeTransportBoxStateHandler constructor call sites"
```

---

### task: add-dispatch-uniqueness-test

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/TransportBoxTransitionSideEffectDispatchTests.cs`

Guards against the risk flagged in arch-review.r1.md: two registered side effects both
claiming the same `(from, to)` pair, which would make dispatch order silently significant.

- [ ] **Step 1: Write the test**

```csharp
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class TransportBoxTransitionSideEffectDispatchTests
{
    private static readonly (TransportBoxState From, TransportBoxState To)[] KnownPairs =
    {
        (TransportBoxState.New, TransportBoxState.Opened),
        (TransportBoxState.Opened, TransportBoxState.Reserve),
        (TransportBoxState.Opened, TransportBoxState.Quarantine),
        (TransportBoxState.InTransit, TransportBoxState.Received),
        (TransportBoxState.Reserve, TransportBoxState.Received),
        (TransportBoxState.Quarantine, TransportBoxState.Received),
    };

    private static IReadOnlyList<ITransportBoxTransitionSideEffect> AllSideEffects() => new ITransportBoxTransitionSideEffect[]
    {
        new NewToOpenedSideEffect(Mock.Of<ITransportBoxRepository>(), Mock.Of<ICurrentUserService>(), Mock.Of<TimeProvider>()),
        new OpenToReserveSideEffect(),
        new OpenToQuarantineSideEffect(),
        new ReceivedSideEffect(Mock.Of<ILogisticsStockOperationService>(), NullLogger<ReceivedSideEffect>.Instance),
    };

    [Theory]
    [MemberData(nameof(KnownPairsData))]
    public void ExactlyOneSideEffectSupports_EachKnownTransitionPair(TransportBoxState from, TransportBoxState to)
    {
        var matches = AllSideEffects().Count(s => s.Supports(from, to));
        matches.Should().Be(1, $"exactly one side effect should handle ({from} -> {to})");
    }

    public static IEnumerable<object[]> KnownPairsData() => KnownPairs.Select(p => new object[] { p.From, p.To });
}
```

(Add `using Anela.Heblo.Application.Features.Logistics.Contracts;` and
`using Anela.Heblo.Domain.Features.Users;` if `ICurrentUserService` / other referenced types
require them per their actual namespaces.)

- [ ] **Step 2: Run to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~TransportBoxTransitionSideEffectDispatchTests"`
Expected: PASS — 6 known pairs, each matched by exactly one side effect.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/TransportBoxTransitionSideEffectDispatchTests.cs
git commit -m "test(logistics): guard against overlapping transition side-effect dispatch"
```

---

### task: full-verification

**Files:** none (verification only)

- [ ] **Step 1: Full backend build**

Run: `cd backend && dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Format check**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: No formatting violations. If violations are reported, run `dotnet format` (without
`--verify-no-changes`) and commit the formatting fix separately.

- [ ] **Step 3: Full Logistics test run**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~Logistics"`
Expected: All tests pass, including the six new test files and both updated existing test
files.

- [ ] **Step 4: Full solution test run**

Run: `cd backend && dotnet test`
Expected: All tests pass (confirms no unrelated regression from the DI/constructor changes).

- [ ] **Step 5: Commit (only if step 2 produced formatting fixes not yet committed)**

```bash
git add -A
git commit -m "chore(logistics): apply dotnet format"
```

## Self-Review Notes

**Spec coverage:** FR-1 (per-transition side effects as isolated units) → tasks
`extract-*-side-effect`. FR-2 (handler keeps only orchestration) → task
`refactor-handler-orchestration`. FR-3 (extending transitions needs no handler edit) →
satisfied by the `IEnumerable<ITransportBoxTransitionSideEffect>` + DI registration mechanism
established across `create-side-effect-interface` and `register-di`. FR-4
(`RestoreInventoryForItemsAsync` placement) → task `extract-inventory-restorer` per arch-review
Decision 3. NFR-4 (existing tests keep passing unmodified in assertions) → task
`update-existing-tests`. The arch-review's dispatch-uniqueness risk → task
`add-dispatch-uniqueness-test`.

**Type consistency:** `ITransportBoxTransitionSideEffect.ExecuteAsync` and
`ITransportBoxInventoryRestorer.RestoreAsync` signatures are defined once in
`create-side-effect-interface` / `extract-inventory-restorer` and reused verbatim by every
later task (`refactor-handler-orchestration`, `update-existing-tests`,
`add-dispatch-uniqueness-test`) — no drift between them.

**Verification caveat:** exact signatures of `ITransportBoxRepository.GetPagedListAsync` and
`TransportBoxItem`'s constructor/`SourceInventoryId` mutability are referenced from what this
plan's author read in the current source; the implementing engineer must confirm these against
the live files (flagged inline at each usage) before treating a mismatch as a plan defect.
