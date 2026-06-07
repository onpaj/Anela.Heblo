## Module
ShoptetOrders

## Finding
`BlockOrderProcessingHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderProcessingHandler.cs`, lines 28–59) executes three Shoptet API calls in sequence inside a single `try/catch`:

```
1. GetOrderStatusIdAsync   — state guard
2. UpdateStatusAsync       — changes order to BlockedStatusId  ← point of no return
3. GetEshopRemarkAsync     ─┐
4. UpdateEshopRemarkAsync  ─┘ appends the block reason note
```

If step 3 or 4 throws (network error, Shoptet 5xx, etc.), the `catch` block logs and returns `ErrorCodes.InternalServerError`. At that point the order's status **is already changed** to the blocked state, but:

- The note recording why it was blocked was never written.
- The caller receives an error, not a success.
- A retry is impossible: the next call will fail at step 1 with `ShoptetOrderInvalidSourceState` because the order is no longer in `AllowedBlockSourceStateIds`.

The `BlockOrderProcessingHandlerTests` verify the exception-on-status-update path (test `Handle_ShoptetApiThrowsOnStatusUpdate_ReturnsInternalServerError`) but there is no test for the case where the status update succeeds and the note update fails.

## Why it matters
The two operations are not atomic (Shoptet provides no transaction API), but the current error handling makes them appear atomic to the caller: any failure returns the same `InternalServerError` regardless of how far execution progressed. The real failure mode is a **blocked order with no recorded reason**, and the operator has no reliable way to know whether a retry is needed or why it was blocked.

## Suggested fix
Separate the note update from the status update at the handler level. The smallest correct fix is to treat the note update as a best-effort operation — log a warning if it fails but still return success, since the primary goal (blocking the order) succeeded:

```csharp
await _eshopOrderClient.UpdateStatusAsync(request.OrderCode, _settings.Value.BlockedStatusId, cancellationToken);

// Best-effort: append block reason to internal remark. Failure is logged but
// does not roll back the status change, which cannot be undone transactionally.
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

Add a test: status update succeeds, note update throws → response is `Success`, warning is logged.

---
_Filed by daily arch-review routine on 2026-06-05._