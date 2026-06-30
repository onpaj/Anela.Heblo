# Specification: IEshopOrderClient Dead-Method Cleanup (ShoptetOrders)

## Summary

Three methods on `IEshopOrderClient` — `SetInternalNoteAsync`, `GetRecentOrdersByEmailAsync`, and `GetOrderStatusNamesAsync` — have no callers in production source (`backend/src/`) and should be removed from the interface and its concrete implementation. The companion DTO `EshopOrderInfo`, whose sole purpose is typing the return value of `GetRecentOrdersByEmailAsync`, should be deleted at the same time. A lower-priority follow-up (out of scope for this task) will move the four test-only methods off the production interface.

## Background

`IEshopOrderClient` currently defines 13 methods. Three were superseded or abandoned without cleanup:

- `SetInternalNoteAsync` — posted to `POST /api/orders/{code}/history`. It was replaced by `UpdateEshopRemarkAsync` (PATCH to `/notes`) when the remark strategy changed. A unit test in `BlockOrderProcessingHandlerTests` asserts it is **never** called (`Times.Never`), confirming it was intentionally retired.
- `GetRecentOrdersByEmailAsync` — fetched recent orders filtered by email in-memory. Its only production call site was `GetSmartsuppContactShoptetInfoHandler`, which was subsequently replaced with a hard-coded empty list and a TODO comment (`// email fallback was removed`). Its return type, `EshopOrderInfo`, exists solely to type this method's result.
- `GetOrderStatusNamesAsync` — fetched `GET /api/eshop?include=orderStatuses`. No handler in `backend/src/` calls it.

Keeping dead members on a production Application-layer interface violates YAGNI, inflates mock setup in unit tests, and misleads readers about the feature's actual capabilities.

Four additional methods (`CreateOrderAsync`, `DeleteOrderAsync`, `ListByExternalCodePrefixAsync`, `GetRecentOrdersAsync`) are used exclusively in integration-test fixtures. They are production-interface members only because integration tests resolve `IEshopOrderClient` from the DI container. Decoupling these is the right long-term call but is lower-priority and explicitly out of scope here.

## Functional Requirements

### FR-1: Remove `SetInternalNoteAsync` from the interface and implementation

Delete the method declaration from `IEshopOrderClient` and its implementation body from `ShoptetOrderClient`.

**Acceptance criteria:**
- `IEshopOrderClient.cs` no longer declares `Task SetInternalNoteAsync(string orderCode, string note, CancellationToken ct = default)`.
- `ShoptetOrderClient.cs` no longer contains the corresponding implementation (lines 163-177 in the current file).
- The existing `Times.Never` verification in `BlockOrderProcessingHandlerTests.cs:183` is removed (it would not compile against the new interface).
- `dotnet build` passes with no errors.

### FR-2: Remove `GetRecentOrdersByEmailAsync` from the interface and implementation

Delete the method declaration from `IEshopOrderClient` and its implementation body from `ShoptetOrderClient`, including the private helper `MapToOrderInfo` if it becomes unused.

**Acceptance criteria:**
- `IEshopOrderClient.cs` no longer declares `Task<List<EshopOrderInfo>> GetRecentOrdersByEmailAsync(string email, int count, CancellationToken ct = default)`.
- `ShoptetOrderClient.cs` no longer contains the corresponding implementation (lines 203-215) or the `MapToOrderInfo` private helper (lines 301-310) if `MapToOrderInfo` has no remaining callers after this deletion.
- `dotnet build` passes with no errors.

### FR-3: Remove `GetOrderStatusNamesAsync` from the interface and implementation

Delete the method declaration from `IEshopOrderClient` and its implementation body from `ShoptetOrderClient`.

**Acceptance criteria:**
- `IEshopOrderClient.cs` no longer declares `Task<Dictionary<int, string>> GetOrderStatusNamesAsync(CancellationToken ct = default)`.
- `ShoptetOrderClient.cs` no longer contains the corresponding implementation (lines 217-226).
- Any model types introduced solely to support this method's deserialization (e.g. `ShoptetEshopResponse`, `ShoptetOrderStatus`) are removed if they have no other callers. If they are shared with other methods, leave them in place.
- `dotnet build` passes with no errors.

### FR-4: Delete `EshopOrderInfo.cs`

After `GetRecentOrdersByEmailAsync` is removed, `EshopOrderInfo` has no callers. Delete the file.

**Acceptance criteria:**
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/EshopOrderInfo.cs` no longer exists.
- A codebase-wide search for `EshopOrderInfo` returns zero results in `backend/src/` and `backend/test/`.
- `dotnet build` passes with no errors.

### FR-5: Ensure all existing unit tests compile and pass

Removing interface members may require updating `Mock<IEshopOrderClient>` setups in unit tests. No test behavior should change; only dead mock setups or dead verifications are removed.

**Acceptance criteria:**
- All projects under `backend/test/` build successfully.
- `dotnet test` (excluding integration tests that require live Shoptet credentials) passes with no regressions.
- The `Times.Never` verification for `SetInternalNoteAsync` in `BlockOrderProcessingHandlerTests.cs:183` is deleted (not replaced).

## Non-Functional Requirements

### NFR-1: Zero behavioral change

This is a pure dead-code removal. No production request path, handler, or API response must change as a result.

**Acceptance criteria:**
- No handler in `backend/src/` is modified except to remove an import that is no longer needed.
- The Shoptet adapter layer makes no new HTTP calls and loses no existing HTTP call paths that are exercised by production handlers.

### NFR-2: Clean build and format

Removal must not leave orphaned `using` directives or dangling XML doc references.

**Acceptance criteria:**
- `dotnet build` passes.
- `dotnet format` produces no changes (or only whitespace normalization that was already present).

## Data Model

No data model changes. `EshopOrderInfo` is a DTO defined entirely within the Application layer and is not persisted or mapped to any database entity.

## API / Interface Design

Post-cleanup `IEshopOrderClient` will expose exactly 10 methods:

| Method | Status |
|---|---|
| `CreateOrderAsync` | Retained (test-only, out-of-scope for this task) |
| `GetOrderStatusIdAsync` | Retained (active production caller) |
| `UpdateStatusAsync` | Retained (active production caller) |
| `GetEshopRemarkAsync` | Retained (active production caller) |
| `UpdateEshopRemarkAsync` | Retained (active production caller) |
| `DeleteOrderAsync` | Retained (test-only, out-of-scope for this task) |
| `GetRecentOrdersAsync` | Retained (test-only, out-of-scope for this task) |
| `ListByExternalCodePrefixAsync` | Retained (test-only, out-of-scope for this task) |
| `MarkAsPackedAsync` | Retained (active production caller) |
| `SetAdditionalFieldAsync` | Retained (active; defined on `IShoptetExpeditionOrderSource`, also implemented on `ShoptetOrderClient`) |

The three methods listed in FR-1 through FR-3 are removed. No new methods are added.

## Dependencies

- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs` — primary change target.
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs` — implementation to prune.
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/EshopOrderInfo.cs` — file to delete.
- `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/BlockOrderProcessingHandlerTests.cs` — must remove the `Times.Never` verification for `SetInternalNoteAsync`.
- Model types inside `Anela.Heblo.Adapters.ShoptetApi` that may become unused after `GetOrderStatusNamesAsync` is removed (e.g. `ShoptetEshopResponse`) — check callers before deleting.

## Out of Scope

- Moving `CreateOrderAsync`, `DeleteOrderAsync`, `ListByExternalCodePrefixAsync`, and `GetRecentOrdersAsync` off the production interface into a separate `IShoptetOrderTestClient`. This is the right long-term shape but is a separate, lower-priority task.
- The TODO in `GetSmartsuppContactShoptetInfoHandler` about restoring guid-based order lookup. Resolving that TODO is a feature addition, not part of this cleanup.
- Any changes to the Shoptet API documentation (`docs/integrations/shoptet-api.md`), beyond noting that `SetInternalNoteAsync` (`POST /api/orders/{code}/history`) and `GetOrderStatusNamesAsync` (`GET /api/eshop?include=orderStatuses`) are no longer used by the application.

## Open Questions

None.

## Status: COMPLETE
