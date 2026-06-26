## Module
ShoptetOrders

## Finding
`BlockOrderProcessingHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderProcessingHandler.cs`, lines 33–39) contains the module's only non-trivial branch: a guard that rejects blocking when the current Shoptet order status is not in `ShoptetOrdersSettings.AllowedBlockSourceStateIds`:

```csharp
if (!_settings.Value.AllowedBlockSourceStateIds.Contains(currentStatusId))
{
    return new BlockOrderProcessingResponse(
        ErrorCodes.ShoptetOrderInvalidSourceState, ...);
}
```

There are no tests for this handler in `backend/test/Anela.Heblo.Tests/Features/ShoptetOrders/` (directory does not exist). The existing test files (`ShoptetOrderClientTests.cs`, `ShoptetOrderClient_SetAdditionalFieldTests.cs`) cover only the adapter, not the handler.

## Why it matters
The source-state guard is safety-critical: it prevents operators from accidentally blocking orders that are already shipped, invoiced, or otherwise in a terminal state. A misconfigured `AllowedBlockSourceStateIds` (e.g., an empty array) would silently block no orders, or a wrong entry would allow blocking orders in the wrong state. Without unit tests, regressions in this logic are invisible until a live order is incorrectly blocked.

## Suggested fix
Add a handler unit test class at `backend/test/Anela.Heblo.Tests/Features/ShoptetOrders/BlockOrderProcessingHandlerTests.cs` covering:
1. Happy path: status in `AllowedBlockSourceStateIds` → status update and remark append called, `Success = true`.
2. Invalid source state: status not in allowed list → returns `ShoptetOrderInvalidSourceState`, no status update called.
3. Status API throws → returns `InternalServerError`, no remark call.
4. Remark append throws non-cancel exception → order is still blocked (success), only a warning is logged.

---
_Filed by daily arch-review routine on 2026-06-23._
