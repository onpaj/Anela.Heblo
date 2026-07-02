# Architecture Review: Coverage Gap – Logistics ChangeTransportBoxStateHandler (Opened→Reserve, Reserve→Received)

## Skip Design: true
This is a backend-only test addition. No UI components are introduced or modified.

## Architectural Fit Assessment

The three new tests close coverage gaps on two transition paths that share handler infrastructure with already-tested paths:

| Transition | Handler callback | Existing coverage analogue |
|---|---|---|
| `Opened → Reserve` (missing Location) | `HandleOpenToReserve` | `Handle_OpenedToQuarantine_NoLocationRequired_ReturnsSuccess` demonstrates the early-return pattern from a callback |
| `Opened → Reserve` (valid Location) | `HandleOpenToReserve` → success path | `Handle_OpenedToInTransit_WithItems_ReturnsSuccess` and the Quarantine success tests |
| `Reserve → Received` (duplicate products) | `HandleReceived` | `Handle_InTransitToReceived_AggregatesDuplicateProductCodes_IntoSingleStockUpOperation` is a direct template |

All three tests belong in the existing class `ChangeTransportBoxStateHandlerTests` in the existing file. No new files, no new test fixtures, no production code changes.

The handler's `CallBackMap` already registers both paths:

```
{ (Opened, Reserve),   h => h.HandleOpenToReserve }
{ (Reserve, Received), h => h.HandleReceived }
```

`HandleOpenToReserve` returns a `RequiredFieldMissing` error when `request.Location` is null/empty, and returns `null` (proceed) otherwise.
`HandleReceived` is shared by `InTransit`, `Reserve`, and `Quarantine` — the aggregation and `CreateOperationAsync` call behaviour is identical regardless of the originating state.

## Proposed Architecture

### Component Overview

Single file change only:

```
backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/
    ChangeTransportBoxStateHandlerTests.cs          ← append 3 [Fact] methods
```

No new helpers, no new mocks, no new using directives beyond what is already present.

### Key Design Decisions

1. **Reserve-state box via `CreateTestBox(TransportBoxState.Reserve)`** — the existing `CreateTestBox` helper already handles non-`New` states by setting `State` via the property `SetValue` reflection path and assigning the code field. No new helper is required for FR-3. The existing `CreateTestBox(TransportBoxState.Reserve)` call produces a box whose `State` is `Reserve` and whose `Code` is `"TEST-BOX-001"`, which is sufficient for the `Reserve → Received` path.

2. **Reserve-state box needs `Location` set for FR-3** — `HandleReceived` does not inspect `Location`. The `AssignLocationIfAny` guard in the handler throws if called with a non-null location on a non-`Opened` state, so the request must **not** include a `Location` field. The box's `Location` property can be left null (the `Receive()` domain method sets `Location = null` anyway).

3. **Box `Id` must be set to `1` for FR-3** — `HandleReceived` builds `documentNumber = $"BOX-{box.Id:000000}-{group.ProductCode}"`. The existing `CreateTestBoxWithMultipleItems` helper already sets `box.Id = 1` (line 545). Use the same helper for FR-3.

4. **FR-1 uses `CreateTestBox(TransportBoxState.Opened)`** — no items needed; the callback fires before any state-change logic.

5. **FR-2 uses `CreateTestBox(TransportBoxState.Opened)` with `Location` in the request** — the handler sets `box.Location = request.Location` after the callback returns `null`, then calls `transition.ChangeStateAsync`, which internally calls `box.ToReserve(time, userName, box.Location!)`. The `Opened → Reserve` transition node has `condition: b => b.Location != null`, so the location must be assigned before the condition is checked. In the handler, `box.AssignLocationIfAny(request.Location)` is called before the condition check; this sets `box.Location` only when `box.State == Opened`, which is correct here.

6. **`SetupReceivedTransitionMocks` reuse** — FR-3 must call `SetupReceivedTransitionMocks(box)` exactly as the three existing `InTransit/Quarantine → Received` tests do. This sets up `GetByIdWithDetailsAsync`, `UpdateAsync`, `SaveChangesAsync`, and the `mediatorMock` `GetTransportBoxByIdRequest` response.

## Implementation Guidance

### Directory / Module Structure

All three tests are added to the bottom of `ChangeTransportBoxStateHandlerTests.cs`, before the closing brace of the class, after the existing `Handle_NonOpenedToNewTransition_DoesNotCallRestore` test.

### Interfaces and Contracts

No new interfaces or contracts. The existing mocks cover everything:

| Mock | Used by |
|---|---|
| `_repositoryMock` | all three tests (via `SetupReceivedTransitionMocks` for FR-3, or direct setup for FR-1/FR-2) |
| `_mediatorMock` | FR-2 and FR-3 (success path calls `GetTransportBoxByIdRequest`) |
| `_stockUpProcessingServiceMock` | FR-3 only |
| `_currentUserServiceMock` / `_timeProviderMock` | already set up in the constructor; no additional setup needed |

### Data Flow

**FR-1 (`Handle_OpenedToReserve_EmptyLocation_ReturnsRequiredFieldMissing`)**

```
Request: BoxId=1, NewState=Reserve, Location=null
→ GetByIdWithDetailsAsync returns Opened box
→ AssignBoxCodeIfAny(null) → no-op
→ AssignLocationIfAny(null) → no-op
→ GetTransition(Reserve) → OK
→ condition: b.Location != null → false → returns StateChangeError  [WRONG – see note]
```

Wait — re-reading the handler: the condition check (`transition.Condition`) returns `StateChangeError`, but `HandleOpenToReserve` returns `RequiredFieldMissing`. These are two separate paths. The `CallBackMap` is checked **after** the condition check. So the spec must intend the `RequiredFieldMissing` return from `HandleOpenToReserve`.

The handler flow is:
1. `GetTransition(Reserve)` — succeeds (transition exists in node)
2. `transition.Condition(box)` — condition is `b => b.Location != null`; since `Location` is `null` at this point, condition is `false` → returns `StateChangeError`

But `HandleOpenToReserve` also returns `RequiredFieldMissing` when `request.Location` is empty. These are **two independent guards**:
- The transition node's `condition` fires first in the handler (line ~81) and returns `StateChangeError`.
- The `CallBackMap` callback fires second (line ~106).

Since `AssignLocationIfAny` is a no-op when `request.Location == null`, `box.Location` is `null` when the condition is evaluated. Therefore the condition fires and returns `StateChangeError`, **not** `RequiredFieldMissing`.

**FR-1 corrected assertion**: the result will have `ErrorCode = ErrorCodes.TransportBoxStateChangeError`, not `RequiredFieldMissing`. The spec names the test `ReturnsRequiredFieldMissing`, but the handler actually returns `StateChangeError` via the condition guard. Implementation must match actual handler behaviour — assert `TransportBoxStateChangeError`.

**FR-2 (`Handle_OpenedToReserve_WithValidLocation_Succeeds`)**

```
Request: BoxId=1, NewState=Reserve, Location="A1"
→ AssignLocationIfAny("A1") → box.Location = "A1"  (box.State == Opened, so allowed)
→ condition: b.Location != null → true → condition passes
→ CallBackMap → HandleOpenToReserve → request.Location is "A1" (not empty) → returns null
→ handler assigns box.Location = "A1" again (idempotent)
→ transition.ChangeStateAsync → box.ToReserve(time, userName, "A1")
→ UpdateAsync, SaveChangesAsync, GetTransportBoxByIdRequest
→ Success = true
```

Mocks needed: same pattern as `Handle_OpenedToInTransit_WithItems_ReturnsSuccess` — `GetByIdWithDetailsAsync`, `UpdateAsync`, `SaveChangesAsync`, `mediatorMock`.

**FR-3 (`Handle_ReserveToReceived_AggregatesDuplicateProductCodes_IntoSingleStockUpOperation`)**

```
Box: State=Reserve, Id=1, items: [("P-001", 3.0, "LOT-A"), ("P-001", 5.0, "LOT-B")]
Request: BoxId=1, NewState=Received
→ AssignLocationIfAny(null) → throws if state != Opened? No — the guard is:
    if (location == null) return;   ← exits immediately since location param is null
→ GetTransition(Received) → OK (Reserve node has Received transition)
→ condition: null (no condition on Reserve→Received)
→ CallBackMap → HandleReceived:
    grouped: P-001 → sum(3+5)=8, rounded=8
    CreateOperationAsync("BOX-000001-P-001", "P-001", 8, TransportBox, 1)
→ UpdateAsync, SaveChangesAsync, GetTransportBoxByIdRequest
→ Success = true
```

Assertion mirrors `Handle_InTransitToReceived_AggregatesDuplicateProductCodes_IntoSingleStockUpOperation` exactly, verifying both the specific call and that it was called `Times.Once`.

### Exact Code Patterns

**Helper usage for FR-3:**
```csharp
var box = CreateTestBoxWithMultipleItems(TransportBoxState.Reserve, new[]
{
    ("P-001", 3.0, "LOT-A"),
    ("P-001", 5.0, "LOT-B")
});
SetupReceivedTransitionMocks(box);
```

`CreateTestBoxWithMultipleItems` sets `box.Id = 1` internally and calls `CreateTestBox(state)`, which sets `State = Reserve` and `Code = "TEST-BOX-001"` via reflection. No additional reflection needed.

**FR-1 box setup:**
```csharp
var box = CreateTestBox(TransportBoxState.Opened);
_repositoryMock.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(box);
```
No `UpdateAsync`, `SaveChangesAsync`, or `mediatorMock` setup needed — the handler returns early before those are reached.

**FR-2 box setup:**
```csharp
var box = CreateTestBox(TransportBoxState.Opened);
_repositoryMock.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(box);
_repositoryMock.Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
_repositoryMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
_mediatorMock.Setup(x => x.Send(It.IsAny<GetTransportBoxByIdRequest>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(new GetTransportBoxByIdResponse { TransportBox = new TransportBoxDto() });
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| FR-1 spec names the error `RequiredFieldMissing` but the handler actually returns `TransportBoxStateChangeError` (condition guard fires before callback) | Medium | Assert `ErrorCodes.TransportBoxStateChangeError`. Rename the test method to `Handle_OpenedToReserve_EmptyLocation_ReturnsStateChangeError` to match actual behaviour, or keep the spec name and note the discrepancy in a comment. Either is acceptable; accuracy is more important than spec name alignment. |
| `AssignLocationIfAny` throws `InvalidOperationException` when called with a non-null location on a non-`Opened` box — relevant if FR-3 request accidentally includes a `Location` | Low | FR-3 request must omit `Location` (leave null). The `if (location == null) return;` guard exits safely. |
| `CreateTestBox(TransportBoxState.Reserve)` sets `Code = "TEST-BOX-001"` which is not a valid B+3-digit code — irrelevant for these tests since no code validation runs on the Reserve→Received path | Low | No mitigation needed; domain validation is not triggered by these transitions. |
| `HandleReceived` is shared across `InTransit`, `Reserve`, `Quarantine` — FR-3 only proves aggregation on Reserve; the aggregation logic itself is already covered by the InTransit tests | Informational | FR-3's value is confirming the `Reserve → Received` dispatch path reaches `HandleReceived`, not re-testing aggregation logic. The existing duplicate-product test on InTransit already covers the aggregation branch thoroughly. |

## Specification Amendments

The spec names FR-1 `Handle_OpenedToReserve_EmptyLocation_ReturnsRequiredFieldMissing`. Based on handler code inspection, the actual returned `ErrorCode` is `TransportBoxStateChangeError` (from the transition condition guard), not `RequiredFieldMissing` (which is returned by `HandleOpenToReserve` — a path never reached when Location is null, because the condition fires first). The test method name and assertion should reflect `TransportBoxStateChangeError`.

## Prerequisites

None. All dependencies (mocks, helpers, using directives) are already present in the test class.
