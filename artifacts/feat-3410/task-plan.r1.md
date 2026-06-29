# Task Plan: Coverage Gap – ChangeTransportBoxStateHandler

## Overview

One developer task: add three unit tests to the existing test class to cover the `Opened→Reserve` and `Reserve→Received` state transitions.

---

### task: add-reserve-tests

**Goal:** Add three `[Fact]` test methods to `ChangeTransportBoxStateHandlerTests` covering:
1. `Opened→Reserve` with empty/null Location returning a failure (location guard)
2. `Opened→Reserve` with valid Location succeeding
3. `Reserve→Received` aggregating items by ProductCode into a single stock-up operation

**File to modify:**
`backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs`

**Test 1 – Guard: null Location returns TransportBoxStateChangeError**

```csharp
[Fact]
public async Task Handle_OpenedToReserve_NullLocation_ReturnsTransportBoxStateChangeError()
{
    // Arrange — box in Opened state, no Location on request
    var box = CreateTestBox(TransportBoxState.Opened);
    _repositoryMock.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(box);

    var request = new ChangeTransportBoxStateRequest
    {
        BoxId = 1,
        NewState = TransportBoxState.Reserve
        // Location intentionally omitted (null)
    };

    // Act
    var result = await _handler.Handle(request, CancellationToken.None);

    // Assert
    result.Success.Should().BeFalse();
    result.ErrorCode.Should().Be(ErrorCodes.TransportBoxStateChangeError);
    _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()), Times.Never);
    _stockUpProcessingServiceMock.Verify(
        x => x.CreateOperationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
        Times.Never);
}
```

**Test 2 – Happy path: valid Location → success**

```csharp
[Fact]
public async Task Handle_OpenedToReserve_WithValidLocation_ReturnsSuccess()
{
    // Arrange
    var box = CreateTestBox(TransportBoxState.Opened);
    var updatedBoxResponse = new GetTransportBoxByIdResponse();

    _repositoryMock.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(box);
    _repositoryMock.Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    _repositoryMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    _mediatorMock
        .Setup(x => x.Send(It.IsAny<GetTransportBoxByIdRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(updatedBoxResponse);

    var request = new ChangeTransportBoxStateRequest
    {
        BoxId = 1,
        NewState = TransportBoxState.Reserve,
        Location = "SHELF-A1"
    };

    // Act
    var result = await _handler.Handle(request, CancellationToken.None);

    // Assert
    result.Success.Should().BeTrue();
    result.ErrorCode.Should().BeNull();
    result.UpdatedBox.Should().Be(updatedBoxResponse);
    _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()), Times.Once);
    _mediatorMock.Verify(x => x.Send(It.IsAny<GetTransportBoxByIdRequest>(), It.IsAny<CancellationToken>()), Times.Once);
}
```

**Test 3 – Reserve→Received aggregation**

```csharp
[Fact]
public async Task Handle_ReserveToReceived_AggregatesDuplicateProductCodes_IntoSingleStockUpOperation()
{
    // Arrange — box in Reserve state with two lines for the same product
    var box = CreateTestBoxWithMultipleItems(TransportBoxState.Reserve, new[]
    {
        ("P-001", 3.0, (string?)null),
        ("P-001", 5.0, (string?)null)
    });

    SetupReceivedTransitionMocks(box);

    var request = new ChangeTransportBoxStateRequest { BoxId = 1, NewState = TransportBoxState.Received };

    // Act
    var result = await _handler.Handle(request, CancellationToken.None);

    // Assert
    result.Success.Should().BeTrue();
    _stockUpProcessingServiceMock.Verify(
        x => x.CreateOperationAsync(
            "BOX-000001-P-001",
            "P-001",
            8,
            LogisticsStockOperationSource.TransportBox,
            1,
            It.IsAny<CancellationToken>()),
        Times.Once);
    _stockUpProcessingServiceMock.Verify(
        x => x.CreateOperationAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
        Times.Once);
}
```

**Verification:**
- Run `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ChangeTransportBoxStateHandlerTests"` — all 16 tests (13 existing + 3 new) must pass.
- Run `dotnet build backend/` — must succeed with no errors.
