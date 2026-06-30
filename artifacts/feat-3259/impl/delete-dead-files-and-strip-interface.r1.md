# Implementation: delete-dead-files-and-strip-interface

## What was implemented

Pure dead-code removal: three unused methods stripped from `IEshopOrderClient` and their implementations from `ShoptetOrderClient`, three now-orphaned model files deleted, and one obsolete `Times.Never` test assertion removed.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs` — removed `SetInternalNoteAsync`, `GetRecentOrdersByEmailAsync` (with XML doc), `GetOrderStatusNamesAsync` (with XML doc)
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs` — removed `SetInternalNoteAsync`, `GetRecentOrdersByEmailAsync` (with NOTE comment), `GetOrderStatusNamesAsync`, and `MapToOrderInfo` private helper
- `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/BlockOrderProcessingHandlerTests.cs` — removed 3-line `clientMock.Verify` block for `SetInternalNoteAsync`
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/EshopOrderInfo.cs` — deleted
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/ShoptetEshopResponse.cs` — deleted
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/UpdateNotesRequest.cs` — deleted

## Tests

- `dotnet build Anela.Heblo.sln` — 0 errors, 254 pre-existing warnings (unchanged)
- `dotnet format Anela.Heblo.sln --verify-no-changes` — exit 0, no formatting issues
- `dotnet test Anela.Heblo.sln` — 5155 passed, 62 failed (all failures are Docker/Testcontainers integration tests that require a running Docker daemon; pre-existing environment limitation, unrelated to this change)
- `BlockOrderProcessingHandlerTests` specifically: 10/10 passed

## How to verify

```bash
cd /home/user/worktrees/feature-3259-Arch-Review-Shoptetorders-Ieshoporderclient-Carrie
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~BlockOrderProcessingHandlerTests"
```

## Notes

- The build command in the task spec was `dotnet build backend/` but the solution file is at the repo root (`Anela.Heblo.sln`). Used the solution file for the build — this is the correct approach for this monorepo.
- The 62 Docker-based integration test failures are pre-existing (Docker is not running in this sandbox environment) and are unrelated to this change.
- No deviations from the spec: all four blocks were removed from `ShoptetOrderClient.cs`, all three files were deleted, and the interface was cleaned up correctly.

## PR Summary

Removes three dead methods (`SetInternalNoteAsync`, `GetRecentOrdersByEmailAsync`, `GetOrderStatusNamesAsync`) from `IEshopOrderClient` and their implementations in `ShoptetOrderClient`. Deletes three now-orphaned model types (`EshopOrderInfo`, `ShoptetEshopResponse`, `UpdateNotesRequest`/`CreateOrderRemarkRequest`) that existed solely to support these methods. Cleans up a stale `Times.Never` assertion in `BlockOrderProcessingHandlerTests`. Zero production behavior change.

### Changes

- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs`
- `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/BlockOrderProcessingHandlerTests.cs`
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/EshopOrderInfo.cs` (deleted)
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/ShoptetEshopResponse.cs` (deleted)
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/UpdateNotesRequest.cs` (deleted)

## Status
DONE
