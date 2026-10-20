# Implementation: packaging-adapter

## What was implemented
Added `ShoptetOrdersPackedOrderStatusUpdaterAdapter`, the `ShoptetOrders`-owned adapter that
implements `Packaging.Contracts.IPackedOrderStatusUpdater` (added in the `packaging-contract`
task) by delegating to `IEshopOrderClient.MarkAsPackedAsync`. Registered the adapter in
`ShoptetOrdersModule` so DI resolution for `IPackedOrderStatusUpdater` is owned by the provider
module (`ShoptetOrders`), not the consumer (`Packaging`) — consistent with the
`IShipmentDeliveryChecker` / `ILeafletKnowledgeSource` cross-module contract pattern already used
elsewhere in this codebase.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/ShoptetOrdersPackedOrderStatusUpdaterAdapter.cs` — new internal sealed adapter class implementing `IPackedOrderStatusUpdater` by delegating to `IEshopOrderClient`.
- `backend/test/Anela.Heblo.Tests/Features/ShoptetOrders/Infrastructure/ShoptetOrdersPackedOrderStatusUpdaterAdapterTests.cs` — new test file with 2 tests: delegation with same arguments, and exception propagation.
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersModule.cs` — registered `IPackedOrderStatusUpdater` → `ShoptetOrdersPackedOrderStatusUpdaterAdapter` as `Transient` (mirrors `IEshopOrderClient`'s own lifetime), with a comment explaining the consumer-owns-contract / provider-owns-registration split.

## Tests
- `ShoptetOrdersPackedOrderStatusUpdaterAdapterTests.MarkAsPackedAsync_DelegatesToEshopOrderClient_WithSameArguments` — verifies the adapter forwards `orderCode` and `CancellationToken` unchanged to `IEshopOrderClient.MarkAsPackedAsync`.
- `ShoptetOrdersPackedOrderStatusUpdaterAdapterTests.MarkAsPackedAsync_PropagatesException_WhenEshopOrderClientThrows` — verifies an `HttpRequestException` thrown by the underlying client propagates unchanged through the adapter.

## How to verify
```
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShoptetOrdersPackedOrderStatusUpdaterAdapterTests"
```
Expected: build succeeds (0 errors, only pre-existing warnings in unrelated files); test run passes 2/2.

## Notes
Followed the task-context file's steps exactly — no deviations. The adapter does not yet have
any consumer wired to it; `Packaging`'s handlers (`ScanPackingOrderHandler`,
`CompletePackingOrderHandler`) still inject `IEshopOrderClient` directly and will be switched to
`IPackedOrderStatusUpdater` in the `packaging-handlers` task.

## PR Summary
Implemented the `ShoptetOrders`-side adapter that satisfies `Packaging`'s new
`IPackedOrderStatusUpdater` contract by delegating to the existing `IEshopOrderClient`, and
registered it in `ShoptetOrdersModule`. This is the second of the tasks decoupling `Packaging`
from directly depending on `ShoptetOrders.IEshopOrderClient`.

### Changes
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/ShoptetOrdersPackedOrderStatusUpdaterAdapter.cs` — new file, adapter implementation
- `backend/test/Anela.Heblo.Tests/Features/ShoptetOrders/Infrastructure/ShoptetOrdersPackedOrderStatusUpdaterAdapterTests.cs` — new file, 2 unit tests
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersModule.cs` — registered the adapter for `IPackedOrderStatusUpdater`

## Status
DONE
