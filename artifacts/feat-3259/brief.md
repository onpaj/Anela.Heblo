## Module
ShoptetOrders

## Finding
`IEshopOrderClient` (`backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs`) defines 13 methods, three of which have **no production callers anywhere in `backend/src/`**:

| Method | Last reference | Status |
|---|---|---|
| `SetInternalNoteAsync` (line 7 area — see `SetInternalNoteAsync`) | `BlockOrderProcessingHandlerTests.cs:183` — a `Times.Never` verification confirming it should NOT be called | Dead; `UpdateEshopRemarkAsync` replaced its role |
| `GetRecentOrdersByEmailAsync` | Removed from `GetSmartsuppContactShoptetInfoHandler.cs:66` with comment "email fallback was removed" | Dead; TODO left behind |
| `GetOrderStatusNamesAsync` | Nowhere in `src/` | Dead; no feature uses it |

Four additional methods (`CreateOrderAsync`, `DeleteOrderAsync`, `ListByExternalCodePrefixAsync`, `GetRecentOrdersAsync`) are only called from integration test fixtures for test-environment setup and cleanup — not from any production handler.

## Why it matters
- Every mock of `IEshopOrderClient` in handler unit tests must be compatible with a 13-method interface; dead methods add noise and maintenance surface.
- Dead methods on a production Application-layer interface violate YAGNI and mislead readers about the feature's actual capabilities.
- `SetInternalNoteAsync` specifically causes confusion: it exists alongside `GetEshopRemarkAsync`/`UpdateEshopRemarkAsync`, and a test even asserts it is **never** called — a signal the method was superseded but not cleaned up.
- `GetRecentOrdersByEmailAsync` has a matching dead DTO `EshopOrderInfo` that exists solely to type its return value, compounding the dead surface area.

## Suggested fix
Remove the three dead methods from `IEshopOrderClient` and their implementations in `ShoptetOrderClient`:
```csharp
// Remove from IEshopOrderClient.cs:
Task SetInternalNoteAsync(string orderCode, string note, CancellationToken ct = default);
Task<List<EshopOrderInfo>> GetRecentOrdersByEmailAsync(string email, int count, CancellationToken ct = default);
Task<Dictionary<int, string>> GetOrderStatusNamesAsync(CancellationToken ct = default);
```
Remove `EshopOrderInfo.cs` if it has no remaining callers after `GetRecentOrdersByEmailAsync` is gone.

For the four test-only methods (`CreateOrderAsync`, `DeleteOrderAsync`, `ListByExternalCodePrefixAsync`, `GetRecentOrdersAsync`): consider moving them off the production interface and having integration tests inject the concrete `ShoptetOrderClient` directly (or via a separate `IShoptetOrderTestClient`). This is a lower-priority cleanup — the three dead methods above are the clear win.

---
_Filed by daily arch-review routine on 2026-06-21._
