# Code Review: feat-3410

## Summary

Three unit tests were added to `ChangeTransportBoxStateHandlerTests` to cover previously untested paths in `ChangeTransportBoxStateHandler`. No production code was changed. The tests target: `Opened→Reserve` with null Location (guard path), `Opened→Reserve` with valid Location (happy path), and `Reserve→Received` with duplicate ProductCodes (aggregation path). All three align with the spec intent.

## Review Result: CLEAN

## Blocking

- None

## Advisory

### Test 1 — `Handle_OpenedToReserve_NullLocation_ReturnsTransportBoxStateChangeError`

The test comment says "condition b => b.Location != null fails before callback is reached", but the production code does not reach the condition guard here. Looking at the handler and the domain:

1. `AssignLocationIfAny(null)` is a no-op, so `box.Location` stays null.
2. `transition.Condition` on the `Opened→Reserve` arc is `b => b.Location != null` — this evaluates false → the handler returns early with `TransportBoxStateChangeError`. The test assertion is correct.
3. However, `HandleOpenToReserve` (the callback) also independently returns `RequiredFieldMissing` when `request.Location` is null. That callback is never reached in this scenario, so the observed error code (`TransportBoxStateChangeError`) is correct, but the comment ("condition... fails before callback is reached") is accurate and the tested error code is correct.

The assertion `result.ErrorCode.Should().Be(ErrorCodes.TransportBoxStateChangeError)` is correct given the control flow. No change needed, but the comment could be more precise: the condition being evaluated is on the domain transition node, not the handler callback.

### Test 2 — `Handle_OpenedToReserve_WithValidLocation_ReturnsSuccess`

The test is well-structured and follows existing conventions. One observation: `CreateTestBox` sets `box.Location` to null by default and does not set `box.Id`. The `AssignLocationIfAny("SHELF-A1")` call in the handler sets `box.Location = "SHELF-A1"` before the condition is checked, so the transition condition `b => b.Location != null` passes. This is the intended code path and the test correctly validates it.

The test does not set `box.Id = 1` explicitly, but `CreateTestBox` returns a box whose `Id` defaults to `0` (EF Core default), while the repository mock is keyed on `GetByIdWithDetailsAsync(1)`. The handler calls `GetByIdWithDetailsAsync(request.BoxId)` where `request.BoxId = 1`, so the mock match is on the request value, not the box entity's `Id`. This is fine and consistent with other tests in the file.

### Test 3 — `Handle_ReserveToReceived_AggregatesDuplicateProductCodes_IntoSingleStockUpOperation`

This test closely mirrors the existing `Handle_InTransitToReceived_AggregatesDuplicateProductCodes_IntoSingleStockUpOperation` test, which is the intended coverage: proving the same `HandleReceived` aggregation logic applies regardless of whether the prior state was `InTransit` or `Reserve`. The assertion on both the exact `CreateOperationAsync` call signature and the `Times.Once` total invocation count is correct and thorough.

The items are constructed with `null` lot numbers, which matches the `CreateTestBoxWithMultipleItems` helper signature. The expected document number `"BOX-000001-P-001"` is derived from `$"BOX-{box.Id:000000}-{group.ProductCode}"` — `box.Id` is set to `1` inside `CreateTestBoxWithMultipleItems`, so `000001` is correct.

`SetupReceivedTransitionMocks` returns a `GetTransportBoxByIdResponse` with a non-null `TransportBox` property, which diverges slightly from the two new tests (test 1 uses no mediator call; test 2 sets up its own mediator mock with a bare `new GetTransportBoxByIdResponse()`). The variation is harmless since the return value is not asserted in test 3.

### Convention adherence

All three tests use `[Fact]`, xUnit async pattern, FluentAssertions, `CreateTestBox`/`CreateTestBoxWithMultipleItems` helpers, and `SetupReceivedTransitionMocks` where appropriate. This is fully consistent with the existing 11 tests in the file.

### No production code changes

Confirmed: the diff touches only the test file. No accidental production code drift.
