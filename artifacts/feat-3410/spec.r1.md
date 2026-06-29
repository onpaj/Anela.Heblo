# Specification: Coverage Gap – ChangeTransportBoxStateHandler (Opened→Reserve and Reserve→Received)

## Summary

`ChangeTransportBoxStateHandler` has 25.2% line coverage against a 60% threshold. Two live code paths are entirely untested: the `Opened→Reserve` transition (guarded by a mandatory `Location` field) and the `Reserve→Received` path through `HandleReceived` (which aggregates items by product code and creates stock-up operations). Adding three focused unit tests closes both gaps and raises coverage above the threshold.

## Background

The handler uses a `CallBackMap` keyed on `(currentState, targetState)` tuples to dispatch state-specific side effects before executing a domain transition. Thirteen existing tests cover the handler's other branches: `BoxNotFound`, `New→Opened`, `Opened→InTransit`, `Opened→Quarantine`, `Quarantine→Received`, `InTransit→Received` (with weight rounding and product-code aggregation), and `Opened→New` (inventory restore).

Two entries in `CallBackMap` have no test coverage at all:

- `(Opened, Reserve)` → `HandleOpenToReserve` — validates that `request.Location` is non-empty and returns `RequiredFieldMissing` with `field=Location` if it is not. Without a test, a regression that removes or weakens this guard would go undetected, causing reserved boxes to enter the system without a storage location and silently breaking all "find stock at location X" queries.
- `(Reserve, Received)` → `HandleReceived` — shares the same aggregation and rounding logic already tested for `InTransit→Received` and `Quarantine→Received`, but the entry point via Reserve state is untested. Reserved shipments following the Opened→Reserve→Received workflow are in production use.

## Functional Requirements

### FR-1: Test – Opened→Reserve with empty Location returns RequiredFieldMissing

When `ChangeTransportBoxStateRequest.Location` is null or empty and the box is in `Opened` state transitioning to `Reserve`, the handler must return a failure response before executing any state change.

**Acceptance criteria:**
- Arrange: box in `Opened` state; request with `NewState = Reserve` and `Location = null` (or empty string).
- `result.Success` is `false`.
- `result.ErrorCode` equals `ErrorCodes.RequiredFieldMissing`.
- `result.Params` contains key `"field"` with value `"Location"`.
- `_repositoryMock.Verify(x => x.UpdateAsync(...), Times.Never)` — no persistence occurs.
- `_stockUpProcessingServiceMock.Verify(x => x.CreateOperationAsync(...), Times.Never)` — no stock operations created.

### FR-2: Test – Opened→Reserve with valid Location succeeds and persists location

When `request.Location` is a non-empty string and the box is in `Opened` state transitioning to `Reserve`, the handler must proceed through the state transition and return success.

**Acceptance criteria:**
- Arrange: box in `Opened` state; request with `NewState = Reserve` and `Location = "SHELF-A1"` (any non-empty string).
- `result.Success` is `true`.
- `result.ErrorCode` is null.
- `result.UpdatedBox` is the response returned by the `GetTransportBoxById` mediator call.
- `_repositoryMock.Verify(x => x.UpdateAsync(...), Times.Once)` — the updated box is persisted.
- `_mediatorMock.Verify(x => x.Send(It.IsAny<GetTransportBoxByIdRequest>(), ...), Times.Once)` — the refreshed box is fetched after the transition.

### FR-3: Test – Reserve→Received aggregates items by ProductCode and creates stock-up operations

When a box in `Reserve` state transitions to `Received`, `HandleReceived` must aggregate box items by `ProductCode`, round fractional totals with `MidpointRounding.AwayFromZero`, and call `ILogisticsStockOperationService.CreateOperationAsync` once per distinct product code.

This test mirrors the existing `Handle_InTransitToReceived_AggregatesDuplicateProductCodes_IntoSingleStockUpOperation` test but uses `TransportBoxState.Reserve` as the starting state.

**Acceptance criteria:**
- Arrange: box in `Reserve` state with two item lines for the same `ProductCode` (e.g. `"P-001"`, amounts `3.0` and `5.0`).
- `result.Success` is `true`.
- `_stockUpProcessingServiceMock.Verify(x => x.CreateOperationAsync("BOX-000001-P-001", "P-001", 8, LogisticsStockOperationSource.TransportBox, 1, ...), Times.Once)` — amounts are summed to 8.
- `_stockUpProcessingServiceMock.Verify(x => x.CreateOperationAsync(Any, Any, Any, Any, Any, Any), Times.Once)` — exactly one operation is created (not two).

## Non-Functional Requirements

### NFR-1: Test Coverage

Adding the three tests described above must bring `ChangeTransportBoxStateHandler` line coverage to at least 60%, satisfying the project coverage threshold. No changes to production code are required or permitted as part of this feature.

### NFR-2: Test style consistency

New tests must follow the conventions established in `ChangeTransportBoxStateHandlerTests.cs`:
- Use `CreateTestBox(TransportBoxState.Reserve)` (existing helper, already handles arbitrary states via reflection).
- Use `CreateTestBoxWithMultipleItems` for FR-3.
- Use `SetupReceivedTransitionMocks` for FR-3 repository/mediator setup.
- FluentAssertions (`result.Success.Should().BeTrue()`, etc.).
- xUnit `[Fact]` attributes.
- Test method names follow the pattern `Handle_{Scenario}_{ExpectedOutcome}`.

### NFR-3: No production code changes

All three requirements are satisfied by adding test methods only. The handler implementation, domain model, request/response contracts, and repository interfaces must not be modified.

## Data Model

No new entities or schema changes. The relevant types are:

| Type | Role |
|---|---|
| `TransportBox` | Aggregate root; `State`, `Location`, `Items` are the fields exercised by these tests |
| `TransportBoxItem` | Value object inside `TransportBox._items`; has `ProductCode` and `Amount` |
| `ChangeTransportBoxStateRequest` | MediatR request; `BoxId`, `NewState`, `Location` are the fields under test |
| `ChangeTransportBoxStateResponse` | Result; `Success`, `ErrorCode`, `Params`, `UpdatedBox` |
| `ErrorCodes.RequiredFieldMissing` | Error code returned by `HandleOpenToReserve` when `Location` is absent |
| `LogisticsStockOperationSource.TransportBox` | Enum value passed to `CreateOperationAsync` |

## Out of Scope

- Changes to the `HandleOpenToReserve` or `HandleReceived` production implementations.
- Testing any other state transitions not listed above (already covered or out of coverage scope for this feature).
- Integration or E2E tests for these transitions.
- Frontend changes.
- Database migrations.

## Open Questions

None.

## Status: COMPLETE
