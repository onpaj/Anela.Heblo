# Specification: BlockOrderProcessing Note Update Resilience

## Summary
Fix non-atomic failure handling in `BlockOrderProcessingHandler` so that a successful, irreversible order status change is not reported to the caller as a failure when the subsequent note append fails. Treat the eshop remark update as best-effort: log a warning on failure, but return success because the primary operation (blocking the order) already committed.

## Background
`BlockOrderProcessingHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderProcessingHandler.cs`, lines 28–59) executes four sequential Shoptet API calls inside a single `try/catch`:

1. `GetOrderStatusIdAsync` — state guard against `AllowedBlockSourceStateIds`.
2. `UpdateStatusAsync` — moves the order to `BlockedStatusId`. **Point of no return.**
3. `GetEshopRemarkAsync` — reads the current internal remark.
4. `UpdateEshopRemarkAsync` — writes the remark with the block reason appended.

Shoptet provides no transaction API, so steps 2 and 3–4 are not atomic. The current error handling, however, makes them *appear* atomic to the caller: any throw in steps 3–4 (network error, Shoptet 5xx, transient timeout) is caught and returned as `ErrorCodes.InternalServerError`. By that point the status change has already committed and:

- The block reason was never recorded.
- The caller sees a failure response.
- Retry is impossible — the next call short-circuits at step 1 with `ShoptetOrderInvalidSourceState` because the order is no longer in `AllowedBlockSourceStateIds`.

The real failure mode is **a blocked order with no recorded reason**, and the API response gives the operator no way to distinguish that from a clean failure where nothing happened.

The existing test `BlockOrderProcessingHandlerTests.Handle_ShoptetApiThrowsOnStatusUpdate_ReturnsInternalServerError` covers a throw on step 2, but there is no test for the partial-success path (step 2 succeeds, step 3 or 4 throws).

## Functional Requirements

### FR-1: Separate failure semantics for status update vs. note update
The handler must treat the status update and the remark update as independent operations with different failure handling. A failure in the remark update must not cause the response to misrepresent the (already committed) status change.

**Acceptance criteria:**
- If `UpdateStatusAsync` throws, the handler returns `ErrorCodes.InternalServerError` (current behavior preserved).
- If `UpdateStatusAsync` succeeds but `GetEshopRemarkAsync` or `UpdateEshopRemarkAsync` throws, the handler returns a successful `BlockOrderProcessingResponse`.
- The state guard (`GetOrderStatusIdAsync` → `ShoptetOrderInvalidSourceState`) continues to short-circuit before any mutation.
- `CancellationToken` is honoured for all calls.

### FR-2: Warning log on note update failure
When the remark update fails after a successful status update, the handler must emit a warning so an operator can reconcile manually.

**Acceptance criteria:**
- Log level is `Warning`.
- The exception is passed to the logger as the exception argument (not just stringified into the message).
- The order code is emitted as a structured log property (`{OrderCode}`), not only interpolated into the message text.
- The message communicates that the order was blocked but the note could not be appended.

### FR-3: Preserve existing note append behavior
The remark composition logic must remain unchanged from the current implementation.

**Acceptance criteria:**
- If the current remark is null or empty, `UpdateEshopRemarkAsync` is called with `request.Note`.
- If the current remark is non-empty, `UpdateEshopRemarkAsync` is called with `"{currentRemark}\n{request.Note}"`.

### FR-4: Test coverage for the partial-success path
Add tests to lock in the new behavior and prevent regression.

**Acceptance criteria:**
- New test: `Handle_ShoptetApiThrowsOnGetEshopRemark_ReturnsSuccessAndLogsWarning` — `GetEshopRemarkAsync` throws → response is success and a warning is logged carrying the order code and the exception.
- New test: `Handle_ShoptetApiThrowsOnUpdateEshopRemark_ReturnsSuccessAndLogsWarning` — `UpdateEshopRemarkAsync` throws → response is success and a warning is logged carrying the order code and the exception.
- Existing test `Handle_ShoptetApiThrowsOnStatusUpdate_ReturnsInternalServerError` continues to pass unchanged.
- Existing happy-path test (status + note both succeed) continues to pass unchanged.

## Non-Functional Requirements

### NFR-1: Performance
No change. Same Shoptet API call count and ordering on the happy path.

### NFR-2: Security
No change. No new inputs, persistence, or auth surface.

### NFR-3: Observability
Warning logs are the *only* signal that a partial-success occurred. They must be structured (order code as a property) so operators can filter and alert on them in the log aggregation system.

### NFR-4: Backward compatibility
The handler's public contract (`BlockOrderProcessingRequest` → `BlockOrderProcessingResponse`) is unchanged. Callers that treat `Success` as "order is blocked" remain correct — in fact, more correct, because the previously-misreported partial-success path now reports success.

## Data Model
No data model changes. Affected entities (unchanged):
- **Shoptet order** — identified by `OrderCode`. Has a status that transitions from a value in `AllowedBlockSourceStateIds` to `BlockedStatusId`. Has an `eshopRemark` free-text field to which the block reason is appended.

## API / Interface Design
No public API changes. Handler internal structure:

1. **State guard** — `GetOrderStatusIdAsync`. If not in `AllowedBlockSourceStateIds`, return `ShoptetOrderInvalidSourceState`. (Outside any `try/catch` that masks state validation as an internal error.)
2. **Status update** — `UpdateStatusAsync`. Wrap in `try/catch`; on exception, log and return `InternalServerError`.
3. **Best-effort note append** — `GetEshopRemarkAsync` + `UpdateEshopRemarkAsync` wrapped in their own `try/catch`; on exception, log a structured warning and fall through.
4. **Return** — `new BlockOrderProcessingResponse()` success.

Target shape (per the brief):

```csharp
await _eshopOrderClient.UpdateStatusAsync(request.OrderCode, _settings.Value.BlockedStatusId, cancellationToken);

try
{
    var currentRemark = await _eshopOrderClient.GetEshopRemarkAsync(request.OrderCode, cancellationToken);
    var updatedRemark = string.IsNullOrEmpty(currentRemark) ? request.Note : $"{currentRemark}\n{request.Note}";
    await _eshopOrderClient.UpdateEshopRemarkAsync(request.OrderCode, updatedRemark, cancellationToken);
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "Order {OrderCode} was blocked but the note could not be appended", request.OrderCode);
}

return new BlockOrderProcessingResponse();
```

## Dependencies
- `IEshopOrderClient` (Shoptet client) — no interface changes.
- `ILogger<BlockOrderProcessingHandler>` — already injected.
- `BlockOrderProcessingOptions` (`BlockedStatusId`, `AllowedBlockSourceStateIds`) — unchanged.
- xUnit + Moq (existing test infrastructure) — for the new tests.

## Out of Scope
- Automatic retry or queue-based reconciliation for failed note updates. The brief explicitly accepts manual reconciliation via logs.
- Persisting the block reason locally (e.g., to an audit table) before or after the Shoptet call. Possible future enhancement, not part of this fix.
- Reordering operations or moving the note update before the status change. The status change must remain the point of commit.
- Extending the response type to distinguish full success from partial success. The brief deliberately chooses to treat partial success as success.
- Changes to other handlers in the `ShoptetOrders` module (e.g., unblock flows). Out of scope even if they exhibit similar patterns.

## Open Questions
None.

## Status: COMPLETE