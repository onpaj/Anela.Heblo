# Architecture Review: BlockOrderProcessing Note Update Resilience

## Skip Design: true

## Architectural Fit Assessment

The proposed change stays entirely inside one MediatR handler in the existing Vertical Slice (`Features/ShoptetOrders/UseCases/BlockOrderProcessing/`). No new types, no new interfaces, no DI registrations, no contract changes — the public `BlockOrderProcessingRequest → BlockOrderProcessingResponse` shape is preserved.

The change aligns with three patterns already in this codebase:

- `IEshopOrderClient` is the single Shoptet boundary; the handler keeps depending only on this interface (no leakage of Shoptet specifics into the `try/catch` shape).
- `BaseResponse` / `ErrorCodes` envelope is unchanged. Existing callers that branch on `Success` keep working — *more* correctly, in fact, because a previously-misreported partial-success now returns success.
- The structured-logging pattern (`_loggerMock.Verify(x => x.Log(LogLevel.Warning, ...))`) matches what `UpdatePurchaseOrderStatusHandlerTests` already exercises — the test infrastructure is proven.

The only architectural decision worth recording is the **error-handling scope**: two `try/catch` blocks at different granularities inside one handler. That is unusual relative to the rest of this module (most handlers use a single outer `try/catch`), but it is the correct shape here because the two operations have genuinely different failure semantics — and the spec's rationale (Shoptet has no transaction API, status change is the point of commit) makes this explicit and reviewable.

## Proposed Architecture

### Component Overview

```
BlockOrderProcessingHandler.Handle(request, ct)
  │
  ├── [State guard — outside committed-state]
  │   GetOrderStatusIdAsync ────► not in AllowedBlockSourceStateIds?
  │                               └► return ShoptetOrderInvalidSourceState
  │
  ├── try { [Primary operation — point of commit] }
  │   UpdateStatusAsync(BlockedStatusId)
  │   catch (Exception ex):
  │     LogError + return InternalServerError
  │
  ├── try { [Best-effort secondary] }
  │   GetEshopRemarkAsync
  │   compose updatedRemark (existing logic)
  │   UpdateEshopRemarkAsync
  │   catch (Exception ex):
  │     LogWarning(ex, "Order {OrderCode} was blocked but the note could not be appended", request.OrderCode)
  │     ── fall through ──
  │
  └── return new BlockOrderProcessingResponse()    // success
```

No collaborators change; the architecture is purely the control-flow restructure inside `Handle`.

### Key Design Decisions

#### Decision 1: Scope of the error boundaries

**Options considered:**
- (A) Keep one outer `try/catch` and inspect a state flag to decide whether to return success or error. Rejected — couples failure handling to mutable bookkeeping and obscures the commit point.
- (B) Split into two methods (`BlockOrderAsync`, `TryAppendNoteAsync`). Rejected — overkill for ~15 lines; the linearity of the handler is part of the readability.
- (C) **Two scoped `try/catch` blocks inline:** one wrapping `UpdateStatusAsync` (returns error on throw), one wrapping the remark read-modify-write (logs warning and falls through). State guard sits outside both.

**Chosen approach:** (C).

**Rationale:** Each `try/catch` block visibly documents *what kind of failure it tolerates*. The two failure modes are genuinely different (one short-circuits, one degrades). Inline structure stays under 50 lines and reads top-to-bottom in commit order.

#### Decision 2: State guard placement relative to error handling

**Options considered:**
- (A) Leave the state guard inside the status-update `try/catch`. Rejected — a throw from `GetOrderStatusIdAsync` would be reported as `InternalServerError`, which is fine, *but* a successful "wrong state" path returning `ShoptetOrderInvalidSourceState` shares the same `try` block as the commit operation. Reviewable but noisy.
- (B) **Move the state guard outside any `try/catch`. Wrap only `UpdateStatusAsync` in the first `try/catch`.**

**Chosen approach:** (B), matching the spec.

**Rationale:** `GetOrderStatusIdAsync` exceptions still bubble naturally — but they need a `try/catch` of their own to convert to `InternalServerError` (see *Specification Amendments* below). Keeping the state-validation result and the commit failure in separate scopes preserves the spec's stated invariant: *the state guard short-circuits before any mutation*.

#### Decision 3: Catch `Exception` (broad) vs. typed exceptions

**Options considered:**
- (A) Catch only `HttpRequestException` / `TaskCanceledException`. Rejected — `IEshopOrderClient` does not document its exception contract; the Shoptet adapter may throw deserialization, timeout, or wrapper exceptions. A typed catch would silently let some "note failed" cases bubble as `InternalServerError`, reintroducing the very bug being fixed.
- (B) **Catch `Exception` and let `OperationCanceledException` rethrow naturally.**

**Chosen approach:** (B), with one nuance — see *Specification Amendments*.

**Rationale:** The whole point of the best-effort block is that *any* failure in steps 3–4 should not poison the response. The only exception worth letting through is `OperationCanceledException` from the caller's `CancellationToken`, because cancellation is not a Shoptet failure.

## Implementation Guidance

### Directory / Module Structure

No new files. Edits confined to:
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderProcessingHandler.cs`
- `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/BlockOrderProcessingHandlerTests.cs`

### Interfaces and Contracts

Unchanged. `IEshopOrderClient`, `BlockOrderProcessingRequest`, `BlockOrderProcessingResponse`, `ShoptetOrdersSettings`, `BaseResponse`, `ErrorCodes` — none touched.

### Data Flow

For an order in an allowed source state, with both Shoptet calls succeeding: identical to today.

For an order where status update succeeds but remark read or write fails:

1. Status guard passes.
2. `UpdateStatusAsync` commits in Shoptet — order is now blocked.
3. `GetEshopRemarkAsync` or `UpdateEshopRemarkAsync` throws.
4. `catch` block fires `_logger.LogWarning(ex, "Order {OrderCode} was blocked but the note could not be appended", request.OrderCode)`.
5. Control falls through to `return new BlockOrderProcessingResponse()`.
6. Caller sees `Success = true`. Reconciliation signal lives in the warning log.

For an order where status update itself fails: identical to today — `InternalServerError` returned, no remark calls attempted (already verified by `Handle_ShoptetApiThrowsOnStatusUpdate_ReturnsInternalServerError`).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Operator misses the warning log and an order stays blocked with no recorded reason | Medium | NFR-3 mandates structured `{OrderCode}` property. Operator must configure a log-aggregation alert on this message — call this out in the PR description and the operations runbook (if one exists). |
| Broad `catch (Exception)` swallows `OperationCanceledException`, making the handler ignore cancellation after the status change | Medium | Either rethrow `OperationCanceledException` from the catch block, or use `catch (Exception ex) when (ex is not OperationCanceledException)`. See *Specification Amendments*. |
| Future regression — someone re-merges the two catch blocks during a "cleanup" | Low | The new test `Handle_ShoptetApiThrowsOnUpdateEshopRemark_ReturnsSuccessAndLogsWarning` locks the behavior in. The `try/catch` boundary becomes load-bearing for that test. |
| `_logger.LogWarning(ex, ...)` interacts with a logging filter that drops Warning-level entries | Low | Out of scope — logging configuration is project-wide. Verify with whoever owns the log pipeline that Warning is retained. |

## Specification Amendments

**A-1: Cancellation should not be swallowed.** The spec's `catch (Exception ex)` block will catch `OperationCanceledException` thrown from a caller-cancelled `CancellationToken` and convert it to "success with warning log." That is incorrect — cancellation means the caller no longer wants the response, and the partial state in Shoptet is the same as if the cancellation had hit `UpdateStatusAsync` after-the-fact (we genuinely can't undo it). The fix is small but should be in the spec:

```csharp
catch (Exception ex) when (ex is not OperationCanceledException)
{
    _logger.LogWarning(ex, "Order {OrderCode} was blocked but the note could not be appended", request.OrderCode);
}
```

Update FR-1 acceptance criteria to read: *"…honoured for all calls; an `OperationCanceledException` from the remark step is not converted to success."*

**A-2: `UpdateStatusAsync` needs its own `try/catch` once the outer one is gone.** The spec describes "Step 2 … wrap in `try/catch`" but the *Target shape* code block in the spec does not show one — it starts with the bare `await _eshopOrderClient.UpdateStatusAsync(...)`. The implementer must either:
- Keep an outer `try/catch` covering steps 1 + 2 (state guard + status update), and add the inner best-effort `try/catch` for steps 3–4. **Recommended** because it preserves the existing behavior of the `Handle_ShoptetApiThrowsOnStatusFetch_ReturnsInternalServerError` test — `GetOrderStatusIdAsync` throws are also caught.
- Or use three blocks: bare state-fetch (rethrows), `try/catch` around status update, `try/catch` around remark.

Either is acceptable; the first is smaller. Update the spec's *Target shape* to make this explicit so it doesn't get implemented as the bare snippet shown.

**A-3: Test naming.** The two new tests proposed in FR-4 are well-scoped. Add `_loggerMock` verification in the existing two happy-path tests is **not** required, but the new tests should explicitly verify the warning was logged *with the exception attached* (the spec mandates "the exception is passed to the logger as the exception argument") — use Moq's `It.IsAny<Exception>()` slot, asserting it is not `null`:

```csharp
_loggerMock.Verify(x => x.Log(
    LogLevel.Warning,
    It.IsAny<EventId>(),
    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("ORDER-X")),
    It.Is<Exception>(e => e != null),
    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
    Times.Once);
```

This pattern is already used in `UpdatePurchaseOrderStatusHandlerTests:230` — reuse it for consistency.

**A-4: Existing `_loggerMock` field is unused.** `BlockOrderProcessingHandlerTests` declares `_loggerMock` but the existing tests construct handlers with `NullLogger<>.Instance` and never verify on `_loggerMock`. The two new tests should use `CreateHandler()` (which threads `_loggerMock`) — not construct a fresh handler with `NullLogger`. Worth a line in the spec to prevent the implementer from copying the older pattern.

## Prerequisites

None. No migrations, no config changes, no infrastructure, no Key Vault entries, no client regeneration. The implementation can begin immediately against `main`.